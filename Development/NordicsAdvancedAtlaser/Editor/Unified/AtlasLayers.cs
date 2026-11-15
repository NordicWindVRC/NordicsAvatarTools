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

    public enum AtlasLookupProjection
    {
        // The normal in view space with world up kept up, so the lookup doesn't roll with the head.
        ViewStabilised
    }

    // 4.3: a texture looked up by direction instead of by UV (matcap). Placement Surface blends
    // into the base colour, so it's lit; later placements blend into the lit colour.
    public sealed class AtlasLookupLayer : AtlasLayer
    {
        public readonly AtlasColorInput Source = new AtlasColorInput();
        public AtlasLookupProjection Projection = AtlasLookupProjection.ViewStabilised;
        public AtlasBlend Blend = AtlasBlend.Add;
        public float Opacity = 1f;
    }

    public enum AtlasAngleSource
    {
        // e = 1 − abs(view · normal), the view from between the eyes.
        ViewAngle
    }

    public enum AtlasAngleLightResponse
    {
        IgnoreLight,
        // Times (main light colour + ambient).
        ScaleByLight
    }

    // 4.4: colour driven by how the surface faces the viewer (rim). Usually placement Overlay.
    public sealed class AtlasAngleLayer : AtlasLayer
    {
        public AtlasAngleSource Source = AtlasAngleSource.ViewAngle;
        // Band profile: smoothstep((1 − width) − softness, (1 − width) + softness, e).
        public float Width = 0.3f;
        public float Softness = 0.1f;
        public readonly AtlasColorInput Color = new AtlasColorInput();
        // 0–1: multiplies by lerp(1, base, mix).
        public float BaseColorMix;
        public AtlasAngleLightResponse LightResponse = AtlasAngleLightResponse.IgnoreLight;
        public AtlasBlend Blend = AtlasBlend.Add;
        public float Opacity = 1f;

        public AtlasAngleLayer() { Placement = AtlasPlacement.Overlay; }
    }
}
