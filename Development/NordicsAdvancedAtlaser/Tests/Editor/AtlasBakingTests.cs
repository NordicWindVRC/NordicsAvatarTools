using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // The baking toolkit (ATLAS_OUTPUT.md, "Baking toolkit"): the virtual light rig, the virtual
    // view, and normal-map shading baked into the base color.
    public sealed class AtlasBakingTests
    {
        private const int Size = 8;
        private readonly List<Object> created = new List<Object>();
        private readonly List<System.IDisposable> disposables = new List<System.IDisposable>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            foreach (var d in disposables) d.Dispose();
            created.Clear();
            disposables.Clear();
        }

        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static void AreEqual(Vector3 expected, Vector3 actual, string message = null)
        {
            Assert.AreEqual(expected.x, actual.x, 1e-4f, message);
            Assert.AreEqual(expected.y, actual.y, 1e-4f, message);
            Assert.AreEqual(expected.z, actual.z, 1e-4f, message);
        }

        // A unit square facing +Z, its UVs over the whole texture (counterclockwise), tangent +X.
        // Mirrored: the same UVs on a square facing +Z left of it, with U running along −X, as
        // mirrored halves are modeled.
        private static AtlasMeshSection Square(bool mirrored = false) => Square(Quaternion.identity, mirrored);

        // The square turned in avatar space.
        private static AtlasMeshSection Square(Quaternion rotation, bool mirrored = false)
        {
            float sign = mirrored ? -1f : 1f;
            return new AtlasMeshSection
            {
                Vertices = new[] { Vector3.zero, new Vector3(sign, 0f, 0f), new Vector3(sign, 1f, 0f), new Vector3(0f, 1f, 0f) },
                Normals = Enumerable.Repeat(Vector3.forward, 4).ToArray(),
                Tangents = Enumerable.Repeat(new Vector4(sign, 0f, 0f, mirrored ? -1f : 1f), 4).ToArray(),
                UV = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                Triangles = mirrored ? new[] { 0, 2, 1, 0, 3, 2 } : new[] { 0, 1, 2, 0, 2, 3 },
                LocalToWorld = Matrix4x4.Rotate(rotation)
            };
        }

        private NordicsTextureWrapper Normals(Vector3 tangentSpace)
        {
            tangentSpace.Normalize();
            var map = new NordicsTextureWrapper(Size, Size, AtlasMapEvaluator.Settings(AtlasVirtualMap.Normal));
            map.Fill(new Color(tangentSpace.x, tangentSpace.y, tangentSpace.z, 1f));
            disposables.Add(map);
            return map;
        }

        private NordicsTextureWrapper Gray()
        {
            var map = new NordicsTextureWrapper(Size, Size, AtlasMapEvaluator.Settings(AtlasVirtualMap.BaseColor));
            map.Fill(new Color(0.5f, 0.5f, 0.5f, 1f));
            disposables.Add(map);
            return map;
        }

        // The shading gain at the middle of the square: the color after over the color before.
        private float Gain(Vector3 tangentSpace, bool symmetric, params AtlasMeshSection[] sections)
        {
            var color = Gray();
            AtlasNormalShading.Apply(color, Normals(tangentSpace), sections, AtlasLightRig.Create(new AtlasBakeSettings(), symmetric), 1f);
            return Luminance(color.GetPixel(Size / 2, Size / 2)) / 0.5f;
        }

        [Test]
        public void ALightColorHasLuminanceOneAndTheColorInfluenceBlendsItFromWhite()
        {
            Assert.AreEqual(Color.white, AtlasLightRig.Temperature(3000f, 0f), "no influence: the brightness alone");
            var warm = AtlasLightRig.Temperature(3000f, 1f);
            var cool = AtlasLightRig.Temperature(10000f, 1f);
            Assert.AreEqual(1f, Luminance(warm), 1e-3f);
            Assert.AreEqual(1f, Luminance(cool), 1e-3f);
            Assert.Greater(warm.r, warm.b, "a low temperature is warm");
            Assert.Greater(cool.b, cool.r, "a high temperature is cool");
            var quarter = AtlasLightRig.Temperature(3000f, 0.25f);
            Assert.AreEqual(Mathf.Lerp(1f, warm.r, 0.25f), quarter.r, 1e-4f);
        }

        [Test]
        public void TheClassicRigSplitsKeyAndFillAndTheSymmetricRigIsCentered()
        {
            var settings = new AtlasBakeSettings();
            settings.fill.intensity = 2f;
            var classic = AtlasLightRig.Create(settings, false);
            Assert.Less(classic.Key.Direction.x * classic.Fill.Direction.x, 0f, "the key and the fill on opposite sides");
            Assert.Greater(classic.Key.Direction.y, 0f, "the key above");
            Assert.Greater(classic.Key.Direction.z, 0f, "the key in front");
            Assert.Less(classic.Back.Direction.z, 0f, "the back light behind");
            Assert.AreEqual(AtlasLightRig.FillDiffuse * 2f, classic.Fill.Diffuse, 1e-5f, "the role's weight × the intensity");

            var symmetric = AtlasLightRig.Create(settings, true);
            foreach (var light in new[] { symmetric.Key, symmetric.Fill, symmetric.Back })
                Assert.AreEqual(0f, light.Direction.x, 1e-5f, "centered");
            Assert.Greater(symmetric.Key.Direction.y, symmetric.Fill.Direction.y, "the key above the fill");
            Assert.AreEqual(classic.Key.Color, symmetric.Key.Color, "the rigs share the lights' settings");
        }

        [Test]
        public void TheVirtualViewIsFrontalInFrontAndAlongTheNormalBehind()
        {
            AreEqual(Vector3.forward, AtlasVirtualView.Direction(Vector3.forward));
            AreEqual(Vector3.forward, AtlasVirtualView.Direction(new Vector3(0.6f, 0f, 0.8f)), "within the frontal range");
            AreEqual(Vector3.back, AtlasVirtualView.Direction(Vector3.back));
            var side = AtlasVirtualView.Direction(Vector3.right);
            Assert.Greater(side.x, 0f);
            Assert.Greater(side.z, 0f, "the sides blend between the two");

            foreach (var view in new[] { Vector3.forward, Vector3.right, Vector3.up, Vector3.back, new Vector3(1f, 1f, -1f).normalized })
                AreEqual(view, AtlasVirtualView.Turn(Vector3.forward, view), "the front turns onto the view");
            AreEqual(Vector3.up, AtlasVirtualView.Turn(Vector3.up, Vector3.right), "up stays up when the view turns sideways");
        }

        [Test]
        public void AFlatNormalMapLeavesTheColorAsItIs()
        {
            var color = Gray();
            int changed = AtlasNormalShading.Apply(color, Normals(Vector3.forward), new[] { Square() },
                AtlasLightRig.Create(new AtlasBakeSettings(), false), 1f);
            Assert.AreEqual(Size * Size, changed);
            foreach (var c in color.GetPixels())
            {
                Assert.AreEqual(0.5f, c.r, 1e-4f);
                Assert.AreEqual(0.5f, c.b, 1e-4f);
            }
        }

        [Test]
        public void DetailFacingTheLightsBrightensAndDetailFacingAwayDarkens()
        {
            Assert.Greater(Gain(new Vector3(0f, 0.5f, 0.866f), false, Square()), 1.05f, "tilted up, toward the key");
            Assert.Less(Gain(new Vector3(0f, -0.5f, 0.866f), false, Square()), 0.95f, "tilted down");
            Assert.Greater(Gain(new Vector3(0.5f, 0f, 0.866f), false, Square()), Gain(new Vector3(-0.5f, 0f, 0.866f), false, Square()),
                "the classic key is on the +X side");
            Assert.AreEqual(Gain(new Vector3(0.5f, 0f, 0.866f), true, Square()), Gain(new Vector3(-0.5f, 0f, 0.866f), true, Square()), 1e-4f,
                "the symmetric rig lights both sides alike");
        }

        [Test]
        public void DetailReadsTheSameOnSurfacesFacingAnyWay()
        {
            // Facing down and forward, as under a jaw: in the frontal view, but the key from above
            // would only graze it. Every surface gets the lights at the angles a front one does.
            var tilts = new[] { new Vector3(0f, 0.5f, 0.866f), new Vector3(0f, -0.5f, 0.866f), new Vector3(0.4f, 0.2f, 0.89f) };
            var facings = new[]
            {
                Quaternion.FromToRotation(Vector3.forward, new Vector3(0f, -0.8f, 0.6f)),
                Quaternion.FromToRotation(Vector3.forward, new Vector3(0f, -0.95f, 0.3f)),
                Quaternion.Euler(0f, 180f, 0f),
                Quaternion.Euler(0f, 90f, 0f)
            };
            foreach (var tilt in tilts)
            {
                float front = Gain(tilt, false, Square());
                foreach (var facing in facings)
                    Assert.AreEqual(front, Gain(tilt, false, Square(facing)), 1e-3f, $"{tilt} facing {facing * Vector3.forward}");
            }
        }

        [Test]
        public void MirroredHalvesAreShadedApartAndAveraged()
        {
            // Under the symmetric rig, a mirrored half shades exactly like its original, so the
            // shared texels keep the same result instead of mixing the two halves' frames.
            var tilted = new Vector3(0.5f, 0.2f, 0.84f);
            float single = Gain(tilted, true, Square());
            Assert.AreEqual(single, Gain(tilted, true, Square(), Square(true)), 1e-4f);
            Assert.AreEqual(single, Gain(tilted, true, Square(true)), 1e-4f);
        }

        [Test]
        public void OverlappingUVsAreFound()
        {
            AtlasUVTriangles Triangles(AtlasMeshSection s) => new AtlasUVTriangles { UV = s.UV, Triangles = s.Triangles };
            Assert.IsFalse(AtlasSurfaceGeometry.Overlaps(new[] { Triangles(Square()) }));
            Assert.IsTrue(AtlasSurfaceGeometry.Overlaps(new[] { Triangles(Square()), Triangles(Square(true)) }), "mirrored halves");
            Assert.IsFalse(AtlasSurfaceGeometry.Overlaps(new AtlasUVTriangles[0]));
            var settings = new AtlasBakeSettings();
            var source = new AtlasGeneratorSource();
            source.Coverage.Add(Triangles(Square()));
            Assert.IsFalse(AtlasNormalFeature.UsesSymmetricRig(settings, source));
            source.Coverage.Add(Triangles(Square(true)));
            Assert.IsTrue(AtlasNormalFeature.UsesSymmetricRig(settings, source), "overlapping UVs use the symmetric rig");
        }

        private Texture2D NormalTexture(Vector3 normal)
        {
            normal.Normalize();
            var encoded = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1f);
            var texture = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
            texture.SetPixels(Enumerable.Repeat(encoded, 16).ToArray());
            texture.Apply();
            created.Add(texture);
            return texture;
        }

        private static AtlasOutputCapabilities BaseColorOnly() => new AtlasOutputCapabilities
        {
            Textures =
            {
                new AtlasOutputTexture("BaseColor", "Base color") { SizeMode = AtlasTextureSizeMode.Main }
                    .With(AtlasVirtualMap.BaseColor, AtlasTextureChannels.RGB)
            }
        };

        [Test]
        public void WithoutANormalMapTheNormalsAreBakedIntoTheBaseColor()
        {
            var material = new AtlasUnifiedMaterial();
            material.Surface.Normal.Map = new AtlasTextureReference
                { Texture = NormalTexture(new Vector3(0f, 0.5f, 0.866f)), Interpretation = AtlasTextureInterpretation.TangentNormal };
            material.Surface.Normal.Strength = 1f;
            var job = new AtlasGeneratorJob();
            job.Sources.Add(new AtlasGeneratorSource { Material = material, UVRegion = new Rect(0f, 0f, 1f, 1f), Surface = () => new[] { Square() } });
            var plan = AtlasOutputPipeline.Resolve(job, BaseColorOnly());
            Assert.AreSame(AtlasNormalFeature.Baked, plan.Modes["normal"], "the default where the output has no normal map");
            Assert.IsFalse(plan.Maps.Any(m => m.Map == AtlasVirtualMap.Normal));

            var request = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor);
            var map = request.Evaluate(0, material, Size, Size);
            disposables.Add(map);
            float lit = map.GetPixel(Size / 2, Size / 2).g;
            Assert.Greater(lit, 1.05f, "white, brightened where the detail faces the key");
            request.Finish(map);
            Assert.LessOrEqual(map.GetPixel(Size / 2, Size / 2).g, 1f + 1e-4f, "the map stays within 0–1");
            Assert.AreEqual(lit, plan.Output.Surface.BaseColor.Color.g * map.GetPixel(Size / 2, Size / 2).g, 1e-3f,
                "the shared color carries the rest");

            job.Baking.normalShading = 0f;
            var off = AtlasOutputPipeline.Resolve(job, BaseColorOnly()).Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor);
            using (var plain = off.Evaluate(0, material, Size, Size))
                Assert.AreEqual(1f, plain.GetPixel(Size / 2, Size / 2).g, 1e-4f, "at strength 0: the color as it is");

            job.Baking.normalShading = 1f;
            job.Sources[0].Surface = null;
            var unknown = AtlasOutputPipeline.Resolve(job, BaseColorOnly());
            Assert.IsTrue(unknown.Notes.Any(n => n.Contains("needs the meshes")), string.Join("\n", unknown.Notes));
        }
    }
}
