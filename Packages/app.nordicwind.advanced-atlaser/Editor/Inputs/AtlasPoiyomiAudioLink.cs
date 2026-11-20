using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: AudioLink, as audio modulators on the features it drives. Every one keeps its target
    // as is without AudioLink in the world, as the shader does.
    internal sealed partial class AtlasPoiyomiReader
    {
        // The section's toggle (the shader keyword) and its animation toggle.
        private bool AudioLinkOn(Material m) => On(m, "_EnableAudioLink") && On(m, "_AudioLinkAnimToggle", 1f);

        private static readonly string[] SmoothingProperties =
            { "_AudioLinkSmoothingBass", "_AudioLinkSmoothingLowMid", "_AudioLinkSmoothingHighMid", "_AudioLinkSmoothingTreble" };

        private AtlasModulator AudioModulator(Material m, string target, AtlasModulatorMode mode, int band, float min, float max)
        {
            var modulator = new AtlasModulator { Target = target, Source = AtlasModulatorSource.Audio, Mode = mode, Min = min, Max = max };
            band = Mathf.Clamp(band, 0, 4);
            modulator.Audio.Band = (AtlasAudioBand)band;
            // Volume isn't smoothed.
            modulator.Audio.Smoothing = band < 4 ? Float(m, SmoothingProperties[band], 0f) : 0f;
            modulator.Audio.IdleWithoutAudioLink = true;
            return modulator;
        }

        // target mode lerp(range.x, range.y, band); nothing when the range leaves the target as is.
        private void AudioRange(Material m, AtlasUnifiedMaterial d, string target, AtlasModulatorMode mode, string band, string range)
        {
            var values = AtlasMaterialReader.Vector(m, range, Vector4.zero);
            float neutral = mode == AtlasModulatorMode.Multiply ? 1f : 0f;
            if (values.x == neutral && values.y == neutral) return;
            d.Modulators.Add(AudioModulator(m, target, mode, Int(m, band, 0), values.x, values.y));
        }

        // The color becomes lerp(color, override, lerp(ramp.x, ramp.y, smoothstep(ramp.z, ramp.w, band))).
        // The properties are prefix + Enabled, Band, ThemeIndex and Ramp, each + suffix.
        private void AudioColorOverride(Material m, AtlasUnifiedMaterial d, string target, string prefix, string suffix = "")
        {
            if (!On(m, prefix + "Enabled" + suffix)) return;
            var modulator = AudioModulator(m, target, AtlasModulatorMode.BlendToColor, Int(m, prefix + "Band" + suffix, 0), 0f, 1f);
            modulator.Color = TintColor(m, d, prefix + suffix, prefix + "ThemeIndex" + suffix);
            modulator.Curve = AtlasMaterialReader.Vector(m, prefix + "Ramp" + suffix, new Vector4(0f, 1f, 0f, 1f));
            d.Modulators.Add(modulator);
        }

        // AudioLink's chronotensity × speed added to the target.
        private void AudioChrono(Material m, AtlasUnifiedMaterial d, string target, string band, string motion, float speed)
        {
            if (speed == 0f) return;
            var modulator = AudioModulator(m, target, AtlasModulatorMode.Add, Int(m, band, 0), 0f, speed);
            modulator.Audio.Spread = AtlasAudioSpread.Chrono;
            modulator.Audio.ChronoMotion = Mathf.Clamp(Int(m, motion, 0), 0, 7);
            d.Modulators.Add(modulator);
        }

        // Strength: + lerp(add) then × lerp(multipliers); center out: + lerp(add, the band's history
        // along the view angle); the color override.
        private void EmissionAudioLink(Material m, AtlasUnifiedMaterial d, int index, int layer)
        {
            string n = index.ToString();
            if (!AudioLinkOn(m) || !On(m, "_EmissionAL" + n + "Enabled")) return;
            string strength = "layers[" + layer + "].targets[0].opacity";
            AudioRange(m, d, strength, AtlasModulatorMode.Add, "_EmissionAL" + n + "StrengthBand", "_EmissionAL" + n + "StrengthMod");
            AudioRange(m, d, strength, AtlasModulatorMode.Multiply, "_EmissionAL" + n + "MultipliersBand", "_EmissionAL" + n + "Multipliers");
            var centerOut = AtlasMaterialReader.Vector(m, "_AudioLinkEmission" + n + "CenterOut", Vector4.zero);
            if (centerOut.x != 0f || centerOut.y != 0f)
            {
                float duration = Float(m, "_AudioLinkEmission" + n + "CenterOutDuration", 1f);
                var history = AudioModulator(m, strength, AtlasModulatorMode.Add, Int(m, "_AudioLinkEmission" + n + "CenterOutBand", 0), centerOut.x, centerOut.y);
                history.Audio.Spread = AtlasAudioSpread.History;
                history.Audio.Coordinate = AtlasSpreadCoordinate.ViewAngle;
                history.Audio.HistoryLength = Mathf.Abs(duration) * 128f;
                history.Audio.Threshold = Mathf.Min(Float(m, "_AudioLinkEmission" + n + "CenterOutSize", 0f), 0.9999f);
                d.Modulators.Add(history);
                if (duration < 0f)
                    d.Report(AtlasIssueKind.Unmapped, "audiolink.center-out", "A reversed AudioLink center-out emission isn't mapped yet.");
            }
            AudioColorOverride(m, d, "layers[" + layer + "].source.color", "_EmissionAL" + n + "ColorOverride");
        }

        // Scale, rotation, alpha and emission by bands, rotation by chronotensity, the color override.
        private void DecalAudioLink(Material m, AtlasUnifiedMaterial d, int index, int layer)
        {
            if (!AudioLinkOn(m)) return;
            string n = index.ToString(), p = "_AudioLinkDecal" + n, decal = "layers[" + layer + "].source.map.decal.";
            var scale = AtlasMaterialReader.Vector(m, p + "Scale", Vector4.zero);
            int scaleBand = Int(m, p + "ScaleBand", 0);
            if (scale.x != 0f || scale.z != 0f) d.Modulators.Add(AudioModulator(m, decal + "scale.x", AtlasModulatorMode.Add, scaleBand, scale.x, scale.z));
            if (scale.y != 0f || scale.w != 0f) d.Modulators.Add(AudioModulator(m, decal + "scale.y", AtlasModulatorMode.Add, scaleBand, scale.y, scale.w));
            AudioRange(m, d, decal + "rotation", AtlasModulatorMode.Add, p + "RotationBand", p + "Rotation");
            AudioChrono(m, d, decal + "rotation", "_DecalRotationCTALBand" + n, "_DecalRotationCTALType" + n, Float(m, "_DecalRotationCTALSpeed" + n, 0f) * 360f);
            // Poiyomi adds to the blend alpha inside its saturate; here it adds to the opacity.
            AudioRange(m, d, "layers[" + layer + "].targets[0].opacity", AtlasModulatorMode.Add, p + "AlphaBand", p + "Alpha");
            AudioRange(m, d, "layers[" + layer + "].emissionAmount", AtlasModulatorMode.Add, p + "EmissionBand", p + "Emission");
            AudioColorOverride(m, d, "layers[" + layer + "].source.color", p + "ColorOverride");
            if (AtlasMaterialReader.Vector(m, p + "SideMin", Vector4.zero) != Vector4.zero || AtlasMaterialReader.Vector(m, p + "SideMax", Vector4.zero) != Vector4.zero)
                d.Report(AtlasIssueKind.Unmapped, "audiolink.decal-sides", "A decal's side offsets by AudioLink aren't mapped yet.");
            if (On(m, "_AudioLinkDecalCC" + n))
                d.Report(AtlasIssueKind.Unmapped, "audiolink.colorchord-strip", "A decal colored by the ColorChord strip isn't mapped yet.");
        }

        // Alpha, emission and intensity by bands, the color override, on every layer of the matcap.
        private void MatcapAudioLink(Material m, AtlasUnifiedMaterial d, int index, string prefix, int from)
        {
            string p = "_Matcap" + index + "AL";
            if (!AudioLinkOn(m) || !On(m, p + "Enabled")) return;
            float intensity = Float(m, prefix + "Intensity", 1f);
            for (int i = from; i < d.Layers.Count; i++)
            {
                string layer = "layers[" + i + "]";
                AudioRange(m, d, layer + ".source.color.a", AtlasModulatorMode.Add, p + "AlphaAddBand", p + "AlphaAdd");
                if (i == from) AudioRange(m, d, layer + ".emissionAmount", AtlasModulatorMode.Add, p + "EmissionAddBand", p + "EmissionAdd");
                // The color × (intensity + add): × (1 + add / intensity).
                var add = AtlasMaterialReader.Vector(m, p + "IntensityAdd", Vector4.zero);
                if ((add.x != 0f || add.y != 0f) && intensity != 0f)
                    d.Modulators.Add(AudioModulator(m, layer + ".source.color", AtlasModulatorMode.Multiply, Int(m, p + "IntensityAddBand", 0),
                        1f + add.x / intensity, 1f + add.y / intensity));
                AudioColorOverride(m, d, layer + ".source.color", p + "ColorOverride");
            }
            if (Float(m, p + "ChronoPanSpeed", 0f) != 0f)
                d.Report(AtlasIssueKind.Unmapped, "audiolink.matcap-pan", "A matcap's AudioLink chrono pan isn't mapped yet.");
        }

        // Width, emission and brightness by bands, the color override (Poiyomi style only).
        private void RimAudioLink(Material m, AtlasUnifiedMaterial d, string p, int layer, float brightness)
        {
            if (!AudioLinkOn(m)) return;
            string rim = "layers[" + layer + "]";
            // The width shifts the step's high threshold and, with it, the low one.
            AudioRange(m, d, rim + ".high", AtlasModulatorMode.Add, p + "WidthBand", p + "WidthAdd");
            AudioRange(m, d, rim + ".low", AtlasModulatorMode.Add, p + "WidthBand", p + "WidthAdd");
            AudioRange(m, d, rim + ".emissionAmount", AtlasModulatorMode.Add, p + "EmissionBand", p + "EmissionAdd");
            var add = AtlasMaterialReader.Vector(m, p + "BrightnessAdd", Vector4.zero);
            if ((add.x != 0f || add.y != 0f) && brightness != 0f)
                d.Modulators.Add(AudioModulator(m, rim + ".color.color", AtlasModulatorMode.Multiply, Int(m, p + "BrightnessBand", 0),
                    1f + add.x / brightness, 1f + add.y / brightness));
            AudioColorOverride(m, d, rim + ".color.color", p + "ColorOverride");
        }

        private void ReportAudioLinkGlobals(Material m, AtlasUnifiedMaterial d)
        {
            if (AudioLinkOn(m) && On(m, "_AudioLinkBandOverridesEnabled"))
                d.Report(AtlasIssueKind.Unmapped, "audiolink.band-overrides", "AudioLink's band overrides aren't mapped yet.");
        }
    }
}
