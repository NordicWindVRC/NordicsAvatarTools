using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // The unchanged-geometry check: two original renderers with two submeshes each, all with the
    // same material, merged into one mesh with some triangles removed.
    public sealed class AtlasGeometryComparisonTests
    {
        // A 3×3 vertex grid (8 triangles) with varying normals, tangents and UVs.
        private static AtlasMeshSection Grid(Vector3 offset, Matrix4x4 localToWorld, int[] triangles)
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var tangents = new List<Vector4>(); var uv = new List<Vector2>();
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
            {
                vertices.Add(offset + new Vector3(x * .1f, y * .1f, (x * y) * .01f));
                normals.Add(new Vector3(x * .1f - .1f, y * .1f - .1f, 1f).normalized);
                tangents.Add(new Vector4(1f, 0f, x * .05f, 1f));
                uv.Add(new Vector2(x * .25f + offset.x, y * .25f));
            }
            return new AtlasMeshSection { Vertices = vertices.ToArray(), Normals = normals.ToArray(), Tangents = tangents.ToArray(),
                UV = uv.ToArray(), Triangles = triangles, LocalToWorld = localToWorld };
        }
        private static readonly int[] LowerHalf = { 0, 1, 3, 1, 4, 3, 1, 2, 4, 2, 5, 4 };
        private static readonly int[] UpperHalf = { 3, 4, 6, 4, 7, 6, 4, 5, 7, 5, 8, 7 };

        // Two renderers × two submeshes, in avatar-root space through different transforms.
        private static List<AtlasMeshSection> Originals(out Matrix4x4 second)
        {
            second = Matrix4x4.TRS(new Vector3(.3f, .1f, -.2f), Quaternion.Euler(10f, 35f, -5f), new Vector3(1f, 1f, 1f));
            var first = Matrix4x4.identity;
            return new List<AtlasMeshSection> {
                Grid(Vector3.zero, first, LowerHalf), Grid(Vector3.zero, first, UpperHalf),
                Grid(new Vector3(1f, 0f, 0f), second, LowerHalf), Grid(new Vector3(1f, 0f, 0f), second, UpperHalf) };
        }

        // Merges sections into one root-space mesh, as a mesh merge does: vertices transformed,
        // shuffled, and triangles re-indexed with their corners rotated.
        private static AtlasMeshSection Merge(IEnumerable<(AtlasMeshSection section, int[] triangles)> parts)
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var tangents = new List<Vector4>(); var uv = new List<Vector2>();
            var triangles = new List<int>();
            foreach (var (section, tri) in parts)
            {
                var m = section.LocalToWorld; var nm = m.inverse.transpose;
                int start = vertices.Count, count = section.Vertices.Length;
                // Reverse the vertex order to show indices don't matter.
                for (int i = count - 1; i >= 0; i--)
                {
                    vertices.Add(m.MultiplyPoint3x4(section.Vertices[i]));
                    normals.Add(nm.MultiplyVector(section.Normals[i]).normalized);
                    var t = m.MultiplyVector(section.Tangents[i]);
                    tangents.Add(new Vector4(t.x, t.y, t.z, section.Tangents[i].w));
                    uv.Add(section.UV[i]);
                }
                int Index(int i) => start + count - 1 - i;
                for (int i = 0; i + 2 < tri.Length; i += 3)
                {
                    triangles.Add(Index(tri[i + 1])); triangles.Add(Index(tri[i + 2])); triangles.Add(Index(tri[i]));
                }
            }
            return new AtlasMeshSection { Vertices = vertices.ToArray(), Normals = normals.ToArray(), Tangents = tangents.ToArray(),
                UV = uv.ToArray(), Triangles = triangles.ToArray(), LocalToWorld = Matrix4x4.identity };
        }

        [Test]
        public void RemovedSubmeshesAndTriangles_AreUnchanged()
        {
            var originals = Originals(out _);
            // One submesh removed entirely, one cut down to two triangles, two kept.
            var current = Merge(new[] {
                (originals[0], LowerHalf), (originals[1], UpperHalf.Take(6).ToArray()), (originals[3], UpperHalf) });
            Assert.IsTrue(AtlasGeometryComparison.SurvivingTrianglesUnchanged(originals, current));
        }

        [Test]
        public void TinyNoise_IsStillUnchanged()
        {
            var originals = Originals(out _);
            var current = Merge(new[] { (originals[0], LowerHalf) });
            for (int i = 0; i < current.Vertices.Length; i++) current.Vertices[i] += new Vector3(2e-7f, -2e-7f, 1e-7f);
            Assert.IsTrue(AtlasGeometryComparison.SurvivingTrianglesUnchanged(originals, current));
        }

        [Test]
        public void NewTriangleFromExistingVertices_IsChanged()
        {
            // A reducer that removed vertex 4 joins its neighbours into new, larger triangles.
            var originals = Originals(out _);
            var current = Merge(new[] { (originals[0], new[] { 0, 1, 3, 1, 5, 3, 1, 2, 5 }) });
            Assert.IsFalse(AtlasGeometryComparison.SurvivingTrianglesUnchanged(originals, current));
        }

        [Test]
        public void MovedVertex_IsChanged()
        {
            var originals = Originals(out _);
            var current = Merge(new[] { (originals[0], LowerHalf) });
            current.Vertices[3] += new Vector3(0f, 0f, .001f); // original vertex 5, moved 1 mm
            Assert.IsFalse(AtlasGeometryComparison.SurvivingTrianglesUnchanged(originals, current));
        }

        [Test]
        public void RecalculatedNormal_IsChanged()
        {
            var originals = Originals(out _);
            var current = Merge(new[] { (originals[0], LowerHalf) });
            // Original vertex 4 (indices are reversed), used by the lower half. Its normal is
            // (0, 0, 1), so tilt it by about 17°.
            current.Normals[4] = new Vector3(.3f, 0f, 1f).normalized;
            Assert.That(Vector3.Angle(current.Normals[4], originals[0].Normals[4]), Is.GreaterThan(10f));
            Assert.IsFalse(AtlasGeometryComparison.SurvivingTrianglesUnchanged(originals, current));
        }

        [Test]
        public void MissingDataOrNoOriginals_IsNotTreatedAsUnchanged()
        {
            var originals = Originals(out _);
            var current = Merge(new[] { (originals[0], LowerHalf) });
            Assert.IsFalse(AtlasGeometryComparison.SurvivingTrianglesUnchanged(new List<AtlasMeshSection>(), current));
            current.Tangents = null;
            Assert.IsFalse(AtlasGeometryComparison.SurvivingTrianglesUnchanged(originals, current));
        }
    }
}
