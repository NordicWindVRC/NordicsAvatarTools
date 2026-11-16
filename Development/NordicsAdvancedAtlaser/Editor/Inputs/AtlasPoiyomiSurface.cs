using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: Main, Alpha Options, the normal sections and Rendering.
    public abstract partial class AtlasPoiyomiParser
    {
        // Render presets: blend factors as the preset sets them (source, destination).
        private static readonly (int Preset, AtlasSurfaceMode Mode, int Source, int Destination)[] Presets =
        {
            (0, AtlasSurfaceMode.Opaque, 1, 0), (1, AtlasSurfaceMode.Cutout, 1, 0), (9, AtlasSurfaceMode.Fade, 5, 10),
            (2, AtlasSurfaceMode.Fade, 5, 10), (3, AtlasSurfaceMode.Transparent, 1, 10), (4, AtlasSurfaceMode.Additive, 1, 1),
            (5, AtlasSurfaceMode.SoftAdditive, 4, 1), (6, AtlasSurfaceMode.Multiplicative, 2, 0), (7, AtlasSurfaceMode.MultiplicativeDouble, 2, 3)
        };

        // Main: texture × colour, pixel mode, ignore alpha, vertex colouring.
        private static void Main(Material m, AtlasUnifiedMaterial d)
        {
            var surface = d.Surface;
            surface.BaseColor.Color = TintColor(m, d, "_Color", "_ColorThemeIndex", surface.BaseColor);
            surface.BaseColor.Map = Texture(m, d, "_MainTex");
            if (surface.BaseColor.Map != null && On(m, "_MainPixelMode")) surface.BaseColor.Map.Sampling = AtlasTextureSampling.Pixel;
            if (On(m, "_MainIgnoreTexAlpha")) surface.Alpha.Source = AtlasAlphaSource.BaseColorConstantAlpha;
            if (On(m, "_MainVertexColoringEnabled"))
            {
                var vertex = surface.VertexColor;
                vertex.Amount = Float(m, "_MainVertexColoring", 0f);
                vertex.MultiplyColor = vertex.Amount > 0f;
                vertex.AlphaAmount = Float(m, "_MainUseVertexColorAlpha", 0f);
                vertex.MultiplyAlpha = vertex.AlphaAmount > 0f;
                // "Linear Colors" converts the vertex colours from sRGB to linear.
                vertex.SrgbData = On(m, "_MainVertexColoringLinearSpace", 1f);
            }
        }

        // Alpha: the alpha map, offset, fades, dithering and coverage.
        private static void Alpha(Material m, AtlasUnifiedMaterial d)
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
            alpha.Offset = Float(m, "_AlphaMod", 0f);
            alpha.Mask = BusMask(m, alpha.Mask, "_AlphaGlobalMask");
            if (On(m, "_AlphaDistanceFade"))
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
            if (On(m, "_AlphaForceOpaque", 1f)) alpha.Source = AtlasAlphaSource.None;
        }

        // The alpha map's channel weights: one channel at full weight is a channel; anything else
        // (mixed weights, levels, gamma) is reported and read as the strongest channel.
        private static AtlasChannel AlphaMaskChannel(Material m, AtlasUnifiedMaterial d)
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
        private static void Normals(Material m, AtlasUnifiedMaterial d)
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
            }
        }

        private static void NormalLayer(AtlasUnifiedMaterial d, AtlasTextureReference map, float strength, AtlasMask mask)
        {
            if (map == null) return;
            var layer = new AtlasSurfaceLayer { Mask = mask };
            layer.Source.Map = map;
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Normal, AtlasBlend.Replace, strength));
            d.Layers.Add(layer);
        }

        // Rendering: the preset (or custom blend factors), culling, depth, colour mask, stencil, fog.
        private static void Render(Material m, AtlasUnifiedMaterial d)
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

            var pass = render.Pass;
            int cull = Int(m, "_Cull", 2);
            pass.Cull = cull == 0 ? AtlasCull.Off : cull == 1 ? AtlasCull.Front : AtlasCull.Back;
            pass.DepthTest = (AtlasDepthTest)Mathf.Clamp(Int(m, "_ZTest", 4), 0, 8);
            pass.DepthWrite = On(m, "_ZWrite", 1f);
            pass.DepthClip = On(m, "_ZClip", 1f);
            pass.DepthOffsetFactor = Float(m, "_OffsetFactor", 0f);
            pass.DepthOffsetUnits = Float(m, "_OffsetUnits", 0f);
            pass.ColorMask = Int(m, "_ColorMask", 15);
            var stencil = pass.Stencil;
            stencil.Reference = Int(m, "_StencilRef", 0);
            stencil.ReadMask = Int(m, "_StencilReadMask", 255);
            stencil.WriteMask = Int(m, "_StencilWriteMask", 255);
            stencil.Compare = (AtlasDepthTest)Mathf.Clamp(Int(m, "_StencilCompareFunction", 8), 0, 8);
            stencil.Pass = (AtlasStencilOperation)Mathf.Clamp(Int(m, "_StencilPassOp", 0), 0, 7);
            stencil.Fail = (AtlasStencilOperation)Mathf.Clamp(Int(m, "_StencilFailOp", 0), 0, 7);
            stencil.DepthFail = (AtlasStencilOperation)Mathf.Clamp(Int(m, "_StencilZFailOp", 0), 0, 7);
            stencil.Enabled = stencil.Reference != 0 || stencil.Compare != AtlasDepthTest.Always || stencil.Pass != AtlasStencilOperation.Keep;
            if (Int(m, "_StencilType", 0) == 1)
                d.Report(AtlasIssueKind.Unmapped, "render.stencil-faces", "Separate front- and back-face stencils aren't mapped yet.");

            render.Fog = !On(m, "_IgnoreFog");
            render.TwoSidedLighting = On(m, "_FlipBackfaceNormals", 1f);
            render.ReduceNearClipping = On(m, "_RenderingReduceClipDistance");
        }
    }
}
