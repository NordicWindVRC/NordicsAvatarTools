using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Rim lighting, the environmental rim and the backlight (ATLAS_OUTPUT.md, "Feature inventory").
    public sealed class AtlasRimTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private Texture2D Solid(Color color, int size = 4)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            texture.SetPixels(Enumerable.Repeat(color, size * size).ToArray());
            texture.Apply();
            created.Add(texture);
            return texture;
        }

        private static AtlasOutputCapabilities Poiyomi => AtlasShaderRegistry.Output("poiyomi10").Capabilities;
        private static AtlasOutputCapabilities ToonStandard => AtlasShaderRegistry.Output("mobiletoon").Capabilities;

        private static AtlasGeneratorJob Job(params AtlasUnifiedMaterial[] materials)
        {
            var job = new AtlasGeneratorJob();
            float x = 0f;
            for (int i = 0; i < materials.Length; i++)
            {
                float width = (materials.Length - i) / (materials.Length * (materials.Length + 1) / 2f);
                job.Sources.Add(new AtlasGeneratorSource { Material = materials[i], UVRegion = new Rect(x, 0f, width, 1f) });
                x += width;
            }
            return job;
        }

        // A Poiyomi-style rim: 1 − linstep(0.2, 0.6, facing).
        private static AtlasAngleLayer Rim(Color color, AtlasMask mask = null) => With(new AtlasAngleLayer
        {
            Placement = AtlasPlacement.Surface, Profile = AtlasAngleProfile.Step, StepBase = AtlasAngleBase.Facing,
            Falling = true, Power = 1f, Low = 0.2f, High = 0.6f, Mask = mask, SourceAlphaIsOpacity = true
        }, color);

        private static AtlasAngleLayer With(AtlasAngleLayer layer, Color color)
        {
            layer.Color.Color = color;
            return layer;
        }

        private static AtlasUnifiedMaterial Material(params AtlasLayer[] layers)
        {
            var material = new AtlasUnifiedMaterial();
            material.Layers.AddRange(layers);
            return material;
        }

        private static float Evaluate(AtlasOutputPlan plan, AtlasVirtualMap map, AtlasGeneratorJob job, int index, int channel = 0)
        {
            var request = plan.Maps.Single(m => m.Map == map);
            using (var result = request.Evaluate(index, job.Sources[index].Material, 4, 4)) return result.GetPixel(1, 1)[channel];
        }

        private static void Place(AtlasGeneratorJob job, string key, AtlasInstancePlacement placement, string target = "")
        {
            var choice = job.Features.Find(f => f.FeatureId == AtlasRimFeature.FeatureId);
            if (choice == null) job.Features.Add(choice = new AtlasFeatureChoice { FeatureId = AtlasRimFeature.FeatureId });
            choice.Values[AtlasInstancePlacements.Id(key)] = ((int)placement, target);
        }

        private static List<AtlasAngleLayer> Rims(AtlasOutputPlan plan) =>
            plan.Output.Layers.OfType<AtlasAngleLayer>().Where(l => l.ColorSource == AtlasAngleColorSource.Color && l.Source == AtlasAngleSource.ViewAngle).ToList();

        private static string Notes(AtlasOutputPlan plan) => string.Join(" | ", plan.Notes);

        [Test]
        public void TheRimCurveFollowsTheProfileAndFitsABand()
        {
            var rim = Rim(Color.white);
            Assert.AreEqual(1f, AtlasRimCurve.Evaluate(rim, 0f), 1e-5f, "edge-on");
            Assert.AreEqual(0f, AtlasRimCurve.Evaluate(rim, 1f), 1e-5f, "facing the viewer");
            Assert.AreEqual(0.5f, AtlasRimCurve.Evaluate(rim, 0.4f), 1e-5f);
            Assert.IsTrue(AtlasRimCurve.FitBand(rim, out float range, out float softness, out float peak));
            Assert.AreEqual(0.4f, range, 0.01f, "the midpoint at facing 0.4");
            Assert.AreEqual(1f, peak, 1e-5f);
            Assert.Greater(softness, 0.1f);

            var band = new AtlasAngleLayer { Profile = AtlasAngleProfile.Band, Width = 0.3f, Softness = 0.1f };
            Assert.IsTrue(AtlasRimCurve.FitBand(band, out range, out softness, out _));
            Assert.AreEqual(0.3f, range);
            Assert.AreEqual(0.1f, softness);

            var pixels = AtlasRimCurve.Matcap(rim, Color.red, 8);
            Assert.AreEqual(0f, pixels[3 * 8 + 3].r, 0.05f, "the center faces the viewer");
            Assert.AreEqual(1f, pixels[0].r, 1e-5f, "the corner is edge-on");
        }

        [Test]
        public void RimsThatLookTheSameShareASlotWithEachSourcesMask()
        {
            var mask = new AtlasMask { Map = new AtlasTextureReference { Texture = Solid(new Color(0.4f, 0f, 0f, 1f)) } };
            var job = Job(Material(Rim(Color.cyan, mask)), Material(Rim(Color.cyan)), new AtlasUnifiedMaterial());
            Assert.AreEqual(1, new AtlasRimFeature().Instances(job, new[] { 0, 1 }).Count);
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var rim = Rims(plan).Single();
            Assert.IsNotNull(rim.Mask?.Map);
            Assert.AreEqual(0.4f, Evaluate(plan, AtlasVirtualMap.Mask1, job, 0), 1e-4f);
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.Mask1, job, 1), 1e-4f);
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.Mask1, job, 2), 1e-4f);
        }

        [Test]
        public void ToonStandardDrawsAPartialRimIntoTheMatcapSlotOrShowsItEverywhere()
        {
            var band = With(new AtlasAngleLayer { Profile = AtlasAngleProfile.Band, Width = 0.3f, Softness = 0.1f }, Color.white);
            // Only the first material has it: drawn into the free matcap slot, with a mask.
            var job = Job(Material(band), new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            Assert.AreEqual(0, Rims(plan).Count, Notes(plan));
            var matcap = plan.Output.Layers.OfType<AtlasLookupLayer>().Single();
            Assert.AreSame(plan.GeneratedTextures.Single(t => t.Name == "RimMatcap1").Reference, matcap.Source.Map);
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.Mask1, job, 0), 1e-4f);
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.Mask1, job, 1), 1e-4f);

            // A matcap takes the slot: the rim shows on every part, listed.
            var withMatcap = new AtlasUnifiedMaterial();
            var lookup = new AtlasLookupLayer { Blend = AtlasBlend.Add, Projection = AtlasLookupProjection.ViewCorrected };
            lookup.Source.Map = new AtlasTextureReference { Texture = Solid(Color.white), Space = AtlasTextureSpace.View };
            withMatcap.Layers.Add(lookup);
            job = Job(Material(band), withMatcap);
            plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            Assert.AreEqual(1, Rims(plan).Count);
            StringAssert.Contains("shows on every part", Notes(plan));

            // Every material has it: the rim itself, exactly.
            job = Job(Material(band), Material(With(new AtlasAngleLayer { Profile = AtlasAngleProfile.Band, Width = 0.3f, Softness = 0.1f }, Color.white)));
            plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            Assert.AreEqual(1, Rims(plan).Count);
            Assert.IsFalse(plan.Notes.Any(n => n.Contains("every part") || n.Contains("fitted")), Notes(plan));
        }

        [Test]
        public void ThePlacementChoicesGiveRimSlotsMatcapsAndLeaveOut()
        {
            var job = Job(Material(Rim(Color.red)), Material(Rim(Color.green)), Material(Rim(Color.blue)));
            var keys = new AtlasRimFeature().Instances(job, new[] { 0, 1, 2 }).Select(i => i.Key).ToList();
            Assert.AreEqual(3, keys.Count);
            // Automatic: two rim slots, the third drawn into a matcap slot.
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            Assert.AreEqual(2, Rims(plan).Count);
            Assert.AreEqual(1, plan.Output.Layers.OfType<AtlasLookupLayer>().Count());

            job = Job(job.Sources.Select(s => s.Material).ToArray());
            Place(job, keys[1], AtlasInstancePlacement.AsMatcap);
            Place(job, keys[2], AtlasInstancePlacement.Off);
            plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            Assert.AreEqual(Color.red, Rims(plan).Single().Color.Color);
            Assert.AreEqual(1, plan.Output.Layers.OfType<AtlasLookupLayer>().Count());
            StringAssert.Contains("Rims left out", Notes(plan));

            job = Job(job.Sources.Select(s => s.Material).ToArray());
            Place(job, keys[1], AtlasInstancePlacement.Combined, keys[0]);
            Place(job, keys[2], AtlasInstancePlacement.Off);
            plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var rim = Rims(plan).Single();
            Assert.AreEqual(Color.red, rim.Color.Color, "the slot's rim");
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.Mask1, job, 1), 1e-4f, "shown on the second material too");
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.Mask1, job, 2), 1e-4f);
        }

        private static List<(IReadOnlyList<Color> Bases, float Weight)> Bases(params Color[] colors) =>
            new List<(IReadOnlyList<Color> Bases, float Weight)> { (colors, 1f) };

        // An output whose rim only adds after lighting gets the strength, base color mix and
        // lighting that look most like the source's rim, its blend, placement and glow included.
        [Test]
        public void ARimTheOutputCanOnlyAddIsFittedByItsLook()
        {
            var dark = new Color(0.01f, 0.01f, 0.01f, 1f);
            // Added after lighting: shown as it is, and a fit finds it again.
            var added = With(new AtlasAngleLayer { Profile = AtlasAngleProfile.Band, Opacity = 0.7f, BaseColorMix = 0.3f }, new Color(1f, 0.5f, 0.2f, 1f));
            Assert.IsTrue(AtlasRimResponse.AddsAsIs(added));
            var fit = AtlasRimResponse.Fit(added, 1f, Bases(new Color(0.3f, 0.3f, 0.3f, 1f), new Color(0.6f, 0.5f, 0.4f, 1f)));
            Assert.AreEqual(0.7f, fit.Strength, 0.01f, "as it is");
            Assert.AreEqual(0.3f, fit.BaseColorMix, 0.05f);
            Assert.IsFalse(fit.Lit);

            // Mixed into a dark base color before lighting, glowing at 0.2: the glow is what shows.
            var mixed = Rim(Color.white);
            mixed.Blend = AtlasBlend.Mixed;
            mixed.EmissionAmount = 0.2f;
            Assert.IsFalse(AtlasRimResponse.AddsAsIs(mixed));
            fit = AtlasRimResponse.Fit(mixed, 1f, Bases(dark));
            Assert.IsFalse(fit.Lit, "the glow is unlit");
            Assert.AreEqual(0.2f, fit.Strength, 0.03f);
            Assert.AreEqual(0f, fit.BaseColorMix, 0.05f, "the source's mix, on an even base");

            // Added to a gray base color before lighting: lit with it, only on the light's side.
            var surface = Rim(Color.white);
            surface.Opacity = 0.5f;
            fit = AtlasRimResponse.Fit(surface, 1f, Bases(new Color(0.5f, 0.5f, 0.5f, 1f)));
            Assert.IsTrue(fit.Lit);
            Assert.Less(fit.Strength, 0.5f);

            // Under an unlit atlas the surface isn't lit, and neither is a rim on it: added as it is.
            var unlit = Rim(Color.white);
            unlit.Opacity = 0.4f;
            fit = AtlasRimResponse.Fit(unlit, 1f, Bases(new Color(0.3f, 0.3f, 0.3f, 1f)), new AtlasLighting { Family = AtlasLightingFamily.Unlit });
            Assert.IsFalse(fit.Lit, "the atlas's lighting lights the surface the rim is on");
            Assert.AreEqual(0.4f, fit.Strength, 0.02f);

            // In a build: the slot's rim is the fitted one, listed.
            var first = Material(mixed);
            first.Surface.BaseColor.Color = dark;
            var copy = Rim(Color.white);
            copy.Blend = AtlasBlend.Mixed;
            copy.EmissionAmount = 0.2f;
            var second = Material(copy);
            second.Surface.BaseColor.Color = dark;
            var plan = AtlasOutputPipeline.Resolve(Job(first, second), ToonStandard);
            var rim = Rims(plan).Single();
            Assert.AreEqual(AtlasPlacement.Overlay, rim.Placement, Notes(plan));
            Assert.AreEqual(AtlasBlend.Add, rim.Blend);
            Assert.AreEqual(0f, rim.EmissionAmount);
            Assert.AreEqual(AtlasLightResponse.IgnoreLight, rim.LightResponse);
            Assert.AreEqual(0.2f, rim.Opacity, 0.03f);
            StringAssert.Contains("fitted to it", Notes(plan));
            Assert.AreEqual(AtlasBlend.Mixed, Rims(AtlasOutputPipeline.Resolve(Job(first, second), Poiyomi)).Single().Blend, "Poiyomi keeps it");
        }

        private static AtlasAngleLayer EnvironmentalRim(float opacity, AtlasMask mask = null) => new AtlasAngleLayer
        {
            Placement = AtlasPlacement.Overlay, Profile = AtlasAngleProfile.Step, StepBase = AtlasAngleBase.Facing, Power = 1f,
            Low = 0.1f, High = 0.5f, Falling = true, Smooth = true, ColorSource = AtlasAngleColorSource.EnvironmentAlongNormal,
            EnvironmentRoughnessOverride = true, EnvironmentRoughness = 0.6f, BaseColorMix = 1f, Opacity = opacity, Mask = mask
        };

        private static AtlasAngleLayer Backlight(Color color) => With(new AtlasAngleLayer
        {
            Source = AtlasAngleSource.Backlight, Placement = AtlasPlacement.Overlay, Profile = AtlasAngleProfile.Step, Power = 1f,
            Low = 0.6f, High = 0.7f, Directivity = 4f, LightColorMix = 1f, SourceAlphaIsOpacity = true, Faces = AtlasFaces.Front
        }, color);

        [Test]
        public void TheEnvironmentalRimKeepsItsStrengthPerPartInAMask()
        {
            var job = Job(Material(EnvironmentalRim(0.8f)), Material(EnvironmentalRim(0.4f)), new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var rim = plan.Output.Layers.OfType<AtlasAngleLayer>().Single(l => l.ColorSource != AtlasAngleColorSource.Color);
            Assert.AreEqual(0.8f, rim.Opacity, 1e-5f, "the strongest");
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.Mask1, job, 0), 1e-4f);
            Assert.AreEqual(0.5f, Evaluate(plan, AtlasVirtualMap.Mask1, job, 1), 1e-4f, "half the strongest");
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.Mask1, job, 2), 1e-4f);

            // Toon Standard: its one rim, lit by the scene, on every part.
            var toon = AtlasOutputPipeline.Resolve(job, ToonStandard);
            StringAssert.Contains("environmental rim is drawn by the output's rim", Notes(toon));
            StringAssert.Contains("also shows on source 3", Notes(toon));
        }

        [Test]
        public void TheBacklightColorPerPartGoesIntoItsMap()
        {
            var job = Job(Material(Backlight(new Color(2f, 0f, 0f, 1f))), Material(Backlight(Color.blue)), new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var light = plan.Output.Layers.OfType<AtlasAngleLayer>().Single(l => l.Source == AtlasAngleSource.Backlight);
            Assert.AreEqual(2f, Evaluate(plan, AtlasVirtualMap.BacklightColor, job, 0, 0), 1e-4f);
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.BacklightColor, job, 1, 2), 1e-4f);
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.BacklightColor, job, 2, 2), 1e-4f, "none: black");
            Assert.AreEqual(4f, light.Directivity);

            // The same color everywhere: no map.
            var same = Job(Material(Backlight(Color.blue)), Material(Backlight(Color.blue)));
            var shared = AtlasOutputPipeline.Resolve(same, Poiyomi);
            Assert.IsFalse(shared.Maps.Any(m => m.Map == AtlasVirtualMap.BacklightColor));
            Assert.AreEqual(Color.blue, shared.Output.Layers.OfType<AtlasAngleLayer>().Single().Color.Color);

            StringAssert.Contains("backlight is drawn by the output's rim", Notes(AtlasOutputPipeline.Resolve(job, ToonStandard)));
        }

        private List<AtlasAngleLayer> RoundTrip(string shaderId, AtlasUnifiedMaterial output)
        {
            var shader = AtlasShaderRegistry.Output(shaderId);
            var material = new Material(shader.FindShader());
            created.Add(material);
            shader.Write(output, material);
            return AtlasShaderRegistry.Detect(material).Parse(material).Layers.OfType<AtlasAngleLayer>().ToList();
        }

        [Test]
        public void TheWritersSetTheRimsTheParsersReadBack()
        {
            var maskTexture = Solid(Color.white);
            if (AtlasShaderRegistry.Output("poiyomi10").FindShader() != null)
            {
                // Rim 0 in the Poiyomi style, rim 1 in the UTS2 style with its unlit-side rim.
                var poiyomi = Rim(new Color(2f, 1f, 0.5f, 1f), new AtlasMask { Map = new AtlasTextureReference { Texture = maskTexture }, Channel = AtlasChannel.Green, Invert = true });
                poiyomi.Opacity = 0.7f;
                poiyomi.BaseColorMix = 0.3f;
                var mask = new AtlasMask { Map = new AtlasTextureReference { Texture = maskTexture }, Channel = AtlasChannel.Blue };
                var uts = With(new AtlasAngleLayer
                {
                    Placement = AtlasPlacement.Surface, Profile = AtlasAngleProfile.Step, StepBase = AtlasAngleBase.Edge, Power = 2f,
                    Low = 0.1f, High = 1f, Blend = AtlasBlend.Add, LightColorMix = 1f, Mask = mask
                }, Color.yellow);
                var dark = With(new AtlasAngleLayer
                {
                    Placement = AtlasPlacement.Surface, Profile = AtlasAngleProfile.Step, StepBase = AtlasAngleBase.Edge, Power = 4f,
                    Low = 0.1f, High = 1f, Blend = AtlasBlend.Add, LightColorMix = 1f, Mask = mask
                }, Color.blue);
                dark.LightSide.Mode = AtlasLightSideMode.Subtract;
                dark.LightSide.Invert = true;
                var output = Material(poiyomi, uts, dark, EnvironmentalRim(0.8f, new AtlasMask { Map = new AtlasTextureReference { Texture = maskTexture }, Channel = AtlasChannel.Alpha }),
                    Backlight(new Color(0.5f, 0.25f, 1f, 1f)));
                var parsed = RoundTrip("poiyomi10", output);
                var rims = parsed.Where(l => l.ColorSource == AtlasAngleColorSource.Color && l.Source == AtlasAngleSource.ViewAngle).ToList();
                Assert.AreEqual(3, rims.Count, "Poiyomi: two rims, the second with its unlit side");
                Assert.IsTrue(rims[0].Falling);
                Assert.AreEqual(0.2f, rims[0].Low, 1e-4f);
                Assert.AreEqual(0.6f, rims[0].High, 1e-4f);
                Assert.AreEqual(0.7f, rims[0].Opacity, 1e-4f);
                Assert.AreEqual(0.3f, rims[0].BaseColorMix, 1e-4f);
                Assert.AreEqual(2f, rims[0].Color.Color.r, 1e-3f, "color × brightness");
                Assert.AreEqual(0.5f, rims[0].Color.Color.b, 1e-3f);
                Assert.AreSame(maskTexture, rims[0].Mask?.Map?.Texture);
                Assert.AreEqual(AtlasChannel.Green, rims[0].Mask.Channel);
                Assert.IsTrue(rims[0].Mask.Invert);
                Assert.AreEqual(AtlasAngleBase.Edge, rims[1].StepBase, "UTS2");
                Assert.AreEqual(2f, rims[1].Power, 1e-3f);
                Assert.AreEqual(0.1f, rims[1].Low, 1e-4f);
                Assert.AreEqual(AtlasChannel.Blue, rims[1].Mask.Channel);
                Assert.IsTrue(rims[2].LightSide.Invert, "the unlit side");
                Assert.AreEqual(4f, rims[2].Power, 1e-3f);
                Assert.AreEqual(0f, rims[2].Color.Color.r, 1e-3f);

                var environment = parsed.Single(l => l.ColorSource == AtlasAngleColorSource.EnvironmentAlongNormal);
                Assert.AreEqual(0.1f, environment.Low, 1e-4f);
                Assert.AreEqual(0.5f, environment.High, 1e-4f);
                Assert.AreEqual(0.6f, environment.EnvironmentRoughness, 1e-4f);
                Assert.AreEqual(0.8f, environment.Opacity, 1e-4f);
                Assert.AreEqual(AtlasChannel.Alpha, environment.Mask.Channel);

                var light = parsed.Single(l => l.Source == AtlasAngleSource.Backlight);
                Assert.AreEqual(0.6f, light.Low, 1e-4f);
                Assert.AreEqual(0.7f, light.High, 1e-4f);
                Assert.AreEqual(4f, light.Directivity, 1e-4f);
                Assert.AreEqual(0.25f, light.Color.Color.g, 1e-3f);
                Assert.AreEqual(AtlasFaces.Front, light.Faces);

                Assert.IsFalse(RoundTrip("poiyomi10", new AtlasUnifiedMaterial()).Any(), "Poiyomi: none");
            }

            if (AtlasShaderRegistry.Output("mobiletoon").FindShader() != null)
            {
                var band = With(new AtlasAngleLayer
                {
                    Profile = AtlasAngleProfile.Band, Width = 0.4f, Softness = 0.15f, Opacity = 0.6f, BaseColorMix = 0.3f,
                    LightResponse = AtlasLightResponse.ScaleByLight
                }, new Color(0.5f, 0.8f, 1f, 1f));
                var parsed = RoundTrip("mobiletoon", Material(band)).Single();
                Assert.AreEqual(0.4f, parsed.Width, 1e-4f, "Toon Standard");
                Assert.AreEqual(0.15f, parsed.Softness, 1e-4f);
                Assert.AreEqual(0.6f, parsed.Opacity, 1e-4f);
                Assert.AreEqual(0.3f, parsed.BaseColorMix, 1e-4f);
                Assert.AreEqual(0.8f, parsed.Color.Color.g, 1e-3f);
                Assert.AreEqual(AtlasLightResponse.ScaleByLight, parsed.LightResponse);

                // A Poiyomi-style rim, fitted: its midpoint at facing 0.4.
                Assert.AreEqual(0.4f, RoundTrip("mobiletoon", Material(Rim(Color.white))).Single().Width, 0.01f);
                Assert.IsFalse(RoundTrip("mobiletoon", new AtlasUnifiedMaterial()).Any(), "Toon Standard: none");
            }
        }
    }
}
