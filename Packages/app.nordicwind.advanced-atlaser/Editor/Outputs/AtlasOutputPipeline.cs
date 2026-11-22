using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // The result of resolving an output: the merged unified material and the virtual maps it
    // needs. The maps are baked and packed in later steps.
    public sealed class AtlasOutputPlan
    {
        public readonly AtlasUnifiedMaterial Output = new AtlasUnifiedMaterial();
        public readonly List<AtlasMapRequest> Maps = new List<AtlasMapRequest>();
        // The feature IDs in the plan, with the mode each is produced in.
        public readonly Dictionary<string, AtlasFeatureMode> Modes = new Dictionary<string, AtlasFeatureMode>();
        // What the features leave out, one line each.
        public readonly List<string> Notes = new List<string>();
        // The lighting as the output shader shows it; null when resolved without the shader.
        public AtlasLighting KeptLighting;
        // Level compensation: the base color scale that gives a source the level its own
        // lighting had, by index into the job's sources (applied unless turned off), and what
        // the bake applied of it within the map's range.
        public readonly Dictionary<int, float> LevelScales = new Dictionary<int, float>();
        public readonly Dictionary<int, float> LevelApplied = new Dictionary<int, float>();
        // How the remap rewrites each source's vertex colors, by index into the job's sources;
        // sources without an entry keep theirs.
        public readonly Dictionary<int, System.Func<Color, Color>> VertexColors = new Dictionary<int, System.Func<Color, Color>>();
        // What else the remap does per source (preserved UVs, removed triangles), by index into
        // the job's sources.
        public readonly Dictionary<int, AtlasSourceRemap> SourceRemaps = new Dictionary<int, AtlasSourceRemap>();
        // The UV channels after UV0 the features have taken (the UV channel allocator).
        public readonly HashSet<int> UVChannels = new HashSet<int>();
        // The mask allocator's places: taken mask maps, and the placed masks by their per-source keys.
        public readonly HashSet<AtlasVirtualMap> MaskPlaces = new HashSet<AtlasVirtualMap>();
        public readonly Dictionary<string, AtlasMask> PlacedMasks = new Dictionary<string, AtlasMask>();
        public readonly Dictionary<string, AtlasVirtualMap> PlacedMaskPlaces = new Dictionary<string, AtlasVirtualMap>();
        // The UV channels the tiled layers kept live use, by the sources' tilings.
        public readonly Dictionary<string, int> DetailChannels = new Dictionary<string, int>();
        // The next global mask channel past the mask places (8–15) for live parts with no texture.
        public int NextUnitChannel = 8;
        // The sources' bus channels whose live steps a feature keeps live, by source index.
        public readonly HashSet<(int Source, int Channel)> LiveKept = new HashSet<(int Source, int Channel)>();
        // Per feature whose sources have different live setups: one source of each setup.
        public readonly Dictionary<string, List<int>> LiveSetups = new Dictionary<string, List<int>>();
        // Textures the build makes besides the atlas maps (combined matcaps), before writing.
        public readonly List<AtlasGeneratedTexture> GeneratedTextures = new List<AtlasGeneratedTexture>();
        // The output's matcap slots the matcaps took; later features (rims) use the rest.
        public int MatcapSlotsUsed;
        // A matcap slot is fitted through the base color (a base color shift) where viewers who
        // block the shader see the base color alone, so the inspector offers how far it may go.
        public bool BaseColorShifts;
        // A matcap whose lookup is fixed to the world is in a slot that follows the view, so
        // the inspector offers how it is shown there.
        public bool MatcapViews;
        // Steps that set output values from the sources' maps, run just before the material is
        // written, not when the plan is resolved (the glow of combined matcap slots).
        public readonly List<System.Action> BeforeWrite = new List<System.Action>();

        public AtlasSourceRemap Remap(int source)
        {
            if (!SourceRemaps.TryGetValue(source, out var remap)) SourceRemaps[source] = remap = new AtlasSourceRemap();
            return remap;
        }

        // The lowest free UV channel of 1 to `available`, taken; −1 when none is free.
        public int AllocateUVChannel(int available)
        {
            for (int channel = 1; channel <= System.Math.Min(available, 3); channel++)
                if (UVChannels.Add(channel)) return channel;
            return -1;
        }
    }

    // Steps 1 and 2 of the output pipeline (ATLAS_OUTPUT.md, "Pipeline"): collect each
    // feature's sources and resolve it in its mode into one unified material.
    public static class AtlasOutputPipeline
    {
        /// <summary>The plan for an output shader: resolved by its capabilities, then matched
        /// by look against the lighting the shader keeps. The lighting is tuned where the shader
        /// shows it differently (AtlasLightingTuning), and the sources it still shows brighter or
        /// darker than their own get their base color scaled (AtlasLevelCompensation).</summary>
        public static AtlasOutputPlan Resolve(AtlasGeneratorJob job, IAtlasOutputShader shader)
        {
            var plan = Resolve(job, shader.Capabilities);
            using (var kept = AtlasKeptLighting.For(shader))
            {
                if (kept == null) return plan;
                AtlasLightingTuning.Tune(job, plan, kept);
                plan.KeptLighting = kept.Of(plan.Output.Lighting);
                AtlasLevelCompensation.Apply(job, plan, plan.KeptLighting);
            }
            return plan;
        }

        public static AtlasOutputPlan Resolve(AtlasGeneratorJob job, AtlasOutputCapabilities capabilities)
        {
            var plan = new AtlasOutputPlan();
            // What an earlier resolve attached to the same unified materials.
            foreach (var source in job.Sources)
            {
                AtlasVertexColors.Detach(source.Material);
                AtlasLiveLayers.Clear(source.Material);
                // The look is judged on the base color texels the source's meshes use.
                AtlasSourceSamples.Cover(source.Material, source.Coverage);
            }
            foreach (var feature in AtlasOutputFeatures.All)
                Resolve(job, capabilities, feature, plan);
            var live = new List<string>();
            for (int i = 0; i < job.Sources.Count; i++)
            {
                var material = job.Sources[i].Material;
                if (material == null) continue;
                int index = i;
                // Channels with live steps not kept live by every feature reading them.
                if (material.MaskBus.Steps.Where(s => AtlasMaskBusEvaluator.IsLive(s.Kind)).Select(s => s.Channel).Distinct()
                    .Any(c => !plan.LiveKept.Contains((index, c))))
                    live.Add((i + 1).ToString());
            }
            if (live.Count > 0)
                plan.Notes.Add("Global masks that change live (back faces, mirrors, cameras, distance, blacklights, contacts) are baked as normally seen: " +
                    "front faces, outside mirrors and cameras, from far away, with no blacklight or touch (" +
                    (live.Count == 1 ? "source " : "sources ") + string.Join(", ", live) + ").");
            return plan;
        }

        // The stored feature settings as the job's choices, with sources turned from materials
        // into indices of the job's sources (materials don't cross the job's boundary).
        public static void AddChoices(AtlasOutputConfiguration config, IReadOnlyList<Material> materials, AtlasGeneratorJob job)
        {
            if (config.features == null) return;
            foreach (var setting in config.features)
            {
                if (setting == null || string.IsNullOrEmpty(setting.featureId)) continue;
                var choice = new AtlasFeatureChoice
                {
                    FeatureId = setting.featureId,
                    ModeId = setting.mode ?? "",
                    ReferenceSource = IndexOf(materials, setting.referenceSource)
                };
                if (setting.values != null)
                    foreach (var value in setting.values)
                        if (value != null && !string.IsNullOrEmpty(value.id))
                        {
                            choice.Switches[value.id] = value.toggle;
                            choice.Values[value.id] = (value.number, value.text ?? "");
                        }
                if (setting.disabledSources != null)
                    foreach (var material in setting.disabledSources)
                    {
                        int index = IndexOf(materials, material);
                        if (index >= 0) choice.DisabledSources.Add(index);
                    }
                job.Features.Add(choice);
            }
        }

        private static int IndexOf(IReadOnlyList<Material> materials, Material material)
        {
            if (material == null) return -1;
            for (int i = 0; i < materials.Count; i++)
                if (materials[i] == material) return i;
            return -1;
        }

        // One feature; also used by tests with features that aren't registered.
        public static void Resolve(AtlasGeneratorJob job, AtlasOutputCapabilities capabilities, IAtlasOutputFeature feature, AtlasOutputPlan plan)
        {
            var choice = job.Features.Find(f => f.FeatureId == feature.Id);
            var mode = AtlasOutputFeatures.Effective(feature, choice?.ModeId, capabilities);
            var sources = new List<int>();
            var off = new List<int>();
            for (int i = 0; i < job.Sources.Count; i++)
            {
                bool present = feature is IAtlasSourceAwareFeature aware
                    ? aware.IsPresent(job.Sources[i]) : feature.IsPresent(job.Sources[i].Material);
                if (!present) continue;
                if (choice == null || feature is IAtlasAlwaysOnFeature || !choice.DisabledSources.Contains(i)) sources.Add(i);
                else off.Add(i);
            }
            if (feature is IAtlasLeftOutFeature leftOut && mode != null)
            {
                if (mode.Kind == AtlasFeatureModeKind.Off) off.AddRange(sources);
                if (off.Count > 0) leftOut.LeaveOut(job, off);
            }
            if (sources.Count == 0) return;
            if (mode == null)
            {
                // Not offered: the output shader has nothing for it.
                plan.Notes.Add($"The output shader has no {feature.DisplayName.ToLowerInvariant()}; it's left out (" +
                    (sources.Count == 1 ? "source " : "sources ") + string.Join(", ", sources.Select(i => i + 1)) + ").");
                return;
            }
            plan.Modes[feature.Id] = mode;
            // Off adds nothing: the output unified material starts without any feature.
            if (mode.Kind == AtlasFeatureModeKind.Off) return;
            feature.Resolve(new AtlasFeatureContext
            {
                Job = job, Capabilities = capabilities, Mode = mode, Sources = sources,
                Output = plan.Output, Maps = plan.Maps, Notes = plan.Notes, VertexColors = plan.VertexColors, Plan = plan
            });
        }
    }
}
