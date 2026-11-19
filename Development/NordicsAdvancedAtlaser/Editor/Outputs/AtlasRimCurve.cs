using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // A rim's strength as a function of how the surface faces the viewer (f = |N·V|), for rims
    // that depend on nothing else: what a generated matcap can hold exactly, and what a writer
    // with a simpler rim fits its own to.
    public static class AtlasRimCurve
    {
        /// <summary>Whether the layer's strength depends only on the view angle: a view-angle
        /// source with a plain color, without light-side shaping, in the band, power band or step
        /// profile.</summary>
        public static bool ViewOnly(AtlasAngleLayer layer) =>
            layer.Source == AtlasAngleSource.ViewAngle && layer.ColorSource == AtlasAngleColorSource.Color &&
            !layer.SpecularOnly && !layer.ScaleByMetallic && layer.LightSide.Mode == AtlasLightSideMode.None &&
            layer.LightSide.ShrinkInShadow <= 0f &&
            (layer.Profile == AtlasAngleProfile.Band || layer.Profile == AtlasAngleProfile.PowerBand || layer.Profile == AtlasAngleProfile.Step);

        /// <summary>The strength at facing <paramref name="facing"/> (1: facing the viewer, 0:
        /// edge-on), 0–1.</summary>
        public static float Evaluate(AtlasAngleLayer layer, float facing)
        {
            float f = Mathf.Clamp01(facing);
            float e = 1f - f;
            switch (layer.Profile)
            {
                case AtlasAngleProfile.Band:
                    float center = 1f - layer.Width;
                    return SmoothStep(center - layer.Softness, center + layer.Softness, e);
                case AtlasAngleProfile.PowerBand:
                    return SmoothStep(layer.Edge, 1f - layer.Edge, Mathf.Pow(e, Mathf.Max(0f, layer.Power)));
                case AtlasAngleProfile.Step:
                    // As an angle, linearly: 1 − acos(f) × 2/π.
                    float facingTerm = layer.LinearAngle ? 1f - Mathf.Acos(f) * (2f / Mathf.PI) : f;
                    float baseValue = layer.StepBase == AtlasAngleBase.Facing ? facingTerm : 1f - facingTerm;
                    float x = Mathf.Pow(Mathf.Max(0f, baseValue), Mathf.Max(0f, layer.Power));
                    float value = layer.High <= layer.Low ? (x >= layer.Low ? 1f : 0f)
                        : layer.Smooth ? SmoothStep(layer.Low, layer.High, x)
                        : Mathf.Clamp01((x - layer.Low) / (layer.High - layer.Low));
                    return layer.Falling ? 1f - value : value;
                default:
                    return 0f;
            }
        }

        private static float SmoothStep(float from, float to, float x)
        {
            if (to <= from) return x >= from ? 1f : 0f;
            float t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        /// <summary>A band rim like the layer's: smoothstep((1 − range) − softness, (1 − range) +
        /// softness, 1 − f), with the same midpoint and the same rise from 10% to 90% of its
        /// peak. The peak is the layer's largest strength. False when it never shows.</summary>
        public static bool FitBand(AtlasAngleLayer layer, out float range, out float softness, out float peak)
        {
            if (layer.Profile == AtlasAngleProfile.Band)
            {
                range = layer.Width;
                softness = layer.Softness;
                peak = 1f;
                return true;
            }
            const int steps = 512;
            var values = new float[steps + 1];
            peak = 0f;
            for (int i = 0; i <= steps; i++)
            {
                values[i] = Evaluate(layer, 1f - i / (float)steps);
                peak = Mathf.Max(peak, values[i]);
            }
            range = softness = 0f;
            if (peak <= 1e-4f) return false;
            float top = peak;
            float At(float level)
            {
                for (int i = 0; i <= steps; i++)
                    if (values[i] >= level * top) return i / (float)steps;
                return 1f;
            }
            float e10 = At(0.1f), e50 = At(0.5f), e90 = At(0.9f);
            range = Mathf.Clamp01(1f - e50);
            // A smoothstep rises from 10% to 90% over 0.608 of its width (2 × softness).
            softness = Mathf.Clamp01(Mathf.Max(0.001f, (e90 - e10) / 1.2168f));
            return true;
        }

        /// <summary>A matcap texture of the rim (linear, size × size, bottom row first): the
        /// strength at each lookup direction × <paramref name="color"/>.</summary>
        public static Color[] Matcap(AtlasAngleLayer layer, Color color, int size)
        {
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float ny = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float d2 = nx * nx + ny * ny;
                    float facing = d2 >= 1f ? 0f : Mathf.Sqrt(1f - d2);
                    float strength = Evaluate(layer, facing);
                    pixels[y * size + x] = new Color(color.r * strength, color.g * strength, color.b * strength, 1f);
                }
            }
            return pixels;
        }
    }
}
