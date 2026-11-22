using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Poiyomi Pro as an output (ATLAS_OUTPUT.md, "Output shaders"): its registrations, and its
    // own sections: tone mapping, screen-space ambient occlusion and contact shadows.
    public sealed class AtlasPoiyomiProOutputTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private static AtlasOutputCapabilities Of(string id) => AtlasShaderRegistry.Output(id).Capabilities;

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

        private static string Notes(AtlasOutputPlan plan) => string.Join(" | ", plan.Notes);

        private static AtlasUnifiedMaterial ToneMapped(float accuracy)
        {
            var material = new AtlasUnifiedMaterial();
            material.ColorAdjustments.Add(new AtlasColorAdjustment
            {
                Target = AtlasAdjustmentTarget.FinalColor, Operation = AtlasColorOperation.ToneMapGt7, Amount = 1f,
                Settings = new Vector4(accuracy, 0.9f, 1.3f, 0f)
            });
            return material;
        }

        private static AtlasColorAdjustment Neutral(float retention, float hue) => new AtlasColorAdjustment
        {
            Target = AtlasAdjustmentTarget.Emission, Operation = AtlasColorOperation.NeutralEmission, Amount = 1f,
            Settings = new Vector4(retention, hue, 0f, 0f)
        };

        private static AtlasPatternLayer Occlusion(float intensity, AtlasMask mask = null) => new AtlasPatternLayer
        {
            Kind = AtlasPatternKind.AmbientOcclusion, AmbientOcclusion = new AtlasScreenOcclusion { Intensity = intensity },
            Placement = AtlasPlacement.Surface, Color = Color.black, Blend = AtlasBlend.Multiply, Mask = mask
        };

        private static AtlasPatternLayer Contact(float intensity) => new AtlasPatternLayer
        {
            Kind = AtlasPatternKind.ContactShadow, ContactShadow = new AtlasContactShadow { Intensity = intensity },
            Placement = AtlasPlacement.Surface, Color = Color.black, Blend = AtlasBlend.Multiply
        };

        private static AtlasUnifiedMaterial With(params AtlasLayer[] layers)
        {
            var material = new AtlasUnifiedMaterial();
            material.Layers.AddRange(layers);
            return material;
        }

        // Pro is its own output next to Toon of the same version, ranked just above it, with
        // what Toon of that version has and Pro's own sections.
        [Test]
        public void ProIsAnOutputOfItsOwnPerVersion()
        {
            var toon10 = AtlasShaderRegistry.Output("poiyomi10");
            var toon11 = AtlasShaderRegistry.Output("poiyomi11");
            var pro10 = AtlasShaderRegistry.Output("poiyomipro10");
            var pro11 = AtlasShaderRegistry.Output("poiyomipro11");
            Assert.AreEqual(AtlasShaderFamilies.Poiyomi, pro11.Family);
            Assert.AreEqual(101, pro10.Rank);
            Assert.AreEqual(111, pro11.Rank);
            Assert.Greater(pro10.Rank, toon10.Rank);
            Assert.Greater(toon11.Rank, pro10.Rank, "the version counts before the variant");
            Assert.AreEqual("poiyomipro11", pro11.Capabilities.ShaderId);

            foreach (var toon in new[] { toon10, toon11 })
            {
                Assert.IsFalse(toon.Capabilities.ToneMapping || toon.Capabilities.NeutralEmission);
                Assert.IsFalse(toon.Capabilities.ScreenOcclusion || toon.Capabilities.ContactShadows);
            }
            foreach (var pro in new[] { pro10, pro11 })
            {
                Assert.IsTrue(pro.Capabilities.ToneMapping);
                Assert.IsTrue(pro.Capabilities.ScreenOcclusion);
                Assert.IsTrue(pro.Capabilities.ContactShadows);
                Assert.AreEqual(4, pro.Capabilities.MatcapSlots, "and Toon's sections");
            }
            Assert.IsFalse(pro10.Capabilities.NeutralEmission, "new in 11");
            Assert.IsTrue(pro11.Capabilities.NeutralEmission);
            Assert.AreEqual(1, pro10.Capabilities.Backlights);
            Assert.AreEqual(2, pro11.Capabilities.Backlights, "what Toon 11 adds");
            Assert.IsTrue(pro11.Capabilities.LiveMaskSteps.Contains(AtlasMaskBusStepKind.Contact));

            foreach (var pro in new[] { pro10, pro11 })
            {
                var shader = pro.FindShader();
                if (shader != null) StringAssert.EndsWith("Poiyomi Pro", shader.name);
            }
        }

        // The output has tone mapping once and without a mask: the reference source's, over
        // the whole atlas, with the sources that differ or didn't have it listed.
        [Test]
        public void ToneMappingCoversTheAtlasFromTheReferenceSource()
        {
            var first = ToneMapped(0.4f);
            first.ColorAdjustments.Add(Neutral(0.25f, 0.5f));
            var job = Job(first, ToneMapped(0.8f), new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, Of("poiyomipro11"));
            var tone = AtlasToneMappingFeature.Of(plan.Output, AtlasColorOperation.ToneMapGt7);
            Assert.IsNotNull(tone, Notes(plan));
            Assert.AreEqual(0.4f, tone.Settings.x, 1e-5f, "the larger area's");
            Assert.AreNotSame(first.ColorAdjustments[0], tone);
            Assert.AreEqual(0.5f, AtlasToneMappingFeature.Of(plan.Output, AtlasColorOperation.NeutralEmission).Settings.y, 1e-5f);
            StringAssert.Contains("tone mapping of source 2 differs", Notes(plan));
            StringAssert.Contains("covers the whole atlas, source 3 included", Notes(plan));
            Assert.IsFalse(plan.Notes.Any(n => n.StartsWith("Final color adjustments")), Notes(plan));

            // Pro 10 has the tone map and no neutral surface emission.
            var ten = AtlasOutputPipeline.Resolve(job, Of("poiyomipro10"));
            Assert.IsNotNull(AtlasToneMappingFeature.Of(ten.Output, AtlasColorOperation.ToneMapGt7));
            Assert.IsNull(AtlasToneMappingFeature.Of(ten.Output, AtlasColorOperation.NeutralEmission));
            StringAssert.Contains("has no neutral surface emission", Notes(ten));

            // Toon has neither: said once, and not as a final color adjustment that isn't merged.
            var toon = AtlasOutputPipeline.Resolve(job, Of("poiyomi11"));
            Assert.IsEmpty(toon.Output.ColorAdjustments.Where(AtlasToneMappingFeature.Is));
            StringAssert.Contains("has no tone mapping", Notes(toon));
            Assert.IsFalse(toon.Notes.Any(n => n.StartsWith("Final color adjustments")), Notes(toon));
        }

        // Screen-space ambient occlusion shows where its sources had it: a mask of its own,
        // 0 on the sources without it. Its settings are the output's own copy.
        [Test]
        public void ScreenOcclusionShowsOnTheSourcesThatHadIt()
        {
            var source = Occlusion(2f);
            var job = Job(With(source), new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, Of("poiyomipro11"));
            var layer = AtlasScreenOcclusionFeature.Of(plan.Output);
            Assert.IsNotNull(layer, Notes(plan));
            Assert.AreEqual(2f, layer.AmbientOcclusion.Intensity, 1e-5f);
            Assert.AreNotSame(source.AmbientOcclusion, layer.AmbientOcclusion);
            Assert.IsNotNull(layer.Mask?.Map, "a mask place");
            var map = plan.Maps.Single(m => m.Reference == layer.Mask.Map);
            using (var on = map.Evaluate(0, job.Sources[0].Material, 4, 4))
            using (var off = map.Evaluate(1, job.Sources[1].Material, 4, 4))
            {
                Assert.AreEqual(1f, on.GetPixel(1, 1).r, 1e-4f);
                Assert.AreEqual(0f, off.GetPixel(1, 1).r, 1e-4f, "none on the second material");
            }

            StringAssert.Contains("has no screen-space ambient occlusion", Notes(AtlasOutputPipeline.Resolve(job, Of("poiyomi11"))));
        }

        // Where every source has the shadow unmasked, no mask is needed; a different setup is
        // listed, and the reference source's is used.
        [Test]
        public void ContactShadowsOnEverySourceNeedNoMask()
        {
            var job = Job(With(Contact(0.5f)), With(Contact(0.8f)));
            var plan = AtlasOutputPipeline.Resolve(job, Of("poiyomipro10"));
            var layer = AtlasContactShadowFeature.Of(plan.Output);
            Assert.IsNotNull(layer, Notes(plan));
            Assert.AreEqual(0.5f, layer.ContactShadow.Intensity, 1e-5f, "the larger area's");
            Assert.IsNull(layer.Mask?.Map);
            Assert.IsFalse(plan.Maps.Any(m => m.Map == AtlasVirtualMap.Mask1), "no mask place taken");
            StringAssert.Contains("contact shadows are taken from source 1; those of source 2 differ", Notes(plan));
            StringAssert.Contains("has no contact shadows", Notes(AtlasOutputPipeline.Resolve(job, Of("mobiletoon"))));
        }

        // The writer sets Pro's sections and the Poiyomi parser reads them back.
        [TestCase("poiyomipro10")]
        [TestCase("poiyomipro11")]
        public void TheWriterSetsProsSectionsTheParserReadsBack(string id)
        {
            var shader = AtlasShaderRegistry.Output(id);
            if (shader.FindShader() == null) Assert.Ignore(shader.DisplayName + " isn't installed.");
            bool eleven = id == "poiyomipro11";
            var texture = new Texture2D(4, 4);
            created.Add(texture);

            var output = ToneMapped(0.4f);
            output.ColorAdjustments.Add(Neutral(0.25f, 0.5f));
            var occlusion = Occlusion(1.5f, new AtlasMask
            {
                Map = new AtlasTextureReference { Texture = texture, Interpretation = AtlasTextureInterpretation.Data },
                Channel = AtlasChannel.Green, Invert = true
            });
            occlusion.Color = new Color(0.5f, 0f, 0f, 1f);
            occlusion.Blend = AtlasBlend.Darken;
            occlusion.AmbientOcclusion.Radius = 0.01f;
            occlusion.AmbientOcclusion.Quality = 5f;
            occlusion.AmbientOcclusion.NormalAmount = 0.7f;
            occlusion.AmbientOcclusion.SurfaceColorMix = 0.3f;
            occlusion.AmbientOcclusion.HideInLight = 0.6f;
            occlusion.AmbientOcclusion.QualityFalloff = new Vector2(3f, 5f);
            output.Layers.Add(occlusion);
            var contact = Contact(0.7f);
            contact.Blend = AtlasBlend.Replace;
            contact.ContactShadow.ApplyToColor = true;
            contact.ContactShadow.ApplyToLightMap = true;
            contact.ContactShadow.MaxDistance = 0.05f;
            contact.ContactShadow.CustomDirection = true;
            contact.ContactShadow.Direction = new Vector3(1f, 0f, 0f);
            contact.ContactShadow.DirectionSpace = AtlasDirectionSpace.Object;
            contact.ContactShadow.CameraFalloff = new Vector2(2f, 4f);
            contact.ContactShadow.HideInLight = 0.2f;
            output.Layers.Add(contact);

            var material = new Material(shader.FindShader());
            created.Add(material);
            shader.Write(output, material);
            var parsed = AtlasShaderRegistry.Detect(material).Parse(material);

            var tone = parsed.ColorAdjustments.Single(a => a.Operation == AtlasColorOperation.ToneMapGt7);
            Assert.AreEqual(0.4f, tone.Settings.x, 1e-4f);
            Assert.AreEqual(0.9f, tone.Settings.y, 1e-4f);
            Assert.AreEqual(1.3f, tone.Settings.z, 1e-4f);
            if (eleven)
            {
                var neutral = parsed.ColorAdjustments.Single(a => a.Operation == AtlasColorOperation.NeutralEmission);
                Assert.AreEqual(0.25f, neutral.Settings.x, 1e-4f);
                Assert.AreEqual(0.5f, neutral.Settings.y, 1e-4f);
            }

            var layers = parsed.Layers.OfType<AtlasPatternLayer>().ToList();
            var ao = layers.Single(l => l.Kind == AtlasPatternKind.AmbientOcclusion);
            Assert.AreEqual(1.5f, ao.AmbientOcclusion.Intensity, 1e-4f);
            Assert.AreEqual(0.01f, ao.AmbientOcclusion.Radius, 1e-5f);
            Assert.AreEqual(5f, ao.AmbientOcclusion.Quality, 1e-4f);
            Assert.AreEqual(0.7f, ao.AmbientOcclusion.NormalAmount, 1e-4f);
            Assert.AreEqual(0.3f, ao.AmbientOcclusion.SurfaceColorMix, 1e-4f);
            Assert.AreEqual(0.6f, ao.AmbientOcclusion.HideInLight, 1e-4f);
            Assert.AreEqual(3f, ao.AmbientOcclusion.QualityFalloff.x, 1e-4f);
            Assert.AreEqual(5f, ao.AmbientOcclusion.QualityFalloff.y, 1e-4f);
            Assert.AreEqual(0.5f, ao.Color.r, 1e-3f);
            Assert.AreEqual(AtlasBlend.Darken, ao.Blend);
            Assert.AreSame(texture, ao.Mask?.Map?.Texture);
            Assert.AreEqual(AtlasChannel.Green, ao.Mask.Channel);
            Assert.IsTrue(ao.Mask.Invert);

            var shadow = layers.Single(l => l.Kind == AtlasPatternKind.ContactShadow);
            Assert.AreEqual(0.7f, shadow.ContactShadow.Intensity, 1e-4f);
            Assert.IsTrue(shadow.ContactShadow.ApplyToColor);
            Assert.IsTrue(shadow.ContactShadow.ApplyToLightMap);
            Assert.AreEqual(0.05f, shadow.ContactShadow.MaxDistance, 1e-5f);
            Assert.IsTrue(shadow.ContactShadow.CustomDirection);
            Assert.AreEqual(1f, shadow.ContactShadow.Direction.x, 1e-4f);
            // The direction's space is new in 11.
            if (eleven) Assert.AreEqual(AtlasDirectionSpace.Object, shadow.ContactShadow.DirectionSpace);
            Assert.AreEqual(2f, shadow.ContactShadow.CameraFalloff.x, 1e-4f);
            Assert.AreEqual(4f, shadow.ContactShadow.CameraFalloff.y, 1e-4f);
            Assert.AreEqual(0.2f, shadow.ContactShadow.HideInLight, 1e-4f);
            Assert.AreEqual(AtlasBlend.Replace, shadow.Blend);
            Assert.IsNull(shadow.Mask?.Map?.Texture);
        }
    }
}
