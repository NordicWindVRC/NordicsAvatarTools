using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // One feature as the output's settings and sources make it.
    public sealed class AtlasFeatureState
    {
        public IAtlasOutputFeature Feature;
        // The modes the output shader supports.
        public IReadOnlyList<AtlasFeatureMode> Modes;
        // The mode in use.
        public AtlasFeatureMode Mode;
        // Indices of the sources that have the feature.
        public readonly List<int> Present = new List<int>();
        // Those of them the feature isn't turned off for.
        public readonly List<int> Active = new List<int>();
        // The active sources need different values for the feature's shared value
        // (IAtlasSharedValueFeature), so the user can pick the source it is taken from.
        public bool SharedValuesDiffer;
    }

    // One output texture the chosen modes use, with its size and memory.
    public sealed class AtlasTextureUse
    {
        public AtlasOutputTexture Texture;
        public readonly List<AtlasVirtualMap> Maps = new List<AtlasVirtualMap>();
        public bool Required;
        // Required, or optional and kept on.
        public bool Enabled;
        public bool Alpha;
        public AtlasTextureKind Kind;
        public int Width, Height;
        public long PcBytes, AndroidBytes;
    }

    // What an output uses: its features with their modes and sources, and the textures those
    // need. Read by the inspector; built from the settings without changing them.
    public sealed class AtlasOutputOverview
    {
        public readonly List<AtlasFeatureState> Features = new List<AtlasFeatureState>();
        public readonly List<AtlasTextureUse> Textures = new List<AtlasTextureUse>();
        // The enabled textures' totals.
        public long PcBytes, AndroidBytes;

        // `unified` holds each source's unified material, null where it couldn't be read.
        public static AtlasOutputOverview Build(AtlasOutputConfiguration config, AtlasOutputCapabilities capabilities,
            IReadOnlyList<Material> materials, IReadOnlyList<AtlasUnifiedMaterial> unified)
        {
            var overview = new AtlasOutputOverview();
            if (capabilities == null) return overview;
            foreach (var feature in AtlasOutputFeatures.All)
            {
                var state = State(feature, config, capabilities, materials, unified);
                if (state != null) overview.Features.Add(state);
            }
            overview.AddTextures(config, capabilities);
            return overview;
        }

        public static AtlasFeatureState State(IAtlasOutputFeature feature, AtlasOutputConfiguration config,
            AtlasOutputCapabilities capabilities, IReadOnlyList<Material> materials, IReadOnlyList<AtlasUnifiedMaterial> unified)
        {
            var modes = AtlasOutputFeatures.Available(feature, capabilities);
            if (modes.Count == 0) return null;
            var setting = config.Feature(feature.Id);
            var state = new AtlasFeatureState
            {
                Feature = feature, Modes = modes,
                Mode = AtlasOutputFeatures.Effective(feature, setting?.mode, capabilities)
            };
            for (int i = 0; i < unified.Count; i++)
            {
                if (unified[i] == null) continue;
                if (feature is IAtlasSourceAwareFeature aware)
                {
                    // What the source's settings add (a pre-baked map); the build's own additions
                    // don't exist yet.
                    var entry = config.sources?.Find(e => e != null && e.material == materials[i]);
                    var probe = new AtlasGeneratorSource { Material = unified[i], PrebakedNormalMap = entry?.prebakedNormalMap };
                    if (!aware.IsPresent(probe)) continue;
                }
                else if (!feature.IsPresent(unified[i])) continue;
                state.Present.Add(i);
                bool off = setting?.disabledSources != null && setting.disabledSources.Contains(materials[i]);
                if (!off) state.Active.Add(i);
            }
            state.SharedValuesDiffer = feature is IAtlasSharedValueFeature shared && state.Active.Count > 1 &&
                state.Mode.Kind != AtlasFeatureModeKind.Off &&
                shared.SharedValuesDiffer(state.Active.Select(i => unified[i]).ToList(), capabilities);
            return state;
        }

        private void AddTextures(AtlasOutputConfiguration config, AtlasOutputCapabilities capabilities)
        {
            var required = new HashSet<AtlasVirtualMap>();
            var optional = new HashSet<AtlasVirtualMap>();
            foreach (var state in Features)
            {
                if (state.Active.Count == 0 || state.Mode.Kind == AtlasFeatureModeKind.Off) continue;
                required.UnionWith(state.Mode.RequiredMaps);
                optional.UnionWith(state.Mode.OptionalMaps.Where(capabilities.Provides));
            }
            Textures.AddRange(TextureUses(config, capabilities, required, optional));
            foreach (var use in Textures)
            {
                if (!use.Enabled) continue;
                PcBytes += use.PcBytes;
                AndroidBytes += use.AndroidBytes;
            }
        }

        // The textures that hold the given virtual maps, required ones first, with their sizes
        // (from the settings, or the defaults) and memory. Shared by the inspector, the manual
        // bake and the build, so they agree.
        public static List<AtlasTextureUse> TextureUses(AtlasOutputConfiguration config, AtlasOutputCapabilities capabilities,
            ICollection<AtlasVirtualMap> required, ICollection<AtlasVirtualMap> optional)
        {
            var uses = new List<AtlasTextureUse>();
            var main = config.mainTexture ?? new AtlasTextureSize();
            foreach (var texture in capabilities.Textures)
            {
                var use = new AtlasTextureUse { Texture = texture };
                foreach (var channel in texture.Channels)
                {
                    if (!required.Contains(channel.Map) && !optional.Contains(channel.Map)) continue;
                    use.Maps.Add(channel.Map);
                    use.Required |= required.Contains(channel.Map);
                    use.Alpha |= channel.Channels == AtlasTextureChannels.RGBA || channel.Channels == AtlasTextureChannels.A;
                }
                if (use.Maps.Count == 0) continue;
                var setting = config.Texture(texture.Id);
                use.Enabled = use.Required || setting == null || setting.enabled;
                use.Kind = texture.Normal ? AtlasTextureKind.Normal
                    : !texture.Srgb ? AtlasTextureKind.Data
                    : use.Alpha ? AtlasTextureKind.ColorAlpha : AtlasTextureKind.Color;
                int width, height;
                if (setting != null)
                {
                    width = setting.width;
                    height = setting.height;
                }
                else if (texture.SizeMode == AtlasTextureSizeMode.Square || texture.SizeMode == AtlasTextureSizeMode.Custom)
                    width = height = texture.DefaultSize;
                else
                {
                    width = main.width;
                    height = main.height;
                }
                AtlasTextureSizes.Normalize(texture.SizeMode, main.width, main.height, ref width, ref height);
                use.Width = width;
                use.Height = height;
                use.PcBytes = AtlasTextureMemory.Bytes(width, height, AtlasTextureMemory.Format(config.pcFormats, use.Kind));
                use.AndroidBytes = AtlasTextureMemory.Bytes(width, height, AtlasTextureMemory.Format(config.androidFormats, use.Kind));
                uses.Add(use);
            }
            // Required textures first, then in the layout's order.
            return uses.OrderBy(t => t.Required ? 0 : 1).ToList();
        }
    }
}
