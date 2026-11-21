using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // A standard scene looks are compared in: white light from a main light and from all around.
    public struct AtlasLookScene
    {
        public string Name;
        public float Direct, Ambient;

        public AtlasLookScene(string name, float direct, float ambient)
        {
            Name = name;
            Direct = direct;
            Ambient = ambient;
        }
    }

    // How a lighting setup looks on a set of base colors in one scene.
    public struct AtlasLookValues
    {
        // The mean displayed brightness over the angles to the light.
        public float Level;
        // The lit side's mean displayed brightness minus the shadow side's.
        public float Contrast;
    }

    // A layer's effect in the output against its effect in the source (ATLAS_OUTPUT.md,
    // "Comparing layers"). An effect is the look with the layer minus the look without it, per
    // color channel; the sums run over the sample points.
    public struct AtlasLayerCost
    {
        // What the artist didn't put there reads as a fault, while a weaker effect reads as a
        // subtler material, so what the output adds counts double. A higher factor leaves more
        // layers out; a lower one approximates more.
        public const float AddedWeight = 2f;

        // What both effects have in common: the same direction, up to the source's size.
        public float Kept;
        // What the source shows and the output doesn't.
        public float Missing;
        // What the output shows and the source doesn't.
        public float Added;

        // How much the layer changes the source's look: what leaving it out costs.
        public float Impact => Kept + Missing;
        public float Cost => Missing + AddedWeight * Added;
        // A way of showing the layer is worth using when it costs less than leaving the layer out.
        public bool BetterThanLeftOut => Cost < Impact;

        public void Add(float source, float output, float weight = 1f)
        {
            float kept = source * output > 0f ? Mathf.Min(Mathf.Abs(source), Mathf.Abs(output)) : 0f;
            Kept += kept * weight;
            Missing += (Mathf.Abs(source) - kept) * weight;
            Added += (Mathf.Abs(output) - kept) * weight;
        }

        public void Add(Color source, Color output, float weight = 1f)
        {
            Add(source.r, output.r, weight / 3f);
            Add(source.g, output.g, weight / 3f);
            Add(source.b, output.b, weight / 3f);
        }
    }

    // The look model (ATLAS_OUTPUT.md, "The look model"): the scenes every fit compares looks in,
    // and how a source's lighting looks against the lighting the output shader keeps.
    public static class AtlasLook
    {
        // A bright main light and a dim one, each with ambient light. Not ambient alone: where
        // it goes depends on how a shader turns the probes into direct light, which the unified
        // lighting doesn't record for every shader yet.
        public static readonly AtlasLookScene[] Scenes =
        {
            new AtlasLookScene("bright", 1f, 0.3f),
            new AtlasLookScene("dim", 0.4f, 0.15f)
        };

        // The angles to the light, N·L from −1 to 1.
        public const int Angles = 17;

        // A difference in displayed brightness below this isn't seen side by side: about 8 of
        // the 256 levels.
        public const float Tolerance = 0.03f;

        /// <summary>The level and contrast of the base colors lit by the setup in a scene, as
        /// displayed (sRGB).</summary>
        public static AtlasLookValues Lighting(AtlasLighting lighting, IReadOnlyList<Color> bases, AtlasLookScene scene)
        {
            var ramp = AtlasLightingResponse.RampOf(lighting);
            double all = 0.0, lit = 0.0, shadow = 0.0;
            int half = (Angles - 1) / 2;
            foreach (var color in bases)
                for (int k = 0; k < Angles; k++)
                {
                    float nDotL = -1f + 2f * k / (Angles - 1);
                    float shown = AtlasSourceSamples.Luminance(
                        AtlasLightingResponse.Display(AtlasLightingResponse.Lit(lighting, color, nDotL, scene.Direct, scene.Ambient, ramp)));
                    all += shown;
                    if (k > half) lit += shown;
                    else if (k < half) shadow += shown;
                }
            int count = Mathf.Max(1, bases.Count);
            return new AtlasLookValues
            {
                Level = (float)(all / (count * Angles)),
                Contrast = (float)((lit - shadow) / (count * half))
            };
        }

        private static string Number(float value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The sources whose own lighting looks different from the lighting the output
        /// shader keeps (<paramref name="kept"/>, the written material read back), on their own
        /// base colors: one line naming each source and the scenes it differs in, or none.</summary>
        public static IEnumerable<string> LightingNotes(AtlasGeneratorJob job, AtlasUnifiedMaterial kept)
        {
            if (job == null || kept == null) yield break;
            var sources = new List<string>();
            for (int i = 0; i < job.Sources.Count; i++)
            {
                var material = job.Sources[i].Material;
                if (material == null) continue;
                var bases = AtlasSourceSamples.Bases(material);
                var parts = new List<string>();
                foreach (var scene in Scenes)
                {
                    var own = Lighting(material.Lighting, bases, scene);
                    var shown = Lighting(kept.Lighting, bases, scene);
                    var changes = new List<string>();
                    if (Mathf.Abs(shown.Level - own.Level) > Tolerance)
                        changes.Add((shown.Level > own.Level ? "brighter" : "darker") + $" ({Number(shown.Level)} for {Number(own.Level)})");
                    if (Mathf.Abs(shown.Contrast - own.Contrast) > Tolerance)
                        changes.Add((shown.Contrast > own.Contrast ? "more" : "less") + $" contrast ({Number(shown.Contrast)} for {Number(own.Contrast)})");
                    if (changes.Count > 0) parts.Add($"{scene.Name} scene: " + string.Join(", ", changes));
                }
                if (parts.Count > 0) sources.Add($"source {i + 1} ({string.Join("; ", parts)})");
            }
            if (sources.Count > 0)
                yield return "The output's lighting looks different from these sources' own, as displayed brightness of their base colors: " +
                             string.Join(", ", sources) + ".";
        }
    }
}
