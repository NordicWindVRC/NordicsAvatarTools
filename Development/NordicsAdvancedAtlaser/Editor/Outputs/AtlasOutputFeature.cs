using System;
using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEditor;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // The shared vocabulary of feature modes (ATLAS_OUTPUT.md, "Features and modes").
    public enum AtlasFeatureModeKind { Native, Merged, Single, Baked, Emulated, Off }

    // The inspector section a feature is listed in.
    public enum AtlasFeatureSection { Surface, Lighting, Layers, Effects, Render }

    // One way a feature can be produced, and what it needs from the output shader.
    public sealed class AtlasFeatureMode
    {
        // Stable: stored in the settings.
        public string Id;
        public string Label;
        public AtlasFeatureModeKind Kind;
        // Virtual maps the mode can't work without.
        public AtlasVirtualMap[] RequiredMaps = Array.Empty<AtlasVirtualMap>();
        // Virtual maps the mode uses when the output provides them and the user keeps their
        // texture on; without one, its content goes to the map it falls back to.
        public AtlasVirtualMap[] OptionalMaps = Array.Empty<AtlasVirtualMap>();
        // Other capabilities the mode needs; null: none.
        public Func<AtlasOutputCapabilities, bool> Requires;

        // Every feature can be turned off.
        public static readonly AtlasFeatureMode Off = new AtlasFeatureMode { Id = "off", Label = "Off", Kind = AtlasFeatureModeKind.Off };

        public bool IsSupported(AtlasOutputCapabilities capabilities) =>
            capabilities != null && RequiredMaps.All(capabilities.Provides) && (Requires == null || Requires(capabilities));
    }

    // Shared, shader-independent processing of one output feature: which sources have it, the
    // modes it can be produced in, and how it is written into the output unified material.
    public interface IAtlasOutputFeature
    {
        // Stable: stored in the settings.
        string Id { get; }
        string DisplayName { get; }
        AtlasFeatureSection Section { get; }
        // The order within the section, which is also the priority when places run out.
        int Order { get; }
        // In order of preference; the first supported one that isn't Off is the default.
        IReadOnlyList<AtlasFeatureMode> Modes { get; }
        bool IsPresent(AtlasUnifiedMaterial source);
        void Resolve(AtlasFeatureContext context);
    }

    // A feature that is part of every output and can't be turned off, as a whole or per source:
    // the surface mode, the fallback shader.
    public interface IAtlasAlwaysOnFeature : IAtlasOutputFeature { }

    // A feature with a value the output has only once while each source can have its own. Where
    // the sources need different values, the user picks the source it is taken from (the
    // reference source); automatic: the source with the largest region.
    public interface IAtlasSharedValueFeature : IAtlasOutputFeature
    {
        // The value's name, for the inspector.
        string SharedValueName { get; }
        // Whether the given sources need different values on this output.
        bool SharedValuesDiffer(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputCapabilities capabilities);
    }

    // An on/off setting the output has once, which the user sets where the sources disagree.
    public sealed class AtlasConflictSwitch
    {
        // Stable: stored in the feature's settings values.
        public string Id;
        public string Label;
        public string Tooltip;
        // The value until the user sets one.
        public bool Default;
    }

    // A feature with on/off settings the sources can disagree on while the output has one value
    // each. A switch shows in the inspector only while its sources disagree; when they agree,
    // their value is used.
    public interface IAtlasConflictSwitchFeature : IAtlasOutputFeature
    {
        IEnumerable<AtlasConflictSwitch> ConflictingSwitches(AtlasGeneratorJob job, AtlasOutputCapabilities capabilities);
    }

    // A feature a source can have without its material having it, from what the build adds (the
    // geometry bake's correction) or the source's settings (a pre-baked normal map).
    public interface IAtlasSourceAwareFeature : IAtlasOutputFeature
    {
        bool IsPresent(AtlasGeneratorSource source);
    }

    // A virtual map the output needs, the sources that contribute to it, and the texture
    // reference in the output unified material that points at it once it is packed.
    public sealed class AtlasMapRequest
    {
        public AtlasVirtualMap Map;
        public AtlasTextureReference Reference;
        // Indices into AtlasGeneratorJob.Sources.
        public readonly List<int> Sources = new List<int>();
        // How one source's map is evaluated (source index, its material, width, height), when
        // the feature converts it for the output; null: AtlasMapEvaluator.Evaluate.
        public Func<int, AtlasUnifiedMaterial, int, int, NordicsTextureWrapper> Evaluate;
        // Run on the whole baked map before it is packed; null: nothing.
        public Action<NordicsTextureWrapper> Finish;
    }

    // What a feature's Resolve works with.
    public sealed class AtlasFeatureContext
    {
        public AtlasGeneratorJob Job;
        public AtlasOutputCapabilities Capabilities;
        public AtlasFeatureMode Mode;
        // Indices into Job.Sources of the sources that have the feature and haven't turned it off.
        public IReadOnlyList<int> Sources;
        // The merged material being built.
        public AtlasUnifiedMaterial Output;
        public List<AtlasMapRequest> Maps;
        // What the feature leaves out, one line each, for the build's diagnostics.
        public List<string> Notes;
        // How the remap rewrites each source's vertex colors, by index into Job.Sources.
        public Dictionary<int, Func<UnityEngine.Color, UnityEngine.Color>> VertexColors;

        public AtlasTextureReference RequestMap(AtlasVirtualMap map, AtlasTextureInterpretation interpretation)
        {
            var request = new AtlasMapRequest
            {
                Map = map,
                Reference = new AtlasTextureReference { Interpretation = interpretation }
            };
            request.Sources.AddRange(Sources);
            Maps.Add(request);
            return request.Reference;
        }
    }

    [InitializeOnLoad]
    public static class AtlasOutputFeatures
    {
        private static readonly List<IAtlasOutputFeature> Registered = new List<IAtlasOutputFeature>();

        // Every feature, by section and then order.
        public static IReadOnlyList<IAtlasOutputFeature> All => Registered;

        static AtlasOutputFeatures()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<IAtlasOutputFeature>())
            {
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null) continue;
                var feature = (IAtlasOutputFeature)Activator.CreateInstance(type);
                if (Registered.Any(f => f.Id == feature.Id))
                    throw new ArgumentException($"[Nordic's Advanced Atlaser] Duplicate output feature ID '{feature.Id}'.");
                Registered.Add(feature);
            }
            Registered.Sort((a, b) => a.Section != b.Section ? a.Section.CompareTo(b.Section) : a.Order.CompareTo(b.Order));
        }

        public static IAtlasOutputFeature Find(string id) => Registered.FirstOrDefault(f => f.Id == id);

        // The modes the output shader supports. Empty when only Off would be left: the feature
        // isn't offered at all.
        public static IReadOnlyList<AtlasFeatureMode> Available(IAtlasOutputFeature feature, AtlasOutputCapabilities capabilities)
        {
            var modes = feature.Modes.Where(m => m.IsSupported(capabilities)).ToList();
            if (!modes.Any(m => m.Kind != AtlasFeatureModeKind.Off)) return Array.Empty<AtlasFeatureMode>();
            if (!(feature is IAtlasAlwaysOnFeature) && !modes.Contains(AtlasFeatureMode.Off)) modes.Add(AtlasFeatureMode.Off);
            return modes;
        }

        // The mode in use: the stored one when the output shader supports it, otherwise the
        // default. Null when the feature isn't offered.
        public static bool Switch(AtlasGeneratorJob job, string featureId, AtlasConflictSwitch conflictSwitch)
        {
            var choice = job.Features.Find(f => f.FeatureId == featureId);
            return choice != null && choice.Switches.TryGetValue(conflictSwitch.Id, out bool value) ? value : conflictSwitch.Default;
        }

        public static AtlasFeatureMode Effective(IAtlasOutputFeature feature, string storedMode, AtlasOutputCapabilities capabilities)
        {
            var modes = Available(feature, capabilities);
            if (modes.Count == 0) return null;
            return modes.FirstOrDefault(m => !string.IsNullOrEmpty(storedMode) && m.Id == storedMode)
                ?? modes.First(m => m.Kind != AtlasFeatureModeKind.Off);
        }
    }
}
