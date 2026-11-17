using System.Collections.Generic;
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
        // How the remap rewrites each source's vertex colors, by index into the job's sources;
        // sources without an entry keep theirs.
        public readonly Dictionary<int, System.Func<Color, Color>> VertexColors = new Dictionary<int, System.Func<Color, Color>>();
    }

    // Steps 1 and 2 of the output pipeline (ATLAS_OUTPUT.md, "Pipeline"): collect each
    // feature's sources and resolve it in its mode into one unified material.
    public static class AtlasOutputPipeline
    {
        public static AtlasOutputPlan Resolve(AtlasGeneratorJob job, AtlasOutputCapabilities capabilities)
        {
            var plan = new AtlasOutputPlan();
            // What an earlier resolve attached to the same unified materials.
            foreach (var source in job.Sources) AtlasVertexColors.Detach(source.Material);
            foreach (var feature in AtlasOutputFeatures.All)
                Resolve(job, capabilities, feature, plan);
            if (job.Sources.Count > 0)
                plan.Notes.Add("Lighting (shading, shadows, light volumes) isn't merged yet; the output shader's default lighting is used.");
            var live = new List<string>();
            for (int i = 0; i < job.Sources.Count; i++)
                if (AtlasMaskBusEvaluator.HasLiveSteps(job.Sources[i].Material)) live.Add((i + 1).ToString());
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
                        if (value != null && !string.IsNullOrEmpty(value.id)) choice.Switches[value.id] = value.toggle;
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
            if (mode == null) return;
            var sources = new List<int>();
            for (int i = 0; i < job.Sources.Count; i++)
            {
                bool present = feature is IAtlasSourceAwareFeature aware
                    ? aware.IsPresent(job.Sources[i]) : feature.IsPresent(job.Sources[i].Material);
                if (present &&
                    (choice == null || feature is IAtlasAlwaysOnFeature || !choice.DisabledSources.Contains(i)))
                    sources.Add(i);
            }
            if (sources.Count == 0) return;
            plan.Modes[feature.Id] = mode;
            // Off adds nothing: the output unified material starts without any feature.
            if (mode.Kind == AtlasFeatureModeKind.Off) return;
            feature.Resolve(new AtlasFeatureContext
            {
                Job = job, Capabilities = capabilities, Mode = mode, Sources = sources,
                Output = plan.Output, Maps = plan.Maps, Notes = plan.Notes, VertexColors = plan.VertexColors
            });
        }
    }
}
