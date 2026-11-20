using System;
using NUnit.Framework;
using UnityEngine;
namespace NordicWind.TextureWrapper.Tests
{
    // BakeNormalDifference must give a tangent-space normal that a shader turns back into the
    // original world normal: normalize(T·x + B·y + N·z), with the frame as interpolated.
    public class NormalBakeTests
    {
        private const float MaxAngle = .1f; // degrees; float precision, far below 8-bit output steps

        private static Color V(Vector3 v) => new Color(v.x, v.y, v.z, 1f);
        private static NordicsTextureWrapper Map(int count) => new NordicsTextureWrapper(count, 1,
            new TextureSettings { DataType = TextureDataType.NormalWorld, SRGB = false, Mipmaps = false });
        private static Vector3 Unit(System.Random r)
        {
            while (true)
            {
                var v = new Vector3((float)(r.NextDouble() * 2 - 1), (float)(r.NextDouble() * 2 - 1), (float)(r.NextDouble() * 2 - 1));
                if (v.sqrMagnitude > .01f && v.sqrMagnitude <= 1f) return v.normalized;
            }
        }
        private static Vector3 Perpendicular(System.Random r, Vector3 n)
        {
            while (true)
            {
                var v = Unit(r); v -= n * Vector3.Dot(v, n);
                if (v.sqrMagnitude > .01f) return v.normalized;
            }
        }
        // A world normal within acos(minDot) of the frame's normal, like real shading.
        private static Vector3 FrontFacing(System.Random r, Vector3 n, float minDot)
        {
            while (true) { var v = Unit(r); if (Vector3.Dot(v, n.normalized) > minDot) return v; }
        }

        /// <summary>Bakes one pixel per case and checks each decodes back to its reference.</summary>
        private static void AssertRoundTrip(Vector3[] reference, Vector3[] t, Vector3[] b, Vector3[] n)
        {
            int count = reference.Length;
            using (var original = Map(count)) using (var target = Map(count))
            using (var tangent = Map(count)) using (var bitangent = Map(count)) using (var normal = Map(count))
            {
                for (int i = 0; i < count; i++)
                {
                    original.SetPixel(i, 0, V(reference[i])); target.SetPixel(i, 0, V(n[i]));
                    tangent.SetPixel(i, 0, V(t[i])); bitangent.SetPixel(i, 0, V(b[i])); normal.SetPixel(i, 0, V(n[i]));
                }
                using (var correction = original.BakeNormalDifference(target,
                    new TangentBasisData { Tangent = tangent, Bitangent = bitangent, Normal = normal }))
                {
                    for (int i = 0; i < count; i++)
                    {
                        Assert.IsTrue(correction.IsPixelUsed(i, 0), "case " + i);
                        Color ts = correction.GetPixel(i, 0);
                        Assert.That(ts.b, Is.GreaterThan(0f), "case " + i + ": tangent-space Z must face outward");
                        Vector3 decoded = (t[i] * ts.r + b[i] * ts.g + n[i] * ts.b).normalized;
                        Assert.That(Vector3.Angle(decoded, reference[i]), Is.LessThan(MaxAngle), "case " + i);
                    }
                }
            }
        }

        [Test] public void OrthonormalFrames_RoundTripInEveryOrientationAndHandedness()
        {
            var r = new System.Random(1234);
            const int count = 256;
            var reference = new Vector3[count]; var t = new Vector3[count]; var b = new Vector3[count]; var n = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                n[i] = Unit(r); t[i] = Perpendicular(r, n[i]);
                float handedness = i % 2 == 0 ? 1f : -1f; // mirrored UVs flip the bitangent
                b[i] = Vector3.Cross(n[i], t[i]) * handedness;
                reference[i] = FrontFacing(r, n[i], .1f);
            }
            AssertRoundTrip(reference, t, b, n);
        }

