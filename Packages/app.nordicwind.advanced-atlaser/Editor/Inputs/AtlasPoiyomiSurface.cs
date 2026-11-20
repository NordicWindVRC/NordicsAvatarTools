using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: Main, Alpha Options, the normal sections and Rendering.
    internal sealed partial class AtlasPoiyomiReader
    {
        // Render presets: blend factors as the preset sets them (source, destination).
        private static readonly (int Preset, AtlasSurfaceMode Mode, int Source, int Destination)[] Presets =
        {
            (0, AtlasSurfaceMode.Opaque, 1, 0), (1, AtlasSurfaceMode.Cutout, 1, 0), (9, AtlasSurfaceMode.Fade, 5, 10),
            (2, AtlasSurfaceMode.Fade, 5, 10), (3, AtlasSurfaceMode.Transparent, 1, 10), (4, AtlasSurfaceMode.Additive, 1, 1),
            (5, AtlasSurfaceMode.SoftAdditive, 4, 1), (6, AtlasSurfaceMode.Multiplicative, 2, 0), (7, AtlasSurfaceMode.MultiplicativeDouble, 2, 3)
        };

        // Main: texture × color, pixel mode, ignore alpha, vertex coloring.
        private void Main(Material m, AtlasUnifiedMaterial d)
        {
            var surface = d.Surface;
            surface.BaseColor.Color = TintColor(m, d, "_Color", "_ColorThemeIndex", surface.BaseColor);
            surface.BaseColor.Map = Texture(m, d, "_MainTex");
            if (surface.BaseColor.Map != null && On(m, "_MainPixelMode")) surface.BaseColor.Map.Sampling = AtlasTextureSampling.Pixel;
            if (On(m, "_MainIgnoreTexAlpha")) surface.Alpha.Source = AtlasAlphaSource.BaseColorConstantAlpha;
            // 7.3 has no toggle: its vertex color slider is always live.
            if (major <= 7 || On(m, "_MainVertexColoringEnabled"))
            {
                var vertex = surface.VertexColor;
                vertex.Amount = Float(m, "_MainVertexColoring", 0f);
                vertex.MultiplyColor = vertex.Amount > 0f;
                vertex.AlphaAmount = Float(m, "_MainUseVertexColorAlpha", 0f);
                vertex.MultiplyAlpha = vertex.AlphaAmount > 0f;
                // "Linear Colors" converts the vertex colors from sRGB to linear.
                vertex.SrgbData = On(m, "_MainVertexColoringLinearSpace", 1f);
                if (On(m, "_MainVertexColorHueShiftEnabled"))
                {
                    vertex.HueEnabled = true;
                    vertex.HueShift = Float(m, "_MainVertexColorHueShift", 0f);
                    // Turns per 20 seconds.
                    vertex.HueSpeed = Float(m, "_MainVertexColorHueShiftSpeed", 0f) * 0.05f;
                    vertex.HueMethod = Int(m, "_MainVertexColorHueShiftColorSpace", 0) == 1 ? AtlasHueMethod.HSV : AtlasHueMethod.Oklab;
                    vertex.HueSet = Int(m, "_MainVertexColorHueSelectOrShift", 1) == 0;
                }
            }
        }

        // Alpha: the alpha map, offset, fades, dithering and coverage.
        private void Alpha(Material m, AtlasUnifiedMaterial d)
        {
            var alpha = d.Surface.Alpha;
            int mode = Int(m, "_MainAlphaMaskMode", 2);
            if (mode > 0)
            {
                var mask = Mask(m, d, "_AlphaMask", AlphaMaskChannel(m, d));
                if (mask != null)
                {
                    mask.Invert = On(m, "_AlphaMaskInvert");
                    alpha.Mask = mask;
                    if (mode == 1) alpha.Source = AtlasAlphaSource.Mask;
                    else alpha.MaskBlend = mode == 3 ? AtlasAlphaMaskBlend.Add : mode == 4 ? AtlasAlphaMaskBlend.Subtract : AtlasAlphaMaskBlend.Multiply;
                }
            }
            // 8's alpha map: alpha × its red channel (inverted with Inverse Clipping).
            if (major <= 8 && alpha.Mask == null)
            {
                var clipping = Mask(m, d, "_ClippingMask");
                if (clipping != null)
                {
                    clipping.Invert = On(m, "_Inverse_Clipping");
                    alpha.Mask = clipping;
                    alpha.MaskBlend = AtlasAlphaMaskBlend.Multiply;
                }
            }
            alpha.Offset = Float(m, "_AlphaMod", 0f);
            // The global mask acts on the alpha after Alpha Mod, not on the alpha mask.
            int global = Int(m, "_AlphaGlobalMask", 0);
            if (global > 0)
            {
                alpha.Bus = Mathf.Min(global, BusChannels) - 1;
                alpha.BusBlend = BusBlend(Int(m, "_AlphaGlobalMaskBlendType", 2));
            }
            if (On(m, "_AlphaDistanceFade") || (major <= 7 && DistanceFade7(m)))
            {
                alpha.Modifiers.Add(new AtlasAlphaModifier { Kind = AtlasAlphaModifierKind.DistanceFade,
                    PixelPosition = Int(m, "_AlphaDistanceFadeType", 1) == 1,
                    MinDistance = Float(m, "_AlphaDistanceFadeMin", 0f), MaxDistance = Float(m, "_AlphaDistanceFadeMax", 0f),
                    MinAlpha = Float(m, "_AlphaDistanceFadeMinAlpha", 0f), MaxAlpha = Float(m, "_AlphaDistanceFadeMaxAlpha", 1f) });
                alpha.Modifiers[alpha.Modifiers.Count - 1].Mask = BusMask(m, null, "_AlphaDistanceFadeGlobalMask");
            }
            if (On(m, "_AlphaFresnel"))
            {
                alpha.Modifiers.Add(new AtlasAlphaModifier { Kind = AtlasAlphaModifierKind.ViewAngleFade,
                    Intensity = Float(m, "_AlphaFresnelAlpha", 0f), Sharpness = Float(m, "_AlphaFresnelSharpness", 0.5f),
                    Width = Float(m, "_AlphaFresnelWidth", 0.5f), Invert = On(m, "_AlphaFresnelInvert") });
                alpha.Modifiers[alpha.Modifiers.Count - 1].Mask = BusMask(m, null, "_AlphaFresnelGlobalMask");
            }
            if (On(m, "_AlphaAngular"))
            {
                alpha.Modifiers.Add(new AtlasAlphaModifier { Kind = AtlasAlphaModifierKind.AngleFade,
                    AngleType = (AtlasAngleFadeType)Mathf.Clamp(Int(m, "_AngleType", 0), 0, 2),
                    CameraAngleMin = Float(m, "_CameraAngleMin", 45f), CameraAngleMax = Float(m, "_CameraAngleMax", 90f),
                    ModelAngleMin = Float(m, "_ModelAngleMin", 45f), ModelAngleMax = Float(m, "_ModelAngleMax", 90f),
                    Forward = Vector3Of(m, "_AngleForwardDirection", Vector3.forward),
                    FromVertex = Int(m, "_AngleCompareTo", 0) == 1, MinAlpha = Float(m, "_AngleMinAlpha", 0f) });
                alpha.Modifiers[alpha.Modifiers.Count - 1].Mask = BusMask(m, null, "_AlphaAngularGlobalMask");
            }
            if (On(m, "_AlphaAudioLinkEnabled") && AudioLinkOn(m))
            {
                // Adds lerp(min, max, band), saturated, after the fades.
                alpha.Modifiers.Add(new AtlasAlphaModifier { Kind = AtlasAlphaModifierKind.Add });
                var range = AtlasMaterialReader.Vector(m, "_AlphaAudioLinkAddRange", Vector4.zero);
                d.Modulators.Add(AudioModulator(m, "surface.alpha.modifiers[" + (alpha.Modifiers.Count - 1) + "].amount",
                    AtlasModulatorMode.Replace, Int(m, "_AlphaAudioLinkAddBand", 0), range.x, range.y));
            }
            alpha.SharpenedCoverage = On(m, "_AlphaSharpenedA2C");
            alpha.MipScale = Float(m, "_AlphaMipScale", 0.25f);
            alpha.Dithering = On(m, "_AlphaDithering");
            alpha.DitherGradient = Float(m, "_AlphaDitherGradient", 0.1f);
            alpha.DitherBias = Float(m, "_AlphaDitherBias", 0f);
            alpha.DitherCutout = Int(m, "_AlphaDitherMode", 0) == 1;
            d.Render.DitheredShadows = On(m, "_AlphaDitheredShadows");
            if (On(m, "_AlphaForceOpaque", 1f)) alpha.Source = AtlasAlphaSource.None;
        }

        // 7.3's distance fade has no toggle: it applies unless its settings leave the alpha as is.
        private bool DistanceFade7(Material m)
        {
            float minAlpha = Float(m, "_AlphaDistanceFadeMinAlpha", 0f), maxAlpha = Float(m, "_AlphaDistanceFadeMaxAlpha", 1f);
            float near = Float(m, "_AlphaDistanceFadeMin", 0f), far = Float(m, "_AlphaDistanceFadeMax", 0f);
            return !(minAlpha == 1f && maxAlpha == 1f) && !(maxAlpha == 1f && near == 0f && far == 0f);
        }

        // The alpha map's channel weights: one channel at full weight is a channel; anything else
        // (mixed weights, levels, gamma) is reported and read as the strongest channel.
        private AtlasChannel AlphaMaskChannel(Material m, AtlasUnifiedMaterial d)
        {
            float[] weights = { Float(m, "_AlphaMaskR", 1f), Float(m, "_AlphaMaskG", 0f), Float(m, "_AlphaMaskB", 0f), Float(m, "_AlphaMaskA", 0f) };
            int strongest = 0, used = 0;
            for (int i = 0; i < 4; ++i)
            {
                if (weights[i] > weights[strongest]) strongest = i;
                if (weights[i] > 0f) used++;
            }
            var levels = AtlasMaterialReader.Vector(m, "_AlphaMaskMinMax", new Vector4(0f, 1f, 0f, 1f));
            bool plain = used == 1 && weights[strongest] == 1f && levels == new Vector4(0f, 1f, 0f, 1f) &&
                         Float(m, "_AlphaMaskGamma", 1f) == 1f && Float(m, "_AlphaMaskBlendStrength", 1f) == 1f && Float(m, "_AlphaMaskValue", 0f) == 0f;
            if (!plain && AtlasMaterialReader.Texture(m, "_AlphaMask") != null)
                d.Report(AtlasIssueKind.Unmapped, "alpha.mask-levels", "The alpha map's channel mix and levels aren't mapped yet; read as one channel.");
            return Channels[strongest];
        }

        // Normal maps: the main one, the second with its mask, the detail normal (its strength by the
        // detail mask's green), the bent normal, and normal correction.
        private void Normals(Material m, AtlasUnifiedMaterial d)
        {
            d.Surface.Normal.Map = Texture(m, d, "_BumpMap", AtlasTextureInterpretation.TangentNormal);
            d.Surface.Normal.Strength = Float(m, "_BumpScale", 1f);
            if (On(m, "_UseBump2ndMap"))
            {
                NormalLayer(d, Texture(m, d, "_Bump2ndMap", AtlasTextureInterpretation.TangentNormal), Float(m, "_Bump2ndScale", 1f),
                    BusMask(m, Mask(m, d, "_Bump2ndScaleMask"), "_Bump2ndScaleMaskGlobalMask"));
            }
            if (On(m, "_DetailEnabled"))
            {
                NormalLayer(d, Texture(m, d, "_DetailNormalMap", AtlasTextureInterpretation.TangentNormal), Float(m, "_DetailNormalMapScale", 1f),
                    BusMask(m, Mask(m, d, "_DetailMask", AtlasChannel.Green), "_DetailNormalGlobalMask"));
            }
            if (On(m, "_EnableBentNormal"))
            {
                var bent = d.Surface.BentNormal;
                bent.Map = Texture(m, d, "_BentNormalMap", AtlasTextureInterpretation.TangentNormal);
                bent.SpecularOcclusion = Float(m, "_BentNormalSpecOccStrength", 1f);
                bent.IndirectDiffuse = On(m, "_BentNormalIndirectDiffuse", 1f);
            }
            if (On(m, "_NormalCorrect"))
            {
                d.Surface.Normal.CorrectAmount = Float(m, "_NormalCorrectAmount", 0.9f);
                d.Surface.Normal.CorrectOrigin = Vector3Of(m, "_NormalCorrectOrigin", new Vector3(0f, 0.4f, -0.025f));
                int vertex = Int(m, "_NormalCorrectVertexColorMask", 0);
                if (vertex > 0) d.Surface.Normal.CorrectMask = new AtlasMask { VertexColor = true, Channel = Channels[Mathf.Clamp(vertex - 1, 0, 3)] };
            }
        }

        private void NormalLayer(AtlasUnifiedMaterial d, AtlasTextureReference map, float strength, AtlasMask mask)
        {
            if (map == null) return;
            var layer = new AtlasSurfaceLayer { Mask = mask };
            layer.Source.Map = map;
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Normal, AtlasBlend.Replace, strength));
            d.Layers.Add(layer);
        }

        // Rendering: the preset (or custom blend factors), culling, depth, color mask, stencil, fog.
        private void Render(Material m, AtlasUnifiedMaterial d)
        {
            var render = d.Render;
            int preset = Int(m, "_Mode", 0);
            int source = Int(m, "_SrcBlend", 1), destination = Int(m, "_DstBlend", 0);
            var match = System.Array.Find(Presets, p => p.Preset == preset);
            if (match.Source == source && match.Destination == destination) render.SurfaceMode = match.Mode;
            else
            {
                // Factors changed from the preset's: kept as they are.
                render.SurfaceMode = AtlasSurfaceMode.Custom;
                render.SourceBlend = (AtlasBlendFactor)Mathf.Clamp(source, 0, 10);
                render.DestinationBlend = (AtlasBlendFactor)Mathf.Clamp(destination, 0, 10);
            }
            render.CutoffInTransparent = preset == 9;
            render.Cutoff = Float(m, "_Cutoff", 0.5f);
            render.AlphaToCoverage = On(m, "_AlphaToCoverage");
            if (Int(m, "_BlendOp", 0) != 0)
                d.Report(AtlasIssueKind.Unmapped, "render.blend-op", "A blend operation other than add isn't mapped yet.");

            PassSettings(m, d, render.Pass, "");

            render.Fog = !On(m, "_IgnoreFog");
            render.TwoSidedLighting = On(m, "_FlipBackfaceNormals", 1f);
            render.ReduceNearClipping = On(m, "_RenderingReduceClipDistance");
            SecondPass(m, d);
        }

        // Culling, depth, color mask and stencil; the second pass's names end in 2 (_Stencil2…).
        private void PassSettings(Material m, AtlasUnifiedMaterial d, AtlasPassSettings pass, string n)
        {
            int cull = Int(m, "_Cull" + n, 2);
            pass.Cull = cull == 0 ? AtlasCull.Off : cull == 1 ? AtlasCull.Front : AtlasCull.Back;
            pass.DepthTest = (AtlasDepthTest)Mathf.Clamp(Int(m, "_ZTest" + n, 4), 0, 8);
            pass.DepthWrite = On(m, "_ZWrite" + n, 1f);
            pass.DepthClip = On(m, "_ZClip" + n, 1f);
            pass.DepthOffsetFactor = Float(m, "_OffsetFactor" + n, 0f);
            pass.DepthOffsetUnits = Float(m, "_OffsetUnits" + n, 0f);
            pass.ColorMask = Int(m, "_ColorMask" + n, 15);
            string s = "_Stencil" + n;
            var stencil = pass.Stencil;
            stencil.Reference = Int(m, s + "Ref", 0);
            stencil.ReadMask = Int(m, s + "ReadMask", 255);
            stencil.WriteMask = Int(m, s + "WriteMask", 255);
            stencil.Compare = (AtlasDepthTest)Mathf.Clamp(Int(m, s + "CompareFunction", 8), 0, 8);
            stencil.Pass = (AtlasStencilOperation)Mathf.Clamp(Int(m, s + "PassOp", 0), 0, 7);
            stencil.Fail = (AtlasStencilOperation)Mathf.Clamp(Int(m, s + "FailOp", 0), 0, 7);
            stencil.DepthFail = (AtlasStencilOperation)Mathf.Clamp(Int(m, s + "ZFailOp", 0), 0, 7);
            stencil.Enabled = stencil.Reference != 0 || stencil.Compare != AtlasDepthTest.Always || stencil.Pass != AtlasStencilOperation.Keep;
            if (Int(m, s + "Type", 0) == 1)
                d.Report(AtlasIssueKind.Unmapped, "render.stencil-faces", "Separate front- and back-face stencils aren't mapped yet.");
        }

        // The Two Pass variants' second pass, and its pass masks (each writes 1 in its own pass and
        // 0 in the other).
        private void SecondPass(Material m, AtlasUnifiedMaterial d)
        {
            // Lil Fur Two Pass has no second surface pass (its strands are drawn twice instead).
            if (!IsVariant(m, d, "_ModeTwoPass", "Two Pass", "Self Grab", "-Lil Fur")) return;
            var second = d.Render.SecondPass;
            second.Enabled = true;
            int preset = Int(m, "_ModeTwoPass", 0);
            int source = Int(m, "_SrcBlend2", 1), destination = Int(m, "_DstBlend2", 0);
            var match = System.Array.Find(Presets, p => p.Preset == preset);
            if (match.Source == source && match.Destination == destination) second.SurfaceMode = match.Mode;
            else
            {
                second.SurfaceMode = AtlasSurfaceMode.Custom;
                second.SourceBlend = (AtlasBlendFactor)Mathf.Clamp(source, 0, 10);
                second.DestinationBlend = (AtlasBlendFactor)Mathf.Clamp(destination, 0, 10);
            }
            if (Int(m, "_BlendOp2", 0) != 0)
                d.Report(AtlasIssueKind.Unmapped, "render.blend-op", "A blend operation other than add isn't mapped yet.");
            second.Cutoff = Float(m, "_TwoPassAlphaCutoff", 0.5f);
            second.CutoffInvert = On(m, "_TwoPassAlphaCutoffInvert");
            second.AlphaPower = Float(m, "_TwoPassAlphaPower", 1f);
            second.AlphaToCoverage = On(m, "_AlphaToCoverage2");
            second.ForceOpaque = On(m, "_AlphaForceOpaque2");
            int color = Int(m, "_TwoPassOverrideFirstPassColor", 0);
            second.ColorSource = color == 1 ? AtlasPassColor.ColorAndAlpha : color == 2 ? AtlasPassColor.AlphaOnly : AtlasPassColor.Same;
            if (color == 1) second.Color = TintColor(m, d, "_TwoPassColor", "_TwoPassColorThemeIndex");
            second.Alpha = Float(m, "_TwoPassAlphaOverride", 1f);
            PassSettings(m, d, second.Pass, "2");

            PassMask(m, d, "_TwoPassFirstPassGlobalMaskIndex", "_TwoPassFirtstPassApplyGlobalMaskBlendType", false);
            PassMask(m, d, "_TwoPassSecondPassGlobalMaskIndex", "_TwoPassSecondPassApplyGlobalMaskBlendType", true);
        }

        private void PassMask(Material m, AtlasUnifiedMaterial d, string index, string blend, bool second)
        {
            int channel = Int(m, index, 0);
            if (channel <= 0) return;
            d.MaskBus.Steps.Add(new AtlasMaskBusStep
            {
                Channel = Mathf.Clamp(channel - 1, 0, 15), Kind = AtlasMaskBusStepKind.SecondPass, Inside = second,
                Blend = (AtlasMaskBlend)Mathf.Clamp(Int(m, blend, 0), 0, 7)
            });
        }
    }
}
