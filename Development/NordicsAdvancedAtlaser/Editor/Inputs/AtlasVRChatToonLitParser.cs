using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// VRChat/Mobile/Toon Lit (VRChat SDK): one texture times vertex colour, lit uniformly by the
    /// main light and the average ambient colour. See UNIFIED_MATERIAL_VRCHAT_MOBILE.md.
    /// </summary>
    public sealed class AtlasVRChatToonLitParser : AtlasParserBase
    {
        public override string ParserId => "vrc-mobile-toonlit";
        public override string DisplayName => "VRChat Mobile Toon Lit";
        public override bool CanParse(Material m) => Name(m) == "VRChat/Mobile/Toon Lit";

        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            // Texture times vertex colour, which is used as stored. There is no tint colour.
            d.Surface.BaseColor.Map = AtlasSourceMapping.Texture(m, "_MainTex");
            d.Surface.VertexColor.MultiplyColor = true;
            // Always opaque, back faces culled (the render defaults); the texture's alpha is unused.
            d.Surface.Alpha.Source = AtlasAlphaSource.None;

            // The same light everywhere: main light × shadow + the ambient average. No other
            // per-pixel or per-vertex lights; lights Unity folds into the ambient still count.
            d.Lighting.Family = AtlasLightingFamily.Flat;
            d.Lighting.Ambient = AtlasAmbientModel.Average;
            d.Lighting.Contributions.AdditionalLights = 0f;
            d.Lighting.Contributions.VertexLights = 0f;
        }
    }
}
