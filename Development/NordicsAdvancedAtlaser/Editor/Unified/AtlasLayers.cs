using System.Collections.Generic;

namespace NordicWind.AdvancedAtlaser.Editor
{
    public enum AtlasFaces { Both, Front, Back }

    // 4: the parts every layer type shares.
    public abstract class AtlasLayer
    {
        public bool Enabled = true;
        // Null: everywhere.
        public AtlasMask Mask;
        // Which side of the triangles it shows on.
        public AtlasFaces Faces = AtlasFaces.Both;
        // In mirrors, outside them, or both.
        public AtlasMirrorVisibility Mirror = AtlasMirrorVisibility.Both;
        // It also shows on the outline.
        public bool OnOutlines;
        // 0–1: the mask × lerp(1, the shading's brightness (ramp × light map), amount).
        public float HideInShadow;
        public AtlasPlacement Placement = AtlasPlacement.Surface;
        // ≥ 0: how much of the layer's result is also added as emission (S5).
        public float EmissionAmount;
    }

    public enum AtlasSurfaceTarget { BaseColor, Alpha, Normal, Metallic, Roughness, Occlusion, Emission }

    // One surface input a surface layer changes.
    public sealed class AtlasLayerTarget
    {
        public AtlasSurfaceTarget Target;
        // Normal targets are composited by whiteout; their opacity is the normal strength.
        public AtlasBlend Blend = AtlasBlend.Replace;
        public float Opacity = 1f;
        // For the one-value targets (alpha, metallic, roughness, occlusion): which channel of the
        // source, read as 1 − channel when inverted.
        public AtlasChannel Channel = AtlasChannel.Red;
        public bool InvertChannel;
        // Normal targets: lerp toward the layer's normal instead of whiteout.
        public bool ReplaceNormal;

        public AtlasLayerTarget() { }
        public AtlasLayerTarget(AtlasSurfaceTarget target, AtlasBlend blend, float opacity = 1f)
        {
            Target = target; Blend = blend; Opacity = opacity;
        }
    }

    // 4.1: a color or texture applied to one or more surface inputs. Covers what sources call
    // detail, decal, recolor, back face and emission. Different textures for different targets
    // (a detail color map and a detail normal map) are separate layers with the same mask.
    public sealed class AtlasSurfaceLayer : AtlasLayer
    {
        public readonly AtlasColorInput Source = new AtlasColorInput();
        // The source times the base color ("use base color" emission).
        public bool SourceTimesBaseColor;
        // The source's alpha multiplies every target's opacity (alpha-blended details and decals);
        // the product is clamped to 0–1.
        public bool SourceAlphaIsOpacity;
        // Added to the source alpha before it's used as opacity (saturated).
        public float SourceAlphaOffset;
        // The map's alpha is replaced by its brightest channel, max(r, g, b).
        public bool SourceIntensityIsAlpha;
        // Only the map's alpha is used: the color shows where it covers (a color mask).
        public bool SourceMapIsMask;
        // The source goes through this video screen first (4.6); null: none.
        public AtlasVideoScreen Screen;
        // The emission it adds × the surface's alpha (premultiplied transparency).
        public bool EmissionTimesAlpha;
        // The emission below is replaced where the layer covers: e × (1 − coverage) + its own.
        public bool ReplacesEmission;
        // An emission layer whose emission e also replaces the base color: lerp(b, saturate(e), max(e)).
        public bool OverridesBaseColor;
        public readonly List<AtlasLayerTarget> Targets = new List<AtlasLayerTarget>();
    }

    public enum AtlasLightResponse
    {
        IgnoreLight,
        // Times (main light color + ambient).
        ScaleByLight,
        // Times the main light's color alone.
        ScaleByDirectLight
    }

    public enum AtlasLookupProjection
    {
        // The normal in view space with world up kept up, so the lookup doesn't roll with the head.
        ViewStabilized,
        // A cubemap sampled along the direction from the camera to the surface.
        ViewDirection,
        // The view-space normal corrected for the view direction, so it doesn't skew at the edges.
        ViewCorrected,
        // The view reflected about the normal, its world x and y: the same on both sides.
        ReflectionPlanar,
        // u = v = 1 − abs(N·V): a gradient from the center to the edge.
        Gradient,
        // The half vector of light and view, in a tangent frame around the normal.
        LightHalfVector,
        // A cubemap sampled along the view reflected about the normal.
        Reflection,
        // A cubemap sampled along the world normal.
        WorldNormal,
        // A cubemap sampled along the object-space normal.
        ObjectNormal
    }

    // 4.3: a texture looked up by direction instead of by UV (matcap). Placement Surface blends
    // into the base color, so it's lit; later placements blend into the lit color.
    public sealed class AtlasLookupLayer : AtlasLayer
    {
        public readonly AtlasColorInput Source = new AtlasColorInput();
        public AtlasLookupProjection Projection = AtlasLookupProjection.ViewStabilized;
        // Both eyes use the view from between them.
        public bool CenterEye;
        // View direction: rotations of the xy, xz and yz planes in that order (degrees), and spin
        // (degrees per second).
        public UnityEngine.Vector3 Rotation;
        public UnityEngine.Vector3 Spin;
        // Scales the lookup coordinate: uv = projection × 0.5 × tiling + 0.5.
        public UnityEngine.Vector2 Tiling = UnityEngine.Vector2.one;
        // Turns the 2D lookup around its center (degrees).
        public float LookupRotation;
        // Blends the lookup coordinate toward a mesh UV channel, per axis.
        public UnityEngine.Vector2 UVBlend;
        public AtlasTextureSpace UVBlendSpace = AtlasTextureSpace.UV1;
        // u flips in mirrors.
        public bool MirrorFlip;
        // 0–1: how much of the surface's normal map the lookup follows (0: the mesh normal).
        public float SurfaceNormalAmount = 1f;
        // 0–1: the color times lerp(1, base, mix) and lerp(1, direct light color, mix).
        public float BaseColorMix;
        public float LightColorMix;
        // Hidden where the scene has reflection probes (a matcap as their fallback).
        public bool HideWithReflectionProbes;
        // Fades outside a circle of the lookup: 1 − edge(distance to the center, radius, blur).
        public bool CircleMask;
        public float CircleRadius = 0.45f;
        public float CircleBlur = 0.1f;
        // Mip blur as roughness, 0–1; or the surface's roughness.
        public float Blur;
        public bool BlurFromRoughness;
        // The source's alpha multiplies the opacity.
        public bool SourceAlphaIsOpacity;
        public AtlasLightResponse LightResponse = AtlasLightResponse.IgnoreLight;
        // Its own normal for the lookup; null: the surface normal.
        public AtlasTextureReference NormalMap;
        public float NormalStrength = 1f;
        // Whiteout-blend the lookup's normal with the surface normal.
        public bool NormalWithSurface;
        public AtlasBlend Blend = AtlasBlend.Add;
        public float Opacity = 1f;
    }

    public enum AtlasAngleSource
    {
        // e = 1 − abs(view · normal), the view from between the eyes.
        ViewAngle,
        // Silhouettes from the screen's depth: Depth.
        Depth,
        // Light from behind: e = (normalize(L − V × ViewStrength) · N) × 0.5 + 0.5, times the
        // received shadow when ReceiveShadows; the result × saturate(0.5 − (V·L) × 0.5)^Directivity.
        Backlight
    }

    // 4.4: a silhouette from the screen's depth. Samples around the pixel (2 along the normal,
    // or 4 or 8 around it) at Width × 0.1 world units, scaled up within FixedSizeDistance; the
    // rim where a sample lies Threshold farther (a smoothstep from Threshold × Sharpness, or a
    // step when Binary), times how sideways the normal faces unless Binary; faded between
    // FadeFar and FadeNear (FadeFar 0: no fade).
    public sealed class AtlasDepthEdge
    {
        public int Samples = 8;
        public float Width = 0.08f;
        public float Sharpness = 0.5f;
        public float Threshold = 0.25f;
        // A hard edge; the mask and fade also step at 0.5.
        public bool Binary = true;
        public float FixedSizeDistance = 0.5f;
        public float FadeNear, FadeFar;
    }

    public enum AtlasLightSideMode
    {
        None,
        // value × lerp(1, w, Strength)
        Mask,
        // value × w × Strength
        Scale,
        // saturate(value − (1 − h) − Level)
        Subtract
    }

