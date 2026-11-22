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
        // The feature's switches whose sources disagree (IAtlasConflictSwitchFeature).
        public readonly List<AtlasConflictSwitch> Switches = new List<AtlasConflictSwitch>();
        // The items the user places one by one (IAtlasInstanceFeature), when its mode isn't off.
        public readonly List<AtlasFeatureInstance> Instances = new List<AtlasFeatureInstance>();
        // Whether an item can also be placed in a matcap slot.
        public bool InstancesAsMatcap;
        // Whether an item can be baked into the textures instead.
        public bool InstancesCanBake;
        // Whether an item can be kept as its average effect in the base color instead.
        public bool InstancesCanAverage;
        // Sources with different live global mask setups for the feature, one of each (Live from).
        public readonly List<int> LiveSetups = new List<int>();
        // The feature's own settings in its mode (IAtlasOptionFeature).
        public readonly List<AtlasFeatureOption> Options = new List<AtlasFeatureOption>();
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
        // Made by the build besides the atlas maps (a combined matcap, a shadow ramp): fixed size.
        public bool Generated;
        // A source's own texture the output uses as it is (a matcap, a ramp, a cubemap).
        public Texture Source;
    }

    // What an output uses: its features with their modes and sources, and the textures those
    // need. Read by the inspector; built from the settings without changing them.
    public sealed class AtlasOutputOverview
    {
        public readonly List<AtlasFeatureState> Features = new List<AtlasFeatureState>();
        public readonly List<AtlasTextureUse> Textures = new List<AtlasTextureUse>();
        // The enabled textures' totals.
        public long PcBytes, AndroidBytes;

        // For an output shader: resolved as the build does, with the lighting matched by look.
        public static AtlasOutputOverview Build(AtlasOutputConfiguration config, IAtlasOutputShader shader,
            IReadOnlyList<Material> materials, IReadOnlyList<AtlasUnifiedMaterial> unified) =>
            Build(config, shader?.Capabilities, shader, materials, unified);

        // `unified` holds each source's unified material, null where it couldn't be read.
        public static AtlasOutputOverview Build(AtlasOutputConfiguration config, AtlasOutputCapabilities capabilities,
            IReadOnlyList<Material> materials, IReadOnlyList<AtlasUnifiedMaterial> unified) =>
            Build(config, capabilities, null, materials, unified);

        private static AtlasOutputOverview Build(AtlasOutputConfiguration config, AtlasOutputCapabilities capabilities, IAtlasOutputShader shader,
            IReadOnlyList<Material> materials, IReadOnlyList<AtlasUnifiedMaterial> unified)
        {
            var overview = new AtlasOutputOverview();
            if (capabilities == null) return overview;
            AtlasGeneratorJob probe = null;
            foreach (var feature in AtlasOutputFeatures.All)
            {
                var state = State(feature, config, capabilities, materials, unified);
                if (state == null) continue;
                if (feature is IAtlasOptionFeature withOptions && state.Mode != null && state.Mode.Kind != AtlasFeatureModeKind.Off && state.Active.Count > 0)
                    state.Options.AddRange(withOptions.Options(state.Mode));
                if (feature is IAtlasConflictSwitchFeature switches && state.Present.Count > 1)
                    state.Switches.AddRange(switches.ConflictingSwitches(probe ?? (probe = Probe(config, materials, unified)), capabilities));
                if (feature is IAtlasInstanceFeature items && state.Mode != null && state.Mode.Kind != AtlasFeatureModeKind.Off && state.Active.Count > 0)
                {
                    state.Instances.AddRange(items.Instances(probe ?? (probe = Probe(config, materials, unified)), state.Active));
                    state.InstancesAsMatcap = items.CanPlaceAsMatcap(capabilities);
                    state.InstancesCanBake = items.CanBake(capabilities);
                    state.InstancesCanAverage = items.CanAverage(capabilities);
                }
                overview.Features.Add(state);
            }
            // What only a resolve knows: the live global mask setups the user picks between, the
            // textures the build makes and the sources' textures it uses as they are.
            var plan = unified.Count > 0 && unified.All(u => u != null) ? Resolved(config, capabilities, shader, materials, unified) : null;
            if (plan != null)
                foreach (var state in overview.Features)
                {
                    if (plan.LiveSetups.TryGetValue(state.Feature.Id, out var setups)) state.LiveSetups.AddRange(setups);
                    // How each placed item ends up shown.
                    foreach (var instance in state.Instances)
                        if (plan.Ways.TryGetValue((state.Feature.Id, instance.Key), out var way))
                        {
                            instance.Way = way.Text;
                            instance.LeftOut = way.LeftOut;
                        }
                    // How far a matcap slot's fit may shift the base color, while one does.
                    if (state.Feature is AtlasMatcapFeature && plan.BaseColorShifts) state.Options.Add(AtlasMatcapFeature.ShiftOption);
                    // How a world-fixed matcap is shown in a slot that follows the view, while one is.
                    if (state.Feature is AtlasMatcapFeature && plan.MatcapViews) state.Options.Add(AtlasMatcapFeature.ViewOption);
                    if (!(state.Feature is AtlasLightingFeature)) continue;
                    // Level compensation, while a material would be scaled.
                    if (plan.LevelScales.Count > 0) state.Switches.Add(AtlasLevelCompensation.Switch);
                }
            overview.AddTextures(config, capabilities, plan);
            if (plan != null) overview.AddOtherTextures(plan, config, capabilities);
            return overview;
        }

        // The last resolve, kept while the settings, output shader and sources stay the same (the
        // inspector rebuilds the overview on every repaint).
        private static string resolvedKey;
        private static AtlasOutputPlan resolvedPlan;

        private static AtlasOutputPlan Resolved(AtlasOutputConfiguration config, AtlasOutputCapabilities capabilities, IAtlasOutputShader shader,
            IReadOnlyList<Material> materials, IReadOnlyList<AtlasUnifiedMaterial> unified)
        {
            string key = JsonUtility.ToJson(config) + "|" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(capabilities) + "|" +
                         (shader?.ShaderId ?? "") + "|" +
                         string.Join(",", unified.Select(u => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(u)));
            if (key == resolvedKey) return resolvedPlan;
            AtlasOutputPlan plan;
            try
            {
                var job = Probe(config, materials, unified);
                plan = shader != null ? AtlasOutputPipeline.Resolve(job, shader) : AtlasOutputPipeline.Resolve(job, capabilities);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                plan = null;
            }
            resolvedKey = key;
            resolvedPlan = plan;
            return plan;
        }

        // The generated textures, the generated shadow ramp, and the sources' textures the output
        // material uses as they are, with their memory (a source texture's: as imported, and its
        // Android override, or the output's Android format for its kind).
        private void AddOtherTextures(AtlasOutputPlan plan, AtlasOutputConfiguration config, AtlasOutputCapabilities capabilities)
        {
            var other = plan.GeneratedTextures.Select(g => AtlasGeneratedTextures.Use(g, config)).ToList();
            if (AtlasLightingFeature.NeedsGeneratedRamp(plan.Output, capabilities))
            {
                long bytes = AtlasTextureMemory.Bytes(AtlasRampTexture.Width, 4, AtlasTextureFormat.RGBA32, mipmaps: false);
                other.Add(new AtlasTextureUse
                {
                    Texture = new AtlasOutputTexture("ShadowRamp", "Shadow ramp") { SizeMode = AtlasTextureSizeMode.Custom },
                    Required = true, Enabled = true, Generated = true, Kind = AtlasTextureKind.Color,
                    Width = AtlasRampTexture.Width, Height = 4, PcBytes = bytes, AndroidBytes = bytes
                });
            }
            var found = new List<Texture>();
            Collect(plan.Output, new HashSet<object>(), found);
            other.AddRange(found.Select(t => SourceUse(t, config)));
            foreach (var use in other)
            {
                Textures.Add(use);
                PcBytes += use.PcBytes;
                AndroidBytes += use.AndroidBytes;
            }
        }

        // Every texture reference in the output unified material that already has a texture: the
        // atlas maps and generated textures get theirs when the build makes them.
        private static void Collect(object value, HashSet<object> seen, List<Texture> found)
        {
            if (value == null || value is UnityEngine.Object || value is string) return;
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || type.IsValueType) return;
            if (!seen.Add(value)) return;
            if (value is AtlasTextureReference reference)
            {
                if (reference.Texture != null && !found.Contains(reference.Texture)) found.Add(reference.Texture);
                return;
            }
            if (value is System.Collections.IEnumerable items)
            {
                foreach (var item in items) Collect(item, seen, found);
                return;
            }
            if (type.Namespace != typeof(AtlasUnifiedMaterial).Namespace) return;
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                Collect(field.GetValue(value), seen, found);
        }

        private static AtlasTextureUse SourceUse(Texture texture, AtlasOutputConfiguration config)
        {
            bool alpha = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.HasAlphaChannel(texture.graphicsFormat);
            var kind = alpha ? AtlasTextureKind.ColorAlpha : AtlasTextureKind.Color;
            long android;
            var importer = UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(texture)) as UnityEditor.TextureImporter;
            var settings = importer?.GetPlatformTextureSettings("Android");
            var format = settings != null && settings.overridden && settings.format > 0
                ? UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetGraphicsFormat((TextureFormat)(int)settings.format, importer.sRGBTexture)
                : UnityEngine.Experimental.Rendering.GraphicsFormat.None;
            if (format != UnityEngine.Experimental.Rendering.GraphicsFormat.None)
            {
                float scale = Mathf.Min(1f, settings.maxTextureSize / (float)Mathf.Max(texture.width, texture.height));
                android = SourceBytes(texture, Mathf.Max(1, Mathf.RoundToInt(texture.width * scale)), Mathf.Max(1, Mathf.RoundToInt(texture.height * scale)), format);
            }
            else
                android = AtlasTextureMemory.Bytes(texture.width, texture.height, AtlasTextureMemory.Format(config.androidFormats, kind)) * (texture is Cubemap ? 6 : 1);
            return new AtlasTextureUse
            {
                Texture = new AtlasOutputTexture("Source:" + texture.name, texture.name),
                Required = true, Enabled = true, Source = texture, Kind = kind, Alpha = alpha,
                Width = texture.width, Height = texture.height,
                PcBytes = SourceBytes(texture, texture.width, texture.height, texture.graphicsFormat), AndroidBytes = android
            };
        }

        private static long SourceBytes(Texture texture, int width, int height, UnityEngine.Experimental.Rendering.GraphicsFormat format)
        {
            long total = 0;
            int mips = Mathf.Max(1, texture.mipmapCount);
            for (int m = 0; m < mips; m++)
                total += UnityEngine.Experimental.Rendering.GraphicsFormatUtility.ComputeMipmapSize(Mathf.Max(1, width >> m), Mathf.Max(1, height >> m), format);
            return texture is Cubemap ? total * 6 : total;
        }

        // The sources and choices as a job, for what depends on more than one feature.
        private static AtlasGeneratorJob Probe(AtlasOutputConfiguration config, IReadOnlyList<Material> materials, IReadOnlyList<AtlasUnifiedMaterial> unified)
        {
            var job = new AtlasGeneratorJob();
            // With each source's area in the layout, so the automatic choices are the build's.
            for (int i = 0; i < unified.Count; i++)
            {
                var material = i < materials.Count ? materials[i] : null;
                var entry = material == null ? null : config.sources?.Find(e => e != null && e.material == material);
                job.Sources.Add(new AtlasGeneratorSource { Material = unified[i], UVRegion = entry?.uvRegion ?? default });
            }
            AtlasOutputPipeline.AddChoices(config, materials, job);
            return job;
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

        // The atlas textures: with a resolve, the maps the build makes (optional where a mode
        // declares them optional); without one, the maps the modes declare.
        private void AddTextures(AtlasOutputConfiguration config, AtlasOutputCapabilities capabilities, AtlasOutputPlan plan)
        {
            var required = new HashSet<AtlasVirtualMap>();
            var optional = new HashSet<AtlasVirtualMap>();
            foreach (var state in Features)
            {
                if (state.Active.Count == 0 || state.Mode.Kind == AtlasFeatureModeKind.Off) continue;
                required.UnionWith(state.Mode.RequiredMaps);
                optional.UnionWith(state.Mode.OptionalMaps.Where(capabilities.Provides));
            }
            if (plan != null)
            {
                var made = new HashSet<AtlasVirtualMap>(plan.Maps.Select(r => r.Map));
                optional.IntersectWith(made);
                made.ExceptWith(optional);
                required = made;
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
                    int divisor = texture.SizeMode == AtlasTextureSizeMode.MainAspect ? Mathf.Max(1, texture.MainDivisor) : 1;
                    width = main.width / divisor;
                    height = main.height / divisor;
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
