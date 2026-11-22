using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Vertex colors (ATLAS_OUTPUT.md, "Feature inventory"): on the base color and alpha (2.1),
    // in masks (1.4) and in mask bus steps (1.5).
    // - Live: the vertex colors stay on the mesh and the output multiplies its base color (and
    //   alpha, where it can) by them. The remap rewrites each source's vertex colors so the
    //   output's one setting gives each source what its own did: sources that don't use them
    //   get white. Masks and bus steps that read vertex colors are baked.
    // - Baked: everything that reads vertex colors is baked, from the colors painted in UV space.
    // - Off: vertex colors are ignored (read as white).
    // An output that always multiplies by the vertex colors shows them in every mode, so the
    // sources no mode rewrote them for get white on the mesh (AtlasOutputPipeline).
    public sealed class AtlasVertexColorFeature : IAtlasOutputFeature, IAtlasConflictSwitchFeature
    {
        public const string FeatureId = "vertex-colors";

        private static readonly AtlasFeatureMode Live = new AtlasFeatureMode
        {
            Id = "live", Label = "Kept on the mesh", Kind = AtlasFeatureModeKind.Native,
            Requires = c => (c.VertexColors & AtlasVertexColorSupport.Color) != 0
        };

        private static readonly AtlasFeatureMode[] ModeList =
        {
            Live,
            new AtlasFeatureMode { Id = "baked", Label = "Baked into the textures", Kind = AtlasFeatureModeKind.Baked },
            AtlasFeatureMode.Off
        };

        public string Id => FeatureId;
        public string DisplayName => "Vertex Colors";
        public AtlasFeatureSection Section => AtlasFeatureSection.Surface;
        public int Order => 5;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        public bool IsPresent(AtlasUnifiedMaterial source) => source != null && (UsesOnSurface(source) || UsesInMasks(source));

        private static bool UsesOnSurface(AtlasUnifiedMaterial source) =>
            source.Surface.VertexColor.MultiplyColor || source.Surface.VertexColor.MultiplyAlpha;

        // Masks and bus steps that read vertex colors.
        public static bool UsesInMasks(AtlasUnifiedMaterial source) =>
            Masks(source).Any(m => m != null && (m.VertexColor || m.TimesVertexColor)) ||
            source.MaskBus.Steps.Any(s => s.Kind == AtlasMaskBusStepKind.VertexColor);

        private static IEnumerable<AtlasMask> Masks(AtlasUnifiedMaterial source)
        {
            yield return source.Surface.Alpha.Mask;
            yield return source.Surface.Response.Highlights.Mask;
            yield return source.Surface.Response.Reflections.Mask;
            foreach (var layer in source.Layers) yield return layer.Mask;
            foreach (var adjustment in source.ColorAdjustments) yield return adjustment.Mask;
            foreach (var step in source.MaskBus.Steps) yield return step.Mask;
        }

        // "Vertex colors as sRGB": shown when the output can decode them and the live sources
        // that multiply the color disagree.
        private static AtlasConflictSwitch Srgb(IEnumerable<AtlasUnifiedMaterial> sources)
        {
            var colored = sources.Where(m => m.Surface.VertexColor.MultiplyColor).ToList();
            int srgb = colored.Count(m => m.Surface.VertexColor.SrgbData);
            return new AtlasConflictSwitch
            {
                Id = "srgb", Label = "Vertex colors as sRGB", Default = srgb * 2 >= colored.Count,
                Tooltip = "Some materials read their vertex colors as sRGB and some as linear values, and the atlas material reads them one way. " +
                          "The others' vertex colors are converted on the mesh, which matches at the vertices and can differ slightly between them."
            };
        }

        private static List<AtlasUnifiedMaterial> ActiveSources(AtlasGeneratorJob job, AtlasOutputCapabilities capabilities, out AtlasFeatureMode mode)
        {
            var choice = job.Features.Find(f => f.FeatureId == FeatureId);
            mode = AtlasOutputFeatures.Effective(new AtlasVertexColorFeature(), choice?.ModeId, capabilities);
            var result = new List<AtlasUnifiedMaterial>();
            for (int i = 0; i < job.Sources.Count; i++)
            {
                var material = job.Sources[i].Material;
                if (material != null && (choice == null || !choice.DisabledSources.Contains(i))) result.Add(material);
            }
            return result;
        }

        public IEnumerable<AtlasConflictSwitch> ConflictingSwitches(AtlasGeneratorJob job, AtlasOutputCapabilities capabilities)
        {
            if ((capabilities.VertexColors & AtlasVertexColorSupport.Srgb) == 0) yield break;
            var sources = ActiveSources(job, capabilities, out var mode);
            if (mode != Live) yield break;
            var colored = sources.Where(m => m.Surface.VertexColor.MultiplyColor).Select(m => m.Surface.VertexColor.SrgbData).Distinct();
            if (colored.Count() > 1) yield return Srgb(sources);
        }

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var support = context.Capabilities.VertexColors;
            bool live = context.Mode == Live;
            var active = context.Sources.Where(i => job.Sources[i].Material != null).ToList();
            var materials = active.Select(i => job.Sources[i].Material).ToList();

            var output = context.Output.Surface.VertexColor;
            if (live)
            {
                var colored = materials.Where(m => m.Surface.VertexColor.MultiplyColor).ToList();
                var alphaed = materials.Where(m => m.Surface.VertexColor.MultiplyAlpha).ToList();
                output.MultiplyColor = colored.Count > 0 || (support & AtlasVertexColorSupport.Always) != 0;
                output.MultiplyAlpha = alphaed.Count > 0 && (support & AtlasVertexColorSupport.Alpha) != 0;
                output.Amount = Common(colored.Select(m => m.Surface.VertexColor.Amount), (support & AtlasVertexColorSupport.Amount) != 0);
                output.AlphaAmount = Common(alphaed.Select(m => m.Surface.VertexColor.AlphaAmount), (support & AtlasVertexColorSupport.Amount) != 0);
                output.SrgbData = (support & AtlasVertexColorSupport.Srgb) != 0 && colored.Count > 0 &&
                                  (colored.Select(m => m.Surface.VertexColor.SrgbData).Distinct().Count() == 1
                                      ? colored[0].Surface.VertexColor.SrgbData
                                      : AtlasOutputFeatures.Switch(job, FeatureId, Srgb(materials)));
            }

            for (int k = 0; k < active.Count; k++)
                AtlasVertexColors.Attach(materials[k], job.Sources[active[k]].Coverage, bakeColor: !live, bakeAlpha: !live || !output.MultiplyAlpha);
            if (live && (output.MultiplyColor || output.MultiplyAlpha))
            {
                // Every source of the output gets its vertex colors rewritten: those without the
                // feature (or turned off) get white.
                var copy = new AtlasVertexColor
                {
                    MultiplyColor = output.MultiplyColor, MultiplyAlpha = output.MultiplyAlpha, Amount = output.Amount,
                    AlphaAmount = output.AlphaAmount, SrgbData = output.SrgbData
                };
                for (int i = 0; i < job.Sources.Count; i++)
                {
                    var settings = active.Contains(i) ? job.Sources[i].Material.Surface.VertexColor : null;
                    context.VertexColors[i] = color => Convert(settings, copy, color);
                }
            }
            Notes(context, active, materials, live, output);
        }

        // One value when all agree and the output has the setting; otherwise 1.
        private static float Common(IEnumerable<float> values, bool supported)
        {
            var list = values.ToList();
            if (!supported || list.Count == 0) return 1f;
            return list.All(v => Mathf.Approximately(v, list[0])) ? Mathf.Clamp01(list[0]) : 1f;
        }

        /// <summary>
        /// A vertex color rewritten so the output's setting gives what the source's did:
        /// lerp(1, decode_out(v'), amount_out) = lerp(1, decode_source(v), amount_source).
        /// Unchanged where both read it the same way; white for a source without the setting
        /// (null). Alpha is kept where the output doesn't read it.
        /// </summary>
        public static Color Convert(AtlasVertexColor source, AtlasVertexColor output, Color color)
        {
            var result = color;
            if (output.MultiplyColor)
            {
                if (source == null || !source.MultiplyColor) result.r = result.g = result.b = 1f;
                else if (source.SrgbData != output.SrgbData || !Mathf.Approximately(source.Amount, output.Amount))
                {
                    result.r = Solve(color.r, source.SrgbData, source.Amount, output.SrgbData, output.Amount);
                    result.g = Solve(color.g, source.SrgbData, source.Amount, output.SrgbData, output.Amount);
                    result.b = Solve(color.b, source.SrgbData, source.Amount, output.SrgbData, output.Amount);
                }
            }
            if (output.MultiplyAlpha)
            {
                if (source == null || !source.MultiplyAlpha) result.a = 1f;
                else if (!Mathf.Approximately(source.AlphaAmount, output.AlphaAmount))
                    result.a = Solve(color.a, false, source.AlphaAmount, false, output.AlphaAmount);
            }
            return result;
        }

        private static float Solve(float value, bool sourceSrgb, float sourceAmount, bool outputSrgb, float outputAmount)
        {
            float wanted = Mathf.Lerp(1f, sourceSrgb ? AtlasVertexColors.Decode(value) : value, Mathf.Clamp01(sourceAmount));
            if (outputAmount <= 0f) return 1f;
            float decoded = Mathf.Clamp01(1f - (1f - wanted) / outputAmount);
            return outputSrgb ? AtlasVertexColors.Encode(decoded) : decoded;
        }

        // Whether a source's live conversion is exact between the vertices: it is where nothing
        // is decoded, or where it reads them as the output does.
        private static bool Exact(AtlasVertexColor source, AtlasVertexColor output) =>
            !source.MultiplyColor || !output.MultiplyColor ||
            (source.SrgbData == output.SrgbData && Mathf.Approximately(source.Amount, output.Amount)) ||
            (!source.SrgbData && !output.SrgbData);

        private static void Notes(AtlasFeatureContext context, List<int> active, List<AtlasUnifiedMaterial> materials, bool live, AtlasVertexColor output)
        {
            string Numbers(IEnumerable<int> indices)
            {
                var list = indices.Select(i => (i + 1).ToString()).ToList();
                return (list.Count == 1 ? "source " : "sources ") + string.Join(", ", list);
            }
            var hue = active.Where(i => context.Job.Sources[i].Material.Surface.VertexColor.HueEnabled).ToList();
            if (hue.Count > 0)
                context.Notes.Add($"Vertex color hue shifts aren't kept yet; the vertex colors of {Numbers(hue)} are used unshifted.");
            if (!live) return;
            var inexact = active.Where(i => !Exact(context.Job.Sources[i].Material.Surface.VertexColor, output)).ToList();
            if (inexact.Count > 0)
                context.Notes.Add($"The vertex colors of {Numbers(inexact)} are converted on the mesh for the atlas material's vertex color setting; they match at the vertices and can differ slightly between them.");
        }
    }
}