    // 4.4: how the light's direction shapes an angle layer. h = half-Lambert (1 − it when
    // Invert); w = linstep(Low, High, h), or smoothstep when Smooth. Applied to the angle term
    // before the step when BeforeStep, else to the result. ShrinkInShadow adds (1 − h) × 3 ×
    // amount to the term before the step.
    public sealed class AtlasAngleLightSide
    {
        public AtlasLightSideMode Mode = AtlasLightSideMode.None;
        public float Low, High = 1f;
        public bool Smooth;
        public bool Invert;
        public float Strength = 1f;
        public float Level;
        public bool BeforeStep;
        public float ShrinkInShadow;
    }

    public enum AtlasAngleProfile
    {
        // smoothstep((1 − width) − softness, (1 − width) + softness, e)
        Band,
        // smoothstep(edge, 1 − edge, e^power)
        PowerBand,
        // The color below rotated (HSV) by frac(power band) turns.
        HueRotation,
        // The color below × the ramp texture at e.
        Ramp,
        // x = base^power, the base facing abs(N·V) or edge 1 − abs(N·V) (as angles, linearly, when
        // LinearAngle); a linear step from Low to High (a hard step when equal), or its complement
        // when Falling.
        Step
    }

    public enum AtlasAngleBase { Facing, Edge }

    public enum AtlasAngleColorSource
    {
        Color,
        // The reflection probe along the reflection, times the color.
        Environment,
        // The reflection probe along the normal, less EnvironmentThreshold (not below 0), times the color.
        EnvironmentAlongNormal
    }

    // 4.4: color driven by how the surface faces the viewer (rim). Usually placement Overlay.
    public sealed class AtlasAngleLayer : AtlasLayer
    {
        public AtlasAngleSource Source = AtlasAngleSource.ViewAngle;
        public AtlasAngleProfile Profile = AtlasAngleProfile.Band;
        // Band.
        public float Width = 0.3f;
        public float Softness = 0.1f;
        // Power band.
        public float Power = 5f;
        public float Edge;
        // Ramp profile.
        public AtlasTextureReference Ramp;
        // Step profile; smoothstep instead of linear when Smooth.
        public AtlasAngleBase StepBase = AtlasAngleBase.Edge;
        public bool LinearAngle;
        public float Low, High = 1f;
        public bool Falling;
        public bool Smooth;
        // Depth source.
        public readonly AtlasDepthEdge DepthEdge = new AtlasDepthEdge();
        // Backlight source.
        public float Directivity = 5f;
        public float ViewStrength = 1f;
        public bool ReceiveShadows = true;
        public readonly AtlasAngleLightSide LightSide = new AtlasAngleLightSide();
        // Acts on highlights and reflections only (iridescence).
        public bool SpecularOnly;
        // The opacity times the metallic value.
        public bool ScaleByMetallic;
        public readonly AtlasColorInput Color = new AtlasColorInput();
        public AtlasAngleColorSource ColorSource = AtlasAngleColorSource.Color;
        // For ColorSource Environment.
        public bool EnvironmentRoughnessOverride;
        public float EnvironmentRoughness = 0.5f;
        public float EnvironmentThreshold;
        // Environment colors: × saturate(|direct light| / it); 0: none.
        public float ExposureOcclusion;
        // 0–1: the step's range widens by this × the value's change across a pixel.
        public float AntiAliasing = 1f;
        // × the surface's alpha (fade and transparent surfaces).
        public bool TimesAlpha;
        // 0–1: multiplies by lerp(1, base, mix), and by lerp(1, direct light color, mix).
        public float BaseColorMix;
        public float LightColorMix;
        public AtlasLightResponse LightResponse = AtlasLightResponse.IgnoreLight;
        // The color's alpha multiplies the opacity.
        public bool SourceAlphaIsOpacity;
        // 0–1: how much of the surface's normal map the angle follows (0: the mesh normal).
        public float SurfaceNormalAmount = 1f;
        // Each eye uses its own view instead of the view from between them.
        public bool PerEye;
        public AtlasBlend Blend = AtlasBlend.Add;
        public float Opacity = 1f;

        public AtlasAngleLayer() { Placement = AtlasPlacement.Overlay; }
    }

    public enum AtlasSubsurfaceLight
    {
        // × (direct + ambient light), times the received shadow when there's a real-time light.
        DirectAndAmbient,
        // × lerp(1, direct light color, LightColorMix) × lerp(1, the light map, ShadowStrength).
        Direct
    }

    // 4.5: light through thin parts. light = (pow(saturate(view · −(L + N × distortion)), power)
    // × intensity / π + ambient) × pow(saturate(1 − thickness − ThicknessOffset), thicknessPower)
    // × the light (Light) × color.
    public sealed class AtlasSubsurfaceLayer : AtlasLayer
    {
        public readonly AtlasColorInput Color = new AtlasColorInput();
        // 0–1: multiplies by lerp(1, base, mix).
        public float BaseColorMix;
        // Data; null: thickness 0 (fully translucent).
        public AtlasTextureReference Thickness;
        public AtlasChannel ThicknessChannel = AtlasChannel.Red;
        public float ThicknessOffset;
        public float ThicknessPower = 1f;
        public AtlasSubsurfaceLight Light = AtlasSubsurfaceLight.DirectAndAmbient;
        public float LightColorMix;
        public float ShadowStrength = 1f;
        public float Intensity = 1f;
        public float Power = 1f;
        public float Distortion = 1f;
        public float Ambient;

        public AtlasSubsurfaceLayer() { Placement = AtlasPlacement.Lit; }
    }

    public enum AtlasPatternKind
    {
        // AudioLink's waveform drawn as a line.
        Oscilloscope,
        // Sparkling particles, one per cell of a UV grid; see AtlasGlitter.
        Glitter,
        // Cells colored from their edges to their centers; see AtlasVoronoi.
        Voronoi,
        // Random quarter-circle arcs on a grid, joined into paths; see AtlasTruchet.
        Truchet,
        // Up to four lines running along gradients; see AtlasPath.
        Path,
        // Layers of a texture seen below the surface; see AtlasInternalParallax.
        InternalParallax,
        // Color and alpha by the distance to the scene behind the surface; see AtlasDepthEffect.
        Depth,
        // A low, mid and high color by AudioLink's spectrum along a UV axis; see AtlasAudioVolumeColor.
        AudioVolumeColor,
        // The surface shown as a screen of pixels; see AtlasVideoScreen.
        VideoScreen,
        // AudioLink's spectrum drawn as bars or lines in a placed area; see AtlasAudioSpectrum.
        AudioSpectrum,
        // Screen-space ambient occlusion from the depth texture; see AtlasScreenOcclusion.
        AmbientOcclusion,
        // Short shadows traced through the depth texture toward the light; see AtlasContactShadow.
        ContactShadow,
        // The color of a squish deformation (AtlasSquish, shared with the deformation); coverage = the squish.
        Squish,
        // Twinkling stars joined by lines, in four layers at different depths; see AtlasConstellation.
        Constellation,
        // A raymarched lava lamp inside the mesh; see AtlasLavaLamp.
        LavaLamp,
        // A skin color from pigments; see AtlasSkinTone.
        SkinTone
    }

    // 3.6 as a pattern layer: darkening where the depth texture shows nearby geometry around the
    // pixel (samples within Radius, Quality of them, jittered, biased by depth and cone, rejecting
    // samples by the normal), × Intensity, faded with the camera distance (QualityFalloff, and
    // MaxDistanceFalloff for far samples), hidden near the camera (CameraClip), by alpha
    // (AlphaFade) and in light (HideInLight). The occlusion blends the layer's color (× the color
    // map, or the map sampled by the occlusion as a ramp, HSVG-adjusted, × lerp(1, base,
    // SurfaceColorMix)) into the base color by the layer's blend. Needs a depth texture (a
    // depth-providing prefab on avatars).
    public sealed class AtlasScreenOcclusion
    {
        public float Intensity = 1f;
        public float Radius = 0.002f;
        public float Quality = 2.4f;
        public float CenterImportance;
        public float DepthBias;
        public float ConeBias;
        public float Jitter;
        public float NormalAmount;
        public float NormalRejection;
        public AtlasTextureReference ColorMap;
        public bool ColorMapAsRamp;
        // Hue, saturation, value and gamma offsets.
        public UnityEngine.Vector4 Hsvg;
        public float SurfaceColorMix;
        public float CameraClip = 0.1f;
        public float HideInLight;
        public float AlphaFade = 1f;
        public UnityEngine.Vector2 QualityFalloff = new UnityEngine.Vector2(6f, 8f);
        public UnityEngine.Vector4 MaxDistanceFalloff = new UnityEngine.Vector4(0.01f, 0.1f, 1f, 1f);
    }

