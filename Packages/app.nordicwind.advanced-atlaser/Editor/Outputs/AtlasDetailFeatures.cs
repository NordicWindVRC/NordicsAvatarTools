using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // A source's layers the output keeps live, or leaves out, instead of baking them (attached
    // while resolving, cleared when the next resolve starts, like AtlasVertexColors).
    public static class AtlasLiveLayers
    {
        private static readonly ConditionalWeakTable<AtlasUnifiedMaterial, HashSet<AtlasLayer>> Layers =
            new ConditionalWeakTable<AtlasUnifiedMaterial, HashSet<AtlasLayer>>();

        public static void Add(AtlasUnifiedMaterial material, AtlasLayer layer)
        {
            if (material != null && layer != null) Layers.GetOrCreateValue(material).Add(layer);
        }

        public static void Clear(AtlasUnifiedMaterial material)
        {
            if (material != null) Layers.Remove(material);
        }

        /// <summary>Whether the bakes skip the layer (kept live, or left out).</summary>
        public static bool Skipped(AtlasUnifiedMaterial material, AtlasLayer layer) =>
            material != null && Layers.TryGetValue(material, out var set) && set.Contains(layer);
    }

    // Tiled layers kept live (ATLAS_OUTPUT.md, "UV channels"): a texture layer tiled over the
    // surface (a detail texture or detail normal) baked at a region's size turns blurry. Kept
    // live, the output samples the texture through a preserved UV channel holding each source's
    // UV0 × its tiling + offset, so every source keeps its own scale; each source's strength and
    // mask go into the detail's mask, 0 where a source doesn't have it. The output has one slot:
    // the sources whose layer looks the same as the reference source's share it, the others are
    // baked as before. Modes: kept live, baked (the base color or normal map), off (left out).
    public abstract class AtlasDetailFeature : IAtlasSharedValueFeature, IAtlasLeftOutFeature
    {
        public void LeaveOut(AtlasGeneratorJob job, IReadOnlyList<int> sources)
        {
            foreach (int i in sources) AtlasLiveLayers.Add(job.Sources[i].Material, Candidate(job.Sources[i].Material).Layer);
        }

        protected abstract AtlasSurfaceTarget Target { get; }
        protected abstract string Name { get; }
        protected abstract AtlasVirtualMap MaskMap { get; }
        protected abstract bool Supports(AtlasOutputCapabilities capabilities);

        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public AtlasFeatureSection Section => AtlasFeatureSection.Surface;
        public abstract int Order { get; }
        public string SharedValueName => DisplayName;

        private AtlasFeatureMode[] modes;
        public IReadOnlyList<AtlasFeatureMode> Modes => modes ?? (modes = new[]
        {
            new AtlasFeatureMode { Id = "live", Label = "Kept live", Kind = AtlasFeatureModeKind.Native, Requires = Supports },
            new AtlasFeatureMode { Id = "baked", Label = "Baked", Kind = AtlasFeatureModeKind.Baked },
            AtlasFeatureMode.Off
        });

        // A layer tiled over the surface: a still texture on UV0, repeated more than once.
        public static bool Tiled(AtlasSurfaceLayer layer)
        {
            var map = layer.Source.Map;
            return map?.Texture is Texture2D && map.Space == AtlasTextureSpace.UV0 && map.Decal == null && map.Frames == null &&
                   map.Distortion == null && map.Scroll == Vector2.zero && map.Spin == 0f &&
                   (Mathf.Abs(map.Tiling.x) > 1.001f || Mathf.Abs(map.Tiling.y) > 1.001f);
        }

        // The source's last enabled, static, tiled layer of this kind, with its index.
        protected (AtlasSurfaceLayer Layer, int Index) Candidate(AtlasUnifiedMaterial material)
        {
            if (material == null) return (null, -1);
            for (int i = material.Layers.Count - 1; i >= 0; i--)
                if (material.Layers[i] is AtlasSurfaceLayer layer && layer.Enabled && layer.Targets.Any(t => t.Target == Target) &&
                    Tiled(layer) && AtlasColorStack.IsStatic(material, "layers[" + i + "]"))
                    return (layer, i);
            return (null, -1);
        }

        public bool IsPresent(AtlasUnifiedMaterial source) => Candidate(source).Layer != null;

        // Why the layer can't be kept live in this output, or null.
        protected virtual string Unkeepable(AtlasUnifiedMaterial material, AtlasSurfaceLayer layer, int index, AtlasOutputCapabilities capabilities)
        {
            if (layer.Targets.Count != 1) return "it changes more than one thing";
            if (Mathf.Abs(layer.Source.Map.Rotation) > 1e-3f) return "it's rotated";
            if (layer.Mask != null && !AtlasMapEvaluator.CanEvaluate(layer.Mask, material)) return "its mask can't be baked";
            // Layers after it of the same kind would change what it does.
            for (int i = index + 1; i < material.Layers.Count; i++)
                if (material.Layers[i] is AtlasSurfaceLayer later && later.Enabled && later.Targets.Any(t => t.Target == Target))
                    return "a layer after it changes its result";
            return null;
        }

        // What shares the slot: the texture and the layer's look apart from its strength and mask.
        private static string Look(AtlasSurfaceLayer layer) =>
            RuntimeHelpers.GetHashCode(layer.Source.Map.Texture) + "|" + layer.Targets[0].Blend + "|" +
            AtlasFieldwise.Signature(layer.Source.Color) + "|" + layer.SourceAlphaIsOpacity + "|" + layer.Placement;

        public bool SharedValuesDiffer(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputCapabilities capabilities) =>
            sources.Select(m => Candidate(m).Layer).Where(l => l != null && l.Targets.Count == 1).Select(Look).Distinct().Count() > 1;

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var candidates = context.Sources.Select(i => (Index: i, Found: Candidate(job.Sources[i].Material)))
                .Where(c => c.Found.Layer != null).ToList();
            // Baked: the base color or normal map bakes it as before.
            if (context.Mode.Kind != AtlasFeatureModeKind.Native) return;
            var capabilities = context.Capabilities;

            var reasons = new Dictionary<string, List<int>>();
            void Baked(string reason, int source)
            {
                if (!reasons.TryGetValue(reason, out var list)) reasons[reason] = list = new List<int>();
                list.Add(source);
            }
            var keepable = new List<(int Index, AtlasSurfaceLayer Layer)>();
            foreach (var c in candidates)
            {
                string reason = Unkeepable(job.Sources[c.Index].Material, c.Found.Layer, c.Found.Index, capabilities);
                if (reason != null) Baked(reason, c.Index);
                else keepable.Add((c.Index, c.Found.Layer));
            }
            if (keepable.Count == 0)
            {
                Notes(context, reasons);
                return;
            }
            // The reference source's look, or the one on the largest area.
            var choice = job.Features.Find(f => f.FeatureId == Id);
            string look = keepable.Any(k => choice != null && k.Index == choice.ReferenceSource)
                ? Look(keepable.First(k => k.Index == choice.ReferenceSource).Layer)
                : keepable.GroupBy(k => Look(k.Layer))
                    .OrderByDescending(g => g.Sum(k => job.Sources[k.Index].UVRegion.width * job.Sources[k.Index].UVRegion.height)).First().Key;
            var live = keepable.Where(k => Look(k.Layer) == look).ToList();
            foreach (var k in keepable.Where(k => Look(k.Layer) != look))
                Baked($"it differs from the one kept live ({DisplayName} from picks it)", k.Index);

            // One UV channel per set of tilings: each source's UV0 × tiling + offset.
            string uvKey = string.Join(";", live.Select(k => k.Index + ":" + k.Layer.Source.Map.Tiling + k.Layer.Source.Map.Offset));
            if (!context.Plan.DetailChannels.TryGetValue(uvKey, out int channel))
            {
                channel = context.Plan.AllocateUVChannel(capabilities.DetailUVChannels);
                if (channel < 0)
                {
                    foreach (var k in live) Baked("no UV channel is free for it", k.Index);
                    Notes(context, reasons);
                    return;
                }
                context.Plan.DetailChannels[uvKey] = channel;
                foreach (var k in live)
                {
                    if (!context.Plan.SourceRemaps.TryGetValue(k.Index, out var remap))
                        context.Plan.SourceRemaps[k.Index] = remap = new AtlasSourceRemap();
                    var tiling = k.Layer.Source.Map.Tiling;
                    var offset = k.Layer.Source.Map.Offset;
                    remap.PreservedUVs.Add(new AtlasPreservedUV { Channel = channel, SourceChannel = 0, Convert = uv => Vector2.Scale(uv, tiling) + offset });
                }
            }

            // Strengths: relative to the strongest where the output has a strength, else as they are.
            var liveSet = live.ToDictionary(k => k.Index, k => k.Layer);
            float strongest = HasStrength(capabilities) ? live.Max(k => k.Layer.Targets[0].Opacity) : 1f;
            if (strongest <= 0f) return;
            float Ratio(AtlasSurfaceLayer layer) => Mathf.Clamp01(layer.Targets[0].Opacity / strongest);
            var mask = Mask(context, liveSet, Ratio, out float scale);
            if (mask == null && scale < 0f)
            {
                foreach (var k in live) Baked("no mask place is left for its strength", k.Index);
                Notes(context, reasons);
                return;
            }
            var template = live[0].Layer;
            var output = new AtlasSurfaceLayer
            {
                Mask = mask, Placement = template.Placement, SourceAlphaIsOpacity = template.SourceAlphaIsOpacity
            };
            output.Source.Map = new AtlasTextureReference
            {
                Texture = template.Source.Map.Texture, Space = AtlasTextureSpace.UV0 + channel, Interpretation = template.Source.Map.Interpretation
            };
            output.Source.Color = template.Source.Color;
            output.Targets.Add(new AtlasLayerTarget(Target, template.Targets[0].Blend, strongest * scale));
            context.Output.Layers.Add(output);
            foreach (var k in live) AtlasLiveLayers.Add(job.Sources[k.Index].Material, k.Layer);
            Notes(context, reasons);
        }

        // Whether the output's slot has a strength of its own.
        protected virtual bool HasStrength(AtlasOutputCapabilities capabilities) => true;

        // The mask: the live sources' masks × their strength ratio, 0 elsewhere. Null with scale
        // ≥ 0 where it's one value everywhere (scale); null with scale −1 when no place is left.
        protected virtual AtlasMask Mask(AtlasFeatureContext context, Dictionary<int, AtlasSurfaceLayer> live,
            System.Func<AtlasSurfaceLayer, float> ratio, out float scale)
        {
            var job = context.Job;
            scale = 1f;
            var keys = Enumerable.Range(0, job.Sources.Count).Select(i => live.TryGetValue(i, out var layer)
                ? AtlasMaskKeys.Of(layer.Mask, ratio(layer), job.Sources[i].Material) : AtlasMaskKeys.Constant(0f)).ToList();
            // A constant goes into the strength, where the slot has one.
            if (keys.All(k => k == keys[0]) && AtlasMaskKeys.IsConstant(keys[0], out float constant) &&
                (HasStrength(context.Capabilities) || Mathf.Approximately(constant, 1f)))
            {
                scale = constant;
                return null;
            }
            NordicWind.TextureWrapper.NordicsTextureWrapper Evaluate(int i, AtlasUnifiedMaterial material, int w, int h) =>
                live.TryGetValue(i, out var layer)
                    ? AtlasMapEvaluator.ScaledMask(layer.Mask, ratio(layer), w, h, material)
                    : AtlasMapEvaluator.Constant(MaskMap, 0f, w, h);
            if (context.Capabilities.DetailMaskByChannel)
            {
                // One allocated place, shared by both details where their masks are the same.
                var allocated = AtlasMaskAllocator.Allocate(context, new AtlasMaskRequest
                {
                    Name = Name, Key = i => keys[i], Evaluate = Evaluate
                });
                if (allocated == null) { scale = -1f; return null; }
                if (allocated.IsConstant) { scale = allocated.Constant; return null; }
                return allocated.Mask;
            }
            var request = new AtlasMapRequest
            {
                Map = MaskMap,
                Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Data },
                Evaluate = Evaluate
            };
            for (int i = 0; i < job.Sources.Count; i++) request.Sources.Add(i);
            context.Maps.Add(request);
            return new AtlasMask { Map = request.Reference, Channel = MaskChannel };
        }

        // The channel of the output's detail mask texture the mask map is in.
        protected abstract AtlasChannel MaskChannel { get; }

        private void Notes(AtlasFeatureContext context, Dictionary<string, List<int>> reasons)
        {
            foreach (var reason in reasons)
                context.Notes.Add($"The tiled {Name} of " + AtlasLiveMasks.Numbers(reason.Value) + " is baked: " + reason.Key + ".");
        }
    }

    public sealed class AtlasDetailTextureFeature : AtlasDetailFeature
    {
        public const string FeatureId = "detailtexture";
        public override string Id => FeatureId;
        public override string DisplayName => "Detail Texture";
        public override int Order => 6;
        protected override string Name => "detail texture";
        protected override AtlasSurfaceTarget Target => AtlasSurfaceTarget.BaseColor;
        protected override AtlasVirtualMap MaskMap => AtlasVirtualMap.DetailTextureMask;
        protected override AtlasChannel MaskChannel => AtlasChannel.Red;
        protected override bool Supports(AtlasOutputCapabilities capabilities) => capabilities.DetailTextureBlends.Count > 0 && capabilities.DetailUVChannels > 0;
        protected override bool HasStrength(AtlasOutputCapabilities capabilities) => capabilities.DetailStrength;

        protected override string Unkeepable(AtlasUnifiedMaterial material, AtlasSurfaceLayer layer, int index, AtlasOutputCapabilities capabilities)
        {
            string reason = base.Unkeepable(material, layer, index, capabilities);
            if (reason != null) return reason;
            if (!capabilities.DetailTextureBlends.Contains(layer.Targets[0].Blend)) return "the output shader's detail texture lacks its blend";
            var color = layer.Source.Color;
            if (!capabilities.DetailStrength && (Mathf.Abs(color.r - 1f) + Mathf.Abs(color.g - 1f) + Mathf.Abs(color.b - 1f) > 1e-3f))
                return "the output shader's detail texture has no tint";
            if (!capabilities.DetailStrength && layer.Targets[0].Opacity > 1f) return "it's stronger than the output shader's detail texture";
            // Adjustments of the layer, or of the whole surface color, apply after it.
            if (material.ColorAdjustments.Any(a => a.Layer == layer || a.Target == AtlasAdjustmentTarget.SurfaceColor))
                return "a color adjustment changes it";
            return null;
        }
    }

    public sealed class AtlasDetailNormalFeature : AtlasDetailFeature
    {
        public const string FeatureId = "detailnormal";
        public override string Id => FeatureId;
        public override string DisplayName => "Detail Normal";
        public override int Order => 7;
        protected override string Name => "detail normal";
        protected override AtlasSurfaceTarget Target => AtlasSurfaceTarget.Normal;
        protected override AtlasVirtualMap MaskMap => AtlasVirtualMap.DetailNormalMask;
        protected override AtlasChannel MaskChannel => AtlasChannel.Green;
        protected override bool Supports(AtlasOutputCapabilities capabilities) => capabilities.DetailNormal && capabilities.DetailUVChannels > 0;

        protected override AtlasMask Mask(AtlasFeatureContext context, Dictionary<int, AtlasSurfaceLayer> live,
            System.Func<AtlasSurfaceLayer, float> ratio, out float scale)
        {
            var mask = base.Mask(context, live, ratio, out scale);
            // An output with one detail mask for both: the detail texture's, where it has one.
            if (context.Capabilities.DetailMaskByChannel)
            {
                var texture = AtlasDetailFeatures.Of(context.Output, AtlasSurfaceTarget.BaseColor);
                if (texture != null && texture.Mask?.Map != mask?.Map)
                {
                    scale = -1f;
                    context.Notes.Add("The output shader has one detail mask; the detail normal's differs from the detail texture's.");
                    return null;
                }
            }
            return mask;
        }
    }

    public static class AtlasDetailFeatures
    {
        /// <summary>The output's live detail layer of the target (a surface layer on a preserved
        /// UV channel), if any.</summary>
        public static AtlasSurfaceLayer Of(AtlasUnifiedMaterial output, AtlasSurfaceTarget target) =>
            output.Layers.OfType<AtlasSurfaceLayer>().FirstOrDefault(l => l.Targets.Any(t => t.Target == target) &&
                l.Source.Map != null && l.Source.Map.Space >= AtlasTextureSpace.UV1 && l.Source.Map.Space <= AtlasTextureSpace.UV3);

        public static int UV(AtlasSurfaceLayer layer) => layer.Source.Map.Space - AtlasTextureSpace.UV0;
    }
}
