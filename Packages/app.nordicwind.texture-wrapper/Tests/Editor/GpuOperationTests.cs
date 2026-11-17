using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NordicWind.TextureWrapper.Tests
{
    /// <summary>The operations with a GPU version, checked on both processors.</summary>
    public class GpuOperationTests
    {
        private static TextureSettings Data() => new TextureSettings { DataType = TextureDataType.LinearData, SRGB = false };

        // Triangles of every size and winding, some overlapping, some partly outside the texture.
        private static void RandomTriangles(int count, int seed, List<Vector2> uv, List<Color> color)
        {
            var random = new System.Random(seed);
            float R() => (float)random.NextDouble();
            for (int t = 0; t < count; t++)
            {
                var centre = new Vector2(R() * 1.1f - 0.05f, R() * 1.1f - 0.05f);
                float size = t % 10 == 0 ? 0.4f : 0.06f;
                for (int k = 0; k < 3; k++)
                {
                    uv.Add(centre + new Vector2((R() - 0.5f) * size, (R() - 0.5f) * size));
                    color.Add(new Color(R(), R() * 2f - 1f, R(), R()));
                }
            }
        }

        [Test]
        public void TheTiledBatchPaintsExactlyLikeOneTriangleAtATime()
        {
            var uv = new List<Vector2>();
            var color = new List<Color>();
            RandomTriangles(300, 1, uv, color);
            bool useGpu = TextureProcessingSettings.UseGpu;
            int parallel = TextureProcessingSettings.MinParallelPixels;
            try
            {
                TextureProcessingSettings.UseGpu = false;
                TextureProcessingSettings.MinParallelPixels = 0;
                var options = new TrianglePaintOptions { SamplesPerAxis = 3 };
                using (var single = new NordicsTextureWrapper(150, 90, Data()))
                using (var batch = new NordicsTextureWrapper(150, 90, Data()))
                {
                    single.Fill(new Color(0.25f, 0f, 0f, 0f), new Rect(0f, 0f, 0.5f, 1f));
                    batch.Fill(new Color(0.25f, 0f, 0f, 0f), new Rect(0f, 0f, 0.5f, 1f));
                    for (int i = 0; i < uv.Count; i += 3)
                        single.PaintTriangle(uv[i], uv[i + 1], uv[i + 2], color[i], color[i + 1], color[i + 2], options);
                    batch.PaintTriangles(uv, color, options);
                    for (int y = 0; y < 90; y++)
                        for (int x = 0; x < 150; x++)
                        {
                            Assert.AreEqual(single.IsPixelUsed(x, y), batch.IsPixelUsed(x, y), $"usage at ({x}, {y})");
                            Assert.AreEqual(single.GetPixel(x, y), batch.GetPixel(x, y), $"pixel at ({x}, {y})");
                        }
                }
            }
            finally
            {
                TextureProcessingSettings.UseGpu = useGpu;
                TextureProcessingSettings.MinParallelPixels = parallel;
            }
        }

        [Test]
        public void TheGpuPaintsLikeTheCpuApartFromSharedEdges()
        {
            var uv = new List<Vector2>();
            var color = new List<Color>();
            RandomTriangles(400, 2, uv, color);
            var options = new TrianglePaintOptions { SamplesPerAxis = 2 };
            Color[] reference = null;
            bool[] referenceUsed = null;
            BothProcessors.Run(processor =>
            {
                using (var map = new NordicsTextureWrapper(128, 96, Data()))
                {
                    map.Fill(new Color(0.5f, 0.5f, 0.5f, 0.5f), new Rect(0.5f, 0.5f, 0.5f, 0.5f));
                    map.PaintTriangles(uv, color, options);
                    var pixels = new Color[128 * 96];
                    var used = new bool[128 * 96];
                    for (int y = 0; y < 96; y++)
                        for (int x = 0; x < 128; x++)
                        {
                            pixels[y * 128 + x] = map.GetPixel(x, y);
                            used[y * 128 + x] = map.IsPixelUsed(x, y);
                        }
                    if (reference == null)
                    {
                        reference = pixels;
                        referenceUsed = used;
                        return;
                    }
                    // The GPU snaps corners to a sub-pixel grid and interpolates in its own
                    // precision, so values drift slightly everywhere, well under one 8-bit step.
                    // A sample on or very near an edge may also fall the other way, changing its
                    // pixel by that sample's share: only a few pixels may differ that much.
                    const float step = 1f / 255f;
                    int differing = 0, drifting = 0, usageDiffering = 0;
                    float largest = 0f;
                    Vector4 sum = Vector4.zero, referenceSum = Vector4.zero;
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        float difference = ((Vector4)(pixels[i] - reference[i])).magnitude;
                        largest = Mathf.Max(largest, difference);
                        if (difference > step) differing++;
                        else if (difference > 1e-4f) drifting++;
                        if (used[i] != referenceUsed[i]) usageDiffering++;
                        sum += (Vector4)pixels[i];
                        referenceSum += (Vector4)reference[i];
                    }
                    Assert.Less(differing, pixels.Length / 50,
                        $"{processor}: pixels that differ from the CPU by more than an 8-bit step ({drifting} more drift under it; the largest difference is {largest})");
                    Assert.Less(usageDiffering, pixels.Length / 100, $"{processor}: usage that differs from the CPU");
                    Assert.Less((sum - referenceSum).magnitude, 0.01f * referenceSum.magnitude, $"{processor}: the total painted");
                }
            });
        }

        [Test]
        public void TheGpuPaintsTheRightWayUp()
        {
            // A triangle in the lower left: the GPU must paint the same corner as the CPU.
            var uv = new List<Vector2> { new Vector2(0.05f, 0.05f), new Vector2(0.3f, 0.05f), new Vector2(0.05f, 0.3f) };
            var color = new List<Color> { Color.white, Color.white, Color.white };
            BothProcessors.Run(processor =>
            {
                // Without a working test draw, painting would quietly stay on the CPU.
                if (processor == "GPU") Assert.AreNotEqual(0f, TextureGpu.PaintFlip, "The GPU's test draw didn't land in the texture.");
                using (var map = new NordicsTextureWrapper(32, 32, Data()))
                {
                    map.PaintTriangles(uv, color, new TrianglePaintOptions { Mode = TriangleRasterizationMode.Fast });
                    Assert.IsTrue(map.IsPixelUsed(3, 3), processor);
                    Assert.IsFalse(map.IsPixelUsed(3, 28), processor);
                    Assert.IsFalse(map.IsPixelUsed(28, 3), processor);
                    Assert.AreEqual(1f, map.GetPixel(3, 3).r, 1e-5f, processor);
                }
            });
        }

        // The CPU always finds a nearest used pixel; the GPU's jump flooding rarely takes a
        // slightly farther one.
        [Test]
        public void DilationFillsEveryPixelFromANearestUsedPixel()
        {
            const int width = 64, height = 48;
            var random = new System.Random(3);
            var seeds = new List<Vector2Int>();
            for (int i = 0; i < 14; i++) seeds.Add(new Vector2Int(random.Next(width), random.Next(height)));
            BothProcessors.Run(processor =>
            {
                using (var map = new NordicsTextureWrapper(width, height, Data()))
                {
                    // Each used pixel's value is its own position, so a filled pixel tells where it came from.
                    foreach (var s in seeds) map.SetPixel(s.x, s.y, new Color(s.x, s.y, 0f, 1f));
                    map.Dilate();
                    int farther = 0;
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            Assert.IsTrue(map.IsPixelUsed(x, y), $"{processor}: ({x}, {y}) used");
                            var c = map.GetPixel(x, y);
                            var from = new Vector2(c.r, c.g);
                            float nearest = float.MaxValue;
                            foreach (var s in seeds) nearest = Mathf.Min(nearest, (s - new Vector2Int(x, y)).sqrMagnitude);
                            Assert.IsTrue(seeds.Contains(new Vector2Int((int)from.x, (int)from.y)), $"{processor}: ({x}, {y}) took a used pixel's value");
                            float taken = (from - new Vector2(x, y)).sqrMagnitude;
                            if (processor == "CPU")
                            {
                                Assert.AreEqual(nearest, taken, 1e-3f, $"{processor}: ({x}, {y}) took a nearest used pixel");
                                continue;
                            }
                            if (taken > nearest + 1e-3f) farther++;
                            Assert.Less(Mathf.Sqrt(taken) - Mathf.Sqrt(nearest), 2f, $"{processor}: ({x}, {y}) took a used pixel much farther than the nearest");
                        }
                    Assert.Less(farther, width * height / 100, $"{processor}: pixels that took a farther used pixel");
                }
            });
        }

        [Test]
        public void DilatedNormalMapsAreOpaque()
        {
            BothProcessors.Run(processor =>
            {
                using (var map = new NordicsTextureWrapper(40, 30, new TextureSettings { DataType = TextureDataType.NormalTangent, SRGB = false }))
                {
                    map.SetPixel(5, 5, new Color(0f, 0f, 1f, 0.25f));
                    map.SetPixel(30, 20, new Color(0.6f, 0f, 0.8f, 0.5f));
                    map.Dilate();
                    for (int y = 0; y < 30; y++)
                        for (int x = 0; x < 40; x++)
                        {
                            Assert.IsTrue(map.IsPixelUsed(x, y), processor);
                            Assert.AreEqual(1f, map.GetPixel(x, y).a, 1e-6f, $"{processor}: alpha at ({x}, {y})");
                        }
                    Assert.AreEqual(0.6f, map.GetPixel(39, 29).r, 1e-4f, processor);
                }
            });
        }
    }
}
