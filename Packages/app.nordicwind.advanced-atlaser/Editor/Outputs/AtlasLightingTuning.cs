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
    // list holds no knowledge of any shader. For an output that shades through a generated ramp
    // texture, the ramp's shape is fitted the same way, texel by texel.
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

        // What a search found: the tuned values in the order of Values (null: as mapped), the
        // fitted ramp (null: as mapped) and the note.
        private sealed class Result
        {
            public float[] Values;
            public List<AtlasRampKey> Keys;
            public string Note;
        }

        // The results so far, by the output shader, the chosen lighting and the sources' colors;
        // null where the mapped lighting stays. The inspector resolves on every change of a
        // setting, mostly with the same lighting.
        private static readonly Dictionary<string, Result> Results = new Dictionary<string, Result>();
        private const int MostResults = 64;

        /// <summary>Tunes the plan's lighting for the output shader, in place, and adds a note
        /// naming what was tuned. Unchanged where the shader keeps the lighting as mapped, or
        /// where no value brings it closer by the margin.</summary>
        public static void Tune(AtlasGeneratorJob job, AtlasOutputPlan plan, AtlasKeptLighting kept)
        {
            if (job == null || plan?.Output == null || kept == null) return;
            var lighting = plan.Output.Lighting;
            var targets = Targets(job, lighting);
            if (targets.Count == 0) return;
            var values = Values(lighting);
            string key = Key(kept.Shader, lighting, targets);
            if (!Results.TryGetValue(key, out var result))
            {
                result = Search(lighting, values, targets, kept);
                if (Results.Count >= MostResults) Results.Clear();
                Results[key] = result;
            }
            if (result == null) return;
            if (result.Keys != null) SetRamp(lighting.Ramp, null, AtlasRampPreset.None, result.Keys);
            if (result.Values != null)
                for (int i = 0; i < values.Count; i++) values[i].Set(result.Values[i]);
            plan.Notes.Add(result.Note);
        }

        // The search, which leaves the lighting as mapped: what it found, or null where the
        // mapped lighting stays. The ramp's shape comes first, then the values, then the shape
        // again with the values found, since each is fitted with the other as it is.
        private static Result Search(AtlasLighting lighting, List<Value> values, List<Target> targets, AtlasKeptLighting kept)
        {
            float Cost() => Difference(targets, kept.Of(lighting));
            float mapped = Cost();
            if (mapped <= Margin) return null;
            foreach (var value in values) value.Mapped = value.Get();
            var ramp = lighting.Ramp;
            var mappedTexture = ramp.Ramp;
            var mappedPreset = ramp.Preset;
            var mappedKeys = ramp.Keys.ToList();
            float best = mapped;

            // The chosen lighting at the ramp's texels, taken before anything changes.
            var texels = kept.Shader.Capabilities.GeneratedRamp ? RampTargets(lighting, targets) : null;
            List<AtlasRampKey> keys = null;
            void FitRamp()
            {
                if (texels == null) return;
                var fitted = Fit(texels, targets, kept.Of(lighting));
                if (fitted == null) return;
                var texture = ramp.Ramp;
                var preset = ramp.Preset;
                var before = ramp.Keys.ToList();
                SetRamp(ramp, null, AtlasRampPreset.None, fitted);
                float cost = Cost();
                // The first fit replaces the mapped ramp by the margin; a refit only has to be closer.
                if (cost < best - (keys == null ? Margin : 1e-5f))
                {
                    best = cost;
                    keys = fitted;
                }
                else SetRamp(ramp, texture, preset, before);
            }

            FitRamp();
            float beforeValues = best;
            var effective = Effective(values, Cost).ToList();
            for (int round = 0; round < Rounds; round++)
            {
                float before = best;
                foreach (var value in effective) best = Search(value, best, Cost);
                if (before - best < 1e-4f) break;
            }
            float[] found = null;
            var tuned = new List<string>();
            if (beforeValues - best >= Margin)
            {
                found = values.Select(v => v.Get()).ToArray();
                tuned.AddRange(values.Where(v => Mathf.Abs(v.Get() - v.Mapped) > 1e-4f).Select(v => v.Name));
                FitRamp();
            }
            else
            {
                foreach (var value in values) value.Set(value.Mapped);
                best = beforeValues;
            }
            if (keys != null) tuned.Insert(0, "the shadow ramp's shape");
            foreach (var value in values) value.Set(value.Mapped);
            SetRamp(ramp, mappedTexture, mappedPreset, mappedKeys);
            if (keys == null && found == null) return null;
            return new Result
            {
                Values = found, Keys = keys,
                Note = "The output shader shows the chosen lighting differently, so these are tuned to look closer to it: " +
                       string.Join(", ", tuned) + $" (mean difference in displayed brightness {Number(mapped)}, tuned {Number(best)})."
            };
        }

        private static void SetRamp(AtlasRampToon ramp, AtlasTextureReference texture, AtlasRampPreset preset, List<AtlasRampKey> keys)
        {
            ramp.Ramp = texture;
            ramp.Preset = preset;
            ramp.Keys.Clear();
            ramp.Keys.AddRange(keys);
        }

        // The source's ramp is read this finely for the fit, so a sharp edge stays sharp.
        private const int SourceRampSamples = 256;
        // Values tried per texel: steps of a sixteenth, then steps that halve down to 1/256.
        private const int RampSteps = 16;
        private const int RampRefinements = 4;

        // The sources' colors as the chosen lighting shows them at each texel of a generated
        // ramp, read as half-Lambert: per target, per color, per scene, per texel.
        private static List<Color[]> RampTargets(AtlasLighting chosen, List<Target> targets)
        {
            if (chosen.Family == AtlasLightingFamily.Unlit) return null;
            var source = AtlasLightingResponse.RampOf(chosen, SourceRampSamples);
            var all = new List<Color[]>();
            foreach (var target in targets)
            {
                var shown = new Color[target.Bases.Count * AtlasLook.Scenes.Length * AtlasRampTexture.Width];
                int n = 0;
                foreach (var color in target.Bases)
                    foreach (var scene in AtlasLook.Scenes)
                        for (int x = 0; x < AtlasRampTexture.Width; x++)
                            shown[n++] = AtlasLightingResponse.Display(AtlasLightingResponse.Lit(chosen, color, NDotL(x), scene, source));
                all.Add(shown);
            }
            return all;
        }

        // The angle to the light a ramp texel is read at, by half-Lambert.
        private static float NDotL(int texel) => 2f * (texel + 0.5f) / AtlasRampTexture.Width - 1f;

        // The ramp that makes the kept lighting show the sources' colors closest to the chosen
        // lighting: for each texel and color channel, the value with the lowest difference over
        // the scenes and the colors. Where the ramp makes no difference it is white. Null where
        // the kept lighting doesn't read its ramp by half-Lambert.
        private static List<AtlasRampKey> Fit(List<Color[]> texels, List<Target> targets, AtlasLighting kept)
        {
            if (kept.Family != AtlasLightingFamily.RampToon || kept.Ramp.Term != AtlasShadingTerm.HalfLambert ||
                Mathf.Abs(kept.Ramp.Offset) > 1e-5f || kept.Ramp.Bands.Count > 0)
                return null;
            // The candidate is read in place of the kept ramp.
            kept.Ramp.Keys.Clear();
            var candidate = new Color[1];
            var tried = new Dictionary<int, Vector3>();
            var keys = new List<AtlasRampKey>(AtlasRampTexture.Width);
            int scenes = AtlasLook.Scenes.Length;
            for (int x = 0; x < AtlasRampTexture.Width; x++)
            {
                float nDotL = NDotL(x);
                tried.Clear();
                // The difference per color channel with the ramp at a value, in 256ths.
                Vector3 Error(int value)
                {
                    if (tried.TryGetValue(value, out var known)) return known;
                    float v = value / 256f;
                    candidate[0] = new Color(v, v, v, 1f);
                    var error = Vector3.zero;
                    for (int t = 0; t < targets.Count; t++)
                    {
                        var target = targets[t];
                        var chosen = texels[t];
                        for (int b = 0; b < target.Bases.Count; b++)
                            for (int s = 0; s < scenes; s++)
                            {
                                var shown = AtlasLightingResponse.Display(
                                    AtlasLightingResponse.Lit(kept, target.Bases[b], nDotL, AtlasLook.Scenes[s], candidate));
                                var wanted = chosen[(b * scenes + s) * AtlasRampTexture.Width + x];
                                error += target.Weight * new Vector3(Mathf.Abs(shown.r - wanted.r), Mathf.Abs(shown.g - wanted.g), Mathf.Abs(shown.b - wanted.b));
                            }
                    }
                    tried[value] = error;
                    return error;
                }
                // From white down, so the highest value wins among equals.
                var at = new Vector3Int(256, 256, 256);
                var lowest = Error(256);
                void Try(int channel, int value)
                {
                    value = Mathf.Clamp(value, 0, 256);
                    float error = Error(value)[channel];
                    if (error >= lowest[channel] - 1e-6f) return;
                    lowest[channel] = error;
                    at[channel] = value;
                }
                int step = 256 / RampSteps;
                for (int value = 256 - step; value >= 0; value -= step)
                    for (int c = 0; c < 3; c++) Try(c, value);
                for (int i = 0; i < RampRefinements; i++)
                {
                    step /= 2;
                    for (int c = 0; c < 3; c++)
                    {
                        int center = at[c];
                        Try(c, center + step);
                        Try(c, center - step);
                    }
                }
                keys.Add(new AtlasRampKey((x + 0.5f) / AtlasRampTexture.Width, new Color(at.x / 256f, at.y / 256f, at.z / 256f, 1f)));
            }
            return keys;
        }

        private static string Key(IAtlasOutputShader shader, AtlasLighting lighting, List<Target> targets)
        {
            var text = new System.Text.StringBuilder(shader.ShaderId).Append('|').Append(AtlasFieldwise.Signature(lighting));
            // A ramp texture by its content too, which the signature doesn't hold.
            if (lighting.Ramp.Ramp?.Texture is Texture2D ramp) text.Append('|').Append(ramp.imageContentsHash);
            foreach (var target in targets)
            {
                text.Append('|').Append(target.Weight.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                foreach (var color in target.Bases) text.Append(':').Append(ColorUtility.ToHtmlStringRGB(color)).Append(color.maxColorComponent.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            return text.ToString();
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
        // weighted by its area in the atlas.
        private static List<Target> Targets(AtlasGeneratorJob job, AtlasLighting chosen)
        {
            var targets = new List<Target>();
            foreach (var source in job.Sources)
            {
                if (source.Material == null) continue;
                var bases = AtlasLook.Representative(AtlasSourceSamples.Bases(source.Material));
                if (bases.Count == 0) continue;
                targets.Add(new Target
                {
                    Weight = source.UVRegion.width * source.UVRegion.height,
                    Bases = bases,
                    Shown = AtlasLook.Shown(chosen, bases)
                });
            }
            // Without a layout yet, every source counts alike.
            if (targets.Sum(t => t.Weight) <= 0f)
                foreach (var target in targets) target.Weight = 1f;
            return targets;
        }

        // The mean difference between the sources' colors as the chosen lighting shows them and
        // as the kept one does, by area.
        private static float Difference(List<Target> targets, AtlasLighting kept)
        {
            double sum = 0.0, weights = 0.0;
            foreach (var target in targets)
            {
                sum += target.Weight * AtlasLook.Difference(target.Shown, AtlasLook.Shown(kept, target.Bases));
                weights += target.Weight;
            }
            return weights > 0.0 ? (float)(sum / weights) : 0f;
        }
    }
}
