using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: Dissolve, as a transition layer.
    public abstract partial class AtlasPoiyomiParser
    {
        private static void Dissolve(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_EnableDissolve")) return;
            // The amount runs past both ends of the gradient by the padding, so 0 and 1 are fully in and out.
            var layer = new AtlasTransitionLayer
            {
                Amount = Float(m, "_DissolveAlpha", 0f),
                Softness = Float(m, "_DissolveSoftness", 0f),
                Padded = true
            };
            switch (Int(m, "_DissolveType", 1))
            {
                case 2:
                    layer.Source = AtlasTransitionSource.PointToPoint;
                    layer.Space = (AtlasTransitionSpace)Mathf.Clamp(Int(m, "_DissolveP2PWorldLocal", 0), 0, 3);
                    layer.SpaceUV = AtlasTextureSpace.UV0 + Mathf.Clamp(Int(m, "_DissolveP2PUV", 0), 0, 3);
                    layer.Start = Vector3Of(m, "_DissolveStartPoint", new Vector3(0f, -1f, 0f));
                    layer.End = Vector3Of(m, "_DissolveEndPoint", new Vector3(0f, 1f, 0f));
                    break;
                case 3:
                    layer.Source = AtlasTransitionSource.Sphere;
                    layer.Centre = Vector3Of(m, "_SphericalDissolveCenter", Vector3.zero);
                    layer.Radius = Float(m, "_SphericalDissolveRadius", 1.5f);
                    layer.InvertThreshold = On(m, "_SphericalDissolveInvert");
                    break;
                case 4:
                    layer.Source = AtlasTransitionSource.CentreOut;
                    layer.CentreOutMode = (AtlasCentreOutMode)Mathf.Clamp(Int(m, "_CenterOutDissolveMode", 1) - 1, 0, 2);
                    layer.Direction = Vector3Of(m, "_CenterOutDissolveDirection", Vector3.forward);
                    layer.PixelNormalAmount = Float(m, "_CenterOutDissolveNormals", 0f);
                    layer.Linear = On(m, "_CenterOutDissolveLinear", 1f);
                    layer.InvertThreshold = On(m, "_CenterOutDissolveInvert");
                    break;
                default:
                    layer.Threshold = Texture(m, d, "_DissolveNoiseTexture", AtlasTextureInterpretation.Data);
                    layer.InvertThreshold = On(m, "_DissolveInvertNoise");
                    break;
            }

            if (On(m, "_DissolveDetailNoiseEnabled"))
            {
                if (Int(m, "_DissolveDetailNoiseMode", 0) == 0)
                {
                    layer.DetailNoise = Texture(m, d, "_DissolveDetailNoise", AtlasTextureInterpretation.Data);
                    layer.InvertDetailNoise = On(m, "_DissolveInvertDetailNoise");
                }
                else d.Report(AtlasIssueKind.Unmapped, "dissolve.dot-noise", "A dissolve's 3D dot noise isn't mapped yet.");
                layer.DetailStrength = Float(m, "_DissolveDetailStrength", 0.1f);
                layer.DetailEdgeOnly = Float(m, "_DissolveDetailEdgeMask", 0f);
            }
            layer.Wave = On(m, "_DissolveWaveMode");
            layer.WaveWidth = Float(m, "_DissolveWaveWidth", 0.1f);
            layer.WaveBounds = (AtlasWaveBounds)Mathf.Clamp(Int(m, "_DissolveWaveBounds", 0), 0, 2);

            layer.AlphaOnly = On(m, "_DissolveAlphaOnly");
            layer.AlphaCurve = AtlasMaterialReader.Vector(m, "_DissolveAlphaOnlyRamp", new Vector4(1f, 0f, 0f, 1f));
            layer.Revealed.Color = TintColor(m, d, "_DissolveTextureColor", "_DissolveTextureColorThemeIndex", layer.Revealed);
            layer.Revealed.Map = Texture(m, d, "_DissolveToTexture");
            layer.RevealedEmission = Float(m, "_DissolveToEmissionStrength", 0f);
            if (On(m, "_DissolveEdgeEnabled"))
            {
                layer.EdgeWidth = Float(m, "_DissolveEdgeWidth", 0.1f);
                layer.EdgeColor = TintColor(m, d, "_DissolveEdgeColor", "_DissolveEdgeColorThemeIndex");
                layer.EdgeFlip = On(m, "_DissolveEdgeFlip");
                layer.EdgeReplacesColor = true;
                layer.EdgeEmission = Float(m, "_DissolveEdgeEmission", 1f);
                if (On(m, "_DissolveEdgeGradient"))
                    d.Report(AtlasIssueKind.Unmapped, "dissolve.edge-gradient", "A dissolve edge's three-colour gradient isn't mapped yet; its first colour is used.");
                if (On(m, "_DissolveEdgeHueShiftEnabled"))
                    d.Report(AtlasIssueKind.Unmapped, "dissolve.edge-hue", "A dissolve edge's hue shift isn't mapped yet.");
            }

            // The mask (red, or a vertex colour channel) scales the amount.
            int vertex = Int(m, "_DissolveUseVertexColors", 0);
            var mask = vertex > 0 ? new AtlasMask { VertexColor = true, Channel = Channels[Mathf.Clamp(vertex - 1, 0, 3)] } : Mask(m, d, "_DissolveMask");
            mask = BusMask(m, mask, "_DissolveMaskGlobalMask");
            if (mask != null) mask.Invert = On(m, "_DissolveMaskInvert");
            layer.Mask = mask;
            d.Layers.Add(layer);
            int index = d.Layers.Count - 1;
            string path = "layers[" + index + "]";

            LayerHue(m, d, index, "_DissolveHueShift");
            // Poiyomi 11: the revealed colour's gamma, saturation and brightness, after the hue (with it on).
            if (On(m, "_DissolveHueShiftEnabled"))
            {
                float gamma = Float(m, "_DissolveGamma", 1f), saturation = Float(m, "_DissolveSaturation", 0f), brightness = Float(m, "_DissolveBrightness", 0f);
                if (gamma != 1f) LayerAdjust(d, layer, AtlasColorOperation.Power, gamma);
                if (saturation != 0f) LayerAdjust(d, layer, AtlasColorOperation.Saturation, 1f + saturation);
                if (brightness != 0f) LayerAdjust(d, layer, AtlasColorOperation.Brightness, 1f + brightness);
            }
            BusWrite(m, d, layer, "_DissolveApplyGlobalMaskIndex", "_DissolveApplyGlobalMaskBlendType");
            BusWrite(m, d, layer, "_DissolveInverseApplyGlobalMaskIndex", "_DissolveInverseApplyGlobalMaskBlendType", invert: true);

            if (On(m, "_ContinuousDissolveEnabled"))
            {
                // amount = blend(amount, lerp(range.x, range.y, wave(frac(t × speed + phase)))).
                var range = AtlasMaterialReader.Vector(m, "_ContinuousDissolveRange", new Vector4(0f, 1f, 0f, 1f));
                int blend = Int(m, "_ContinuousDissolveBlend", 0);
                var continuous = new AtlasModulator
                {
                    Target = path + ".amount", Source = AtlasModulatorSource.Time, Min = range.x, Max = range.y,
                    Mode = blend == 1 ? AtlasModulatorMode.Add : blend == 2 ? AtlasModulatorMode.Multiply : AtlasModulatorMode.Replace
                };
                float speed = Float(m, "_ContinuousDissolveSpeed", 1f), phase = Float(m, "_ContinuousDissolvePhase", 0f);
                int wave = Int(m, "_ContinuousDissolveWave", 0);
                continuous.Time.Waveform = wave == 1 ? AtlasWaveform.Triangle : wave == 2 ? AtlasWaveform.Saw : wave == 3 ? AtlasWaveform.Pulse : AtlasWaveform.Sine;
                continuous.Time.Frequency = speed;
                // Square is 0 for the first half: a pulse half a cycle later.
                continuous.Time.Offset = speed != 0f ? (phase + (wave == 3 ? 0.5f : 0f)) / speed : 0f;
                d.Modulators.Add(continuous);
            }
            if (On(m, "_UVTileDissolveEnabled"))
                d.Report(AtlasIssueKind.Unmapped, "dissolve.uv-tiles", "Dissolving UV tiles isn't mapped yet.");
            if (On(m, "_DissolveVertexHeightEnabled"))
                d.Report(AtlasIssueKind.Unmapped, "dissolve.vertex-height", "A dissolve's vertex height isn't mapped yet.");

            if (!AudioLinkOn(m) || !On(m, "_EnableDissolveAudioLink")) return;
            AudioRange(m, d, path + ".amount", AtlasModulatorMode.Add, "_AudioLinkDissolveAlphaBand", "_AudioLinkDissolveAlpha");
            if (layer.DetailStrength > 0f)
                AudioRange(m, d, path + ".detailStrength", AtlasModulatorMode.Add, "_AudioLinkDissolveDetailBand", "_AudioLinkDissolveDetail");
            if (On(m, "_DissolveEdgeEnabled"))
            {
                AudioRange(m, d, path + ".edgeWidth", AtlasModulatorMode.Add, "_AudioLinkDissolveEdgeWidthBand", "_AudioLinkDissolveEdgeWidth");
                AudioRange(m, d, path + ".edgeEmission", AtlasModulatorMode.Add, "_AudioLinkDissolveEdgeEmissionBand", "_AudioLinkDissolveEdgeEmission");
                AudioColorOverride(m, d, path + ".edgeColor", "_DissolveALColorOverride");
            }
            if (Float(m, "_AudioLinkDissolveAlphaChronoSpeed", 0f) != 0f || Float(m, "_AudioLinkDissolveHueShiftChronoSpeed", 0f) != 0f)
                d.Report(AtlasIssueKind.Unmapped, "audiolink.dissolve-chrono", "A dissolve's AudioLink chrono amount and hue aren't mapped yet.");
        }
    }
}
