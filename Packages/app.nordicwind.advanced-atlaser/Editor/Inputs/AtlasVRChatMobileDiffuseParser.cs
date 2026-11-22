using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// VRChat/Mobile/Diffuse and Bumped Diffuse (VRChat SDK): one texture times vertex color,
    /// lit by Lambert under one per-pixel directional light and the light probes along the
    /// normal; Bumped Diffuse adds a normal map. See UNIFIED_MATERIAL_VRCHAT_MOBILE.md.
    /// </summary>
    public sealed class AtlasVRChatMobileDiffuseParser : AtlasParserBase
    {
        private const string Diffuse = "VRChat/Mobile/Diffuse";
        private const string BumpedDiffuse = "VRChat/Mobile/Bumped Diffuse";

        public override string ParserId => "vrc-mobile-diffuse";
        public override string Family => AtlasShaderFamilies.VRChat;
        public override string DisplayName => "VRChat Mobile Diffuse";
        public override bool CanParse(Material m) => Name(m) == Diffuse || Name(m) == BumpedDiffuse;

        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            // Texture times vertex color, which is used as stored. There is no tint color.
            d.Surface.BaseColor.Map = AtlasSourceMapping.Texture(m, "_MainTex");
            d.Surface.VertexColor.MultiplyColor = true;
            // Always opaque, back faces culled (the render defaults); the texture's alpha is unused.
            d.Surface.Alpha.Source = AtlasAlphaSource.None;

            // Bumped Diffuse: a normal map on the texture's tiling, at full strength. Diffuse has
            // none, whatever a material switched from another shader still holds.
            if (Name(m) == BumpedDiffuse)
                d.Surface.Normal.Map = AtlasSourceMapping.Texture(m, "_BumpMap", AtlasTextureInterpretation.TangentNormal, "_MainTex");

            // Lambert alone: no highlights and no reflections.
            var response = d.Surface.Response;
            response.Highlights.Enabled = false;
            response.Reflections.Enabled = false;

            // base × (main light × N·L + ambient along the normal). No received shadows, no add
            // pass: other per-pixel lights count only through vertex lights and probes.
            d.Lighting.Family = AtlasLightingFamily.PhysicallyBased;
            d.Lighting.Diffuse = AtlasDiffuseModel.Lambert;
            d.Lighting.Ambient = AtlasAmbientModel.Directional;
            d.Lighting.ReceiveShadows = 0f;
            d.Lighting.Contributions.AdditionalLights = 0f;
        }
    }
}
