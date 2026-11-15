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
        public readonly List<AtlasLayerTarget> Targets = new List<AtlasLayerTarget>();
    }
}
