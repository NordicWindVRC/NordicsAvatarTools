using System.Collections.Generic;
using System.Threading.Tasks;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Fades a tangent-space normal correction to none at the borders of its UV islands
    // (ATLAS_OUTPUT.md, "Normal maps"). Where two islands meet on the mesh, at a UV seam, each
    // side reads its own part of the texture in its own tangent directions, and the shading only
    // matches if both decode to exactly the same direction there. The texture's pixel size,
    // filtering, compression blocks and mip levels never allow that, so any correction left at a
    // seam shows as a hard edge, while the mesh's own vertex normals, shared by the split
    // vertices, are seamless. Fading to none at every island border makes both sides of a seam
    // show only the vertex normals, so they always agree; the correction stays across the middle
    // of the faces, where triangle distortion shows.
    //
    // Islands are the connected parts of the source's coverage. The fade is smooth (smoothstep)
    // over a width in final pixels, measured with a chamfer distance in pixels scaled per axis to
    // final pixels. Pixels outside the coverage are left as they are; the island growing that
    // follows fills them from the faded islands.
    public static class AtlasNormalSeams
    {
        public struct Result
        {
            // The share of the covered pixels whose correction was reduced.
            public float FadedShare;
        }

        /// <summary>Fades <paramref name="map"/> (tangent-space normals) toward flat within
        /// <paramref name="width"/> final pixels of each island's border; <paramref name="scale"/>
        /// is the map's pixels per final pixel, per axis. <paramref name="coverage"/>: R &gt; 0
        /// where the source's mesh uses the texture (null: the whole map is one island).</summary>
        public static Result Fade(NordicsTextureWrapper map, NordicsTextureWrapper coverage, Vector2 scale, float width)
        {
            int w = map.Width, h = map.Height;
            if (width <= 0f) return default;
            var labels = Islands(coverage, w, h);
            // Steps in final pixels.
            float sx = 1f / Mathf.Max(scale.x, 1e-4f), sy = 1f / Mathf.Max(scale.y, 1e-4f);
            float sd = Mathf.Sqrt(sx * sx + sy * sy);
            var distance = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (labels[i] == 0) continue;
                    bool border = x == 0 || y == 0 || x == w - 1 || y == h - 1 ||
                                  labels[i - 1] != labels[i] || labels[i + 1] != labels[i] ||
                                  labels[i - w] != labels[i] || labels[i + w] != labels[i];
                    distance[i] = border ? 0.5f * Mathf.Min(sx, sy) : float.MaxValue;
                }
            // Two-pass chamfer distance within each island.
            void Relax(int i, int j, float step)
            {
                if (labels[j] == labels[i] && distance[j] + step < distance[i]) distance[i] = distance[j] + step;
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (labels[i] == 0) continue;
                    if (x > 0) Relax(i, i - 1, sx);
                    if (y > 0)
                    {
                        Relax(i, i - w, sy);
                        if (x > 0) Relax(i, i - w - 1, sd);
                        if (x + 1 < w) Relax(i, i - w + 1, sd);
                    }
                }
            for (int y = h - 1; y >= 0; y--)
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = y * w + x;
                    if (labels[i] == 0) continue;
                    if (x + 1 < w) Relax(i, i + 1, sx);
                    if (y + 1 < h)
                    {
                        Relax(i, i + w, sy);
                        if (x + 1 < w) Relax(i, i + w + 1, sd);
                        if (x > 0) Relax(i, i + w - 1, sd);
                    }
                }
            var pixels = map.GetPixels();
            var faded = new int[h];
            var covered = new int[h];
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (labels[i] == 0) continue;
                    covered[y]++;
                    float keep = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance[i] / width));
                    if (keep >= 1f) continue;
                    faded[y]++;
                    var n = new Vector3(pixels[i].r, pixels[i].g, pixels[i].b);
                    n = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.forward;
                    n = Vector3.Lerp(Vector3.forward, n, keep).normalized;
                    pixels[i] = new Color(n.x, n.y, n.z, pixels[i].a);
                }
            });
            map.SetPixels(pixels);
            long count = 0, reduced = 0;
            for (int y = 0; y < h; y++) { count += covered[y]; reduced += faded[y]; }
            return count == 0 ? default : new Result { FadedShare = (float)reduced / count };
        }

        // Connected parts of the coverage (4-neighbors), numbered from 1; 0 where it's uncovered.
        private static int[] Islands(NordicsTextureWrapper coverage, int width, int height)
        {
            var labels = new int[width * height];
            if (coverage == null)
            {
                for (int i = 0; i < labels.Length; i++) labels[i] = 1;
                return labels;
            }
            Color[] mask = coverage.Width == width && coverage.Height == height ? coverage.GetPixels() : null;
            if (mask == null)
                using (var sized = coverage.Resize(width, height, sampling: TextureSamplingMode.Nearest))
                    mask = sized.GetPixels();
            int next = 0;
            var stack = new Stack<int>();
            for (int start = 0; start < labels.Length; start++)
            {
                if (labels[start] != 0 || mask[start].r <= 0f) continue;
                next++;
                labels[start] = next;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    int x = i % width, y = i / width;
                    void Visit(int j)
                    {
                        if (labels[j] != 0 || mask[j].r <= 0f) return;
                        labels[j] = next;
                        stack.Push(j);
                    }
                    if (x > 0) Visit(i - 1);
                    if (x + 1 < width) Visit(i + 1);
                    if (y > 0) Visit(i - width);
                    if (y + 1 < height) Visit(i + width);
                }
            }
            return labels;
        }
    }
}