        // Between two vertices, the interpolated frame is neither unit length nor orthogonal.
        // Some shaders take the bitangent as cross(N, T) per pixel, others interpolate the
        // vertex bitangents; both must decode exactly with the frame they use.
        [TestCase(false)]
        [TestCase(true)]
        public void InterpolatedFrames_RoundTripExactlyAsShadersDecodeThem(bool interpolatedBitangent)
        {
            var r = new System.Random(interpolatedBitangent ? 77 : 78);
            const int count = 256;
            var reference = new Vector3[count]; var t = new Vector3[count]; var b = new Vector3[count]; var n = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                // Two vertex frames, with normals up to about 60° apart, like a strongly
                // curved triangle of a reduced mesh.
                Vector3 n0 = Unit(r), n1;
                do n1 = Unit(r); while (Vector3.Dot(n0, n1) < .5f);
                Vector3 t0 = Perpendicular(r, n0), t1 = Perpendicular(r, n1);
                if (Vector3.Dot(t0, t1) < 0f) t1 = -t1; // keep the UV direction consistent
                float h = i % 2 == 0 ? 1f : -1f;
                float w = (float)r.NextDouble();
                n[i] = Vector3.Lerp(n0, n1, w); t[i] = Vector3.Lerp(t0, t1, w);
                b[i] = interpolatedBitangent
                    ? Vector3.Lerp(Vector3.Cross(n0, t0) * h, Vector3.Cross(n1, t1) * h, w)
                    : Vector3.Cross(n[i], t[i]) * h;
                // Within 60° of the normal. Further out, a frame this tilted can need a
                // negative tangent-space Z, which a normal map can't store.
                reference[i] = FrontFacing(r, n[i], .5f);
            }
            AssertRoundTrip(reference, t, b, n);
        }

        // Safety rules: an identity frame (T = X, B = Y, N = Z), so the correction equals the
        // reference, and the rules' effect is easy to check.
        private static Vector3 Tilted(float degrees) =>
            new Vector3(Mathf.Sin(degrees * Mathf.Deg2Rad), 0f, Mathf.Cos(degrees * Mathf.Deg2Rad));
        private static NordicsTextureWrapper BakeIdentity(Vector3[] reference, Color[] coverage, NormalDifferenceOptions options,
            out NormalDifferenceStatistics statistics)
        {
            int count = reference.Length;
            using (var original = Map(count)) using (var target = Map(count))
            using (var tangent = Map(count)) using (var bitangent = Map(count)) using (var normal = Map(count))
            using (var cover = new NordicsTextureWrapper(count, 1, new TextureSettings { DataType = TextureDataType.Mask, SRGB = false, Mipmaps = false }))
            {
                for (int i = 0; i < count; i++)
                {
                    original.SetPixel(i, 0, V(reference[i])); target.SetPixel(i, 0, V(Vector3.forward));
                    tangent.SetPixel(i, 0, V(Vector3.right)); bitangent.SetPixel(i, 0, V(Vector3.up)); normal.SetPixel(i, 0, V(Vector3.forward));
                    if (coverage != null) cover.SetPixel(i, 0, coverage[i]);
                }
                if (options != null && coverage != null) options.Coverage = cover;
                return original.BakeNormalDifference(target,
                    new TangentBasisData { Tangent = tangent, Bitangent = bitangent, Normal = normal }, options, out statistics);
            }
        }
        private static void AssertFlat(Color c) { Assert.AreEqual(0f, c.r, 1e-4f); Assert.AreEqual(0f, c.g, 1e-4f); Assert.AreEqual(1f, c.b, 1e-4f); }
        private static float AngleFromFlat(Color c) => Vector3.Angle(Vector3.forward, new Vector3(c.r, c.g, c.b));

        [Test] public void OverlappingCoverage_GetsNoCorrection()
        {
            var reference = new Vector3[5];
            for (int i = 0; i < reference.Length; i++) reference[i] = Tilted(20f);
            var coverage = new[]
            {
                new Color(1f, 0f, 1f, 0f),    // covered once by each mesh
                new Color(1.1f, 0f, 1f, 0f),  // edge sub-samples counted twice: still one surface
                new Color(2f, 0f, 1f, 0f),    // two stacked reference copies
                new Color(.5f, .5f, 1f, 0f),  // mirrored reference halves, partly covering
                new Color(1f, 0f, .6f, .4f),  // mirrored target halves
            };
            using (var correction = BakeIdentity(reference, coverage, new NormalDifferenceOptions(), out var stats))
            {
                Assert.AreEqual(20f, AngleFromFlat(correction.GetPixel(0, 0)), .05f);
                Assert.AreEqual(20f, AngleFromFlat(correction.GetPixel(1, 0)), .05f);
                AssertFlat(correction.GetPixel(2, 0)); AssertFlat(correction.GetPixel(3, 0)); AssertFlat(correction.GetPixel(4, 0));
                Assert.AreEqual(5, stats.Baked); Assert.AreEqual(3, stats.Overlapping); Assert.AreEqual(0, stats.Limited);
            }
        }

        // A soft limit: kept up to half the limit, then compressed smoothly toward it, so a larger
        // raw correction never gives a smaller result (a cutoff leaves steps in the shading).
        [Test] public void LargeCorrections_AreCompressedSmoothlyTowardTheLimit()
        {
            var reference = new[] { Tilted(20f), Tilted(45f), Tilted(55f), Tilted(65f), Tilted(120f) };
            using (var correction = BakeIdentity(reference, null, new NormalDifferenceOptions { MaxAngle = 60f }, out var stats))
            {
                Assert.AreEqual(20f, AngleFromFlat(correction.GetPixel(0, 0)), .05f); // below half the limit: kept
                float previous = 20f;
                for (int i = 1; i < reference.Length; i++)
                {
                    Color c = correction.GetPixel(i, 0);
                    float angle = AngleFromFlat(c);
                    Assert.That(angle, Is.GreaterThan(previous), "case " + i + ": larger raw corrections give larger results");
                    Assert.That(angle, Is.LessThan(60f), "case " + i + ": never past the limit");
                    Assert.That(c.r, Is.GreaterThan(0f), "case " + i + ": the same direction");
                    previous = angle;
                }
                Assert.AreEqual(NordicsTextureWrapper.SoftLimit(45f, 60f), AngleFromFlat(correction.GetPixel(1, 0)), .05f);
                Assert.AreEqual(2, stats.Limited, "beyond 60°, including the one into the surface");
                Assert.AreEqual(previous, stats.LargestAngle, .05f);
            }
            Assert.AreEqual(30f, NordicsTextureWrapper.SoftLimit(30f, 60f), 1e-5f);
            Assert.AreEqual(30f + 30f * (float)Math.Tanh(.5), NordicsTextureWrapper.SoftLimit(45f, 60f), 1e-4f);
        }

        [Test] public void DefaultOptions_NeverStoreACorrectionIntoTheSurface()
        {
            var reference = new[] { Tilted(45f), Tilted(100f) };
            using (var safe = BakeIdentity(reference, null, new NormalDifferenceOptions(), out var stats))
            {
                Assert.AreEqual(45f, AngleFromFlat(safe.GetPixel(0, 0)), .05f);
                Assert.That(safe.GetPixel(1, 0).b, Is.GreaterThan(0f), "compressed below 90°");
                Assert.That(AngleFromFlat(safe.GetPixel(1, 0)), Is.GreaterThan(45f));
                Assert.AreEqual(1, stats.Limited);
            }
            // Without options, no rules apply.
            using (var raw = BakeIdentity(reference, null, null, out _))
                Assert.That(raw.GetPixel(1, 0).b, Is.LessThan(0f));
        }

        // The keep map fades the correction per texel; the statistics say how far the shading is
        // from the reference without and with the correction.
        [Test] public void TheKeepMap_FadesTheCorrectionPerTexel()
        {
            var reference = new[] { Tilted(20f), Tilted(20f), Tilted(20f) };
            using (var keep = new NordicsTextureWrapper(3, 1, new TextureSettings { DataType = TextureDataType.LinearData, SRGB = false, Mipmaps = false }))
            {
                keep.SetPixel(0, 0, Color.white); keep.SetPixel(1, 0, new Color(.5f, .5f, .5f, 1f)); keep.SetPixel(2, 0, Color.black);
                using (var correction = BakeIdentity(reference, null, new NormalDifferenceOptions { MaxAngle = 60f, Keep = keep }, out var stats))
                {
                    Assert.AreEqual(20f, AngleFromFlat(correction.GetPixel(0, 0)), .05f);
                    float half = AngleFromFlat(correction.GetPixel(1, 0));
                    Assert.AreEqual(10f, half, .1f, "halfway toward flat");
                    AssertFlat(correction.GetPixel(2, 0));
                    Assert.AreEqual(2, stats.Kept);
                    Assert.AreEqual(20f, stats.MeanAngleBefore, .05f, "the target's own normal is 20° off");
                    Assert.AreEqual((0f + (20f - half) + 20f) / 3f, stats.MeanAngleAfter, .1f);
                }
            }
        }

        // Shrinking a normal map: where the normals a texel covers disagree, AreaFlatten tilts it
        // back toward flat by how much (the length of their average); agreeing ones barely change.
        [Test] public void AreaFlatten_FlattensWhereTheNormalsItAveragesDisagree()
        {
            using (var map = new NordicsTextureWrapper(4, 1, new TextureSettings { DataType = TextureDataType.NormalTangent, SRGB = false, Mipmaps = false }))
            {
                Vector3 left = Tilted(-40f), right = Tilted(40f);
                map.SetPixel(0, 0, V(left)); map.SetPixel(1, 0, V(right)); map.SetPixel(2, 0, V(Tilted(10f))); map.SetPixel(3, 0, V(Tilted(70f)));
                using (var area = map.Resize(2, 1, sampling: TextureSamplingMode.Area))
                using (var flat = map.Resize(2, 1, sampling: TextureSamplingMode.AreaFlatten))
                {
                    Assert.AreEqual(0f, AngleFromFlat(flat.GetPixel(0, 0)), .5f, "opposite tilts average to flat");
                    float areaTilt = AngleFromFlat(area.GetPixel(1, 0)), flatTilt = AngleFromFlat(flat.GetPixel(1, 0));
                    Assert.AreEqual(40f, areaTilt, .5f, "Area keeps the average direction");
                    // 10° and 70° are 60° apart: their average is cos(30°) long.
                    Assert.AreEqual(40f * Mathf.Cos(30f * Mathf.Deg2Rad), flatTilt, 2f, "flattened by their agreement");
                }
            }
        }

        [Test] public void FlatFrame_IsSkippedWithoutInvalidValues()
        {
            using (var original = Map(1)) using (var target = Map(1))
            using (var tangent = Map(1)) using (var bitangent = Map(1)) using (var normal = Map(1))
            {
                original.SetPixel(0, 0, V(Vector3.up)); target.SetPixel(0, 0, V(Vector3.forward));
                // The tangent lies along the normal, so the frame has no volume.
                tangent.SetPixel(0, 0, V(Vector3.forward)); bitangent.SetPixel(0, 0, V(Vector3.up)); normal.SetPixel(0, 0, V(Vector3.forward));
                using (var correction = original.BakeNormalDifference(target,
                    new TangentBasisData { Tangent = tangent, Bitangent = bitangent, Normal = normal }))
                {
                    Assert.IsFalse(correction.IsPixelUsed(0, 0));
                    Color c = correction.GetPixel(0, 0);
                    Assert.IsFalse(float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b));
                }
            }
        }
    }
}
