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
        public void OcclusionDarkensTheShareOfTheLightItsShaderDarkens()
        {
            var rig = AtlasLightRig.Create(new AtlasBakeSettings(), false);
            var all = AtlasOcclusionShading.Shares(rig, 1f, 1f);
            Assert.AreEqual(1f, all.x + all.y, 1e-4f, "direct and ambient: all of it");
            var ambientOnly = AtlasOcclusionShading.Shares(rig, 0f, 1f);
            Assert.AreEqual(0f, ambientOnly.x);
            Assert.AreEqual(all.y, ambientOnly.y, 1e-6f);
            Assert.That(ambientOnly.y, Is.InRange(0.2f, 0.5f), "the ambient's share, counting the shadow sides fully");
            Assert.AreEqual(Vector2.zero, AtlasOcclusionShading.Shares(rig, 0f, 0f));
        }

        private float Occluded(float o, Vector2 shares, Color tint, float strength = 1f, bool multiBounce = false, float albedo = 1f)
        {
            var color = new NordicsTextureWrapper(Size, Size, AtlasMapEvaluator.Settings(AtlasVirtualMap.BaseColor));
            color.Fill(new Color(albedo, albedo, albedo, 1f));
            disposables.Add(color);
            using (var occlusion = AtlasMapEvaluator.Constant(AtlasVirtualMap.Occlusion, o, Size, Size))
                AtlasOcclusionShading.Apply(color, occlusion, null, shares, tint, multiBounce, strength);
            return color.GetPixel(1, 1).g / albedo;
        }

        [Test]
        public void OcclusionMultipliesTheColorByItsDarkening()
        {
            var shares = new Vector2(0.2f, 0.3f);
            Assert.AreEqual(1f, Occluded(1f, shares, Color.black), 1e-5f, "unoccluded");
            Assert.AreEqual(0.5f, Occluded(0f, shares, Color.black), 1e-5f, "fully occluded: 1 − 0.2 − 0.3");
            Assert.AreEqual(0.75f, Occluded(0.5f, shares, Color.black), 1e-5f);
            Assert.AreEqual(0.75f, Occluded(0f, shares, Color.black, 0.5f), 1e-5f, "at half strength");
            Assert.AreEqual(1f, Occluded(0f, shares, Color.white), 1e-5f, "toward a white tint: nothing");
            Assert.AreEqual(0.75f, Occluded(0f, shares, new Color(0.5f, 0.5f, 0.5f)), 1e-5f, "toward a gray tint: halfway");
            Assert.Greater(Occluded(0.3f, shares, Color.black, multiBounce: true, albedo: 0.9f), Occluded(0.3f, shares, Color.black, albedo: 0.9f),
                "light colors keep more light with multi-bounce");
            Assert.AreEqual(Occluded(1f, shares, Color.black, multiBounce: true), 1f, 1e-3f);
        }

        private Texture2D Solid(float value)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
            texture.SetPixels(Enumerable.Repeat(new Color(value, value, value, 1f), 16).ToArray());
            texture.Apply();
            created.Add(texture);
            return texture;
        }

        [Test]
        public void WithoutAnOcclusionMapTheOcclusionIsBakedIntoTheBaseColorAndTheReflectionMask()
        {
            var material = new AtlasUnifiedMaterial();
            var response = material.Surface.Response;
            response.Occlusion.Map = new AtlasTextureReference { Texture = Solid(0.5f), Interpretation = AtlasTextureInterpretation.Data };
            response.OcclusionDirect = 1f;
            response.OcclusionIndirect = 1f;
            var job = new AtlasGeneratorJob();
            job.Sources.Add(new AtlasGeneratorSource { Material = material, UVRegion = new Rect(0f, 0f, 1f, 1f) });
            var plan = AtlasOutputPipeline.Resolve(job, BaseColorOnly());
            Assert.AreSame(AtlasOcclusionFeature.Baked, plan.Modes["occlusion"], "the default where the output has no occlusion map");
            Assert.IsFalse(plan.Maps.Any(m => m.Map == AtlasVirtualMap.Occlusion));
            using (var color = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor).Evaluate(0, material, Size, Size))
                Assert.AreEqual(0.5f, color.GetPixel(1, 1).g, 1e-3f, "darkening every light: × the occlusion");

            // Poiyomi: reflections × occlusion, in a reflection mask of their own.
            response.OcclusionDirect = 0f;
            response.OcclusionReflections = 1f;
            material.Lighting.Family = AtlasLightingFamily.PhysicallyBased;
            response.Reflections.Enabled = response.Highlights.Enabled = true;
            response.Reflections.Strength = response.Highlights.Strength = 1f;
            job.Features.Add(new AtlasFeatureChoice { FeatureId = "occlusion", ModeId = "baked" });
            var poiyomi = AtlasOutputPipeline.Resolve(job, AtlasShaderRegistry.Output("poiyomi10").Capabilities);
            var output = poiyomi.Output.Surface.Response;
            Assert.IsNotNull(output.Reflections.Mask?.Map, "a mask that holds the occlusion");
            var reflections = poiyomi.Maps.Single(m => m.Map == AtlasVirtualMap.ReflectionMask);
            using (var mask = reflections.Evaluate(0, material, Size, Size))
                Assert.AreEqual(0.5f, mask.GetPixel(1, 1).r, 1e-3f);
            Assert.AreNotSame(output.Reflections.Mask.Map, output.Highlights.Mask?.Map, "highlights aren't occluded");
            using (var color = poiyomi.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor).Evaluate(0, material, Size, Size))
            {
                var share = AtlasOcclusionShading.Shares(AtlasLightRig.Create(job.Baking, false), 0f, 1f).y;
                Assert.AreEqual(1f - 0.5f * share, color.GetPixel(1, 1).g, 1e-3f, "only the ambient's share");
            }
        }

        [Test]
        public void AMatcapIsLookedUpAsItsShaderDoes()
        {
            var layer = new AtlasLookupLayer();
            var front = Vector3.forward;
            AreEqual(new Vector3(0.5f, 0.5f, 0f), AtlasMatcapBake.Lookup(layer, front, front, Vector3.up), "facing the view: the center");
            var up = AtlasMatcapBake.Lookup(layer, new Vector3(0f, 0.6f, 0.8f), front, Vector3.up);
            Assert.AreEqual(0.8f, up.y, 1e-4f, "tilted up: 0.6 × 0.5 + 0.5");
            // The view's right, seen from the front of the avatar, is −X.
            Assert.Greater(AtlasMatcapBake.Lookup(layer, new Vector3(-0.6f, 0f, 0.8f), front, Vector3.up).x, 0.5f);
            layer.Tiling = new Vector2(0.5f, 0.5f);
            Assert.AreEqual(0.65f, AtlasMatcapBake.Lookup(layer, new Vector3(0f, 0.6f, 0.8f), front, Vector3.up).y, 1e-4f, "× the tiling (2 × border)");
            layer.Projection = AtlasLookupProjection.Gradient;
            var gradient = AtlasMatcapBake.Lookup(layer, new Vector3(0f, 0.6f, 0.8f), front, Vector3.up);
            Assert.AreEqual(0.2f, gradient.x, 1e-4f, "1 − |N·V|");
            Assert.AreEqual(gradient.x, gradient.y);
        }

        private Texture2D Solid(Color color)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
            texture.SetPixels(Enumerable.Repeat(color, 16).ToArray());
            texture.Apply();
            created.Add(texture);
            return texture;
        }

        private AtlasLookupLayer Matcap(Color color, AtlasBlend blend, AtlasPlacement placement = AtlasPlacement.Surface)
        {
            var layer = new AtlasLookupLayer { Blend = blend, Placement = placement, Opacity = 1f };
            layer.Source.Map = new AtlasTextureReference { Texture = Solid(color), Space = AtlasTextureSpace.View };
            return layer;
        }

        [Test]
        public void AMatcapIsBakedIntoTheColorByItsBlend()
        {
            var rig = AtlasLightRig.Create(new AtlasBakeSettings(), false);
            var tint = new Color(0.2f, 0.4f, 0.6f, 1f);
            var added = Gray();
            AtlasMatcapBake.ApplyColor(added, new AtlasUnifiedMaterial(), new[] { Matcap(tint, AtlasBlend.Add) }, new[] { Square() }, null, rig, 1f);
            var c = added.GetPixel(Size / 2, Size / 2);
            Assert.AreEqual(0.7f, c.r, 1e-3f);
            Assert.AreEqual(1.1f, c.b, 1e-3f);
            var multiplied = Gray();
            AtlasMatcapBake.ApplyColor(multiplied, new AtlasUnifiedMaterial(), new[] { Matcap(tint, AtlasBlend.Multiply) }, new[] { Square() }, null, rig, 0.5f);
            Assert.AreEqual(Mathf.Lerp(0.5f, 0.1f, 0.5f), multiplied.GetPixel(Size / 2, Size / 2).r, 1e-3f, "at half strength");
            // A light mask at its average: the key light's wrap (0.2) lights 60% of the
            // directions, so a light mask of 0.5 keeps 80% of the matcap.
            var masked = Gray();
            var shaded = Matcap(tint, AtlasBlend.Multiply);
            shaded.HideInShadow = 0.5f;
            AtlasMatcapBake.ApplyColor(masked, new AtlasUnifiedMaterial(), new[] { shaded }, new[] { Square() }, null, rig, 1f);
            Assert.AreEqual(0.8f, AtlasMatcapBake.LightMask(shaded, rig), 1e-5f);
            Assert.AreEqual(0.5f * Mathf.Lerp(1f, 0.2f, 0.8f), masked.GetPixel(Size / 2, Size / 2).r, 1e-3f);

            var emission = new NordicsTextureWrapper(Size, Size, AtlasMapEvaluator.Settings(AtlasVirtualMap.Emission));
            disposables.Add(emission);
            emission.Fill(Color.black);
            var glow = Matcap(tint, AtlasBlend.Add);
            glow.Opacity = 0f;
            glow.EmissionAmount = 2f;
            AtlasMatcapBake.ApplyEmission(emission, null, new AtlasUnifiedMaterial(),
                new[] { glow, Matcap(tint, AtlasBlend.Add, AtlasPlacement.Overlay) }, new[] { Square() }, null, rig, 0.5f);
            Assert.AreEqual(0.2f * 3f * 0.5f, emission.GetPixel(Size / 2, Size / 2).r, 1e-3f, "the glow and the overlay add, × the scale");

            // A glow that follows the light (light color mix, light mask): only the share that
            // shows without light glows; the rest is added to the color, which the light shades.
            var following = Matcap(tint, AtlasBlend.Add);
            following.Opacity = 0f;
            following.EmissionAmount = 1f;
            following.LightColorMix = 0.5f;
            following.HideInShadow = 0.5f;
            Assert.AreEqual(0.25f, AtlasMatcapBake.GlowShare(following), 1e-5f);
            var lit = Gray();
            AtlasMatcapBake.ApplyColor(lit, new AtlasUnifiedMaterial(), new[] { following }, new[] { Square() }, null, rig, 1f);
            Assert.AreEqual(0.5f + 0.2f * 0.75f, lit.GetPixel(Size / 2, Size / 2).r, 1e-3f, "the light-following three quarters, lit");
            var dark = new NordicsTextureWrapper(Size, Size, AtlasMapEvaluator.Settings(AtlasVirtualMap.Emission));
            disposables.Add(dark);
            dark.Fill(Color.black);
            AtlasMatcapBake.ApplyEmission(dark, null, new AtlasUnifiedMaterial(), new[] { following }, new[] { Square() }, null, rig, 1f);
            Assert.AreEqual(0.2f * 0.25f, dark.GetPixel(Size / 2, Size / 2).r, 1e-3f, "the quarter that shows without light, glowing");
            var noEmission = Gray();
            AtlasMatcapBake.ApplyColor(noEmission, new AtlasUnifiedMaterial(), new[] { following }, new[] { Square() }, null, rig, 1f, emission: false);
            Assert.AreEqual(0.5f + 0.2f, noEmission.GetPixel(Size / 2, Size / 2).r, 1e-3f, "without an emission: all of it, lit");
        }

        [Test]
        public void TheSidesReachTheEdgeOfTheMatcapAsSeenFromTheFront()
        {
            // A matcap holding its distance from the center: 0 in the middle, 1 at the edge.
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Min(1f, new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f).magnitude * 2f);
                    pixels[y * size + x] = new Color(r, r, r, 1f);
                }
            texture.SetPixels(pixels);
            texture.Apply();
            created.Add(texture);
            var layer = new AtlasLookupLayer { Blend = AtlasBlend.Add, Opacity = 1f };
            layer.Source.Map = new AtlasTextureReference { Texture = texture, Space = AtlasTextureSpace.View };
            var rig = AtlasLightRig.Create(new AtlasBakeSettings(), false);
            float Baked(Quaternion facing, Vector3? camera = null)
            {
                var color = new NordicsTextureWrapper(Size, Size, AtlasMapEvaluator.Settings(AtlasVirtualMap.BaseColor));
                disposables.Add(color);
                color.Fill(Color.black);
                AtlasMatcapBake.ApplyColor(color, new AtlasUnifiedMaterial(), new[] { layer }, new[] { Square(facing) }, null, rig, 1f, camera: camera);
                return color.GetPixel(Size / 2, Size / 2).r;
            }
            Assert.Less(Baked(Quaternion.identity), 0.1f, "facing the front: the center");
            Assert.Greater(Baked(Quaternion.Euler(0f, 80f, 0f)), 0.9f, "nearly edge-on from the front: the edge");
            Assert.Less(Baked(Quaternion.Euler(0f, 180f, 0f)), 0.1f, "the back, seen from behind: the center");

            // Seen from the side and above, a surface facing the front is off-center by the
            // angle between them.
            var settings = new AtlasBakeSettings { matcapViewSide = 30f, matcapViewHeight = 20f };
            var camera = AtlasMatcapBake.Camera(settings);
            Assert.Greater(camera.x, 0f, "toward the viewer's left, +X");
            Assert.Greater(camera.y, 0f);
            float angle = Vector3.Angle(Vector3.forward, camera) * Mathf.Deg2Rad;
            Assert.AreEqual(Mathf.Sin(angle), Baked(Quaternion.identity, camera), 0.05f);
        }

        [Test]
        public void MatcapsAreBakedWhereTheOutputHasNoSlotsOrWhereTheyArePlacedBaked()
        {
            var material = new AtlasUnifiedMaterial();
            var layer = Matcap(new Color(0.2f, 0.4f, 0.6f, 1f), AtlasBlend.Add);
            material.Layers.Add(layer);
            var job = new AtlasGeneratorJob();
            job.Sources.Add(new AtlasGeneratorSource { Material = material, UVRegion = new Rect(0f, 0f, 1f, 1f), Surface = () => new[] { Square() } });
            var plan = AtlasOutputPipeline.Resolve(job, BaseColorOnly());
            Assert.AreSame(AtlasMatcapFeature.Baked, plan.Modes["matcaps"], "the default without matcap slots");
            using (var color = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor).Evaluate(0, material, Size, Size))
                Assert.AreEqual(1.4f, color.GetPixel(Size / 2, Size / 2).g, 1e-3f, "white + the matcap");

            // Placed Baked on an output with slots: no slot, and its glow in the emission.
            layer.EmissionAmount = 1f;
            var capabilities = AtlasShaderRegistry.Output("poiyomi10").Capabilities;
            var feature = (AtlasMatcapFeature)AtlasOutputFeatures.Find(AtlasMatcapFeature.FeatureId);
            var key = feature.Instances(job, new[] { 0 }).Single().Key;
            var choice = new AtlasFeatureChoice { FeatureId = AtlasMatcapFeature.FeatureId };
            choice.Values[AtlasInstancePlacements.Id(key)] = ((float)AtlasInstancePlacement.Baked, "");
            job.Features.Add(choice);
            var poiyomi = AtlasOutputPipeline.Resolve(job, capabilities);
            Assert.AreEqual(0, poiyomi.MatcapSlotsUsed);
            using (var color = poiyomi.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor).Evaluate(0, material, Size, Size))
                Assert.AreEqual(1.4f, color.GetPixel(Size / 2, Size / 2).g, 1e-3f);
            var emission = poiyomi.Maps.Single(m => m.Map == AtlasVirtualMap.Emission);
            using (var glow = emission.Evaluate(0, material, Size, Size))
            {
                var target = poiyomi.Output.Layers.OfType<AtlasSurfaceLayer>().Single(l => l.Source.Map == emission.Reference)
                    .Targets.Single(t => t.Target == AtlasSurfaceTarget.Emission);
                Assert.AreEqual(0.4f, glow.GetPixel(Size / 2, Size / 2).g * target.Opacity, 1e-3f, "the matcap × its glow");
            }
        }

        [Test]
        public void ABakedMatcapThatAddsAboveOneGoesIntoTheSharedColor()
        {
            // One color on every source and nothing layered on it: the color stays shared, and
            // the map still has to rescale what the matcap adds above 1.
            var material = new AtlasUnifiedMaterial();
            material.Layers.Add(Matcap(new Color(0.2f, 0.4f, 0.6f, 1f), AtlasBlend.Add));
            var job = new AtlasGeneratorJob();
            job.Sources.Add(new AtlasGeneratorSource { Material = material, UVRegion = new Rect(0f, 0f, 1f, 1f), Surface = () => new[] { Square() } });
            var plan = AtlasOutputPipeline.Resolve(job, BaseColorOnly());
            var request = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor);
            var map = request.Evaluate(0, material, Size, Size);
            disposables.Add(map);
            float lit = map.GetPixel(Size / 2, Size / 2).b;
            Assert.AreEqual(1.6f, lit, 1e-3f, "white + the matcap");
            request.Finish(map);
            Assert.LessOrEqual(map.GetPixel(Size / 2, Size / 2).b, 1f + 1e-4f, "the map stays within 0–1");
            Assert.AreEqual(lit, plan.Output.Surface.BaseColor.Color.b * map.GetPixel(Size / 2, Size / 2).b, 1e-3f,
                "the shared color carries the rest");
        }

        [Test]
        public void ABakedMatcapIsAddedToTheTintedColorNotTintedWithIt()
        {
            // The shared color isn't in the map, but a contribution works on the surface's real
            // color: the source shows 0.5 + the matcap, not (1 + the matcap) × 0.5.
            var material = new AtlasUnifiedMaterial();
            material.Surface.BaseColor.Color = new Color(0.5f, 0.5f, 0.5f, 1f);
            material.Layers.Add(Matcap(new Color(0.2f, 0.4f, 0.6f, 1f), AtlasBlend.Add));
            var job = new AtlasGeneratorJob();
            job.Sources.Add(new AtlasGeneratorSource { Material = material, UVRegion = new Rect(0f, 0f, 1f, 1f), Surface = () => new[] { Square() } });
            var plan = AtlasOutputPipeline.Resolve(job, BaseColorOnly());
            var shared = plan.Output.Surface.BaseColor.Color;
            Assert.AreEqual(0.5f, shared.g, 1e-4f, "the one color stays shared");
            using (var map = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor).Evaluate(0, material, Size, Size))
                Assert.AreEqual(0.9f, shared.g * map.GetPixel(Size / 2, Size / 2).g, 1e-3f, "0.5 + the matcap");
        }

        [Test]
        public void AMapsContributionsRunByStageAndItsContentIsSetOnce()
        {
            var request = new AtlasMapRequest
            {
                Map = AtlasVirtualMap.BaseColor,
                Content = (i, m, w, h) => AtlasMapEvaluator.Constant(AtlasVirtualMap.BaseColor, 0.5f, w, h)
            };
            // Added in the other order: the shading first, the layer second.
            request.Add(new AtlasMapContribution
            {
                Feature = "test", Stage = AtlasMapStage.Shading,
                Apply = c => AtlasPixels.Map(c.Map, p => new Color(p.r * 2f, p.g * 2f, p.b * 2f, p.a))
            });
            request.Add(new AtlasMapContribution
            {
                Feature = "test", Stage = AtlasMapStage.Layers, Applies = i => i == 0,
                Apply = c => AtlasPixels.Map(c.Map, p => new Color(p.r + 0.25f, p.g + 0.25f, p.b + 0.25f, p.a))
            });
            using (var map = request.Evaluate(0, new AtlasUnifiedMaterial(), 2, 2))
                Assert.AreEqual(1.5f, map.GetPixel(0, 0).r, 1e-5f, "(0.5 + 0.25) × 2: the layer, then the shading");
            using (var map = request.Evaluate(1, new AtlasUnifiedMaterial(), 2, 2))
                Assert.AreEqual(1f, map.GetPixel(0, 0).r, 1e-5f, "the layer isn't on this source");
            Assert.Throws<System.InvalidOperationException>(() => request.Content = (i, m, w, h) => null,
                "later features add contributions; they don't replace the content");
        }

        // A matcap texture that is black but for a small spot at its center.
        private Texture2D Spot(float value)
        {
            const int size = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            var pixels = Enumerable.Repeat(Color.black, size * size).ToArray();
            for (int y = size / 2 - 1; y <= size / 2; y++)
                for (int x = size / 2 - 1; x <= size / 2; x++)
                    pixels[y * size + x] = new Color(value, value, value, 1f);
            texture.SetPixels(pixels);
            texture.Apply();
            created.Add(texture);
            return texture;
        }

        [Test]
        public void AutomaticBakesShadingMatcapsAndLeavesHighlightsOut()
        {
            // The shading multiplies all over; the highlight is a small bright spot that adds.
            // Both look up the planar reflection, as Poiyomi's double-sided mode does for every
            // kind of matcap: the lookup makes neither an effect.
            var material = new AtlasUnifiedMaterial();
            var shading = Matcap(new Color(0.5f, 0.5f, 0.5f, 1f), AtlasBlend.Multiply);
            shading.Projection = AtlasLookupProjection.ReflectionPlanar;
            var highlight = new AtlasLookupLayer { Blend = AtlasBlend.Add, Placement = AtlasPlacement.Surface, Opacity = 1f };
            highlight.Source.Map = new AtlasTextureReference { Texture = Spot(0.3f), Space = AtlasTextureSpace.View };
            highlight.Projection = AtlasLookupProjection.ReflectionPlanar;
            material.Layers.Add(shading);
            material.Layers.Add(highlight);
            var job = new AtlasGeneratorJob();
            job.Sources.Add(new AtlasGeneratorSource { Material = material, UVRegion = new Rect(0f, 0f, 1f, 1f), Surface = () => new[] { Square() } });
            var plan = AtlasOutputPipeline.Resolve(job, BaseColorOnly());
            using (var color = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor).Evaluate(0, material, Size, Size))
                Assert.AreEqual(0.5f, color.GetPixel(Size / 2, Size / 2).g, 1e-3f, "white × the shading matcap, without the highlight");
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("highlights") && n.Contains("aren't baked")), string.Join("\n", plan.Notes));

            // Soft light that brightens all over is baked, through the same lookup.
            var lit = new AtlasUnifiedMaterial();
            var light = Matcap(new Color(0.3f, 0.3f, 0.3f, 1f), AtlasBlend.Add);
            light.Projection = AtlasLookupProjection.ReflectionPlanar;
            lit.Layers.Add(light);
            job = new AtlasGeneratorJob();
            job.Sources.Add(new AtlasGeneratorSource { Material = lit, UVRegion = new Rect(0f, 0f, 1f, 1f), Surface = () => new[] { Square() } });
            plan = AtlasOutputPipeline.Resolve(job, BaseColorOnly());
            using (var color = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor).Evaluate(0, lit, Size, Size))
                Assert.AreEqual(1.3f, color.GetPixel(Size / 2, Size / 2).g, 1e-3f, "white + the light");
            Assert.IsFalse(plan.Notes.Any(n => n.Contains("aren't baked")), string.Join("\n", plan.Notes));
        }

        [Test]
        public void TheEnvironmentalRimAndTheBacklightGoIntoAFreeRimOfTheOutput()
        {
            var material = new AtlasUnifiedMaterial();
            var environmental = new AtlasAngleLayer
            {
                Placement = AtlasPlacement.Overlay, Profile = AtlasAngleProfile.Step, StepBase = AtlasAngleBase.Facing, Low = 0f, High = 0.45f,
                Falling = true, Smooth = true, ColorSource = AtlasAngleColorSource.EnvironmentAlongNormal, BaseColorMix = 1f, Opacity = 0.8f
            };
            material.Layers.Add(environmental);
            var job = new AtlasGeneratorJob();
            job.Sources.Add(new AtlasGeneratorSource { Material = material, UVRegion = new Rect(0f, 0f, 1f, 1f) });
            var toonStandard = AtlasShaderRegistry.Output("mobiletoon").Capabilities;
            var plan = AtlasOutputPipeline.Resolve(job, toonStandard);
            Assert.AreSame(AtlasEnvironmentalRimFeature.InRim, plan.Modes[AtlasEnvironmentalRimFeature.FeatureId]);
            var rim = plan.Output.Layers.OfType<AtlasAngleLayer>().Single();
            Assert.AreEqual(AtlasAngleColorSource.Color, rim.ColorSource, "the writer's rim");
            Assert.AreEqual(AtlasLightResponse.ScaleByLight, rim.LightResponse, "× the scene's light and ambient");
            Assert.That(rim.Opacity, Is.InRange(0.05f, 0.5f), "weaker: the scene's light is brighter than what its probe shows");
            Assert.IsTrue(AtlasRimCurve.FitBand(rim, out _, out _, out _));

            // The backlight takes it where the environmental rim doesn't.
            material.Layers.Clear();
            var backlight = new AtlasAngleLayer
            {
                Source = AtlasAngleSource.Backlight, Placement = AtlasPlacement.Overlay, Profile = AtlasAngleProfile.Step,
                Low = 0.625f, High = 0.675f, Directivity = 5f, ViewStrength = 1f, LightColorMix = 1f
            };
            backlight.Color.Color = new Color(0.85f, 0.8f, 0.7f, 1f);
            material.Layers.Add(backlight);
            var lit = AtlasOutputPipeline.Resolve(job, toonStandard);
            Assert.AreSame(AtlasBacklightFeature.InRim, lit.Modes[AtlasBacklightFeature.FeatureId]);
            var band = lit.Output.Layers.OfType<AtlasAngleLayer>().Single();
            Assert.AreEqual(AtlasAngleSource.ViewAngle, band.Source);
            Assert.AreEqual(AtlasAngleProfile.Band, band.Profile);
            Assert.That(band.Opacity, Is.InRange(0.2f, 0.6f), "its strength where it shows under the back light");
            Assert.AreEqual(backlight.Color.Color, band.Color.Color);

            // Both: the environmental rim comes first, and the backlight is listed.
            material.Layers.Add(environmental);
            var both = AtlasOutputPipeline.Resolve(job, toonStandard);
            Assert.AreEqual(1, both.Output.Layers.OfType<AtlasAngleLayer>().Count());
            Assert.IsTrue(both.Notes.Any(n => n.Contains("backlight is left out")), string.Join("\n", both.Notes));
        }

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
