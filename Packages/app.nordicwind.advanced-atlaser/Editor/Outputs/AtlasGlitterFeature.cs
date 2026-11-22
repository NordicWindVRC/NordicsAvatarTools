using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Glitter and sparkle (ATLAS_OUTPUT.md, "Feature inventory"): the glitter pattern layer.
    // - Native: one setup for the atlas from the reference source (Glitter from). Its cells are
    //   a grid over its UVs, so it reads a preserved UV channel holding each source's UV ×
    //   (its density ÷ the output's), and every source keeps its own particle size; each
    //   source's opacity × mask goes into an allocated mask, 0 where it has none.
    // - Emulated, for outputs without glitter: the same cells drawn as still points in UV space,
    //   into the emission (they glow, as additive glitter does), the metallic and gloss (they
    //   catch the light and reflections), and the base color, each with its own strength.
    public sealed class AtlasGlitterFeature : IAtlasSharedValueFeature, IAtlasOptionFeature
    {
        public const string FeatureId = "glitter";
        public const string EmissionOption = "emission", GlossOption = "gloss", ColorOption = "color";

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode { Id = "native", Label = "Native", Kind = AtlasFeatureModeKind.Native, OptionalMaps = new[] { AtlasVirtualMap.Mask1 }, Requires = c => c.Glitter },
            new AtlasFeatureMode
            {
                Id = "emulated", Label = "Points in the maps", Kind = AtlasFeatureModeKind.Emulated,
                Requires = c => !c.Glitter && (c.Provides(AtlasVirtualMap.Emission) || c.Provides(AtlasVirtualMap.Roughness)),
                // Still points don't twinkle: only when chosen, so outputs without glitter
                // default to Off.
                OptIn = true
            },
            AtlasFeatureMode.Off
        };

        private static readonly AtlasFeatureOption[] OptionList =
        {
            new AtlasFeatureOption
            {
                Id = EmissionOption, Label = "Glow", Default = 1f,
                Tooltip = "How strongly the points glow in the emission map, as additive glitter does (0: not at all)."
            },
            new AtlasFeatureOption
            {
                Id = GlossOption, Label = "Shine", Default = 1f,
                Tooltip = "How metallic and glossy the points are, so they catch the light and reflections (0: not at all)."
            },
            new AtlasFeatureOption
            {
                Id = ColorOption, Label = "Color", Default = 0f,
                Tooltip = "How much the points take the glitter's color in the base color (0: not at all)."
            }
        };

        public string Id => FeatureId;
        public string DisplayName => "Glitter";
        public AtlasFeatureSection Section => AtlasFeatureSection.Effects;
        public int Order => 0;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;
        public string SharedValueName => "Glitter";

        public IReadOnlyList<AtlasFeatureOption> Options(AtlasFeatureMode mode) =>
            mode?.Kind == AtlasFeatureModeKind.Emulated ? OptionList : System.Array.Empty<AtlasFeatureOption>();

        // The source's first enabled, static glitter layer.
        public static AtlasPatternLayer Layer(AtlasUnifiedMaterial material)
        {
            if (material == null) return null;
            for (int i = 0; i < material.Layers.Count; i++)
                if (material.Layers[i] is AtlasPatternLayer layer && layer.Kind == AtlasPatternKind.Glitter && layer.Enabled &&
                    layer.Glitter != null && AtlasColorStack.IsStatic(material, "layers[" + i + "]"))
                    return layer;
            return null;
        }

        public bool IsPresent(AtlasUnifiedMaterial source) =>
            source != null && source.Layers.OfType<AtlasPatternLayer>().Any(l => l.Kind == AtlasPatternKind.Glitter && l.Enabled && l.Glitter != null);

        // The setup apart from what's kept per source: its mask, opacity and density.
        private static string Setup(AtlasPatternLayer layer)
        {
            var glitter = new AtlasGlitter();
            AtlasFieldwise.Copy(layer.Glitter, glitter);
            glitter.Density = 0f;
            glitter.Space = AtlasTextureSpace.UV0;
            return AtlasFieldwise.Signature(layer, "Mask", "Opacity", "Enabled", "Glitter") + "|" + AtlasFieldwise.Signature(glitter);
        }

        public bool SharedValuesDiffer(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputCapabilities capabilities) =>
            sources.Select(Layer).Where(l => l != null).Select(Setup).Distinct().Count() > 1;

        private static bool OnUV(AtlasPatternLayer layer) =>
            layer.Glitter.Space >= AtlasTextureSpace.UV0 && layer.Glitter.Space <= AtlasTextureSpace.UV3;

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var layers = context.Sources.Select(i => (Index: i, Layer: Layer(job.Sources[i].Material))).Where(s => s.Layer != null).ToList();
            var live = context.Sources.Where(i => Layer(job.Sources[i].Material) == null).ToList();
            if (live.Count > 0)
                context.Notes.Add("Live glitter (animated, audio-driven) isn't merged yet; it's left out (" + AtlasLiveMasks.Numbers(live) + ").");
            if (layers.Count == 0) return;
            if (context.Mode.Kind == AtlasFeatureModeKind.Emulated) Emulate(context, layers);
            else Native(context, layers);
        }

        // ---- Native ----

        private static void Native(AtlasFeatureContext context, List<(int Index, AtlasPatternLayer Layer)> layers)
        {
            var job = context.Job;
            var choice = job.Features.Find(f => f.FeatureId == FeatureId);
            var onUV = layers.Where(s => OnUV(s.Layer)).ToList();
            var offUV = layers.Where(s => !OnUV(s.Layer)).Select(s => s.Index).ToList();
            if (offUV.Count > 0)
                context.Notes.Add("Glitter in a space other than a UV channel isn't merged yet; it's left out (" + AtlasLiveMasks.Numbers(offUV) + ").");
            if (onUV.Count == 0) return;
            var reference = onUV.Any(s => choice != null && s.Index == choice.ReferenceSource)
                ? onUV.First(s => s.Index == choice.ReferenceSource)
                : onUV.OrderByDescending(s => job.Sources[s.Index].UVRegion.width * job.Sources[s.Index].UVRegion.height).ThenBy(s => s.Index).First();
            string setup = Setup(reference.Layer);
            var kept = onUV.Where(s => Setup(s.Layer) == setup).ToList();
            var differ = onUV.Where(s => Setup(s.Layer) != setup).Select(s => s.Index).ToList();
            if (differ.Count > 0)
                context.Notes.Add($"The glitter is taken from source {reference.Index + 1}; the glitter of " + AtlasLiveMasks.Numbers(differ) +
                                  " differs and is left out (Glitter from picks the one kept).");

            // Each source's cells at its own size: UV × its density ÷ the output's.
            float density = reference.Layer.Glitter.Density;
            string key = "glitter;" + string.Join(";", kept.Select(s => s.Index + ":" + s.Layer.Glitter.Space + ":" + s.Layer.Glitter.Density / density));
            if (!context.Plan.DetailChannels.TryGetValue(key, out int channel))
            {
                channel = context.Plan.AllocateUVChannel(context.Capabilities.UVChannels);
                if (channel < 0)
                {
                    context.Notes.Add("No UV channel is free for the glitter; it's left out.");
                    return;
                }
                context.Plan.DetailChannels[key] = channel;
                foreach (var s in kept)
                {
                    if (!context.Plan.SourceRemaps.TryGetValue(s.Index, out var remap))
                        context.Plan.SourceRemaps[s.Index] = remap = new AtlasSourceRemap();
                    float scale = s.Layer.Glitter.Density / density;
                    remap.PreservedUVs.Add(new AtlasPreservedUV
                    {
                        Channel = channel, SourceChannel = s.Layer.Glitter.Space - AtlasTextureSpace.UV0, Convert = uv => uv * scale
                    });
                }
            }

            float strongest = kept.Max(s => s.Layer.Opacity);
            if (strongest <= 0f) return;
            var keptSet = kept.ToDictionary(s => s.Index, s => s.Layer);
            var allocated = AtlasMaskAllocator.Allocate(context, new AtlasMaskRequest
            {
                Name = "glitter", CanInvert = true,
                Key = i => keptSet.TryGetValue(i, out var l) ? AtlasMaskKeys.Of(l.Mask, l.Opacity / strongest, job.Sources[i].Material) : AtlasMaskKeys.Constant(0f),
                InverseKey = i => keptSet.TryGetValue(i, out var l) ? AtlasMaskKeys.Inverse(l.Mask, l.Opacity / strongest, job.Sources[i].Material) : AtlasMaskKeys.Constant(1f),
                Evaluate = (i, material, w, h) => keptSet.TryGetValue(i, out var l)
                    ? AtlasMapEvaluator.ScaledMask(l.Mask, l.Opacity / strongest, w, h, material)
                    : AtlasMapEvaluator.Constant(AtlasVirtualMap.Mask1, 0f, w, h)
            });
            if (allocated == null)
            {
                context.Notes.Add("No mask place is left for the glitter; it's left out.");
                return;
            }
            if (allocated.IsConstant && allocated.Constant <= 0f) return;

            var output = new AtlasPatternLayer();
            AtlasFieldwise.Copy(reference.Layer, output);
            output.Glitter = new AtlasGlitter();
            AtlasFieldwise.Copy(reference.Layer.Glitter, output.Glitter);
            output.Glitter.Space = AtlasTextureSpace.UV0 + channel;
            output.Glitter.Density = density;
            output.Mask = allocated.Mask;
            output.Opacity = strongest * (allocated.IsConstant ? allocated.Constant : 1f);
            if (output.Glitter.ColorMap != null)
            {
                output.Glitter.ColorMap = null;
                context.Notes.Add("The glitter's color map isn't baked yet; it's left out.");
            }
            context.Output.Layers.Add(output);
            AtlasHueCycling.CopyLayerHue(context.Output, job.Sources[reference.Index].Material, reference.Layer, output);
            AtlasAudio.CopyLayerAudio(context.Output, job.Sources[reference.Index].Material, reference.Layer, output);
        }

        // ---- Emulated ----

        private static float Option(AtlasFeatureContext context, string id)
        {
            var choice = context.Job.Features.Find(f => f.FeatureId == FeatureId);
            float fallback = OptionList.First(o => o.Id == id).Default;
            return Mathf.Clamp01(choice != null && choice.Values.TryGetValue(AtlasFeatureOption.ValueId(id), out var value) ? value.Number : fallback);
        }

        private static void Emulate(AtlasFeatureContext context, List<(int Index, AtlasPatternLayer Layer)> layers)
        {
            var job = context.Job;
            var drawn = layers.Where(s => s.Layer.Glitter.Space == AtlasTextureSpace.UV0 && s.Layer.Glitter.Scroll == Vector2.zero).ToDictionary(s => s.Index, s => s.Layer);
            var skipped = layers.Where(s => !drawn.ContainsKey(s.Index)).Select(s => s.Index).ToList();
            if (skipped.Count > 0)
                context.Notes.Add("Glitter that moves or isn't on UV0 can't be drawn into the maps; it's left out (" + AtlasLiveMasks.Numbers(skipped) + ").");
            if (drawn.Count == 0) return;
            float glow = Option(context, EmissionOption), shine = Option(context, GlossOption), tint = Option(context, ColorOption);
            var capabilities = context.Capabilities;
            var output = context.Output;
            var targets = new List<string>();

            NordicsTextureWrapper Points(int i, AtlasUnifiedMaterial material, int w, int h)
            {
                var layer = drawn[i];
                return AtlasEvaluationCache.Copy(("glitter points", material, layer, w, h), () =>
                {
                    var points = AtlasGlitterPattern.Draw(layer, w, h);
                    using (var mask = AtlasMapEvaluator.ScaledMask(layer.Mask, Mathf.Clamp01(layer.Opacity), w, h, material))
                        AtlasPixels.Map(points, mask, (p, m) => new Color(p.r, p.g, p.b, p.a * m.r));
                    return points;
                });
            }

            if (glow > 0f && capabilities.Provides(AtlasVirtualMap.Emission))
            {
                float peak = drawn.Values.Max(l => l.Glitter.MaxBrightness * l.Color.maxColorComponent) * glow;
                var request = AtlasLiveMasks.Request(context, AtlasVirtualMap.Emission);
                var emission = AtlasEmissionFeature.Of(output);
                if (request == null || emission == null)
                {
                    request = new AtlasMapRequest
                    {
                        Map = AtlasVirtualMap.Emission,
                        Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Color },
                        Content = (i, m, w, h) => AtlasMapEvaluator.Constant(AtlasVirtualMap.Emission, 0f, w, h)
                    };
                    for (int i = 0; i < job.Sources.Count; i++) request.Sources.Add(i);
                    context.Maps.Add(request);
                    // The glitter's own emission glows at its brightness, so its points fill
                    // the map's range.
                    emission = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
                    emission.Source.Map = request.Reference;
                    emission.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, peak));
                    output.Layers.Add(emission);
                    request.SharedColor = new Color(peak, peak, peak, 1f);
                }
                var target = emission.Targets.First(t => t.Target == AtlasSurfaceTarget.Emission);
                if (request.Carry == null) AtlasEmissionFeature.CarryInStrength(request, target);
                // The map gives its value in full (× the emission's strength): the points are
                // added where they are drawn, and what goes above the map's range goes into
                // the strength.
                request.Add(new AtlasMapContribution
                {
                    Feature = FeatureId, Label = "glitter glow", Stage = AtlasMapStage.Overlays, CanRaise = true,
                    Applies = drawn.ContainsKey,
                    Apply = content =>
                    {
                        using (var points = Points(content.Source, content.Material, content.Width, content.Height))
                            AtlasPixels.Map(content.Map, points, (c, p) =>
                            {
                                float k = p.a * glow;
                                return new Color(c.r + p.r * k, c.g + p.g * k, c.b + p.b * k, c.a);
                            });
                    }
                });
                targets.Add("glow");
            }

            if (shine > 0f && capabilities.Provides(AtlasVirtualMap.Roughness))
            {
                var response = output.Surface.Response;
                if (AtlasLiveMasks.Request(context, AtlasVirtualMap.Roughness) == null)
                {
                    var none = new HashSet<int>();
                    response.Metallic.Constant = 1f;
                    response.Metallic.Map = AtlasSpecularFeature.Request(context, AtlasVirtualMap.Metallic, none, 0f, null);
                    response.Roughness.Constant = 1f;
                    response.Roughness.Map = AtlasSpecularFeature.Request(context, AtlasVirtualMap.Roughness, none, 1f, null);
                }
                response.Highlights.Enabled = response.Reflections.Enabled = true;
                if (response.Highlights.Strength <= 0f) response.Highlights.Strength = 1f;
                if (response.Reflections.Strength <= 0f) response.Reflections.Strength = 1f;
                Wrap(AtlasLiveMasks.Request(context, AtlasVirtualMap.Metallic), (c, p) => Mathf.Lerp(c, 1f, p.a * shine));
                Wrap(AtlasLiveMasks.Request(context, AtlasVirtualMap.Roughness), (c, p) => Mathf.Lerp(c, 0f, p.a * shine));
                targets.Add("shine");
            }

            if (tint > 0f && capabilities.Provides(AtlasVirtualMap.BaseColor))
            {
                var request = AtlasLiveMasks.Request(context, AtlasVirtualMap.BaseColor);
                if (request != null)
                {
                    request.Add(new AtlasMapContribution
                    {
                        Feature = FeatureId, Label = "glitter color", Stage = AtlasMapStage.Overlays, Applies = drawn.ContainsKey,
                        Apply = content =>
                        {
                            var color = drawn[content.Source].Color;
                            using (var points = Points(content.Source, content.Material, content.Width, content.Height))
                                AtlasPixels.Map(content.Map, points, (c, p) => Color.Lerp(c, new Color(color.r, color.g, color.b, c.a), p.a * tint));
                        }
                    });
                    targets.Add("color");
                }
            }

            void Wrap(AtlasMapRequest request, System.Func<float, Color, float> apply)
            {
                if (request == null) return;
                request.Add(new AtlasMapContribution
                {
                    Feature = FeatureId, Label = "glitter shine", Stage = AtlasMapStage.Overlays, Applies = drawn.ContainsKey,
                    Apply = content =>
                    {
                        using (var points = Points(content.Source, content.Material, content.Width, content.Height))
                            AtlasPixels.Map(content.Map, points, (c, p) =>
                            {
                                float v = apply(c.r, p);
                                return new Color(v, v, v, c.a);
                            });
                    }
                });
            }

            if (targets.Count > 0)
                context.Notes.Add("The output shader has no glitter; it's drawn as still points (" + string.Join(", ", targets) +
                                  "): they don't twinkle, and the glitter's texture, color map and rotation aren't drawn.");
        }
    }

    // The glitter's cells drawn as still points over a source's UV 0–1 (AtlasGlitter): each
    // cell's particle at its corner + a random point × RandomPosition, at its size (random in
    // SizeRange with RandomSize), the nearest one drawn (every one with Overlap), anti-aliased,
    // in every layer of cells. RGB: the particle's color × a frozen brightness between
    // MinBrightness and MaxBrightness (× a random color with RandomColors), A: its coverage.
    public static class AtlasGlitterPattern
    {
        public static NordicsTextureWrapper Draw(AtlasPatternLayer layer, int width, int height)
        {
            var glitter = layer.Glitter;
            var pixels = new Color[width * height];
            float density = Mathf.Max(1f, glitter.Density);
            // A pixel's size in cells.
            float pixel = density / Mathf.Max(1, Mathf.Min(width, height));
            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width * density, v = (y + 0.5f) / height * density;
                    var best = new Color(0f, 0f, 0f, 0f);
                    for (int l = 0; l < Mathf.Clamp(glitter.Layers, 1, 4); l++)
                    {
                        float nearest = float.MaxValue, coverage = 0f;
                        Color color = Color.black;
                        int cx = Mathf.FloorToInt(u), cy = Mathf.FloorToInt(v);
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int px = cx + dx, py = cy + dy;
                                float ox = Hash(px, py, l, 0) * glitter.RandomPosition, oy = Hash(px, py, l, 1) * glitter.RandomPosition;
                                float size = glitter.RandomSize ? Mathf.Lerp(glitter.SizeRange.x, glitter.SizeRange.y, Hash(px, py, l, 2)) : glitter.Size;
                                float ddx = u - (px + ox), ddy = v - (py + oy);
                                float distance = glitter.Shape == AtlasGlitterShape.Square ? Mathf.Max(Mathf.Abs(ddx), Mathf.Abs(ddy)) : Mathf.Sqrt(ddx * ddx + ddy * ddy);
                                float c = Mathf.Clamp01((size - distance) / Mathf.Max(pixel, 1e-4f) + 0.5f);
                                bool take = glitter.Overlap ? c > coverage : distance < nearest;
                                if (!take) continue;
                                nearest = distance;
                                coverage = c;
                                float brightness = Mathf.Lerp(glitter.MinBrightness, glitter.MaxBrightness, Hash(px, py, l, 3));
                                var tint = layer.Color;
                                if (glitter.RandomColors)
                                    tint *= Color.HSVToRGB(Hash(px, py, l, 4),
                                        Mathf.Lerp(glitter.SaturationRange.x, glitter.SaturationRange.y, Hash(px, py, l, 5)),
                                        Mathf.Lerp(glitter.BrightnessRange.x, glitter.BrightnessRange.y, Hash(px, py, l, 6)));
                                color = tint * brightness;
                            }
                        if (coverage > best.a) best = new Color(color.r, color.g, color.b, coverage);
                    }
                    pixels[y * width + x] = best;
                }
            });
            var result = new NordicsTextureWrapper(width, height, new TextureSettings { DataType = TextureDataType.LinearData, SRGB = false });
            result.SetPixels(pixels);
            return result;
        }

        // A stable random value in [0, 1) for a cell, layer and purpose.
        private static float Hash(int x, int y, int layer, int salt)
        {
            unchecked
            {
                uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)(layer * 31 + salt) * 0xcb1ab31fu;
                h ^= h >> 16;
                h *= 0x7feb352du;
                h ^= h >> 15;
                h *= 0x846ca68bu;
                h ^= h >> 16;
                return (h & 0xffffff) / 16777216f;
            }
        }
    }
}
