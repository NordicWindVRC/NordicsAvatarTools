using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// VRChat/Mobile/Standard Lite (VRChat SDK): a cut-down metallic Standard, always opaque, one
    /// per-pixel directional light, no received shadows. Behavior follows the shader's keywords;
    /// every main map uses the albedo's tiling. See UNIFIED_MATERIAL_VRCHAT_MOBILE.md.
    /// </summary>
    public sealed class AtlasVRChatStandardLiteParser : AtlasParserBase
    {
        public override string ParserId => "vrc-mobile-standardlite";
        public override string Family => AtlasShaderFamilies.VRChat;
        public override string DisplayName => "VRChat Mobile Standard Lite";
        public override bool CanParse(Material m) => Name(m) == "VRChat/Mobile/Standard Lite";

        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            // Albedo × color × vertex color, unless the hidden keyword turns vertex color off.
            // Always opaque: alpha is unused.
            d.Surface.BaseColor.Color = AtlasMaterialReader.ColorLinear(m, "_Color", Color.white);
            d.Surface.BaseColor.Map = Main(m, "_MainTex");
            d.Surface.VertexColor.MultiplyColor = !Keyword(m, "DISABLE_VERTEX_COLORING");
            d.Surface.Alpha.Source = AtlasAlphaSource.None;

            // Metallic (red) and smoothness (alpha) map, each times its slider. The metallic slider
            // is gamma-encoded ([Gamma]); the shading sees it in linear.
            var response = d.Surface.Response;
            var metallicGloss = Main(m, "_MetallicGlossMap", AtlasTextureInterpretation.Data);
            response.Metallic.Constant = Mathf.GammaToLinearSpace(AtlasMaterialReader.Float(m, "_Metallic", 1f));
            response.Metallic.Map = metallicGloss;
            response.Roughness.Constant = AtlasMaterialReader.Float(m, "_Glossiness", 1f);
            response.Roughness.Map = metallicGloss;
            response.Roughness.Channel = AtlasChannel.Alpha;
            response.Roughness.InvertResult = true;

            // Normal map: always applied, no keyword.
            d.Surface.Normal.Map = Main(m, "_BumpMap", AtlasTextureInterpretation.TangentNormal);
            d.Surface.Normal.Strength = AtlasMaterialReader.Float(m, "_BumpScale", 1f);

            // Occlusion: green channel, strength around one.
            response.Occlusion.Map = Main(m, "_OcclusionMap", AtlasTextureInterpretation.Data);
            response.Occlusion.Channel = AtlasChannel.Green;
            response.Occlusion.Constant = AtlasMaterialReader.Float(m, "_OcclusionStrength", 1f);

            // Emission: texture × HDR color when the checkbox (keyword) is on.
            if (Keyword(m, "_EMISSION"))
            {
                var emission = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
                emission.Source.Color = AtlasMaterialReader.ColorLinear(m, "_EmissionColor", Color.black);
                emission.Source.Map = Main(m, "_EmissionMap");
                emission.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add));
                d.Layers.Add(emission);
            }

            // Secondary maps: Unity's detail ×2 and detail normal, when the detail keyword is on.
            if (Keyword(m, "_DETAIL")) AtlasSourceMapping.UnityDetail(m, d, true);

            // Highlights and reflections: on unless their *_OFF keyword is set, as in the shader.
            response.Highlights.Enabled = !Keyword(m, "_SPECULARHIGHLIGHTS_OFF");
            if (Keyword(m, "_GLOSSYREFLECTIONS_OFF")) response.Reflections.Source = AtlasReflectionSource.AmbientColor;

            // Specular anti-aliasing.
            var antiAliasing = response.SpecularAntiAliasing;
            antiAliasing.Enabled = Keyword(m, "_ENABLE_GEOMETRIC_SPECULAR_AA");
            antiAliasing.Variance = AtlasMaterialReader.Float(m, "_SpecularAAScreenSpaceVariance", 0.1f);
            antiAliasing.Threshold = AtlasMaterialReader.Float(m, "_SpecularAAThreshold", 0.2f);

            // Lighting: physically based with Lambert diffuse (the shader's mobile BRDF; PC builds
            // use Unity's, with Disney diffuse), ambient along the normal, no received shadows,
            // no add pass: other per-pixel lights count only through vertex lights and probes.
            d.Lighting.Family = AtlasLightingFamily.PhysicallyBased;
            d.Lighting.Diffuse = AtlasDiffuseModel.Lambert;
            d.Lighting.Ambient = AtlasAmbientModel.Directional;
            d.Lighting.ReceiveShadows = 0f;
            d.Lighting.Contributions.AdditionalLights = 0f;

            // Lightmap Type and bicubic sampling only affect lightmaps, which skinned avatars don't
            // use; instancing and double-sided GI aren't visual. Render: the opaque defaults.
        }

        // Main maps use the albedo's tiling and offset.
        private static AtlasTextureReference Main(Material m, string property,
            AtlasTextureInterpretation interpretation = AtlasTextureInterpretation.Color) =>
            AtlasSourceMapping.Texture(m, property, interpretation, "_MainTex");

        private static bool Keyword(Material m, string keyword) => AtlasMaterialReader.Keyword(m, keyword);
    }
}
