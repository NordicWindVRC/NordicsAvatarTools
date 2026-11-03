using System.Collections.Generic;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    public sealed class AtlasArchitectureTests
    {
        [TestCase(0,AtlasBlendOperation.Replace)]
        [TestCase(1,AtlasBlendOperation.Subtract)]
        [TestCase(2,AtlasBlendOperation.Multiply)]
        [TestCase(3,AtlasBlendOperation.Divide)]
        [TestCase(4,AtlasBlendOperation.Min)]
        [TestCase(5,AtlasBlendOperation.Max)]
        [TestCase(6,AtlasBlendOperation.Average)]
        [TestCase(7,AtlasBlendOperation.Add)]
        public void GlobalMaskBlendCodesAreNormalized(int value,AtlasBlendOperation expected)
        {
            Assert.AreEqual(expected,AtlasPoiyomiGlobalMaskParser.DecodeBlend(value));
        }
        [Test]
        public void GlobalMaskChannelMappingIsIndependent()
        {
            var resource=new AtlasGlobalMaskResource();
            Assert.NotNull(resource.MappingFor(AtlasChannel.Red));
            resource.SplitChannels=true;
            resource.ChannelMappings[1].Scale=new Vector2(2,1);
            Assert.AreEqual(2,resource.MappingFor(AtlasChannel.Green).Scale.x);
            Assert.AreEqual(1,resource.MappingFor(AtlasChannel.Blue).Scale.x);
        }
        [Test]
        public void SourceReaderUsesEvaluatedSetNotSerializedBaseList()
        {
            var owner = new GameObject("Source reader test");
            var source = new GameObject("Effective source");
            try
            {
                var renderer = source.AddComponent<SkinnedMeshRenderer>();
                var fakeMerge = owner.AddComponent<AtlasTestSourceContainer>();
                fakeMerge.renderersSet.Effective.Add(renderer);
                var method = typeof(AtlasMaterialDiscovery).GetMethod("TryReadEffectiveSources",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(method);
                var args = new object[] { fakeMerge, null, null };
                Assert.IsTrue((bool)method.Invoke(null, args), (string)args[2]);
                var resolved = (List<SkinnedMeshRenderer>)args[1];
                Assert.AreEqual(1, resolved.Count);
                Assert.AreSame(renderer, resolved[0]);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(source);
            }
        }
        [Test]
        public void DiscoveryWithoutAAOUsesOnlyLocalRendererAndDeduplicatesMaterials()
        {
            var owner = new GameObject("Discovery owner");
            var unrelated = new GameObject("Unrelated child");
            var material = new Material(Shader.Find("Standard"));
            try
            {
                unrelated.transform.SetParent(owner.transform);
                var local = owner.AddComponent<SkinnedMeshRenderer>();
                local.sharedMaterials = new[] { material, material };
                unrelated.AddComponent<SkinnedMeshRenderer>().sharedMaterial = material;
                var result = AtlasMaterialDiscovery.Discover(owner);
                Assert.IsFalse(result.HasAAO);
                Assert.AreEqual(1, result.Renderers.Count);
                Assert.AreSame(local, result.Renderers[0]);
                Assert.AreEqual(1, result.Materials.Count);
                Assert.AreSame(material, result.Materials[0]);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(material);
            }
        }
        [Test]
        public void DiscoveryHandlesMissingOwner()
        {
            var result = AtlasMaterialDiscovery.Discover(null);
            Assert.IsFalse(result.HasAAO);
            Assert.IsEmpty(result.Renderers);
            Assert.IsEmpty(result.Materials);
        }
        [Test]
        public void BlendShapeExclusionDefaultsOffAndSupportsRendererNamePairs()
        {
            var go = new GameObject("Atlaser Exclusion Test");
            try
            {
                var atlaser = go.AddComponent<NordicsAdvancedAtlaser>();
                var renderer = go.AddComponent<SkinnedMeshRenderer>();
                Assert.IsFalse(atlaser.IgnoreGeometryAffectedByBlendShape);
                Assert.NotNull(atlaser.ExcludedBlendShapes);
                Assert.AreEqual(0, atlaser.ExcludedBlendShapes.Count);
                atlaser.IgnoreGeometryAffectedByBlendShape = true;
                atlaser.ExcludedBlendShapes.Add(new AtlasExcludedBlendShape {
                    Renderer = renderer, BlendShapeName = "Hide Breasts" });
                Assert.AreSame(renderer, atlaser.ExcludedBlendShapes[0].Renderer);
                Assert.AreEqual("Hide Breasts", atlaser.ExcludedBlendShapes[0].BlendShapeName);
                Assert.AreEqual(0.001f, atlaser.ExcludedBlendShapes[0].Threshold);
                Assert.AreEqual(AtlasBlendShapeSelectionMode.AnyVertex, atlaser.ExcludedBlendShapes[0].SelectionMode);
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test]
        public void GeometryBakingDefaultsToEnabled4096OnMainComponent()
        {
            var go = new GameObject("Atlaser Test");
            try
            {
                var atlaser = go.AddComponent<NordicsAdvancedAtlaser>();
                Assert.IsTrue(atlaser.EnableGeometryBaking);
                Assert.AreEqual(4096, atlaser.GeometryBakingResolution);
                Assert.IsNull(typeof(NordicsAdvancedAtlaser).GetField("GeometryNormalPadding"));
                var output = go.AddComponent<NordicsAtlasOutput>();
                Assert.IsNull(typeof(AtlasOutputConfiguration).GetField("MaxGeometryBakeDimension"));
                Assert.NotNull(output.Configuration);
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test]
        public void OutputSettingsAreIndependentAndPreserved()
        {
            var config = new AtlasOutputConfiguration();
            var p = AtlasShaderRegistry.Output("poiyomi10");
            var m = AtlasShaderRegistry.Output("mobiletoon");
            Assert.NotNull(p); Assert.NotNull(m);
            AtlasShaderRegistry.EnsureSettings(config,p);
            config.Find("normal").Texture.Width = 2048;
            AtlasShaderRegistry.EnsureSettings(config,m);
            Assert.AreEqual(2048,config.Find("normal").Texture.Width);
            Assert.NotNull(config.Find("normal"));
            Assert.AreEqual(1, config.Settings.FindAll(x => x.Id == "normal").Count);
        }
        [Test]
        public void SwitchingShadersPreservesOneSharedNormalAndEmissionSetting()
        {
            var config = new AtlasOutputConfiguration();
            var poiyomi = AtlasShaderRegistry.Output("poiyomi10");
            var mobile = AtlasShaderRegistry.Output("mobiletoon");
            AtlasShaderRegistry.EnsureSettings(config, poiyomi);
            config.Find("normal").Texture.Width = 2048;
            config.Find("emission").Texture.Enabled = true;
            config.ShaderId = "mobiletoon";
            AtlasShaderRegistry.EnsureSettings(config, mobile);
            Assert.AreEqual(2048, config.Find("normal").Texture.Width);
            Assert.IsTrue(config.Find("emission").Texture.Enabled);
            Assert.AreEqual(1, config.Settings.FindAll(x => x.Id == "normal").Count);
            Assert.AreEqual(1, config.Settings.FindAll(x => x.Id == "emission").Count);
        }
        [Test]
        public void MainTextureSettingsAreNotResetByValidation()
        {
            var go = new GameObject("Atlas output Main setting test");
            try
            {
                var output = go.AddComponent<NordicsAtlasOutput>();
                output.Configuration.MainTexture.Enabled = false;
                output.Configuration.MainTexture.Width = 4096;
                output.Configuration.MainTexture.Height = 1024;
                // OnValidate used to overwrite this with hidden legacy 2048x2048.
                output.SendMessage("OnValidate", SendMessageOptions.DontRequireReceiver);
                Assert.IsFalse(output.Configuration.MainTexture.Enabled);
                Assert.AreEqual(4096, output.Configuration.MainTexture.Width);
                Assert.AreEqual(1024, output.Configuration.MainTexture.Height);
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test]
        public void OutputComponentHasNoLegacyMainTextureShadow()
        {
            Assert.IsNull(typeof(NordicsAtlasOutput).GetField("MainTexture",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
            Assert.IsNull(typeof(NordicsAtlasOutput).GetMethod("Migrate"));
        }
        [Test]
        public void AutoConfigureUsesUnifiedSemanticLayers()
        {
            var config = new AtlasOutputConfiguration();
            var shader = AtlasShaderRegistry.Output("poiyomi10");
            AtlasShaderRegistry.EnsureSettings(config, shader);
            var source = new AtlasUnifiedMaterial();
            source.Normals.Add(new AtlasNormalLayer { Map = new AtlasTextureSample { Texture = Texture2D.whiteTexture } });
            source.Emissions.Add(new AtlasEmissionLayer { Map = new AtlasTextureSample { Texture = Texture2D.whiteTexture } });
            shader.AutoConfigure(new List<AtlasUnifiedMaterial> { source }, config);
            Assert.IsTrue(config.Find("normal").Texture.Enabled);
            Assert.IsTrue(config.Find("emission").Texture.Enabled);
            Assert.IsFalse(config.Find("globalMasks").Texture.Enabled);
        }
        [Test]
        public void SharedAutoSetupChoosesLargestAlbedoOnly()
        {
            var small = new Texture2D(128, 256);
            var large = new Texture2D(1024, 512);
            var hugeNormal = new Texture2D(2048, 2048);
            try
            {
                var first = new AtlasUnifiedMaterial();
                first.Surface.BaseColor.Map = new AtlasTextureSample { Texture = small };
                var second = new AtlasUnifiedMaterial();
                second.Surface.BaseColor.Map = new AtlasTextureSample { Texture = large };
                second.Normals.Add(new AtlasNormalLayer { Map = new AtlasTextureSample { Texture = hugeNormal } });
                var main = new AtlasTextureDimensions { Enabled = false };
                AtlasOutputAutoSetup.SetMainTextureDimensions(new List<AtlasUnifiedMaterial> { first, second }, main);
                Assert.IsTrue(main.Enabled);
                Assert.AreEqual(1024, main.Width);
                Assert.AreEqual(512, main.Height);
            }
            finally { Object.DestroyImmediate(small); Object.DestroyImmediate(large); Object.DestroyImmediate(hugeNormal); }
        }
        [Test]
        public void StandardShaderDetectionAndMaterialParsing()
        {
            var shader = Shader.Find("Standard");
            if(shader == null) Assert.Ignore("Standard shader is not available in this project.");
            var material = new Material(shader);
            try
            {
                var parser = AtlasShaderRegistry.Detect(material);
                Assert.AreEqual("standard",parser.ParserId);
                var data = parser.Parse(material);
                Assert.AreEqual(AtlasShadingModel.StandardMetallic, data.Surface.Model);
                Assert.AreEqual(AtlasSurfaceMode.Opaque, data.Render.Mode);
                Assert.NotNull(data.Surface.BaseColor);
            }
            finally { Object.DestroyImmediate(material); }
        }

[TestCase(".poiyomi/Poiyomi Toon", "", AtlasPoiyomiVariant.Toon)]
[TestCase(".poiyomi/Poiyomi Pro", "", AtlasPoiyomiVariant.Pro)]
[TestCase("Hidden/Whatever", "Assets/Poiyomi/Shaders/Toon/Foo.shader", AtlasPoiyomiVariant.Toon)]
[TestCase("Standard", "", AtlasPoiyomiVariant.None)]
public void PoiyomiFamilyAndVariantDetection(string name, string path, AtlasPoiyomiVariant expected)
{
    Assert.AreEqual(expected, AtlasPoiyomiVersionDetector.DetectVariantFromMetadata(name, path));
}

        
[TestCase(".poiyomi/• Poiyomi Toon •", "Assets/_PoiyomiShaders/Shaders/Pro/Shaders/S_Poiyomi_Toon.shader", null, 7)]
[TestCase(".poiyomi/Poiyomi 7.3/• Poiyomi Toon •", "", null, 7)]
[TestCase(".poiyomi/Poiyomi 8.1/Poiyomi Toon", "", null, 8)]
[TestCase(".poiyomi/Old Versions/8.1/Poiyomi Toon", "", null, 8)]
[TestCase(".poiyomi/Poiyomi Toon", "Assets/_PoiyomiShaders/Shaders/9.3/Toon/Poiyomi Toon.shader", null, 9)]
[TestCase(".poiyomi/Poiyomi Toon", "Assets/_PoiyomiShaders/Shaders/10.0/Toon/Poiyomi Toon.shader", null, 10)]
[TestCase(".poiyomi/Poiyomi Toon", "", "<color=#E75898ff>Poiyomi 9.3.64</color>", 9)]
[TestCase(".poiyomi/Poiyomi Toon", "", "<color=#E75898ff>Poiyomi 10.0.28</color>", 10)]
[TestCase(".poiyomi/Poiyomi Toon", "", null, 0)]
[TestCase("Standard", "", null, 0)]
public void PoiyomiSourceNameAndPathDetection(string name, string path, string label, int expected)
{
    Assert.AreEqual(expected, AtlasPoiyomiVersionDetector.DetectFromMetadata(name, path, label));
}
        [Test]
        public void UnifiedMaterialExposesCanonicalRoughnessAndExtensibleFeatures()
        {
            var model = new AtlasUnifiedMaterial();
            model.Surface.Roughness.Constant = 0.8f;
            model.Surface.Roughness.InvertResult = true; // from smoothness = 0.8
            Assert.AreEqual(0.2f, model.Surface.Roughness.Evaluate(), 0.0001f);
            var feature = new AtlasSemanticFeature { Id = "surface.clearcoat" };
            feature.Parameters["weight"] = new AtlasSemanticValue { Kind = AtlasSemanticValueKind.Number, Number = 0.6f };
            model.Features.Add(feature);
            Assert.AreEqual("surface.clearcoat", model.Features[0].Id);
            Assert.AreEqual(0.6f, model.Features[0].Parameters["weight"].Number);
        }

        [Test]
        public void AutoSetupIgnoresDisabledSemanticLayers()
        {
            var source = new AtlasUnifiedMaterial();
            source.Normals.Add(new AtlasNormalLayer { Enabled = false, Map = new AtlasTextureSample { Texture = Texture2D.whiteTexture } });
            Assert.IsFalse(source.HasTexture(AtlasTextureSemantic.Normal));
            source.Normals[0].Enabled = true;
            Assert.IsTrue(source.HasTexture(AtlasTextureSemantic.Normal));
        }

        [Test]
        public void GlobalMasksAreSeparateFromAlphaAndColorMasks()
        {
            var source = new AtlasUnifiedMaterial();
            source.Masks.Add(new AtlasNamedMask { Purpose = AtlasMaskPurpose.Alpha,
                Map = new AtlasTextureSample { Texture = Texture2D.whiteTexture } });
            Assert.IsTrue(source.HasTexture(AtlasTextureSemantic.Mask));
            Assert.IsFalse(source.HasTexture(AtlasTextureSemantic.GlobalMask));
            source.Masks.Add(new AtlasNamedMask { Purpose = AtlasMaskPurpose.Global,
                Map = new AtlasTextureSample { Texture = Texture2D.whiteTexture } });
            Assert.IsTrue(source.HasTexture(AtlasTextureSemantic.GlobalMask));
        }

        [Test]
        public void UnifiedModelHasNoRawSourcePropertyDictionary()
        {
            Assert.IsNull(typeof(AtlasUnifiedMaterial).GetField("Properties"));
            Assert.IsNull(typeof(AtlasUnifiedMaterial).GetField("DiagnosticParserId"));
            Assert.IsNull(typeof(AtlasUnifiedMaterial).GetField("DiagnosticSourceShader"));
        }

        [Test]
        public void GeneratorInputCannotAccessOriginalSourceMaterialOrParser()
        {
            Assert.IsNull(typeof(AtlasGeneratorSource).GetField("SourceMaterial"));
            Assert.IsNull(typeof(AtlasGeneratorSource).GetField("Parser"));
            Assert.IsNull(typeof(AtlasGeneratorJob).GetField("Component"));
            Assert.IsNull(typeof(AtlasGeneratorJob).GetField("SourcesMaterials"));
        }

        [Test]
        public void StandardSmoothnessIsExpressedAsInvertedCanonicalRoughness()
        {
            var shader=Shader.Find("Standard");
            if(shader==null) Assert.Ignore("Standard shader is not available in this project.");
            var m=new Material(shader);
            try
            {
                m.SetFloat("_Glossiness",0.8f);
                var model=AtlasShaderRegistry.Detect(m).Parse(m);
                Assert.IsTrue(model.Surface.Roughness.InvertResult);
                Assert.AreEqual(0.8f,model.Surface.Roughness.Constant,0.0001f);
            }
            finally { Object.DestroyImmediate(m); }
        }

        [TestCase(7,0,AtlasLightingMode.LegacyToon)]
        [TestCase(7,2,AtlasLightingMode.Wrapped)]
        [TestCase(7,4,AtlasLightingMode.Flat)]
        [TestCase(8,0,AtlasLightingMode.ToonRamp)]
        [TestCase(8,1,AtlasLightingMode.ToonMultilayer)]
        [TestCase(8,2,AtlasLightingMode.Wrapped)]
        [TestCase(9,5,AtlasLightingMode.Flat)]
        [TestCase(10,5,AtlasLightingMode.Flat)]
        [TestCase(10,6,AtlasLightingMode.Realistic)]
        [TestCase(10,8,AtlasLightingMode.SignedDistanceField)]
        [TestCase(10,18,AtlasLightingMode.Unknown)]
        public void PoiyomiLightingVersionEnumIsNormalized(int version,int value,AtlasLightingMode expected)
        {
            Assert.AreEqual(expected,AtlasPoiyomiUnifiedParser.ToLightingMode(version,value));
        }

        [Test]
        public void UnionLightingHasOneModeAndSharedFields()
        {
            var u=new AtlasUnifiedMaterial();
            u.Lighting.Mode=AtlasLightingMode.Wrapped;
            u.Lighting.ShadowStrength=0.7f;
            u.Lighting.WrapAmount=0.3f;
            u.Lighting.ShadowBlur=0f; // older shader default
            Assert.AreEqual(AtlasLightingMode.Wrapped,u.Lighting.Mode);
            Assert.AreEqual(0.3f,u.Lighting.WrapAmount);
            Assert.IsNull(typeof(AtlasLightingDefinition).GetField("PoiyomiLighting"));
            Assert.IsNull(typeof(AtlasLightingDefinition).GetField("MobileToonLighting"));
        }

        [Test]
        public void MatcapBlendWeightsPreserveSimultaneousContributions()
        {
            var layer=new AtlasMatcapLayer();
            layer.BlendWeights.Replace=0.4f;
            layer.BlendWeights.Multiply=0.6f;
            Assert.AreEqual(0.4f,layer.BlendWeights.Replace);
            Assert.AreEqual(0.6f,layer.BlendWeights.Multiply);
            Assert.IsNotNull(layer.BlendWeights);
        }

        [Test]
        public void OcclusionStrengthUsesWhitePreservingInterpolation()
        {
            var scalar=new AtlasScalarInput { Constant=0.4f,
                Sample=new AtlasTextureSample { Texture=Texture2D.whiteTexture },
                CombineMode=AtlasScalarCombineMode.StrengthAroundOne };
            Assert.AreEqual(0.8f,scalar.Evaluate(0.5f),0.0001f);
            Assert.AreEqual(1f,scalar.Evaluate(1f),0.0001f);
        }

        [Test]
        public void MobileToonSourcePropertiesPopulateSharedFeaturesWhenShaderInstalled()
        {
            var shader=Shader.Find("VRChat/Mobile/Toon Standard");
            if(shader==null)Assert.Ignore("VRChat Mobile Toon Standard shader is not installed.");
            var material=new Material(shader);
            try
            {
                material.SetFloat("_MatcapType",1f);
                material.SetFloat("_ShadowBoost",0.3f);
                material.SetFloat("_MinBrightness",0.2f);
                material.SetFloat("_LimitBrightness",1f);
                var parsed=new AtlasVRChatToonStandardParser().Parse(material);
                Assert.AreEqual(AtlasLightingMode.ToonRamp,parsed.Lighting.Mode);
                Assert.AreEqual(0.3f,parsed.Lighting.ShadowBoost,0.0001f);
                Assert.AreEqual(0.2f,parsed.Lighting.MinimumBrightness,0.0001f);
                Assert.IsTrue(parsed.Lighting.LimitBrightness);
            }
            finally {Object.DestroyImmediate(material);}
        }

        [Test]
        public void MobileMatcapEnumMapsToCanonicalBlendWhenInstalled()
        {
            var shader=Shader.Find("VRChat/Mobile/Toon Standard");
            if(shader==null)Assert.Ignore("VRChat Mobile Toon Standard shader is not installed.");
            var material=new Material(shader);
            try
            {
                material.SetTexture("_Matcap",Texture2D.whiteTexture);
                material.SetFloat("_MatcapType",1f);
                var unified=new AtlasVRChatToonStandardParser().Parse(material);
                Assert.IsTrue(unified.Matcaps.Count>0);
                Assert.AreEqual(AtlasBlendOperation.Multiply,unified.Matcaps[0].Blend);
                Assert.AreEqual(1f,unified.Matcaps[0].BlendWeights.Multiply);
            }
            finally {Object.DestroyImmediate(material);}
        }

        [Test]
        public void MochieSourceWorkflowMapsIntoSingleSurfaceWhenInstalled()
        {
            var shader=Shader.Find("Mochie/Standard");
            if(shader==null)Assert.Ignore("Mochie/Standard shader is not installed.");
            var material=new Material(shader);
            try
            {
                material.SetInt("_PrimaryWorkflow",2);
                material.SetInt("_PrimarySampleMode",3);
                var normalized=new AtlasMochieStandardParser().Parse(material);
                Assert.AreEqual(AtlasSurfaceWorkflow.Specular,normalized.Surface.Workflow);
                Assert.AreEqual(AtlasLightingMode.StandardSpecular,normalized.Lighting.Mode);
                if(normalized.Surface.BaseColor.Map!=null)
                    Assert.AreEqual(AtlasCoordinateSpace.Triplanar,normalized.Surface.BaseColor.Map.Mapping.Space);
            }
            finally {Object.DestroyImmediate(material);}
        }

        [Test]
        public void StandardSpecularAndMetallicUseSameUnionSurfaceFields()
        {
            var a=Shader.Find("Standard");
            var b=Shader.Find("Standard (Specular setup)");
            if(a==null||b==null)Assert.Ignore("Both built-in Standard shaders are required.");
            var ma=new Material(a);
            var mb=new Material(b);
            try
            {
                var metallic=new AtlasStandardParser().Parse(ma);
                var specular=new AtlasStandardSpecularParser().Parse(mb);
                Assert.AreEqual(AtlasSurfaceWorkflow.Metallic,metallic.Surface.Workflow);
                Assert.AreEqual(AtlasSurfaceWorkflow.Specular,specular.Surface.Workflow);
                Assert.AreEqual(AtlasLightingMode.StandardMetallic,metallic.Lighting.Mode);
                Assert.AreEqual(AtlasLightingMode.StandardSpecular,specular.Lighting.Mode);
            }
            finally {Object.DestroyImmediate(ma);Object.DestroyImmediate(mb);}
        }

        [Test]
        public void UnifiedDecalAndRenderCapabilitiesAreShaderIndependent()
        {
            var material = new AtlasUnifiedMaterial();
            material.Render.ZClip = false;
            material.Render.DepthOffsetFactor = 2f;
            material.Render.ColorWriteMask = 7;
            var decal = new AtlasDecalLayer {EmissionStrength=3f,Alpha=0.4f,
                EmissionAnimation=new AtlasAnimatableParameter {Target="decal[0].emissionStrength"}};
            material.Decals.Add(decal);
            Assert.AreEqual(7, material.Render.ColorWriteMask);
            Assert.AreEqual(3f, material.Decals[0].EmissionStrength);
            Assert.AreEqual("decal[0].emissionStrength",material.Decals[0].EmissionAnimation.Target);
        }
        [Test]
        public void SharedLTCGIAndAudioDefinitionsHaveNoSourceShaderDependency()
        {
            var material=new AtlasUnifiedMaterial();
            material.ExternalLighting.EnableLTCGI=true;
            material.ExternalLighting.LTCGIDiffuseMultiplier=2.25f;
            material.AudioReactive.BandSmoothing[0]=0.7f;
            Assert.AreEqual(2.25f,material.ExternalLighting.LTCGIDiffuseMultiplier);
            Assert.AreEqual(0.7f,material.AudioReactive.BandSmoothing[0]);
        }
        [Test]
        public void MissingSubmeshesProduceEmptyMaskAndFlatCorrection()
        {
            var shader = Shader.Find("Standard");
            if(shader == null) Assert.Ignore("Standard shader is not available in this project.");
            var material=new Material(shader);
            try
            {
                using(var bake=AtlasSubmeshBaker.Bake(material,16,16,null,
                    new List<SkinnedMeshRenderer>(),null,new List<string>()))
                {
                    Assert.AreEqual(1,bake.Submeshes.Count);
                    Assert.IsTrue(bake.Submeshes[0].CurrentSubmeshMissing);
                    Assert.IsFalse(bake.Submeshes[0].CurrentMask.IsPixelUsed(0,0));
                    var normal=bake.Submeshes[0].TangentNormalCorrection.GetPixel(0,0);
                    Assert.AreEqual(1f,normal.b,0.001f);
                }
            }
            finally { Object.DestroyImmediate(material); }
        }
    }
}

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Mirrors the reflection contract without introducing a hard AAO dependency.
    public sealed class AtlasTestSourceContainer : MonoBehaviour
    {
        public AtlasTestEffectiveSources renderersSet = new AtlasTestEffectiveSources();
    }

    [System.Serializable]
    public sealed class AtlasTestEffectiveSources
    {
        public readonly List<SkinnedMeshRenderer> Effective = new List<SkinnedMeshRenderer>();
        public List<SkinnedMeshRenderer> GetAsList() => Effective;
    }
}
