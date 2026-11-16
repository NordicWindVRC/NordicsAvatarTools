using System;
using System.Collections.Generic;
using System.Linq;
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
        // The mode takes shared values from the reference source, which the user can choose.
        public bool UsesReferenceSource;
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

    // A virtual map the output needs, the sources that contribute to it, and the texture
    // reference in the output unified material that points at it once it is packed.
    public sealed class AtlasMapRequest
    {
        public AtlasVirtualMap Map;
        public AtlasTextureReference Reference;
        // Indices into AtlasGeneratorJob.Sources.
        public readonly List<int> Sources = new List<int>();
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
            if (!modes.Contains(AtlasFeatureMode.Off)) modes.Add(AtlasFeatureMode.Off);
            return modes;
        }

        // The mode in use: the stored one when the output shader supports it, otherwise the
        // default. Null when the feature isn't offered.
        public static AtlasFeatureMode Effective(IAtlasOutputFeature feature, string storedMode, AtlasOutputCapabilities capabilities)
        {
            var modes = Available(feature, capabilities);
            if (modes.Count == 0) return null;
            return modes.FirstOrDefault(m => !string.IsNullOrEmpty(storedMode) && m.Id == storedMode)
                ?? modes.First(m => m.Kind != AtlasFeatureModeKind.Off);
        }
    }
}
