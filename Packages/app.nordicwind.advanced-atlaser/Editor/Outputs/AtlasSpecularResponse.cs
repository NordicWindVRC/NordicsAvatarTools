using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // How bright a surface's highlights and reflections look, for fitting the one reflectance
    // value an output takes (not for baking). Each source is drawn twice in the reference
    // look's lighting stage (AtlasReferenceLook), under its own lighting:
    // - as its own shader shows it: its reflectance, its highlight and reflection strengths,
    //   and its shader's fresnel, reflectance fade and fresnel strength of reflections;
    // - as the output shader shows it: the output's reflectance value (× the smoothness where
    //   it follows it), the strengths it keeps (masks it provides), 1 otherwise, and the
    //   output shader's fresnel as its own parser describes the shader
    //   (AtlasKeptMaterial.Described; the unified defaults where it isn't known).
    // Without a strength, an output only gets dimmer highlights and reflections through a lower
    // reflectance (F0 = 0.16 × r²), which the fit finds; a fresnel that rises less toward
    // grazing angles is made up for the same way, as far as one value can.
    //
    // The diffuse light and the direct light of highlights are what the source's lighting makes
    // of each scene; reflections show an environment as bright as the scene's ambient light.
    // They're added to a gray diffuse of the base color's luminance, and the displayed (sRGB)
    // results are compared over the four scenes (AtlasLook), a few light angles and the view
    // angles in the light's plane. Tints, steps, occlusion, normal maps, and how reflections
    // fall off with roughness aren't part of it.
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
        // highlight without its fresnel, the environment, and how far the fresnel of highlights
        // and of reflections rises from F0 there, in the source's shader and in the output's.
        internal struct Entry
        {
            public int Texel;
            public float Diffuse, Highlight, Environment;
            public float OwnHighlightRise, OwnReflectionRise, HighlightRise, ReflectionRise;
        }

        // A source as the fit sees it: its samples, its weight (its area), its lighting, and
        // how its shader computes highlights and reflections.
        public sealed class Source
        {
            public readonly List<Texel> Texels = new List<Texel>();
            public float Weight = 1f;
            public AtlasLighting Lighting = new AtlasLighting();
            public AtlasSurfaceResponse Response = Unified;
            internal Entry[] Entries;
            // The displayed results under its own settings, by entry.
            internal float[] Looks;
        }

        /// <summary>A source's samples (AtlasSourceSamples): base luminance, smoothness, metallic,
        /// reflectance and the highlight and reflection strengths (0 where off).</summary>
        public static Source Sample(AtlasUnifiedMaterial material, float weight = 1f)
        {
            var response = material.Surface.Response;
            // By position, like the other maps' samples: each sample is one place on the surface.
            var bases = AtlasSourceSamples.BasesByPosition(material).Select(AtlasSourceSamples.Luminance).ToArray();
            var roughness = AtlasSourceSamples.Scalars(response.Roughness);
            var metallic = AtlasSourceSamples.Scalars(response.Metallic);
            var reflectance = AtlasSourceSamples.Scalars(response.Reflectance);
            var highlights = response.Highlights.Enabled ? AtlasSourceSamples.Masks(response.Highlights.Mask) : new[] { 0f };
            var reflections = response.Reflections.Enabled ? AtlasSourceSamples.Masks(response.Reflections.Mask) : new[] { 0f };
            int count = new[] { bases.Length, roughness.Length, metallic.Length, reflectance.Length, highlights.Length, reflections.Length }.Max();
            var source = new Source { Weight = weight, Lighting = material.Lighting, Response = response };
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
            var shown = Shown(capabilities);
            float best = 0f;
            double lowest = double.MaxValue;
            for (int k = 0; k <= Steps; k++)
            {
                float value = k / (float)Steps;
                double cost = Cost(sources, capabilities, value, shown);
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
            double fc = Cost(sources, capabilities, c, shown), fd = Cost(sources, capabilities, d, shown);
            while (b - a > Tolerance)
            {
                if (fc <= fd)
                {
                    b = d; d = c; fd = fc;
                    c = b - ratio * (b - a);
                    fc = Cost(sources, capabilities, c, shown);
                }
                else
                {
                    a = c; c = d; fc = fd;
                    d = a + ratio * (b - a);
                    fd = Cost(sources, capabilities, d, shown);
                }
            }
            float refined = (a + b) * 0.5f;
            return Cost(sources, capabilities, refined, shown) < lowest ? refined : best;
        }

        /// <summary>The sources' weighted mean visible difference with the output's reflectance at
        /// <paramref name="value"/>.</summary>
        public static double Cost(IReadOnlyList<Source> sources, AtlasOutputCapabilities capabilities, float value) =>
            Cost(sources, capabilities, value, Shown(capabilities));

        private static double Cost(IReadOnlyList<Source> sources, AtlasOutputCapabilities capabilities, float value, AtlasSurfaceResponse shown)
        {
            bool keepsHighlights = capabilities.Provides(AtlasVirtualMap.SpecularMask);
            bool keepsReflections = capabilities.Provides(AtlasVirtualMap.ReflectionMask);
            bool follows = capabilities.Reflectance == AtlasReflectanceSupport.FollowsSmoothness;
            double sum = 0.0;
            foreach (var source in sources)
            {
                Prepare(source, shown);
                if (source.Entries.Length == 0) continue;
                double difference = 0.0;
                for (int i = 0; i < source.Entries.Length; i++)
                {
                    var entry = source.Entries[i];
                    var texel = source.Texels[entry.Texel];
                    float output = Look(entry, texel, shown, entry.HighlightRise, entry.ReflectionRise, follows ? texel.Smoothness * value : value,
                        keepsHighlights ? texel.Highlights : 1f, keepsReflections ? texel.Reflections : 1f);
                    difference += Mathf.Abs(output - source.Looks[i]);
                }
                sum += source.Weight * difference / source.Entries.Length;
            }
            return sum;
        }

        private static readonly AtlasSurfaceResponse Unified = new AtlasSurfaceResponse();

        // The output shader's surface response as its parser describes the shader: its fresnel,
        // its reflectance fade and the fresnel strength of its reflections.
        private static AtlasSurfaceResponse Shown(AtlasOutputCapabilities capabilities) =>
            AtlasKeptMaterial.Described(capabilities)?.Surface.Response ?? Unified;

        /// <summary>Whether the output shader computes highlights and reflections as the
        /// source's shader does: the same fresnel, reflectance fade and fresnel strength of
        /// reflections. Then the source's own values give it the same look.</summary>
        public static bool SameShading(AtlasSurfaceResponse source, AtlasOutputCapabilities capabilities)
        {
            var shown = Shown(capabilities);
            return source.Fresnel == shown.Fresnel && Mathf.Abs(source.ReflectanceFade - shown.ReflectanceFade) <= 1e-4f &&
                   Mathf.Abs(source.Reflections.FresnelStrength - shown.Reflections.FresnelStrength) <= 1e-4f;
        }

        // The entries and the source's own looks, once per source.
        private static void Prepare(Source source, AtlasSurfaceResponse shown)
        {
            if (source.Entries != null) return;
            var ramp = AtlasLightingResponse.RampOf(source.Lighting);
            var entries = new List<Entry>();
            for (int t = 0; t < source.Texels.Count; t++)
            {
                var texel = source.Texels[t];
                float roughness = 1f - Mathf.Clamp01(texel.Smoothness);
                float gray = texel.Base * (1f - texel.Metallic);
                foreach (var scene in AtlasLook.Scenes)
                    foreach (float lightAngle in LightAngles)
                    {
                        float lightRadians = lightAngle * Mathf.Deg2Rad;
                        var l = new Vector2(Mathf.Sin(lightRadians), Mathf.Cos(lightRadians));
                        float nDotL = Mathf.Max(0f, l.y);
                        // What the source's lighting makes of the scene at this angle.
                        var light = AtlasLookLight.At(source.Lighting, ramp, scene, nDotL);
                        float diffuse = AtlasSourceSamples.Luminance(light.Lit(new Color(gray, gray, gray, 1f)));
                        for (int k = 0; k < ViewAngles; k++)
                        {
                            float viewRadians = (-80f + 10f * k) * Mathf.Deg2Rad;
                            var v = new Vector2(Mathf.Sin(viewRadians), Mathf.Cos(viewRadians));
                            var h = (l + v).normalized;
                            float nDotV = Mathf.Max(1e-4f, Mathf.Abs(v.y)), nDotH = Mathf.Max(0f, h.y);
                            float vDotH = Mathf.Clamp01(Vector2.Dot(v, h));
                            var entry = new Entry
                            {
                                Texel = t,
                                Diffuse = diffuse,
                                Highlight = AtlasReferenceLook.Highlight(roughness, nDotL, nDotV, nDotH) * light.Direct,
                                Environment = scene.Ambient
                            };
                            AtlasReferenceLook.FresnelRise(source.Response, vDotH, nDotV, out entry.OwnHighlightRise, out entry.OwnReflectionRise);
                            AtlasReferenceLook.FresnelRise(shown, vDotH, nDotV, out entry.HighlightRise, out entry.ReflectionRise);
                            entries.Add(entry);
                        }
                    }
            }
            source.Entries = entries.ToArray();
            source.Looks = new float[source.Entries.Length];
            for (int i = 0; i < source.Entries.Length; i++)
            {
                var entry = source.Entries[i];
                var texel = source.Texels[entry.Texel];
                source.Looks[i] = Look(entry, texel, source.Response, entry.OwnHighlightRise, entry.OwnReflectionRise, texel.Reflectance,
                    texel.Highlights, texel.Reflections);
            }
        }

        // The displayed gray of an entry with the reflectance and strengths given, in a shader's
        // response.
        private static float Look(Entry entry, Texel texel, AtlasSurfaceResponse response, float highlightRise, float reflectionRise, float reflectance,
            float highlights, float reflections)
        {
            float f0 = 0.16f * reflectance * reflectance * (1f - texel.Metallic) + texel.Base * texel.Metallic;
            float specular = AtlasReferenceLook.Specular(response, f0, reflectance, texel.Metallic, highlights * entry.Highlight, highlightRise,
                reflections * entry.Environment, reflectionRise);
            return Mathf.LinearToGammaSpace(Mathf.Clamp01(entry.Diffuse + specular));
        }
    }
}
