using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // AudioLink (ATLAS_OUTPUT.md, "Feature inventory").
    public sealed class AtlasAudioLinkTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private static AtlasOutputCapabilities Poiyomi => AtlasShaderRegistry.Output("poiyomi10").Capabilities;
        private static AtlasOutputCapabilities ToonStandard => AtlasShaderRegistry.Output("mobiletoon").Capabilities;

        private static AtlasGeneratorJob Job(params AtlasUnifiedMaterial[] materials)
        {
            var job = new AtlasGeneratorJob();
            for (int i = 0; i < materials.Length; i++)
                job.Sources.Add(new AtlasGeneratorSource
                {
                    Material = materials[i], UVRegion = new Rect(0f, 0f, (materials.Length - i) / (float)materials.Length, 1f)
                });
            return job;
        }

        private static AtlasModulator Audio(string target, AtlasModulatorMode mode, AtlasAudioBand band, float min, float max, bool idle)
        {
            var modulator = new AtlasModulator { Target = target, Source = AtlasModulatorSource.Audio, Mode = mode, Min = min, Max = max };
            modulator.Audio.Band = band;
            modulator.Audio.IdleWithoutAudioLink = idle;
            return modulator;
        }

        // An emission, red at strength 1, whose strength the bass adds to (as Poiyomi's: idle
        // without AudioLink), or whose color the bass scales (as Toon Standard's).
        private static AtlasUnifiedMaterial Glowing(bool poiyomi, AtlasAudioBand band = AtlasAudioBand.Bass)
        {
            var material = new AtlasUnifiedMaterial();
            var layer = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
            layer.Source.Color = Color.red;
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, poiyomi ? AtlasBlend.Add : AtlasBlend.Add, 1f));
            material.Layers.Add(layer);
            material.Modulators.Add(poiyomi
                ? Audio("layers[0].targets[0].opacity", AtlasModulatorMode.Add, band, 0f, 2f, true)
                : Audio("layers[0].source.color", AtlasModulatorMode.Multiply, band, 0f, 1f, false));
            return material;
        }

        private static float Evaluate(AtlasOutputPlan plan, AtlasVirtualMap map, AtlasGeneratorJob job, int index)
        {
            using (var result = plan.Maps.Single(m => m.Map == map).Evaluate(index, job.Sources[index].Material, 4, 4))
                return result.GetPixel(1, 1).r;
        }

        private static string Notes(AtlasOutputPlan plan) => string.Join(" | ", plan.Notes);

        [Test]
        public void AudioThatRestsWithoutMusicIsBakedAtRest()
        {
            var poiyomi = Glowing(true);
            Assert.IsTrue(AtlasColorStack.IsStatic(poiyomi, "layers[0]"), "idle without AudioLink: at rest");
            Assert.AreEqual(1f, AtlasColorStack.EmissionStrength(poiyomi), 1e-5f);
            var toon = Glowing(false);
            Assert.IsFalse(AtlasColorStack.IsStatic(toon, "layers[0]"), "dark without AudioLink: not baked");
            Assert.AreEqual(0f, AtlasColorStack.EmissionStrength(toon), 1e-5f);
        }

        [Test]
        public void PoiyomiKeepsAnAudioEmissionInASlotOfItsOwn()
        {
            var job = Job(Glowing(true), new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var slot = AtlasEmissionFeature.CyclingOf(plan.Output).Single();
            var modulator = AtlasAudio.Find(plan.Output, slot, ".targets[0].opacity", AtlasModulatorMode.Add);
            Assert.IsNotNull(modulator, Notes(plan));
            Assert.AreEqual(2f, modulator.Max, 1e-5f);
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.EmissionCycle1, job, 0), 1e-4f, "red at rest");
            Assert.IsNull(AtlasEmissionFeature.Of(plan.Output), "nothing else glows");
        }

        // Glowing(true) with a second emission, the same but for the band of its AudioLink.
        private static AtlasUnifiedMaterial TwoBands()
        {
            var material = Glowing(true);
            var second = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
            second.Source.Color = Color.red;
            second.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, 1f));
            material.Layers.Add(second);
            material.Modulators.Add(Audio("layers[1].targets[0].opacity", AtlasModulatorMode.Add, AtlasAudioBand.Highs, 0f, 2f, true));
            return material;
        }

        [Test]
        public void SlotsWhoseMapsComeOutTheSameShareOne()
        {
            var job = Job(TwoBands(), new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var slots = AtlasEmissionFeature.CyclingOf(plan.Output);
            Assert.AreEqual(2, slots.Count, Notes(plan));
            Assert.AreSame(slots[0].Source.Map, slots[1].Source.Map, "one map");
            Assert.IsTrue(plan.Maps.Any(m => m.Map == AtlasVirtualMap.EmissionCycle1));
            Assert.IsFalse(plan.Maps.Any(m => m.Map == AtlasVirtualMap.EmissionCycle2));
            CollectionAssert.AreEquivalent(new[] { AtlasAudioBand.Bass, AtlasAudioBand.Highs },
                slots.Select(s => AtlasAudio.Find(plan.Output, s, ".targets[0].opacity", AtlasModulatorMode.Add).Audio.Band).ToArray(), "each slot keeps its band");

            // Another color: maps of their own.
            var other = TwoBands();
            ((AtlasSurfaceLayer)other.Layers[1]).Source.Color = Color.blue;
            plan = AtlasOutputPipeline.Resolve(Job(other, new AtlasUnifiedMaterial()), Poiyomi);
            Assert.IsTrue(plan.Maps.Any(m => m.Map == AtlasVirtualMap.EmissionCycle2));
        }

        [Test]
        public void TheTextureListShowsTheEmissionTexturesTheBuildMakes()
        {
            var config = new AtlasOutputConfiguration { mainTexture = { width = 1024, height = 1024 } };
            var poiyomi = AtlasOutputOverview.Build(config, Poiyomi, new Material[] { null, null }, new[] { TwoBands(), new AtlasUnifiedMaterial() });
            var ids = poiyomi.Textures.Select(t => t.Texture.Id).ToList();
            CollectionAssert.Contains(ids, "EmissionCycle1");
            CollectionAssert.DoesNotContain(ids, "EmissionCycle2", "shared");
            CollectionAssert.DoesNotContain(ids, "Emission", "nothing else glows");
            var cycle = poiyomi.Textures.Single(t => t.Texture.Id == "EmissionCycle1");
            Assert.AreEqual((512, 512), (cycle.Width, cycle.Height), "half the main texture");

            // Toon Standard: glowing only with music, in its AudioLink texture.
            var toon = AtlasOutputOverview.Build(config, ToonStandard, new Material[] { null, null }, new[] { Glowing(false), new AtlasUnifiedMaterial() });
            ids = toon.Textures.Select(t => t.Texture.Id).ToList();
            CollectionAssert.Contains(ids, "AudioLink");
            CollectionAssert.DoesNotContain(ids, "Emission");
        }

        [Test]
        public void ToonStandardKeepsTheReferenceSourcesAudioEmission()
        {
            var job = Job(Glowing(false), Glowing(false, AtlasAudioBand.Highs), new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, ToonStandard);
            var layer = AtlasAudioLinkFeature.Of(plan.Output);
            Assert.IsNotNull(layer, Notes(plan));
            Assert.AreEqual(AtlasAudioBand.Bass, AtlasAudio.Of(plan.Output, layer).Single().Audio.Band, "the larger area's");
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.AudioLinkColor, job, 0), 1e-4f, "red");
            Assert.AreEqual(0f, Evaluate(plan, AtlasVirtualMap.AudioLinkColor, job, 1), 1e-4f, "another band: not kept");
            StringAssert.Contains("differs and isn't kept", Notes(plan));
            Assert.IsNull(AtlasEmissionFeature.Of(plan.Output), "nothing glows without music");
        }

        // Red at strength 0: AudioLink adds 0.3–0.5 (bass) and multiplies by 0–1 (low mids), as
        // Poiyomi's emissions that only glow with music.
        private static AtlasUnifiedMaterial MusicOnly()
        {
            var material = new AtlasUnifiedMaterial();
            var layer = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
            layer.Source.Color = Color.red;
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, 0f));
            material.Layers.Add(layer);
            material.Modulators.Add(Audio("layers[0].targets[0].opacity", AtlasModulatorMode.Add, AtlasAudioBand.Bass, 0.3f, 0.5f, true));
            material.Modulators.Add(Audio("layers[0].targets[0].opacity", AtlasModulatorMode.Multiply, AtlasAudioBand.LowMids, 0f, 1f, true));
            return material;
        }

        [Test]
        public void AnEmissionThatOnlyGlowsWithMusicKeepsItsColor()
        {
            var job = Job(MusicOnly(), new AtlasUnifiedMaterial());
            Assert.IsTrue(new AtlasEmissionFeature().IsPresent(job.Sources[0].Material), "glows with music");
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var slot = AtlasEmissionFeature.CyclingOf(plan.Output).Single();
            Assert.AreEqual(0f, slot.Targets[0].Opacity, 1e-5f, "its own strength: AudioLink adds to it");
            Assert.AreEqual(1f, slot.Source.Color.r, 1e-5f);
            Assert.AreEqual(1f, Evaluate(plan, AtlasVirtualMap.EmissionCycle1, job, 0), 1e-4f, "red at unit strength, not black");
            Assert.AreEqual(0.5f, AtlasAudio.Find(plan.Output, slot, ".targets[0].opacity", AtlasModulatorMode.Add).Max, 1e-5f);

            var toon = AtlasOutputPipeline.Resolve(job, ToonStandard);
            Assert.IsNotNull(AtlasAudioLinkFeature.Of(toon.Output), Notes(toon));
            Assert.AreEqual(0.5f, Evaluate(toon, AtlasVirtualMap.AudioLinkColor, job, 0), 1e-4f, "(0 + 0.5) × 1");

            var shader = AtlasShaderRegistry.Output("poiyomi10");
            if (shader.FindShader() == null) return;
            var texture = new Texture2D(4, 4);
            created.Add(texture);
            var output = MusicOnly();
            ((AtlasSurfaceLayer)output.Layers[0]).Source.Map = new AtlasTextureReference { Texture = texture };
            ((AtlasSurfaceLayer)output.Layers[0]).Source.Color = Color.white;
            var material = new Material(shader.FindShader());
            created.Add(material);
            shader.Write(output, material);
            Assert.AreEqual(1f, material.GetColor("_EmissionColor2").r, 1e-3f, "not black at strength 0");
            Assert.AreEqual(0f, material.GetFloat("_EmissionStrength2"), 1e-5f);
        }

        [Test]
        public void TheWritersSetTheAudioLinkTheParsersReadBack()
        {
            var texture = new Texture2D(4, 4);
            created.Add(texture);
            var poiyomi = AtlasShaderRegistry.Output("poiyomi10");
            if (poiyomi.FindShader() != null)
            {
                var output = new AtlasUnifiedMaterial();
                var glow = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
                glow.Source.Map = new AtlasTextureReference { Texture = texture };
                glow.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, 1f));
                output.Layers.Add(glow);
                output.Modulators.Add(Audio("layers[0].targets[0].opacity", AtlasModulatorMode.Add, AtlasAudioBand.LowMids, 0.5f, 3f, true));
                var matcap = new AtlasLookupLayer { Mask = new AtlasMask(), Blend = AtlasBlend.Add, Opacity = 1f, Projection = AtlasLookupProjection.ViewCorrected };
                matcap.Source.Map = new AtlasTextureReference { Texture = texture, Space = AtlasTextureSpace.View };
                output.Layers.Add(matcap);
                output.Modulators.Add(Audio("layers[1].source.color.a", AtlasModulatorMode.Add, AtlasAudioBand.Highs, 0f, 0.5f, true));
                var material = new Material(poiyomi.FindShader());
                created.Add(material);
                poiyomi.Write(output, material);
                var parsed = AtlasShaderRegistry.Detect(material).Parse(material);
                var emission = parsed.Layers.OfType<AtlasSurfaceLayer>().Single(l => l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission));
                var strength = AtlasAudio.Of(parsed, emission).Single(m => m.Target.EndsWith(".targets[0].opacity") && m.Mode == AtlasModulatorMode.Add);
                Assert.AreEqual(AtlasAudioBand.LowMids, strength.Audio.Band, "Poiyomi emission");
                Assert.AreEqual(3f, strength.Max, 1e-4f);
                Assert.AreEqual(0.5f, strength.Min, 1e-4f);
                var parsedMatcap = parsed.Layers.OfType<AtlasLookupLayer>().First();
                var alpha = AtlasAudio.Of(parsed, parsedMatcap).Single(m => m.Target.EndsWith(".source.color.a"));
                Assert.AreEqual(AtlasAudioBand.Highs, alpha.Audio.Band, "Poiyomi matcap");
                Assert.AreEqual(0.5f, alpha.Max, 1e-4f);
            }

            var toon = AtlasShaderRegistry.Output("mobiletoon");
            if (toon.FindShader() != null)
            {
                var output = new AtlasUnifiedMaterial();
                var glow = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
                glow.Source.Map = new AtlasTextureReference { Texture = texture };
                glow.Source.Color = new Color(3f, 3f, 3f, 1f);
                glow.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, 1f));
                output.Layers.Add(glow);
                var modulator = Audio("layers[0].source.color", AtlasModulatorMode.Multiply, AtlasAudioBand.HighMids, 0f, 1f, false);
                modulator.Audio.Spread = AtlasAudioSpread.History;
                modulator.Audio.HistoryLength = 256f;
                output.Modulators.Add(modulator);
                var material = new Material(toon.FindShader());
                created.Add(material);
                toon.Write(output, material);
                var parsed = AtlasShaderRegistry.Detect(material).Parse(material);
                var layer = parsed.Layers.OfType<AtlasSurfaceLayer>().Single(l => AtlasAudio.Of(parsed, l).Count > 0);
                Assert.AreSame(texture, layer.Source.Map?.Texture, "Toon Standard");
                Assert.AreEqual(3f, layer.Source.Color.r, 1e-3f, "white × the intensity");
                var parsedModulator = AtlasAudio.Of(parsed, layer).Single();
                Assert.AreEqual(AtlasAudioBand.HighMids, parsedModulator.Audio.Band);
                Assert.AreEqual(AtlasAudioSpread.History, parsedModulator.Audio.Spread);
                Assert.AreEqual(256f, parsedModulator.Audio.HistoryLength, 1e-3f);
            }
        }
    }
}
