namespace NordicWind.AdvancedAtlaser.Editor
{
    public enum AtlasOutlineShape
    {
        // An inverted hull pushed out along the normal.
        NormalExpansion,
        // Pushed out by width × lerp(1, lit, SizeFollowsLight) + LightSideWidth × lit +
        // DarkSideWidth × dark, lit = saturate((L·N − f.x) / (f.y − f.x)), dark the same of −L·N,
        // f = LightFalloff, L the main light plus the probes' dominant direction (by default
        // width × saturate(L·N): thick on the lit side only).
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
        // Null: everywhere.
        public AtlasMask Mask;
    }

    public enum AtlasOutlineTipStyle { Miter, Taper }

    // Outline shape data baked by an editor tool into a texture indexed by vertex: expansion
    // directions with hard edges kept above an angle limit, sharp tips mitred or tapered past an
    // angle, optional smoothing and crevice shrinking, and outline texture UVs.
    public sealed class AtlasOutlineCurvature
    {
        public AtlasTextureReference Data;
        public int VertexCount;
        public bool HardEdges = true;
        public float HardEdgeLimit = 93f;
        public AtlasOutlineTipStyle TipStyle = AtlasOutlineTipStyle.Miter;
        public float TipAngle = 60f;
        public bool SmoothNormals;
        public float SmoothStrength = 1f;
        public bool Crevices;
        public float CreviceShrink = 1f;
        public bool TextureUVs = true;
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
        // Width × lerp(1, the vertex colour channel, strength × VertexStrengthMask); null: none.
        public AtlasMask WidthVertexMask;
        public AtlasMask VertexStrengthMask;
        // LightFacing's falloff and side widths (metres, like Width).
        public UnityEngine.Vector2 LightFalloff = new UnityEngine.Vector2(0f, 1f);
        public float SizeFollowsLight = 1f;
        public float LightSideWidth, DarkSideWidth;
        // Outlines thinner than a pixel are widened to one, their width kept as coverage.
        public bool SmoothThin;
        // Width × lerp(1, clamp(distance to the camera, 0, FixedSizeMaxDistance), FixedSize ×
        // FixedSizeMask): keeps its on-screen size up to that distance.
        public float FixedSize;
        public AtlasMask FixedSizeMask;
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
        // Colour × lerp(1, base, amount × TextureBlendMask); squared base when BaseColorSquared (UTS style).
        public float BaseColorMultiply;
        public AtlasMask TextureBlendMask;
        // The colour applies × ColorMask: lerp(1, colour, mask); its alpha × AlphaMask.
        public AtlasMask ColorMask;
        public AtlasMask AlphaMask;
        // Applied to the outline's colour (and its highlight's) by ColorAdjustMask, in order.
        public readonly System.Collections.Generic.List<AtlasColorAdjustment> ColorAdjustments = new System.Collections.Generic.List<AtlasColorAdjustment>();
        public AtlasMask ColorAdjustMask;
        public bool BaseColorSquared;
        // 0–1: toward the direct light's colour.
        public float LightColorMix;
        // Lit: how much the shading's shadow shows, 0–1; the lighting × LitMask (lerp(1, lighting, mask)).
        public float ShadowStrength = 1f;
        public AtlasMask LitMask;
        // The outline's colour (or EmissionColor) × amount × EmissionMask, added as emission.
        public float EmissionAmount;
        public AtlasMask EmissionMask;
        // Null: the outline's colour.
        public AtlasColorInput EmissionColor;
        public readonly AtlasOutlineHighlight Highlight = new AtlasOutlineHighlight();
        // The outline's alpha is its colour's alpha instead of the base alpha × it.
        public bool OverrideAlpha;
        // Alpha × the mask; nothing where it is 0. Null: everywhere.
        public AtlasMask Mask;
        // Alpha × smoothstep(0, EdgeFade, abs(N·V)): fades where it faces the camera edge-on.
        public float EdgeFade;
        // The outline also draws into the camera's depth texture.
        public bool WritesCameraDepth = true;
        // Baked curvature data (Poiyomi Pro); null: none.
        public AtlasOutlineCurvature Curvature;
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
