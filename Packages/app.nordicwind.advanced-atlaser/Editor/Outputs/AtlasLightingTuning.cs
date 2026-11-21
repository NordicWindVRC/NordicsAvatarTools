using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Tuning the lighting by look (ATLAS_OUTPUT.md, "Tuning the lighting"). The chosen lighting
    // is mapped to the output shader by its values. Where the shader shows it differently (the
    // kept lighting, AtlasKeptLighting), the values below are searched for the ones that make
    // the sources' colors look closest to the chosen lighting in the standard scenes. A value
    // the shader doesn't have changes nothing in the kept lighting and stays as mapped, so the
    // list holds no knowledge of any shader.
    public static class AtlasLightingTuning
    {
        // The mean displayed difference below which the mapped lighting stays as it is, and by
        // which a tuned one must be closer to replace it, so a result doesn't flip on a small
        // change. A third of the visible tolerance: a difference is rarely spread evenly over the
        // scenes and the angles to the light.
        public const float Margin = AtlasLook.Tolerance / 3f;

        // Points tried across a value's range, then the steps that halve around the best one.
        private const int Points = 9;
        private const int Refinements = 4;
        private const int Rounds = 2;

        // One value of the unified lighting that tuning may change.
        private sealed class Value
        {
            public readonly string Name;
            public readonly float Min, Max;
            public readonly Func<float> Get;
            public readonly Action<float> Set;
            // A choice among whole numbers (an enumeration) instead of a range.
            public bool Whole;
            public float Mapped;

            public Value(string name, float min, float max, Func<float> get, Action<float> set)
            {
                Name = name;
                Min = min;
                Max = max;
                Get = get;
                Set = set;
            }
        }

        // The values that set how bright the surface is and how much darker its shadow side:
        // where the light comes from, how much of it there is, its floors and its ceiling, and
        // how dark the shadow gets. What the artist chose as a look of its own (the family, the
        // ramp's shape, the shading term, the shadow's hue) isn't among them.
        private static List<Value> Values(AtlasLighting l)
        {
            var ramp = l.Ramp;
            // The shadow's color: the first band's where the shading is banded.
            var shade = ramp.Bands.Count > 0 ? ramp.Bands[0].Color : ramp.ShadowTint;
            var mapped = shade.Color;
            float level = AtlasSourceSamples.Luminance(mapped);
            return new List<Value>
            {
                new Value("light color model", 0f, Enum.GetValues(typeof(AtlasLightColorModel)).Length - 1,
                    () => (float)l.ColorModel, v => l.ColorModel = (AtlasLightColorModel)Mathf.RoundToInt(v)) { Whole = true },
                new Value("light multiplier", 0.5f, 2f, () => l.LightMultiplier, v => l.LightMultiplier = v),
                new Value("light addition", 0f, 0.5f, () => l.LightAddition, v => l.LightAddition = v),
                new Value("brightness ceiling", 0.5f, 2f, () => l.BrightnessCeiling, v => l.BrightnessCeiling = v),
                new Value("minimum light", 0f, 1f, () => l.MinimumLight, v => l.MinimumLight = v),
                new Value("minimum brightness", 0f, 1f, () => l.MinimumBrightness, v => l.MinimumBrightness = v),
                new Value("directional ambient amount", 0f, 1f, () => l.DirectionalAmount, v => l.DirectionalAmount = v),
                new Value("shadow lift", 0f, 1f, () => ramp.ShadowLift, v => ramp.ShadowLift = v),
                new Value("ramp strength", 0f, 1f, () => ramp.RampStrength, v => ramp.RampStrength = v),
                new Value("shadow strength", 0f, 1f, () => l.ShadowStrength, v => l.ShadowStrength = v),
                // The mapped color at another brightness, so its hue stays the artist's.
                new Value("shadow color brightness", 0f, 1f, () => AtlasSourceSamples.Luminance(shade.Color),
                    v => shade.Color = Mathf.Abs(v - level) < 1e-6f ? mapped : AtLevel(mapped, level, v))
            };
        }

        private static Color AtLevel(Color color, float level, float target)
        {
            if (level < 1e-4f) return new Color(target, target, target, color.a);
            float scale = target / level;
            return new Color(Mathf.Min(1f, color.r * scale), Mathf.Min(1f, color.g * scale), Mathf.Min(1f, color.b * scale), color.a);
        }

        // A source's colors as the chosen lighting shows them, per color, scene and angle to the
        // light.
        private sealed class Target
        {
            public float Weight;
            public List<Color> Bases;
            public Color[] Shown;
        }

        /// <summary>Tunes the plan's lighting for the output shader, in place, and adds a note
        /// naming what was tuned. Unchanged where the shader keeps the lighting as mapped, or
        /// where no value brings it closer by the margin.</summary>
        public static void Tune(AtlasGeneratorJob job, AtlasOutputPlan plan, IAtlasOutputShader shader)
        {
            if (job == null || plan?.Output == null) return;
            var lighting = plan.Output.Lighting;
            using (var kept = AtlasKeptLighting.For(shader))
            {
                if (kept == null) return;
                var targets = Targets(job, lighting);
                if (targets.Count == 0) return;
                float Cost() => Difference(targets, kept.Of(lighting));
                float mapped = Cost();
                if (mapped <= Margin) return;

                var values = Values(lighting);
                foreach (var value in values) value.Mapped = value.Get();
                float best = mapped;
                var effective = Effective(values, Cost).ToList();
                for (int round = 0; round < Rounds; round++)
                {
                    float before = best;
                    foreach (var value in effective) best = Search(value, best, Cost);
                    if (before - best < 1e-4f) break;
                }
                if (mapped - best < Margin)
                {
                    foreach (var value in values) value.Set(value.Mapped);
                    return;
                }
                var tuned = values.Where(v => Mathf.Abs(v.Get() - v.Mapped) > 1e-4f).Select(v => v.Name).ToList();
                plan.Notes.Add("The output shader shows the chosen lighting differently, so these are tuned to look closer to it: " +
                               string.Join(", ", tuned) + $" (mean difference in displayed brightness {Number(mapped)}, tuned {Number(best)}).");
            }
        }

        private static string Number(float value) => value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

        // The values the output shader has: the kept lighting looks different at the two ends of
        // their range. Each is left at its mapped value.
        private static IEnumerable<Value> Effective(List<Value> values, Func<float> cost)
        {
            foreach (var value in values)
            {
                bool effect = false;
                float first = 0f;
                int steps = value.Whole ? Mathf.RoundToInt(value.Max - value.Min) : 1;
                for (int i = 0; i <= steps && !effect; i++)
                {
                    value.Set(Mathf.Lerp(value.Min, value.Max, (float)i / steps));
                    float c = cost();
                    if (i == 0) first = c;
                    else effect = Mathf.Abs(c - first) > 1e-5f;
                }
                value.Set(value.Mapped);
                if (effect) yield return value;
            }
        }

        // The value's best setting with the others as they are: the lowest difference among
        // points across its range, then among steps that halve around the best one. The value it
        // had stays unless another is closer.
        private static float Search(Value value, float best, Func<float> cost)
        {
            float at = value.Get();
            void Try(float candidate)
            {
                candidate = Mathf.Clamp(candidate, value.Min, value.Max);
                if (Mathf.Abs(candidate - at) < 1e-6f) return;
                value.Set(candidate);
                float c = cost();
                if (c < best - 1e-5f)
                {
                    best = c;
                    at = candidate;
                }
            }
            if (value.Whole)
            {
                for (float v = value.Min; v <= value.Max + 0.5f; v += 1f) Try(v);
            }
            else
            {
                for (int i = 0; i < Points; i++) Try(Mathf.Lerp(value.Min, value.Max, i / (Points - 1f)));
                float step = (value.Max - value.Min) / (Points - 1f);
                for (int i = 0; i < Refinements; i++)
                {
                    step *= 0.5f;
                    float center = at;
                    Try(center - step);
                    Try(center + step);
                }
            }
            value.Set(at);
            return best;
        }

        // Each source's darkest, average and brightest base color under the chosen lighting,
        // weighted by its area in the atlas. Three colors show what the lighting does to a
        // source's whole range, and keep a search of a few hundred candidates short.
        private static List<Target> Targets(AtlasGeneratorJob job, AtlasLighting chosen)
        {
            var targets = new List<Target>();
            foreach (var source in job.Sources)
            {
                if (source.Material == null) continue;
                var all = AtlasSourceSamples.Bases(source.Material);
                if (all.Count == 0) continue;
                var bases = new List<Color> { all.Aggregate((a, b) => a + b) / all.Count };
                if (all.Count > 1)
                {
                    bases.Add(all.OrderBy(AtlasSourceSamples.Luminance).First());
                    bases.Add(all.OrderBy(AtlasSourceSamples.Luminance).Last());
                }
                targets.Add(new Target
                {
                    Weight = source.UVRegion.width * source.UVRegion.height,
                    Bases = bases,
                    Shown = Shown(chosen, bases)
                });
            }
            // Without a layout yet, every source counts alike.
            if (targets.Sum(t => t.Weight) <= 0f)
                foreach (var target in targets) target.Weight = 1f;
            return targets;
        }

        private static Color[] Shown(AtlasLighting lighting, List<Color> bases)
        {
            var ramp = AtlasLightingResponse.RampOf(lighting);
            var scenes = AtlasLook.Scenes;
            var shown = new Color[bases.Count * scenes.Length * AtlasLook.Angles];
            int n = 0;
            foreach (var color in bases)
                foreach (var scene in scenes)
                    for (int k = 0; k < AtlasLook.Angles; k++)
                        shown[n++] = AtlasLightingResponse.Display(
                            AtlasLightingResponse.Lit(lighting, color, -1f + 2f * k / (AtlasLook.Angles - 1), scene, ramp));
            return shown;
        }

        // The mean difference between the sources' colors as the chosen lighting shows them and
        // as the kept one does, by area.
        private static float Difference(List<Target> targets, AtlasLighting kept)
        {
            double sum = 0.0, weights = 0.0;
            foreach (var target in targets)
            {
                var shown = Shown(kept, target.Bases);
                double difference = 0.0;
                for (int i = 0; i < shown.Length; i++)
                {
                    var a = target.Shown[i];
                    var b = shown[i];
                    difference += (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b)) / 3.0;
                }
                sum += target.Weight * difference / shown.Length;
                weights += target.Weight;
            }
            return weights > 0.0 ? (float)(sum / weights) : 0f;
        }
    }
}
