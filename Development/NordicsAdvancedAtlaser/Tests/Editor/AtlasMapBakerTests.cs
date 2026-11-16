using System.Collections.Generic;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    public sealed class AtlasMapBakerTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        // 8 × 8: red on the left half, green on the right.
        private Texture2D Halves()
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var pixels = new Color[64];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    pixels[y * 8 + x] = x < 4 ? Color.red : Color.green;
            texture.SetPixels(pixels);
            texture.Apply();
            created.Add(texture);
            return texture;
        }

        private Texture2D Solid(Color color)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply();
            created.Add(texture);
            return texture;
        }

        private static AtlasUnifiedMaterial WithBaseColor(Texture2D texture, Vector2? tiling = null, Vector2? offset = null, float rotation = 0f)
        {
            var material = new AtlasUnifiedMaterial();
            if (texture != null)
                material.Surface.BaseColor.Map = new AtlasTextureReference
                {
                    Texture = texture, Tiling = tiling ?? Vector2.one, Offset = offset ?? Vector2.zero, Rotation = rotation
                };
            return material;
        }

        private static void AssertColor(Color expected, NordicsTextureWrapper map, int x, int y)
        {
            var c = map.GetPixel(x, y);
            Assert.That(c.r, Is.EqualTo(expected.r).Within(0.02f), $"r at ({x}, {y})");
            Assert.That(c.g, Is.EqualTo(expected.g).Within(0.02f), $"g at ({x}, {y})");
            Assert.That(c.b, Is.EqualTo(expected.b).Within(0.02f), $"b at ({x}, {y})");
        }

        private static NordicsTextureWrapper Evaluate(AtlasUnifiedMaterial material) =>
            AtlasMapEvaluator.Evaluate(AtlasVirtualMap.BaseColor, material, 8, 8);

        [Test]
        public void BaseColourWithoutATextureIsWhite()
        {
            using (var map = Evaluate(WithBaseColor(null)))
                AssertColor(Color.white, map, 3, 3);
        }

        [Test]
        public void TilingScalesAroundTheUVOrigin()
        {
            // u × 2: the first quarter shows the texture's left half, the second its right half.
            using (var map = Evaluate(WithBaseColor(Halves(), tiling: new Vector2(2f, 1f))))
            {
                AssertColor(Color.red, map, 1, 4);
                AssertColor(Color.green, map, 3, 4);
                AssertColor(Color.red, map, 5, 4);
            }
        }

        [Test]
        public void OffsetShiftsTheUV()
        {
            using (var map = Evaluate(WithBaseColor(Halves(), offset: new Vector2(0.5f, 0f))))
            {
                AssertColor(Color.green, map, 1, 4);
                AssertColor(Color.red, map, 6, 4);
            }
        }

        [Test]
        public void RotationTurnsTheUVCounterclockwise()
        {
            // 90°: uv′ = (1 − v, u), so the bottom rows read the texture's right half.
            using (var map = Evaluate(WithBaseColor(Halves(), rotation: 90f)))
            {
                AssertColor(Color.green, map, 4, 1);
                AssertColor(Color.red, map, 4, 6);
            }
        }

        // A quad over u 0 to width (or the same quad one UV tile further on).
        private static AtlasUVTriangles LeftHalf(float tile = 0f, float width = 0.5f) => new AtlasUVTriangles
        {
            UV = new[] { new Vector2(tile, 0f), new Vector2(tile + width, 0f), new Vector2(tile + width, 1f), new Vector2(tile, 1f) },
            Triangles = new[] { 0, 1, 2, 0, 2, 3 }
        };

        [Test]
        public void CoverageMarksWhereTheTrianglesLie()
        {
            using (var mask = AtlasCoverage.Paint(new[] { LeftHalf() }, 8, 8))
            {
                Assert.IsTrue(mask.IsPixelUsed(1, 4));
                Assert.IsFalse(mask.IsPixelUsed(6, 4));
            }
            using (var mask = AtlasCoverage.Paint(new[] { LeftHalf(tile: 1f) }, 8, 8))
            {
                Assert.IsTrue(mask.IsPixelUsed(1, 4), "A triangle in the next UV tile covers the same texels.");
                Assert.IsFalse(mask.IsPixelUsed(6, 4));
            }
        }

        [Test]
        public void WithoutTrianglesThereIsNoCoverage()
        {
            Assert.IsNull(AtlasCoverage.Paint(new AtlasUVTriangles[0], 8, 8));
            var degenerate = new AtlasUVTriangles { UV = new[] { Vector2.zero, Vector2.zero, Vector2.zero }, Triangles = new[] { 0, 1, 2 } };
            Assert.IsNull(AtlasCoverage.Paint(new[] { degenerate }, 8, 8), "Nothing covered: the whole texture is used.");
        }

        [Test]
        public void IslandsGrowOverTheTextureTheMeshDoesntUse()
        {
            var job = new AtlasGeneratorJob();
            var source = new AtlasGeneratorSource { Material = WithBaseColor(Halves()), UVRegion = new Rect(0f, 0f, 1f, 1f) };
            // The island ends inside the red half: where it ends at the colour change, its edge
            // pixels are a filtered mix of both, and that mix is what grows.
            source.Coverage.Add(LeftHalf(width: 0.375f));
            job.Sources.Add(source);
            var request = new AtlasMapRequest { Map = AtlasVirtualMap.BaseColor };
            request.Sources.Add(0);
            using (var atlas = AtlasMapBaker.Bake(request, job, 16, 16))
            {
                AssertColor(Color.red, atlas, 2, 8);
                // The texture's green half isn't used by the mesh: the red island grows over it.
                AssertColor(Color.red, atlas, 13, 8);
            }
        }

        [Test]
        public void SourcesArePlacedInTheirRegionsAndTheGapsTakeTheNearestColour()
        {
            var job = new AtlasGeneratorJob();
            job.Sources.Add(new AtlasGeneratorSource { Material = WithBaseColor(Solid(Color.red)), UVRegion = new Rect(0f, 0f, 0.25f, 1f) });
            job.Sources.Add(new AtlasGeneratorSource { Material = WithBaseColor(Solid(Color.blue)), UVRegion = new Rect(0.75f, 0f, 0.25f, 1f) });
            job.Sources.Add(new AtlasGeneratorSource { Material = WithBaseColor(null), UVRegion = new Rect(0.25f, 0.5f, 0.25f, 0.5f) });
            var request = new AtlasMapRequest { Map = AtlasVirtualMap.BaseColor };
            request.Sources.AddRange(new[] { 0, 1, 2 });
            using (var atlas = AtlasMapBaker.Bake(request, job, 64, 32))
            {
                AssertColor(Color.red, atlas, 8, 16);
                AssertColor(Color.blue, atlas, 56, 16);
                AssertColor(Color.white, atlas, 20, 24);
                // Between the regions: the nearest one's colour.
                AssertColor(Color.red, atlas, 20, 4);
                AssertColor(Color.blue, atlas, 44, 4);
                Assert.IsTrue(atlas.IsPixelUsed(40, 4), "Every pixel is filled.");
            }
        }
    }
}
