using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // How bright a surface's highlights and reflections look in an output shader, for fitting the
    // one reflectance value an output takes (not for baking). Both the source and the output are
    // drawn with the output's own response, so only what the output can't set differs: the
    // source with its own reflectance and its highlight and reflection strengths, the output with
    // its reflectance value (× the smoothness where it follows it) and the strengths it keeps
    // (masks it provides), 1 otherwise. Without a strength, an output only gets dimmer highlights
    // and reflections through a lower reflectance (F0 = 0.16 × r²), which the fit finds.
    //
    // The response: GGX highlights, D × V × F × N·L (V Smith-GGX correlated), from a white light,
    // and reflections of a uniform environment as bright as the ambient light, × F; F by the
    // output's fresnel and faded below its reflectance fade. They're added to a gray diffuse of
    // the base color's luminance, and the displayed (sRGB) results are compared over the scenes
    // with a main light (AtlasLook), a few light angles and the view angles in the light's plane. Tints, fresnel
    // strengths, steps, occlusion and normal maps aren't part of it.
    public static class AtlasSpecularResponse
    {
        private static readonly float[] LightAngles = { 0f, 20f, 40f, 60f, 80f };
        // View angles from −80° to 80° in 10° steps, so each light angle's mirror direction is one.
        private const int ViewAngles = 17;
        private const int Steps = 50;
        private const float Tolerance = 0.0005f;

        // One sample of a source.
        public struct Texel
        {
            public float Base, Smoothness, Metallic, Reflectance, Highlights, Reflections;
        }

        // What doesn't change with the reflectance, per sample, scene and angle: the diffuse, the
        // highlight without its fresnel (D × V × N·L × light), the environment, and the fresnel's
        // falloff toward 1 for highlights and for reflections.
        internal struct Entry
        {
            public int Texel;
            public float Diffuse, Highlight, Ambient, HighlightFalloff, ReflectionFalloff;
        }

        // A source as the fit sees it: its samples and its weight (its area).
        public sealed class Source
        {
            public readonly List<Texel> Texels = new List<Texel>();
            public float Weight = 1f;
            internal Entry[] Entries;
            // The displayed results under its own settings, by entry.
            internal float[] Looks;
        }

        /// <summary>A source's samples (AtlasSourceSamples): base luminance, smoothness, metallic,
        /// reflectance and the highlight and reflection strengths (0 where off).</summary>
        public static Source Sample(AtlasUnifiedMaterial material, float weight = 1f)
        {
            var response = material.Surface.Response;
            var bases = AtlasSourceSamples.Bases(material).Select(AtlasSourceSamples.Luminance).ToArray();
            var roughness = AtlasSourceSamples.Scalars(response.Roughness);
            var metallic = AtlasSourceSamples.Scalars(response.Metallic);
            var reflectance = AtlasSourceSamples.Scalars(response.Reflectance);
            var highlights = response.Highlights.Enabled ? AtlasSourceSamples.Masks(response.Highlights.Mask) : new[] { 0f };
            var reflections = response.Reflections.Enabled ? AtlasSourceSamples.Masks(response.Reflections.Mask) : new[] { 0f };
            int count = new[] { bases.Length, roughness.Length, metallic.Length, reflectance.Length, highlights.Length, reflections.Length }.Max();
            var source = new Source { Weight = weight };
            for (int i = 0; i < count; i++)
                source.Texels.Add(new Texel
                {
                    Base = bases[i % bases.Length],
                    Smoothness = 1f - roughness[i % roughness.Length],
                    Metallic = Mathf.Clamp01(metallic[i % metallic.Length]),
                    Reflectance = reflectance[i % reflectance.Length],
                    Highlights = highlights[i % highlights.Length] * Mathf.Max(0f, response.Highlights.Strength),
                    Reflections = reflections[i % reflections.Length] * Mathf.Max(0f, response.Reflections.Strength)
                });
            return source;
        }

        /// <summary>The output's reflectance value that makes the sources look most like
        /// themselves: the lowest sum of their weight × the mean visible difference, 0–1. Among
        /// equal ones, the lowest.</summary>
        public static float Fit(IReadOnlyList<Source> sources, AtlasOutputCapabilities capabilities)
        {
            float best = 0f;
            double lowest = double.MaxValue;
            for (int k = 0; k <= Steps; k++)
            {
                float value = k / (float)Steps;
                double cost = Cost(sources, capabilities, value);
                if (cost < lowest - 1e-9)
                {
                    lowest = cost;
                    best = value;
                }
            }
            // Golden section within the steps on either side.
            float a = Mathf.Max(0f, best - 1f / Steps), b = Mathf.Min(1f, best + 1f / Steps);
            const float ratio = 0.618034f;
            float c = b - ratio * (b - a), d = a + ratio * (b - a);
            double fc = Cost(sources, capabilities, c), fd = Cost(sources, capabilities, d);
            while (b - a > Tolerance)
            {
                if (fc <= fd)
                {
                    b = d; d = c; fd = fc;
                    c = b - ratio * (b - a);
                    fc = Cost(sources, capabilities, c);
                }
                else
                {
                    a = c; c = d; fc = fd;
                    d = a + ratio * (b - a);
                    fd = Cost(sources, capabilities, d);
                }
            }
            float refined = (a + b) * 0.5f;
            return Cost(sources, capabilities, refined) < lowest ? refined : best;
        }

        /// <summary>The sources' weighted mean visible difference with the output's reflectance at
        /// <paramref name="value"/>.</summary>
        public static double Cost(IReadOnlyList<Source> sources, AtlasOutputCapabilities capabilities, float value)
        {
            bool keepsHighlights = capabilities.Provides(AtlasVirtualMap.SpecularMask);
            bool keepsReflections = capabilities.Provides(AtlasVirtualMap.ReflectionMask);
            bool follows = capabilities.Reflectance == AtlasReflectanceSupport.FollowsSmoothness;
            double sum = 0.0;
            foreach (var source in sources)
            {
                Prepare(source, capabilities);
                if (source.Entries.Length == 0) continue;
                double difference = 0.0;
                for (int i = 0; i < source.Entries.Length; i++)
                {
                    var entry = source.Entries[i];
                    var texel = source.Texels[entry.Texel];
                    float output = Look(entry, texel, follows ? texel.Smoothness * value : value,
                        keepsHighlights ? texel.Highlights : 1f, keepsReflections ? texel.Reflections : 1f, capabilities);
                    difference += Mathf.Abs(output - source.Looks[i]);
                }
                sum += source.Weight * difference / source.Entries.Length;
            }
            return sum;
        }

        // The entries and the source's own looks, once per source.
        private static void Prepare(Source source, AtlasOutputCapabilities capabilities)
        {
            if (source.Entries != null) return;
            bool oneFalloff = capabilities.SpecularFresnel == AtlasSpecularFresnel.ViewHalfTimesViewNormal;
            var entries = new List<Entry>();
            for (int t = 0; t < source.Texels.Count; t++)
            {
                var texel = source.Texels[t];
                float roughness = 1f - Mathf.Clamp01(texel.Smoothness);
                float alpha = Mathf.Max(roughness * roughness, 0.0045f);
                float a2 = alpha * alpha;
                foreach (var scene in AtlasLook.MainLightScenes)
                    foreach (float lightAngle in LightAngles)
                        for (int k = 0; k < ViewAngles; k++)
                        {
                            float lightRadians = lightAngle * Mathf.Deg2Rad, viewRadians = (-80f + 10f * k) * Mathf.Deg2Rad;
                            var l = new Vector2(Mathf.Sin(lightRadians), Mathf.Cos(lightRadians));
                            var v = new Vector2(Mathf.Sin(viewRadians), Mathf.Cos(viewRadians));
                            var h = (l + v).normalized;
                            float nDotL = Mathf.Max(0f, l.y), nDotV = Mathf.Max(1e-4f, Mathf.Abs(v.y)), nDotH = Mathf.Max(0f, h.y);
                            float vDotH = Mathf.Clamp01(Vector2.Dot(v, h));
                            float denominator = nDotH * nDotH * (a2 - 1f) + 1f;
                            float distribution = a2 / (Mathf.PI * denominator * denominator + 1e-6f);
                            float visibility = Mathf.Clamp01(0.5f / Mathf.Max(Mathf.Lerp(2f * nDotL * nDotV, nDotL + nDotV, alpha), 1e-5f));
                            float highlightFalloff = oneFalloff ? Mathf.Pow((1f - vDotH) * (1f - nDotV), 5f) : Mathf.Pow(1f - vDotH, 5f);
                            entries.Add(new Entry
                            {
                                Texel = t,
                                Diffuse = texel.Base * (1f - texel.Metallic) * (nDotL * scene.Direct + scene.Ambient),
                                Highlight = distribution * visibility * nDotL * scene.Direct,
                                Ambient = scene.Ambient,
                                HighlightFalloff = highlightFalloff,
                                ReflectionFalloff = oneFalloff ? highlightFalloff : Mathf.Pow(1f - nDotV, 5f)
                            });
                        }
            }
            source.Entries = entries.ToArray();
            source.Looks = new float[source.Entries.Length];
            for (int i = 0; i < source.Entries.Length; i++)
            {
                var texel = source.Texels[source.Entries[i].Texel];
                source.Looks[i] = Look(source.Entries[i], texel, texel.Reflectance, texel.Highlights, texel.Reflections, capabilities);
            }
        }

        // The displayed gray of an entry with the reflectance and strengths given.
        private static float Look(Entry entry, Texel texel, float reflectance, float highlights, float reflections, AtlasOutputCapabilities capabilities)
        {
            float f0 = 0.16f * reflectance * reflectance * (1f - texel.Metallic) + texel.Base * texel.Metallic;
            float fade = capabilities.ReflectanceFade > 0f ? Mathf.Clamp01((reflectance + texel.Metallic) / capabilities.ReflectanceFade) : 1f;
            float specular = highlights * entry.Highlight * (f0 + (1f - f0) * entry.HighlightFalloff) +
                             reflections * entry.Ambient * (f0 + (1f - f0) * entry.ReflectionFalloff);
            return Mathf.LinearToGammaSpace(Mathf.Clamp01(entry.Diffuse + specular * fade));
        }
    }
}
