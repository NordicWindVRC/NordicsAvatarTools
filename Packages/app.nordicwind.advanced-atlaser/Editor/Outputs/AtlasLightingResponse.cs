using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // How a lighting setup looks, for choosing between setups (not for baking): a base color lit
    // by white light in the standard scenes (AtlasLook), over the angle to the light. It follows the
    // unified formulas (AtlasLighting) for the main light and the ambient light: the light color
    // model, with what stands in for the main light where a scene has none; the ramp toon
    // family's L = lerp(S, D, b), the physically based family's diffuse with its shadow strength
    // and tint, flat and unlit; the minimum light, the limit (on each light color or on the
    // shaded light), the light's multiplier and addition; and the minimum brightness. Received
    // shadows, occlusion, additional lights, masks and anything drawn on the surface aren't
    // part of it, and pre-integrated skin is read as half-Lambert.
    public static class AtlasLightingResponse
    {
        private const int Angles = AtlasLook.Angles;
        private const int RampSamples = 32;

        /// <summary>The visible difference between a material's colors lit by two setups: the
        /// mean difference of the displayed (sRGB) colors over the scenes and the angles to the
        /// light, 0–1.</summary>
        public static float Difference(AtlasLighting a, AtlasLighting b, IReadOnlyList<Color> bases)
        {
            var rampA = RampOf(a);
            var rampB = RampOf(b);
            double sum = 0.0;
            int count = 0;
            foreach (var color in bases)
                foreach (var scene in AtlasLook.Scenes)
                    for (int k = 0; k < Angles; k++)
                    {
                        float nDotL = -1f + 2f * k / (Angles - 1);
                        var x = Display(Lit(a, color, nDotL, scene, rampA));
                        var y = Display(Lit(b, color, nDotL, scene, rampB));
                        sum += (Mathf.Abs(x.r - y.r) + Mathf.Abs(x.g - y.g) + Mathf.Abs(x.b - y.b)) / 3.0;
                        count++;
                    }
            return count > 0 ? (float)(sum / count) : 0f;
        }

        /// <summary>A base color lit by the setup, at N·L = <paramref name="nDotL"/>.</summary>
        public static Color Lit(AtlasLighting lighting, Color baseColor, float nDotL, AtlasLookScene scene, Color[] ramp = null)
        {
            if (lighting.Family == AtlasLightingFamily.Unlit) return baseColor;
            Light(lighting, scene, nDotL, out float d, out float a);
            float halfLambert = nDotL * 0.5f + 0.5f;
            float term = Term(lighting.Ramp, nDotL, d);
            Color lit;
            switch (lighting.Family)
            {
                case AtlasLightingFamily.Flat:
                    lit = baseColor * (d + a);
                    break;
                case AtlasLightingFamily.RampToon:
                    // Bands with their own term shade by half-Lambert, whatever the light map is.
                    lit = RampToon(lighting.Ramp, baseColor, lighting.Ramp.BandsOwnTerm && lighting.Ramp.Bands.Count > 0 ? halfLambert : term,
                        d, a, ramp);
                    break;
                default:
                {
                    // The light × lerp(1, term, strength) × the diffuse, the ambient added, then
                    // lerp(1, tint, (1 − term) × strength).
                    float strength = lighting.ShadowStrength;
                    float diffuse = lighting.Diffuse == AtlasDiffuseModel.PreintegratedSkin ? halfLambert : Mathf.Clamp01(nDotL);
                    float light = d * Mathf.Lerp(1f, term, strength) * diffuse + a;
                    var tint = Color.Lerp(Color.white, lighting.ShadowTint, Mathf.Clamp01((1f - term) * strength));
                    lit = new Color(baseColor.r * light * tint.r, baseColor.g * light * tint.g, baseColor.b * light * tint.b, 1f);
                    break;
                }
            }
            // The limit on the shaded light: the surface shows at most its base color × the ceiling.
            if (lighting.LimitBrightness && lighting.LimitTotal)
                for (int c = 0; c < 3; c++) lit[c] = Mathf.Min(lit[c], baseColor[c] * lighting.BrightnessCeiling);
            for (int c = 0; c < 3; c++) lit[c] += baseColor[c] * lighting.MinimumBrightness;
            lit.a = 1f;
            return lit;
        }

        /// <summary>The direct and ambient light a setup makes of a scene's main light and probes
        /// (3.4), at N·L: through its light color model, then its minimum light, its limit on
        /// each light color, and its multiplier and addition.</summary>
        public static void Light(AtlasLighting lighting, AtlasLookScene scene, float nDotL, out float direct, out float ambient)
        {
            float main = scene.Direct, average = scene.Ambient;
            // The probes along their dominant direction, and as the surface gets them.
            float brightest = average + scene.Directional;
            float amount = lighting.Ambient == AtlasAmbientModel.Directional ? Mathf.Clamp01(lighting.DirectionalAmount) : 0f;
            float atNormal = Mathf.Max(0f, average + scene.Directional * nDotL * amount);
            float d, a;
            switch (lighting.ColorModel)
            {
                case AtlasLightColorModel.MergedProbes:
                {
                    // The probes' brightest light and the main light + the average, each by its
                    // share of their sum, so the total keeps the luminance of the larger part.
                    float lit = main + average, sum = brightest + lit;
                    d = sum > 1e-6f ? (brightest * brightest + lit * lit) / sum : 0f;
                    a = atNormal;
                    break;
                }
                case AtlasLightColorModel.Floored:
                    a = Mathf.Min(1f, Mathf.Max(average, 0.05f) * lighting.FloorIntensity);
                    d = Mathf.Max(main, a);
                    break;
                case AtlasLightColorModel.SplitProbes:
                    // The probes two thirds of the way along their dominant direction join the
                    // main light; the same against it is the ambient light.
                    d = main + Mathf.Max(0f, average + scene.Directional * (2f / 3f));
                    a = Mathf.Max(0f, average - scene.Directional * (2f / 3f));
                    break;
                default:
                    d = main;
                    a = atNormal;
                    // No main light: the ambient light stands in for it.
                    if (main <= 0f)
                    {
                        var contributions = lighting.Contributions;
                        d = contributions.AmbientAsMainLight *
                            (contributions.AmbientStandIn == AtlasAmbientStandIn.Brightest ? brightest : atNormal);
                        a = atNormal * contributions.AmbientWithoutMainLight;
                    }
                    break;
            }
            d = Mathf.Max(d, lighting.MinimumLight);
            a = Mathf.Max(a, lighting.MinimumLight);
            if (lighting.LimitBrightness && !lighting.LimitTotal)
            {
                d = Mathf.Min(d, lighting.BrightnessCeiling);
                a = Mathf.Min(a, lighting.BrightnessCeiling);
            }
            direct = Mathf.Max(0f, d * lighting.LightMultiplier + lighting.LightAddition) * lighting.Contributions.MainLight;
            ambient = Mathf.Max(0f, a * lighting.LightMultiplier + lighting.LightAddition);
        }

        // The shading term at N·L.
        private static float Term(AtlasRampToon ramp, float nDotL, float direct)
        {
            float halfLambert = nDotL * 0.5f + 0.5f;
            switch (ramp.Term)
            {
                case AtlasShadingTerm.Lambert: return Mathf.Clamp01(nDotL);
                case AtlasShadingTerm.ShadowsOnly: return 1f;
                case AtlasShadingTerm.RelativeBrightness:
                    // (half-Lambert × direct + ambient) between the ambient and direct + ambient.
                    return direct > 1e-4f ? Mathf.SmoothStep(0f, 1f, halfLambert) : 1f;
                case AtlasShadingTerm.Wrapped:
                {
                    float w = Mathf.Min(ramp.Wrap, 1f);
                    return Mathf.Clamp01((nDotL + w) / ((1f + w) * (1f + w * ramp.WrapNormalization)));
                }
                default: return halfLambert;
            }
        }

        // 3.2: L = lerp(S, D, b) with b = lerp(1, min(1, ramp(term) + lift), strength),
        // S = C ⊙ (ShadowDirect × direct × (b) + ShadowAmbient × A) + UntintedAmbient × A,
        // D = direct + LitAmbient × A, C = tint × the first band's color × lerp(1, base, mix).
        private static Color RampToon(AtlasRampToon ramp, Color baseColor, float term, float direct, float ambient, Color[] texture)
        {
            var value = RampValue(ramp, term + ramp.Offset, texture);
            var band = ramp.Bands.FirstOrDefault();
            var tint = ramp.ShadowTint.Color;
            var shade = band != null ? new Color(tint.r * band.Color.Color.r, tint.g * band.Color.Color.g, tint.b * band.Color.Color.b) : tint;
            var lit = new Color(0f, 0f, 0f, 1f);
            for (int c = 0; c < 3; c++)
            {
                float b = Mathf.Lerp(1f, Mathf.Min(1f, value[c] + ramp.ShadowLift), ramp.RampStrength);
                float tone = shade[c] * Mathf.Lerp(1f, baseColor[c], ramp.ShadowColorFromBase);
                float s = tone * (ramp.ShadowDirect * direct * (ramp.RampScaledShadowDirect ? b : 1f) + ramp.ShadowAmbient * ambient) +
                          ramp.UntintedAmbient * ambient;
                // Bands that replace the base color in the shadow are the shadow's color themselves.
                float shadow = ramp.BandsReplaceBase && band != null ? s : baseColor[c] * s;
                lit[c] = Mathf.Lerp(shadow, baseColor[c] * (direct + ramp.LitAmbient * ambient), b);
            }
            return lit;
        }

        // The ramp at t: the first band's edge, the keys, the ramp texture, or white.
        private static Color RampValue(AtlasRampToon ramp, float t, Color[] texture)
        {
            var band = ramp.Bands.FirstOrDefault();
            if (band != null)
            {
                if (band.Alpha <= 0f) return Color.white;
                float low = band.Border - band.Blur * 0.5f, high = band.Border + band.Blur * 0.5f;
                float e = high - low > 1e-5f ? Mathf.Clamp01((t - low) / (high - low)) : (t >= band.Border ? 1f : 0f);
                if (ramp.SmoothEdges) e = e * e * (3f - 2f * e);
                return new Color(e, e, e, 1f);
            }
            if (ramp.Keys.Count > 0) return AtlasRampTexture.Evaluate(ramp.Keys, t);
            if (texture != null && texture.Length > 0)
            {
                float u = Mathf.Clamp01(t) * (texture.Length - 1);
                int i = Mathf.Min(texture.Length - 2, Mathf.FloorToInt(u));
                return i < 0 ? texture[0] : Color.Lerp(texture[i], texture[i + 1], u - i);
            }
            return Color.white;
        }

        // A ramp texture the setup reads, as samples along u; null otherwise.
        public static Color[] RampOf(AtlasLighting lighting, int samples = RampSamples) =>
            lighting.Family == AtlasLightingFamily.RampToon && lighting.Ramp.Bands.Count == 0 && lighting.Ramp.Keys.Count == 0 &&
            lighting.Ramp.Ramp?.Texture is Texture2D texture
                ? AtlasSourceSamples.Small(texture, samples, 1)
                : null;

        /// <summary>A linear color as displayed (sRGB), clamped to 0–1.</summary>
        public static Color Display(Color c) =>
            new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(c.r)), Mathf.LinearToGammaSpace(Mathf.Clamp01(c.g)),
                Mathf.LinearToGammaSpace(Mathf.Clamp01(c.b)), 1f);
    }
}
