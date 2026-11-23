using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Tone mapping (ATLAS_OUTPUT.md, "Feature inventory"): operations on the final color and on
    // the emission that an output has once for the whole material: a GT7 tone map of the final
    // color, and a neutral surface emission. Native only. One setup from the reference source
    // (Tone Mapping from). The output's have no mask, so they cover the whole atlas, which is
    // listed when some sources don't have them; Off is the choice then.
    public sealed class AtlasToneMappingFeature : IAtlasSharedValueFeature
    {
        public const string FeatureId = "tonemapping";

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode { Id = "native", Label = "Native", Kind = AtlasFeatureModeKind.Native, Requires = c => c.ToneMapping || c.NeutralEmission },
            AtlasFeatureMode.Off
        };

        public string Id => FeatureId;
        public string DisplayName => "Tone Mapping";
        public AtlasFeatureSection Section => AtlasFeatureSection.Effects;
        public int Order => 5;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;
        public string SharedValueName => "Tone mapping";

        /// <summary>Whether a color adjustment is one of this feature's operations.</summary>
        public static bool Is(AtlasColorAdjustment adjustment) =>
            adjustment.Operation == AtlasColorOperation.ToneMapGt7 || adjustment.Operation == AtlasColorOperation.NeutralEmission;

        // The source's operations that don't change live.
        private static List<AtlasColorAdjustment> Operations(AtlasUnifiedMaterial material)
        {
            var result = new List<AtlasColorAdjustment>();
            if (material == null) return result;
            for (int i = 0; i < material.ColorAdjustments.Count; i++)
                if (Is(material.ColorAdjustments[i]) && AtlasColorStack.IsStatic(material, "colorAdjustments[" + i + "]"))
                    result.Add(material.ColorAdjustments[i]);
            return result;
        }

        public bool IsPresent(AtlasUnifiedMaterial source) => Operations(source).Count > 0;

        private static string Setup(AtlasUnifiedMaterial material) =>
            string.Join("|", Operations(material).Select(a => AtlasFieldwise.Signature(a, "Mask", "Layer")));

        public bool SharedValuesDiffer(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputCapabilities capabilities) =>
            sources.Where(IsPresent).Select(Setup).Distinct().Count() > 1;

        private static bool Has(AtlasOutputCapabilities capabilities, AtlasColorOperation operation) =>
            operation == AtlasColorOperation.ToneMapGt7 ? capabilities.ToneMapping : capabilities.NeutralEmission;

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var choice = job.Features.Find(f => f.FeatureId == FeatureId);
            int reference = choice != null && context.Sources.Contains(choice.ReferenceSource) ? choice.ReferenceSource
                : context.Sources.OrderByDescending(i => job.Sources[i].UVRegion.width * job.Sources[i].UVRegion.height).ThenBy(i => i).First();
            foreach (var operation in Operations(job.Sources[reference].Material))
            {
                if (!Has(context.Capabilities, operation.Operation))
                {
                    context.Notes.Add("The output shader has no " + Name(operation.Operation) + "; it's left out.");
                    continue;
                }
                var copy = new AtlasColorAdjustment();
                AtlasFieldwise.Copy(operation, copy);
                copy.Mask = null;
                copy.Layer = null;
                context.Output.ColorAdjustments.Add(copy);
            }
            string used = Setup(job.Sources[reference].Material);
            var differ = context.Sources.Where(i => Setup(job.Sources[i].Material) != used).ToList();
            if (differ.Count > 0)
                context.Notes.Add($"Tone mapping is taken from source {reference + 1}; the tone mapping of " + AtlasLiveMasks.Numbers(differ) + " differs.");
            var without = Enumerable.Range(0, job.Sources.Count).Where(i => !context.Sources.Contains(i)).ToList();
            if (without.Count > 0)
                context.Notes.Add("The output shader's tone mapping has no mask, so it covers the whole atlas, " + AtlasLiveMasks.Numbers(without) +
                                  " included, which didn't have it (Tone Mapping: Off leaves it out).");
        }

        private static string Name(AtlasColorOperation operation) =>
            operation == AtlasColorOperation.ToneMapGt7 ? "GT7 tone mapping" : "neutral surface emission";

        /// <summary>The output's operation of a kind (for the writers); null without one.</summary>
        public static AtlasColorAdjustment Of(AtlasUnifiedMaterial output, AtlasColorOperation operation) =>
            output.ColorAdjustments.FirstOrDefault(a => a.Operation == operation);
    }

    // Shadows from the depth texture (ATLAS_OUTPUT.md, "Feature inventory"): screen-space
    // ambient occlusion and contact shadows, a pattern layer each. Native only: they follow
    // the view and the light, so there is nothing to bake. One setup from the reference source;
    // each source's mask goes into an allocated mask place, 0 where a source has none, so the
    // shadow shows where it did.
    public abstract class AtlasDepthShadowFeature : IAtlasSharedValueFeature
    {
        private static readonly string[] PerSource = { "Mask", "Enabled" };

        private readonly AtlasFeatureMode[] modes;

        protected AtlasDepthShadowFeature()
        {
            modes = new[]
            {
                new AtlasFeatureMode
                {
                    Id = "native", Label = "Native", Kind = AtlasFeatureModeKind.Native,
                    OptionalMaps = new[] { AtlasVirtualMap.Mask1 },
                    Requires = Supported
                },
                AtlasFeatureMode.Off
            };
        }

        protected abstract AtlasPatternKind Kind { get; }
        protected abstract bool Supported(AtlasOutputCapabilities capabilities);
        // The feature's name inside a sentence: "screen-space ambient occlusion".
        protected abstract string Name { get; }

        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public abstract string SharedValueName { get; }
        public abstract int Order { get; }
        public AtlasFeatureSection Section => AtlasFeatureSection.Effects;
        public IReadOnlyList<AtlasFeatureMode> Modes => modes;

        /// <summary>The material's layer of the kind that doesn't change live; null without one.</summary>
        public static AtlasPatternLayer Layer(AtlasUnifiedMaterial material, AtlasPatternKind kind)
        {
            if (material == null) return null;
            for (int i = 0; i < material.Layers.Count; i++)
                if (material.Layers[i] is AtlasPatternLayer layer && layer.Enabled && layer.Kind == kind &&
                    AtlasColorStack.IsStatic(material, "layers[" + i + "]"))
                    return layer;
            return null;
        }

        public bool IsPresent(AtlasUnifiedMaterial source) =>
            source != null && source.Layers.OfType<AtlasPatternLayer>().Any(l => l.Enabled && l.Kind == Kind);

        public bool SharedValuesDiffer(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputCapabilities capabilities) =>
            sources.Select(s => Layer(s, Kind)).Where(l => l != null).Select(l => AtlasFieldwise.Signature(l, PerSource)).Distinct().Count() > 1;

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var layers = context.Sources.Select(i => (Index: i, Layer: Layer(job.Sources[i].Material, Kind))).Where(s => s.Layer != null)
                .ToDictionary(s => s.Index, s => s.Layer);
            var live = context.Sources.Where(i => !layers.ContainsKey(i)).ToList();
            if (live.Count > 0)
                context.Notes.Add($"Live {Name} (animated, audio-driven) isn't merged yet; it's left out ({AtlasLiveMasks.Numbers(live)}).");
            if (layers.Count == 0) return;
            var choice = job.Features.Find(f => f.FeatureId == Id);
            int reference = choice != null && layers.ContainsKey(choice.ReferenceSource) ? choice.ReferenceSource
                : layers.Keys.OrderByDescending(i => job.Sources[i].UVRegion.width * job.Sources[i].UVRegion.height).ThenBy(i => i).First();
            var template = layers[reference];

            // Where it shows: each source's mask, 0 on the sources without it.
            var allocated = AtlasMaskAllocator.Allocate(context, new AtlasMaskRequest
            {
                Name = Name,
                CanInvert = true,
                Key = i => layers.TryGetValue(i, out var l) ? AtlasMaskKeys.Of(l.Mask, 1f, job.Sources[i].Material) : AtlasMaskKeys.Constant(0f),
                InverseKey = i => layers.TryGetValue(i, out var l) ? AtlasMaskKeys.Inverse(l.Mask, 1f, job.Sources[i].Material) : AtlasMaskKeys.Constant(1f),
                Evaluate = (i, material, w, h) => layers.TryGetValue(i, out var l)
                    ? AtlasMapEvaluator.ScaledMask(l.Mask, 1f, w, h, material)
                    : AtlasMapEvaluator.Constant(AtlasVirtualMap.Mask1, 0f, w, h)
            });
            if (allocated == null)
            {
                context.Notes.Add($"No mask place is left for the {Name}; it's left out.");
                return;
            }
            if (allocated.IsConstant && allocated.Constant <= 0f) return;

            var output = new AtlasPatternLayer();
            AtlasFieldwise.Copy(template, output);
            output.Mask = allocated.Mask;
            Own(template, output, allocated.IsConstant ? allocated.Constant : 1f, context);
            context.Output.Layers.Add(output);

            string used = AtlasFieldwise.Signature(template, PerSource);
            var differ = layers.Where(l => AtlasFieldwise.Signature(l.Value, PerSource) != used).Select(l => l.Key).ToList();
            if (differ.Count > 0)
                context.Notes.Add($"The settings of the {Name} are taken from source {reference + 1}; those of {AtlasLiveMasks.Numbers(differ)} differ.");
        }

        // Gives the output its own copy of the kind's parameters (the template's belong to its
        // source), with the mask's one value in the intensity where the mask needs no map.
        protected abstract void Own(AtlasPatternLayer template, AtlasPatternLayer output, float constant, AtlasFeatureContext context);
    }

    public sealed class AtlasScreenOcclusionFeature : AtlasDepthShadowFeature
    {
        public const string FeatureId = "screenocclusion";
        public override string Id => FeatureId;
        public override string DisplayName => "Screen-Space Ambient Occlusion";
        public override string SharedValueName => "Screen-space AO";
        public override int Order => 6;
        protected override AtlasPatternKind Kind => AtlasPatternKind.AmbientOcclusion;
        protected override string Name => "screen-space ambient occlusion";
        protected override bool Supported(AtlasOutputCapabilities capabilities) => capabilities.ScreenOcclusion;

        protected override void Own(AtlasPatternLayer template, AtlasPatternLayer output, float constant, AtlasFeatureContext context)
        {
            output.AmbientOcclusion = new AtlasScreenOcclusion();
            AtlasFieldwise.Copy(template.AmbientOcclusion, output.AmbientOcclusion);
            output.AmbientOcclusion.Intensity *= constant;
            // A color map lies on the source's UVs, which the atlas doesn't keep; one sampled
            // by the occlusion as a ramp needs none.
            if (output.AmbientOcclusion.ColorMap != null && !output.AmbientOcclusion.ColorMapAsRamp)
            {
                output.AmbientOcclusion.ColorMap = null;
                context.Notes.Add("The screen-space ambient occlusion's color map isn't baked yet; its color is used.");
            }
        }

        /// <summary>The output's layer (for the writers).</summary>
        public static AtlasPatternLayer Of(AtlasUnifiedMaterial output) => Layer(output, AtlasPatternKind.AmbientOcclusion);
    }

    public sealed class AtlasContactShadowFeature : AtlasDepthShadowFeature
    {
        public const string FeatureId = "contactshadows";
        public override string Id => FeatureId;
        public override string DisplayName => "Contact Shadows";
        public override string SharedValueName => "Contact shadows";
        public override int Order => 7;
        protected override AtlasPatternKind Kind => AtlasPatternKind.ContactShadow;
        protected override string Name => "contact shadows";
        protected override bool Supported(AtlasOutputCapabilities capabilities) => capabilities.ContactShadows;

        protected override void Own(AtlasPatternLayer template, AtlasPatternLayer output, float constant, AtlasFeatureContext context)
        {
            output.ContactShadow = new AtlasContactShadow();
            AtlasFieldwise.Copy(template.ContactShadow, output.ContactShadow);
            output.ContactShadow.Intensity *= constant;
        }

        /// <summary>The output's layer (for the writers).</summary>
        public static AtlasPatternLayer Of(AtlasUnifiedMaterial output) => Layer(output, AtlasPatternKind.ContactShadow);
    }
}
