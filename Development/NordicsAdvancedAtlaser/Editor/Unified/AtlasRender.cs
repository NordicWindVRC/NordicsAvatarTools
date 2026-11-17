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

    // VRChat's fallback shader, what viewers with the shader blocked see. Not read from source
    // materials (6: not typed); an output sets it, and its writer turns it into its shader's
    // form. Automatic: the writer's own choice.
    public enum AtlasFallbackShader { Automatic, Toon, Unlit, Standard, Hidden }

    // Where the material is drawn, by view. A camera is a VRChat photo or stream camera; generic
    // mirror detection (outside VRChat) treats cameras as normal views.
    public sealed class AtlasVisibility
    {
        // Outside mirrors and cameras.
        public bool Normal = true;
        public bool MirrorVR = true;
        public bool MirrorDesktop = true;
        public bool CameraVR = true;
        public bool CameraDesktop = true;
        public bool Screenshot = true;

        // Drawn only in mirrors.
        public void MirrorOnly() { Normal = CameraVR = CameraDesktop = Screenshot = false; }
        public void NotInMirror() { MirrorVR = MirrorDesktop = false; }
    }

    // Hides the triangles whose UV lies in chosen tiles of the 4 × 4 grid from (0, 0) to (4, 4)
    // (UV tile discard, for toggling parts by moving their UVs to a tile).
    public sealed class AtlasTileDiscard
    {
        public AtlasTextureSpace Space = AtlasTextureSpace.UV0;
        // Bit row × 4 + column: tile u in [column, column + 1), v in [row, row + 1).
        public int Tiles;
        // Only on this side; Both: everywhere.
        public AtlasFaces Faces = AtlasFaces.Both;
    }

    // Hides parts close to the camera (not in mirrors), so views from inside the head stay clear.
    public sealed class AtlasNearClip
    {
        public bool Enabled;
        // Meters.
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
        // Clip past the near and far planes.
        public bool DepthClip = true;
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
        public int ReadMask = 255;
        public int WriteMask = 255;
        public AtlasDepthTest Compare = AtlasDepthTest.Always;
        public AtlasStencilOperation Pass = AtlasStencilOperation.Keep;
        public AtlasStencilOperation Fail = AtlasStencilOperation.Keep;
        public AtlasStencilOperation DepthFail = AtlasStencilOperation.Keep;
    }

    // 6: how the material is drawn.
    // The second pass's color: the base color's (Same), its own color and alpha, or only its alpha.
    public enum AtlasPassColor { Same, ColorAndAlpha, AlphaOnly }

    // 6: the whole surface drawn again after the first pass (Poiyomi's Two Pass), with its own
    // surface mode, blend factors and pass settings. Its alpha is pow(alpha, AlphaPower) (1 when
    // opaque or ForceOpaque); pixels where it (1 − it with CutoffInvert) is below Cutoff are
    // discarded whatever the mode. Mask bus steps of kind SecondPass tell the passes apart.
    public sealed class AtlasSecondPass
    {
        public bool Enabled;
        public AtlasSurfaceMode SurfaceMode = AtlasSurfaceMode.Opaque;
        public AtlasBlendFactor SourceBlend = AtlasBlendFactor.One;
        public AtlasBlendFactor DestinationBlend = AtlasBlendFactor.Zero;
        public float Cutoff = 0.5f;
        public bool CutoffInvert;
        public float AlphaPower = 1f;
        public bool AlphaToCoverage;
        public bool ForceOpaque;
        // ColorAndAlpha: the base color's constant color and alpha replaced by Color (linear);
        // AlphaOnly: its alpha by Alpha.
        public AtlasPassColor ColorSource = AtlasPassColor.Same;
        public UnityEngine.Color Color = UnityEngine.Color.white;
        public float Alpha = 1f;
        public readonly AtlasPassSettings Pass = new AtlasPassSettings();
    }

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
        public readonly AtlasVisibility Visibility = new AtlasVisibility();
        public readonly System.Collections.Generic.List<AtlasTileDiscard> HiddenTiles = new System.Collections.Generic.List<AtlasTileDiscard>();
        // Replaces the base color texture in mirrors; null: none.
        public AtlasTextureReference MirrorBaseColor;
        public readonly AtlasNearClip NearClip = new AtlasNearClip();
        public bool Invisible;
        // The shadow is as see-through as the surface, by a dither of its alpha.
        public bool DitheredShadows;
        // Fade and transparent also discard below the cutoff.
        public bool CutoffInTransparent;
        public bool Fog = true;
        // Geometry very close to the camera isn't cut by the near plane.
        public bool ReduceNearClipping;
        public readonly AtlasPassSettings Pass = new AtlasPassSettings();
        public readonly AtlasSecondPass SecondPass = new AtlasSecondPass();
        // A depth-only pass first, so a see-through surface shows only its nearest layer.
        public bool DepthPrepass;
        // Outputs only (see AtlasFallbackShader).
        public AtlasFallbackShader Fallback = AtlasFallbackShader.Automatic;
    }
}
