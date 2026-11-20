using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>The cells Auto Setup packed (ATLAS_OUTPUT.md, "Auto setup") into an atlas of
    /// Width × Height pixels, in hundredths of a pixel, so the regions are exact decimals.</summary>
    public sealed class AtlasRegionLayout
    {
        public int Width, Height;
        // One per input size, in its order, in hundredths of a pixel (AtlasRegionPacker.Steps).
        public RectInt[] Cells;
        // Each cell's size relative to its input size, in whole percent: 100 at full size, less
        // when the sources were scaled down to fit the largest atlas.
        public int[] Percents;
        // The scale they were all brought down to before the free space was handed out.
        public int Percent;
        public float Scale => Percent / 100f;

        /// <summary>A cell as a UV region. The regions tile the atlas; padding is applied inside
        /// them later, where the content is placed.</summary>
        public Rect Region(int index)
        {
            var cell = Cells[index];
            double width = (double)Width * AtlasRegionPacker.Steps, height = (double)Height * AtlasRegionPacker.Steps;
            return new Rect((float)(cell.x / width), (float)(cell.y / height), (float)(cell.width / width), (float)(cell.height / height));
        }
    }

    /// <summary>
    /// Packs rectangles into the smallest atlas of supported texture sizes that holds them at full
    /// size, up to a largest side; when none does, into the largest square atlas with every size
    /// scaled down alike, in whole percent, as little as fits. Then the free space goes to the
    /// scaled-down ones, the largest first, each growing toward its full size as far as everything
    /// still fits. Last, with `stretch`, every region stretches into the free space beside it, the
    /// largest first, so the atlas is used whole: no region loses detail, some gain it along one
    /// axis, but they no longer keep their textures' proportions. Sizes and
    /// positions are in hundredths of a pixel, so a 4096 source at 80% is exactly 0.8 of a 4096
    /// atlas. Packing is MaxRects with the best short side fit, largest first.
    /// </summary>
    public static class AtlasRegionPacker
    {
        // The units per pixel, and the steps of the scale (whole percent).
        public const int Steps = 100;

        public static AtlasRegionLayout Pack(IReadOnlyList<Vector2Int> sizes, int maxSide = 4096, bool stretch = true)
        {
            if (sizes == null) throw new ArgumentNullException(nameof(sizes));
            if (sizes.Count == 0) throw new ArgumentException("Nothing to pack.", nameof(sizes));
            var input = sizes.Select(s => new Vector2Int(Math.Max(1, s.x), Math.Max(1, s.y))).ToList();
            var sides = AtlasTextureSizes.All.Where(s => s <= maxSide).ToList();
            if (sides.Count == 0) sides.Add(maxSide);
            foreach (var (width, height) in Atlases(sides))
            {
                var cells = MaxRects(Scaled(input, Uniform(input.Count, Steps)), width * Steps, height * Steps);
                if (cells != null)
                    return new AtlasRegionLayout { Width = width, Height = height, Percent = Steps, Percents = Uniform(input.Count, Steps),
                        Cells = stretch ? Stretch(input, cells, width * Steps, height * Steps) : cells };
            }
            // The largest percent below full size that still packs, by bisection.
            int side = sides.Max();
            int low = 0, high = Steps - 1;
            RectInt[] best = null;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                var packed = MaxRects(Scaled(input, Uniform(input.Count, mid)), side * Steps, side * Steps);
                if (packed != null) { low = mid; best = packed; }
                else high = mid - 1;
            }
            if (best == null)
                throw new InvalidOperationException("Too many regions to fit an atlas of " + side + " × " + side + ".");
            var percents = Uniform(input.Count, low);
            best = Grow(input, percents, side * Steps, best);
            return new AtlasRegionLayout { Width = side, Height = side, Percent = low, Percents = percents,
                Cells = stretch ? Stretch(input, best, side * Steps, side * Steps) : best };
        }

        // Every cell, the largest source first, grows up, right, down and left to the nearest other
        // cell or the atlas edge; twice, since a cell that moved may have freed room for another.
        private static RectInt[] Stretch(List<Vector2Int> sizes, RectInt[] cells, int width, int height)
        {
            var result = (RectInt[])cells.Clone();
            var order = Enumerable.Range(0, sizes.Count)
                .OrderByDescending(i => (long)sizes[i].x * sizes[i].y).ThenBy(i => i).ToList();
            for (int pass = 0; pass < 2; pass++)
                foreach (int i in order)
                {
                    var c = result[i];
                    int x0 = c.x, y0 = c.y, x1 = c.xMax, y1 = c.yMax;
                    for (int j = 0, top = height; j <= result.Length; j++)
                    {
                        if (j == result.Length) { y1 = top; break; }
                        var o = result[j];
                        if (j != i && o.x < x1 && o.xMax > x0 && o.y >= y1) top = Math.Min(top, o.y);
                    }
                    for (int j = 0, right = width; j <= result.Length; j++)
                    {
                        if (j == result.Length) { x1 = right; break; }
                        var o = result[j];
                        if (j != i && o.y < y1 && o.yMax > y0 && o.x >= x1) right = Math.Min(right, o.x);
                    }
                    for (int j = 0, bottom = 0; j <= result.Length; j++)
                    {
                        if (j == result.Length) { y0 = bottom; break; }
                        var o = result[j];
                        if (j != i && o.x < x1 && o.xMax > x0 && o.yMax <= y0) bottom = Math.Max(bottom, o.yMax);
                    }
                    for (int j = 0, left = 0; j <= result.Length; j++)
                    {
                        if (j == result.Length) { x0 = left; break; }
                        var o = result[j];
                        if (j != i && o.y < y1 && o.yMax > y0 && o.xMax <= x0) left = Math.Max(left, o.xMax);
                    }
                    result[i] = new RectInt(x0, y0, x1 - x0, y1 - y0);
                }
            return result;
        }

        // The free space goes to the scaled-down sources, the largest first: each grows toward its
        // full size as far as everything still packs. A later pass may find room an earlier
        // growth opened. Returns the cells of the last packing that fit.
        private static RectInt[] Grow(List<Vector2Int> sizes, int[] percents, int side, RectInt[] cells)
        {
            var order = Enumerable.Range(0, sizes.Count)
                .OrderByDescending(i => (long)sizes[i].x * sizes[i].y).ThenBy(i => i).ToList();
            for (int pass = 0; pass < 3; pass++)
            {
                bool grew = false;
                foreach (int i in order)
                {
                    int low = percents[i], high = Steps;
                    RectInt[] grown = null;
                    while (low < high)
                    {
                        int mid = (low + high + 1) / 2;
                        var trial = (int[])percents.Clone();
                        trial[i] = mid;
                        var packed = MaxRects(Scaled(sizes, trial), side, side);
                        if (packed != null) { low = mid; grown = packed; }
                        else high = mid - 1;
                    }
                    if (grown == null) continue;
                    percents[i] = low;
                    cells = grown;
                    grew = true;
                }
                if (!grew) break;
            }
            return cells;
        }

        private static int[] Uniform(int count, int percent) => Enumerable.Repeat(percent, count).ToArray();

        // The smallest area first, then the most square, then wider before taller.
        private static IEnumerable<(int width, int height)> Atlases(List<int> sides) =>
            sides.SelectMany(w => sides.Select(h => (w, h)))
                .OrderBy(a => (long)a.w * a.h)
                .ThenBy(a => Math.Abs(Math.Log((double)a.w / a.h)))
                .ThenByDescending(a => a.w);

        // Each size at its percent of full size, in hundredths of a pixel.
        private static List<Vector2Int> Scaled(List<Vector2Int> sizes, int[] percents) =>
            sizes.Select((s, i) => new Vector2Int(s.x * percents[i], s.y * percents[i])).ToList();

        // Null when they don't all fit.
        private static RectInt[] MaxRects(IReadOnlyList<Vector2Int> sizes, int width, int height)
        {
            var free = new List<RectInt> { new RectInt(0, 0, width, height) };
            var cells = new RectInt[sizes.Count];
            var order = Enumerable.Range(0, sizes.Count)
                .OrderByDescending(i => Math.Max(sizes[i].x, sizes[i].y))
                .ThenByDescending(i => (long)sizes[i].x * sizes[i].y)
                .ThenBy(i => i);
            foreach (int i in order)
            {
                var size = sizes[i];
                if (size.x > width || size.y > height) return null;
                bool found = false;
                RectInt best = default;
                int bestShort = int.MaxValue, bestLong = int.MaxValue;
                foreach (var f in free)
                {
                    if (f.width < size.x || f.height < size.y) continue;
                    int dx = f.width - size.x, dy = f.height - size.y;
                    int shortSide = Math.Min(dx, dy), longSide = Math.Max(dx, dy);
                    bool better = !found || shortSide < bestShort || (shortSide == bestShort && (longSide < bestLong ||
                        (longSide == bestLong && (f.y < best.y || (f.y == best.y && f.x < best.x)))));
                    if (!better) continue;
                    found = true;
                    best = new RectInt(f.x, f.y, size.x, size.y);
                    bestShort = shortSide;
                    bestLong = longSide;
                }
                if (!found) return null;
                cells[i] = best;
                Split(free, best);
            }
            return cells;
        }

        // Every free rectangle the placed one overlaps is replaced by the up to four parts of it
        // around the placed one; then the ones inside another are dropped.
        private static void Split(List<RectInt> free, RectInt placed)
        {
            var next = new List<RectInt>();
            foreach (var f in free)
            {
                if (!(f.x < placed.xMax && f.xMax > placed.x && f.y < placed.yMax && f.yMax > placed.y))
                {
                    next.Add(f);
                    continue;
                }
                if (placed.x > f.x) next.Add(new RectInt(f.x, f.y, placed.x - f.x, f.height));
                if (placed.xMax < f.xMax) next.Add(new RectInt(placed.xMax, f.y, f.xMax - placed.xMax, f.height));
                if (placed.y > f.y) next.Add(new RectInt(f.x, f.y, f.width, placed.y - f.y));
                if (placed.yMax < f.yMax) next.Add(new RectInt(f.x, placed.yMax, f.width, f.yMax - placed.yMax));
            }
            free.Clear();
            for (int i = 0; i < next.Count; i++)
            {
                bool inside = false;
                for (int j = 0; j < next.Count && !inside; j++)
                    inside = i != j && Contains(next[j], next[i]) && (!Contains(next[i], next[j]) || j < i);
                if (!inside) free.Add(next[i]);
            }
        }

        private static bool Contains(RectInt outer, RectInt inner) =>
            inner.x >= outer.x && inner.y >= outer.y && inner.xMax <= outer.xMax && inner.yMax <= outer.yMax;
    }
}
