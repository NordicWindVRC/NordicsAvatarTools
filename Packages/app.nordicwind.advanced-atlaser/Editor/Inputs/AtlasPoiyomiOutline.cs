using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: Outlines, with their rendering options, blending, stencil and AudioLink; Pro's Triple
    // Outline adds layers 2 and 3, each its own outline under renamed properties.
    internal sealed partial class AtlasPoiyomiReader
    {
        private void Outline(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_EnableOutlines")) return;
            // Triple Outline's layer toggles; the other variants have only layer 1.
            if (On(m, "_EnableOutlineLayer1", 1f)) OutlineLayer(m, d, 0);
            for (int layer = 2; layer <= 3; layer++)
                if (IsVariant(m, d, "_EnableOutlineLayer" + layer, "Triple Outline") && On(m, "_EnableOutlineLayer" + layer, 1f))
                    OutlineLayer(m, d, layer);
        }

        // A layer's name for a layer 1 property: _Outline… → _Outline2…, _LineWidth → _Outline2Width,
        // _AudioLinkOutline… → _AudioLinkOutline2…. The mask texture, blend factors, depth clip and
        // offsets, thin fade, camera depth and curvature bake are shared and keep their names.
        private string OutlineName(string name, int layer)
        {
            if (layer == 0) return name;
            string k = layer.ToString();
            switch (name)
            {
                case "_LineWidth": return "_Outline" + k + "Width";
                case "_LineColor": return "_Outline" + k + "Color";
                case "_LineColorThemeIndex": return "_Outline" + k + "ColorThemeIndex";
                case "_OutlinesMaxDistance": return "_Outline" + k + "MaxDistance";
                case "_PoiUTSStyleOutlineBlend": case "_Offset_Z": return name + k;
            }
            const string audio = "_AudioLinkOutline";
            if (name.StartsWith(audio)) return audio + k + name.Substring(audio.Length);
            return "_Outline" + k + name.Substring("_Outline".Length);
        }

        private void OutlineLayer(Material m, AtlasUnifiedMaterial d, int layer)
        {
            string N(string name) => OutlineName(name, layer);
            var outline = new AtlasOutline();
            int mode = Int(m, N("_OutlineExpansionMode"), 1);
            outline.Shape = mode == 2 ? AtlasOutlineShape.LightFacing : mode == 3 ? AtlasOutlineShape.ViewDirectional
                : mode == 4 ? AtlasOutlineShape.DropShadow : AtlasOutlineShape.NormalExpansion;
            if (mode == 3)
            {
                var direction = AtlasMaterialReader.Vector(m, N("_OutlinePersonaDirection"), new Vector4(1f, 0f, 0f, 0f));
                outline.Direction = new Vector3(direction.x, direction.y, 0f);
            }
            else if (mode == 4) outline.Direction = Vector3Of(m, N("_OutlineDropShadowOffset"), Vector3.right);

            // Size in hundredths of a unit, local (scaled with the object) or world.
            outline.Width = Float(m, N("_LineWidth"), layer == 0 ? 1f : 2f) / 100f;
            outline.ObjectSpace = Int(m, N("_OutlineSpace"), 0) == 0;
            // The mask texture's channels feed the properties, each choosing one (4: off; Poiyomi 11).
            var masks = Texture(m, d, "_OutlineMask", AtlasTextureInterpretation.Data);
            AtlasMask MaskOf(string channel, int fallback)
            {
                int c = Int(m, N(channel), fallback);
                return c > 3 ? null : MaskOptions(m, masks == null ? null : new AtlasMask { Map = masks, Channel = Channels[c] }, "_OutlineMask", c);
            }
            outline.WidthMask = MaskOf("_OutlineMaskChannel", 0);
            int vertex = Int(m, N("_OutlineVertexColorMask"), 0);
            if (vertex > 0)
            {
                outline.WidthVertexMask = new AtlasMask
                {
                    VertexColor = true, Channel = Channels[Mathf.Clamp(vertex - 1, 0, 3)], Strength = Float(m, N("_OutlineVertexColorMaskStrength"), 1f)
                };
                outline.VertexStrengthMask = MaskOf("_OutlineVertexColorMaskChannel", 4);
            }
            outline.ColorMask = MaskOf("_OutlineColorMaskChannel", 4);
            outline.AlphaMask = MaskOf("_OutlineAlphaMaskChannel", 4);
            outline.TextureBlendMask = MaskOf("_OutlineTextureBlendMaskChannel", 4);
            outline.EmissionMask = MaskOf("_OutlineEmissionMaskChannel", 4);
            outline.ColorAdjustMask = MaskOf("_OutlineColorAdjustMaskChannel", 4);
            outline.FixedSizeMask = MaskOf("_OutlineFixedSizeMaskChannel", 4);
            outline.LitMask = MaskOf("_OutlineLightingMaskChannel", 4);
            outline.Highlight.Mask = MaskOf("_OutlineHighlightMaskChannel", 4);
            outline.Mask = BusMask(m, null, N("_OutlineGlobalMask"), N("_OutlineGlobalMaskBlendType"));
            outline.EdgeFade = Float(m, N("_OutlineEdgeFade"), 0f);
            outline.SmoothThin = On(m, "_OutlineThinFade");
            outline.WritesCameraDepth = On(m, "_OutlineCameraDepth", 1f);
            outline.Curvature = OutlineCurvature(m);
            if (mode == 2)
            {
                outline.LightFalloff = AtlasMaterialReader.Vector(m, N("_OutlineRimFalloff"), new Vector4(0f, 1f, 0f, 1f));
                outline.SizeFollowsLight = Float(m, N("_OutlineRimSizeFollowsLight"), 1f);
                outline.LightSideWidth = Float(m, N("_OutlineRimLightWidth"), 0f) / 100f;
                outline.DarkSideWidth = Float(m, N("_OutlineRimDarkWidth"), 0f) / 100f;
            }
            outline.NormalsFromVertexColor = On(m, N("_OutlineUseVertexColorNormals"));
            if (On(m, N("_OutlineFixedSize"), 1f))
            {
                outline.FixedSize = Float(m, N("_OutlineFixWidth"), 0.5f);
                outline.FixedSizeMaxDistance = Float(m, N("_OutlinesMaxDistance"), 1f);
            }
            outline.ClipAtZeroWidth = On(m, N("_OutlineClipAtZeroWidth"), 1f);

            // Color: texture × lerp(1, base (squared, UTS style), main texture blend) × color.
            outline.Color.Color = TintColor(m, d, N("_LineColor"), N("_LineColorThemeIndex"), outline.Color);
            outline.Color.Map = Texture(m, d, N("_OutlineTexture"));
            outline.BaseColorMultiply = Float(m, N("_OutlineTintMix"), 0f);
            outline.BaseColorSquared = On(m, N("_PoiUTSStyleOutlineBlend"));
            if (mode == 2) outline.LightColorMix = Float(m, N("_OutlineRimLightBlend"), 0f);
            outline.Lit = On(m, N("_OutlineLit"), 1f);
            outline.ShadowStrength = Float(m, N("_OutlineShadowStrength"), 0f);
            outline.EmissionAmount = Float(m, N("_OutlineEmission"), 0f);
            if (On(m, N("_OutlineEmissionCustomColor")))
            {
                outline.EmissionColor = new AtlasColorInput { Color = LinearColor(m, N("_OutlineEmissionColor"), Color.white) };
                outline.EmissionColor.Map = Texture(m, d, N("_OutlineEmissionMap"));
            }
            var highlight = outline.Highlight;
            highlight.Enabled = On(m, N("_OutlineHighlightEnabled"));
            if (highlight.Enabled)
            {
                highlight.Color = TintColor(m, d, N("_OutlineHighlightColor"), N("_OutlineHighlightColorThemeIndex"));
                highlight.Emission = Float(m, N("_OutlineHighlightEmission"), 0f);
                highlight.TintFromTexture = On(m, N("_OutlineHighlightApplyTex"));
                highlight.Border = Float(m, N("_OutlineHighlightBorder"), 0.85f);
                highlight.Blur = Float(m, N("_OutlineHighlightBlur"), 0.1f);
                highlight.ReceiveShadows = On(m, N("_OutlineHighlightReceiveShadow"));
            }
            outline.OverrideAlpha = On(m, N("_OutlineOverrideAlpha"));
            if (On(m, N("_OutlineAlphaDistanceFade")))
                outline.AlphaModifiers.Add(new AtlasAlphaModifier
                {
                    Kind = AtlasAlphaModifierKind.DistanceFade, PixelPosition = Int(m, N("_OutlineAlphaDistanceFadeType"), 1) == 1,
                    MinDistance = Float(m, N("_OutlineAlphaDistanceFadeMin"), 0f), MaxDistance = Float(m, N("_OutlineAlphaDistanceFadeMax"), 0f),
                    MinAlpha = Float(m, N("_OutlineAlphaDistanceFadeMinAlpha"), 0f), MaxAlpha = Float(m, N("_OutlineAlphaDistanceFadeMaxAlpha"), 1f),
                    Mask = MaskOf("_OutlineDistanceFadeMaskChannel", 4)
                });
            if (On(m, N("_OutlineHueShift"))) OutlineColorAdjust(m, d, outline, N);

            // Rendering, blending and stencil.
            var pass = outline.Pass;
            int cull = Int(m, N("_OutlineCull"), 1);
            pass.Cull = cull == 0 ? AtlasCull.Off : cull == 1 ? AtlasCull.Front : AtlasCull.Back;
            pass.DepthWrite = On(m, N("_OutlineZWrite"), 1f);
            pass.DepthTest = (AtlasDepthTest)Mathf.Clamp(Int(m, N("_OutlineZTest"), 4), 0, 8);
            outline.SourceBlend = (AtlasBlendFactor)Mathf.Clamp(Int(m, "_OutlineSrcBlend", 1), 0, 10);
            outline.DestinationBlend = (AtlasBlendFactor)Mathf.Clamp(Int(m, "_OutlineDstBlend", 0), 0, 10);
            if (Int(m, "_OutlineBlendOp", 0) != 0)
                d.Report(AtlasIssueKind.Unmapped, "render.blend-op", "A blend operation other than add isn't mapped yet.");
            var stencil = pass.Stencil;
            stencil.Reference = Int(m, N("_OutlineStencilRef"), 0);
            stencil.ReadMask = Int(m, N("_OutlineStencilReadMask"), 255);
            stencil.WriteMask = Int(m, N("_OutlineStencilWriteMask"), 255);
            stencil.Compare = (AtlasDepthTest)Mathf.Clamp(Int(m, N("_OutlineStencilCompareFunction"), 8), 0, 8);
            stencil.Pass = (AtlasStencilOperation)Mathf.Clamp(Int(m, N("_OutlineStencilPassOp"), 0), 0, 7);
            stencil.Fail = (AtlasStencilOperation)Mathf.Clamp(Int(m, N("_OutlineStencilFailOp"), 0), 0, 7);
            stencil.DepthFail = (AtlasStencilOperation)Mathf.Clamp(Int(m, N("_OutlineStencilZFailOp"), 0), 0, 7);
            stencil.Enabled = stencil.Reference != 0 || stencil.Compare != AtlasDepthTest.Always || stencil.Pass != AtlasStencilOperation.Keep;
            if (Int(m, N("_OutlineStencilType"), 0) == 1)
                d.Report(AtlasIssueKind.Unmapped, "render.stencil-faces", "Separate front- and back-face stencils aren't mapped yet.");
            pass.DepthClip = On(m, "_OutlineZClip", 1f);
            pass.DepthOffsetFactor = Float(m, "_OutlineOffsetFactor", 0f);
            pass.DepthOffsetUnits = Float(m, "_OutlineOffsetUnits", 0f);
            if (Float(m, N("_Offset_Z"), 0f) != 0f)
                d.Report(AtlasIssueKind.Unmapped, "outline.z-offset", "An outline's depth offset isn't mapped yet.");

            d.Outlines.Add(outline);
            OutlineAudioLink(m, d, d.Outlines.Count - 1, N);
        }

        // Hue (with its speed, turns per 20 seconds), then saturation and value (HSV, old) or
        // saturation and brightness (clamped), then gamma.
        private void OutlineColorAdjust(Material m, AtlasUnifiedMaterial d, AtlasOutline outline, System.Func<string, string> N)
        {
            var list = outline.ColorAdjustments;
            AtlasColorAdjustment Add(AtlasColorOperation operation, float amount)
            {
                var adjustment = new AtlasColorAdjustment { Target = AtlasAdjustmentTarget.BaseColor, Operation = operation, Amount = amount };
                list.Add(adjustment);
                return adjustment;
            }
            var hue = Add(AtlasColorOperation.Hue, Float(m, N("_OutlineHue"), 0f));
            hue.HueMethod = HueMethodOf(m, N("_OutlineHueShiftColorSpace"));
            float rate = Float(m, N("_OutlineHueOffsetSpeed"), 0f) * 0.05f;
            if (rate != 0f)
            {
                var turning = new AtlasModulator
                {
                    Target = "outlines[" + d.Outlines.Count + "].colorAdjustments[0].amount",
                    Source = AtlasModulatorSource.Time, Mode = AtlasModulatorMode.Add, Min = 0f, Max = 1f
                };
                turning.Time.Waveform = rate > 0f ? AtlasWaveform.Saw : AtlasWaveform.ReverseSaw;
                turning.Time.Frequency = Mathf.Abs(rate);
                d.Modulators.Add(turning);
            }
            if (Int(m, N("_OutlineSaturationMethod"), 1) == 0)
                Add(AtlasColorOperation.HsvOffset, 0f).Hsv = new Vector3(0f, Float(m, N("_OutlineSaturationHSV"), 0f), Float(m, N("_OutlineValue"), 1f));
            else
            {
                float saturation = Float(m, N("_OutlineSaturation"), 0f), brightness = Float(m, N("_OutlineBrightness"), 0f);
                if (saturation != 0f) Add(AtlasColorOperation.Saturation, 1f + saturation);
                if (brightness != 0f) Add(AtlasColorOperation.Brightness, 1f + brightness);
                Add(AtlasColorOperation.Clamp, 1f);
            }
            float gamma = Float(m, N("_OutlineGamma"), 1f);
            if (gamma != 1f) Add(AtlasColorOperation.Power, gamma);
        }

        // Size (in hundredths), emission and highlight emission by bands; the color overrides; the
        // texture's offset by a band and by chronotensity (Poiyomi 11).
        private void OutlineAudioLink(Material m, AtlasUnifiedMaterial d, int index, System.Func<string, string> N)
        {
            if (!AudioLinkOn(m)) return;
            string outline = "outlines[" + index + "]";
            if (Int(m, N("_OutlineAudioLinkMaskChannel"), 4) < 4)
                d.Report(AtlasIssueKind.Unmapped, "outline.audiolink-mask", "An outline's AudioLink mask isn't mapped yet.");
            if (d.Outlines[index].Color.Map != null)
            {
                var offset = AtlasMaterialReader.Vector(m, N("_AudioLinkOutlineTextureOffset"), Vector4.zero);
                int band = Int(m, N("_AudioLinkOutlineTextureOffsetBand"), 0);
                if (offset.x != 0f || offset.z != 0f) d.Modulators.Add(AudioModulator(m, outline + ".color.map.offset.x", AtlasModulatorMode.Add, band, offset.x, offset.z));
                if (offset.y != 0f || offset.w != 0f) d.Modulators.Add(AudioModulator(m, outline + ".color.map.offset.y", AtlasModulatorMode.Add, band, offset.y, offset.w));
                var speed = AtlasMaterialReader.Vector(m, N("_AudioLinkOutlineTextureChronoSpeed"), Vector4.zero);
                AudioChrono(m, d, outline + ".color.map.offset.x", N("_AudioLinkOutlineTextureChronoBand"), N("_AudioLinkOutlineTextureChronoType"), speed.x);
                AudioChrono(m, d, outline + ".color.map.offset.y", N("_AudioLinkOutlineTextureChronoBand"), N("_AudioLinkOutlineTextureChronoType"), speed.y);
            }
            var size = AtlasMaterialReader.Vector(m, N("_AudioLinkOutlineSize"), Vector4.zero);
            if (size.x != 0f || size.y != 0f)
                d.Modulators.Add(AudioModulator(m, outline + ".width", AtlasModulatorMode.Add, Int(m, N("_AudioLinkOutlineSizeBand"), 0), size.x / 100f, size.y / 100f));
            AudioRange(m, d, outline + ".emissionAmount", AtlasModulatorMode.Add, N("_AudioLinkOutlineEmissionBand"), N("_AudioLinkOutlineEmission"));
            AudioRange(m, d, outline + ".highlight.emission", AtlasModulatorMode.Add, N("_AudioLinkOutlineHighlightEmissionBand"), N("_AudioLinkOutlineHighlightEmission"));
            if (On(m, N("_OutlineALColorEnabled")))
            {
                var color = AudioModulator(m, outline + ".color.color", AtlasModulatorMode.BlendToColor, Int(m, N("_AudioLinkOutlineColorBand"), 0), 0f, 1f);
                color.Color = TintColor(m, d, N("_AudioLinkOutlineColor"), N("_AudioLinkOutlineColorThemeIndex"));
                color.Curve = AtlasMaterialReader.Vector(m, N("_AudioLinkOutlineColorRamp"), new Vector4(0f, 1f, 0f, 1f));
                d.Modulators.Add(color);
            }
            if (On(m, N("_OutlineHighlightALColorEnabled")))
            {
                var color = AudioModulator(m, outline + ".highlight.color", AtlasModulatorMode.BlendToColor, Int(m, N("_AudioLinkOutlineHighlightColorBand"), 0), 0f, 1f);
                color.Color = TintColor(m, d, N("_AudioLinkOutlineHighlightColor"), N("_AudioLinkOutlineHighlightColorThemeIndex"));
                color.Curve = AtlasMaterialReader.Vector(m, N("_AudioLinkOutlineHighlightColorRamp"), new Vector4(0f, 1f, 0f, 1f));
                d.Modulators.Add(color);
            }
        }
    }
}
