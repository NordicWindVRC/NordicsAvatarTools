using System;
using System.Collections.Generic;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // End to end through AtlasSubmeshBaker.Bake: a curved, finely divided original and a flat
    // two-triangle current mesh with the same UVs. Every texel of the correction is decoded with
    // the current mesh's interpolated frame and compared with the original's shading.
    public sealed class AtlasSubmeshBakeTests
    {
        internal const int Size = 128, Grid = 24;
        internal const float U0 = .1f, U1 = .9f;

        internal struct Frame { public Vector3 N, T, B; }
        internal static Frame Surface(float s, float t)
        {
            float pi3 = 3f * Mathf.PI;
            float dhs = (s - .5f) + .05f * pi3 * Mathf.Cos(pi3 * s) * Mathf.Sin(pi3 * t);
            float dht = .05f * pi3 * Mathf.Sin(pi3 * s) * Mathf.Cos(pi3 * t);
            Vector3 n = new Vector3(-dhs, -dht, 1f).normalized;
            Vector3 tangent = new Vector3(1f, 0f, dhs).normalized;
            tangent = (tangent - n * Vector3.Dot(tangent, n)).normalized;
            return new Frame { N = n, T = tangent, B = Vector3.Cross(n, tangent) };
        }
        internal static float Height(float s, float t) =>
            .5f * (s - .5f) * (s - .5f) + .05f * Mathf.Sin(3f * Mathf.PI * s) * Mathf.Sin(3f * Mathf.PI * t);

        // A grid mesh: (grid + 1)² vertices, each cell split into a lower-left and upper-right triangle.
        internal static void GridData(int grid, out Vector3[] vertices, out Vector3[] normals, out Vector4[] tangents,
            out Vector2[] uv, out int[] triangles)
        {
            int row = grid + 1, count = row * row;
            vertices = new Vector3[count]; normals = new Vector3[count]; tangents = new Vector4[count]; uv = new Vector2[count];
            for (int j = 0; j <= grid; j++) for (int i = 0; i <= grid; i++)
            {
                float s = i / (float)grid, t = j / (float)grid; int k = j * row + i;
                var f = Surface(s, t);
                vertices[k] = new Vector3(s, t, Height(s, t)); normals[k] = f.N;
                tangents[k] = new Vector4(f.T.x, f.T.y, f.T.z, 1f);
                uv[k] = new Vector2(U0 + s * (U1 - U0), U0 + t * (U1 - U0));
            }
            var tri = new List<int>();
            for (int j = 0; j < grid; j++) for (int i = 0; i < grid; i++)
            {
                int a = j * row + i, b = a + 1, c = a + row, d = c + 1;
                tri.AddRange(new[] { a, b, c, d, c, b });
            }
            triangles = tri.ToArray();
        }
        internal static Frame Interpolated(int grid, float s, float t)
        {
            int i = Mathf.Min(grid - 1, (int)(s * grid)), j = Mathf.Min(grid - 1, (int)(t * grid));
            float fs = s * grid - i, ft = t * grid - j;
            Frame At(int di, int dj) => Surface((i + di) / (float)grid, (j + dj) / (float)grid);
            Frame Mix(Frame p, float wp, Frame q, float wq, Frame r, float wr) => new Frame {
                N = p.N * wp + q.N * wq + r.N * wr, T = p.T * wp + q.T * wq + r.T * wr, B = p.B * wp + q.B * wq + r.B * wr };
            return fs + ft <= 1f
                ? Mix(At(0, 0), 1f - fs - ft, At(1, 0), fs, At(0, 1), ft)
                : Mix(At(1, 1), fs + ft - 1f, At(0, 1), 1f - fs, At(1, 0), 1f - ft);
        }
        internal static double Angle(Vector3 a, Vector3 b)
        {
            a.Normalize(); b.Normalize();
            return Math.Atan2(Vector3.Cross(a, b).magnitude, Vector3.Dot(a, b)) * 180.0 / Math.PI;
        }

        [Test]
        public void Bake_ReducedMeshDecodesToTheOriginalShading()
        {
            var shader = Shader.Find("Standard");
            if (shader == null) Assert.Ignore("Standard shader is not available in this project.");
            var material = new Material(shader);
            var root = new GameObject("Atlaser bake test");
            var mesh = new Mesh();
            try
            {
                GridData(Grid, out var ov, out var on, out var ot, out var ouv, out var otri);
                var originals = new List<AtlasMeshSection> { new AtlasMeshSection {
                    Material = material, RendererPath = "Original", Vertices = ov, Normals = on, Tangents = ot,
                    UV = ouv, Triangles = otri, LocalToWorld = Matrix4x4.identity, RendererToAvatarRoot = Matrix4x4.identity } };

                GridData(1, out var cv, out var cn, out var ct, out var cuv, out var ctri);
                mesh.vertices = cv; mesh.normals = cn; mesh.tangents = ct; mesh.uv = cuv; mesh.triangles = ctri;
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh; renderer.sharedMaterials = new[] { material };

                var diagnostics = new List<string>();
                using (var result = AtlasSubmeshBaker.Bake(material, Size, Size, originals,
                    new List<SkinnedMeshRenderer> { renderer }, root.transform, diagnostics))
                {
                    var bake = result.Submeshes[0];
                    string log = string.Join("\n", diagnostics);
                    Assert.IsFalse(bake.CorrectionIsFlat, log);
                    Assert.IsFalse(diagnostics.Exists(d => d.Contains("[Warning]")), log);

                    int checkedTexels = 0; double sum = 0, worst = 0, uncorrected = 0;
                    for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                    {
                        float s = ((x + .5f) / Size - U0) / (U1 - U0), t = ((y + .5f) / Size - U0) / (U1 - U0);
                        bool inside = s > 0f && s < 1f && t > 0f && t < 1f;
                        // The usage mask covers the mesh's interior.
                        if (s > .02f && s < .98f && t > .02f && t < .98f) Assert.IsTrue(bake.CurrentMask.IsPixelUsed(x, y), "mask " + x + "," + y);
                        if (!inside) continue;
                        Color ts = bake.TangentNormalCorrection.GetPixel(x, y);
                        Frame reduced = Interpolated(1, s, t);
                        Vector3 expected = Interpolated(Grid, s, t).N;
                        double error = Angle(reduced.T * ts.r + reduced.B * ts.g + reduced.N * ts.b, expected);
                        sum += error; worst = Math.Max(worst, error); uncorrected += Angle(reduced.N, expected);
                        checkedTexels++;
                    }
                    string stats = $"checked {checkedTexels}, mean {sum / checkedTexels:F4}°, worst {worst:F4}°, " +
                                   $"uncorrected mean {uncorrected / checkedTexels:F2}°\n{log}";
                    Assert.That(checkedTexels, Is.GreaterThan(9000), stats);
                    Assert.That(uncorrected / checkedTexels, Is.GreaterThan(10.0), stats);
                    // What's left comes from texels on triangle edges, which average two triangles
                    // over the texel's area (simulated: about 0.02° on average, 0.22° at worst).
                    Assert.That(sum / checkedTexels, Is.LessThan(.05), stats);
                    Assert.That(worst, Is.LessThan(.5), stats);
                }
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); }
        }

        // The same original with a flat copy behind it (below its lowest point), over the same UVs, like a body part
        // hidden inside the mesh. With the depth test, the copy is left out and the bake matches
        // the one without it; without it, the overlap leaves the texels uncorrected.
        [TestCase(true)]
        [TestCase(false)]
        public void Bake_HiddenGeometryIsLeftOutWithTheDepthTest(bool excludeHidden)
        {
            var shader = Shader.Find("Standard");
            if (shader == null) Assert.Ignore("Standard shader is not available in this project.");
            var material = new Material(shader);
            var root = new GameObject("Atlaser hidden geometry test");
            var mesh = new Mesh();
            try
            {
                GridData(Grid, out var ov, out var on, out var ot, out var ouv, out var otri);
                var iv = new Vector3[ov.Length];
                var inn = new Vector3[ov.Length];
                for (int i = 0; i < ov.Length; i++) { iv[i] = new Vector3(ov[i].x, ov[i].y, -.2f); inn[i] = Vector3.forward; }
                var originals = new List<AtlasMeshSection> {
                    new AtlasMeshSection { Material = material, RendererPath = "Original", Vertices = ov, Normals = on, Tangents = ot,
                        UV = ouv, Triangles = otri, LocalToWorld = Matrix4x4.identity, RendererToAvatarRoot = Matrix4x4.identity },
                    new AtlasMeshSection { Material = material, RendererPath = "Hidden", Vertices = iv, Normals = inn, Tangents = ot,
                        UV = ouv, Triangles = otri, LocalToWorld = Matrix4x4.identity, RendererToAvatarRoot = Matrix4x4.identity } };

                GridData(1, out var cv, out var cn, out var ct, out var cuv, out var ctri);
                mesh.vertices = cv; mesh.normals = cn; mesh.tangents = ct; mesh.uv = cuv; mesh.triangles = ctri;
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh; renderer.sharedMaterials = new[] { material };

                var diagnostics = new List<string>();
                using (var result = AtlasSubmeshBaker.Bake(material, Size, Size, originals,
                    new List<SkinnedMeshRenderer> { renderer }, root.transform, diagnostics, excludeHidden: excludeHidden))
                {
                    var bake = result.Submeshes[0];
                    string log = string.Join("\n", diagnostics);
                    int checkedTexels = 0; double sum = 0;
                    for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                    {
                        float s = ((x + .5f) / Size - U0) / (U1 - U0), t = ((y + .5f) / Size - U0) / (U1 - U0);
                        if (s <= 0f || s >= 1f || t <= 0f || t >= 1f) continue;
                        Color ts = bake.TangentNormalCorrection.GetPixel(x, y);
                        Frame reduced = Interpolated(1, s, t);
                        sum += Angle(reduced.T * ts.r + reduced.B * ts.g + reduced.N * ts.b, Interpolated(Grid, s, t).N);
                        checkedTexels++;
                    }
                    string stats = $"depth test {excludeHidden}: checked {checkedTexels}, mean {sum / checkedTexels:F4}°\n{log}";
                    if (excludeHidden) Assert.That(sum / checkedTexels, Is.LessThan(.05), stats);
                    else Assert.That(sum / checkedTexels, Is.GreaterThan(5.0), stats);
                }
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); }
        }
    }
}
