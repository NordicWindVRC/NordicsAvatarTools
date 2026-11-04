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

        [Test] public void LargeCorrections_FadeOutAtTheAngleLimit()
        {
            var reference = new[] { Tilted(30f), Tilted(55f), Tilted(65f), Tilted(120f) };
            using (var correction = BakeIdentity(reference, null, new NormalDifferenceOptions { MaxAngle = 60f }, out var stats))
            {
                Assert.AreEqual(30f, AngleFromFlat(correction.GetPixel(0, 0)), .05f); // kept
                float faded = AngleFromFlat(correction.GetPixel(1, 0));              // halfway through the fade
                Assert.That(faded, Is.GreaterThan(1f).And.LessThan(54f));
                AssertFlat(correction.GetPixel(2, 0));                                 // beyond the limit
                AssertFlat(correction.GetPixel(3, 0));                                 // would point into the surface
                Assert.AreEqual(3, stats.Limited);
                Assert.AreEqual(30f, stats.LargestAngle, .05f);
            }
        }

        [Test] public void DefaultOptions_NeverStoreACorrectionIntoTheSurface()
        {
            var reference = new[] { Tilted(45f), Tilted(100f) };
            using (var safe = BakeIdentity(reference, null, new NormalDifferenceOptions(), out var stats))
            {
                Assert.AreEqual(45f, AngleFromFlat(safe.GetPixel(0, 0)), .05f);
                AssertFlat(safe.GetPixel(1, 0));
                Assert.AreEqual(1, stats.Limited);
            }
            // Without options, no rules apply.
            using (var raw = BakeIdentity(reference, null, null, out _))
                Assert.That(raw.GetPixel(1, 0).b, Is.LessThan(0f));
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
