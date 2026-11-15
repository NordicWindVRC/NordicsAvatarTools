namespace NordicWind.AdvancedAtlaser.Editor
{
    // 6: surface modes are named by how they look, not by blend factors.
    public enum AtlasSurfaceMode
    {
        Opaque,
        // Cut at the cutoff.
        Cutout,
        // Cutout with dithered edges.
        Dithered,
        // Everything fades with alpha.
        Fade,
        // Glass: reflections and highlights stay as alpha drops.
        Transparent,
        Additive,
        SoftAdditive,
        Multiplicative,
        MultiplicativeDouble,
        // Blend factors that match none of the above; see AtlasRender.SourceBlend.
        Custom
    }

    public enum AtlasCull { Off, Front, Back }
    public enum AtlasDepthTest { Disabled, Never, Less, Equal, LessOrEqual, Greater, NotEqual, GreaterOrEqual, Always }
    // GPU blend factors, only for AtlasSurfaceMode.Custom.
    public enum AtlasBlendFactor
    {
        Zero, One, DestinationColor, SourceColor, OneMinusDestinationColor, SourceAlpha,
        OneMinusSourceColor, DestinationAlpha, OneMinusDestinationAlpha, SourceAlphaSaturate, OneMinusSourceAlpha
    }

    public enum AtlasMirrorVisibility { Both, MirrorOnly, NotInMirror }

    // Hides parts close to the camera (not in mirrors), so views from inside the head stay clear.
    public sealed class AtlasNearClip
    {
        public bool Enabled;
        // Metres.
        public float Distance;
        // Hidden where the mask is above 0.5; null: everywhere.
        public AtlasMask Mask;
    }

    // Settings each pass has: the base pass, each outline layer, fur.
    public sealed class AtlasPassSettings
    {
        public AtlasCull Cull = AtlasCull.Back;
        public AtlasDepthTest DepthTest = AtlasDepthTest.LessOrEqual;
        public bool DepthWrite = true;
        public float DepthOffsetFactor;
        public float DepthOffsetUnits;
        // Bits: red 8, green 4, blue 2, alpha 1.
        public int ColorMask = 15;
        public readonly AtlasStencil Stencil = new AtlasStencil();
    }

    // Unity's stencil operations, in its order.
    public enum AtlasStencilOperation { Keep, Zero, Replace, IncrementSaturate, DecrementSaturate, Invert, IncrementWrap, DecrementWrap }

    // A pass's stencil test, with Unity's meanings. The compare uses the depth test's functions.
    public sealed class AtlasStencil
    {
        public bool Enabled;
        public int Reference;
        public AtlasDepthTest Compare = AtlasDepthTest.Always;
        public AtlasStencilOperation Pass = AtlasStencilOperation.Keep;
        public AtlasStencilOperation Fail = AtlasStencilOperation.Keep;
        public AtlasStencilOperation DepthFail = AtlasStencilOperation.Keep;
    }

    // 6: how the material is drawn.
    public sealed class AtlasRender
    {
        public AtlasSurfaceMode SurfaceMode = AtlasSurfaceMode.Opaque;
        public float Cutoff = 0.5f;
        public bool AlphaToCoverage;
        public AtlasBlendFactor SourceBlend = AtlasBlendFactor.One;
        public AtlasBlendFactor DestinationBlend = AtlasBlendFactor.Zero;
        // The render queue; −1 uses the output shader's own.
        public int Queue = -1;
        // Back faces flip their normal, so they light like front faces.
        public bool TwoSidedLighting;
        public AtlasMirrorVisibility MirrorVisibility = AtlasMirrorVisibility.Both;
        // Replaces the base colour texture in mirrors; null: none.
        public AtlasTextureReference MirrorBaseColor;
        public readonly AtlasNearClip NearClip = new AtlasNearClip();
        public bool Invisible;
        public readonly AtlasPassSettings Pass = new AtlasPassSettings();
    }
}
