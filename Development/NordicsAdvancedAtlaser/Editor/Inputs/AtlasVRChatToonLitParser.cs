using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// VRChat/Mobile/Toon Lit (VRChat SDK): one texture times vertex color, lit uniformly by the
    /// main light and the average ambient color. See UNIFIED_MATERIAL_VRCHAT_MOBILE.md.
    /// </summary>
    public sealed class AtlasVRChatToonLitParser : AtlasParserBase
    {
        public override string ParserId => "vrc-mobile-toonlit";
        public override string Family => AtlasShaderFamilies.VRChat;
        public override string DisplayName => "VRChat Mobile Toon Lit";
        public override bool CanParse(Material m) => Name(m) == "VRChat/Mobile/Toon Lit";

        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            // Texture times vertex color, which is used as stored. There is no tint color.
            d.Surface.BaseColor.Map = AtlasSourceMapping.Texture(m, "_MainTex");
            d.Surface.VertexColor.MultiplyColor = true;
            // Always opaque, back faces culled (the render defaults); the texture's alpha is unused.
            d.Surface.Alpha.Source = AtlasAlphaSource.None;

            // The same light everywhere: main light + the ambient average. The shader skips every
            // shadow variant, so it receives no shadows. No other per-pixel or per-vertex lights;
            // lights Unity folds into the ambient still count. Without a real-time main light, 20%
            // of the ambient color stands in for it.
            d.Lighting.Family = AtlasLightingFamily.Flat;
            d.Lighting.Ambient = AtlasAmbientModel.Average;
            d.Lighting.ReceiveShadows = 0f;
            d.Lighting.Contributions.AmbientAsMainLight = 0.2f;
            d.Lighting.Contributions.AdditionalLights = 0f;
            d.Lighting.Contributions.VertexLights = 0f;
        }
    }
}
