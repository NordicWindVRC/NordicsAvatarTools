using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // A standard scene looks are compared in: white light from a main light and from the light
    // probes.
    public struct AtlasLookScene
    {
        public string Name;
        // The main light; 0: the world has none.
        public float Direct;
        // The probes' average light, and how much more of it comes along their dominant direction
        // (and how much less against it). Where there is no main light, the angle to the light is
        // the angle to that direction.
        public float Ambient, Directional;

        public AtlasLookScene(string name, float direct, float ambient, float directional = 0f)
        {
            Name = name;
            Direct = direct;
            Ambient = ambient;
            Directional = directional;
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
        // A bright main light and a dim one, each with even ambient light.
        public static readonly AtlasLookScene[] MainLightScenes =
        {
            new AtlasLookScene("bright", 1f, 0.3f),
            new AtlasLookScene("dim", 0.4f, 0.15f)
        };

        // Those, and two worlds without a main light, where shaders differ most: each turns the
        // probes into light its own way (AtlasLightingResponse.Light).
        // - Probes only: about as much light as the bright scene gives a surface on average (a
        //   quarter of its main light + its ambient light), half as much again from one side.
        // - Dark: almost no light, where the floors (minimum brightness, minimum light) decide
        //   the look.
        // The levels are starting values with these reasons, not tuned to an avatar.
        public static readonly AtlasLookScene[] Scenes =
        {
            MainLightScenes[0],
            MainLightScenes[1],
            new AtlasLookScene("probes-only", 0f, 0.5f, 0.25f),
            new AtlasLookScene("dark", 0f, 0.02f)
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
                        AtlasLightingResponse.Display(AtlasLightingResponse.Lit(lighting, color, nDotL, scene, ramp)));
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

        /// <summary>A source's darkest, average and brightest base color: what a lighting does
        /// to its whole range, in three colors, so a search over many candidates stays short.</summary>
        public static List<Color> Representative(List<Color> bases)
        {
            var colors = new List<Color>();
            if (bases.Count == 0) return colors;
            colors.Add(bases.Aggregate((a, b) => a + b) / bases.Count);
            if (bases.Count == 1) return colors;
            colors.Add(bases.OrderBy(AtlasSourceSamples.Luminance).First());
            colors.Add(bases.OrderBy(AtlasSourceSamples.Luminance).Last());
            return colors;
        }

        /// <summary>The base colors as a lighting shows them (displayed, sRGB): per color, per
        /// scene, per angle to the light.</summary>
        public static Color[] Shown(AtlasLighting lighting, IReadOnlyList<Color> bases)
        {
            var ramp = AtlasLightingResponse.RampOf(lighting);
            var shown = new Color[bases.Count * Scenes.Length * Angles];
            int n = 0;
            foreach (var color in bases)
                foreach (var scene in Scenes)
                    for (int k = 0; k < Angles; k++)
                        shown[n++] = AtlasLightingResponse.Display(
                            AtlasLightingResponse.Lit(lighting, color, -1f + 2f * k / (Angles - 1), scene, ramp));
            return shown;
        }

        /// <summary>The mean difference between two sets of displayed colors, per channel.</summary>
        public static float Difference(Color[] a, Color[] b)
        {
            double sum = 0.0;
            for (int i = 0; i < a.Length; i++)
                sum += (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b)) / 3.0;
            return a.Length > 0 ? (float)(sum / a.Length) : 0f;
        }

        private static string Number(float value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The sources whose own lighting looks different from the lighting the output
        /// shader keeps (<paramref name="kept"/>, the written material read back), on their own
        /// base colors: one line naming each source and the scenes it differs in, or none.</summary>
        public static IEnumerable<string> LightingNotes(AtlasGeneratorJob job, AtlasUnifiedMaterial kept,
            IReadOnlyDictionary<int, float> scales = null)
        {
            if (job == null || kept == null) yield break;
            var sources = LightingDifferences(job.Sources.Select(s => s.Material).ToList(), kept.Lighting, scales);
            if (sources.Count > 0)
                yield return "The output's lighting looks different from these sources' own, as displayed brightness of their base colors: " +
                             string.Join(", ", sources) + ".";
        }

        /// <summary>The sources that a lighting shows differently from their own, on their own
        /// base colors: each with the scenes it differs in, its level and its contrast there
        /// against its own. <paramref name="scales"/>: the scale of a source's base colors in
        /// the atlas (level compensation), by index.</summary>
        public static List<string> LightingDifferences(IReadOnlyList<AtlasUnifiedMaterial> materials, AtlasLighting lighting,
            IReadOnlyDictionary<int, float> scales = null)
        {
            var sources = new List<string>();
            for (int i = 0; i < materials.Count; i++)
            {
                var material = materials[i];
                if (material == null) continue;
                var bases = AtlasSourceSamples.Bases(material);
                var inAtlas = bases;
                if (scales != null && scales.TryGetValue(i, out float scale))
                    inAtlas = bases.Select(c => new Color(c.r * scale, c.g * scale, c.b * scale, 1f)).ToList();
                var parts = new List<string>();
                foreach (var scene in Scenes)
                {
                    var own = Lighting(material.Lighting, bases, scene);
                    var shown = Lighting(lighting, inAtlas, scene);
                    var changes = new List<string>();
                    if (Mathf.Abs(shown.Level - own.Level) > Tolerance)
                        changes.Add((shown.Level > own.Level ? "brighter" : "darker") + $" ({Number(shown.Level)} for {Number(own.Level)})");
                    if (Mathf.Abs(shown.Contrast - own.Contrast) > Tolerance)
                        changes.Add((shown.Contrast > own.Contrast ? "more" : "less") + $" contrast ({Number(shown.Contrast)} for {Number(own.Contrast)})");
                    if (changes.Count > 0) parts.Add($"{scene.Name} scene: " + string.Join(", ", changes));
                }
                if (parts.Count > 0) sources.Add($"source {i + 1} ({string.Join("; ", parts)})");
            }
            return sources;
        }
    }
}
