using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: the Grab Pass and Self Grab variants' screen effects (catalogue 4.7): refraction,
    // blur, pixelation, colour, colour ramp and the blend onto the surface, each with a channel of
    // the blend map and a global mask; Pro's lava lamp after it.
    internal sealed partial class AtlasPoiyomiReader
    {
        private void GrabPass(Material m, AtlasUnifiedMaterial d)
        {
            ScreenGrab(m, d);
            LavaLamp(m, d);
        }

        // Blended into the base colour where Poiyomi does it, after the dissolve and before the
        // decals, and exempt from the lighting (Poiyomi divides the screen by it).
        private void ScreenGrab(Material m, AtlasUnifiedMaterial d)
        {
            if (!IsVariant(m, d, "_EnableGrabpass", "Grab Pass", "Self Grab") || !On(m, "_EnableGrabpass", 1f)) return;
            var map = Texture(m, d, "_GrabPassBlendMap", AtlasTextureInterpretation.Data);
            var layer = new AtlasRefractionLayer
            {
                Placement = AtlasPlacement.Surface,
                IOR = Float(m, "_RefractionIndex", 1.333333f), FieldOfViewScale = true,
                FresnelPower = Float(m, "_RefractionFresnelPower", 0f),
                IgnoreForeground = On(m, "_RefractionIgnoreForeground"),
                Amount = Float(m, "_GrabPassBlendFactor", 1f),
                SourceBlend = (AtlasBlendFactor)Mathf.Clamp(Int(m, "_GrabSrcBlend", 5), 0, 10),
                DestinationBlend = (AtlasBlendFactor)Mathf.Clamp(Int(m, "_GrabDstBlend", 10), 0, 10),
                // Self Grab's extra pass blends with what the first pass drew, whatever its alpha.
                SelfGrab = IsVariant(m, d, "_ModeTwoPass", "Self Grab"),
                Tint = LinearColor(m, "_GrabpassColor", Color.white),
                Brightness = Float(m, "_GrabPassBrightness", 1f), AddBrightness = Float(m, "_GrabPassAddBrightness", 0f),
                Vibrance = Float(m, "_GrabVibrance", 0f), Saturation = Float(m, "_GrabSaturation", 1f)
            };
            layer.ByAlpha = !layer.SelfGrab;
            bool off;

            layer.Refracts = On(m, "_RefractionEnabled", 1f);
            layer.RefractionMask = GrabMask(m, d, map, "_GrabRefraction", 4, out off);
            if (off) layer.Refracts = false;
            float aberration = Float(m, "_RefractionChromaticAberration", 0f);
            layer.ChromaticAberration = layer.Refracts && aberration > 0f;
            layer.ChromaticAberrationStrength = aberration;

            var blurMask = GrabMask(m, d, map, "_GrabBlur", 4, out off);
            if (On(m, "_EnableBlur") && !off)
                layer.Blur = new AtlasScreenBlur
                {
                    Strength = Float(m, "_GrabBlurDistance", 0f), ByRoughness = Float(m, "_GrabBlurUseSmoothness", 0f),
                    Pattern = (AtlasBlurPattern)Mathf.Clamp(Int(m, "_GrabBlurPattern", 0), 0, 2),
                    Quality = Float(m, "_GrabBlurQuality", 10f), CameraFalloff = Float(m, "_GrabBlurFalloff", 0f),
                    Power = Float(m, "_GrabBlurPower", 0f), DepthFade = On(m, "_GrabBlurUseDepth"),
                    DepthRamp = AtlasMaterialReader.Vector(m, "_GrabBlurDepthRamp", new Vector4(0f, 1f, 0f, 1f)),
                    Mask = blurMask
                };

            var pixelMask = GrabMask(m, d, map, "_GrabPixel", 4, out off);
            if (On(m, "_GrabPixelationEnabled") && !off)
                layer.Pixelation = new AtlasScreenPixelation
                {
                    Size = Float(m, "_GrabPixelSize", 8f), DistanceScaling = On(m, "_GrabPixelDistanceScaling"),
                    DistanceAmount = Float(m, "_GrabPixelDistanceScalingAmount", 1f), Gap = Float(m, "_GrabPixelGap", 0f),
                    Mask = pixelMask
                };

            var rampMask = GrabMask(m, d, map, "_GrabRamp", 4, out off);
            if (On(m, "_GrabRampEnabled") && !off)
            {
                var ramp = new AtlasScreenRamp
                {
                    Palette = (AtlasScreenRampPalette)Mathf.Clamp(Int(m, "_GrabRampPreset", 1), 0, 5),
                    Blend = Float(m, "_GrabRampBlend", 1f), Contrast = Float(m, "_GrabRampContrast", 0f), Offset = Float(m, "_GrabRampOffset", 0f),
                    Quantize = On(m, "_GrabRampQuantize", 1f), Levels = Mathf.Max(2, Int(m, "_GrabRampLevels", 4)),
                    Dither = Float(m, "_GrabRampDither", 1f), Mask = rampMask
                };
                // A custom ramp reads its texture when it's wider than 16 texels; otherwise grey steps.
                var texture = AtlasSourceMapping.Texture(m, "_GrabRampTex", AtlasTextureInterpretation.Color);
                if (ramp.Palette == AtlasScreenRampPalette.Texture && texture != null && texture.Texture != null
                    && Mathf.Max(texture.Texture.width, texture.Texture.height) > 16)
                    ramp.Texture = texture;
                layer.Ramp = ramp;
            }

            layer.ColorMask = GrabMask(m, d, map, "_GrabColor", 4, out bool colorOff);
            if (colorOff)
            {
                layer.Tint = Color.white;
                layer.Brightness = 1f;
                layer.AddBrightness = layer.Vibrance = 0f;
                layer.Saturation = 1f;
            }
            layer.AlphaMask = GrabMask(m, d, map, "_GrabAlpha", 0, out off);
            if (off) layer.Amount = 0f;

            d.Layers.Add(layer);
            if (On(m, "_GrabPassHueShiftToggle") && !colorOff)
                Hue(m, d, AtlasAdjustmentTarget.Layer, layer, "_GrabPassHueShift", "_GrabPassHueShiftSpeed",
                    "_GrabPassHueShiftColorSpace", "_GrabPassHueSelectOrShift", layer.ColorMask);

        }

        // Pro Grab Pass's lava lamp, unlit like the grab, where the surface is see-through.
        private void LavaLamp(Material m, AtlasUnifiedMaterial d)
        {
            if (!ProFeatures || !On(m, "_InternalWaterEnable")) return;
            var lamp = new AtlasLavaLamp
            {
                SdfVolume = AtlasSourceMapping.Texture(m, "_SDFTexture", AtlasTextureInterpretation.Data),
                SdfVoxelSize = Float(m, "_SDFPixelSize", 1f),
                SdfCorner = Vector3Of(m, "_SDFLowerCorner", Vector3.zero), SdfSize = Vector3Of(m, "_SDFSize", Vector3.one),
                MinThickness = Float(m, "_MinThickness", 0.15f),
                BindData = Int(m, "_BindDataMode", 3) == 1 ? AtlasBindData.On : Int(m, "_BindDataMode", 3) == 0 ? AtlasBindData.Off : AtlasBindData.Auto,
                WorldRescale = Float(m, "_WorldRecale", 1f),
                RefractiveIndex = Float(m, "_LavaLampRefractiveIndex", 1.1f),
                NormalStrength = Float(m, "_LavaLampRefractionNormalStrength", 1f),
                Scale = Float(m, "_LavaScale0", 0.056f), Padding = Float(m, "_LavaPadding", 0.06f),
                Steps = Mathf.Clamp(Int(m, "_LavaRaymarchSteps", 64), 1, 100),
                Shape = (AtlasLavaShape)Mathf.Clamp(Int(m, "_LavaShape", 0), 0, 3),
                Smoothing = Float(m, "_LavaSmoothingFactor", 0.12f), VerticalSpacing = Float(m, "_LavaVerticalSeparation", 2f),
                SkipChance = Float(m, "_LavaSkipChance", 0.6f),
                MinSize = Float(m, "_LavaMinSize", 0.5f), MaxSize = Float(m, "_LavaMaxSize", 1f),
                SizeDistribution = Float(m, "_LavaSizeDistribution", 0f),
                MinSpeed = Float(m, "_LavaMinSpeed", 0.05f), MaxSpeed = Float(m, "_LavaMaxSpeed", 0.4f),
                MinDriftSpeed = Float(m, "_LavaMinDriftSpeed", 0.5f), MaxDriftSpeed = Float(m, "_LavaMaxDriftSpeed", 2f),
                FlowDirection = Vector3Of(m, "_LavaFlowDirection0", Vector3.up),
                Reflectiveness = Float(m, "_LavaReflectiveness", 0.15f), Roughness = Float(m, "_LavaPerceptualRoughness", 0.25f),
                Translucency = Float(m, "_LavaSubsurface", 1f),
                Matcap = On(m, "_LavaLampMatcapEnable"),
                MatcapBorder = Float(m, "_LavaLampMatcapBorder", 0.5f), MatcapIntensity = Float(m, "_LavaLampMatcapIntensity", 1f),
                MatcapAlpha = Float(m, "_LavaLampMatcapAlpha", 1f), MatcapBlend = PoiyomiBlend(Int(m, "_LavaLampMatcapBlendMode", 8)),
                TopHeight = Float(m, "_LavaTopReservoirHeight0", 1f), BottomHeight = Float(m, "_LavaBottomReservoirHeight0", 0f),
                CoreColor = TintColor(m, d, "_LavaCoreColor0", "_LavaCoreColor0ThemeIndex"),
                HazeColor = TintColor(m, d, "_LavaWaterHazeColor0", "_LavaWaterHazeColor0ThemeIndex"),
                HazeDensity = Float(m, "_LavaWaterHazeStrength0", 10f),
                TintColor = LinearColor(m, "_LavaWaterTintColor0", Color.white), TintStrength = Float(m, "_LavaWaterTintStrength0", 1f),
                TopColor = TintColor(m, d, "_LavaTopLightColor0", "_LavaTopLightColor0ThemeIndex"),
                TopEmission = Float(m, "_LavaTopLightBrightness0", 0.5f),
                BottomColor = TintColor(m, d, "_LavaBottomLightColor0", "_LavaBottomLightColor0ThemeIndex"),
                BottomEmission = Float(m, "_LavaBottomLightBrightness0", 0.5f),
                ColorRange = Float(m, "_LavaLightFalloff0", 0.8f)
            };
            if (lamp.Matcap) lamp.MatcapMap = Texture(m, d, "_LavaLampMatcap");
            var blob = AtlasMaterialReader.Vector(m, "_LavaAudioLinkBlobSize", new Vector4(1f, 1f, 0f, 0f));
            var flow = AtlasMaterialReader.Vector(m, "_LavaAudioLinkFlowSpeed", new Vector4(1f, 1f, 0f, 0f));
            var glow = AtlasMaterialReader.Vector(m, "_LavaAudioLinkEmission", new Vector4(1f, 1f, 0f, 0f));
            lamp.BlobScale = new Vector2(blob.x, blob.y);
            lamp.FlowSpeed = new Vector2(flow.x, flow.y);
            lamp.EmissionScale = new Vector2(glow.x, glow.y);
            if (AudioLinkOn(m) && On(m, "_LavaAudioLinkEnable")) lamp.AudioBand = Mathf.Clamp(Int(m, "_LavaAudioLinkBand", 0), 0, 4);
            d.Layers.Add(new AtlasPatternLayer
            {
                Kind = AtlasPatternKind.LavaLamp, LavaLamp = lamp, Placement = AtlasPlacement.Surface,
                Blend = AtlasBlend.Replace, Opacity = Float(m, "_LavaLampAlpha", 1f), Mask = Mask(m, d, "_LavaLampMask")
            });
        }

        // One effect's mask: a blend map channel (<prefix>MaskChannel, 4 off) with the map's options
        // and the effect's invert, then its global mask. Off when it's 0 everywhere (an inverted
        // channel without a map).
        private AtlasMask GrabMask(Material m, AtlasUnifiedMaterial d, AtlasTextureReference map, string prefix, int fallback, out bool off)
        {
            off = false;
            int c = Int(m, prefix + "MaskChannel", fallback);
            AtlasMask mask = null;
            if (c < 4)
            {
                bool invert = On(m, prefix + "MaskInvert");
                mask = MaskOptions(m, map == null ? null : new AtlasMask { Map = map, Channel = Channels[c] }, "_GrabPassBlendMap", c);
                if (mask != null) mask.Invert = mask.Invert != invert;
                else if (invert) off = true;
            }
            return BusMask(m, mask, prefix + "GlobalMask", prefix + "GlobalMaskBlendType");
        }
    }
}
