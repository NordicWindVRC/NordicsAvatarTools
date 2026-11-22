using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Live global masks (ATLAS_OUTPUT.md, "Masks"): parts of a mask that change live kept live
    // in a global mask channel.
    public sealed class AtlasLiveMaskTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private AtlasTextureReference Gray(float value)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
            texture.SetPixels(Enumerable.Repeat(new Color(value, value, value, 1f), 16).ToArray());
            texture.Apply();
            created.Add(texture);
            return new AtlasTextureReference { Texture = texture, Interpretation = AtlasTextureInterpretation.Data };
        }

        private static AtlasOutputCapabilities Poiyomi => AtlasShaderRegistry.Output("poiyomi10").Capabilities;
        private static AtlasOutputCapabilities ToonStandard => AtlasShaderRegistry.Output("mobiletoon").Capabilities;

        private static AtlasGeneratorJob Job(params AtlasUnifiedMaterial[] materials)
        {
            var job = new AtlasGeneratorJob();
            for (int i = 0; i < materials.Length; i++)
                job.Sources.Add(new AtlasGeneratorSource
                {
                    Material = materials[i], UVRegion = new Rect(i / (float)materials.Length, 0f, (materials.Length - i) / (float)materials.Length, 1f)
                });
            return job;
        }

        // Channel 0: a 0.5 texture, then only on back faces (live: 0 as normally seen).
        private AtlasUnifiedMaterial OnBackFaces(AtlasMaskBusStepKind kind = AtlasMaskBusStepKind.Faces)
        {
            var material = new AtlasUnifiedMaterial();
            material.Lighting.Family = AtlasLightingFamily.PhysicallyBased;
            material.MaskBus.Steps.Add(new AtlasMaskBusStep { Channel = 0, Kind = AtlasMaskBusStepKind.Texture, Mask = new AtlasMask { Map = Gray(0.5f) } });
            material.MaskBus.Steps.Add(kind == AtlasMaskBusStepKind.Faces
                ? new AtlasMaskBusStep { Channel = 0, Kind = kind, Faces = AtlasFaces.Back }
                : new AtlasMaskBusStep { Channel = 0, Kind = kind, Inside = true });
            return material;
        }

        private static float Value(NordicsTextureWrapper map, int channel = 0)
        {
            using (map) return map.GetPixel(1, 1)[channel];
        }

        private static float Evaluate(AtlasMapRequest request, AtlasGeneratorJob job, int index, int channel = 0) =>
            Value(request.Evaluate(index, job.Sources[index].Material, 4, 4), channel);

        [Test]
        public void ABusChannelEvaluatesAsItIsAtZeroOrWithoutItsLiveSteps()
        {
            var material = OnBackFaces();
            Assert.AreEqual(0f, Value(AtlasMaskBusEvaluator.Channel(material, 0, 4, 4)), 1e-4f, "front faces: 0");
            using (AtlasMaskBusEvaluator.View(material, new[] { 0 }, AtlasBusView.Prefix))
                Assert.AreEqual(0.5f, Value(AtlasMaskBusEvaluator.Channel(material, 0, 4, 4)), 1e-4f, "without the live step");
            using (AtlasMaskBusEvaluator.View(material, new[] { 0 }, AtlasBusView.Zero))
                Assert.AreEqual(0f, Value(AtlasMaskBusEvaluator.Channel(material, 0, 4, 4)), 1e-4f);
            Assert.AreEqual(0f, Value(AtlasMaskBusEvaluator.Channel(material, 0, 4, 4)), 1e-4f, "the view ends");

            var caps = Poiyomi;
            Assert.AreEqual(1, AtlasLiveMasks.Tail(material, 0, caps).Count);
            var replacing = OnBackFaces();
            replacing.MaskBus.Steps.Add(new AtlasMaskBusStep { Channel = 0, Kind = AtlasMaskBusStepKind.Offset, Amount = 0.1f });
            Assert.IsNull(AtlasLiveMasks.Tail(replacing, 0, caps), "a step after the live ones that isn't live");
        }

        [Test]
        public void ALiveMetallicKeepsItsOwnPartAndTheRestInAChannel()
        {
            var live = OnBackFaces();
            live.Surface.Response.Metallic.Constant = 0.8f;
            live.Surface.Response.Metallic.Bus = 0;
            var plain = new AtlasUnifiedMaterial();
            plain.Lighting.Family = AtlasLightingFamily.PhysicallyBased;
            plain.Surface.Response.Metallic.Constant = 0.3f;
            var job = Job(live, plain);
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var metallic = plan.Output.Surface.Response.Metallic;
            Assert.GreaterOrEqual(metallic.Bus, 0, string.Join(" | ", plan.Notes));
            Assert.AreEqual(AtlasMaskBlend.Add, metallic.BusBlend);
            var own = plan.Maps.Single(m => m.Map == AtlasVirtualMap.Metallic);
            Assert.AreEqual(0f, Evaluate(own, job, 0), 1e-4f, "0.8 × the channel at 0");
            Assert.AreEqual(0.3f, Evaluate(own, job, 1), 1e-4f);
            var channel = plan.Maps.Single(m => m.Map == AtlasMaskAllocator.Places[metallic.Bus]);
            Assert.AreEqual(0.4f, Evaluate(channel, job, 0), 1e-4f, "0.8 × 0.5, before the back face step");
            Assert.AreEqual(0f, Evaluate(channel, job, 1), 1e-4f);
            var steps = plan.Output.MaskBus.Steps.Where(s => s.Channel == metallic.Bus).ToList();
            Assert.AreEqual(AtlasMaskBusStepKind.Texture, steps[0].Kind);
            Assert.AreEqual(AtlasMaskBusStepKind.Faces, steps[1].Kind);
            Assert.AreEqual(AtlasFaces.Back, steps[1].Faces);
            Assert.IsFalse(plan.Notes.Any(n => n.StartsWith("Global masks that change live")), string.Join(" | ", plan.Notes));

            var toon = AtlasOutputPipeline.Resolve(job, ToonStandard);
            Assert.Less(toon.Output.Surface.Response.Metallic.Bus, 0, "Toon Standard has no global masks");
            Assert.IsTrue(toon.Notes.Any(n => n.StartsWith("Global masks that change live")));
        }

        [Test]
        public void DifferentLiveSetupsKeepOneLiveWhichTheUserPicks()
        {
            var backFaces = OnBackFaces();
            backFaces.Surface.Response.Metallic.Constant = 1f;
            backFaces.Surface.Response.Metallic.Bus = 0;
            var mirror = OnBackFaces(AtlasMaskBusStepKind.Mirror);
            mirror.Surface.Response.Metallic.Constant = 1f;
            mirror.Surface.Response.Metallic.Bus = 0;
            var job = Job(backFaces, mirror);
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            CollectionAssert.AreEqual(new[] { 0, 1 }, plan.LiveSetups["specular"]);
            int channel = plan.Output.Surface.Response.Metallic.Bus;
            Assert.AreEqual(AtlasMaskBusStepKind.Faces, plan.Output.MaskBus.Steps.Last(s => s.Channel == channel).Kind, "the larger area's");
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("differ from the ones kept live")), string.Join(" | ", plan.Notes));

            var choice = new AtlasFeatureChoice { FeatureId = "specular" };
            choice.Values[AtlasLiveMasks.LiveFromId] = (2f, "");
            job.Features.Add(choice);
            plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            channel = plan.Output.Surface.Response.Metallic.Bus;
            Assert.AreEqual(AtlasMaskBusStepKind.Mirror, plan.Output.MaskBus.Steps.Last(s => s.Channel == channel).Kind, "the chosen one's");
        }

        [Test]
        public void AlphaReadsItsGlobalMaskAfterTheOffsetAndKeepsItLive()
        {
            var material = OnBackFaces();
            material.Surface.BaseColor.Color = new Color(1f, 1f, 1f, 0.6f);
            material.Surface.Alpha.Offset = 0.2f;
            material.Surface.Alpha.Bus = 0;
            material.Render.SurfaceMode = AtlasSurfaceMode.Fade;
            using (AtlasMaskBusEvaluator.View(material, new[] { 0 }, AtlasBusView.Prefix))
                Assert.AreEqual(0.4f, Value(AtlasMapEvaluator.Alpha(material, 4, 4, new AtlasAlphaConversion())), 1e-4f, "(0.6 + 0.2) × 0.5");

            var job = Job(material, new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var alpha = plan.Output.Surface.Alpha;
            Assert.GreaterOrEqual(alpha.Bus, 0, string.Join(" | ", plan.Notes));
            Assert.AreEqual(AtlasMaskBlend.Add, alpha.BusBlend);
            var own = plan.Maps.Single(m => m.Map == AtlasVirtualMap.Alpha);
            Assert.AreEqual(0f, Evaluate(own, job, 0), 1e-4f, "the channel at 0");
            Assert.AreEqual(1f, Evaluate(own, job, 1), 1e-4f);
            Assert.AreEqual(0.4f, Evaluate(plan.Maps.Single(m => m.Map == AtlasMaskAllocator.Places[alpha.Bus]), job, 0), 1e-4f);
        }

        [Test]
        public void LiveEmissionGoesIntoASecondEmissionShownThroughItsChannel()
        {
            var material = OnBackFaces(AtlasMaskBusStepKind.Mirror);
            var layer = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission, Mask = new AtlasMask { Bus = 0, BusBlend = AtlasMaskBlend.Multiply } };
            layer.Source.Color = Color.red;
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, 1f));
            material.Layers.Add(layer);
            var job = Job(material, new AtlasUnifiedMaterial());
            var plan = AtlasOutputPipeline.Resolve(job, Poiyomi);
            var live = AtlasEmissionFeature.LiveOf(plan.Output);
            Assert.IsNotNull(live, string.Join(" | ", plan.Notes));
            Assert.GreaterOrEqual(live.Mask.Bus, 8, "a channel past the mask places");
            var steps = plan.Output.MaskBus.Steps.Where(s => s.Channel == live.Mask.Bus).ToList();
            Assert.AreEqual(AtlasMaskBusStepKind.Mirror, steps.Single().Kind, "1, then the mirror step");
            var own = plan.Maps.Single(m => m.Map == AtlasVirtualMap.Emission);
            Assert.AreEqual(0f, Evaluate(own, job, 0), 1e-4f, "the channel at 0");
            var difference = plan.Maps.Single(m => m.Map == AtlasVirtualMap.EmissionLive);
            Assert.AreEqual(0.5f, Evaluate(difference, job, 0), 1e-4f, "red × 0.5");
            Assert.AreEqual(0f, Evaluate(difference, job, 0, 1), 1e-4f);
            Assert.AreEqual(0f, Evaluate(difference, job, 1), 1e-4f);
        }

        // Poiyomi 11 has global mask contacts, so a touch stays live there: the writer sets
        // them and the parser reads them back. Poiyomi 10 has none.
        [Test]
        public void PoiyomiElevenKeepsContactsLive()
        {
            var ten = AtlasShaderRegistry.Output("poiyomi10");
            var shader = AtlasShaderRegistry.Output("poiyomi11");
            Assert.IsFalse(ten.Capabilities.LiveMaskSteps.Contains(AtlasMaskBusStepKind.Contact));
            Assert.IsTrue(shader.Capabilities.LiveMaskSteps.Contains(AtlasMaskBusStepKind.Contact));
            Assert.IsTrue(shader.Capabilities.LiveMaskSteps.Contains(AtlasMaskBusStepKind.Mirror), "and what 10 keeps live");
            Assert.Greater(shader.Rank, ten.Rank, "the newer version is picked first");
            Assert.AreEqual("poiyomi11", shader.Capabilities.ShaderId);
            if (shader.FindShader() == null) Assert.Ignore("Poiyomi Toon 11 isn't installed.");
            Assert.AreEqual(11, AtlasPoiyomiVersionDetector.DetectFromMetadata(shader.FindShader().name,
                UnityEditor.AssetDatabase.GetAssetPath(shader.FindShader()), AtlasPoiyomiVersionDetector.GetShaderMasterLabel(shader.FindShader())));

            var output = new AtlasUnifiedMaterial();
            var contact = new AtlasContact
            {
                People = AtlasContactPeople.Others, InnerRadius = 0.1f, OuterRadius = 0.3f, ScaleWithToucher = true, AroundTouch = false, Invert = true
            };
            contact.Tags[1] = 5;
            output.MaskBus.Steps.Add(new AtlasMaskBusStep { Channel = 9, Kind = AtlasMaskBusStepKind.Contact, Contact = contact, Blend = AtlasMaskBlend.Multiply });
            var material = new Material(shader.FindShader());
            created.Add(material);
            shader.Write(output, material);
            Assert.IsTrue(material.IsKeywordEnabled("POI_GLOBALMASK_CONTACTS"));
            var step = AtlasShaderRegistry.Detect(material).Parse(material).MaskBus.Steps.Single(s => s.Kind == AtlasMaskBusStepKind.Contact);
            Assert.AreEqual(9, step.Channel);
            Assert.AreEqual(AtlasMaskBlend.Multiply, step.Blend);
            Assert.AreEqual(AtlasContactPeople.Others, step.Contact.People);
            Assert.AreEqual(0.1f, step.Contact.InnerRadius, 1e-5f);
            Assert.AreEqual(0.3f, step.Contact.OuterRadius, 1e-5f);
            Assert.IsTrue(step.Contact.ScaleWithToucher);
            Assert.IsFalse(step.Contact.AroundTouch);
            Assert.IsTrue(step.Contact.Invert);
            Assert.AreEqual(5, step.Contact.Tags[1]);

            var without = new Material(shader.FindShader());
            created.Add(without);
            shader.Write(new AtlasUnifiedMaterial(), without);
            Assert.IsFalse(AtlasShaderRegistry.Detect(without).Parse(without).MaskBus.Steps.Any(s => s.Kind == AtlasMaskBusStepKind.Contact), "none");
        }

        [Test]
        public void TheWriterSetsTheGlobalMasksTheParserReadsBack()
        {
            var shader = AtlasShaderRegistry.Output("poiyomi10");
            if (shader.FindShader() == null) return;
            var output = new AtlasUnifiedMaterial();
            var masks = Gray(0.5f);
            output.MaskBus.Steps.Add(new AtlasMaskBusStep { Channel = 1, Kind = AtlasMaskBusStepKind.Texture, Mask = new AtlasMask { Map = masks, Channel = AtlasChannel.Green } });
            output.MaskBus.Steps.Add(new AtlasMaskBusStep { Channel = 1, Kind = AtlasMaskBusStepKind.Faces, Faces = AtlasFaces.Back });
            output.MaskBus.Steps.Add(new AtlasMaskBusStep { Channel = 8, Kind = AtlasMaskBusStepKind.Camera, Inside = true });
            output.MaskBus.Steps.Add(new AtlasMaskBusStep
            {
                Channel = 9, Kind = AtlasMaskBusStepKind.Distance, Min = 1f, Max = 3f, MinValue = 0.2f, MaxValue = 1f, Blend = AtlasMaskBlend.Multiply
            });
            var response = output.Surface.Response;
            response.Metallic.Constant = 1f;
            response.Metallic.Map = masks;
            AtlasLiveMasks.Read(response.Metallic, 1);
            var live = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission, Mask = new AtlasMask { Bus = 8, BusBlend = AtlasMaskBlend.Multiply } };
            live.Source.Map = masks;
            live.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, 1f));
            var first = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
            first.Source.Map = masks;
            first.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, 1f));
            output.Layers.Add(first);
            output.Layers.Add(live);
            var material = new Material(shader.FindShader());
            created.Add(material);
            shader.Write(output, material);
            var parsed = AtlasShaderRegistry.Detect(material).Parse(material);
            var steps = parsed.MaskBus.Steps;
            Assert.AreSame(masks.Texture, steps.First(s => s.Kind == AtlasMaskBusStepKind.Texture && s.Channel == 1).Mask.Map.Texture, "Global Mask Texture 1, green");
            Assert.AreEqual(AtlasFaces.Back, steps.Single(s => s.Kind == AtlasMaskBusStepKind.Faces).Faces);
            Assert.AreEqual(1, steps.Single(s => s.Kind == AtlasMaskBusStepKind.Faces).Channel);
            var camera = steps.Single(s => s.Kind == AtlasMaskBusStepKind.Camera);
            Assert.AreEqual(8, camera.Channel);
            Assert.IsTrue(camera.Inside);
            var distance = steps.Single(s => s.Kind == AtlasMaskBusStepKind.Distance);
            Assert.AreEqual(9, distance.Channel);
            Assert.AreEqual(3f, distance.Max, 1e-4f);
            Assert.AreEqual(0.2f, distance.MinValue, 1e-4f);
            Assert.AreEqual(AtlasMaskBlend.Multiply, distance.Blend);
            Assert.AreEqual(1, parsed.Surface.Response.Metallic.Bus);
            Assert.AreEqual(AtlasMaskBlend.Add, parsed.Surface.Response.Metallic.BusBlend);
            var emission = parsed.Layers.OfType<AtlasSurfaceLayer>().Where(l => l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission)).ToList();
            Assert.AreEqual(2, emission.Count, "emission 0 and 1");
            Assert.AreEqual(8, emission[1].Mask.Bus);
            Assert.AreEqual(AtlasMaskBlend.Multiply, emission[1].Mask.BusBlend);
        }
    }
}
