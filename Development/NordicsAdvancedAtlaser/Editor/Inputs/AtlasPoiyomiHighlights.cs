using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: Anisotropics (strand highlights) and Stylized Reflections (toon highlights) as
    // highlight layers. Stylized Reflections' lilToon mode is reported.
    public abstract partial class AtlasPoiyomiParser
    {
        private static void HighlightLayers(Material m, AtlasUnifiedMaterial d)
        {
            Anisotropics(m, d);
            StylizedReflections(m, d);
        }

        // Two strand lobes, added after lighting and replacing the base colour by weights: one layer each.
        private static void Anisotropics(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_EnableAniso")) return;
            int from = d.Layers.Count;
            var map = Texture(m, d, "_AnisoColorMap");
            foreach (var (property, blend, placement, fallback) in new[]
            {
                ("_AnisoAdd", AtlasBlend.Add, AtlasPlacement.Overlay, 1f), ("_AnisoReplace", AtlasBlend.Replace, AtlasPlacement.Surface, 0f)
            })
            {
                float opacity = Float(m, property, fallback);
                if (opacity <= 0f) continue;
                var layer = new AtlasHighlightLayer
                {
                    Kind = AtlasHighlightKind.Strand,
                    Placement = placement,
                    Blend = blend,
                    Opacity = opacity,
                    HideInShadow = Float(m, "_AnisoHideInShadow", 1f),
                    FadeByLightDirection = Float(m, "_AnisoFadeByLightDirection", 0f),
                    LightColorMix = Float(m, "_AnisoUseLightColor", 1f),
                    BaseColorMix = Float(m, "_AnisoUseBaseColor", 0f),
                    SurfaceNormalAmount = Float(m, "_AnisoNormalBlend", 1f)
                };
                layer.Color.Map = map;
                for (int i = 0; i < 2; i++)
                {
                    string p = "_Aniso" + i;
                    layer.Lobes.Add(new AtlasHighlightLobe
                    {
                        Strength = Float(m, p + "Strength", 1f),
                        // The shader raises to power × 1000.
                        Exponent = Float(m, p + "Power", i == 0 ? 0f : 0.1f) * 1000f,
                        Offset = Float(m, p + "Offset", 0f),
                        OffsetMapStrength = Float(m, p + "OffsetMapStrength", 0f),
                        // The binormal unless switched to the tangent.
                        Bitangent = !On(m, p + "SwitchDirection"),
                        EvenWidth = On(m, p + "EvenWidth"),
                        Tint = TintColor(m, d, p + "Tint", p + "TintIndex"),
                        Stylised = On(m, p + "ToonMode"),
                        Edge = Float(m, p + "Edge", 0.5f),
                        Blur = Float(m, p + "Blur", 0f)
                    });
                }
                d.Layers.Add(layer);
            }
            BusLayers(m, d, from, "_AnisoGlobalMask");
        }

        // A two-size toon highlight (UnityChan style); lilToon mode isn't mapped yet.
        private static void StylizedReflections(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_StylizedSpecular")) return;
            if (Int(m, "_StylizedReflectionMode", 0) == 1)
            {
                d.Report(AtlasIssueKind.Unmapped, "stylized.liltoon", "Stylized Reflections in lilToon mode aren't mapped yet.");
                return;
            }
            int blend = Int(m, "_Is_BlendAddToHiColor", 0);
            var layer = new AtlasHighlightLayer
            {
                Kind = AtlasHighlightKind.Toon,
                // Add goes after lighting; the others blend into the base colour.
                Placement = blend == 1 ? AtlasPlacement.Overlay : AtlasPlacement.Surface,
                Blend = blend == 1 ? AtlasBlend.Add : blend == 2 ? AtlasBlend.Screen : blend == 3 ? AtlasBlend.Multiply : AtlasBlend.Replace,
                Realistic = Int(m, "_Is_SpecularToHighColor", 0) == 1,
                Strength = Float(m, "_StylizedSpecularStrength", 1f),
                LightColorMix = On(m, "_UseLightColor", 1f) ? 1f : 0f,
                SurfaceNormalAmount = Float(m, "_StylizedSpecularNormalStrength", 1f),
                ShowOnBack = On(m, "_StylizedSpecularIgnoreNormal"),
                IgnoreShadows = On(m, "_StylizedSpecularIgnoreShadow"),
                IgnoreReceivedShadows = On(m, "_SSIgnoreCastedShadows")
            };
            layer.Color.Color = TintColor(m, d, "_HighColor", "_HighColorThemeIndex", layer.Color);
            layer.Color.Map = Texture(m, d, "_HighColor_Tex");
            layer.Mask = Mask(m, d, "_Set_HighColorMask", Channels[Mathf.Clamp(Int(m, "_Set_HighColorMaskChannel", 1), 0, 3)]);
            if (layer.Mask != null) layer.Mask.Invert = On(m, "_StylizedSpecularInvertMask");
            if (Float(m, "_Tweak_HighColorMaskLevel", 0f) != 0f)
                d.Report(AtlasIssueKind.Unmapped, "stylized.mask-level", "A stylized highlight mask's level isn't mapped yet.");
            layer.Lobes.Add(new AtlasHighlightLobe { Size = Float(m, "_HighColor_Power", 0.2f), Feather = Float(m, "_StylizedSpecularFeather", 0f), Strength = Float(m, "_Layer1Strength", 1f) });
            layer.Lobes.Add(new AtlasHighlightLobe { Size = Float(m, "_Layer2Size", 0f), Feather = Float(m, "_StylizedSpecular2Feather", 0f), Strength = Float(m, "_Layer2Strength", 0f) });
            d.Layers.Add(layer);
        }
    }
}
