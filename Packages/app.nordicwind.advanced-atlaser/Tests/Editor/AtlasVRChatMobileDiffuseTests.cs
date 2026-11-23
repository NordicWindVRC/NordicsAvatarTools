using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // VRChat Mobile Diffuse and Bumped Diffuse as inputs (UNIFIED_MATERIAL_VRCHAT_MOBILE.md), and
    // Diffuse as an output with one color texture (ATLAS_OUTPUT.md, "Output shaders").
    public sealed class AtlasVRChatMobileDiffuseTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private static AtlasOutputCapabilities Diffuse => AtlasShaderRegistry.Output("mobilediffuse").Capabilities;
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

        private static AtlasUnifiedMaterial Colored(Color color)
        {
            var material = new AtlasUnifiedMaterial();
            material.Surface.BaseColor.Color = color;
            return material;
        }

        private static string Notes(AtlasOutputPlan plan) => string.Join(" | ", plan.Notes);

        // ---- Input ----

        [Test]
        public void AnUntouchedMaterialIsTextureTimesVertexColorLitByLambert()
        {
            var material = AtlasTestMaterials.Standard();
            var texture = new Texture2D(4, 4);
            created.Add(material);
            created.Add(texture);
            // What a material switched from another shader still holds isn't Diffuse's.
            material.SetTexture("_BumpMap", texture);
            AtlasUnifiedMaterial unified;
            using (new AtlasTestMaterials.SavedValuesOnly())
                unified = new AtlasVRChatMobileDiffuseParser().Parse(material);
            Assert.IsNull(unified.Surface.BaseColor.Map);
            Assert.AreEqual(Color.white, unified.Surface.BaseColor.Color);
            Assert.IsTrue(unified.Surface.VertexColor.MultiplyColor);
            Assert.IsFalse(unified.Surface.VertexColor.SrgbData);
            Assert.AreEqual(AtlasAlphaSource.None, unified.Surface.Alpha.Source);
            Assert.IsNull(unified.Surface.Normal.Map, "Diffuse has no normal map");
            Assert.IsFalse(unified.Surface.Response.Highlights.Enabled);
            Assert.IsFalse(unified.Surface.Response.Reflections.Enabled);
            Assert.AreEqual(AtlasLightingFamily.PhysicallyBased, unified.Lighting.Family);
            Assert.AreEqual(AtlasDiffuseModel.Lambert, unified.Lighting.Diffuse);
            Assert.AreEqual(AtlasAmbientModel.Directional, unified.Lighting.Ambient);
            Assert.AreEqual(0f, unified.Lighting.ReceiveShadows);
            Assert.AreEqual(0f, unified.Lighting.Contributions.AdditionalLights);
            Assert.AreEqual(AtlasSurfaceMode.Opaque, unified.Render.SurfaceMode);
            Assert.AreEqual(AtlasCull.Back, unified.Render.Pass.Cull);
            Assert.AreEqual(AtlasShaderFamilies.VRChat, new AtlasVRChatMobileDiffuseParser().Family);
        }

        [TestCase("VRChat/Mobile/Diffuse", false)]
        [TestCase("VRChat/Mobile/Bumped Diffuse", true)]
        public void TheInstalledShadersAreDetected(string name, bool bumped)
        {
            var shader = Shader.Find(name);
            if (shader == null) Assert.Ignore(name + " is not installed.");
            var material = new Material(shader);
            var texture = new Texture2D(4, 4);
            var normal = new Texture2D(4, 4);
            created.AddRange(new Object[] { material, texture, normal });
            material.SetTexture("_MainTex", texture);
            material.SetTextureScale("_MainTex", new Vector2(2f, 3f));
            if (bumped) material.SetTexture("_BumpMap", normal);
            var parser = AtlasShaderRegistry.Detect(material);
            Assert.IsInstanceOf<AtlasVRChatMobileDiffuseParser>(parser);
            var unified = parser.Parse(material);
            Assert.AreSame(texture, unified.Surface.BaseColor.Map.Texture);
            Assert.AreEqual(new Vector2(2f, 3f), unified.Surface.BaseColor.Map.Tiling);
            Assert.AreEqual(bumped, unified.Surface.Normal.Map != null);
            if (bumped)
            {
                Assert.AreSame(normal, unified.Surface.Normal.Map.Texture);
                Assert.AreEqual(new Vector2(2f, 3f), unified.Surface.Normal.Map.Tiling, "on the texture's tiling");
                Assert.AreEqual(1f, unified.Surface.Normal.Strength, 1e-6f);
            }
            Assert.IsFalse(unified.Issues.Exists(i => i.IsWarning));
        }

        // ---- Output ----

        [Test]
        public void DiffuseIsAnOutputWithOneColorTexture()
        {
            var shader = AtlasShaderRegistry.Output("mobilediffuse");
            Assert.AreEqual(AtlasShaderFamilies.VRChat, shader.Family);
            Assert.Less(shader.Rank, AtlasShaderRegistry.Output("mobiletoon").Rank, "Toon Standard is picked first");
            var capabilities = shader.Capabilities;
            Assert.AreEqual(1, capabilities.Textures.Count);
            Assert.IsTrue(capabilities.Provides(AtlasVirtualMap.BaseColor));
            Assert.IsFalse(capabilities.BaseColorTint);
            Assert.IsTrue((capabilities.VertexColors & AtlasVertexColorSupport.Always) != 0);
            Assert.IsTrue(ToonStandard.BaseColorTint);

            // What a baked mode puts into the color is kept that way; what needs a control of
            // its own isn't offered.
            string Mode(string feature) => AtlasOutputFeatures.Effective(AtlasOutputFeatures.Find(feature), "", capabilities)?.Id;
            Assert.AreEqual("native", Mode("color"));
            Assert.AreEqual("baked", Mode("normal"));
            Assert.AreEqual("baked", Mode("occlusion"));
            Assert.AreEqual("average", Mode(AtlasMatcapFeature.FeatureId), "its own lighting shades the form");
            Assert.AreEqual("off", Mode("emission"), "drawn into the color only when chosen");
            Assert.IsNull(Mode("specular"));
            Assert.IsNull(Mode(AtlasRimFeature.FeatureId));
        }

        // An output without a color of its own gets every source's color in the texture, also
        // one all sources share.
        [Test]
        public void EveryColorGoesIntoTheTexture()
        {
            var gray = new Color(0.5f, 0.5f, 0.5f, 1f);
            var job = Job(Colored(gray), Colored(gray));
            var shared = AtlasOutputPipeline.Resolve(job, ToonStandard);
            Assert.AreEqual(0.5f, shared.Output.Surface.BaseColor.Color.r, 1e-4f, "an output with a color keeps the shared one there");

            var plan = AtlasOutputPipeline.Resolve(job, Diffuse);
            Assert.AreEqual(Color.white, plan.Output.Surface.BaseColor.Color);
            var request = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor);
            Assert.IsNull(request.Carry, "nothing takes what the texture can't hold");
            using (var map = request.Evaluate(0, job.Sources[0].Material, 4, 4))
                Assert.AreEqual(0.5f, map.GetPixel(1, 1).r, 1e-3f);
            Assert.IsFalse(plan.Notes.Any(n => n.Contains("cut off")), Notes(plan));

            var bright = AtlasOutputPipeline.Resolve(Job(Colored(new Color(2f, 1f, 1f, 1f)), Colored(gray)), Diffuse);
            StringAssert.Contains("colors brighter than 1 are cut off at 1 (source 1)", Notes(bright));
        }

        private static AtlasUnifiedMaterial Glowing(Color baseColor, Color emission, float strength)
        {
            var material = Colored(baseColor);
            var glow = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
            glow.Source.Color = emission;
            glow.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, strength));
            material.Layers.Add(glow);
            return material;
        }

        [Test]
        public void WhatTheShaderHasNoControlForIsLeftOutAndListed()
        {
            var material = Colored(Color.white);
            var rim = new AtlasAngleLayer
            {
                Placement = AtlasPlacement.Surface, Profile = AtlasAngleProfile.Step, StepBase = AtlasAngleBase.Facing,
                Falling = true, Power = 1f, Low = 0.2f, High = 0.6f, SourceAlphaIsOpacity = true
            };
            rim.Color.Color = Color.red;
            material.Layers.Add(rim);
            var plan = AtlasOutputPipeline.Resolve(Job(material), Diffuse);
            StringAssert.Contains("The output shader has no rim lighting", Notes(plan));
            Assert.IsEmpty(plan.Output.Layers);
            Assert.IsTrue(plan.Maps.All(m => m.Map == AtlasVirtualMap.BaseColor), "one map");
        }

        // Emission has no place on an output without an emission map. It is off there until
        // the user chooses to have it drawn into the base color, where it shows under light.
        [Test]
        public void EmissionIsDrawnIntoTheColorOnlyWhenChosen()
        {
            var gray = new Color(0.25f, 0.25f, 0.25f, 1f);
            var job = Job(Glowing(gray, new Color(0.5f, 0f, 0f, 1f), 1f), Glowing(gray, Color.white, 3f), Colored(gray));
            var plan = AtlasOutputPipeline.Resolve(job, Diffuse);
            Assert.AreEqual("off", plan.Modes["emission"].Id);
            StringAssert.Contains("Emission is off", Notes(plan));
            StringAssert.Contains("\"Drawn into the color\" is used only when chosen (sources 1, 2)", Notes(plan));
            var request = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor);
            using (var map = request.Evaluate(0, job.Sources[0].Material, 4, 4))
                Assert.AreEqual(0.25f, map.GetPixel(1, 1).r, 1e-3f, "off: the base color alone");

            // Turned off by the user: nothing to say.
            job.Features.Add(new AtlasFeatureChoice { FeatureId = "emission", ModeId = "off" });
            Assert.IsFalse(AtlasOutputPipeline.Resolve(job, Diffuse).Notes.Any(n => n.StartsWith("Emission is off")));

            job.Features.Clear();
            job.Features.Add(new AtlasFeatureChoice { FeatureId = "emission", ModeId = "color" });
            plan = AtlasOutputPipeline.Resolve(job, Diffuse);
            Assert.AreEqual("color", plan.Modes["emission"].Id);
            StringAssert.Contains("is drawn into the base color", Notes(plan));
            Assert.IsEmpty(plan.Output.Layers, "no emission layer: it is part of the color");
            request = plan.Maps.Single(m => m.Map == AtlasVirtualMap.BaseColor);
            using (var red = request.Evaluate(0, job.Sources[0].Material, 4, 4))
            using (var bright = request.Evaluate(1, job.Sources[1].Material, 4, 4))
            using (var none = request.Evaluate(2, job.Sources[2].Material, 4, 4))
            {
                Assert.AreEqual(0.75f, red.GetPixel(1, 1).r, 1e-3f, "the base color + the emission");
                Assert.AreEqual(0.25f, red.GetPixel(1, 1).g, 1e-3f);
                Assert.AreEqual(1f, bright.GetPixel(1, 1).r, 1e-3f, "what the texture can't hold stops at 1");
                Assert.AreEqual(0.25f, none.GetPixel(1, 1).r, 1e-3f, "a source without emission keeps its color");
            }

            // An output with an emission map keeps it there; the mode isn't offered.
            Assert.AreEqual("native", AtlasOutputPipeline.Resolve(job, ToonStandard).Modes["emission"].Id);
        }

        // The shader multiplies by the mesh's vertex colors whatever the sources did, so a
        // source that didn't use its colors gets white ones; a source that did keeps them.
        [Test]
        public void SourcesThatDontUseTheirVertexColorsGetWhiteOnes()
        {
            var plain = new AtlasUnifiedMaterial();
            var job = Job(plain, new AtlasUnifiedMaterial());
            var painted = new Color(0.2f, 0.4f, 0.6f, 0.5f);
            var plan = AtlasOutputPipeline.Resolve(job, Diffuse);
            Assert.AreEqual(2, plan.VertexColors.Count);
            Assert.AreEqual(new Color(1f, 1f, 1f, 0.5f), plan.VertexColors[0](painted), "white; the alpha isn't read and stays");
            Assert.IsEmpty(AtlasOutputPipeline.Resolve(job, ToonStandard).VertexColors, "an output with a switch turns it off instead");

            var colored = new AtlasUnifiedMaterial();
            colored.Surface.VertexColor.MultiplyColor = true;
            var mixed = AtlasOutputPipeline.Resolve(Job(colored, plain), Diffuse);
            Assert.IsTrue(mixed.Output.Surface.VertexColor.MultiplyColor);
            Assert.AreEqual(painted, mixed.VertexColors[0](painted), "used as stored: kept");
            Assert.AreEqual(1f, mixed.VertexColors[1](painted).g, 1e-6f);
        }

        // Colors that all come out white are what a mesh without colors gives, so none are stored.
        [Test]
        public void AMeshWhoseColorsAllTurnWhiteStoresNone()
        {
            var source = AtlasTestMaterials.Standard();
            var output = AtlasTestMaterials.Standard();
            created.AddRange(new Object[] { source, output });
            var mesh = new Mesh();
            created.Add(mesh);
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.uv = new[] { new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.1f, 0.9f) };
            mesh.colors = Enumerable.Repeat(new Color(0.2f, 0.4f, 0.6f, 1f), 3).ToArray();
            mesh.triangles = new[] { 0, 1, 2 };
            var targets = new Dictionary<Material, AtlasRemapTarget>
            {
                [source] = new AtlasRemapTarget { Region = new Rect(0f, 0f, 1f, 1f), Output = output, VertexColor = color => new Color(1f, 1f, 1f, color.a) }
            };
            var result = AtlasMeshRemapper.Remap(mesh, new[] { source }, targets);
            created.Add(result.Mesh);
            Assert.IsFalse(result.Mesh.HasVertexAttribute(VertexAttribute.Color));
        }

        [Test]
        public void TheWriterSetsTheTextureAndTheKeptLightingIsTheShaders()
        {
            var shader = AtlasShaderRegistry.Output("mobilediffuse");
            if (shader.FindShader() == null) Assert.Ignore("VRChat Mobile Diffuse is not installed.");
            var texture = new Texture2D(4, 4);
            created.Add(texture);
            var output = new AtlasUnifiedMaterial();
            output.Surface.BaseColor.Map = new AtlasTextureReference { Texture = texture };
            var material = new Material(shader.FindShader());
            created.Add(material);
            shader.Write(output, material);
            Assert.AreSame(texture, material.GetTexture("_MainTex"));
            Assert.AreEqual(2000, material.renderQueue);
            var parsed = AtlasShaderRegistry.Detect(material).Parse(material);
            Assert.AreSame(texture, parsed.Surface.BaseColor.Map?.Texture);
            Assert.IsTrue(parsed.Surface.VertexColor.MultiplyColor);

            // Whatever lighting the atlas chose, the shader shows its own, and says which
            // settings it couldn't take.
            var toon = new AtlasUnifiedMaterial();
            toon.Lighting.Family = AtlasLightingFamily.RampToon;
            using (var kept = AtlasKeptLighting.For(shader))
            {
                Assert.IsNotNull(kept);
                var lighting = kept.Of(toon.Lighting);
                Assert.AreEqual(AtlasLightingFamily.PhysicallyBased, lighting.Family);
                Assert.AreEqual(AtlasDiffuseModel.Lambert, lighting.Diffuse);
            }
            StringAssert.Contains("can't set these lighting settings the same way", string.Join(" ", AtlasLightingFeature.Unkept(toon, material)));

            // A plan resolves against the shader end to end.
            var plan = AtlasOutputPipeline.Resolve(Job(Colored(new Color(0.5f, 0.5f, 0.5f, 1f)), toon), shader);
            Assert.IsNotNull(plan.KeptLighting);
            Assert.AreEqual(AtlasLightingFamily.PhysicallyBased, plan.KeptLighting.Family);
        }
    }
}
