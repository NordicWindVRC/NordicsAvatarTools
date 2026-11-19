using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Edge light features with one setup for the atlas (ATLAS_OUTPUT.md, "Feature inventory"):
    // the settings come from the reference source (Automatic: the largest region, or a chosen
    // material), where they're kept per part goes into a map.
    internal static class AtlasEdgeLight
    {
        // The first enabled, static angle layer that matches.
        public static AtlasAngleLayer Layer(AtlasUnifiedMaterial material, System.Func<AtlasAngleLayer, bool> match)
        {
            if (material == null) return null;
            for (int i = 0; i < material.Layers.Count; i++)
                if (material.Layers[i] is AtlasAngleLayer layer && layer.Enabled && match(layer) &&
                    AtlasColorStack.IsStatic(material, "layers[" + i + "]"))
                    return layer;
            return null;
        }

        public static bool Live(AtlasUnifiedMaterial material, System.Func<AtlasAngleLayer, bool> match)
        {
            for (int i = 0; i < material.Layers.Count; i++)
                if (material.Layers[i] is AtlasAngleLayer layer && layer.Enabled && match(layer) &&
                    !AtlasColorStack.IsStatic(material, "layers[" + i + "]"))
                    return true;
            return false;
        }

        // The chosen source when it has the feature, otherwise the one with the largest region.
        public static int Reference(AtlasFeatureContext context, string featureId)
        {
            var choice = context.Job.Features.Find(f => f.FeatureId == featureId);
            if (choice != null && context.Sources.Contains(choice.ReferenceSource)) return choice.ReferenceSource;
            return context.Sources.OrderByDescending(i => context.Job.Sources[i].UVRegion.width * context.Job.Sources[i].UVRegion.height)
                .ThenBy(i => i).First();
        }

        public static bool Differ(IEnumerable<AtlasAngleLayer> layers, params string[] skip) =>
            layers.Where(l => l != null).Select(l => AtlasFieldwise.Signature(l, skip)).Distinct().Count() > 1;

        // One line naming the sources whose settings differ from the reference's.
        public static void DifferNote(AtlasFeatureContext context, int reference, string name, System.Func<AtlasUnifiedMaterial, AtlasAngleLayer> layer,
            params string[] skip)
        {
            string used = AtlasFieldwise.Signature(layer(context.Job.Sources[reference].Material), skip);
            var differ = context.Sources.Where(i => AtlasFieldwise.Signature(layer(context.Job.Sources[i].Material), skip) != used)
                .Select(i => (i + 1).ToString()).ToList();
            if (differ.Count > 0)
                context.Notes.Add($"The {name} is taken from source {reference + 1}; the {name} of " +
                                  (differ.Count == 1 ? "source " : "sources ") + string.Join(", ", differ) + " differs.");
        }
    }

    // Environmental rim: the reflection probe's color at the edge (an angle layer whose color
    // comes from the environment). Its strength per part goes into an allocated mask.
    public sealed class AtlasEnvironmentalRimFeature : IAtlasOutputFeature, IAtlasSharedValueFeature
    {
        public const string FeatureId = "environmentalrim";
        private static readonly string[] PerSource = { "Mask", "Opacity", "Enabled" };

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Native", Kind = AtlasFeatureModeKind.Native,
                OptionalMaps = new[] { AtlasVirtualMap.Mask1 },
                Requires = c => c.EnvironmentalRim
            },
            AtlasFeatureMode.Off
        };

        public string Id => FeatureId;
        public string DisplayName => "Environmental Rim";
        public AtlasFeatureSection Section => AtlasFeatureSection.Layers;
        public int Order => 2;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;
        public string SharedValueName => "Environmental Rim";

        private static bool Match(AtlasAngleLayer layer) =>
            layer.Source == AtlasAngleSource.ViewAngle && layer.ColorSource != AtlasAngleColorSource.Color;

        public static AtlasAngleLayer Layer(AtlasUnifiedMaterial material) => AtlasEdgeLight.Layer(material, Match);

        public bool IsPresent(AtlasUnifiedMaterial source) => Layer(source) != null;

        public bool SharedValuesDiffer(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputCapabilities capabilities) =>
            AtlasEdgeLight.Differ(sources.Select(Layer), PerSource);

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var active = new HashSet<int>(context.Sources);
            int reference = AtlasEdgeLight.Reference(context, FeatureId);
            var template = Layer(job.Sources[reference].Material);
            float strongest = context.Sources.Max(i => Layer(job.Sources[i].Material).Opacity);
            if (strongest <= 0f) return;
            AtlasAngleLayer Of(int i) => active.Contains(i) ? Layer(job.Sources[i].Material) : null;
            var allocated = AtlasMaskAllocator.Allocate(context, new AtlasMaskRequest
            {
                Name = "environmental rim",
                Key = i => Of(i) is AtlasAngleLayer l ? AtlasMaskKeys.Of(l.Mask, l.Opacity / strongest, job.Sources[i].Material) : AtlasMaskKeys.Constant(0f),
                Evaluate = (i, material, w, h) => Of(i) is AtlasAngleLayer l
                    ? AtlasMapEvaluator.ScaledMask(l.Mask, l.Opacity / strongest, w, h, material)
                    : AtlasMapEvaluator.Constant(AtlasVirtualMap.Mask1, 0f, w, h)
            });
            if (allocated == null)
            {
                context.Notes.Add("No mask place is left for the environmental rim; it's left out.");
                return;
            }
            if (allocated.IsConstant && allocated.Constant <= 0f) return;
            var copy = new AtlasAngleLayer();
            AtlasFieldwise.Copy(template, copy);
            copy.Mask = allocated.Mask ?? new AtlasMask();
            copy.Opacity = strongest * (allocated.IsConstant ? allocated.Constant : 1f);
            context.Output.Layers.Add(copy);
            AtlasEdgeLight.DifferNote(context, reference, "environmental rim", Layer, PerSource);
            if (context.Sources.Any(i => AtlasEdgeLight.Live(job.Sources[i].Material, Match)))
                context.Notes.Add("A live environmental rim (animated, audio-driven) isn't merged yet; it's left out.");
        }
    }

    // Backlight: light shining through from behind. Its color per part (color × texture, black
    // where a source has none) goes into the backlight color map, normalized to its brightest
    // value, which becomes the color.
    public sealed class AtlasBacklightFeature : IAtlasOutputFeature, IAtlasSharedValueFeature
    {
        public const string FeatureId = "backlight";
        private static readonly string[] PerSource = { "Mask", "Color", "Enabled" };

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Native", Kind = AtlasFeatureModeKind.Native,
                OptionalMaps = new[] { AtlasVirtualMap.BacklightColor },
                Requires = c => c.Backlight
            },
            AtlasFeatureMode.Off
        };

        public string Id => FeatureId;
        public string DisplayName => "Backlight";
        public AtlasFeatureSection Section => AtlasFeatureSection.Layers;
        public int Order => 3;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;
        public string SharedValueName => "Backlight";

        private static bool Match(AtlasAngleLayer layer) => layer.Source == AtlasAngleSource.Backlight;

        public static AtlasAngleLayer Layer(AtlasUnifiedMaterial material) => AtlasEdgeLight.Layer(material, Match);

        public bool IsPresent(AtlasUnifiedMaterial source) => Layer(source) != null;

        public bool SharedValuesDiffer(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputCapabilities capabilities) =>
            AtlasEdgeLight.Differ(sources.Select(Layer), PerSource);

        // A color texture the map can bake: a still texture on UV0.
        private static bool Bakeable(AtlasTextureReference map) =>
            map?.Texture is Texture2D && map.Space == AtlasTextureSpace.UV0 && map.Scroll == Vector2.zero && map.Spin == 0f &&
            map.Frames == null && map.Distortion == null && map.Decal == null;

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var active = new HashSet<int>(context.Sources);
            int reference = AtlasEdgeLight.Reference(context, FeatureId);
            var template = Layer(job.Sources[reference].Material);
            AtlasAngleLayer Of(int i) => active.Contains(i) ? Layer(job.Sources[i].Material) : null;
            var copy = new AtlasAngleLayer();
            AtlasFieldwise.Copy(template, copy);
            copy.Mask = null;
            context.Output.Layers.Add(copy);

            // One color everywhere: no map.
            var colors = Enumerable.Range(0, job.Sources.Count).Select(i => Of(i)).ToList();
            if (colors.All(l => l != null && l.Color.Map == null && l.Color.Color == colors[0].Color.Color))
            {
                copy.Color.Map = null;
                copy.Color.Color = colors[0].Color.Color;
            }
            else
            {
                var request = new AtlasMapRequest
                {
                    Map = AtlasVirtualMap.BacklightColor,
                    Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Color },
                    Evaluate = (i, material, w, h) =>
                    {
                        var layer = Of(i);
                        if (layer == null) return AtlasMapEvaluator.Constant(AtlasVirtualMap.BacklightColor, 0f, w, h);
                        var color = layer.Color.Color;
                        NordicsTextureWrapper result;
                        if (Bakeable(layer.Color.Map)) result = AtlasMapEvaluator.SampleColor(layer.Color.Map, (Texture2D)layer.Color.Map.Texture, w, h);
                        else result = AtlasMapEvaluator.Constant(AtlasVirtualMap.BacklightColor, 1f, w, h);
                        AtlasPixels.Map(result, c => new Color(c.r * color.r, c.g * color.g, c.b * color.b, 1f));
                        return result;
                    }
                };
                for (int i = 0; i < job.Sources.Count; i++) request.Sources.Add(i);
                request.Finish = map =>
                {
                    float peak = AtlasColorFeature.Peak(map);
                    copy.Color.Color = peak > 1f ? new Color(peak, peak, peak, 1f) : Color.white;
                    if (peak <= 1f) return;
                    map.ApplyColor(new ColorOperation
                    {
                        Opcode = ColorOperationCode.Tint, Parameters = { Color = new Color(1f / peak, 1f / peak, 1f / peak, 1f) }
                    });
                };
                context.Maps.Add(request);
                copy.Color.Color = Color.white;
                copy.Color.Map = request.Reference;
            }
            AtlasEdgeLight.DifferNote(context, reference, "backlight", Layer, PerSource);
            var unbaked = context.Sources.Where(i => Of(i)?.Color.Map != null && !Bakeable(Of(i).Color.Map)).Select(i => (i + 1).ToString()).ToList();
            if (unbaked.Count > 0)
                context.Notes.Add("A backlight color texture on another UV or moving isn't baked; its color is used (" +
                                  (unbaked.Count == 1 ? "source " : "sources ") + string.Join(", ", unbaked) + ").");
            if (context.Sources.Any(i => AtlasEdgeLight.Live(job.Sources[i].Material, Match)))
                context.Notes.Add("A live backlight (animated, audio-driven) isn't merged yet; it's left out.");
        }
    }
}
