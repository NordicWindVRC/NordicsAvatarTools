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
        public readonly AtlasPassSettings Pass = new AtlasPassSettings();
    }
}
