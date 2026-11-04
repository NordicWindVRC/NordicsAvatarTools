using System;
using NUnit.Framework;
using UnityEngine;
namespace NordicWind.TextureWrapper.Tests
{
    // Whole-mesh normal correction: a curved, finely divided original and a flat two-triangle
    // reduced mesh share the same UVs. Both are painted with PaintTriangle, the correction is
    // baked with the safety rules, and every texel is decoded with the reduced mesh's
    // interpolated frame, computed independently at the texel centre, and compared with the
    // original's interpolated normal.
    public class WholeMeshNormalBakeTests
    {
        private const int Size = 128, Grid = 24;
        private const float U0 = .1f, U1 = .9f;

        private struct Frame { public Vector3 N, T, B; }

        // A sheet curved in s (so the reduced mesh's corner normals differ) with bumps.
        // mirroredUVs: U runs the other way. mirroredGeometry: the copy mirrored in X, as the
        // other half of a symmetric model.
        private static Frame Surface(float s, float t, bool mirroredUVs, bool mirroredGeometry)
        {
            float pi3 = 3f * Mathf.PI;
            float dhs = (s - .5f) + .05f * pi3 * Mathf.Cos(pi3 * s) * Mathf.Sin(pi3 * t);
            float dht = .05f * pi3 * Mathf.Sin(pi3 * s) * Mathf.Cos(pi3 * t);
            Vector3 n = new Vector3(-dhs, -dht, 1f).normalized;
            Vector3 tangent = new Vector3(1f, 0f, dhs).normalized;
            tangent = (tangent - n * Vector3.Dot(tangent, n)).normalized;
            float handedness = 1f;
            // Flipping U flips the tangent and the handedness.
            if (mirroredUVs) { tangent = -tangent; handedness = -handedness; }
            // Mirroring the geometry mirrors the vectors and flips the handedness.
            if (mirroredGeometry) { n.x = -n.x; tangent.x = -tangent.x; handedness = -handedness; }
            // Per-vertex bitangent, interpolated like the other vertex values.
            return new Frame { N = n, T = tangent, B = Vector3.Cross(n, tangent) * handedness };
        }
        private static Vector2 UV(float s, float t, bool mirroredUVs) =>
            new Vector2(mirroredUVs ? U1 - s * (U1 - U0) : U0 + s * (U1 - U0), U0 + t * (U1 - U0));
        private static Color V(Vector3 v) => new Color(v.x, v.y, v.z, 1f);