    public enum AtlasDirectionSpace { World, Object, View }

    // 3.6 as a pattern layer: a ray marched through the depth texture from the pixel toward the
    // light (the main light, or Direction in its space) up to MaxDistance in Quality steps; geometry
    // in front within Thickness (and RejectionDepth) shadows it, × Intensity, dithered, faded by
    // camera distance (CameraFalloff) and in light (HideInLight). The shadow blends the layer's
    // color into the base color (ApplyToColor) and/or multiplies the shading's light map
    // (ApplyToLightMap).
    public sealed class AtlasContactShadow
    {
        public float Intensity = 0.5f;
        public float MaxDistance = 0.03f;
        public float Quality = 4f;
        public float Thickness = 0.005f;
        public float DepthBias = 0.001f;
        public bool CustomDirection;
        public UnityEngine.Vector3 Direction = UnityEngine.Vector3.up;
        public AtlasDirectionSpace DirectionSpace = AtlasDirectionSpace.World;
        public UnityEngine.Vector2 CameraFalloff = new UnityEngine.Vector2(5f, 10f);
        public bool ApplyToColor;
        public bool ApplyToLightMap = true;
        public float NormalRejection = 0.3f;
        public float HideInLight;
        public float Dither = 1f;
        public float RejectionDepth = 0.1f;
    }

    public enum AtlasScreenType { LCD, TN, CRT, OLED, Gameboy, Projector }

    // 4.6: Poiyomi's video effects, on the base color c. Grade: c = lerp(c, luminance(c),
    // −Saturation), then the contrast saturate(lerp(0.5, c, Contrast + 1)) in gamma. p = the pixel
    // texture tiled Resolution times. LCD: max(c, Backlight / 100), graded, × p × Backlight; TN: as
    // LCD with the view angle shifting contrast (along the bitangent) and hue (gbr, along the
    // tangent) before the grade; CRT: graded × p × Backlight × the beam's afterglow (a beam sweeping
    // the Resolution's pixels RefreshRate times a second; saturate(FadeTime − the time since it
    // passed, as a fraction of a sweep)); OLED: graded × p × Backlight; Gameboy: the graded
    // brightness (gamma luminance) through the ramp (when its larger side exceeds 16) or from dark
    // to light green; Projector: graded, each channel at least × Backlight. The base color becomes
    // lerp(base, screen, mask); with Emission, emission += screen × mask. With Pixelate, the base
    // color texture is sampled once per screen pixel (cells of Resolution on the pixel texture's UV).
    public sealed class AtlasVideoScreen
    {
        public AtlasScreenType Type = AtlasScreenType.OLED;
        public float Backlight = 1f;
        public bool Emission = true;
        // Tiling and offset are already × Resolution; null: white.
        public AtlasTextureReference PixelTexture;
        public UnityEngine.Vector2 Resolution = new UnityEngine.Vector2(1280f, 720f);
        public bool Pixelate;
        public float Saturation;
        public float Contrast;
        public float RefreshRate = 24f;
        public float FadeTime = 1.9f;
        public AtlasTextureReference GameboyRamp;
    }

    public enum AtlasSpectrumMirror { Off, Mirror, MirrorFlipped }
    public enum AtlasSpectrumColorSource { Band, Volume, Level }
    // Line: covered below the level (or within the line width of it); Filled: below the level;
    // SmoothLine: an anti-aliased polyline through the bins, LineThickness pixels wide.
    public enum AtlasSpectrumDisplay { Line, Filled, SmoothLine }
    public enum AtlasSpectrumColorMode { ThreeColors, ColorChord, Gradient }
    // Which of AudioLink's spectrum rows: filtered (smooth), raw (responsive) or the world's EQ.
    public enum AtlasSpectrumResponse { Smooth, Responsive, WorldEq }
    public enum AtlasSpectrumBandSampling { Point, Average4, Peak4 }

    // 4.6: Poiyomi's AL spectrum, in a placed area (u band, v volume; or with Circle, the angle and
    // the radius remapped by CircleDimensions (radius x–y, angle z–w)). Volume and band are stepped
    // to VolumeSteps and BandSteps (0: smooth) and clipped to their ranges; MirrorBands folds the
    // bands (1 − abs(2b − 1), or abs(2b − 1)); ShapeClip leaves gaps of 1 − the widths between
    // steps; Bilateral measures the volume from the middle (abs(v − 0.5) × 2). The level is
    // lerp(saturate(x), tanh(x), Compression), x = AudioLink's spectrum (the Response row) at the
    // band × Volume × lerp(BassBoost, TrebleBoost, band); the band runs over the DFT's first
    // BandRange, or (FrequencyMapping) logarithmically from FrequencyMin to FrequencyMax Hz.
    // Poiyomi 11 adds: Flip, the Arc (radii and angles instead of CircleDimensions), the Display
    // style, LitSteps, the gradient color mode, mask coordinates following the spectrum,
    // MasterOpacity, EmissionStrength and TintEmission. Covered where the level is above the volume, or, with
    // LineWidth below 1, within the line width of it; × the color mask's alpha. The color is the
    // ColorChord color at the band, or lerp(lerp(Low, Mid, saturate(2s)), High, saturate(2s − 1))
    // with s the band, volume or level, × the color mask; emission the same with each color × its
    // emission (ColorChordEmission). With Screen, the color goes through that video screen, and the
    // emission becomes lerp(emission, the screen's, coverage). The color blends into the base by the
    // layer's blend × opacity × coverage; alpha = lerp(alpha, alpha × coverage, OverrideAlpha).
    // Nothing without AudioLink.
    public sealed class AtlasAudioSpectrum
    {
        public AtlasTextureSpace Space = AtlasTextureSpace.UV0;
        // Placement on the UV; clipped outside unless tiled.
        public AtlasDecal Placement = new AtlasDecal();
        public bool Circle;
        public UnityEngine.Vector4 CircleDimensions = new UnityEngine.Vector4(0f, 1f, 0f, 1f);
        public float LineWidth = 1f;
        public bool Bilateral;
        public AtlasSpectrumMirror MirrorBands = AtlasSpectrumMirror.Off;
        public float BandRange = 0.5f;
        public float Volume = 1f;
        public float BassBoost = 1f;
        public float TrebleBoost = 1f;
        public float VolumeSteps;
        public UnityEngine.Vector2 VolumeClip = new UnityEngine.Vector2(0f, 1f);
        public float BandSteps;
        public UnityEngine.Vector2 BandClip = new UnityEngine.Vector2(0f, 1f);
        public bool ShapeClip;
        public float ShapeClipVolumeWidth = 0.5f;
        public float ShapeClipBandWidth = 0.5f;
        public AtlasSpectrumColorMode ColorMode = AtlasSpectrumColorMode.ThreeColors;
        public float ColorChordEmission;
        // Gradient: the ramp sampled by the color source, with its emission.
        public AtlasTextureReference Gradient;
        public float GradientEmission;
        // u and v flipped in the placed area.
        public bool FlipU, FlipV;
        // Circle: an arc from StartAngle over ArcDegrees, between the radii (0–1).
        public bool Arc;
        public float InnerRadius = 0.25f, OuterRadius = 1f, StartAngle, ArcDegrees = 360f;
        public AtlasSpectrumDisplay Display = AtlasSpectrumDisplay.Line;
        // SmoothLine: pixels.
        public float LineThickness = 2f;
        // The lit part stepped into this many height steps.
        public float LitSteps = 1f;
        public bool FrequencyMapping;
        public float FrequencyMin = 20f, FrequencyMax = 14080f;
        public AtlasSpectrumBandSampling BandSampling = AtlasSpectrumBandSampling.Point;
        public AtlasSpectrumResponse Response = AtlasSpectrumResponse.Smooth;
        public float Compression = 1f;
        // The color mask sampled in the spectrum's placed area instead of on its own UV.
        public bool MaskFollowsSpectrum;
        public float MasterOpacity = 1f;
        public float EmissionStrength = 1f;
        // The emission × the color mask's color.
        public bool TintEmission;
        public AtlasSpectrumColorSource ColorSource = AtlasSpectrumColorSource.Volume;
        // Linear.
        public UnityEngine.Color Low = UnityEngine.Color.blue;
        public UnityEngine.Color Mid = UnityEngine.Color.green;
        public UnityEngine.Color High = UnityEngine.Color.red;
        public float LowEmission, MidEmission, HighEmission;
        // Color × rgb, coverage × alpha; null: white.
        public AtlasTextureReference ColorMask;
        public float OverrideAlpha;
        // The video screen the color goes through; null: none.
        public AtlasVideoScreen Screen;
    }

