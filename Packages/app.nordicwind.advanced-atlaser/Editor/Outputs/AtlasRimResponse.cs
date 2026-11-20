using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // How a rim changes the surface's look at its strongest, for fitting a rim to an output that
    // can only add one after lighting (Toon Standard's: color × strength × lerp(1, base, mix), ×
    // (light + ambient) when environmental, unlit otherwise). The source's rim follows its own
    // placement and blend: on the surface it changes the base color before lighting and is lit
    // with it, a lit or overlay rim blends with the lit color by its light response, an emission
    // rim adds unlit, and its emission amount glows as well. The output's strength, base color mix
    // and lighting are the ones whose displayed (sRGB) results differ least, on the sources' base
    // colors (AtlasSourceSamples), in AtlasLightingResponse's two scenes over the angle to the
    // light, weighted by area (ATLAS_OUTPUT.md, "Matching the look"). Lighting is Lambert × the
    // direct light + the ambient; shadows, light sides and masks aren't part of it.
    public static class AtlasRimResponse
    {
        private static readonly (float Direct, float Ambient)[] Scenes = { (1f, 0.3f), (0.4f, 0.15f) };
        private const int Angles = 9;
        private const int MixSteps = 20;
        // Per base color mix and lighting: the strength in steps, then refined around the best
        // (golden section). An added rim's strength is at most its color.
        private const int StrengthSteps = 25;
        private const float MaxStrength = 1f, Tolerance = 0.002f;
        // A candidate replaces the best only when it looks this much closer.
        private const double Margin = 0.01;

        public struct Fitted
        {
            // The output's strength, relative to the rim's peak (as the layer's opacity). Among
            // equal looks, the base color mix nearest the source's.
            public float Strength;
            public float BaseColorMix;
            // × (light + ambient); unlit otherwise.
            public bool Lit;
        }

        /// <summary>Whether such an output shows the rim as it is: added after lighting (overlay,
        /// or emission unlit), unlit or × (light + ambient), without a glow or light color of its
        /// own.</summary>
        public static bool AddsAsIs(AtlasAngleLayer layer) =>
            layer.Blend == AtlasBlend.Add && layer.EmissionAmount <= 0f && layer.LightColorMix <= 0f &&
            ((layer.Placement == AtlasPlacement.Overlay &&
              (layer.LightResponse == AtlasLightResponse.IgnoreLight || layer.LightResponse == AtlasLightResponse.ScaleByLight)) ||
             (layer.Placement == AtlasPlacement.Emission && layer.LightResponse == AtlasLightResponse.IgnoreLight));

        private struct Sample
        {
            public Color Base, Lit, Target;
            public float Light, Weight;
        }

        /// <summary>The added rim that looks most like <paramref name="layer"/> at its peak
        /// strength <paramref name="peak"/> (its rim curve's largest value) on the sources' base
        /// colors, each source weighted by its area.</summary>
        public static Fitted Fit(AtlasAngleLayer layer, float peak, IReadOnlyList<(IReadOnlyList<Color> Bases, float Weight)> sources)
        {
            var color = layer.Color.Color;
            float alpha = layer.SourceAlphaIsOpacity ? color.a : 1f;
            float strength = Mathf.Max(0f, layer.Opacity) * alpha * peak;
            float glow = Mathf.Max(0f, layer.EmissionAmount) * alpha * peak;
            float mix = Mathf.Clamp01(layer.BaseColorMix);
            var samples = new List<Sample>();
            foreach (var (bases, weight) in sources)
            {
                if (bases.Count == 0 || weight <= 0f) continue;
                float each = weight / (bases.Count * Scenes.Length * Angles);
                foreach (var b in bases)
                    foreach (var (direct, ambient) in Scenes)
                        for (int k = 0; k < Angles; k++)
                        {
                            float nDotL = -1f + 2f * k / (Angles - 1);
                            float light = direct * Mathf.Max(0f, nDotL) + ambient;
                            var lit = b * light;
                            lit.a = 1f;
                            var tinted = Tint(color, b, mix);
                            float response = layer.LightResponse == AtlasLightResponse.ScaleByLight ? direct + ambient
                                : layer.LightResponse == AtlasLightResponse.ScaleByDirectLight ? direct : 1f;
                            Color result;
                            switch (layer.Placement)
                            {
                                case AtlasPlacement.Surface:
                                    result = AtlasBlendMath.Apply(layer.Blend, b, tinted, strength) * light;
                                    break;
                                case AtlasPlacement.Emission:
                                    result = lit + tinted * strength;
                                    break;
                                default:
                                    result = AtlasBlendMath.Apply(layer.Blend, lit, tinted * response, strength);
                                    break;
                            }
                            result += tinted * glow;
                            samples.Add(new Sample { Base = b, Lit = lit, Light = direct + ambient, Target = Display(result), Weight = each });
                        }
            }
            var best = new Fitted { Strength = 0f, BaseColorMix = mix, Lit = false };
            if (samples.Count == 0) return best;
            double lowest = double.MaxValue;
            double Cost(bool litByScene, float t, float amount)
            {
                double cost = 0.0;
                foreach (var sample in samples)
                {
                    var added = Tint(color, sample.Base, t) * (amount * (litByScene ? sample.Light : 1f));
                    var shown = Display(sample.Lit + added);
                    cost += sample.Weight * (Mathf.Abs(shown.r - sample.Target.r) + Mathf.Abs(shown.g - sample.Target.g) + Mathf.Abs(shown.b - sample.Target.b));
                }
                return cost;
            }
            // On an even base, strength and base color mix trade off: the mixes are tried nearest
            // the source's first, and a later one wins only when it looks clearly closer.
            var mixes = new List<float> { mix };
            for (int m = 0; m <= MixSteps; m++) mixes.Add(m / (float)MixSteps);
            mixes.Sort((a, b) => Mathf.Abs(a - mix).CompareTo(Mathf.Abs(b - mix)));
            float step = MaxStrength / StrengthSteps;
            foreach (float t in mixes)
                foreach (bool litByScene in new[] { false, true })
                {
                    float amount = 0f;
                    double cost = double.MaxValue;
                    for (int s = 0; s <= StrengthSteps; s++)
                    {
                        double c = Cost(litByScene, t, step * s);
                        if (c < cost) { cost = c; amount = step * s; }
                    }
                    float a = Mathf.Max(0f, amount - step), b = Mathf.Min(MaxStrength, amount + step);
                    const float ratio = 0.618034f;
                    float x = b - ratio * (b - a), y = a + ratio * (b - a);
                    double fx = Cost(litByScene, t, x), fy = Cost(litByScene, t, y);
                    while (b - a > Tolerance)
                    {
                        if (fx <= fy) { b = y; y = x; fy = fx; x = b - ratio * (b - a); fx = Cost(litByScene, t, x); }
                        else { a = x; x = y; fx = fy; y = a + ratio * (b - a); fy = Cost(litByScene, t, y); }
                    }
                    float refined = (a + b) * 0.5f;
                    double refinedCost = Cost(litByScene, t, refined);
                    if (refinedCost < cost) { cost = refinedCost; amount = refined; }
                    if (cost >= lowest * (1.0 - Margin) - 1e-9) continue;
                    lowest = cost;
                    best = new Fitted { Strength = amount / peak, BaseColorMix = t, Lit = litByScene };
                }
            return best;
        }

        private static Color Tint(Color color, Color b, float mix) =>
            new Color(color.r * Mathf.Lerp(1f, b.r, mix), color.g * Mathf.Lerp(1f, b.g, mix), color.b * Mathf.Lerp(1f, b.b, mix), 1f);

        private static Color Display(Color c) =>
            new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(c.r)), Mathf.LinearToGammaSpace(Mathf.Clamp01(c.g)),
                Mathf.LinearToGammaSpace(Mathf.Clamp01(c.b)), 1f);
    }
}
