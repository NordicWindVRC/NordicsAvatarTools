using System;
using System.Collections.Generic;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using NordicWind.TextureWrapper;
using UnityEngine;
using Object = UnityEngine.Object;
using T = NordicWind.AdvancedAtlaser.Tests.AtlasSubmeshBakeTests;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // The Normal Baker: slots, capture, and baking a curved original onto a flat lower-detail mesh.
    public sealed class AtlasNormalBakerTests
    {
        private readonly List<Object> created = new List<Object>();
        [TearDown] public void CleanUp() { foreach (var o in created) if (o != null) Object.DestroyImmediate(o); created.Clear(); }

        private Mesh GridMesh(int grid, Func<Vector2, Vector2> uvMap = null, bool tangents = true)
        {
            T.GridData(grid, out var v, out var n, out var t, out var uv, out var tri);
            if (uvMap != null) for (int i = 0; i < uv.Length; i++) uv[i] = uvMap(uv[i]);
            var mesh = new Mesh { name = "Grid" + grid, vertices = v, normals = n, uv = uv, triangles = tri };
            if (tangents) mesh.tangents = t;
            created.Add(mesh);
            return mesh;
        }
        private GameObject SceneObject(Mesh mesh)
        {
            var go = new GameObject("Normal baker test " + mesh.name);
            created.Add(go);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            return go;
        }
        private static AtlasMeshSection Capture(Object slot)
        {
            Assert.IsTrue(AtlasNormalBaker.TryCapture(AtlasNormalBaker.Resolve(slot), 0, out var section, out string error), error);
            return section;
        }

        [Test]
        public void Resolve_AcceptsMeshesAndMeshObjectsOnly()
        {
            var mesh = GridMesh(1);
            var asset = AtlasNormalBaker.Resolve(mesh);
            Assert.IsTrue(asset.Valid); Assert.IsFalse(asset.InScene);

            var meshObject = AtlasNormalBaker.Resolve(SceneObject(mesh));
            Assert.IsTrue(meshObject.Valid); Assert.IsTrue(meshObject.InScene);

            var skinnedObject = new GameObject("Skinned"); created.Add(skinnedObject);
            skinnedObject.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            Assert.IsTrue(AtlasNormalBaker.Resolve(skinnedObject).Valid);

            var parent = new GameObject("Parent"); created.Add(parent);
            SceneObject(mesh).transform.SetParent(parent.transform);
            Assert.IsTrue(AtlasNormalBaker.Resolve(parent).Valid, "one mesh below the object");
            SceneObject(mesh).transform.SetParent(parent.transform);
            Assert.IsNotNull(AtlasNormalBaker.Resolve(parent).Error, "several meshes below the object");

            var material = new Material(Shader.Find("Standard") ?? Shader.Find("Hidden/InternalErrorShader")); created.Add(material);
            Assert.IsNotNull(AtlasNormalBaker.Resolve(material).Error);
            var empty = AtlasNormalBaker.Resolve(null);
            Assert.IsFalse(empty.Valid); Assert.IsNull(empty.Error);
            Assert.AreEqual(1, AtlasNormalBaker.SubmeshLabels(asset).Length);
        }

        [Test]
        public void TryCapture_RefusesMeshesWithoutTangents()
        {
            var mesh = GridMesh(1, tangents: false);
            Assert.IsFalse(AtlasNormalBaker.TryCapture(AtlasNormalBaker.Resolve(mesh), 0, out _, out string error));
            StringAssert.Contains("tangents", error);
        }

        [Test]
        public void Bake_IdenticalMeshesGiveAFlatMap()
        {
            var mesh = GridMesh(4);
            using (var result = AtlasNormalBaker.Bake(Capture(mesh), Capture(mesh), 32, 32))
            {
                Assert.IsTrue(result.Identical); Assert.IsTrue(result.UVsMatch);
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                    Assert.AreEqual(1f, result.Map.GetPixel(x, y).b, 1e-6f);
            }
        }

        [Test]
        public void Bake_LowerDetailMeshDecodesToTheOriginalShading()
        {
            int size = T.Size;
            using (var result = AtlasNormalBaker.Bake(Capture(GridMesh(T.Grid)), Capture(GridMesh(1)), size, size))
            {
                Assert.IsTrue(result.UVsMatch, result.Summary);
                Assert.IsFalse(result.Identical);
                int checkedTexels = 0; double sum = 0, worst = 0;
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float s = ((x + .5f) / size - T.U0) / (T.U1 - T.U0), t = ((y + .5f) / size - T.U0) / (T.U1 - T.U0);
                    if (s <= 0f || s >= 1f || t <= 0f || t >= 1f) continue;
                    Color ts = result.Map.GetPixel(x, y);
                    var reduced = T.Interpolated(1, s, t);
                    double error = T.Angle(reduced.T * ts.r + reduced.B * ts.g + reduced.N * ts.b, T.Interpolated(T.Grid, s, t).N);
                    sum += error; worst = Math.Max(worst, error); checkedTexels++;
                }
                string stats = $"mean {sum / checkedTexels:F4}°, worst {worst:F4}°. {result.Summary}";
                Assert.That(checkedTexels, Is.GreaterThan(9000), stats);
                Assert.That(sum / checkedTexels, Is.LessThan(.05), stats);
                Assert.That(worst, Is.LessThan(.5), stats);
            }
        }

        // Where the original's surface at a UV is far from the current one, compared with the
        // current triangles' size, the correction fades: the original's normal belongs elsewhere.
        [Test]
        public void TheCorrectionFadesWhereTheSurfacesDontLineUp()
        {
            const int count = 9;
            NordicsTextureWrapper Map() => new NordicsTextureWrapper(count, 1,
                new TextureSettings { DataType = TextureDataType.LinearData, SRGB = false, Mipmaps = false });
            using (var original = Map()) using (var current = Map()) using (var sizes = Map())
            {
                for (int i = 0; i < count; i++)
                {
                    // Painted × coverage with coverage in alpha; the first texel is half covered.
                    float coverage = i == 0 ? .5f : 1f;
                    var place = new Vector3(i, 0f, 0f);
                    var moved = i == 7 ? place + Vector3.up * .5f : i == 8 ? place + Vector3.up * 2f : place;
                    original.SetPixel(i, 0, new Color(moved.x * coverage, moved.y * coverage, moved.z * coverage, coverage));
                    current.SetPixel(i, 0, new Color(place.x * coverage, place.y * coverage, place.z * coverage, coverage));
                    sizes.SetPixel(i, 0, new Color(coverage, coverage, coverage, coverage)); // triangles 1 long
                }
                AtlasSubmeshBaker.Alignment(original, current, sizes);
                AtlasPixels.Release();
                Assert.AreEqual(1f, original.GetPixel(0, 0).r, 1e-4f, "half covered, in place");
                Assert.AreEqual(1f, original.GetPixel(3, 0).r, 1e-4f, "in place");
                Assert.AreEqual(.5f, original.GetPixel(7, 0).r, 1e-3f, "half a triangle off: halfway");
                Assert.AreEqual(0f, original.GetPixel(8, 0).r, 1e-4f, "two triangles off: none");
            }
        }

        // Corrections fade to none at the borders of their UV islands, where the two sides of a
        // seam can't be made to match; the middle of an island keeps its correction.
        [Test]
        public void CorrectionsFadeToNoneAtUVIslandBorders()
        {
            const int width = 32, height = 9;
            Vector3 Tilted(float degrees) => new Vector3(Mathf.Sin(degrees * Mathf.Deg2Rad), 0f, Mathf.Cos(degrees * Mathf.Deg2Rad));
            float Angle(Color c) => Vector3.Angle(Vector3.forward, new Vector3(c.r, c.g, c.b));
            using (var map = new NordicsTextureWrapper(width, height,
                       new TextureSettings { DataType = TextureDataType.NormalTangent, SRGB = false, Mipmaps = false }))
            using (var coverage = new NordicsTextureWrapper(width, height,
                       new TextureSettings { DataType = TextureDataType.Mask, SRGB = false, Mipmaps = false }))
            {
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        // Two islands, x 0-15 and x 17-31, apart at x 16; every pixel tilted 20°.
                        Vector3 n = Tilted(20f);
                        map.SetPixel(x, y, new Color(n.x, n.y, n.z, 1f));
                        coverage.SetPixel(x, y, x == 16 ? Color.black : Color.white);
                    }
                // Map pixels at twice the final size; a fade over 2 final pixels is 4 map pixels.
                var result = AtlasNormalSeams.Fade(map, coverage, new Vector2(2f, 2f), 2f);
                Assert.Less(Angle(map.GetPixel(0, 4)), 2f, "at the border: none");
                Assert.Less(Angle(map.GetPixel(15, 4)), 2f, "at the border: none");
                Assert.Less(Angle(map.GetPixel(17, 4)), 2f, "the other island's border too");
                Assert.AreEqual(20f, Angle(map.GetPixel(8, 4)), .5f, "the middle keeps its correction");
                Assert.AreEqual(20f, Angle(map.GetPixel(24, 4)), .5f, "the middle keeps its correction");
                Assert.Greater(Angle(map.GetPixel(2, 4)), 2f, "fading in between");
                Assert.Less(Angle(map.GetPixel(2, 4)), 20f, "fading in between");
                Assert.Greater(result.FadedShare, 0f);
            }
        }

        [Test]
        public void Bake_DifferentUVLayoutsAreRefused()
        {
            // The current mesh's UVs were laid out again: the original uses the lower-left of the
            // texture, the current mesh the upper-right, where the original has no geometry.
            var original = GridMesh(8, uv => new Vector2(.05f + (uv.x - T.U0) * .4f, .05f + (uv.y - T.U0) * .4f));
            var relaid = GridMesh(1, uv => new Vector2(.6f + (uv.x - T.U0) * .4f, .6f + (uv.y - T.U0) * .4f));
            using (var result = AtlasNormalBaker.Bake(Capture(original), Capture(relaid), 64, 64))
            {
                Assert.IsFalse(result.UVsMatch, result.Summary);
                Assert.IsNull(result.Map);
            }
        }

        [Test]
        public void Bake_TransformsAppliedToBothMeshesDontChangeTheMap()
        {
            const int size = 64;
            var originalObject = SceneObject(GridMesh(8));
            var currentObject = SceneObject(GridMesh(1));
            Vector3[] Bake()
            {
                using (var result = AtlasNormalBaker.Bake(Capture(originalObject), Capture(currentObject), size, size))
                {
                    Assert.IsTrue(result.UVsMatch, result.Summary);
                    var map = new Vector3[size * size];
                    for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                    {
                        Color c = result.Map.GetPixel(x, y);
                        map[y * size + x] = new Vector3(c.r, c.g, c.b);
                    }
                    return map;
                }
            }
            var reference = Bake();
            foreach (var (rotation, scale) in new[] {
                (Quaternion.Euler(30f, 60f, 10f), new Vector3(2f, 2f, 2f)),
                (Quaternion.Euler(-20f, 0f, 45f), new Vector3(-1.5f, 1.5f, 1.5f)) })   // mirrored
            {
                foreach (var go in new[] { originalObject, currentObject })
                { go.transform.rotation = rotation; go.transform.localScale = scale; go.transform.position = new Vector3(3f, -1f, 2f); }
                Assert.IsNull(AtlasNormalBaker.OrientationWarning(AtlasNormalBaker.Resolve(originalObject), AtlasNormalBaker.Resolve(currentObject)));
                var transformed = Bake();
                double worst = 0;
                for (int i = 0; i < reference.Length; i++) worst = Math.Max(worst, T.Angle(reference[i], transformed[i]));
                Assert.That(worst, Is.LessThan(.1), $"rotation {rotation.eulerAngles}, scale {scale}: worst {worst:F4}°");
            }
        }

        [Test]
        public void OrientationWarning_WhenTheMeshesFaceDifferentWays()
        {
            var mesh = GridMesh(1);
            var sceneObject = SceneObject(mesh);
            var asset = AtlasNormalBaker.Resolve(mesh);
            Assert.IsNull(AtlasNormalBaker.OrientationWarning(asset, AtlasNormalBaker.Resolve(sceneObject)));
            sceneObject.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);   // typical FBX root rotation
            Assert.IsNotNull(AtlasNormalBaker.OrientationWarning(asset, AtlasNormalBaker.Resolve(sceneObject)));
        }
    }
}
