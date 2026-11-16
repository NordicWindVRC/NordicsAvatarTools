namespace NordicWind.AdvancedAtlaser.Editor
{
    public enum AtlasOutlineShape
    {
        // An inverted hull pushed out along the normal.
        NormalExpansion,
        // Pushed out by saturate(L · N), L the main light plus the probes' dominant direction:
        // thick on the lit side only.
        LightFacing,
        // Pushed out by saturate(view-space normal xy · Direction xy): thick toward one screen direction.
        ViewDirectional,
        // The whole hull offset by Direction in view space, a drop shadow.
        DropShadow
    }

    // An inner line on the outline toward the light: edge(view-space N · L × 0.5 + 0.5, Border, Blur).
    public sealed class AtlasOutlineHighlight
    {
        public bool Enabled;
        // Linear; its alpha scales the line.
        public UnityEngine.Color Color = new UnityEngine.Color(1f, 0.48f, 0f, 1f);
        public float Emission;
        // The colour × the outline's texture and base colour mix.
        public bool TintFromTexture;
        public float Border = 0.85f, Blur = 0.1f;
        public bool ReceiveShadows;
    }

    // 5.1: one outline layer. A material can have several.
    public sealed class AtlasOutline
    {
        public bool Enabled = true;
        public AtlasOutlineShape Shape = AtlasOutlineShape.NormalExpansion;
        // Metres; in object space (scaled with the object) when ObjectSpace.
        public float Width = 0.0005f;
        public bool ObjectSpace;
        // ViewDirectional and DropShadow.
        public UnityEngine.Vector3 Direction = UnityEngine.Vector3.right;
        // The expansion follows a tangent-space direction in the vertex colour (RGB × 2 − 1).
        public bool NormalsFromVertexColor;
        // Width × lerp(1, the vertex colour channel, strength); null: none.
        public AtlasMask WidthVertexMask;
        // Width × lerp(1, clamp(distance to the camera, 0, FixedSizeMaxDistance), FixedSize):
        // keeps its on-screen size up to that distance.
        public float FixedSize;
        public float FixedSizeMaxDistance = 1f;
        // Width 0 draws nothing.
        public bool ClipAtZeroWidth = true;
        // Null: everywhere.
        public AtlasMask WidthMask;
        // The largest on-screen offset in normalised device coordinates; 0 = no limit.
        public float ScreenWidthLimit;
        // Width × vertex colour; black vertices have no outline.
        public bool WidthFromVertexColor;
        // No outline closer to the camera than this, in metres.
        public float HideWithin;
        public readonly AtlasColorInput Color = new AtlasColorInput { Color = UnityEngine.Color.black };
        // 0–1: blends toward the base colour (with vertex colour).
        public float BaseColorMix;
        // Colour × the base colour.
        public bool MultiplyByBaseColor;
        // Colour × lerp(1, base, amount); squared base when BaseColorSquared (UTS style).
        public float BaseColorMultiply;
        public bool BaseColorSquared;
        // 0–1: toward the direct light's colour.
        public float LightColorMix;
        // Lit: how much the shading's shadow shows, 0–1.
        public float ShadowStrength = 1f;
        // The outline's colour × amount, added as emission.
        public float EmissionAmount;
        public readonly AtlasOutlineHighlight Highlight = new AtlasOutlineHighlight();
        // The outline's alpha is its colour's alpha instead of the base alpha × it.
        public bool OverrideAlpha;
        public readonly System.Collections.Generic.List<AtlasAlphaModifier> AlphaModifiers = new System.Collections.Generic.List<AtlasAlphaModifier>();
        public AtlasBlendFactor SourceBlend = AtlasBlendFactor.One;
        public AtlasBlendFactor DestinationBlend = AtlasBlendFactor.Zero;
        // Unlit: the colour as is.
        public bool Lit;
        // The material's emission also shows on the outline.
        public bool Emission;
        public readonly AtlasPassSettings Pass = new AtlasPassSettings { Cull = AtlasCull.Front };
    }
}
