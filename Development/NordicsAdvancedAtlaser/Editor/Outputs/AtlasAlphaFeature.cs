using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // How one source's alpha becomes the output's: under a cutout output a cutout source's cutoff
    // is moved to the shared cutoff; under a blended or dithered output its alpha becomes hard
    // (0 below its cutoff, 1 above), as it looked. A transparent source under an output that
    // doesn't blend can't look transparent: no emulation stands in, it's drawn solid.
    public enum AtlasAlphaConversionKind { None, Remap, Step, Solid }

    public struct AtlasAlphaConversion
    {
        public AtlasAlphaConversionKind Kind;
        // The source's cutoff.
        public float Cutoff;
    }

    // Alpha (ATLAS_OUTPUT.md, "Feature inventory"): each source's alpha baked into the base color
    // texture's alpha, converted to the output's surface mode. Sources without alpha (opaque, or
    // turned off here) get 1, so their part of the atlas stays solid.
    public sealed class AtlasAlphaFeature : IAtlasOutputFeature
    {
        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Base color alpha", Kind = AtlasFeatureModeKind.Native,
                RequiredMaps = new[] { AtlasVirtualMap.Alpha },
                Requires = c => c.SurfaceModes.Any(AtlasSurfaceModeFeature.UsesAlpha)
            },
            AtlasFeatureMode.Off
        };

        public string Id => "alpha";
        public string DisplayName => "Alpha";
        public AtlasFeatureSection Section => AtlasFeatureSection.Surface;
        public int Order => 1;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        public bool IsPresent(AtlasUnifiedMaterial source) =>
            source != null && AtlasSurfaceModeFeature.UsesAlpha(source.Render.SurfaceMode) &&
            source.Surface.Alpha.Source != AtlasAlphaSource.None;

        public void Resolve(AtlasFeatureContext context)
        {
            var output = AtlasSurfaceModeFeature.OutputMode(context.Job, context.Capabilities);
            if (!AtlasSurfaceModeFeature.UsesAlpha(output))
            {
                context.Notes.Add("Alpha isn't baked: the surface mode is " + output + ".");
                return;
            }
            context.Output.Surface.Alpha.Source = AtlasAlphaSource.BaseColorAlpha;
            var active = new HashSet<int>(context.Sources);
            var conversions = new Dictionary<int, AtlasAlphaConversion>();
            foreach (int i in active) conversions[i] = Conversion(context.Job.Sources[i].Material.Render, output);
            var request = new AtlasMapRequest
            {
                Map = AtlasVirtualMap.Alpha,
                Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Data },
                Evaluate = (index, source, width, height) =>
                    active.Contains(index) && conversions[index].Kind != AtlasAlphaConversionKind.Solid
                        ? AtlasMapEvaluator.Alpha(source, width, height, conversions[index])
                        : AtlasMapEvaluator.Constant(AtlasVirtualMap.Alpha, 1f, width, height)
            };
            // Every source, so the ones without alpha are solid rather than filled from a neighbor.
            for (int i = 0; i < context.Job.Sources.Count; i++) request.Sources.Add(i);
            context.Maps.Add(request);
            Notes(context, active);
        }

        // Whether the user keeps a source's alpha: the feature is offered and not off, for the
        // output or for this source. Shared with the surface mode, which treats the others as solid.
        public static bool Kept(AtlasGeneratorJob job, AtlasOutputCapabilities capabilities, int index)
        {
            var feature = new AtlasAlphaFeature();
            var choice = job.Features.Find(f => f.FeatureId == feature.Id);
            var mode = AtlasOutputFeatures.Effective(feature, choice?.ModeId, capabilities);
            return mode != null && mode.Kind != AtlasFeatureModeKind.Off && feature.IsPresent(job.Sources[index].Material) &&
                (choice == null || !choice.DisabledSources.Contains(index));
        }

        public static AtlasAlphaConversion Conversion(AtlasRender source, AtlasSurfaceMode output)
        {
            if (AtlasSurfaceModeFeature.Blended(source.SurfaceMode) && !AtlasSurfaceModeFeature.Blended(output))
                return new AtlasAlphaConversion { Kind = AtlasAlphaConversionKind.Solid };
            if (source.SurfaceMode != AtlasSurfaceMode.Cutout) return new AtlasAlphaConversion { Kind = AtlasAlphaConversionKind.None };
            return new AtlasAlphaConversion
            {
                Kind = output == AtlasSurfaceMode.Cutout ? AtlasAlphaConversionKind.Remap : AtlasAlphaConversionKind.Step,
                Cutoff = Mathf.Clamp01(source.Cutoff)
            };
        }

        // One line per kind of alpha input outside the bakeable subset, never one per source.
        private static void Notes(AtlasFeatureContext context, IEnumerable<int> active)
        {
            var materials = active.Select(i => context.Job.Sources[i].Material).ToList();
            if (materials.Any(m => m.Surface.Alpha.Modifiers.Count > 0))
                context.Notes.Add("Alpha fades (distance, view angle, angle, audio) aren't merged yet; they're left out.");
            if (materials.Any(m => m.Surface.VertexColor.MultiplyAlpha))
                context.Notes.Add("Vertex alpha isn't baked yet; it's left out.");
            if (materials.Any(m => m.Surface.Alpha.Mask != null && !AtlasMapEvaluator.CanEvaluate(m.Surface.Alpha.Mask, m)))
                context.Notes.Add("An alpha mask on another UV, vertex colors or a global mask written by vertex colors, layers or the second pass isn't baked yet; it counts as 1.");
            if (materials.Any(m => m.Surface.Alpha.Dithering || m.Surface.Alpha.DitherCutout))
                context.Notes.Add("Alpha dithering isn't merged yet; it's left out.");
            if (materials.Any(m => m.Render.CutoffInTransparent))
                context.Notes.Add("A cutoff in a blended surface mode isn't merged yet; it's left out.");
        }
    }
}
