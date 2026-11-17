using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    public sealed class AtlasOutputFeatureTests
    {
        private static AtlasOutputCapabilities WithBaseColor() => new AtlasOutputCapabilities
        {
            Textures =
            {
                new AtlasOutputTexture("BaseColor", "Base color") { SizeMode = AtlasTextureSizeMode.Main }
                    .With(AtlasVirtualMap.BaseColor, AtlasTextureChannels.RGB)
            }
        };

        private static AtlasGeneratorJob Job(int sources)
        {
            var job = new AtlasGeneratorJob();
            for (int i = 0; i < sources; i++)
                job.Sources.Add(new AtlasGeneratorSource { Material = new AtlasUnifiedMaterial(), UVRegion = new Rect(0.5f * i, 0f, 0.5f, 1f) });
            return job;
        }

        private static IAtlasOutputFeature Color() => AtlasOutputFeatures.Find("color");

        // Not registered: it has no parameterless constructor.
        private sealed class TwoModeFeature : IAtlasOutputFeature
        {
            private readonly AtlasFeatureMode[] modes;
            public TwoModeFeature(System.Func<AtlasOutputCapabilities, bool> secondNeeds)
            {
                modes = new[]
                {
                    new AtlasFeatureMode { Id = "first", Label = "First", Kind = AtlasFeatureModeKind.Native, RequiredMaps = new[] { AtlasVirtualMap.BaseColor } },
                    new AtlasFeatureMode { Id = "second", Label = "Second", Kind = AtlasFeatureModeKind.Baked, Requires = secondNeeds },
                    AtlasFeatureMode.Off
                };
            }
            public string Id => "test.two-modes";
            public string DisplayName => "Two modes";
            public AtlasFeatureSection Section => AtlasFeatureSection.Effects;
            public int Order => 0;
            public IReadOnlyList<AtlasFeatureMode> Modes => modes;
            public bool IsPresent(AtlasUnifiedMaterial source) => true;
            public void Resolve(AtlasFeatureContext context) { }
        }

        [Test]
        public void ColorIsRegisteredInTheSurfaceSection()
        {
            Assert.NotNull(Color());
            Assert.AreEqual(AtlasFeatureSection.Surface, Color().Section);
        }

        [Test]
        public void ModesAreOfferedOnlyWhenTheOutputProvidesTheirMaps()
        {
            Assert.IsEmpty(AtlasOutputFeatures.Available(Color(), new AtlasOutputCapabilities()));
            var modes = AtlasOutputFeatures.Available(Color(), WithBaseColor());
            CollectionAssert.AreEqual(new[] { "native", "off" }, modes.Select(m => m.Id).ToArray());
        }

        [Test]
        public void FeatureWithoutASupportedModeIsLeftOut()
        {
            var plan = AtlasOutputPipeline.Resolve(Job(2), new AtlasOutputCapabilities());
            Assert.IsFalse(plan.Modes.ContainsKey("color"));
            Assert.IsEmpty(plan.Maps);
            Assert.IsNull(plan.Output.Surface.BaseColor.Map);
        }

        [Test]
        public void StoredModeIsUsedOnlyWhereTheOutputSupportsIt()
        {
            var feature = new TwoModeFeature(c => c.Textures.Count > 1);
            var oneTexture = WithBaseColor();
            var twoTextures = WithBaseColor();
            twoTextures.Textures.Add(new AtlasOutputTexture("Other", "Other"));
            // The stored choice isn't lost: an output that supports it uses it again.
            Assert.AreEqual("first", AtlasOutputFeatures.Effective(feature, "second", oneTexture).Id);
            Assert.AreEqual("second", AtlasOutputFeatures.Effective(feature, "second", twoTextures).Id);
            Assert.AreEqual("first", AtlasOutputFeatures.Effective(feature, "", twoTextures).Id, "Empty: the default.");
            Assert.AreEqual("off", AtlasOutputFeatures.Effective(feature, "off", oneTexture).Id);
        }

        [Test]
        public void ColorRequestsOneBaseColorMapFromEverySource()
        {
            var plan = AtlasOutputPipeline.Resolve(Job(2), WithBaseColor());
            var request = plan.Maps.Single();
            Assert.AreEqual(AtlasVirtualMap.BaseColor, request.Map);
            CollectionAssert.AreEqual(new[] { 0, 1 }, request.Sources);
            Assert.AreSame(request.Reference, plan.Output.Surface.BaseColor.Map);
            Assert.AreEqual(UnityEngine.Color.white, plan.Output.Surface.BaseColor.Color);
            Assert.AreEqual("native", plan.Modes["color"].Id);
        }

        [Test]
        public void SourcesTurnedOffAreLeftOutOfTheMap()
        {
            var job = Job(2);
            var choice = new AtlasFeatureChoice { FeatureId = "color" };
            choice.DisabledSources.Add(1);
            job.Features.Add(choice);
            CollectionAssert.AreEqual(new[] { 0 }, AtlasOutputPipeline.Resolve(job, WithBaseColor()).Maps.Single().Sources);
            choice.DisabledSources.Add(0);
            Assert.IsEmpty(AtlasOutputPipeline.Resolve(job, WithBaseColor()).Maps, "Every source turned off: nothing to resolve.");
        }

        [Test]
        public void OffAddsNothingToTheOutput()
        {
            var job = Job(1);
            job.Features.Add(new AtlasFeatureChoice { FeatureId = "color", ModeId = "off" });
            var plan = AtlasOutputPipeline.Resolve(job, WithBaseColor());
            Assert.AreEqual("off", plan.Modes["color"].Id);
            Assert.IsEmpty(plan.Maps);
            Assert.IsNull(plan.Output.Surface.BaseColor.Map);
        }

        [Test]
        public void FeatureSettingsFindTheirSourcesByMaterial()
        {
            var a = AtlasTestMaterials.Standard();
            var b = AtlasTestMaterials.Standard();
            try
            {
                var config = new AtlasOutputConfiguration();
                config.features.Add(new AtlasFeatureSetting { featureId = "color", mode = "off", referenceSource = b, disabledSources = { a } });
                var job = new AtlasGeneratorJob();
                AtlasOutputPipeline.AddChoices(config, new[] { a, b }, job);
                var choice = job.Features.Single();
                Assert.AreEqual("off", choice.ModeId);
                Assert.AreEqual(1, choice.ReferenceSource);
                CollectionAssert.AreEqual(new[] { 0 }, choice.DisabledSources.ToArray());
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void EveryOutputShaderHasAMainBaseColorTexture()
        {
            foreach (var shader in AtlasShaderRegistry.OutputShaders)
            {
                var texture = shader.Capabilities.TextureFor(AtlasVirtualMap.BaseColor);
                Assert.NotNull(texture, shader.DisplayName);
                Assert.AreEqual(AtlasTextureSizeMode.Main, texture.SizeMode, shader.DisplayName);
            }
        }

        [Test]
        public void AutoSetupResetsModesAndKeepsTheOtherChoices()
        {
            var config = new AtlasOutputConfiguration();
            config.features.Add(new AtlasFeatureSetting { featureId = "color", mode = "off" });
            AtlasOutputAutoSetup.ResetModes(config);
            Assert.AreEqual("", config.Feature("color").mode);
        }
    }
}