        private sealed class Mesh
        {
            public int Grid; public bool MirroredUVs, MirroredGeometry, Target;
        }
        // Each grid cell is split into a lower-left and an upper-right triangle. A mirrored
        // copy reverses the vertex order, like a real mirrored mesh, so its faces still point
        // outward and its UV winding is reversed.
        // maps: normals, then optionally tangent, bitangent and frame normal.
        private static void Paint(Mesh mesh, NordicsTextureWrapper coverage, params NordicsTextureWrapper[] maps)
        {
            int grid = mesh.Grid;
            for (int j = 0; j < grid; j++) for (int i = 0; i < grid; i++)
            {
                float s0 = i / (float)grid, s1 = (i + 1) / (float)grid, t0 = j / (float)grid, t1 = (j + 1) / (float)grid;
                var a = new Vector2(s0, t0); var b = new Vector2(s1, t0); var c = new Vector2(s0, t1); var d = new Vector2(s1, t1);
                if (mesh.MirroredGeometry) { PaintTriangle(mesh, coverage, maps, a, c, b); PaintTriangle(mesh, coverage, maps, d, b, c); }
                else { PaintTriangle(mesh, coverage, maps, a, b, c); PaintTriangle(mesh, coverage, maps, d, c, b); }
            }
        }
        private static void PaintTriangle(Mesh mesh, NordicsTextureWrapper coverage, NordicsTextureWrapper[] maps, Vector2 a, Vector2 b, Vector2 c)
        {
            Frame fa = Surface(a.x, a.y, mesh.MirroredUVs, mesh.MirroredGeometry);
            Frame fb = Surface(b.x, b.y, mesh.MirroredUVs, mesh.MirroredGeometry);
            Frame fc = Surface(c.x, c.y, mesh.MirroredUVs, mesh.MirroredGeometry);
            Vector2 ua = UV(a.x, a.y, mesh.MirroredUVs), ub = UV(b.x, b.y, mesh.MirroredUVs), uc = UV(c.x, c.y, mesh.MirroredUVs);
            Color cover = NormalDifferenceOptions.CoverageColor(ua, ub, uc, mesh.Target);
            coverage.PaintTriangle(ua, ub, uc, cover, cover, cover);
            maps[0].PaintTriangle(ua, ub, uc, V(fa.N), V(fb.N), V(fc.N));
            if (maps.Length == 1) return;
            maps[1].PaintTriangle(ua, ub, uc, V(fa.T), V(fb.T), V(fc.T));
            maps[2].PaintTriangle(ua, ub, uc, V(fa.B), V(fb.B), V(fc.B));
            maps[3].PaintTriangle(ua, ub, uc, V(fa.N), V(fb.N), V(fc.N));
        }
        // The interpolated vertex frame of the grid triangle containing (s, t).
        private static Frame Interpolated(int grid, float s, float t, bool mirroredUVs, bool mirroredGeometry)
        {
            int i = Mathf.Min(grid - 1, (int)(s * grid)), j = Mathf.Min(grid - 1, (int)(t * grid));
            float fs = s * grid - i, ft = t * grid - j;
            Frame At(int di, int dj) => Surface((i + di) / (float)grid, (j + dj) / (float)grid, mirroredUVs, mirroredGeometry);
            Frame Mix(Frame p, float wp, Frame q, float wq, Frame r, float wr) => new Frame {
                N = p.N * wp + q.N * wq + r.N * wr, T = p.T * wp + q.T * wq + r.T * wr, B = p.B * wp + q.B * wq + r.B * wr };
            return fs + ft <= 1f
                ? Mix(At(0, 0), 1f - fs - ft, At(1, 0), fs, At(0, 1), ft)
                : Mix(At(1, 1), fs + ft - 1f, At(0, 1), 1f - fs, At(1, 0), 1f - ft);
        }
        // Angle in degrees, accurate for tiny angles (acos isn't).
        private static double Angle(Vector3 a, Vector3 b)
        {
            a.Normalize(); b.Normalize();
            return Math.Atan2(Vector3.Cross(a, b).magnitude, Vector3.Dot(a, b)) * 180.0 / Math.PI;
        }
        private static NordicsTextureWrapper Map(TextureDataType type = TextureDataType.NormalWorld) => new NordicsTextureWrapper(Size, Size,
            new TextureSettings { DataType = type, SRGB = false, Mipmaps = false });
        private static bool Inside(int x, int y, bool mirroredUVs, out float s, out float t)
        {
            float u = (x + .5f) / Size, v = (y + .5f) / Size;
            s = mirroredUVs ? (U1 - u) / (U1 - U0) : (u - U0) / (U1 - U0); t = (v - U0) / (U1 - U0);
            return s > 0f && s < 1f && t > 0f && t < 1f; // texel centre on the mesh
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReducedMesh_DecodesToTheOriginalShading(bool mirroredUVs)
        {
            using (var original = Map()) using (var target = Map())
            using (var tangent = Map()) using (var bitangent = Map()) using (var normal = Map())
            using (var coverage = Map(TextureDataType.Mask))
            {
                Paint(new Mesh { Grid = Grid, MirroredUVs = mirroredUVs }, coverage, original);
                Paint(new Mesh { Grid = 1, MirroredUVs = mirroredUVs, Target = true }, coverage, target, tangent, bitangent, normal);
                using (var correction = original.BakeNormalDifference(target,
                    new TangentBasisData { Tangent = tangent, Bitangent = bitangent, Normal = normal },
                    new NormalDifferenceOptions { Coverage = coverage, MaxAngle = 60f }, out var bakeStats))
                {
                    int checkedTexels = 0, negativeZ = 0;
                    double sum = 0, worst = 0, uncorrectedSum = 0;
                    for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                    {
                        if (!correction.IsPixelUsed(x, y)) continue;
                        Color ts = correction.GetPixel(x, y);
                        if (ts.b <= 0f) negativeZ++;
                        if (!Inside(x, y, mirroredUVs, out float s, out float t)) continue;
                        Frame reduced = Interpolated(1, s, t, mirroredUVs, false);
                        Vector3 expected = Interpolated(Grid, s, t, mirroredUVs, false).N;
                        double error = Angle(reduced.T * ts.r + reduced.B * ts.g + reduced.N * ts.b, expected);
                        sum += error; worst = Math.Max(worst, error);
                        uncorrectedSum += Angle(reduced.N, expected);
                        checkedTexels++;
                    }
                    string stats = $"checked {checkedTexels}, mean {sum / checkedTexels:F4}°, worst {worst:F4}°, " +
                                   $"uncorrected mean {uncorrectedSum / checkedTexels:F2}°, overlapping {bakeStats.Overlapping}, limited {bakeStats.Limited}";
                    Assert.That(checkedTexels, Is.GreaterThan(9000), stats);
                    Assert.AreEqual(0, negativeZ, stats);
                    // No overlaps and no oversized corrections here, so nothing is left out.
                    Assert.AreEqual(0, bakeStats.Overlapping, stats);
                    Assert.AreEqual(0, bakeStats.Limited, stats);
                    // The reduced mesh really is wrong without correction...
                    Assert.That(uncorrectedSum / checkedTexels, Is.GreaterThan(10.0), stats);
                    // ...and right with it. What's left comes from texels on triangle edges,
                    // which average two triangles over the texel's area.
                    Assert.That(sum / checkedTexels, Is.LessThan(.05), stats);
                    Assert.That(worst, Is.LessThan(.5), stats);
                }
            }
        }

        // Two mirrored halves of a symmetric model share the same UVs. One texel can't hold the
        // right correction for both, so the safe result is no correction: neither half may be
        // shaded worse than without it.
        [Test] public void MirroredHalvesSharingUVs_AreNeverShadedWorse()
        {
            using (var original = Map()) using (var target = Map())
            using (var tangent = Map()) using (var bitangent = Map()) using (var normal = Map())
            using (var coverage = Map(TextureDataType.Mask))
            {
                foreach (bool mirrored in new[] { false, true })
                {
                    Paint(new Mesh { Grid = Grid, MirroredGeometry = mirrored }, coverage, original);
                    Paint(new Mesh { Grid = 1, MirroredGeometry = mirrored, Target = true }, coverage, target, tangent, bitangent, normal);
                }
                using (var correction = original.BakeNormalDifference(target,
                    new TangentBasisData { Tangent = tangent, Bitangent = bitangent, Normal = normal },
                    new NormalDifferenceOptions { Coverage = coverage, MaxAngle = 60f }, out var bakeStats))
                {
                    int checkedTexels = 0; double worstExtra = 0;
                    for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                    {
                        if (!correction.IsPixelUsed(x, y) || !Inside(x, y, false, out float s, out float t)) continue;
                        Color ts = correction.GetPixel(x, y);
                        foreach (bool mirrored in new[] { false, true })
                        {
                            Frame reduced = Interpolated(1, s, t, false, mirrored);
                            Vector3 expected = Interpolated(Grid, s, t, false, mirrored).N;
                            double corrected = Angle(reduced.T * ts.r + reduced.B * ts.g + reduced.N * ts.b, expected);
                            double uncorrected = Angle(reduced.N, expected);
                            worstExtra = Math.Max(worstExtra, corrected - uncorrected);
                        }
                        checkedTexels++;
                    }
                    string stats = $"checked {checkedTexels}, baked {bakeStats.Baked}, overlapping {bakeStats.Overlapping}, " +
                                   $"worst extra error {worstExtra:F4}°";
                    Assert.That(checkedTexels, Is.GreaterThan(9000), stats);
                    Assert.That(worstExtra, Is.LessThan(.01), stats);
                    Assert.That(bakeStats.Overlapping, Is.GreaterThanOrEqualTo(bakeStats.Baked * 95 / 100), stats);
                }
            }
        }
    }
}
