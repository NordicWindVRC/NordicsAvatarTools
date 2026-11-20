using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // A unified blend (AtlasBlend) on linear colors, as its declaration defines it.
    public static class AtlasBlendMath
    {
        public static float Apply(AtlasBlend blend, float b, float l, float o)
        {
            switch (blend)
            {
                case AtlasBlend.Replace: return Mathf.Lerp(b, l, o);
                case AtlasBlend.Multiply: return b * Mathf.Lerp(1f, l, o);
                case AtlasBlend.MultiplyDouble: return b * Mathf.Lerp(1f, 4.595f * l, o);
                case AtlasBlend.Subtract: return Mathf.Max(b - l * o, 0f);
                case AtlasBlend.Screen: return Mathf.Lerp(b, 1f - (1f - b) * (1f - l), o);
                case AtlasBlend.Overlay: return Mathf.Lerp(b, b < 0.5f ? 2f * b * l : 1f - 2f * (1f - b) * (1f - l), o);
                case AtlasBlend.Lighten: return Mathf.Lerp(b, Mathf.Max(b, l), o);
                case AtlasBlend.Darken: return Mathf.Lerp(b, Mathf.Min(b, l), o);
                case AtlasBlend.Mixed: return Mathf.Lerp(b, b + b * l, o);
                default: return b + l * o;
            }
        }

        public static Color Apply(AtlasBlend blend, Color b, Color l, float o)
        {
            if (blend == AtlasBlend.TintLuminance || blend == AtlasBlend.AddToLuminance)
            {
                float luminance = AtlasSourceSamples.Luminance(b);
                Color Channel(System.Func<float, float> f) => new Color(f(l.r), f(l.g), f(l.b), b.a);
                var target = blend == AtlasBlend.TintLuminance ? Channel(c => luminance * c) : Channel(c => Mathf.Min(luminance + c, 1f));
                return new Color(Mathf.Lerp(b.r, target.r, o), Mathf.Lerp(b.g, target.g, o), Mathf.Lerp(b.b, target.b, o), b.a);
            }
            return new Color(Apply(blend, b.r, l.r, o), Apply(blend, b.g, l.g, o), Apply(blend, b.b, l.b, o), b.a);
        }
    }

    // The view-dependent surface layers (S1 matcaps and rims) at their average look over the view,
    // for bakes of what reads the surface color after them: an emission that takes the base color
    // (Poiyomi applies its matcaps and rims to the base color before its emissions read it). A
    // matcap's average is its texture's mean over the lookup disc (each part of the disc stands for
    // an equal share of the visible surface) × its color; a rim's is its color × its strength
    // averaged over the visible surface, ∫ rim(f) 2f df over the facing f = |N·V| (a sphere's share
    // of each facing). Base color mixes take the color so far, light color mixes white light. Their
    // masks apply; faces, shadows and light sides don't.
    public static class AtlasViewLayers
    {
        private const int CoverageSteps = 32;

        /// <summary>Applies the source's view-dependent surface layers, in order, at their average
        /// look to <paramref name="surface"/> (linear, the source's UV 0–1).</summary>
        public static void Apply(AtlasUnifiedMaterial source, NordicsTextureWrapper surface, int width, int height)
        {
            foreach (var layer in source.Layers)
            {
                if (!Average(layer, out var color, out float opacity, out var blend, out float mix)) continue;
                Color Blend(Color b, float mask)
                {
                    var tinted = new Color(color.r * Mathf.Lerp(1f, b.r, mix), color.g * Mathf.Lerp(1f, b.g, mix), color.b * Mathf.Lerp(1f, b.b, mix), 1f);
                    return AtlasBlendMath.Apply(blend, b, tinted, opacity * mask);
                }
                if (layer.Mask != null && AtlasMapEvaluator.CanEvaluate(layer.Mask, source))
                    using (var mask = AtlasMapEvaluator.ScaledMask(layer.Mask, 1f, width, height, source))
                        AtlasPixels.Map(surface, mask, (b, m) => Blend(b, m.r));
                else AtlasPixels.Map(surface, b => Blend(b, 1f));
            }
        }

        // A layer's average color and opacity, its blend and base color mix; false when it isn't a
        // view-dependent surface layer that shows.
        private static bool Average(AtlasLayer layer, out Color color, out float opacity, out AtlasBlend blend, out float mix)
        {
            color = Color.white;
            opacity = 0f;
            blend = AtlasBlend.Add;
            mix = 0f;
            if (!layer.Enabled || layer.Placement != AtlasPlacement.Surface) return false;
            switch (layer)
            {
                case AtlasLookupLayer lookup:
                {
                    var mean = AtlasMatcapFeature.MeanColor(lookup);
                    var c = lookup.Source.Color;
                    color = new Color(mean.r * c.r, mean.g * c.g, mean.b * c.b, 1f);
                    opacity = lookup.Opacity * (lookup.SourceAlphaIsOpacity ? c.a * mean.a : 1f);
                    blend = lookup.Blend;
                    mix = Mathf.Clamp01(lookup.BaseColorMix);
                    break;
                }
                case AtlasAngleLayer angle when AtlasRimCurve.ViewOnly(angle):
                {
                    var c = angle.Color.Color;
                    color = new Color(c.r, c.g, c.b, 1f);
                    opacity = angle.Opacity * Coverage(angle) * (angle.SourceAlphaIsOpacity ? c.a : 1f);
                    blend = angle.Blend;
                    mix = Mathf.Clamp01(angle.BaseColorMix);
                    break;
                }
                default:
                    return false;
            }
            return opacity > 0f;
        }

        /// <summary>A rim's strength averaged over the visible surface: ∫ rim(f) 2f df.</summary>
        public static float Coverage(AtlasAngleLayer layer)
        {
            float sum = 0f;
            for (int i = 0; i < CoverageSteps; i++)
            {
                float f = (i + 0.5f) / CoverageSteps;
                sum += AtlasRimCurve.Evaluate(layer, f) * 2f * f;
            }
            return sum / CoverageSteps;
        }
    }
}
