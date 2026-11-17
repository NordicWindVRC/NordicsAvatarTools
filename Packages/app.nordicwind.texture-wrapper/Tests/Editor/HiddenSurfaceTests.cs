using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NordicWind.TextureWrapper.Tests
{
    /// <summary>PaintTriangles with HiddenSurfaceOptions, on both processors.</summary>
    public class HiddenSurfaceTests
    {
        private static TextureSettings Data() => new TextureSettings { DataType = TextureDataType.LinearData, SRGB = false };

        // Two triangles over the same UVs, flat and facing +z: an outer one at z = 0.1 painting
        // red and an inner one painting green, at the given z and UV winding.
        private static void Layers(float innerZ, bool innerMirrored, bool innerFirst,
            List<Vector2> uv, List<Color> color, List<Vector3> positions, List<Vector3> normals)
        {
            var corners = new[] { new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.1f, 0.9f) };
            void Add(float z, Color c, bool mirrored)
            {
                var order = mirrored ? new[] { 0, 2, 1 } : new[] { 0, 1, 2 };
                foreach (int k in order)
                {
                    uv.Add(corners[k]);
                    color.Add(c);
                    positions.Add(new Vector3(corners[k].x, corners[k].y, z));
                    normals.Add(Vector3.forward);
                }
            }
            if (innerFirst) Add(innerZ, Color.green, innerMirrored);
            Add(0.1f, Color.red, false);
            if (!innerFirst) Add(innerZ, Color.green, innerMirrored);
        }

        private static void Paint(NordicsTextureWrapper map, float innerZ, bool innerMirrored, bool innerFirst, float margin = 0.01f)
        {
            var uv = new List<Vector2>();
            var color = new List<Color>();
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            Layers(innerZ, innerMirrored, innerFirst, uv, color, positions, normals);
            map.PaintTriangles(uv, color, new TrianglePaintOptions { SamplesPerAxis = 2 },
                new HiddenSurfaceOptions { Positions = positions, Normals = normals, Margin = margin });
        }

        // Pixels well inside both triangles.
        private static readonly Vector2Int[] Inside = { new Vector2Int(8, 8), new Vector2Int(5, 20), new Vector2Int(20, 5) };

        [Test]
        public void ASurfaceBehindAnotherIsntPainted()
        {
            foreach (bool innerFirst in new[] { true, false })
                BothProcessors.Run(processor =>
                {
                    using (var map = new NordicsTextureWrapper(32, 32, Data()))
                    {
                        Paint(map, 0f, false, innerFirst);
                        foreach (var p in Inside)
                        {
                            var c = map.GetPixel(p.x, p.y);
                            Assert.AreEqual(1f, c.r, 1e-4f, $"{processor}, inner first {innerFirst}: red at {p}");
                            Assert.AreEqual(0f, c.g, 1e-4f, $"{processor}, inner first {innerFirst}: no green at {p}");
                        }
                    }
                });
        }

        [Test]
        public void MirroredSurfacesDontHideEachOther()
        {
            BothProcessors.Run(processor =>
            {
                using (var map = new NordicsTextureWrapper(32, 32, Data()))
                {
                    Paint(map, 0f, true, true);
                    foreach (var p in Inside)
                    {
                        var c = map.GetPixel(p.x, p.y);
                        Assert.AreEqual(1f, c.r, 1e-4f, $"{processor}: red at {p}");
                        Assert.AreEqual(1f, c.g, 1e-4f, $"{processor}: green at {p}");
                    }
                }
            });
        }

        [Test]
        public void SurfacesWithinTheMarginArentHidden()
        {
            BothProcessors.Run(processor =>
            {
                using (var map = new NordicsTextureWrapper(32, 32, Data()))
                {
                    Paint(map, 0.095f, false, true);
                    foreach (var p in Inside)
                        Assert.AreEqual(1f, map.GetPixel(p.x, p.y).g, 1e-4f, $"{processor}: green at {p}");
                }
            });
        }

        [Test]
        public void ACurvedSurfaceDoesntHideItself()
        {
            // A dome made of many small triangles, all facing outwards: nothing is behind anything.
            var uv = new List<Vector2>();
            var color = new List<Color>();
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            const int cells = 12;
            Vector3 Dome(Vector2 t)
            {
                var d = new Vector2(t.x - 0.5f, t.y - 0.5f);
                return new Vector3(t.x, t.y, 0.3f - d.sqrMagnitude);
            }
            Vector3 Normal(Vector2 t) => new Vector3(2f * (t.x - 0.5f), 2f * (t.y - 0.5f), 1f).normalized;
            void Corner(Vector2 t) { uv.Add(t); color.Add(Color.white); positions.Add(Dome(t)); normals.Add(Normal(t)); }
            for (int y = 0; y < cells; y++)
                for (int x = 0; x < cells; x++)
                {
                    Vector2 a = new Vector2(x, y) / cells, b = new Vector2(x + 1, y) / cells, c = new Vector2(x, y + 1) / cells, d = new Vector2(x + 1, y + 1) / cells;
                    Corner(a); Corner(b); Corner(d);
                    Corner(a); Corner(d); Corner(c);
                }
            Color[] plain = null;
            BothProcessors.Run(processor =>
            {
                using (var expected = new NordicsTextureWrapper(48, 48, Data()))
                using (var map = new NordicsTextureWrapper(48, 48, Data()))
                {
                    var options = new TrianglePaintOptions { SamplesPerAxis = 2 };
                    expected.PaintTriangles(uv, color, options);
                    map.PaintTriangles(uv, color, options, new HiddenSurfaceOptions { Positions = positions, Normals = normals, Margin = 0.001f });
                    for (int y = 0; y < 48; y++)
                        for (int x = 0; x < 48; x++)
                            Assert.AreEqual(expected.GetPixel(x, y).r, map.GetPixel(x, y).r, 1e-4f, $"{processor}: ({x}, {y})");
                }
            });
        }

        [Test]
        public void HiddenSurfacesNeedACornerEach()
        {
            using (var map = new NordicsTextureWrapper(8, 8, Data()))
            {
                var uv = new List<Vector2> { Vector2.zero, Vector2.right, Vector2.up };
                var color = new List<Color> { Color.white, Color.white, Color.white };
                var three = new List<Vector3> { Vector3.zero, Vector3.zero, Vector3.zero };
                Assert.Throws<System.ArgumentException>(() => map.PaintTriangles(uv, color, null,
                    new HiddenSurfaceOptions { Positions = new List<Vector3> { Vector3.zero }, Normals = three }));
                Assert.Throws<System.ArgumentOutOfRangeException>(() => map.PaintTriangles(uv, color, null,
                    new HiddenSurfaceOptions { Positions = three, Normals = three, Margin = 0f }));
            }
        }
    }
}