    public enum AtlasInternalParallaxMode
    {
        // Each level adds its color × its height (or the brightest wins, with LevelMax).
        Basic,
        // A level shows where its height reaches 1 − its ratio, nearer levels in front.
        Heightmap
    }

    // 4.6: Poiyomi's internal parallax. For levels j = n − 1 down to 0, r = j / (n − 1): uv = the
    // placed UV + (depth × o + Scroll × t + r × PerLevelOffset) / the map's tiling, depth =
    // lerp(MinDepth, MaxDepth, r), o = −V.xy × 1.3 / (V.z ± 0.3) with V the tangent-space view
    // (clamped to 0–1 unless Tiled). The tint lerp(MinColor, MaxColor, r) (× the map's color with
    // HeightFromAlpha) is hue shifted by frac(r × HuePerLevel) when HuePerLevel isn't 0; the height
    // is the map's alpha (HeightFromAlpha) or red; the level's color is tint × pow(lerp(MinFade,
    // MaxFade, r), 2.2). n = round(lerp(Iterations, MinIterations, saturate((the object's distance
    // − near) / (far − near)))). The result blends into the base color by the layer's blend × mask;
    // emission adds result × the emission amount × mask.
    public sealed class AtlasInternalParallax
    {
        public AtlasInternalParallaxMode Mode = AtlasInternalParallaxMode.Basic;
        public bool LevelMax;
        // Color and height; tiling and offset apply after the placement.
        public AtlasTextureReference Map;
        public bool Tiled = true;
        public bool HeightFromAlpha;
        // Placed UV per second, and per level from the front to the back.
        public UnityEngine.Vector2 Scroll;
        public UnityEngine.Vector2 PerLevelOffset;
        // Added to the scroll offset (modulated by AudioLink chronotensity).
        public UnityEngine.Vector2 PanOffset;
        public int Iterations = 4;
        public int MinIterations = 1;
        // Near and far object distance (meters) for the falloff to MinIterations.
        public UnityEngine.Vector2 IterationDistance = new UnityEngine.Vector2(5f, 15f);
        public float MinDepth;
        public float MaxDepth = 0.1f;
        public float MinFade = 1f;
        public float MaxFade = 0.1f;
        // Sampled with mip maps (otherwise the full-size map).
        public bool MipMaps;
        // Smooth layers: about one sample per pixel the layers shift, up to SmoothSteps (≥ 8).
        public bool Smooth;
        public int SmoothSteps = 64;
        // alpha + saturate(max(r, g, b) of the result) × it × mask.
        public float AlphaAdd;
        // The view bent into the surface by Snell's law (IOR, the normal map by NormalAmount)
        // before it offsets the levels.
        public bool Refraction;
        public float RefractionIor = 1.33f;
        public float RefractionNormalAmount = 1f;
        // Each level toward FogColor by 1 − exp(−density × sqrt(1 + |o|²) / max depth × its depth);
        // Basic adds the fog color × (1 − exp(−density × sqrt(1 + |o|²))).
        public bool Fog;
        public UnityEngine.Color FogColor = new UnityEngine.Color(0.05f, 0.2f, 0.4f, 1f);
        public float FogDensity = 2f;
        // Linear.
        public UnityEngine.Color MinColor = UnityEngine.Color.white;
        public UnityEngine.Color MaxColor = UnityEngine.Color.white;
        public float HuePerLevel;
    }

    public enum AtlasDepthBlend { Replace, Multiply, Add }

    // 4.6: Poiyomi's depth FX. d = the distance from the surface to the scene point behind it (from
    // the camera's depth texture). With ColorEnabled, k = lerp(ColorValues.x, ColorValues.y,
    // saturate((d − ColorRange.x) / (ColorRange.y − ColorRange.x))) × mask and c = the layer's color
    // × the texture: replace lerp(base, c, k), multiply base × lerp(1, c, k), add saturate(base + c ×
    // k); emission += c × k × the emission amount. With AlphaEnabled, alpha ×= lerp(alpha, a, mask),
    // a = lerp(AlphaValues.x, AlphaValues.y, the same remap over AlphaRange).
    public sealed class AtlasDepthEffect
    {
        public bool ColorEnabled;
        public AtlasDepthBlend Blend = AtlasDepthBlend.Replace;
        // Null: white. Sampled at u = v = the depth remap when AlongDepth.
        public AtlasTextureReference Texture;
        public bool AlongDepth;
        public UnityEngine.Vector2 ColorRange = new UnityEngine.Vector2(0f, 1f);
        public UnityEngine.Vector2 ColorValues = new UnityEngine.Vector2(1f, 0f);
        public bool AlphaEnabled;
        public UnityEngine.Vector2 AlphaRange = new UnityEngine.Vector2(0f, 1f);
        public UnityEngine.Vector2 AlphaValues = new UnityEngine.Vector2(1f, 0f);
    }

    // 4.6: Poiyomi's AL volume color. v = AudioLink's spectrum (filtered) at the UV's u or v across
    // its bins; c = lerp(lerp(Low, Mid, saturate(2v)), High, saturate(2v − 1)), and the emission the
    // same with each color × its emission. The color blends into the base color by the layer's
    // blend and opacity; nothing without AudioLink.
    public sealed class AtlasAudioVolumeColor
    {
        public AtlasTextureSpace Space = AtlasTextureSpace.UV0;
        public bool AlongV;
        // Linear.
        public UnityEngine.Color Low = UnityEngine.Color.blue;
        public UnityEngine.Color Mid = UnityEngine.Color.green;
        public UnityEngine.Color High = UnityEngine.Color.red;
        public float LowEmission, MidEmission, HighEmission;
    }

    public enum AtlasVoronoiType
    {
        // The distance to the cell's edge on a 2D grid × 1.55.
        Cells2D,
        // sqrt(F2) − sqrt(F1) of 3D cellular noise (the position × 0.7): bright between cells.
        Difference3D,
        // The distance to the cell's edge on a 3D grid × 1.8.
        Cells3D
    }

    public enum AtlasVoronoiSpace { Object, World, UV }

    // 4.6: Poiyomi's voronoi. p = (the position + Offset) × Scale + Scroll × t (cell units); v =
    // pow(the type's distance, Power); ramp = smoothstep(Gradient.x + n, Gradient.y + n, v), n the
    // noise texture's channel × NoiseIntensity. c = lerp(OuterColor, InnerColor, ramp) (the inner
    // color a random HSV per cell with RandomCellColor). Base color = lerp(base, c.rgb, mask ×
    // c.a) unless EmissionOnly; alpha = lerp(alpha, c.a, mask) with AffectsAlpha; emission +=
    // c.rgb × lerp(OuterEmission, InnerEmission, ramp) × mask × c.a.
    public sealed class AtlasVoronoi
    {
        public AtlasVoronoiType Type = AtlasVoronoiType.Difference3D;
        public AtlasVoronoiSpace Space = AtlasVoronoiSpace.Object;
        public AtlasTextureSpace UV = AtlasTextureSpace.UV0;
        public float Scale = 5f;
        public UnityEngine.Vector3 Scroll = new UnityEngine.Vector3(0.5f, 0.5f, 0.5f);
        // Added to the position (modulated by AudioLink chronotensity).
        public UnityEngine.Vector3 Offset;
        public float Power = 0.45454545f;
        public UnityEngine.Vector2 Gradient = new UnityEngine.Vector2(0f, 0.5f);
        public AtlasTextureReference Noise;
        public AtlasChannel NoiseChannel = AtlasChannel.Red;
        public float NoiseIntensity = 0.1f;
        // Linear, with alpha.
        public UnityEngine.Color OuterColor = UnityEngine.Color.black;
        public UnityEngine.Color InnerColor = UnityEngine.Color.white;
        public float OuterEmission;
        public float InnerEmission;
        public bool RandomCellColor;
        public UnityEngine.Vector2 SaturationRange = new UnityEngine.Vector2(0.8f, 1f);
        public UnityEngine.Vector2 BrightnessRange = new UnityEngine.Vector2(0.8f, 1f);
        public bool EmissionOnly;
        public bool AffectsAlpha;
        // Cell points wander: o = lerp(o, 0.5 + 0.5 sin(CellMotionSpeed × t × (0.6 + 0.8 o.yx) + 2π o), CellMotion).
        public float CellMotion;
        public float CellMotionSpeed = 1f;
        // Each cell listening to AudioLink; null: none.
        public AtlasVoronoiAudioCells AudioCells;
    }

