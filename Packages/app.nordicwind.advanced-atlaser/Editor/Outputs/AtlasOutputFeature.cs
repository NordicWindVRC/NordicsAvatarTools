using System;
using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEditor;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // The shared vocabulary of feature modes (ATLAS_OUTPUT.md, "Features and modes").
    public enum AtlasFeatureModeKind { Native, Merged, Single, Baked, Emulated, Off }

    // The bakes that use the virtual light rig (ATLAS_OUTPUT.md, "Virtual light rigs"), each with
    // its strength in the inspector's Baking section.
    public enum AtlasLitBake { NormalShading, Occlusion, Matcaps }

    // A feature with modes that bake with the virtual light rig; the Baking section is shown
    // while one of them is in use.
    public interface IAtlasLitBakeFeature : IAtlasOutputFeature
    {
        // The lit bake a mode makes; null: none.
        AtlasLitBake? LitBake(AtlasFeatureMode mode);
    }

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
        // Used only when the user chooses it: the default passes it over, and is Off where no
        // other mode fits.
        public bool OptIn;

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

    // A shared value whose Automatic isn't the source with the largest region: the inspector
    // says what it is instead.
    public interface IAtlasAutomaticReference
    {
        string AutomaticRule { get; }
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

    // One item of a feature the user places one by one (a matcap): a key that stays the same
    // between sessions, a label, and the sources it's on.
    public sealed class AtlasFeatureInstance
    {
        public string Key;
        public string Label;
        public List<int> Sources = new List<int>();
    }

    // Where the user puts an item: Automatic (the feature's choice), its own place, combined
    // with another item (the stored text names it), or off.
    public enum AtlasInstancePlacement { Automatic = 0, Own = 1, Combined = 2, Off = 3, AsMatcap = 4, Baked = 5 }

    public static class AtlasInstancePlacements
    {
        // The stored value's ID for an item: its number the placement, its text the item combined with.
        public static string Id(string key) => "instance:" + key;
    }

    // A feature whose items the user places one by one.
    public interface IAtlasInstanceFeature : IAtlasOutputFeature
    {
        IReadOnlyList<AtlasFeatureInstance> Instances(AtlasGeneratorJob job, IReadOnlyList<int> sources);
        // Whether an item can also go into a matcap slot as a generated matcap.
        bool CanPlaceAsMatcap(AtlasOutputCapabilities capabilities);
        // Whether an item can be baked into the textures instead.
        bool CanBake(AtlasOutputCapabilities capabilities);
    }

    // A setting a feature offers in some of its modes (a strength, 0–1 by default), stored in its
    // values under ValueId.
    public sealed class AtlasFeatureOption
    {
        public string Id;
        public string Label;
        public string Tooltip;
        public float Default;
        public float Min, Max = 1f;

        public static string ValueId(string id) => "option:" + id;
    }

    // A feature with settings of its own in some modes.
    public interface IAtlasOptionFeature : IAtlasOutputFeature
    {
        IReadOnlyList<AtlasFeatureOption> Options(AtlasFeatureMode mode);
    }

    // A feature whose parts another feature bakes unless it's told they're left out: called with
    // the sources that have it but have it off (all of them when its mode is off).
    public interface IAtlasLeftOutFeature : IAtlasOutputFeature
    {
        void LeaveOut(AtlasGeneratorJob job, IReadOnlyList<int> sources);
    }

    // A feature a source can have without its material having it, from what the build adds (the
    // geometry bake's correction) or the source's settings (a pre-baked normal map).
    public interface IAtlasSourceAwareFeature : IAtlasOutputFeature
    {
        bool IsPresent(AtlasGeneratorSource source);
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
        // The plan being made: what the remap does per source, and the UV channels in use.
        public AtlasOutputPlan Plan;

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

        // A conflict switch's value: the user's, or its default.
        public static bool Switch(AtlasGeneratorJob job, string featureId, AtlasConflictSwitch conflictSwitch)
        {
            var choice = job.Features.Find(f => f.FeatureId == featureId);
            return choice != null && choice.Switches.TryGetValue(conflictSwitch.Id, out bool value) ? value : conflictSwitch.Default;
        }

        // The mode in use: the stored one when the output shader supports it, otherwise the
        // default. Null when the feature isn't offered.
        public static AtlasFeatureMode Effective(IAtlasOutputFeature feature, string storedMode, AtlasOutputCapabilities capabilities)
        {
            var modes = Available(feature, capabilities);
            if (modes.Count == 0) return null;
            return modes.FirstOrDefault(m => !string.IsNullOrEmpty(storedMode) && m.Id == storedMode)
                ?? modes.FirstOrDefault(m => m.Kind != AtlasFeatureModeKind.Off && !m.OptIn)
                ?? modes.FirstOrDefault(m => m.Kind == AtlasFeatureModeKind.Off)
                ?? modes.First(m => m.Kind != AtlasFeatureModeKind.Off);
        }
    }
}
