using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // AudioLink (ATLAS_OUTPUT.md, "Feature inventory"): audio modulators on the parts they drive.
    // A modulator that leaves its target as it is without AudioLink in the world (idle, as
    // Poiyomi's are) is baked at rest, the part as it's set; the slots that keep a layer of their
    // own (matcaps, rims, glitter, emission slots) copy its modulators live.
    public static class AtlasAudio
    {
        public static bool IsAudio(AtlasModulator modulator) => modulator.Source == AtlasModulatorSource.Audio;

        // An audio modulator that leaves its target as it is without music: the bakes take the
        // part as it's set.
        public static bool AtRest(AtlasModulator modulator) => IsAudio(modulator) && modulator.Audio.IdleWithoutAudioLink;

        private static string Path(int index) => "layers[" + index + "]";

        private static bool On(AtlasModulator modulator, string path) =>
            modulator.Target != null && (modulator.Target == path || modulator.Target.StartsWith(path + ".") || modulator.Target.StartsWith(path + "["));

        /// <summary>The audio modulators on a layer of the material.</summary>
        public static List<AtlasModulator> Of(AtlasUnifiedMaterial material, AtlasLayer layer)
        {
            if (material == null || layer == null) return new List<AtlasModulator>();
            int index = material.Layers.IndexOf(layer);
            if (index < 0) return new List<AtlasModulator>();
            string path = Path(index);
            return material.Modulators.Where(m => IsAudio(m) && On(m, path)).ToList();
        }

        /// <summary>What the layer's audio modulators do, apart from which layer they're on.</summary>
        public static string Signature(AtlasUnifiedMaterial material, AtlasLayer layer)
        {
            int index = material.Layers.IndexOf(layer);
            string path = Path(index);
            return string.Join("|", Of(material, layer).Select(m => m.Target.Substring(path.Length) + AtlasFieldwise.Signature(m, "Target")));
        }

        /// <summary>Copies a source layer's audio modulators onto its output copy (already in the
        /// output's layers).</summary>
        public static void CopyLayerAudio(AtlasUnifiedMaterial output, AtlasUnifiedMaterial source, AtlasLayer from, AtlasLayer to)
        {
            if (source == null) return;
            int fromIndex = source.Layers.IndexOf(from), toIndex = output.Layers.IndexOf(to);
            if (fromIndex < 0 || toIndex < 0) return;
            string fromPath = Path(fromIndex), toPath = Path(toIndex);
            foreach (var modulator in Of(source, from))
            {
                var copy = new AtlasModulator();
                AtlasFieldwise.Copy(modulator, copy);
                copy.Target = toPath + modulator.Target.Substring(fromPath.Length);
                copy.Mask = null;
                output.Modulators.Add(copy);
            }
        }

        public static bool HasAudio(AtlasUnifiedMaterial output, AtlasLayer layer) => Of(output, layer).Count > 0;

        /// <summary>The output's audio modulator on a target path (a layer's path + its part),
        /// by mode and spread.</summary>
        public static AtlasModulator Find(AtlasUnifiedMaterial output, AtlasLayer layer, string part, AtlasModulatorMode mode,
            AtlasAudioSpread spread = AtlasAudioSpread.None)
        {
            int index = output.Layers.IndexOf(layer);
            if (index < 0) return null;
            string target = Path(index) + part;
            return output.Modulators.FirstOrDefault(m => IsAudio(m) && m.Target == target && m.Mode == mode && m.Audio.Spread == spread);
        }
    }

    // AudioLink as its own section (Toon Standard's: one audio-reactive emission): the
    // reference source's audio-reactive emission (AudioLink from), each kept source's emission
    // × its audio gain baked into the AudioLink color map; the others stay at rest. On outputs
    // whose sections each have AudioLink (Poiyomi), the slots keep theirs, and what has no slot
    // stays at rest, listed.
    public sealed class AtlasAudioLinkFeature : IAtlasSharedValueFeature
    {
        public const string FeatureId = "audiolink";

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode { Id = "sections", Label = "Per feature", Kind = AtlasFeatureModeKind.Native, Requires = c => c.AudioLinkPerSection },
            new AtlasFeatureMode
            {
                Id = "emission", Label = "AudioLink emission", Kind = AtlasFeatureModeKind.Native, RequiredMaps = new[] { AtlasVirtualMap.AudioLinkColor },
                Requires = c => !c.AudioLinkPerSection
            },
            AtlasFeatureMode.Off
        };

        public string Id => FeatureId;
        public string DisplayName => "AudioLink";
        public AtlasFeatureSection Section => AtlasFeatureSection.Effects;
        public int Order => 4;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;
        public string SharedValueName => "AudioLink";

        public bool IsPresent(AtlasUnifiedMaterial source) => source != null && source.Modulators.Any(AtlasAudio.IsAudio);

        // The source's first emission layer an audio modulator drives, with that modulator and how
        // bright the music makes it at most (× the emission at unit strength): the strength × the
        // top of a modulator driving its color; or (its strength + the top added) × the top of
        // its multipliers, for modulators driving its strength.
        private static (AtlasSurfaceLayer Layer, AtlasModulator Modulator, float Gain)? Emission(AtlasUnifiedMaterial material)
        {
            if (material == null) return null;
            foreach (var layer in material.Layers.OfType<AtlasSurfaceLayer>().Where(l => l.Enabled && l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission)))
            {
                var audio = AtlasAudio.Of(material, layer);
                float strength = layer.Targets.First(t => t.Target == AtlasSurfaceTarget.Emission).Opacity;
                var color = audio.FirstOrDefault(m => m.Target.EndsWith(".source.color") && m.Mode == AtlasModulatorMode.Multiply);
                if (color != null) return (layer, color, strength * color.Max);
                var add = audio.FirstOrDefault(m => m.Target.EndsWith(".targets[0].opacity") && m.Mode == AtlasModulatorMode.Add && m.Audio.Spread == AtlasAudioSpread.None);
                var multiply = audio.FirstOrDefault(m => m.Target.EndsWith(".targets[0].opacity") && m.Mode == AtlasModulatorMode.Multiply);
                if (add == null && multiply == null) continue;
                float gain = (strength + (add?.Max ?? 0f)) * (multiply?.Max ?? 1f);
                if (gain > 0f) return (layer, add ?? multiply, gain);
            }
            return null;
        }

        private static string Setup(AtlasSurfaceLayer layer, AtlasModulator modulator) =>
            AtlasFieldwise.Signature(modulator.Audio) + "|" + layer.Targets.First(t => t.Target == AtlasSurfaceTarget.Emission).Blend;

        public bool SharedValuesDiffer(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputCapabilities capabilities) =>
            !capabilities.AudioLinkPerSection &&
            sources.Select(Emission).Where(e => e.HasValue).Select(e => Setup(e.Value.Layer, e.Value.Modulator)).Distinct().Count() > 1;

        public void Resolve(AtlasFeatureContext context)
        {
            if (context.Capabilities.AudioLinkPerSection) Sections(context);
            else EmissionSection(context);
        }

        // Per-section outputs: what has no slot of its own stays at rest.
        private static void Sections(AtlasFeatureContext context)
        {
            var job = context.Job;
            var rest = new List<int>();
            foreach (int i in context.Sources)
            {
                var material = job.Sources[i].Material;
                foreach (var modulator in material.Modulators.Where(AtlasAudio.IsAudio))
                {
                    var layer = LayerOf(material, modulator.Target);
                    bool slot = layer is AtlasLookupLayer || layer is AtlasAngleLayer ||
                                (layer is AtlasPatternLayer pattern && pattern.Kind == AtlasPatternKind.Glitter) ||
                                (layer is AtlasSurfaceLayer surface && surface.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission));
                    if (!slot || !AtlasAudio.AtRest(modulator)) { rest.Add(i); break; }
                }
            }
            if (rest.Count > 0)
                context.Notes.Add("AudioLink on parts without a slot of their own (decals, patterns, colors, alpha) isn't kept; they're shown at rest (" +
                                  AtlasLiveMasks.Numbers(rest) + ").");
        }

        private static AtlasLayer LayerOf(AtlasUnifiedMaterial material, string target)
        {
            if (target == null || !target.StartsWith("layers[")) return null;
            int end = target.IndexOf(']');
            return int.TryParse(target.Substring(7, end - 7), out int index) && index >= 0 && index < material.Layers.Count ? material.Layers[index] : null;
        }

        // One AudioLink emission: the reference source's, each kept source's emission × gain in
        // the AudioLink color map, normalized to its brightest value.
        private static void EmissionSection(AtlasFeatureContext context)
        {
            var job = context.Job;
            var found = context.Sources.Select(i => (Index: i, Found: Emission(job.Sources[i].Material))).Where(f => f.Found.HasValue)
                .ToDictionary(f => f.Index, f => f.Found.Value);
            var others = context.Sources.Where(i => !found.ContainsKey(i)).ToList();
            if (others.Count > 0)
                context.Notes.Add("The output shader's AudioLink drives one emission; AudioLink on other parts isn't kept (" + AtlasLiveMasks.Numbers(others) + ").");
            if (found.Count == 0) return;
            var choice = job.Features.Find(f => f.FeatureId == FeatureId);
            int reference = choice != null && found.ContainsKey(choice.ReferenceSource) ? choice.ReferenceSource
                : found.Keys.OrderByDescending(i => job.Sources[i].UVRegion.width * job.Sources[i].UVRegion.height).ThenBy(i => i).First();
            var template = found[reference];
            string setup = Setup(template.Layer, template.Modulator);
            var kept = found.Where(f => Setup(f.Value.Layer, f.Value.Modulator) == setup).ToDictionary(f => f.Key, f => f.Value);
            var differ = found.Keys.Where(i => !kept.ContainsKey(i)).ToList();
            if (differ.Count > 0)
                context.Notes.Add($"AudioLink is taken from source {reference + 1}; the AudioLink of " + AtlasLiveMasks.Numbers(differ) + " differs and isn't kept.");

            var output = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
            var request = new AtlasMapRequest
            {
                Map = AtlasVirtualMap.AudioLinkColor,
                Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Color },
                Evaluate = (i, material, w, h) =>
                {
                    if (!kept.TryGetValue(i, out var entry)) return AtlasMapEvaluator.Constant(AtlasVirtualMap.AudioLinkColor, 0f, w, h);
                    // The emission at unit strength × how bright the music makes it.
                    float gain = entry.Gain;
                    var result = AtlasColorStack.EvaluateEmission(material, w, h, 1f, new HashSet<AtlasLayer> { entry.Layer },
                        anyModulators: true, clamp: false, unitStrength: true);
                    AtlasPixels.Map(result, c => new Color(c.r * gain, c.g * gain, c.b * gain, 1f));
                    return result;
                },
                Finish = map =>
                {
                    float peak = AtlasColorFeature.Peak(map);
                    output.Source.Color = peak > 1f ? new Color(peak, peak, peak, 1f) : Color.white;
                    if (peak <= 1f) return;
                    map.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Tint, Parameters = { Color = new Color(1f / peak, 1f / peak, 1f / peak, 1f) } });
                }
            };
            for (int i = 0; i < job.Sources.Count; i++) request.Sources.Add(i);
            context.Maps.Add(request);
            output.Source.Map = request.Reference;
            output.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, template.Layer.Targets.First(t => t.Target == AtlasSurfaceTarget.Emission).Blend, 1f));
            context.Output.Layers.Add(output);
            var modulator = new AtlasModulator();
            AtlasFieldwise.Copy(template.Modulator, modulator);
            modulator.Target = "layers[" + (context.Output.Layers.Count - 1) + "].source.color";
            modulator.Mode = AtlasModulatorMode.Multiply;
            modulator.Min = 0f;
            modulator.Max = 1f;
            modulator.Mask = null;
            context.Output.Modulators.Add(modulator);
            if (template.Modulator.Audio.Spread != AtlasAudioSpread.None)
                context.Notes.Add("AudioLink's scroll and bar run along the atlas's UVs, so they cross each part differently than before.");
        }

        /// <summary>The output's AudioLink emission (its own section), if any.</summary>
        public static AtlasSurfaceLayer Of(AtlasUnifiedMaterial output) =>
            output.Layers.OfType<AtlasSurfaceLayer>().FirstOrDefault(l => l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission) &&
                AtlasAudio.Find(output, l, ".source.color", AtlasModulatorMode.Multiply, AtlasAudio.Of(output, l).FirstOrDefault()?.Audio.Spread ?? AtlasAudioSpread.None) != null &&
                AtlasHueCycling.LayerHue(output, l).Turn == null);
    }
}
