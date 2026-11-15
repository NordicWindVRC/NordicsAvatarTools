using System.Collections.Generic;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // 4: the parts every layer type shares.
    public abstract class AtlasLayer
    {
        public bool Enabled = true;
        // Null: everywhere.
        public AtlasMask Mask;
        public AtlasPlacement Placement = AtlasPlacement.Surface;
        // 0–1: how much of the layer's result is also added as emission (S5).
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
        // For the one-value targets (alpha, metallic, roughness, occlusion): which channel of the source.
        public AtlasChannel Channel = AtlasChannel.Red;

        public AtlasLayerTarget() { }
        public AtlasLayerTarget(AtlasSurfaceTarget target, AtlasBlend blend, float opacity = 1f)
        {
            Target = target; Blend = blend; Opacity = opacity;
        }
    }

    // 4.1: a colour or texture applied to one or more surface inputs. Covers what sources call
    // detail, decal, recolour, back face and emission. Different textures for different targets
    // (a detail colour map and a detail normal map) are separate layers with the same mask.
    public sealed class AtlasSurfaceLayer : AtlasLayer
    {
        public readonly AtlasColorInput Source = new AtlasColorInput();
        // The material's base colour is the source instead ("use base colour" emission).
        public bool SourceIsBaseColor;
        // The source's alpha multiplies every target's opacity (alpha-blended details and decals).
        public bool SourceAlphaIsOpacity;
        public readonly List<AtlasLayerTarget> Targets = new List<AtlasLayerTarget>();
    }

    public enum AtlasLightResponse
    {
        IgnoreLight,
        // Times (main light colour + ambient).
        ScaleByLight
    }

    public enum AtlasLookupProjection
    {
        // The normal in view space with world up kept up, so the lookup doesn't roll with the head.
        ViewStabilised,
        // A cubemap sampled along the direction from the camera to the surface.
        ViewDirection
    }

    // 4.3: a texture looked up by direction instead of by UV (matcap). Placement Surface blends
    // into the base colour, so it's lit; later placements blend into the lit colour.
    public sealed class AtlasLookupLayer : AtlasLayer
    {
        public readonly AtlasColorInput Source = new AtlasColorInput();
        public AtlasLookupProjection Projection = AtlasLookupProjection.ViewStabilised;
        // Both eyes use the view from between them.
        public bool CentreEye;
        // View direction: rotations of the xy, xz and yz planes in that order (degrees), and spin
        // (degrees per second).
        public UnityEngine.Vector3 Rotation;
        public UnityEngine.Vector3 Spin;
        // Scales the lookup coordinate.
        public UnityEngine.Vector2 Tiling = UnityEngine.Vector2.one;
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
        ViewAngle
    }

    public enum AtlasAngleProfile
    {
        // smoothstep((1 − width) − softness, (1 − width) + softness, e)
        Band,
        // smoothstep(edge, 1 − edge, e^power)
        PowerBand,
        // The colour below rotated (HSV) by frac(power band) turns.
        HueRotation,
        // The colour below × the ramp texture at e.
        Ramp
    }

    public enum AtlasAngleColorSource
    {
        Color,
        // The reflection probe along the reflection, times the colour.
        Environment
    }

    // 4.4: colour driven by how the surface faces the viewer (rim). Usually placement Overlay.
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
        // Acts on highlights and reflections only (iridescence).
        public bool SpecularOnly;
        // The opacity times the metallic value.
        public bool ScaleByMetallic;
        public readonly AtlasColorInput Color = new AtlasColorInput();
        public AtlasAngleColorSource ColorSource = AtlasAngleColorSource.Color;
        // For ColorSource Environment.
        public bool EnvironmentRoughnessOverride;
        public float EnvironmentRoughness = 0.5f;
        // 0–1: multiplies by lerp(1, base, mix).
        public float BaseColorMix;
        public AtlasLightResponse LightResponse = AtlasLightResponse.IgnoreLight;
        public AtlasBlend Blend = AtlasBlend.Add;
        public float Opacity = 1f;

        public AtlasAngleLayer() { Placement = AtlasPlacement.Overlay; }
    }

    // 4.5: light through thin parts. light = (pow(saturate(view · −(L + N × distortion)), power)
    // × intensity / π + ambient) × pow(1 − thickness, thicknessPower) × (direct + ambient light)
    // × colour, times the received shadow when there's a real-time light.
    public sealed class AtlasSubsurfaceLayer : AtlasLayer
    {
        public readonly AtlasColorInput Color = new AtlasColorInput();
        // 0–1: multiplies by lerp(1, base, mix).
        public float BaseColorMix;
        // Data; null: thickness 0 (fully translucent).
        public AtlasTextureReference Thickness;
        public float ThicknessPower = 1f;
        public float Intensity = 1f;
        public float Power = 1f;
        public float Distortion = 1f;
        public float Ambient;

        public AtlasSubsurfaceLayer() { Placement = AtlasPlacement.Lit; }
    }

    public enum AtlasPatternKind
    {
        // AudioLink's waveform drawn as a line.
        Oscilloscope
    }

    // 4.6: a generated pattern. Usually placement Overlay, added.
    public sealed class AtlasPatternLayer : AtlasLayer
    {
        public AtlasPatternKind Kind = AtlasPatternKind.Oscilloscope;
        // Linear.
        public UnityEngine.Color Color = UnityEngine.Color.white;
        public float Opacity = 1f;
        public AtlasBlend Blend = AtlasBlend.Add;
        // Oscilloscope: saturate(1 − 50 × abs(wave(u′) − (2v′ − 1))), u′ v′ the UV scaled, offset and
        // rotated (degrees), inside the u and v ranges.
        public UnityEngine.Vector2 Scale = UnityEngine.Vector2.one;
        public UnityEngine.Vector2 Offset;
        public float Rotation;
        public UnityEngine.Vector2 URange = new UnityEngine.Vector2(0f, 1f);
        public UnityEngine.Vector2 VRange = new UnityEngine.Vector2(0f, 1f);

        public AtlasPatternLayer() { Placement = AtlasPlacement.Overlay; }
    }

    // 4.2: a second appearance revealed where a threshold is below the amount, with a glowing
    // edge band just above it.
    public sealed class AtlasTransitionLayer : AtlasLayer
    {
        // Null: 1 everywhere.
        public AtlasTextureReference Threshold;
        public AtlasChannel ThresholdChannel = AtlasChannel.Red;
        public float Amount;
        public readonly AtlasColorInput Revealed = new AtlasColorInput();
        // 0 < threshold − amount ≤ width glows with the edge colour, added after lighting.
        public float EdgeWidth;
        // Linear.
        public UnityEngine.Color EdgeColor = UnityEngine.Color.white;
    }

    // 4.7: the screen behind, offset by (IOR − 1) × the view-space normal / (depth + 1) ×
    // (1 − N·V), times the tint; then lerp(refraction, surface, opacity).
    public sealed class AtlasRefractionLayer : AtlasLayer
    {
        public float IOR = 1.3f;
        // Linear.
        public UnityEngine.Color Tint = UnityEngine.Color.white;
        public float Opacity;
        public bool ChromaticAberration;
        public float ChromaticAberrationStrength = 0.1f;
        public bool Blur;
        public float BlurStrength;
        public bool BlurByRoughness;
        // Refraction where the mask is at most the threshold; null: everywhere.
        public AtlasTextureReference CutMask;
        public float CutThreshold = 1f;

        public AtlasRefractionLayer() { Placement = AtlasPlacement.Lit; }
    }
}
