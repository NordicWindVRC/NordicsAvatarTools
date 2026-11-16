using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Strands: short cards grown per triangle (lilToon style). Shells: the mesh redrawn in
    // stacked, pushed-out layers with a strand pattern cut into each.
    public enum AtlasFurMethod { Strands, Shells }

    // 5.2: fur grown from the surface by an extra pass. The fur uses the material's own surface,
    // lighting and layers; these settings describe its shape, transparency and fur-only shading.
    public sealed class AtlasFur
    {
        public bool Enabled;
        public AtlasFurMethod Method = AtlasFurMethod.Strands;
        // Cutout (the default) or alpha blended without depth writes.
        public bool Transparent;
        public bool AlphaToCoverage = true;
        // Colour of the fur itself; white when the fur simply shows the surface colour.
        public readonly AtlasColorInput Color = new AtlasColorInput();
        // Whether the fur colour texture also tints the skin underneath.
        public bool ColorAppliesToSurface;
        // 0–1: how much the strands narrow toward the tip.
        public float TipTaper;
        // Fur bends away from a VRChat contact (1–8 selects which; 0 = none).
        public int ContactIndex;
        public float ContactPush = 1f;
        // Fewer layers from LevelOfDetailStart, down to one by LevelOfDetailEnd (metres from the
        // camera); both 0 = always full.
        public float LevelOfDetailStart;
        public float LevelOfDetailEnd;
        // Strand length in metres, scaled by LengthMask where present (its strength lerps from 1).
        public float Length = 0.02f;
        public AtlasMask LengthMask;
        // Default strand direction in tangent space (z = along the normal).
        public Vector3 Direction = new Vector3(0f, 0f, 1f);
        // Per-texel direction (interpretation Direction); null when unused.
        public AtlasTextureReference DirectionMap;
        public float DirectionMapStrength = 1f;
        // Take the strand direction from vertex colours instead of the map or the constant.
        public bool DirectionFromVertexColors;
        // 0–1: how far strands bend downward under gravity.
        public float Gravity = 0.25f;
        // Random per-strand variation of the direction.
        public float Randomness;
        // Strand pattern; strands appear where it is bright.
        public AtlasTextureReference StrandNoise;
        // Thins the fur; null when unused.
        public AtlasMask AlphaMask;
        // 0–1 darkening toward the strand roots.
        public float RootOcclusion;
        public int LayerCount = 2;
        // 0–1: how much of the strand keeps full width from the root up.
        public float RootWidth;
        // 0–1: strand thickness relative to the pattern.
        public float Width = 0.15f;
        public readonly AtlasWind Wind = new AtlasWind();
        // Rim on the strands only, times the direct light colour. Linear.
        public Color RimColor = UnityEngine.Color.black;
        public float RimPower = 3f;
        // 0–1: suppresses the rim on the lit side.
        public float RimLitSideSuppression = 0.5f;
        // Settings of the fur pass alone.
        public readonly AtlasPassSettings Pass = new AtlasPassSettings { Cull = AtlasCull.Off };
    }

}
