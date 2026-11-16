using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: Outlines, with their rendering options, blending, stencil and AudioLink.
    public abstract partial class AtlasPoiyomiParser
    {
        private static void Outline(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_EnableOutlines")) return;
            var outline = new AtlasOutline();
            int mode = Int(m, "_OutlineExpansionMode", 1);
            outline.Shape = mode == 2 ? AtlasOutlineShape.LightFacing : mode == 3 ? AtlasOutlineShape.ViewDirectional
                : mode == 4 ? AtlasOutlineShape.DropShadow : AtlasOutlineShape.NormalExpansion;
            if (mode == 3)
            {
                var direction = AtlasMaterialReader.Vector(m, "_OutlinePersonaDirection", new Vector4(1f, 0f, 0f, 0f));
                outline.Direction = new Vector3(direction.x, direction.y, 0f);
            }
            else if (mode == 4) outline.Direction = Vector3Of(m, "_OutlineDropShadowOffset", Vector3.right);

            // Size in hundredths of a unit, local (scaled with the object) or world.
            outline.Width = Float(m, "_LineWidth", 1f) / 100f;
            outline.ObjectSpace = Int(m, "_OutlineSpace", 0) == 0;
            outline.WidthMask = Mask(m, d, "_OutlineMask", Channels[Mathf.Clamp(Int(m, "_OutlineMaskChannel", 0), 0, 3)]);
            int vertex = Int(m, "_OutlineVertexColorMask", 0);
            if (vertex > 0)
                outline.WidthVertexMask = new AtlasMask
                {
                    VertexColor = true, Channel = Channels[Mathf.Clamp(vertex - 1, 0, 3)], Strength = Float(m, "_OutlineVertexColorMaskStrength", 1f)
                };
            outline.NormalsFromVertexColor = On(m, "_OutlineUseVertexColorNormals");
            if (On(m, "_OutlineFixedSize", 1f))
            {
                outline.FixedSize = Float(m, "_OutlineFixWidth", 0.5f);
                outline.FixedSizeMaxDistance = Float(m, "_OutlinesMaxDistance", 1f);
            }
            outline.ClipAtZeroWidth = On(m, "_OutlineClipAtZeroWidth", 1f);

            // Colour: texture × lerp(1, base (squared, UTS style), main texture blend) × colour.
            outline.Color.Color = TintColor(m, d, "_LineColor", "_LineColorThemeIndex", outline.Color);
            outline.Color.Map = Texture(m, d, "_OutlineTexture");
            outline.BaseColorMultiply = Float(m, "_OutlineTintMix", 0f);
            outline.BaseColorSquared = On(m, "_PoiUTSStyleOutlineBlend");
            if (mode == 2) outline.LightColorMix = Float(m, "_OutlineRimLightBlend", 0f);
            outline.Lit = On(m, "_OutlineLit", 1f);
            outline.ShadowStrength = Float(m, "_OutlineShadowStrength", 0f);
            outline.EmissionAmount = Float(m, "_OutlineEmission", 0f);
            var highlight = outline.Highlight;
            highlight.Enabled = On(m, "_OutlineHighlightEnabled");
            if (highlight.Enabled)
            {
                highlight.Color = TintColor(m, d, "_OutlineHighlightColor", "_OutlineHighlightColorThemeIndex");
                highlight.Emission = Float(m, "_OutlineHighlightEmission", 0f);
                highlight.TintFromTexture = On(m, "_OutlineHighlightApplyTex");
                highlight.Border = Float(m, "_OutlineHighlightBorder", 0.85f);
                highlight.Blur = Float(m, "_OutlineHighlightBlur", 0.1f);
                highlight.ReceiveShadows = On(m, "_OutlineHighlightReceiveShadow");
            }
            outline.OverrideAlpha = On(m, "_OutlineOverrideAlpha");
            if (On(m, "_OutlineAlphaDistanceFade"))
                outline.AlphaModifiers.Add(new AtlasAlphaModifier
                {
                    Kind = AtlasAlphaModifierKind.DistanceFade, PixelPosition = Int(m, "_OutlineAlphaDistanceFadeType", 1) == 1,
                    MinDistance = Float(m, "_OutlineAlphaDistanceFadeMin", 0f), MaxDistance = Float(m, "_OutlineAlphaDistanceFadeMax", 0f),
                    MinAlpha = Float(m, "_OutlineAlphaDistanceFadeMinAlpha", 0f), MaxAlpha = Float(m, "_OutlineAlphaDistanceFadeMaxAlpha", 1f)
                });
            if (On(m, "_OutlineHueShift"))
                d.Report(AtlasIssueKind.Unmapped, "outline.color-adjust", "An outline's colour adjust isn't mapped yet.");

            // Rendering, blending and stencil.
            var pass = outline.Pass;
            int cull = Int(m, "_OutlineCull", 1);
            pass.Cull = cull == 0 ? AtlasCull.Off : cull == 1 ? AtlasCull.Front : AtlasCull.Back;
            pass.DepthWrite = On(m, "_OutlineZWrite", 1f);
            pass.DepthTest = (AtlasDepthTest)Mathf.Clamp(Int(m, "_OutlineZTest", 4), 0, 8);
            outline.SourceBlend = (AtlasBlendFactor)Mathf.Clamp(Int(m, "_OutlineSrcBlend", 1), 0, 10);
            outline.DestinationBlend = (AtlasBlendFactor)Mathf.Clamp(Int(m, "_OutlineDstBlend", 0), 0, 10);
            if (Int(m, "_OutlineBlendOp", 0) != 0)
                d.Report(AtlasIssueKind.Unmapped, "render.blend-op", "A blend operation other than add isn't mapped yet.");
            var stencil = pass.Stencil;
            stencil.Reference = Int(m, "_OutlineStencilRef", 0);
            stencil.ReadMask = Int(m, "_OutlineStencilReadMask", 255);
            stencil.WriteMask = Int(m, "_OutlineStencilWriteMask", 255);
            stencil.Compare = (AtlasDepthTest)Mathf.Clamp(Int(m, "_OutlineStencilCompareFunction", 8), 0, 8);
            stencil.Pass = (AtlasStencilOperation)Mathf.Clamp(Int(m, "_OutlineStencilPassOp", 0), 0, 7);
            stencil.Fail = (AtlasStencilOperation)Mathf.Clamp(Int(m, "_OutlineStencilFailOp", 0), 0, 7);
            stencil.DepthFail = (AtlasStencilOperation)Mathf.Clamp(Int(m, "_OutlineStencilZFailOp", 0), 0, 7);
            stencil.Enabled = stencil.Reference != 0 || stencil.Compare != AtlasDepthTest.Always || stencil.Pass != AtlasStencilOperation.Keep;
            if (Int(m, "_OutlineStencilType", 0) == 1)
                d.Report(AtlasIssueKind.Unmapped, "render.stencil-faces", "Separate front- and back-face stencils aren't mapped yet.");
            if (Float(m, "_Offset_Z", 0f) != 0f)
                d.Report(AtlasIssueKind.Unmapped, "outline.z-offset", "An outline's depth offset isn't mapped yet.");

            d.Outlines.Add(outline);
            OutlineAudioLink(m, d, d.Outlines.Count - 1);
        }

        // Size (in hundredths), emission and highlight emission by bands; the colour overrides.
        private static void OutlineAudioLink(Material m, AtlasUnifiedMaterial d, int index)
        {
            if (!AudioLinkOn(m)) return;
            string outline = "outlines[" + index + "]";
            var size = AtlasMaterialReader.Vector(m, "_AudioLinkOutlineSize", Vector4.zero);
            if (size.x != 0f || size.y != 0f)
                d.Modulators.Add(AudioModulator(m, outline + ".width", AtlasModulatorMode.Add, Int(m, "_AudioLinkOutlineSizeBand", 0), size.x / 100f, size.y / 100f));
            AudioRange(m, d, outline + ".emissionAmount", AtlasModulatorMode.Add, "_AudioLinkOutlineEmissionBand", "_AudioLinkOutlineEmission");
            AudioRange(m, d, outline + ".highlight.emission", AtlasModulatorMode.Add, "_AudioLinkOutlineHighlightEmissionBand", "_AudioLinkOutlineHighlightEmission");
            if (On(m, "_OutlineALColorEnabled"))
            {
                var color = AudioModulator(m, outline + ".color.color", AtlasModulatorMode.BlendToColor, Int(m, "_AudioLinkOutlineColorBand", 0), 0f, 1f);
                color.Color = TintColor(m, d, "_AudioLinkOutlineColor", "_AudioLinkOutlineColorThemeIndex");
                color.Curve = AtlasMaterialReader.Vector(m, "_AudioLinkOutlineColorRamp", new Vector4(0f, 1f, 0f, 1f));
                d.Modulators.Add(color);
            }
            if (On(m, "_OutlineHighlightALColorEnabled"))
            {
                var color = AudioModulator(m, outline + ".highlight.color", AtlasModulatorMode.BlendToColor, Int(m, "_AudioLinkOutlineHighlightColorBand", 0), 0f, 1f);
                color.Color = TintColor(m, d, "_AudioLinkOutlineHighlightColor", "_AudioLinkOutlineHighlightColorThemeIndex");
                color.Curve = AtlasMaterialReader.Vector(m, "_AudioLinkOutlineHighlightColorRamp", new Vector4(0f, 1f, 0f, 1f));
                d.Modulators.Add(color);
            }
        }
    }
}
