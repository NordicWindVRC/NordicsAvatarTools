using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Reflection and specular on an output of the specular workflow (ATLAS_OUTPUT.md, "Feature
    // inventory"): a specular color map in place of metallic, as Unity Standard's specular
    // setup has. The specular color is what the surface reflects at normal incidence, per
    // texel, so it holds what such an output has no other control for:
    //
    // - a source's reflectance: F0 = 0.16 × reflectance² where it isn't metallic;
    // - its metallic, converted: lerp(F0, base color, metallic);
    // - a specular workflow source's own specular color;
    // - its highlight and reflection strengths (and masks), relative to the output shader's
    //   own, × the specular color. One color scales both, so where the output shows both, a
    //   source gets the mean of its two; a source without either gets black, which shows no
    //   highlights or reflections at all.
    //
    // The shader takes the specular color's brightest channel off the diffuse color
    // (diffuse = albedo × (1 − max(specular))), so the base color is scaled to give the
    // source's own diffuse: a metallic source's base × (1 − F0) × (1 − metallic), a specular
    // source's base × (1 − its own specular's brightest channel).
    public static class AtlasSpecularColor
    {
        // What the conversion needs of the output and the plan.
        private sealed class Setup
        {
            public AtlasMapRequest Color;
            // The output shader's own strengths, on the unified scale.
            public float OwnHighlights = 1f, OwnReflections = 1f;
            // Whether the output shows them at all (any source has them).
            public bool Highlights, Reflections;
        }

        public static void Resolve(AtlasFeatureContext context, HashSet<int> active)
        {
            var job = context.Job;
            AtlasSurfaceResponse Response(int i) => job.Sources[i].Material.Surface.Response;
            var output = context.Output.Surface.Response;
            var shown = AtlasKeptMaterial.Described(context.Capabilities)?.Surface.Response;
            var setup = new Setup
            {
                Color = AtlasLiveMasks.Request(context, AtlasVirtualMap.BaseColor),
                OwnHighlights = shown != null && shown.Highlights.Strength > 0f ? shown.Highlights.Strength : 1f,
                OwnReflections = shown != null && shown.Reflections.Strength > 0f ? shown.Reflections.Strength : 1f,
                Highlights = active.Any(i => Response(i).Highlights.Enabled && Response(i).Highlights.Strength > 0f),
                Reflections = active.Any(i => Response(i).Reflections.Enabled && Response(i).Reflections.Strength > 0f)
            };
            // The shader shows each at its own strength; the sources' strengths are in the color.
            output.Highlights.Enabled = setup.Highlights;
            output.Highlights.Strength = setup.OwnHighlights;
            output.Reflections.Enabled = setup.Reflections;
            output.Reflections.Strength = setup.OwnReflections;
            output.Metallic.Constant = 0f;
            output.Roughness.Constant = 1f;
            output.Roughness.Map = AtlasSpecularFeature.Request(context, AtlasVirtualMap.Roughness, active, 1f,
                (m, w, h) => AtlasMapEvaluator.Scalar(m.Surface.Response.Roughness, w, h, m));

            var request = new AtlasMapRequest
            {
                Map = AtlasVirtualMap.SpecularColor,
                Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Color },
                Content = (index, material, width, height) =>
                {
                    var map = AtlasMapEvaluator.Constant(AtlasVirtualMap.SpecularColor, 0f, width, height);
                    if (!active.Contains(index)) return map;
                    Texels(setup, index, material, width, height, out var specular, out _);
                    map.SetPixels(specular);
                    return map;
                }
            };
            // Every source, so the ones without specular are black rather than filled from a neighbor.
            for (int i = 0; i < job.Sources.Count; i++) request.Sources.Add(i);
            context.Maps.Add(request);
            output.SpecularColor = new AtlasColorInput { Map = request.Reference };

            // Right only together with the specular color, which the shader takes off again.
            setup.Color?.Add(new AtlasMapContribution
            {
                Feature = "specular", Label = "diffuse under the specular color", Stage = AtlasMapStage.Level,
                Kind = AtlasContributionKind.Shift, Applies = active.Contains,
                Apply = content =>
                {
                    Texels(setup, content.Source, content.Material, content.Width, content.Height, out _, out var diffuse);
                    var pixels = content.Map.GetPixels();
                    for (int p = 0; p < pixels.Length; p++)
                    {
                        pixels[p].r *= diffuse[p];
                        pixels[p].g *= diffuse[p];
                        pixels[p].b *= diffuse[p];
                    }
                    content.Map.SetPixels(pixels);
                }
            });
            if (setup.Color == null)
                context.Notes.Add("The base color is off, so a metallic source's specular color has no base color to take: its metal reflects as a non-metal.");

            var mixed = active.Where(i => setup.Highlights && setup.Reflections && Differ(Response(i), setup)).OrderBy(i => i).ToList();
            if (mixed.Count > 0)
                context.Notes.Add("The output shader's specular color scales highlights and reflections together, so the mean of the two " +
                                  "strengths is used where they differ (" + AtlasLiveMasks.Numbers(mixed) + ").");
            var metals = active.Where(i => Response(i).SpecularColor == null && Response(i).Metallic.Constant > 0f).OrderBy(i => i).ToList();
            if (metals.Count > 0)
                context.Notes.Add("The output shader has a specular color in place of metallic; the metallic of " + AtlasLiveMasks.Numbers(metals) +
                                  " is converted: the specular color takes the base color where it is metallic, and the base color is darkened there.");
        }

        // The source's highlight and reflection strengths relative to the output shader's.
        private static void Strengths(AtlasSurfaceResponse response, Setup setup, out float highlights, out float reflections)
        {
            highlights = response.Highlights.Enabled ? Mathf.Max(0f, response.Highlights.Strength) / setup.OwnHighlights : 0f;
            reflections = response.Reflections.Enabled ? Mathf.Max(0f, response.Reflections.Strength) / setup.OwnReflections : 0f;
        }

        private static bool Differ(AtlasSurfaceResponse response, Setup setup)
        {
            Strengths(response, setup, out float highlights, out float reflections);
            return Mathf.Abs(highlights - reflections) > 0.05f || response.Highlights.Mask != null || response.Reflections.Mask != null;
        }

        // Per texel of a source: the specular color the output shows, and the factor on the
        // base color that gives the source's own diffuse under it.
        private static void Texels(Setup setup, int index, AtlasUnifiedMaterial material, int width, int height, out Color[] specular, out float[] diffuse)
        {
            var response = material.Surface.Response;
            int count = width * height;
            specular = new Color[count];
            diffuse = new float[count];
            Strengths(response, setup, out float highlights, out float reflections);
            using (var highlightMask = AtlasMapEvaluator.ScaledMask(response.Highlights.Mask, 1f, width, height, material))
            using (var reflectionMask = AtlasMapEvaluator.ScaledMask(response.Reflections.Mask, 1f, width, height, material))
            {
                var lit = highlightMask.GetPixels();
                var mirrored = reflectionMask.GetPixels();
                float Strength(int p)
                {
                    float h = highlights * lit[p].r, r = reflections * mirrored[p].r;
                    return setup.Highlights && setup.Reflections ? (h + r) * 0.5f : setup.Highlights ? h : r;
                }
                if (response.SpecularColor != null)
                {
                    using (var own = Colors(response.SpecularColor, width, height))
                    {
                        var colors = own.GetPixels();
                        for (int p = 0; p < count; p++)
                        {
                            var original = colors[p];
                            specular[p] = Scaled(original, Strength(p));
                            diffuse[p] = Under(1f - Brightest(original), specular[p]);
                        }
                    }
                    return;
                }
                bool metals = response.Metallic.Constant > 0f;
                using (var metallic = AtlasMapEvaluator.Scalar(response.Metallic, width, height, material))
                using (var reflectance = AtlasMapEvaluator.Scalar(response.Reflectance, width, height, material))
                // The surface's color as the source reflects it: before the level compensation.
                using (var baseColor = metals && setup.Color != null ? setup.Color.Before(AtlasMapStage.Level, index, material, width, height) : null)
                {
                    var metal = metallic.GetPixels();
                    var reflect = reflectance.GetPixels();
                    var bases = baseColor?.GetPixels();
                    for (int p = 0; p < count; p++)
                    {
                        float m = metals ? Mathf.Clamp01(metal[p].r) : 0f;
                        float f0 = 0.16f * reflect[p].r * reflect[p].r;
                        var b = bases != null ? bases[p] : Color.black;
                        var original = new Color(Mathf.Lerp(f0, b.r, m), Mathf.Lerp(f0, b.g, m), Mathf.Lerp(f0, b.b, m), 1f);
                        specular[p] = Scaled(original, Strength(p));
                        diffuse[p] = Under((1f - f0) * (1f - m), specular[p]);
                    }
                }
            }
        }

        private static Color Scaled(Color color, float strength) =>
            new Color(Mathf.Clamp01(color.r * strength), Mathf.Clamp01(color.g * strength), Mathf.Clamp01(color.b * strength), 1f);

        private static float Brightest(Color color) => Mathf.Clamp01(Mathf.Max(color.r, Mathf.Max(color.g, color.b)));

        // The factor on the base color that leaves `kept` of it as diffuse, under a specular
        // color whose brightest channel the shader takes off.
        private static float Under(float kept, Color specular) => Mathf.Clamp01(kept) / Mathf.Max(1f - Brightest(specular), 1e-3f);

        // A color input over the source's UV 0–1: its color × its texture on UV0.
        private static NordicsTextureWrapper Colors(AtlasColorInput input, int width, int height)
        {
            var tint = input.Color;
            if (!(input.Map?.Texture is Texture2D texture) || input.Map.Space != AtlasTextureSpace.UV0)
            {
                var constant = AtlasMapEvaluator.Constant(AtlasVirtualMap.SpecularColor, 0f, width, height);
                constant.Fill(new Color(tint.r, tint.g, tint.b, 1f));
                return constant;
            }
            var map = AtlasMapEvaluator.SampleColor(input.Map, texture, width, height);
            AtlasPixels.Map(map, c => new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, 1f));
            return map;
        }
    }
}