    // Each cell takes a band (or a random one of the four) at a random moment of the band's
    // recent history (× History); the inner color × lerp(1 − Dim, 1, it), inner emission + Emission × it.
    public sealed class AtlasVoronoiAudioCells
    {
        // 0–3 bass to treble; 4 random per cell.
        public int Band = 4;
        public float History = 0.5f;
        public float Emission = 2f;
        public float Dim = 0.5f;
    }

    // 4.6: Poiyomi's truchet. q = the UV × Density + Scroll × t + Offset; each cell (random per
    // cell) is flipped in u, and holds two quarter-circle arcs of radius 0.5 around opposite corners.
    // With e = Thickness − abs(distance to the corner − 0.5): coverage = saturate(e / fwidth) ×
    // smoothstep(0, 1 − EdgeSharpness, e) × the mask. The texture is sampled along the path (u the
    // angle, alternating per checker cell, v across the line) + TextureOffset. With k = min(coverage,
    // texture alpha) × opacity: base = lerp(base, blend(base, texture × color), k); emission +=
    // texture × color × k × the emission amount; with OverrideAlpha, alpha × k.
    public sealed class AtlasTruchet
    {
        public AtlasTextureSpace Space = AtlasTextureSpace.UV0;
        public float Density = 10f;
        // Grid cells per second.
        public UnityEngine.Vector2 Scroll;
        public UnityEngine.Vector2 Offset;
        public float Thickness = 0.1f;
        public float EdgeSharpness = 1f;
        // Null: white.
        public AtlasTextureReference Texture;
        public UnityEngine.Vector2 TextureOffset;
        public bool OverrideAlpha;
        // Nothing is drawn while AudioLink is missing.
        public bool HideWithoutAudioLink;
    }

    // 4.6: Poiyomi Pro's constellation. q = (UV − Center) × Density + Scroll / Density × t + Offset.
    // Four copies of a star field at depths z = frac(i / 4 + Phase) (i = 0–3; z = 0 at the back),
    // each at q × lerp(10, 0.5, z) (+ a per-layer shift), weighted by lerp(LayerFade.x, .y, z) and
    // with Depth > 0 sunk below the surface along the tangent-space view (back layers deepest).
    // A star field: one star per grid cell, at a random point that drifts with StarMovement (time
    // × StarSpeed + MovementOffset), its size (StarSize; random magnitudes with StarVariation)
    // twinkling with time × TwinkleSpeed + TwinkleOffset, × StarBrightness; lines join
    // neighboring stars (a random share of the links: LineDensity), LineThickness thick (thinner
    // toward the ends by LineThicknessVariation), × LineBrightness. coverage = min(the layers'
    // sum, 1) × the mask. Color: the layer's color, or with RandomColors one per star (HSV with
    // a random hue + HueOffset, saturation and value in ranges; or AudioLink's theme color
    // AudioThemeColor, 1–4, slightly jittered per star), blended along the lines. With c = color
    // × coverage: the layer's blend with weight coverage (replace, multiply or add), emission +=
    // c × coverage × the emission amount.
    public sealed class AtlasConstellation
    {
        public AtlasTextureSpace Space = AtlasTextureSpace.UV0;
        public UnityEngine.Vector2 Center = new UnityEngine.Vector2(0.5f, 0.5f);
        public float Density = 10f;
        public UnityEngine.Vector2 Scroll;
        public UnityEngine.Vector2 Offset;
        // 0–1: shifts the layers through their depths.
        public float Phase;
        public UnityEngine.Vector2 LayerFade = new UnityEngine.Vector2(0.2f, 1f);
        public float Depth;
        public float StarSize = 0.75f;
        public float StarBrightness = 1f;
        public float StarVariation;
        public float StarMovement = 0.4f;
        public float StarSpeed = 1f;
        public float MovementOffset;
        public float TwinkleSpeed = 5f;
        public float TwinkleOffset;
        public float LineThickness = 0.5f;
        public float LineThicknessVariation = 0.7f;
        public float LineBrightness = 1f;
        public float LineDensity = 1f;
        public bool RandomColors;
        public UnityEngine.Vector2 SaturationRange = new UnityEngine.Vector2(0.7f, 1f);
        public UnityEngine.Vector2 ValueRange = new UnityEngine.Vector2(0.8f, 1f);
        // Turns (0–1).
        public float HueOffset;
        // 0 off; 1–4 AudioLink's theme colors 0–3 (random colors only, while AudioLink is present).
        public int AudioThemeColor;
    }

    // 4.6: a skin color from pigments (Poiyomi 11's Skin section). Melanin m, its redness r and
    // hemoglobin h (each saturated after the additions) pick a color from Lut, a three-panel
    // strip: r chooses between the panels (blended), m runs along a panel's width (kept inside
    // 0.08–0.92), v = 1 − h; white without a lut. Freckles add FreckleStrength × mask^Sharpness to
    // m and that × FreckleRedness to r; a tan adds TanAmount × mask to m and TanRedness × mask to
    // r. The layer's color (tint × brightness) multiplies the result.
    public sealed class AtlasSkinTone
    {
        public AtlasTextureReference Lut;
        public float Melanin = 0.2f;
        public float MelaninRedness = 0.5f;
        public float Hemoglobin = 0.5f;
        // Null: off.
        public AtlasMask FreckleMask;
        public float FreckleStrength = 0.5f;
        public float FreckleSharpness = 3f;
        public float FreckleRedness = 0.1f;
        public AtlasMask TanMask;
        public float TanAmount = 0.15f;
        public float TanRedness = -0.1f;
    }

    public enum AtlasLavaShape { Blob, CrossesAndRings, Stars, Hearts }
    public enum AtlasBindData { Off, On, Auto }

