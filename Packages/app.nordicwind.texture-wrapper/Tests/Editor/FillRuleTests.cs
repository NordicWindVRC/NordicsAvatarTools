using NUnit.Framework;
using UnityEngine;
namespace NordicWind.TextureWrapper.Tests
{
    // PaintTriangle's fill rule: every sub-sample of a mesh belongs to exactly one triangle,
    // including samples exactly on shared edges and vertices, so nothing is painted twice.
    public class FillRuleTests
    {
        private static NordicsTextureWrapper Mask(int size) => new NordicsTextureWrapper(size, size,
            new TextureSettings { DataType = TextureDataType.Mask, SRGB = false, Mipmaps = false });
        private static TrianglePaintOptions Samples(int perAxis) => perAxis == 1
            ? new TrianglePaintOptions { Mode = TriangleRasterizationMode.Fast }
            : new TrianglePaintOptions { Mode = TriangleRasterizationMode.Quality, SamplesPerAxis = perAxis };
        private static float MaxValue(NordicsTextureWrapper w)
        {
            float max = 0f;
            for (int y = 0; y < w.Height; y++) for (int x = 0; x < w.Width; x++) max = Mathf.Max(max, w.GetPixel(x, y).r);
            return max;
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void Quad_SharedDiagonalThroughSamples_IsCoveredExactlyOnce(int samples)
        {
            // An 8×8 quad split along the anti-diagonal x + y = 8, which passes exactly through
            // sub-samples. Both windings of the second triangle must give the same result.
            foreach (bool reversed in new[] { false, true })
                using (var w = Mask(8))
                {
                    w.PaintTriangle(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), Color.white, Color.white, Color.white, Samples(samples));
                    if (reversed) w.PaintTriangle(new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 1), Color.white, Color.white, Color.white, Samples(samples));
                    else w.PaintTriangle(new Vector2(1, 1), new Vector2(0, 1), new Vector2(1, 0), Color.white, Color.white, Color.white, Samples(samples));
                    for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                        Assert.AreEqual(1f, w.GetPixel(x, y).r, 1e-6f, $"texel {x},{y}, reversed {reversed}");
                }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void Fan_AroundAVertexOnASample_IsCoveredExactlyOnce(int samples)
        {
            // Six triangles around a vertex exactly on a sub-sample (texel 4, offset 0.25 of 8 px).
            var center = new Vector2(4.25f / 8f, 4.25f / 8f);
            var ring = new Vector2[6];
            for (int k = 0; k < 6; k++)
                ring[k] = center + new Vector2(Mathf.Cos(k * Mathf.PI / 3f), Mathf.Sin(k * Mathf.PI / 3f)) * (3f / 8f);
            using (var w = Mask(8))
            {
                for (int k = 0; k < 6; k++)
                    w.PaintTriangle(center, ring[k], ring[(k + 1) % 6], Color.white, Color.white, Color.white, Samples(samples));
                Assert.That(MaxValue(w), Is.LessThanOrEqualTo(1f + 1e-6f));
                Assert.AreEqual(1f, w.GetPixel(4, 4).r, 1e-6f); // fully inside the fan
            }
        }

        [TestCase(2)]
        [TestCase(4)]
        public void Grid_IsNeverCoveredMoreThanOnce(int samples)
        {
            // A 24×24-cell grid in the middle of a 128 px texture, like the whole-mesh tests.
            using (var w = Mask(128))
            {
                const int grid = 24; const float u0 = .1f, u1 = .9f;
                Vector2 P(int i, int j) => new Vector2(u0 + i / (float)grid * (u1 - u0), u0 + j / (float)grid * (u1 - u0));
                for (int j = 0; j < grid; j++) for (int i = 0; i < grid; i++)
                {
                    w.PaintTriangle(P(i, j), P(i + 1, j), P(i, j + 1), Color.white, Color.white, Color.white, Samples(samples));
                    w.PaintTriangle(P(i + 1, j + 1), P(i, j + 1), P(i + 1, j), Color.white, Color.white, Color.white, Samples(samples));
                }
                Assert.That(MaxValue(w), Is.LessThanOrEqualTo(1f + 1e-6f));
                Assert.AreEqual(1f, w.GetPixel(64, 64).r, 1e-6f);
            }
        }
    }
}
