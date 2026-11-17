using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi 11: the Skin section, the add pass's blending, the time source, the emission and
    // final color multipliers, Early Z and the world AO blocker.
    internal sealed partial class AtlasPoiyomiReader
    {
        // After the detail texture: a skin color from pigments, its masks the skin detail map's
        // channels (R: where it doesn't apply; G: freckles; B: tan).
        private void SkinTone(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_PoiSkinEnabled")) return;
            var map = AtlasSourceMapping.Texture(m, "_PoiSkinDetailMap", AtlasTextureInterpretation.Data);
            var skin = new AtlasSkinTone
            {
                Lut = AtlasSourceMapping.Texture(m, "_PoiSkinLUT", AtlasTextureInterpretation.Color),
                Melanin = Float(m, "_PoiSkinMelanin", 0.2f), MelaninRedness = Float(m, "_PoiSkinMelaninRedness", 0.5f),
                Hemoglobin = Float(m, "_PoiSkinHemoglobin", 0.5f),
                FreckleStrength = Float(m, "_PoiSkinFreckleStrength", 0.5f), FreckleSharpness = Float(m, "_PoiSkinFreckleSharpness", 3f),
                FreckleRedness = Float(m, "_PoiSkinFreckleRedness", 0.1f),
                TanAmount = Float(m, "_PoiSkinTanAmount", 0.15f), TanRedness = Float(m, "_PoiSkinTanRednessShift", -0.1f)
            };
            if (On(m, "_PoiSkinFrecklesEnabled"))
                skin.FreckleMask = SkinChannel(m, map, 1, "_PoiSkinFreckleMapUV", "_PoiSkinFreckleMapTiling");
            if (On(m, "_PoiSkinTanEnabled"))
                skin.TanMask = SkinChannel(m, map, 2, "_PoiSkinTanMapUV", "_PoiSkinTanMapTiling");

            // Where it applies: 1 − the red channel (on UV0, the map's own tiling ignored) × the vertex color.
            AtlasMask mask = null;
            if (map != null)
            {
                map.Tiling = Vector2.one;
                map.Offset = Vector2.zero;
                mask = MaskOptions(m, new AtlasMask { Map = map, Channel = AtlasChannel.Red }, "_PoiSkinDetailMap", 0);
                mask.Levels = mask.Levels ?? new AtlasMaskLevels();
                mask.Levels.Invert = !mask.Levels.Invert;
            }
            else mask = MaskOptions(m, null, "_PoiSkinDetailMap", 0);

            var tint = TintColor(m, d, "_PoiSkinColorTint", "_PoiSkinColorTintThemeIndex");
            float brightness = Float(m, "_PoiSkinBrightness", 1f);
            d.Layers.Add(new AtlasPatternLayer
            {
                Kind = AtlasPatternKind.SkinTone, SkinTone = skin, Placement = AtlasPlacement.Surface,
                Color = new Color(tint.r * brightness, tint.g * brightness, tint.b * brightness, 1f),
                Blend = Int(m, "_PoiSkinBlendMode", 0) == 1 ? AtlasBlend.Multiply : AtlasBlend.Replace,
                Opacity = Float(m, "_PoiSkinBlend", 1f), Mask = mask
            });
        }

        // A skin detail map channel on its own UV and tiling, × its vertex color source. With
        // neither map nor source it is 0 (null: the effect is off), or 1 when inverted.
        private AtlasMask SkinChannel(Material m, AtlasTextureReference map, int channel, string uv, string tiling)
        {
            if (map != null)
            {
                var tiling4 = AtlasMaterialReader.Vector(m, tiling, new Vector4(1f, 1f, 0f, 0f));
                var own = new AtlasTextureReference
                {
                    Texture = map.Texture, Interpretation = AtlasTextureInterpretation.Data,
                    Space = AtlasTextureSpace.UV0 + Mathf.Clamp(Int(m, uv, 0), 0, 3), Tiling = new Vector2(tiling4.x, tiling4.y)
                };
                return MaskOptions(m, new AtlasMask { Map = own, Channel = Channels[channel] }, "_PoiSkinDetailMap", channel);
            }
            var vertex = MaskOptions(m, null, "_PoiSkinDetailMap", channel);
            if (vertex != null) return vertex;
            bool inverted = AtlasMaterialReader.Vector(m, "_PoiSkinDetailMapMLInvert", Vector4.zero)[channel] >= 0.5f;
            return inverted ? new AtlasMask() : null;
        }

        // The additional lights' pass: Max (Poiyomi's default) or add; other operations and factors
        // other than the preset's are reported.
        private void AddPass(Material m, AtlasUnifiedMaterial d)
        {
            var additional = d.Lighting.AdditionalLights;
            int op = Int(m, "_AddBlendOp", AtlasMaterialReader.Has(m, "_AddBlendOp") ? 4 : 0);
            if (op == 4)
            {
                additional.Combine = AtlasLightCombine.Max;
                additional.MaxAlphaBoost = Float(m, "_AlphaBoostFA", 10f);
                return;
            }
            if (op != 0)
            {
                d.Report(AtlasIssueKind.Unmapped, "render.blend-op", "A blend operation other than add isn't mapped yet.");
                return;
            }
            int preset = Int(m, "_Mode", 0);
            int source = preset == 2 || preset == 9 ? 5 : preset == 5 ? 4 : preset == 6 || preset == 7 ? 2 : 1;
            if (Int(m, "_AddSrcBlend", source) != source || Int(m, "_AddDstBlend", 1) != 1)
                d.Report(AtlasIssueKind.Unmapped, "lighting.add-pass-blend", "Additional lights' blend factors other than the preset's aren't mapped yet.");
        }

        // Poiyomi 11's global odds and ends.
        private void Globals11(Material m, AtlasUnifiedMaterial d)
        {
            // 11's default is network time; 10 has no setting (local).
            d.Clock = Int(m, "_PoiTimeSource", 0) == 1 ? AtlasClock.Network : AtlasClock.Local;
            d.Render.DepthPrepass = On(m, "_RenderingEarlyZEnabled");
            float emission = Float(m, "_PPEmissionMultiplier", 1f), final = Float(m, "_PPFinalColorMultiplier", 1f);
            if (emission != 1f)
                d.ColorAdjustments.Add(new AtlasColorAdjustment { Target = AtlasAdjustmentTarget.Emission, Operation = AtlasColorOperation.Brightness, Amount = Mathf.Max(0f, emission) });
            if (final != 1f)
                d.ColorAdjustments.Add(new AtlasColorAdjustment { Target = AtlasAdjustmentTarget.FinalColor, Operation = AtlasColorOperation.Brightness, Amount = Mathf.Max(0f, final) });
            if (On(m, "_RenderingAOBlockerEnabled"))
                d.Report(AtlasIssueKind.Note, "world.ao-blocker", "The world AO blocker is a world feature and is ignored.");
        }
    }
}