    // 4.6: Poiyomi Pro's lava lamp: blobs rising and sinking in a liquid inside the mesh, drawn
    // where the surface is see-through (opacity × mask × (1 − alpha)), unlit, its glow added as
    // emission.
    // Container: the view ray, bent entering front faces by RefractiveIndex around the normal
    // (lerp(vertex, pixel normal, NormalStrength)), runs through the mesh for its thickness: marched
    // through a baked signed distance volume (SdfVolume, its corner and size in object space, a
    // voxel SdfVoxelSize; without one a sphere-like guess), at least MinThickness. With BindData
    // (Auto: when the mesh carries baked bind positions) it runs in the bind pose, distances ÷
    // WorldRescale; else in object space around the origin.
    // Blobs: a field of cells along FlowDirection, Scale units each (VerticalSpacing tall), in four
    // offset layers; a cell holds a blob (none at SkipChance) of a random size (between MinSize
    // and MaxSize of the room left after Padding, skewed by SizeDistribution, × the blob scale),
    // scrolling at a random speed (MinSpeed–MaxSpeed, up or down) and drifting sideways on an ellipse
    // (MinDriftSpeed–MaxDriftSpeed); shapes are spheres or, slowly turning, crosses and rings,
    // stars or hearts; blobs and the reservoirs at BottomHeight and TopHeight melt together
    // (Smoothing); up to Steps march steps.
    // Shading: the blob color (CoreColor, optionally blended with a matcap) × the reservoir tint
    // (BottomColor to TopColor by height), lit by the main light with wrap Translucency and a GGX
    // highlight (Roughness, Reflectiveness); the reservoirs glow (BottomColor × BottomEmission,
    // TopColor × TopEmission, fading over ColorRange of the span) onto the blobs, through them
    // (absorbed by the blob color's complement, × Translucency) and as reflections. The liquid
    // hazes (HazeColor × the tint, HazeDensity) and tints (TintColor, TintStrength) with distance,
    // in front of the blobs and the surface behind them.
    // Time × lerp(FlowSpeed.x, .y by AudioLink's chronotensity); blob scale lerp(BlobScale.x, .y)
    // and the glow × lerp(EmissionScale.x, .y) by AudioBand's level (x without AudioLink).
    public sealed class AtlasLavaLamp
    {
        public AtlasTextureReference SdfVolume;
        public float SdfVoxelSize = 1f;
        public UnityEngine.Vector3 SdfCorner;
        public UnityEngine.Vector3 SdfSize = UnityEngine.Vector3.one;
        public float MinThickness = 0.15f;
        public AtlasBindData BindData = AtlasBindData.Auto;
        public float WorldRescale = 1f;
        public float RefractiveIndex = 1.1f;
        public float NormalStrength = 1f;
        public float Scale = 0.056f;
        public float Padding = 0.06f;
        public int Steps = 64;
        public AtlasLavaShape Shape = AtlasLavaShape.Blob;
        public float Smoothing = 0.12f;
        public float VerticalSpacing = 2f;
        public float SkipChance = 0.6f;
        public float MinSize = 0.5f, MaxSize = 1f;
        public float SizeDistribution;
        public float MinSpeed = 0.05f, MaxSpeed = 0.4f;
        public float MinDriftSpeed = 0.5f, MaxDriftSpeed = 2f;
        public UnityEngine.Vector3 FlowDirection = UnityEngine.Vector3.up;
        public float Reflectiveness = 0.15f;
        public float Roughness = 0.25f;
        public float Translucency = 1f;
        // The blob color blended with a matcap (without a texture: white × MatcapIntensity).
        public bool Matcap;
        public AtlasTextureReference MatcapMap;
        public float MatcapBorder = 0.5f;
        public float MatcapIntensity = 1f;
        public float MatcapAlpha = 1f;
        public AtlasBlend MatcapBlend = AtlasBlend.Add;
        public float TopHeight = 1f, BottomHeight;
        // Linear.
        public UnityEngine.Color CoreColor = UnityEngine.Color.white;
        public UnityEngine.Color HazeColor = UnityEngine.Color.white;
        public float HazeDensity = 10f;
        public UnityEngine.Color TintColor = UnityEngine.Color.white;
        public float TintStrength = 1f;
        public UnityEngine.Color TopColor = new UnityEngine.Color(1f, 0.214f, 0f);
        public float TopEmission = 0.5f;
        public UnityEngine.Color BottomColor = new UnityEngine.Color(0f, 0.214f, 1f);
        public float BottomEmission = 0.5f;
        public float ColorRange = 0.8f;
        // −1: no AudioLink.
        public int AudioBand = -1;
        public UnityEngine.Vector2 BlobScale = UnityEngine.Vector2.one;
        public UnityEngine.Vector2 FlowSpeed = UnityEngine.Vector2.one;
        public UnityEngine.Vector2 EmissionScale = UnityEngine.Vector2.one;
    }

    public enum AtlasPathSource
    {
        // The map's channels are the gradients (merged: their average for all four).
        Texture,
        // A UV axis per channel; the map's channels mask them.
        UV
    }

    public enum AtlasPathType { Fill, Path, Loop, Dashed, Off }
    public enum AtlasPathEasing { Linear, In, Out, InOut }
    // How the lines' colors combine: over each other in order, added (alpha still over), or
    // only the brightest line.
    public enum AtlasPathOverlap { Layered, Additive, Brightest }

    // One of a path's four lines, along the gradient g (0 is never drawn).
    public sealed class AtlasPathChannel
    {
        public AtlasPathType Type = AtlasPathType.Loop;
        // Linear, with alpha.
        public UnityEngine.Color Color = UnityEngine.Color.white;
        // UV source: the gradient is v instead of u.
        public bool AlongV;
        public float Emission;
        public float Softness = 1f;
        // τ = frac((ManualTime ? Time : t × Speed) + Offset), stepped to Segments when above 0.
        public float Speed = 0.1f;
        public bool ManualTime;
        public float Time;
        public float Offset;
        public float Segments;
        // The line's length (the dash's, Dashed), and the gap between dashes.
        public float Length = 0.03f;
        public float Gap = 0.1f;
        // With remapping, g outside the range isn't drawn and g = saturate((g − x) / (y − x)).
        public UnityEngine.Vector2 Remap = new UnityEngine.Vector2(0f, 1f);
        // A mask bus channel the line's alpha blends with; null: none.
        public AtlasMask Mask;
        // × a channel of the packed mask map; null: none.
        public AtlasMask PackedMask;
        // Texture source: the gradient is this channel of the map (0–3); −1: the line's own.
        public int GradientChannel = -1;
        // τ runs 0 → 1 → 0 (the travel direction flips at the turn), then is eased: τ², 1 − (1 − τ)²
        // or smoothstep.
        public bool PingPong;
        public AtlasPathEasing Easing = AtlasPathEasing.Linear;
        // Path, loop and dashed: the leading and trailing edges softened apart (otherwise both by Softness).
        public bool SeparateEdges;
        public float HeadSoftness = 0.1f;
        public float TailSoftness = 1f;
    }

    // 4.6: Poiyomi's pathing. For each channel, with w = Length / 2 and s = Softness: Fill =
    // smoothstep(τ(1 + s), τ(1 + s) − s, g) (step(g, τ) when s is 0); Path = saturate(1 − abs(lerp(−w,
    // 1 + w, τ) − g) / w); Loop = saturate(1 − min(abs(τ − g), 1 − abs(τ − g)) / w); Dashed = dashes of
    // Length and Gap along g moving with τ, edges softened by s; Path and Loop then smoothstep(0, s).
    // The channels' colors are composited over each other in order R, G, B, A (alpha = line ×
    // color alpha), times the color map (its alpha too); the layer's blend (replace, add,
    // multiply) puts the result on the base color; emission adds each line × color × Emission ×
    // the color map.
    public sealed class AtlasPath
    {
        public AtlasPathSource Source = AtlasPathSource.Texture;
        public AtlasTextureReference Map;
        public bool Merged;
        // Nearest-texel sampling of the map.
        public bool PointSampling = true;
        public AtlasTextureSpace Space = AtlasTextureSpace.UV0;
        public AtlasTextureReference ColorMap;
        public bool Remapping;
        public bool OverrideAlpha;
        public AtlasPathOverlap Overlap = AtlasPathOverlap.Layered;
        // The lines' edges anti-aliased by the gradient's change across a pixel.
        public bool Antialiasing;
        // The color map's alpha masks every line (off: it's ignored).
        public bool ColorMapAlphaMasks = true;
        public readonly AtlasPathChannel[] Channels = { new AtlasPathChannel(), new AtlasPathChannel(), new AtlasPathChannel(), new AtlasPathChannel() };
    }

    public enum AtlasGlitterMode
    {
        // Each particle's normal turned randomly (wiggling with time) and seen from the view:
        // min × a + pow(saturate(dot(lerp(n′, V, bias), V)), contrast).
        Angle,
        // A random normal per particle swept through time, compared with the camera direction
        // (the Linear* parameters).
        Linear,
        // As Angle, against the light's half vector, × N·L and the light color.
        Light
    }

    public enum AtlasGlitterShape { Circle, Square }

