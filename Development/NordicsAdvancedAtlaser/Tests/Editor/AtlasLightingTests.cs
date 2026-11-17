using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Lighting: one setup from the reference source, written by each output shader
    // (ATLAS_OUTPUT_PLAN.md, 2.6a).
    public sealed class AtlasLightingTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private static AtlasGeneratorJob Job(params (AtlasUnifiedMaterial Material, float Width)[] sources)
        {
            var job = new AtlasGeneratorJob();
            float x = 0f;
            foreach (var (material, width) in sources)
            {
                job.Sources.Add(new AtlasGeneratorSource { Material = material, UVRegion = new Rect(x, 0f, width, 1f) });
                x += width;
            }
            return job;
        }

        private static AtlasOutputCapabilities Poiyomi => AtlasShaderRegistry.Output("poiyomi10").Capabilities;

        private static AtlasUnifiedMaterial Lit(AtlasLightingFamily family)
        {
            var material = new AtlasUnifiedMaterial();
            material.Lighting.Family = family;
            return material;
        }

        [Test]
        public void TheLightingComesFromTheChosenSourceOrTheLargest()
        {
            var flat = Lit(AtlasLightingFamily.Flat);
            var toon = Lit(AtlasLightingFamily.RampToon);
            Assert.IsTrue(new AtlasLightingFeature().SharedValuesDiffer(new[] { flat, toon }, Poiyomi));
            Assert.IsFalse(new AtlasLightingFeature().SharedValuesDiffer(new[] { toon, Lit(AtlasLightingFamily.RampToon) }, Poiyomi));

            var job = Job((flat, 0.25f), (toon, 0.75f));
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            Assert.AreEqual(AtlasLightingFamily.RampToon, plan.Output.Lighting.Family, "automatic: the largest region");
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("taken from source 2") && n.Contains("source 1 is lit differently")));

            job.Features.Add(new AtlasFeatureChoice { FeatureId = AtlasLightingFeature.FeatureId, ReferenceSource = 0 });
            Assert.AreEqual(AtlasLightingFamily.Flat, AtlasOutputPipeline.Resolve(job, Poiyomi).Output.Lighting.Family, "the chosen source");
        }

        [Test]
        public void PartsDrawnOnTheTextureAreBakedOrListed()
        {
            var texture = new Texture2D(2, 2);
            created.Add(texture);
            var toon = Lit(AtlasLightingFamily.RampToon);
            toon.Lighting.Ramp.TermMask = new AtlasMask { Map = new AtlasTextureReference { Texture = texture } };
            var band = new AtlasShadeBand();
            band.Color.Map = new AtlasTextureReference { Texture = texture };
            toon.Lighting.Ramp.Bands.Add(band);
            var plan = AtlasOutputPipeline.Resolve(Job((toon, 1f)), Poiyomi);
            var ramp = plan.Output.Lighting.Ramp;
            // Not the source's own maps: Shadow Masks and Shadow Colors bake them into the atlas.
            Assert.AreNotSame(toon.Lighting.Ramp.TermMask, ramp.TermMask);
            Assert.AreEqual(AtlasChannel.Green, ramp.TermMask.Channel, "baked into the masks texture's G");
            Assert.IsNotNull(ramp.Bands.Single().Color.Map);
            Assert.AreNotSame(band.Color.Map, ramp.Bands.Single().Color.Map);
            Assert.AreSame(texture, band.Color.Map.Texture, "the source's own band is untouched");
            Assert.IsFalse(plan.Notes.Any(n => n.Contains("drawn on the texture")));

            var mobile = AtlasOutputPipeline.Resolve(Job((toon, 1f)), AtlasShaderRegistry.Output("mobiletoon").Capabilities);
            Assert.IsTrue(mobile.Notes.Any(n => n.Contains("drawn on the texture") && n.Contains("band and shade textures")));
        }

        [Test]
        public void ShadowColorsAndSkinThicknessAreBakedPerSource()
        {
            var colored = Lit(AtlasLightingFamily.RampToon);
            var band = new AtlasShadeBand();
            band.Color.Map = Gray(0.3f);
            colored.Lighting.Ramp.Bands.Add(band);
            var plain = Lit(AtlasLightingFamily.RampToon);
            plain.Lighting.Ramp.Bands.Add(new AtlasShadeBand());
            var job = Job((colored, 0.5f), (plain, 0.5f));
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var request = plan.Maps.Single(m => m.Map == AtlasVirtualMap.ShadowColor1);
            using (var map = request.Evaluate(0, colored, 4, 4)) Assert.AreEqual(0.3f, map.GetPixel(1, 1).r, 1e-3f, "the source's texture");
            using (var map = request.Evaluate(1, plain, 4, 4)) Assert.AreEqual(1f, map.GetPixel(1, 1).r, 1e-3f, "none: white");
            Assert.IsFalse(plan.Notes.Any(n => n.Contains("lit differently")), "baked textures don't count as lit differently");

            var skin = Lit(AtlasLightingFamily.PhysicallyBased);
            skin.Lighting.Diffuse = AtlasDiffuseModel.PreintegratedSkin;
            skin.Lighting.Skin.Thickness = new AtlasMask { Map = Gray(0.6f) };
            var other = Lit(AtlasLightingFamily.PhysicallyBased);
            other.Lighting.Diffuse = AtlasDiffuseModel.PreintegratedSkin;
            var skinPlan = AtlasOutputPipeline.Resolve(Job((skin, 0.5f), (other, 0.5f)), Poiyomi);
            var thickness = skinPlan.Maps.Single(m => m.Map == AtlasVirtualMap.SkinThickness);
            using (var map = thickness.Evaluate(0, skin, 4, 4)) Assert.AreEqual(0.6f, map.GetPixel(1, 1).r, 1e-3f);
            using (var map = thickness.Evaluate(1, other, 4, 4)) Assert.AreEqual(0f, map.GetPixel(1, 1).r, 1e-3f, "none: thickness 0");
            Assert.AreEqual(AtlasChannel.Red, skinPlan.Output.Lighting.Skin.Thickness.Channel);
        }

        private AtlasUnifiedMaterial SdfFace(AtlasSdfStyle style)
        {
            var face = Lit(AtlasLightingFamily.RampToon);
            face.Lighting.Ramp.Term = AtlasShadingTerm.SdfFace;
            face.Lighting.Ramp.Sdf = new AtlasSdfFace { Style = style, Map = Gray(0.5f), Blur = 0.2f };
            return face;
        }

        [Test]
        public void AnSdfFaceStaysLiveOnItsOwnUVs()
        {
            var face = SdfFace(AtlasSdfStyle.Threshold);
            var job = Job((face, 0.5f), (Lit(AtlasLightingFamily.RampToon), 0.25f));
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var sdf = plan.Output.Lighting.Ramp.Sdf;
            Assert.AreEqual(AtlasShadingTerm.SdfFace, plan.Output.Lighting.Ramp.Term);
            Assert.AreSame(face.Lighting.Ramp.Sdf.Map.Texture, sdf.Map.Texture, "the face's own texture");
            Assert.AreEqual(AtlasTextureSpace.UV1, sdf.Map.Space);
            Assert.AreEqual(AtlasTextureSpace.UV0, face.Lighting.Ramp.Sdf.Map.Space, "the source's own is untouched");
            var preserved = plan.SourceRemaps[1].PreservedUVs.Single();
            Assert.AreEqual(1, preserved.Channel);
            Assert.AreEqual(0, preserved.SourceChannel);

            var mobile = AtlasOutputPipeline.Resolve(job, AtlasShaderRegistry.Output("mobiletoon").Capabilities);
            Assert.IsTrue(mobile.Notes.Any(n => n.Contains("SDF face maps")));
        }

        [Test]
        public void ThePoiyomiWriterSetsTheLightingTexturesTheParserReadsBack()
        {
            var shader = Installed("poiyomi10");
            foreach (var style in new[] { AtlasSdfStyle.Threshold, AtlasSdfStyle.Blended })
            {
                var output = SdfFace(style);
                output.Lighting.Ramp.Sdf.Map.Space = AtlasTextureSpace.UV2;
                var material = new Material(shader.FindShader());
                created.Add(material);
                shader.Write(output, material);
                var sdf = AtlasShaderRegistry.Detect(material).Parse(material).Lighting.Ramp.Sdf;
                Assert.AreEqual(style, sdf?.Style, style.ToString());
                Assert.AreSame(output.Lighting.Ramp.Sdf.Map.Texture, sdf.Map.Texture, style.ToString());
                Assert.AreEqual(AtlasTextureSpace.UV2, sdf.Map.Space, style.ToString());
            }

            var banded = Lit(AtlasLightingFamily.RampToon);
            banded.Lighting.Ramp.Term = AtlasShadingTerm.HalfLambert;
            var band = new AtlasShadeBand();
            band.Color.Map = Gray(0.4f);
            banded.Lighting.Ramp.Bands.Add(band);
            var bandMaterial = new Material(shader.FindShader());
            created.Add(bandMaterial);
            shader.Write(banded, bandMaterial);
            var parsedBand = AtlasShaderRegistry.Detect(bandMaterial).Parse(bandMaterial).Lighting.Ramp.Bands.First();
            Assert.AreSame(band.Color.Map.Texture, parsedBand.Color.Map?.Texture, "the band texture");

            var skin = Lit(AtlasLightingFamily.PhysicallyBased);
            skin.Lighting.Diffuse = AtlasDiffuseModel.PreintegratedSkin;
            skin.Lighting.Skin.Thickness = new AtlasMask { Map = Gray(0.6f) };
            var skinMaterial = new Material(shader.FindShader());
            created.Add(skinMaterial);
            shader.Write(skin, skinMaterial);
            var parsedSkin = AtlasShaderRegistry.Detect(skinMaterial).Parse(skinMaterial).Lighting.Skin;
            Assert.AreSame(skin.Lighting.Skin.Thickness.Map.Texture, parsedSkin.Thickness?.Map?.Texture, "the skin thickness");
        }

        private IAtlasOutputShader Installed(string id)
        {
            var shader = AtlasShaderRegistry.InstalledOutputShaders.FirstOrDefault(s => s.ShaderId == id);
            if (shader == null) Assert.Ignore(id + " isn't installed.");
            return shader;
        }

        // A Poiyomi material with the given settings, parsed, written back and read again.
        private List<string> PoiyomiRoundTrip(Action<Material> set)
        {
            var shader = Installed("poiyomi10");
            var source = new Material(shader.FindShader());
            created.Add(source);
            set(source);
            var parsed = AtlasShaderRegistry.Detect(source).Parse(source);
            var output = new AtlasUnifiedMaterial();
            AtlasFieldwise.Copy(parsed.Lighting, output.Lighting);
            var written = new Material(shader.FindShader());
            created.Add(written);
            shader.Write(output, written);
            return AtlasLightingFeature.Unkept(output, written).ToList();
        }

        [Test]
        public void EveryPoiyomiLightingTypeRoundTrips()
        {
            var cases = new Dictionary<string, Action<Material>>
            {
                ["texture ramp"] = m =>
                {
                    m.SetFloat("_LightingMode", 0);
                    m.SetFloat("_ShadowStrength", 0.7f);
                    m.SetFloat("_ShadowOffset", 0.1f);
                    m.SetColor("_LightingShadowColor", new Color(0.5f, 0.4f, 0.6f, 1f));
                    m.SetFloat("_LightingIgnoreAmbientColor", 0.3f);
                },
                ["multilayer math"] = m =>
                {
                    m.SetFloat("_LightingMode", 1);
                    m.SetColor("_ShadowColor", new Color(0.6f, 0.5f, 0.7f, 1f));
                    m.SetFloat("_ShadowBorder", 0.4f);
                    m.SetColor("_Shadow2ndColor", new Color(0.3f, 0.2f, 0.4f, 0.5f));
                    m.SetFloat("_ShadowEnvStrength", 0.2f);
                },
                ["wrapped"] = m =>
                {
                    m.SetFloat("_LightingMode", 2);
                    m.SetFloat("_LightingWrappedWrap", 0.6f);
                    m.SetFloat("_LightingGradientStart", 0.1f);
                    m.SetFloat("_LightingGradientEnd", 0.6f);
                },
                ["skin"] = m =>
                {
                    m.SetFloat("_LightingMode", 3);
                    m.SetFloat("_SssScale", 0.8f);
                    m.SetFloat("_SkinScatteringStability", 0.3f);
                },
                ["shade map"] = m =>
                {
                    m.SetFloat("_LightingMode", 4);
                    m.SetColor("_1st_ShadeColor", new Color(0.7f, 0.6f, 0.6f, 1f));
                    m.SetFloat("_BaseColor_Step", 0.55f);
                    m.SetFloat("_BaseShade_Feather", 0.05f);
                },
                ["flat"] = m => m.SetFloat("_LightingMode", 5),
                ["realistic, with Light Data"] = m =>
                {
                    m.SetFloat("_LightingMode", 6);
                    m.SetFloat("_ShadowStrength", 0.8f);
                    m.SetFloat("_LightingColorMode", 2);
                    m.SetFloat("_Unlit_Intensity", 0.7f);
                    m.SetFloat("_LightingDirectionMode", 2);
                    m.SetVector("_LightngForcedDirection", new Vector4(0.2f, 1f, 0.3f, 0f));
                    m.SetFloat("_LightingCastedShadows", 0.5f);
                    m.SetFloat("_LightingMinLightBrightness", 0.1f);
                    m.SetFloat("_LightingAdditiveType", 1);
                    m.SetFloat("_LightingEnableLightVolumes", 1);
                    m.SetFloat("_LightingLightVolumesSpecular", 0.5f);
                },
                ["unlit"] = m => m.SetFloat("_ShadingEnabled", 0)
            };
            foreach (var entry in cases)
                CollectionAssert.IsEmpty(PoiyomiRoundTrip(entry.Value), entry.Key);
        }

        [Test]
        public void BandsBecomeARampWhereTheOutputOnlyTakesOne()
        {
            var toon = Lit(AtlasLightingFamily.RampToon);
            var band = new AtlasShadeBand { Border = 0.5f, Blur = 0.2f };
            band.Color.Color = new Color(0.6f, 0.6f, 0.6f, 1f);
            toon.Lighting.Ramp.Bands.Add(band);
            var keys = AtlasRampTexture.FromBands(toon.Lighting.Ramp);
            Assert.AreEqual(0f, AtlasRampTexture.Evaluate(keys, 0.3f).r, 1e-3f, "shadow");
            Assert.AreEqual(0.5f, AtlasRampTexture.Evaluate(keys, 0.5f).r, 0.02f, "the edge");
            Assert.AreEqual(1f, AtlasRampTexture.Evaluate(keys, 0.7f).r, 1e-3f, "lit");

            var job = Job((toon, 1f));
            var mobile = AtlasShaderRegistry.Output("mobiletoon").Capabilities;
            var plan = AtlasOutputPipeline.Resolve(job, mobile);
            Assert.IsNotEmpty(plan.Output.Lighting.Ramp.Keys);
            Assert.IsTrue(AtlasLightingFeature.NeedsGeneratedRamp(plan.Output, mobile));
            Assert.IsEmpty(AtlasOutputPipeline.Resolve(job, Poiyomi).Output.Lighting.Ramp.Keys, "Poiyomi keeps the bands");

            var texture = AtlasRampTexture.Make(plan.Output.Lighting.Ramp.Keys, "ramp", readable: true);
            created.Add(texture);
            Assert.AreEqual(0f, texture.GetPixel(Mathf.RoundToInt(0.2f * AtlasRampTexture.Width), 1).r, 0.02f);
            Assert.AreEqual(1f, texture.GetPixel(Mathf.RoundToInt(0.9f * AtlasRampTexture.Width), 1).r, 0.02f);

            toon.Lighting.Ramp.RampStrength = 0.5f;
            Assert.AreEqual(0.5f, AtlasRampTexture.Evaluate(AtlasRampTexture.FromBands(toon.Lighting.Ramp), 0.3f).r, 1e-3f, "half strength");

            var shader = Installed("mobiletoon");
            var material = new Material(shader.FindShader());
            created.Add(material);
            plan.Output.Lighting.Ramp.Ramp = new AtlasTextureReference { Texture = texture };
            shader.Write(plan.Output, material);
            Assert.AreSame(texture, material.GetTexture("_Ramp"));
            Assert.AreEqual(0.6f, material.GetFloat("_ShadowAlbedo"), 1e-3f, "the first band's shadow color");
        }

        private AtlasTextureReference Gray(float value)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
            texture.SetPixels(Enumerable.Repeat(new Color(value, value, value, 1f), 16).ToArray());
            texture.Apply();
            created.Add(texture);
            return new AtlasTextureReference { Texture = texture, Interpretation = AtlasTextureInterpretation.Data };
        }

        [Test]
        public void ShadowMasksAreBakedPerSource()
        {
            var masked = Lit(AtlasLightingFamily.RampToon);
            masked.Lighting.Ramp.RampStrengthMask = new AtlasMask { Map = Gray(0.4f) };
            var plain = Lit(AtlasLightingFamily.RampToon);
            var job = Job((masked, 0.5f), (plain, 0.5f));
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var request = plan.Maps.Single(m => m.Map == AtlasVirtualMap.ShadowStrengthMask);
            using (var map = request.Evaluate(0, masked, 4, 4)) Assert.AreEqual(0.4f, map.GetPixel(1, 1).r, 1e-3f, "the source's mask");
            using (var map = request.Evaluate(1, plain, 4, 4)) Assert.AreEqual(1f, map.GetPixel(1, 1).r, 1e-3f, "none: full strength");
            Assert.AreEqual(AtlasChannel.Red, plan.Output.Lighting.Ramp.RampStrengthMask.Channel);
            Assert.IsNull(plan.Output.Lighting.Ramp.TermMask, "no detail shadows: none requested");
            Assert.IsFalse(plan.Maps.Any(m => m.Map == AtlasVirtualMap.DetailShadow));

            var mobile = AtlasOutputPipeline.Resolve(job, AtlasShaderRegistry.Output("mobiletoon").Capabilities);
            Assert.IsTrue(mobile.Notes.Any(n => n.Contains("no shadow masks or detail shadows") && n.Contains("source 1")));
        }

        [Test]
        public void ThePoiyomiWriterSetsTheMasksTheParserReadsBack()
        {
            var shader = Installed("poiyomi10");
            var output = Lit(AtlasLightingFamily.RampToon);
            var strength = Gray(0.5f);
            output.Lighting.Ramp.RampStrengthMask = new AtlasMask { Map = strength, Channel = AtlasChannel.Red };
            output.Lighting.Ramp.TermMask = new AtlasMask { Map = strength, Channel = AtlasChannel.Green };
            var material = new Material(shader.FindShader());
            created.Add(material);
            shader.Write(output, material);
            var ramp = AtlasShaderRegistry.Detect(material).Parse(material).Lighting.Ramp;
            Assert.AreSame(strength.Texture, ramp.RampStrengthMask?.Map?.Texture);
            Assert.AreEqual(AtlasChannel.Red, ramp.RampStrengthMask.Channel);
            Assert.AreSame(strength.Texture, ramp.TermMask?.Map?.Texture);
            Assert.AreEqual(AtlasChannel.Green, ramp.TermMask.Channel);
        }

        [Test]
        public void EachWriterSetsItsFloorFromTheUnifiedFloors()
        {
            var toonStandard = Installed("mobiletoon");
            var lit = Lit(AtlasLightingFamily.RampToon);
            lit.Lighting.MinimumLight = 0.04f;
            var material = new Material(toonStandard.FindShader());
            created.Add(material);
            toonStandard.Write(lit, material);
            Assert.AreEqual(0.04f, material.GetFloat("_MinBrightness"), 1e-4f, "the minimum light");

            lit.Lighting.MinimumLight = 0f;
            lit.Lighting.ColorModel = AtlasLightColorModel.Floored;
            lit.Lighting.FloorIntensity = 1f;
            toonStandard.Write(lit, material);
            Assert.AreEqual(0.05f, material.GetFloat("_MinBrightness"), 1e-4f, "the floored color's ambient floor");

            lit.Lighting.MinimumLight = 0.5f;
            toonStandard.Write(lit, material);
            Assert.AreEqual(0.1f, material.GetFloat("_MinBrightness"), 1e-4f, "capped at the slider's 0.1");

            var poiyomi = Installed("poiyomi10");
            var bright = Lit(AtlasLightingFamily.RampToon);
            bright.Lighting.MinimumBrightness = 0.06f;
            var poiyomiMaterial = new Material(poiyomi.FindShader());
            created.Add(poiyomiMaterial);
            poiyomi.Write(bright, poiyomiMaterial);
            Assert.AreEqual(0.06f, poiyomiMaterial.GetFloat("_LightingMinLightBrightness"), 1e-4f, "Toon Standard's minimum brightness");
        }

        [Test]
        public void ToonStandardTakesTheRampAndShadowSettingsItHas()
        {
            var shader = Installed("mobiletoon");
            var output = Lit(AtlasLightingFamily.RampToon);
            var ramp = output.Lighting.Ramp;
            ramp.ShadowLift = 0.3f;
            ramp.ShadowTint.Color = new Color(0.4f, 0.4f, 0.4f, 1f);
            ramp.ShadowColorFromBase = 1f;
            ramp.ShadowDirect = 1f;
            ramp.ShadowAmbient = 0f;
            ramp.UntintedAmbient = 1f;
            output.Lighting.Ambient = AtlasAmbientModel.Average;
            output.Lighting.ReceiveShadows = 0f;
            output.Lighting.MinimumBrightness = 0.05f;
            output.Lighting.LimitBrightness = false;
            var material = new Material(shader.FindShader());
            created.Add(material);
            shader.Write(output, material);
            Assert.AreEqual(0.3f, material.GetFloat("_ShadowBoost"), 1e-4f);
            Assert.AreEqual(0.4f, material.GetFloat("_ShadowAlbedo"), 1e-3f);
            Assert.AreEqual(0.05f, material.GetFloat("_MinBrightness"), 1e-4f);
            Assert.AreEqual(0f, material.GetFloat("_LimitBrightness"));

            // A physically based source gets the SDK's realistic ramp, and the rest is listed.
            var physical = Lit(AtlasLightingFamily.PhysicallyBased);
            var second = new Material(shader.FindShader());
            created.Add(second);
            shader.Write(physical, second);
            var rampTexture = second.GetTexture("_Ramp");
            if (rampTexture != null) Assert.AreEqual("ShadowRampRealistic", rampTexture.name);
            Assert.IsNotEmpty(AtlasLightingFeature.Unkept(physical, second).ToList());
        }
    }
}
