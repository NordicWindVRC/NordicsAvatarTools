using System;
using NUnit.Framework;
using UnityEngine;
namespace NordicWind.TextureWrapper.Tests
{
    // Reoriented normal mapping: the detail normal, drawn as if on a flat surface, is rotated
    // onto the surface the base normal describes. The reference below does exactly that, with
    // the rotation that takes straight up (0, 0, 1) to the base normal.
    public class NormalBlendTests
    {
        private const float MaxAngle = .01f; // degrees

        private static Color V(Vector3 v) => new Color(v.x, v.y, v.z, 1f);
        private static NordicsTextureWrapper Map(int count) => new NordicsTextureWrapper(count, 1,
            new TextureSettings { DataType = TextureDataType.NormalTangent, SRGB = false, Mipmaps = false, WrapMode = TextureWrapMode.Clamp });
        private static Vector3 Reference(Vector3 baseNormal, Vector3 detail) =>
            Quaternion.FromToRotation(Vector3.forward, baseNormal) * detail;
        // A tangent-space normal within maxDegrees of straight up.
        private static Vector3 Hemisphere(System.Random r, float maxDegrees)
        {
            while (true)
            {
                var v = new Vector3((float)(r.NextDouble() * 2 - 1), (float)(r.NextDouble() * 2 - 1), (float)r.NextDouble());
                if (v.sqrMagnitude < .01f || v.sqrMagnitude > 1f) continue;
                v.Normalize();
                if (Vector3.Angle(v, Vector3.forward) < maxDegrees) return v;
            }
        }
        private static Vector3[] Blend(Vector3[] baseNormals, Vector3[] details)
        {
            int count = baseNormals.Length;
            using (var dst = Map(count)) using (var src = Map(count))
            {
                for (int i = 0; i < count; i++) { dst.SetPixel(i, 0, V(baseNormals[i])); src.SetPixel(i, 0, V(details[i])); }
                dst.Blit(src, blend: TextureBlendMode.NormalRNM);
                var result = new Vector3[count];
                for (int i = 0; i < count; i++) { Color c = dst.GetPixel(i, 0); result[i] = new Vector3(c.r, c.g, c.b); }
                return result;
            }
        }

        [Test] public void Rnm_RotatesTheDetailOntoTheBase()
        {
            var r = new System.Random(2024);
            const int count = 256;
            var baseNormals = new Vector3[count]; var details = new Vector3[count];
            for (int i = 0; i < count; i++) { baseNormals[i] = Hemisphere(r, 80f); details[i] = Hemisphere(r, 80f); }
            Vector3[] blended = Blend(baseNormals, details);
            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(1f, blended[i].magnitude, 1e-4f, "case " + i);
                Assert.That(Vector3.Angle(blended[i], Reference(baseNormals[i], details[i])), Is.LessThan(MaxAngle), "case " + i);
            }
        }

        [Test] public void Rnm_FlatDetailKeepsTheBaseAndFlatBaseKeepsTheDetail()
        {
            var tilted = new Vector3(.3f, .2f, .9f).normalized;
            Vector3[] blended = Blend(new[] { tilted, Vector3.forward }, new[] { Vector3.forward, tilted });
            Assert.That(Vector3.Angle(blended[0], tilted), Is.LessThan(MaxAngle));
            Assert.That(Vector3.Angle(blended[1], tilted), Is.LessThan(MaxAngle));
        }

        [Test] public void Rnm_DetailKeepsItsAngleToTheBase()
        {
            // A rotation keeps angles: the result is as far from the base as the detail is from flat.
            var r = new System.Random(7);
            const int count = 64;
            var baseNormals = new Vector3[count]; var details = new Vector3[count];
            for (int i = 0; i < count; i++) { baseNormals[i] = Hemisphere(r, 70f); details[i] = Hemisphere(r, 70f); }
            Vector3[] blended = Blend(baseNormals, details);
            for (int i = 0; i < count; i++)
                Assert.AreEqual(Vector3.Angle(details[i], Vector3.forward), Vector3.Angle(blended[i], baseNormals[i]), .05f, "case " + i);
        }
    }
}