    // 4.6: Poiyomi's glitter. The UV (+ Scroll × t) × Density makes cells; each holds a particle at
    // a random point (× RandomPosition) with a random seed. Only the nearest particle is drawn,
    // or, with Overlap, every one that reaches; Layers repeats the grid with other seeds (up to 4).
    // A particle's coverage a is its shape at Size (radius, or half width), anti-aliased; its
    // sparkle s comes from the mode; its color is Color × lerp(1, base, BaseColorMix) × the color
    // map × the shape texture (× a random HSV color) (hue shifted); its brightness is
    // MaxBrightness. Blend add: emission += color × brightness × s × texture alpha × mask. Blend
    // replace: base = lerp(base, s × color × brightness, s × texture alpha × mask), emission +=
    // s × color × max(0, (brightness − 1) × texture alpha) × mask. The mask is the layer's mask ×
    // the color alpha (Opacity) × lerp(1, the hide in shadow term) × lerp(1, direct light
    // luminance, LightScale). Time τ = t / 20 + SparkleOffset.
    public sealed class AtlasGlitter
    {
        public AtlasGlitterMode Mode = AtlasGlitterMode.Angle;
        public AtlasGlitterShape Shape = AtlasGlitterShape.Circle;
        public AtlasTextureSpace Space = AtlasTextureSpace.UV0;
        // UV per second, before the density.
        public UnityEngine.Vector2 Scroll;
        // Cells per UV unit.
        public float Density = 300f;
        public int Layers = 1;
        // 0–1: how far a particle sits from its cell's corner toward a random point.
        public float RandomPosition = 1f;
        public bool Overlap;
        // Cell units; with RandomSize a random size in SizeRange.
        public float Size = 0.3f;
        public bool RandomSize;
        public UnityEngine.Vector2 SizeRange = new UnityEngine.Vector2(0.1f, 0.5f);
        // 0–1: the particle normal follows the normal map (0: the mesh normal).
        public float NormalAmount;
        public float MinBrightness;
        public float MaxBrightness = 3f;
        // Angle and Light: the turn is sin((τ + r π) × (1 + Speed)) × AngleRange degrees per axis
        // (fixed random angles when Speed + SparkleOffset is 0).
        public float Speed = 10f;
        public float AngleRange = 90f;
        public float Bias = 0.8f;
        public float Contrast = 300f;
        // Square: widens the anti-aliasing by distance² × amount.
        public float JaggyFix;
        // Distant cells merge so the pattern doesn't shimmer.
        public bool DistanceLOD = true;
        // Linear: n = normalize(abs(frac(r × 14.274 + τ × Speed) × 2 − 1) × 2 − 1); d = n · camera
        // (camera forward lerped to the view by LinearVRParallax); g = saturate(1 − ((abs(frac(d × k + k)
        // − 0.5) × 4 − 1) × c + c)), k = LinearSensitivity, c = LinearContrast; g = pow(g,
        // LinearPostContrast) × saturate(N·H × LinearAngleLimit + 1 − LinearAngleLimit), H the half
        // vector of the camera and the light × LinearLightDirection; s = max(MinBrightness, g) × a.
        public float LinearSensitivity = 0.25f;
        public float LinearContrast = 50f;
        public float LinearPostContrast = 1f;
        public float LinearAngleLimit;
        public float LinearLightDirection;
        public float LinearVRParallax;
        // Angle and Light: the sparkle peaks where the flake's angle to the view equals this
        // (degrees; 0 facing, 90 at the rim): facing becomes cos(θ − offset).
        public float AngleOffset;
        // Added to τ by modulators (AudioLink chronotensity), for the sparkle and the rotation.
        public float SparkleOffset;
        public float RotationOffset;
        // The particle's shape (sampled over its square, clamped); null: white. Frames are a sprite
        // sheet whose frame is random per particle, or by time (with a random start).
        public AtlasTextureReference Texture;
        public bool RandomFrame = true;
        public bool RandomStartFrame;
        public AtlasTextureReference ColorMap;
        public float BaseColorMix;
        // × an HSV color of random hue, saturation in SaturationRange, value in BrightnessRange.
        public bool RandomColors;
        public UnityEngine.Vector2 SaturationRange = new UnityEngine.Vector2(0.8f, 1f);
        public UnityEngine.Vector2 BrightnessRange = new UnityEngine.Vector2(0.8f, 1f);
        // A hue adjustment on the layer adds the particle's random hue.
        public bool HueRandomOffset;
        // The particle (its shape texture) turns by (τ′ × speed) turns, τ′ = t / 20 + RotationOffset,
        // speed = RotationSpeed + a random speed in RandomRotationSpeed (turns per 20 seconds),
        // plus a random turn with RandomRotation, the direction random with RandomDirection.
        public bool RandomRotation;
        public bool RandomDirection;
        public float RotationSpeed;
        public UnityEngine.Vector2 RandomRotationSpeed;
        // 0–1: the mask × lerp(1, the direct light's luminance, amount).
        public float LightScale;
    }

    // 4.6: a generated pattern. Usually placement Overlay, added.
    public sealed class AtlasPatternLayer : AtlasLayer
    {
        public AtlasPatternKind Kind = AtlasPatternKind.Oscilloscope;
        // Linear.
        public UnityEngine.Color Color = UnityEngine.Color.white;
        public float Opacity = 1f;
        public AtlasBlend Blend = AtlasBlend.Add;
        // The kind's parameters; null for other kinds.
        public AtlasGlitter Glitter;
        public AtlasVoronoi Voronoi;
        public AtlasTruchet Truchet;
        public AtlasPath Path;
        public AtlasInternalParallax InternalParallax;
        public AtlasDepthEffect Depth;
        public AtlasAudioVolumeColor AudioVolumeColor;
        public AtlasVideoScreen VideoScreen;
        public AtlasAudioSpectrum AudioSpectrum;
        public AtlasScreenOcclusion AmbientOcclusion;
        public AtlasContactShadow ContactShadow;
        public AtlasSquish Squish;
        public AtlasConstellation Constellation;
        public AtlasLavaLamp LavaLamp;
        public AtlasSkinTone SkinTone;
        // Oscilloscope: saturate(1 − 50 × abs(wave(u′) − (2v′ − 1))), u′ v′ the UV scaled, offset and
        // rotated (degrees), inside the u and v ranges.
        public UnityEngine.Vector2 Scale = UnityEngine.Vector2.one;
        public UnityEngine.Vector2 Offset;
        public float Rotation;
        public UnityEngine.Vector2 URange = new UnityEngine.Vector2(0f, 1f);
        public UnityEngine.Vector2 VRange = new UnityEngine.Vector2(0f, 1f);

        public AtlasPatternLayer() { Placement = AtlasPlacement.Overlay; }
    }

    public enum AtlasTransitionSource
    {
        // The threshold texture's channel.
        Texture,
        // Along a line from Start to End: saturate(dot(p − start, direction) / length), p the
        // object or world position, the vertex color, or a UV (z 0).
        PointToPoint,
        // saturate(distance(object position, Center) / Radius).
        Sphere,
        // From the facing, the direction or the light: abs(N·V), N·dir × 0.5 + 0.5, or N·L × 0.5 + 0.5,
        // the normal map by PixelNormalAmount; as an angle when Linear; 1 − it unless inverted.
        CenterOut
    }

    public enum AtlasTransitionSpace { Object, World, VertexColor, UV }
    public enum AtlasCenterOutMode { View, Direction, Light }
    public enum AtlasWaveBounds { Edge, Center, Loop }

    // 4.2: a second appearance revealed where the threshold g is below the amount a. Dissolved
    // d = 1 − smoothstep(a′ − s, a′ + s, g), s = Softness / 2, a′ = lerp(−pad, 1 + pad, a) when
    // Padded (pad = 2s + edge width + detail strength / 2, so 0 and 1 are fully in and out).
    // An edge band of EdgeWidth past the boundary on the undissolved side (the dissolved side
    // when EdgeFlip) shows the edge color. The mask scales the amount.
    public sealed class AtlasTransitionLayer : AtlasLayer
    {
        public AtlasTransitionSource Source = AtlasTransitionSource.Texture;
        // Null: 1 everywhere.
        public AtlasTextureReference Threshold;
        public AtlasChannel ThresholdChannel = AtlasChannel.Red;
        public bool InvertThreshold;
        // Point to point.
        public AtlasTransitionSpace Space = AtlasTransitionSpace.Object;
        public AtlasTextureSpace SpaceUV = AtlasTextureSpace.UV0;
        public UnityEngine.Vector3 Start = new UnityEngine.Vector3(0f, -1f, 0f), End = new UnityEngine.Vector3(0f, 1f, 0f);
        // Sphere.
        public UnityEngine.Vector3 Center;
        public float Radius = 1.5f;
        // Center out.
        public AtlasCenterOutMode CenterOutMode = AtlasCenterOutMode.View;
        public UnityEngine.Vector3 Direction = UnityEngine.Vector3.forward;
        public float PixelNormalAmount;
        public bool Linear = true;
        public float Amount;
        public float Softness;
        public bool Padded;
        // Shifts the boundary by (noise − 0.5) × strength (only near the edge by EdgeOnly).
        public AtlasTextureReference DetailNoise;
        public bool InvertDetailNoise;
        public float DetailStrength;
        public float DetailEdgeOnly;
        // A band of WaveWidth around the amount instead of everything below it.
        public bool Wave;
        public float WaveWidth = 0.1f;
        public AtlasWaveBounds WaveBounds = AtlasWaveBounds.Edge;
        // The revealed color replaces the surface (and its alpha) by d, plus emission; or, with
        // AlphaOnly, the alpha × lerp(x, y, smoothstep(z, w, d)).
        public readonly AtlasColorInput Revealed = new AtlasColorInput();
        public float RevealedEmission;
        public bool AlphaOnly;
        public UnityEngine.Vector4 AlphaCurve = new UnityEngine.Vector4(1f, 0f, 0f, 1f);
        // 0 < threshold − amount ≤ width glows with the edge color, added after lighting; or with
        // EdgeReplacesColor, the color becomes it, with EdgeEmission × it added as emission.
        public float EdgeWidth;
        // Linear.
        public UnityEngine.Color EdgeColor = UnityEngine.Color.white;
        public bool EdgeFlip;
        public bool EdgeReplacesColor;
        public float EdgeEmission;
    }

