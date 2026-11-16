namespace NordicWind.AdvancedAtlaser.Editor
{
    public enum AtlasAdjustmentTarget
    {
        // The base colour input (2.1), before layers.
        BaseColor,
        // The base colour after the surface layers (end of S1).
        SurfaceColor,
        // One layer's source. Emission is a layer.
        Layer,
        // The finished colour (S6).
        FinalColor,
        // The material's emission, all of it.
        Emission
    }

    public enum AtlasColorOperation
    {
        // Amount in turns.
        Hue,
        // lerp(luminance, c, amount); luminance 0.3 r + 0.59 g + 0.11 b.
        Saturation,
        // lerp(0.5, c, amount)
        Contrast,
        // c × amount
        Brightness,
        // c × Color
        Tint,
        // lerp(c, 1 − smootherstep(0, 1, c), amount)
        SmoothInvert,
        // lerp(c, the sRGB transfer curve to linear of c, amount)
        SrgbDecode,
        // lerp(c, ACES(c), amount); ACES(x) = x(2.51x + 0.03) / (x(2.43x + 0.59) + 0.14), x = 0.6 c
        ToneMapAces,
        // Alloy team colours: t = Σ colourᵢ mᵢ + max(0, 1 − Σ mᵢ) (weights normalised above 1); c × (2t − 1)
        ChannelTint,
        // Oklab chroma × (1 + amount), at most the chroma of the most saturated colour of that hue; saturated.
        Chromatize,
        // sign(c) × abs(c)^amount
        Power,
        // saturate(c)
        Clamp,
        // lerp(c, the map sampled per channel at the sRGB value (r of r, g of g, b of b), back to linear, amount)
        GradientMap,
        // lerp(c, Oklab with c's lightness, the colour's hue, and c's chroma × the colour's saturation
        // (1 − min/max), saturated, amount × the map's alpha); the colour × the map's RGB
        HueTint,
        // HSV each quantised to floor(x × amount) / amount
        Posterize,
        // c + amount
        Lightness,
        // c × lerp(1, saturate((1 − l) × sqrt(l)), amount), l the direct light colour per channel:
        // brightest in dim light.
        Fluorescence
    }

    public enum AtlasHueMethod
    {
        // Rotation of the linear colour around the grey axis (1,1,1)/√3; keeps brightness roughly constant.
        GreyAxisRotation,
        // The hue of the HSV colour.
        HSV,
        // The Oklab hue angle.
        Oklab
    }

    // 1.10: one colour operation on a target, with a mask. The material keeps them in order.
    public sealed class AtlasColorAdjustment
    {
        public AtlasAdjustmentTarget Target = AtlasAdjustmentTarget.BaseColor;
        // For Target Layer.
        public AtlasLayer Layer;
        // Null: everywhere.
        public AtlasMask Mask;
        public AtlasColorOperation Operation = AtlasColorOperation.Hue;
        // Hue: turns (1 = a full turn); saturation, contrast, brightness: 1 = unchanged.
        public float Amount;
        // Tint: linear.
        public UnityEngine.Color Color = UnityEngine.Color.white;
        // Hue: every colour takes the hue Amount instead of being rotated by it (a mono tint).
        public bool HueSet;
        // Channel tint: a colour per mask channel (linear), and the mask.
        public readonly UnityEngine.Color[] ChannelColors = { UnityEngine.Color.white, UnityEngine.Color.white, UnityEngine.Color.white, UnityEngine.Color.white };
        public AtlasTextureReference ChannelMask;
        public AtlasHueMethod HueMethod = AtlasHueMethod.GreyAxisRotation;
        // Gradient map: the ramp; hue tint: a texture multiplying the colour.
        public AtlasTextureReference Map;
        // Saturation's luminance weights.
        public UnityEngine.Vector3 Luminance = new UnityEngine.Vector3(0.3f, 0.59f, 0.11f);
    }
}
