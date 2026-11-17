using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// VRChat/Mobile/Toon Standard and its Outline variant (VRChat SDK). Each inspector section
    /// with a toggle sets a keyword; a section that's off has no effect, whatever its values. Every
    /// texture has its own tiling. Always opaque. See UNIFIED_MATERIAL_VRCHAT_MOBILE.md.
    /// </summary>
    public sealed class AtlasVRChatToonStandardParser : AtlasParserBase
    {
        public override string ParserId => "vrc-mobile-toonstandard";
        public override string DisplayName => "VRChat Mobile Toon Standard";
        public override bool CanParse(Material m) =>
            Name(m) == "VRChat/Mobile/Toon Standard" || Name(m) == "VRChat/Mobile/Toon Standard (Outline)";

        private static readonly AtlasChannel[] Channels = { AtlasChannel.Red, AtlasChannel.Green, AtlasChannel.Blue, AtlasChannel.Alpha };
        private static readonly AtlasBlend[] DetailBlends = { AtlasBlend.Replace, AtlasBlend.Add, AtlasBlend.Multiply, AtlasBlend.MultiplyDouble };
        private static readonly AtlasBlend[] AudioBlends = { AtlasBlend.Replace, AtlasBlend.Add, AtlasBlend.Multiply };
        private static readonly AtlasAudioBand[] AudioBands = { AtlasAudioBand.Bass, AtlasAudioBand.LowMids, AtlasAudioBand.HighMids, AtlasAudioBand.Highs };
        private static readonly (string Texture, AtlasRampPreset Preset)[] RampPresets =
        {
            ("ShadowRampFlat", AtlasRampPreset.Flat), ("ShadowRampRealistic", AtlasRampPreset.Realistic),
            ("ShadowRampRealisticSoft", AtlasRampPreset.RealisticSoft), ("ShadowRampRealisticVerySoft", AtlasRampPreset.RealisticVerySoft),
            ("ShadowRampToon2Band", AtlasRampPreset.TwoBands), ("ShadowRampToon3Band", AtlasRampPreset.ThreeBands),
            ("ShadowRampToon4Band", AtlasRampPreset.FourBands)
        };

        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            bool normalMaps = Keyword(m, "USE_NORMAL_MAPS");
            bool hueShift = Keyword(m, "USE_HUE_SHIFT");
            var hueMask = hueShift ? Mask(m, "_HueShiftMask", "_HueShiftMaskChannel", 1) : null;

            // Main: texture × color, optionally × vertex color; always opaque; culling with the
            // back faces' normal flipped.
            d.Surface.BaseColor.Color = AtlasMaterialReader.ColorLinear(m, "_Color", Color.white);
            d.Surface.BaseColor.Map = AtlasSourceMapping.Texture(m, "_MainTex");
            d.Surface.VertexColor.MultiplyColor = Float(m, "_VertexColor", 0f) > 0.5f;
            d.Surface.Alpha.Source = AtlasAlphaSource.None;
            int cull = Mathf.RoundToInt(Float(m, "_Culling", 2f));
            d.Render.Pass.Cull = cull == 0 ? AtlasCull.Off : cull == 1 ? AtlasCull.Front : AtlasCull.Back;
            d.Render.TwoSidedLighting = true;
            if (normalMaps)
            {
                d.Surface.Normal.Map = AtlasSourceMapping.Texture(m, "_BumpMap", AtlasTextureInterpretation.TangentNormal);
                d.Surface.Normal.Strength = Float(m, "_BumpScale", 1f);
            }
            if (hueShift) Hue(m, d, AtlasAdjustmentTarget.BaseColor, null, "_HueShift", hueMask);

            Lighting(m, d);
            Specular(m, d);

            // Occlusion darkens the ramp and the ambient light, not specular.
            var response = d.Surface.Response;
            response.OcclusionDirect = 1f;
            response.OcclusionReflections = 0f;
            if (Keyword(m, "USE_OCCLUSION_MAP"))
            {
                response.Occlusion.Map = AtlasSourceMapping.Texture(m, "_OcclusionMap", AtlasTextureInterpretation.Data);
                response.Occlusion.Channel = Channel(m, "_OcclusionMapChannel", 1);
                response.Occlusion.Constant = Float(m, "_OcclusionStrength", 1f);
            }

            // Layers, in the shader's order: color mask, detail, matcap, emission, AudioLink, rim.
            if (Keyword(m, "USE_COLOR_MASK")) ColorMask(m, d);
            if (Keyword(m, "USE_DETAIL_MAPS")) Detail(m, d, normalMaps, hueShift ? hueMask : null, hueShift);
            if (Keyword(m, "USE_MATCAP")) Matcap(m, d);
            var emission = Emission(m, d);
            if (emission != null && hueShift) Hue(m, d, AtlasAdjustmentTarget.Layer, emission, "_EmissionHueShift", hueMask);
            if (Keyword(m, "USE_AUDIOLINK")) AudioLink(m, d, hueShift || Keyword(m, "USE_COLOR_MASK"));
            if (Keyword(m, "USE_RIMLIGHT")) Rim(m, d);

            // Outline variant only.
            if (AtlasMaterialReader.Has(m, "_OutlineThickness")) Outline(m, d);
        }

        // Ramp toon: half-Lambert through the shadow ramp, flat ambient, no received shadows (the
        // shader skips the shadow variants), every light through the ramp.
        private static void Lighting(Material m, AtlasUnifiedMaterial d)
        {
            var lighting = d.Lighting;
            lighting.Family = AtlasLightingFamily.RampToon;
            lighting.Ambient = AtlasAmbientModel.Average;
            lighting.ReceiveShadows = 0f;
            lighting.Ramp.Ramp = AtlasSourceMapping.Texture(m, "_Ramp");
            lighting.Ramp.Preset = Preset(lighting.Ramp.Ramp);
            lighting.Ramp.ShadowLift = Float(m, "_ShadowBoost", 0f);
            // Shadows: base × Shadow Tint × direct light, plus the ambient light.
            float shadowTint = Float(m, "_ShadowAlbedo", 0.5f);
            lighting.Ramp.ShadowTint.Color = new Color(shadowTint, shadowTint, shadowTint, 1f);
            lighting.Ramp.ShadowColorFromBase = 1f;
            lighting.Ramp.ShadowDirect = 1f;
            lighting.Ramp.ShadowAmbient = 0f;
            lighting.Ramp.UntintedAmbient = 1f;
            lighting.Ramp.LitAmbient = 1f;
            lighting.MinimumBrightness = Float(m, "_MinBrightness", 0f);
            lighting.LimitBrightness = Float(m, "_LimitBrightness", 1f) > 0.5f;
        }

        // Specular: metallic and gloss maps (each a channel) times their strengths; reflectance is
        // also scaled by the gloss map. Off: no highlights and no reflections.
        private static void Specular(Material m, AtlasUnifiedMaterial d)
        {
            var response = d.Surface.Response;
            if (!Keyword(m, "USE_SPECULAR"))
            {
                response.Highlights.Enabled = false;
                response.Reflections.Enabled = false;
                return;
            }
            response.Metallic.Constant = Float(m, "_MetallicStrength", 0f);
            response.Metallic.Map = AtlasSourceMapping.Texture(m, "_MetallicMap", AtlasTextureInterpretation.Data);
            response.Metallic.Channel = Channel(m, "_MetallicMapChannel", 0);
            var gloss = AtlasSourceMapping.Texture(m, "_GlossMap", AtlasTextureInterpretation.Data);
            var glossChannel = Channel(m, "_GlossMapChannel", 3);
            response.Roughness.Constant = Float(m, "_GlossStrength", 0.5f);
            response.Roughness.Map = gloss;
            response.Roughness.Channel = glossChannel;
            response.Roughness.InvertResult = true;
            response.Reflectance.Constant = Float(m, "_Reflectance", 0.5f);
            response.Reflectance.Map = gloss;
            response.Reflectance.Channel = glossChannel;
            response.Highlights.Sharpness = Float(m, "_SpecularSharpness", 0f);
            // Always on in the shader (Valve's geometric roughening, here as the unified filtering).
            response.SpecularAntiAliasing.Enabled = true;
        }

        // Up to four recolor regions, one per mask channel: the grayscale albedo tinted (Multiply)
        // or added to (Additive), blended in by the color's alpha; each also adds emission.
        private static void ColorMask(Material m, AtlasUnifiedMaterial d)
        {
            var mask = AtlasSourceMapping.Texture(m, "_ColorMask", AtlasTextureInterpretation.Data);
            if (mask == null) return; // The default is black: no region.
            var blend = Float(m, "_ColorMaskBlendMode", 0f) > 0.5f ? AtlasBlend.AddToLuminance : AtlasBlend.TintLuminance;
            for (int i = 0; i < 4; ++i)
            {
                var color = AtlasMaterialReader.Color(m, "_ColorMaskColor" + (i + 1), Color.white);
                float emission = Float(m, "_ColorMaskEmissionStrength" + (i + 1), 0f);
                if (color.a <= 0f && emission <= 0f) continue;
                var layer = new AtlasSurfaceLayer { Mask = new AtlasMask { Map = mask, Channel = Channels[i] } };
                var linear = color.linear;
                layer.Source.Color = new Color(linear.r, linear.g, linear.b, 1f);
                if (color.a > 0f) layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.BaseColor, blend, color.a));
                if (emission > 0f) layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, emission));
                d.Layers.Add(layer);
            }
        }

        // Detail texture by mode, faded in by the mask (always UV0); the detail normal's strength is
        // scaled by the mask and needs normal maps on.
        private static void Detail(Material m, AtlasUnifiedMaterial d, bool normalMaps, AtlasMask hueMask, bool hueShift)
        {
            var mask = Mask(m, "_DetailMask", "_DetailMaskChannel", 3);
            var space = Float(m, "_DetailUV", 0f) > 0.5f ? AtlasTextureSpace.UV1 : AtlasTextureSpace.UV0;
            var albedo = AtlasSourceMapping.Texture(m, "_DetailAlbedoMap");
            if (albedo != null)
            {
                albedo.Space = space;
                int mode = Mathf.Clamp(Mathf.RoundToInt(Float(m, "_DetailMode", 0f)), 0, 3);
                var layer = new AtlasSurfaceLayer { Mask = mask, SourceAlphaIsOpacity = mode == 0 };
                layer.Source.Map = albedo;
                layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.BaseColor, DetailBlends[mode]));
                d.Layers.Add(layer);
                if (hueShift) Hue(m, d, AtlasAdjustmentTarget.Layer, layer, "_DetailHueShift", hueMask);
            }
            var normal = normalMaps ? AtlasSourceMapping.Texture(m, "_DetailNormalMap", AtlasTextureInterpretation.TangentNormal) : null;
            if (normal != null)
            {
                normal.Space = space;
                var layer = new AtlasSurfaceLayer { Mask = mask };
                layer.Source.Map = normal;
                layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Normal, AtlasBlend.Replace, Float(m, "_DetailNormalMapScale", 1f)));
                d.Layers.Add(layer);
            }
        }

        // A VR-stable matcap changing the albedo before lighting: added or multiplied. Its texture
        // has no tiling; without one it's white.
        private static void Matcap(Material m, AtlasUnifiedMaterial d)
        {
            var layer = new AtlasLookupLayer {
                Mask = Mask(m, "_MatcapMask", "_MatcapMaskChannel", 0),
                Placement = AtlasPlacement.Surface,
                Blend = Float(m, "_MatcapType", 0f) > 0.5f ? AtlasBlend.Multiply : AtlasBlend.Add,
                Opacity = Float(m, "_MatcapStrength", 1f)
            };
            var texture = AtlasSourceMapping.Texture(m, "_Matcap");
            if (texture != null)
            {
                texture.Space = AtlasTextureSpace.View;
                texture.Tiling = Vector2.one;
                texture.Offset = Vector2.zero;
            }
            layer.Source.Map = texture;
            d.Layers.Add(layer);
        }

        // Always active: map × color × strength, on UV0 or UV1. Null when nothing glows.
        private static AtlasSurfaceLayer Emission(Material m, AtlasUnifiedMaterial d)
        {
            Color color = EmissionColor(m);
            if (color.maxColorComponent <= 0f) return null;
            var layer = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
            layer.Source.Color = color;
            layer.Source.Map = EmissionMap(m);
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add));
            d.Layers.Add(layer);
            return layer;
        }

        private static Color EmissionColor(Material m)
        {
            var color = AtlasMaterialReader.ColorLinear(m, "_EmissionColor", Color.black) * Float(m, "_EmissionStrength", 1f);
            return new Color(color.r, color.g, color.b, 1f);
        }

        private static AtlasTextureReference EmissionMap(Material m)
        {
            var map = AtlasSourceMapping.Texture(m, "_EmissionMap");
            if (map != null) map.Space = Float(m, "_EmissionUV", 0f) > 0.5f ? AtlasTextureSpace.UV1 : AtlasTextureSpace.UV0;
            return map;
        }

        // Audio-reactive emission: an emission layer (tint × intensity, masked) whose color an
        // audio modulator scales. Pulse, Scroll and Bar are the spreads none, history and fill.
        private static void AudioLink(Material m, AtlasUnifiedMaterial d, bool emissionChangedByOtherSections)
        {
            var tint = AtlasMaterialReader.Color(m, "_ALTint", new Color(0f, 0.898f, 0.627f, 1f));
            float intensity = Float(m, "_ALIntensity", 1f);
            var linear = tint.linear * intensity;
            int blend = Mathf.Clamp(Mathf.RoundToInt(Float(m, "_ALBlendMode", 0f)), 0, 2);
            var layer = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
            layer.Source.Color = new Color(linear.r, linear.g, linear.b, 1f);
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AudioBlends[blend], Mathf.Clamp01(tint.a * intensity)));

            // Mask: the emission, or the AudioLink mask; all of RGB, or one channel.
            int maskChannel = Mathf.Clamp(Mathf.RoundToInt(Float(m, "_ALMaskChannel", 0f)), 0, 4);
            if (Float(m, "_ALMaskByEmission", 1f) > 0.5f)
            {
                var emission = EmissionColor(m);
                var map = EmissionMap(m);
                if (maskChannel == 0)
                {
                    layer.Source.Color *= emission;
                    layer.Source.Map = map;
                }
                else
                {
                    if (maskChannel < 4) layer.Source.Color *= emission[maskChannel - 1];
                    if (map != null) layer.Mask = new AtlasMask { Map = map, Channel = Channels[maskChannel - 1] };
                }
                if (emissionChangedByOtherSections)
                    d.Report(AtlasIssueKind.Unmapped, "audiolink.mask",
                        "AudioLink is masked by the emission, which hue shift or color masks also change; the mask uses the emission map and color only.");
            }
            else
            {
                var map = AtlasSourceMapping.Texture(m, "_AudioLinkMask", AtlasTextureInterpretation.Data);
                if (map != null) map.Space = UV(m, "_ALMaskUVChannel");
                if (maskChannel == 0) layer.Source.Map = map;
                else if (map != null) layer.Mask = new AtlasMask { Map = map, Channel = Channels[maskChannel - 1] };
            }
            if (Mathf.Abs(Float(m, "_ALRimMaskStrength", 0f)) > 0f)
                d.Report(AtlasIssueKind.Unmapped, "audiolink.rim-mask", "AudioLink's rim mask (a view-angle fade) isn't mapped yet.");
            d.Layers.Add(layer);

            var modulator = new AtlasModulator { Target = "layers[" + (d.Layers.Count - 1) + "].source.color" };
            var audio = modulator.Audio;
            int band = Mathf.RoundToInt(Float(m, "_ALBand", 0f));
            int mode = Mathf.RoundToInt(Float(m, "_AudioLinkMode", 0f));
            if (band > 3 || mode > 2)
                d.Report(AtlasIssueKind.Unmapped, "audiolink.uv-based",
                    "AudioLink's UV-based mode or band (chosen by the UV tile) isn't mapped yet; read as Pulse on bass.");
            audio.Band = AudioBands[Mathf.Clamp(band, 0, 3)];
            audio.Smoothing = Float(m, "_ALSmoothing", 7f) / 15f;
            audio.FallbackBeat = Float(m, "_ALEnableFallback", 0f) > 0.5f;
            audio.FallbackSpeed = Float(m, "_ALFallbackSpeed", 1f);
            audio.CoordinateUV = UV(m, "_ALEffectUVChannel");
            if (mode == 1)
            {
                // Scroll: the audio history, which has no smoothing.
                audio.Spread = AtlasAudioSpread.History;
                audio.Smoothing = 0f;
                float scale = Float(m, "_ALScrollScale", 1f);
                if (Float(m, "_ALScrollCenterOut", 0f) > 0.5f)
                {
                    // The shader reads the history at 0.5 + 0.5 × the view angle.
                    audio.Coordinate = AtlasSpreadCoordinate.ViewAngle;
                    audio.HistoryOffset = 64f * scale;
                    audio.HistoryLength = 64f * scale;
                }
                else
                {
                    audio.HistoryLength = 128f * scale;
                    if (Float(m, "_ALEffectUseMask", 0f) > 0.5f)
                    {
                        audio.Coordinate = AtlasSpreadCoordinate.TextureChannel;
                        audio.CoordinateMap = AtlasSourceMapping.Texture(m, "_AudioLinkMask", AtlasTextureInterpretation.Data);
                        if (audio.CoordinateMap != null) audio.CoordinateMap.Space = audio.CoordinateUV;
                        audio.CoordinateChannel = Channels[Mathf.Clamp(Mathf.RoundToInt(Float(m, "_ALEffectMaskChannel", 0f)), 0, 3)];
                    }
                    else audio.Coordinate = AtlasSpreadCoordinate.UVU;
                }
            }
            else if (mode == 2)
            {
                // Bar: fills up the effect UV's v.
                audio.Spread = AtlasAudioSpread.Fill;
                audio.Coordinate = AtlasSpreadCoordinate.UVV;
                audio.FillSoftness = Float(m, "_ALBarSmoothing", 0.1f);
            }
            d.Modulators.Add(modulator);
        }

        // A glowing edge, added after lighting; optionally scaled by the scene's light.
        private static void Rim(Material m, AtlasUnifiedMaterial d)
        {
            var rim = new AtlasAngleLayer {
                Width = Float(m, "_RimRange", 0.3f),
                Softness = Float(m, "_RimSharpness", 0.1f),
                BaseColorMix = Float(m, "_RimAlbedoTint", 0f),
                Opacity = Float(m, "_RimIntensity", 0.5f),
                LightResponse = Float(m, "_RimEnvironmental", 0f) > 0.5f ? AtlasLightResponse.ScaleByLight : AtlasLightResponse.IgnoreLight
            };
            var color = AtlasMaterialReader.ColorLinear(m, "_RimColor", Color.white);
            rim.Color.Color = new Color(color.r, color.g, color.b, 1f);
            d.Layers.Add(rim);
        }

        // Inverted hull: thickness × 0.01 m along the normal, times the mask (sampled on raw UV0);
        // the on-screen offset is capped at 0.05 × thickness. Unlit.
        private static void Outline(Material m, AtlasUnifiedMaterial d)
        {
            float thickness = Float(m, "_OutlineThickness", 0.05f);
            if (thickness <= 0f) return;
            var outline = new AtlasOutline { Width = thickness * 0.01f, ScreenWidthLimit = 0.05f * thickness,
                BaseColorMix = Float(m, "_OutlineFromAlbedo", 0f) };
            var mask = Mask(m, "_OutlineMask", "_OutlineMaskChannel", 0);
            if (mask != null) { mask.Map.Tiling = Vector2.one; mask.Map.Offset = Vector2.zero; }
            outline.WidthMask = mask;
            var color = AtlasMaterialReader.ColorLinear(m, "_OutlineColor", Color.black);
            outline.Color.Color = new Color(color.r, color.g, color.b, 1f);
            d.Outlines.Add(outline);
        }

        private static void Hue(Material m, AtlasUnifiedMaterial d, AtlasAdjustmentTarget target, AtlasLayer layer,
            string angleProperty, AtlasMask mask)
        {
            float radians = Float(m, angleProperty, 0f);
            if (radians == 0f) return; // The shader skips a zero angle.
            d.ColorAdjustments.Add(new AtlasColorAdjustment {
                Target = target, Layer = layer, Mask = mask, Amount = radians / (2f * Mathf.PI) });
        }

        private static AtlasRampPreset Preset(AtlasTextureReference ramp)
        {
            if (ramp == null) return AtlasRampPreset.None;
            foreach (var (texture, preset) in RampPresets)
                if (ramp.Texture.name == texture) return preset;
            return AtlasRampPreset.None;
        }

        // A mask texture with its channel option; null without a texture (the default is white).
        private static AtlasMask Mask(Material m, string texture, string channel, int defaultChannel)
        {
            var map = AtlasSourceMapping.Texture(m, texture, AtlasTextureInterpretation.Data);
            return map == null ? null : new AtlasMask { Map = map, Channel = Channel(m, channel, defaultChannel) };
        }

        private static AtlasChannel Channel(Material m, string property, int fallback) =>
            Channels[Mathf.Clamp(Mathf.RoundToInt(Float(m, property, fallback)), 0, 3)];

        private static AtlasTextureSpace UV(Material m, string property) =>
            AtlasTextureSpace.UV0 + Mathf.Clamp(Mathf.RoundToInt(Float(m, property, 0f)), 0, 3);

        private static float Float(Material m, string name, float fallback) => AtlasMaterialReader.Float(m, name, fallback);
        private static bool Keyword(Material m, string keyword) => AtlasMaterialReader.Keyword(m, keyword);
    }
}