    public enum AtlasHighlightKind
    {
        // Hair strands (Kajiya-Kay), per lobe: smoothstep(−1, 0, T′·H) × sin(T′, H)^Exponent ×
        // Strength, T′ = normalize(T + (Offset + OffsetMapStrength × map alpha) × N), T the tangent
        // or bitangent; stylized: linstep(Edge − Blur/2, Edge + Blur/2) of that. Lobes add. Color
        // × lerp(luminance(light), light, LightColorMix); replace blends by saturate(max(color)).
        Strand,
        // A toon highlight on a = N·H × 0.5 + 0.5, per lobe: realistic a^(2^(11 − 10 × size)), or
        // smooth-edged at 1 − size^5 with the feather; × strength, summed and saturated. Then
        // min(it × mask × lerp(ramp, 1, IgnoreShadows), occlusion, lerp(saturate(N·L), 1,
        // ShowOnBack), lerp(light map, 1, IgnoreShadows)) × Strength; color × lerp(1, light, LightColorMix).
        Toon
    }

    public sealed class AtlasHighlightLobe
    {
        public float Strength = 1f;
        // Strand.
        public float Exponent = 100f;
        public float Offset, OffsetMapStrength;
        public bool Bitangent;
        // The half vector projected onto the strand's plane, so the band keeps one width.
        public bool EvenWidth;
        public bool Stylized;
        public float Edge = 0.5f, Blur;
        // Linear.
        public UnityEngine.Color Tint = UnityEngine.Color.white;
        // Toon.
        public float Size = 0.2f, Feather;
    }

    // 4.8: a highlight drawn on its own, besides the surface response's.
    public sealed class AtlasHighlightLayer : AtlasLayer
    {
        public AtlasHighlightKind Kind = AtlasHighlightKind.Strand;
        // Strand: the map's RGB colors it and its alpha offsets the strands.
        public readonly AtlasColorInput Color = new AtlasColorInput();
        public readonly System.Collections.Generic.List<AtlasHighlightLobe> Lobes = new System.Collections.Generic.List<AtlasHighlightLobe>();
        // Toon: realistic lobes instead of toon ones.
        public bool Realistic;
        public float Strength = 1f;
        public float LightColorMix = 1f;
        // 0–1: multiplies by lerp(1, base, mix).
        public float BaseColorMix;
        public float SurfaceNormalAmount = 1f;
        // Strand: × lerp(1, N·L, amount).
        public float FadeByLightDirection;
        // Toon.
        public bool IgnoreShadows, IgnoreReceivedShadows, ShowOnBack;
        public AtlasBlend Blend = AtlasBlend.Add;
        public float Opacity = 1f;

        public AtlasHighlightLayer() { Placement = AtlasPlacement.Overlay; }
    }

    public enum AtlasBlurPattern { Sunflower, Gaussian, Hexagon }

    // 4.7: the grabbed screen blurred: samples within a radius of Strength × 0.1 meters seen at
    // lerp(1, the camera distance, CameraFalloff) meters, × lerp(1, roughness, ByRoughness), × the
    // depth fade, × the mask; up to Quality² samples (fewer for small radii). Pattern: sunflower
    // (a golden-angle spiral at radius r^(Power + 1), weights 1 − r × Power), gaussian (the spiral,
    // weights exp(−r² (3 + 5 Power))) or a hexagonal bokeh (even weights). With DepthFade, the
    // radius × lerp(ramp.x, ramp.y, smoothstep(ramp.z, ramp.w, the distance from the surface to
    // the scene behind)).
    public sealed class AtlasScreenBlur
    {
        public float Strength;
        public float ByRoughness;
        public AtlasBlurPattern Pattern = AtlasBlurPattern.Sunflower;
        public float Quality = 10f;
        public float CameraFalloff;
        public float Power;
        public bool DepthFade;
        public UnityEngine.Vector4 DepthRamp = new UnityEngine.Vector4(0f, 1f, 0f, 1f);
        public AtlasMask Mask;
    }

    // 4.7: the grabbed screen read in blocks of Size screen pixels (with DistanceScaling, ×
    // (object scale / camera distance)^DistanceAmount), anchored at the object's origin on screen;
    // the mask halves the block floor((1 − mask) × 8) times. Gap (0–1) cuts see-through lines of
    // that share between the blocks.
    public sealed class AtlasScreenPixelation
    {
        public float Size = 8f;
        public bool DistanceScaling;
        public float DistanceAmount = 1f;
        public float Gap;
        public AtlasMask Mask;
    }

    public enum AtlasScreenRampPalette { Texture, GameBoy, VirtualBoy, Cga, Sepia, Neon }

    // 4.7: the grabbed screen recolored by its brightness: t = its luminance (gamma), then
    // saturate((t − 0.5)(Contrast + 1) + 0.5 + Offset); with Quantize, Levels steps, dithered
    // first by an 8 × 8 Bayer pattern × Dither / Levels. The color is the Texture at t (palette
    // Texture), or a four-color palette blended along t. Blended in by Blend × the mask.
    public sealed class AtlasScreenRamp
    {
        public AtlasScreenRampPalette Palette = AtlasScreenRampPalette.GameBoy;
        public AtlasTextureReference Texture;
        public float Blend = 1f;
        public float Contrast;
        public float Offset;
        public bool Quantize = true;
        public int Levels = 4;
        public float Dither = 1f;
        public AtlasMask Mask;
    }

    // 4.7: the screen behind the surface (Uber's refraction, Poiyomi's grab pass). With Refracts,
    // read offset by (IOR − 1) × the view-space normal × (1 − N·V), ÷ (depth + 1), or with
    // FieldOfViewScale × the field-of-view scale ÷ max(camera distance, 1); × (1 − N·V)^FresnelPower
    // when that isn't 0; × the refraction mask; with IgnoreForeground, not offset onto things in
    // front of the surface. Chromatic aberration spreads the offset × strength over a spectrum.
    // Then blur, pixelation, the color (lerp(screen, (screen × Brightness + AddBrightness) × Tint,
    // then the layer's hue adjustments, vibrance and saturation, ColorMask)) and the ramp. Blend:
    // k = Amount × (1 − alpha with ByAlpha) × AlphaMask; result = lerp(SourceBlend × surface +
    // DestinationBlend × screen, surface, 1 − k), the factors' source alpha being 1 − k. SelfGrab:
    // drawn in the second pass (6), the surface being the screen at the pixel unrefracted (the
    // object as the first pass drew it).
    public sealed class AtlasRefractionLayer : AtlasLayer
    {
        public bool Refracts = true;
        public float IOR = 1.3f;
        public bool FieldOfViewScale;
        public float FresnelPower;
        public bool IgnoreForeground;
        public AtlasMask RefractionMask;
        public bool ChromaticAberration;
        public float ChromaticAberrationStrength = 0.1f;
        // Null: none.
        public AtlasScreenBlur Blur;
        public AtlasScreenPixelation Pixelation;
        public AtlasScreenRamp Ramp;
        // Linear.
        public UnityEngine.Color Tint = UnityEngine.Color.white;
        public float Brightness = 1f;
        public float AddBrightness;
        public float Vibrance;
        public float Saturation = 1f;
        public AtlasMask ColorMask;
        public float Amount = 1f;
        public bool ByAlpha;
        public AtlasMask AlphaMask;
        public AtlasBlendFactor SourceBlend = AtlasBlendFactor.Zero;
        public AtlasBlendFactor DestinationBlend = AtlasBlendFactor.One;
        public bool SelfGrab;
        // Refraction where the mask is at most the threshold; null: everywhere.
        public AtlasTextureReference CutMask;
        public float CutThreshold = 1f;

        public AtlasRefractionLayer() { Placement = AtlasPlacement.Lit; }
    }
}
