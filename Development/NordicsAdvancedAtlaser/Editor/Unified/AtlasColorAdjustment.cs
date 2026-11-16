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
        Fluorescence,
        // lerp(c, a black and white photograph of c (see AtlasBlackWhite), amount)
        BlackWhite,
        // HSV: hue + Hsv.x (wrapped), saturation + Hsv.y and value + Hsv.z (each saturated)
        HsvOffset,
        // Polyphony Digital's GT7 tone mapping (their 2025 reference); Settings: colour accuracy,
        // chroma fade start, chroma fade end.
        ToneMapGt7,
        // Bright emission eased toward white as its intensity grows, colours in the normal range
        // unchanged (Poiyomi Pro's neutral surface emission); Settings: HDR retention, hue correction.
        NeutralEmission
    }

    // Poiyomi 11's Black & White, on linear values. y = luminance(c) (0.3, 0.59, 0.11); T =
    // clamp(Typical, 0.001, 0.94) and H = Highlight (linear; picked: the luminances of the picked
    // colours). shade(x) = x/T + k(a − 0.05(1 − e^(−20a))), a = max(x/T − 1, 0), with k = 1.1 /
    // (clamp(0.87/T, 1.25, 2.1) − 1) − 1, or picked k = clamp((HighlightStrength − H/T) / max(aH −
    // 0.05(1 − e^(−20 aH)), 0.001), −0.9, 12) × saturate((H/T − 1.05) × 10). h = clamp(shade(H), 1, 2.5);
    // base = decode(lerp(0.24, encode(0.913 / (1 + 0.385(h − 1))), Lightness)) (sRGB). s = log2(shade(y));
    // s = 2^(Contrast × (s < 0 ? s / sqrt(1 + s²/9) : s)); r = 0.95 − base; the photograph is
    // (s ≤ 1 ? base × s : base + r(1 − e^((1 − s) base / r))) × Tint. With Select, the amount ×
    // saturate((SelectRange − |Δ|) / (SelectRange / 2)), Δ = (Oklab ab / max(L, 0.05) of c minus of
    // SelectColor, (L − L_select) × 0.3).
    public sealed class AtlasBlackWhite
    {
        public float Lightness = 1f;
        public float Contrast = 1f;
        // Linear.
        public UnityEngine.Color Tint = UnityEngine.Color.white;
        // The texture's typical tone and highlight (linear).
        public float Typical = 0.21404114f;
        public float Highlight = 0.44798841f;
        // From picked colours (linear): their luminances replace Typical and Highlight.
        public bool Picked;
        public UnityEngine.Color PickedBase = new UnityEngine.Color(0.21404114f, 0.21404114f, 0.21404114f, 1f);
        public UnityEngine.Color PickedHighlight = UnityEngine.Color.black;
        public float HighlightStrength = 1.6f;
        public bool Select;
        // Linear.
        public UnityEngine.Color SelectColor = UnityEngine.Color.red;
        public float SelectRange = 0.2f;
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
        // Black and white's parameters; null for the other operations.
        public AtlasBlackWhite BlackWhite;
        // HSV offset.
        public UnityEngine.Vector3 Hsv;
        // Final colour: the emission goes through it too, on its own, before it's added.
        public bool IncludeEmission;
        // The operation's own values (see the operations).
        public UnityEngine.Vector4 Settings;
    }
}
