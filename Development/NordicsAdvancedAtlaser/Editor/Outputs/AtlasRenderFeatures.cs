using System.Collections.Generic;
using System.Linq;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // The output's surface mode, and whether a blended one still writes depth.
    public struct AtlasOutputSurface
    {
        public AtlasSurfaceMode Mode;
        public bool DepthWrite;
    }

    // Surface mode and render queue (ATLAS_OUTPUT.md, "Effects and render"): one surface mode
    // for the output. Automatic: the most see-through mode among the sources that the output
    // shader supports, counting a source whose alpha is turned off as opaque. When transparent
    // sources mix with opaque or cutout ones, the output fades by alpha but keeps writing depth
    // (Poiyomi's TransClipping): the solid parts, at alpha 1, sort like opaque ones, and the
    // transparent ones keep their alpha. An output that can't do that has no stand-in: the
    // transparent sources are drawn solid. The user can choose another mode. The queue follows
    // the mode (the writer's queue for it).
    public sealed class AtlasSurfaceModeFeature : IAtlasAlwaysOnFeature, IAtlasConflictSwitchFeature
    {
        public const string FeatureId = "surface-mode";

        // Alpha to coverage: one setting for every cutout part.
        public static readonly AtlasConflictSwitch AlphaToCoverage = new AtlasConflictSwitch
        {
            Id = "alpha-to-coverage", Label = "Alpha to coverage", Default = true,
            Tooltip = "Some cutout materials use alpha to coverage (smoother cutout edges with MSAA) and some don't. " +
                      "The atlas material has one setting for all of them."
        };
        // The output's shared cutoff: each cutout source's alpha is remapped so its own cutoff
        // lands here (AtlasAlphaFeature).
        public const float SharedCutoff = 0.5f;

        // From least to most see-through. Additive, multiplicative and custom blends aren't merged
        // yet: their sources count as opaque.
        private static readonly AtlasSurfaceMode[] SeeThrough =
            { AtlasSurfaceMode.Opaque, AtlasSurfaceMode.Cutout, AtlasSurfaceMode.Dithered, AtlasSurfaceMode.Transparent, AtlasSurfaceMode.Fade };

        private static readonly (string Id, string Label, AtlasSurfaceMode Mode, bool Depth)[] FixedModes =
        {
            ("opaque", "Opaque", AtlasSurfaceMode.Opaque, false),
            ("cutout", "Cutout", AtlasSurfaceMode.Cutout, false),
            ("dithered", "Dithered", AtlasSurfaceMode.Dithered, false),
            ("transparent", "Transparent", AtlasSurfaceMode.Transparent, false),
            ("fade", "Fade", AtlasSurfaceMode.Fade, false),
            ("fade-depth", "Fade with Depth", AtlasSurfaceMode.Fade, true)
        };

        private static readonly AtlasFeatureMode[] ModeList =
            new[] { new AtlasFeatureMode { Id = "auto", Label = "Automatic", Kind = AtlasFeatureModeKind.Native } }
            .Concat(FixedModes.Select(f => new AtlasFeatureMode
            {
                Id = f.Id, Label = f.Label, Kind = AtlasFeatureModeKind.Native,
                Requires = c => Supports(c, f.Mode, f.Depth)
            })).ToArray();

        public string Id => FeatureId;
        public string DisplayName => "Surface Mode";
        public AtlasFeatureSection Section => AtlasFeatureSection.Render;
        public int Order => 0;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;
        public bool IsPresent(AtlasUnifiedMaterial source) => source != null;

        public void Resolve(AtlasFeatureContext context)
        {
            var surface = Choose(context.Job, context.Capabilities);
            var mode = surface.Mode;
            var render = context.Output.Render;
            render.SurfaceMode = mode;
            render.Pass.DepthWrite = surface.DepthWrite;
            // A blended surface that writes depth discards what is nearly see-through, so it
            // doesn't hide what lies behind it.
            render.CutoffInTransparent = Blended(mode) && surface.DepthWrite;
            render.Cutoff = SharedCutoff;
            var coverage = CoverageValues(context.Job, context.Capabilities).Distinct().ToList();
            render.AlphaToCoverage = coverage.Count == 1 ? coverage[0]
                : coverage.Count > 1 && AtlasOutputFeatures.Switch(context.Job, FeatureId, AlphaToCoverage);
            if (coverage.Count > 1)
                context.Notes.Add($"The cutout materials disagree on alpha to coverage; it's {(render.AlphaToCoverage ? "on" : "off")} for all of them.");
            foreach (var source in context.Job.Sources)
                if (source.Material != null && Rank(source.Material.Render.SurfaceMode) < 0)
                {
                    context.Notes.Add($"A source's {source.Material.Render.SurfaceMode} surface mode isn't merged yet; it's drawn as {mode}.");
                    break;
                }
            int solid = 0;
            for (int i = 0; i < context.Job.Sources.Count; i++)
            {
                var source = context.Job.Sources[i].Material;
                if (source != null && Blended(source.Render.SurfaceMode) && !Blended(mode) && !TurnedOff(context.Job, i)) solid++;
            }
            if (solid > 0)
                context.Notes.Add($"{solid} transparent source(s) are drawn solid: the output shader can't keep them transparent next to solid ones.");
        }

        public IEnumerable<AtlasConflictSwitch> ConflictingSwitches(AtlasGeneratorJob job, AtlasOutputCapabilities capabilities)
        {
            if (CoverageValues(job, capabilities).Distinct().Count() > 1) yield return AlphaToCoverage;
        }

        // The alpha-to-coverage settings of the cutout sources whose alpha is kept, when the
        // output is cutout.
        private static List<bool> CoverageValues(AtlasGeneratorJob job, AtlasOutputCapabilities capabilities)
        {
            var values = new List<bool>();
            if (Choose(job, capabilities).Mode != AtlasSurfaceMode.Cutout) return values;
            for (int i = 0; i < job.Sources.Count; i++)
            {
                var source = job.Sources[i].Material;
                if (source != null && source.Render.SurfaceMode == AtlasSurfaceMode.Cutout && AtlasAlphaFeature.Kept(job, capabilities, i))
                    values.Add(source.Render.AlphaToCoverage);
            }
            return values;
        }

        public static int Rank(AtlasSurfaceMode mode) => System.Array.IndexOf(SeeThrough, mode);

        // Alpha decides what the surface shows.
        public static bool UsesAlpha(AtlasSurfaceMode mode) =>
            mode == AtlasSurfaceMode.Cutout || mode == AtlasSurfaceMode.Dithered ||
            mode == AtlasSurfaceMode.Transparent || mode == AtlasSurfaceMode.Fade;

        // Transparent and fade: blended by alpha.
        public static bool Blended(AtlasSurfaceMode mode) => mode == AtlasSurfaceMode.Transparent || mode == AtlasSurfaceMode.Fade;

        private static bool Supports(AtlasOutputCapabilities capabilities, AtlasSurfaceMode mode, bool depth) =>
            capabilities != null && capabilities.SurfaceModes.Contains(mode) && (!depth || capabilities.BlendedDepthWrite);

        /// <summary>The output's surface mode: the chosen one when the output supports it;
        /// otherwise, and for Automatic, see Automatic.</summary>
        public static AtlasSurfaceMode OutputMode(AtlasGeneratorJob job, AtlasOutputCapabilities capabilities) =>
            Choose(job, capabilities).Mode;

        public static AtlasOutputSurface Choose(AtlasGeneratorJob job, AtlasOutputCapabilities capabilities)
        {
            string stored = job.Features.Find(f => f.FeatureId == FeatureId)?.ModeId;
            var chosen = FixedModes.FirstOrDefault(f => f.Id == stored);
            if (chosen.Id != null && Supports(capabilities, chosen.Mode, chosen.Depth))
                return new AtlasOutputSurface { Mode = chosen.Mode, DepthWrite = !Blended(chosen.Mode) || chosen.Depth };
            var modes = new List<AtlasSurfaceMode>();
            for (int i = 0; i < job.Sources.Count; i++)
            {
                var source = job.Sources[i].Material;
                if (source == null) continue;
                // A source whose alpha isn't kept is solid.
                bool kept = !UsesAlpha(source.Render.SurfaceMode) || AtlasAlphaFeature.Kept(job, capabilities, i);
                modes.Add(kept ? source.Render.SurfaceMode : AtlasSurfaceMode.Opaque);
            }
            return Automatic(modes, capabilities);
        }

        // The user turned the source's alpha off.
        private static bool TurnedOff(AtlasGeneratorJob job, int index)
        {
            var choice = job.Features.Find(f => f.FeatureId == "alpha");
            return choice != null && (choice.ModeId == AtlasFeatureMode.Off.Id || choice.DisabledSources.Contains(index));
        }

        /// <summary>Automatic: when transparent sources mix with others and the output can blend
        /// while writing depth, a fade with depth writes; otherwise the most see-through of the
        /// sources' modes that the output supports (the blended ones only when every source is
        /// blended, since a blended surface without depth writes would make the solid parts sort
        /// wrongly).</summary>
        public static AtlasOutputSurface Automatic(IReadOnlyList<AtlasSurfaceMode> sources, AtlasOutputCapabilities capabilities)
        {
            var supported = capabilities?.SurfaceModes ?? new HashSet<AtlasSurfaceMode> { AtlasSurfaceMode.Opaque };
            bool anyBlended = sources.Any(Blended), allBlended = sources.Count > 0 && sources.All(Blended);
            if (anyBlended && !allBlended && Supports(capabilities, AtlasSurfaceMode.Fade, true))
                return new AtlasOutputSurface { Mode = AtlasSurfaceMode.Fade, DepthWrite = true };
            int wanted = 0;
            foreach (var mode in sources)
                if (allBlended || !Blended(mode)) wanted = System.Math.Max(wanted, Rank(mode));
            for (int rank = wanted; rank > 0; rank--)
                if (supported.Contains(SeeThrough[rank]))
                    return new AtlasOutputSurface { Mode = SeeThrough[rank], DepthWrite = !Blended(SeeThrough[rank]) };
            return new AtlasOutputSurface { Mode = AtlasSurfaceMode.Opaque, DepthWrite = true };
        }
    }

    // The fallback shader (ATLAS_OUTPUT.md, "Effects and render"): Automatic leaves it to the
    // writer, which follows the surface mode; the user can choose a shader type or hide the
    // avatar's material for viewers with the shader blocked.
    public sealed class AtlasFallbackFeature : IAtlasAlwaysOnFeature
    {
        private static readonly AtlasFeatureMode[] ModeList =
        {
            Mode("auto", "Automatic"), Mode("toon", "Toon"), Mode("unlit", "Unlit"), Mode("standard", "Standard"), Mode("hidden", "Hidden")
        };

        private static AtlasFeatureMode Mode(string id, string label) => new AtlasFeatureMode
        {
            Id = id, Label = label, Kind = AtlasFeatureModeKind.Native, Requires = c => c.VRChatFallback
        };

        public string Id => "fallback";
        public string DisplayName => "Fallback Shader";
        public AtlasFeatureSection Section => AtlasFeatureSection.Render;
        public int Order => 1;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;
        public bool IsPresent(AtlasUnifiedMaterial source) => source != null;

        public void Resolve(AtlasFeatureContext context)
        {
            switch (context.Mode.Id)
            {
                case "toon": context.Output.Render.Fallback = AtlasFallbackShader.Toon; break;
                case "unlit": context.Output.Render.Fallback = AtlasFallbackShader.Unlit; break;
                case "standard": context.Output.Render.Fallback = AtlasFallbackShader.Standard; break;
                case "hidden": context.Output.Render.Fallback = AtlasFallbackShader.Hidden; break;
                default: context.Output.Render.Fallback = AtlasFallbackShader.Automatic; break;
            }
        }
    }
}
