using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // How a rim changes the surface's look at its strongest, for fitting a rim to an output that
    // can only add one after lighting (Toon Standard's: color × strength × lerp(1, base, mix), ×
    // (light + ambient) when environmental, unlit otherwise). The source's rim follows its own
    // placement and blend: on the surface it changes the base color before lighting and is lit
    // with it, a lit or overlay rim blends with the lit color by its light response, an emission
    // rim adds unlit, and its emission amount glows as well. A rim colored by the environment
    // shows the reflection probe along the normal, less its threshold: taken as the scene's
    // ambient light, which the light probes hold of the same surroundings. The output's strength, base color mix
    // and lighting are the ones whose displayed (sRGB) results differ least, on the sources' base
    // colors (AtlasSourceSamples), in the standard scenes (AtlasLook) over the angle to the light,
    // weighted by area (ATLAS_OUTPUT.md, "Matching the look"). The surface is lit by the atlas's
    // lighting (AtlasLightingResponse), which also says what light a scene without a main light
    // gives a rim that follows the light; without one, by Lambert × the direct light + the
    // ambient. Light sides and masks aren't part of it. The source's rim is a layer of the
    // reference look (AtlasReferenceLook), which a rim's effect and its cost in another place
    // (Effects, Cost) use as well.
    public static class AtlasRimResponse
    {
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

        /// <summary>A rim at strength <paramref name="rim"/> (its curve's value at a facing
        /// angle) as a layer of the reference look, in a scene: by its placement, blend, light
        /// response and glow. A rim colored by the environment shows the scene's ambient light,
        /// less its threshold.</summary>
        public static AtlasLookLayer Layer(AtlasAngleLayer layer, float rim, AtlasLookScene scene)
        {
            var color = layer.Color.Color;
            float alpha = layer.SourceAlphaIsOpacity ? color.a : 1f;
            float strength = Mathf.Max(0f, layer.Opacity) * alpha * rim;
            float seen = layer.ColorSource != AtlasAngleColorSource.Color ? Mathf.Max(0f, scene.Ambient - layer.EnvironmentThreshold) : 1f;
            var result = new AtlasLookLayer
            {
                Color = new Color(color.r * seen, color.g * seen, color.b * seen, 1f), BaseColorMix = Mathf.Clamp01(layer.BaseColorMix),
                Blend = layer.Blend, Response = layer.LightResponse, Unlit = Mathf.Max(0f, layer.EmissionAmount) * alpha * rim
            };
            if (layer.Placement == AtlasPlacement.Surface) result.Surface = strength;
            else if (layer.Placement == AtlasPlacement.Emission) result.Unlit += strength;
            else result.Overlay = strength;
            return result;
        }

        // The facing angles a rim's effect is taken at, each an equal share of the surface a
        // viewer sees of a round form (its facing squared is spread evenly).
        public const int Facings = 12;

        /// <summary>A rim's effect: the displayed look with it minus the look without it, at
        /// each sample point and facing angle. The rim is its layers (a style's unlit-side
        /// layer included), each at a share of its strength. A layer's light side is taken
        /// after its curve; shaping before the step and shrinking in shadow aren't part of it.</summary>
        public static Color[] Effects(IReadOnlyList<(AtlasAngleLayer Layer, float Share)> rim, IReadOnlyList<AtlasLookPoint> points)
        {
            var effects = new Color[points.Count * Facings];
            var layers = new AtlasLookLayer[rim.Count];
            for (int p = 0; p < points.Count; p++)
            {
                var point = points[p];
                for (int f = 0; f < Facings; f++)
                {
                    float facing = Mathf.Sqrt((f + 0.5f) / Facings);
                    for (int k = 0; k < rim.Count; k++)
                        layers[k] = Layer(rim[k].Layer, Side(rim[k].Layer.LightSide, AtlasRimCurve.Evaluate(rim[k].Layer, facing), point.NDotL) * rim[k].Share,
                            point.Scene);
                    effects[p * Facings + f] = AtlasReferenceLook.Effect(point.Base, layers, point.Light);
                }
            }
            return effects;
        }

        // 4.4: the light's side of an angle layer, on its result.
        private static float Side(AtlasAngleLightSide side, float value, float nDotL)
        {
            if (side.Mode == AtlasLightSideMode.None) return value;
            float h = nDotL * 0.5f + 0.5f;
            if (side.Invert) h = 1f - h;
            float w = side.High > side.Low ? Mathf.Clamp01((h - side.Low) / (side.High - side.Low)) : h >= side.Low ? 1f : 0f;
            if (side.Smooth) w = w * w * (3f - 2f * w);
            switch (side.Mode)
            {
                case AtlasLightSideMode.Mask: return value * Mathf.Lerp(1f, w, side.Strength);
                case AtlasLightSideMode.Scale: return value * w * side.Strength;
                default: return Mathf.Clamp01(value - (1f - h) - side.Level);
            }
        }

        /// <summary>What showing one effect costs where the source shows another, point by
        /// point (AtlasLayerCost), as a mean over the points. Null: nothing shows there.</summary>
        public static AtlasLayerCost Cost(Color[] own, Color[] shown)
        {
            var cost = new AtlasLayerCost();
            int count = own?.Length ?? shown?.Length ?? 0;
            for (int i = 0; i < count; i++)
                cost.Add(own != null ? own[i] : Color.clear, shown != null ? shown[i] : Color.clear, 1f / count);
            return cost;
        }

        /// <summary>The added rim that looks most like <paramref name="layer"/> at its peak
        /// strength <paramref name="peak"/> (its rim curve's largest value) on the sources' base
        /// colors, each source weighted by its area, under <paramref name="lighting"/> (the
        /// atlas's; null: plain Lambert). <paramref name="litOutput"/> says whether the output's rim is
        /// lit by the scene, where that isn't the fit's to choose.</summary>
        public static Fitted Fit(AtlasAngleLayer layer, float peak, IReadOnlyList<(IReadOnlyList<Color> Bases, float Weight)> sources,
            AtlasLighting lighting = null, bool? litOutput = null)
        {
            lighting = lighting ?? new AtlasLighting();
            var ramp = AtlasLightingResponse.RampOf(lighting);
            var color = layer.Color.Color;
            float mix = Mathf.Clamp01(layer.BaseColorMix);
            var samples = new List<Sample>();
            var source = new AtlasLookLayer[1];
            foreach (var (bases, weight) in sources)
            {
                if (bases.Count == 0 || weight <= 0f) continue;
                float each = weight / (bases.Count * AtlasLook.Scenes.Length * Angles);
                foreach (var b in bases)
                    foreach (var scene in AtlasLook.Scenes)
                        for (int k = 0; k < Angles; k++)
                        {
                            // The light the lighting makes of the scene, the surface under it,
                            // and the source's rim on it at its peak.
                            var light = AtlasLookLight.At(lighting, ramp, scene, -1f + 2f * k / (Angles - 1));
                            source[0] = Layer(layer, peak, scene);
                            samples.Add(new Sample
                            {
                                Base = b, Lit = light.Lit(b), Light = light.Direct + light.Ambient,
                                Target = AtlasReferenceLook.Shown(b, source, light), Weight = each
                            });
                        }
            }
            var best = new Fitted { Strength = 0f, BaseColorMix = mix, Lit = litOutput ?? false };
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
                foreach (bool litByScene in litOutput.HasValue ? new[] { litOutput.Value } : new[] { false, true })
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
