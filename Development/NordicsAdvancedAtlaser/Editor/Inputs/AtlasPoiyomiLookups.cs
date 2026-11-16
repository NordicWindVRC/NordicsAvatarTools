using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: Matcaps (4) and CubeMap, as lookup layers.
    internal sealed partial class AtlasPoiyomiReader
    {
        private static readonly AtlasLookupProjection[] MatcapProjections =
        {
            AtlasLookupProjection.ViewCorrected, AtlasLookupProjection.ViewStabilised, AtlasLookupProjection.ReflectionPlanar,
            AtlasLookupProjection.Gradient, AtlasLookupProjection.LightHalfVector
        };

        private void LookupLayers(Material m, AtlasUnifiedMaterial d)
        {
            for (int i = 0; i < 4; i++) Matcap(m, d, i);
            CubeMap(m, d);
        }

        // A matcap blends by weights in a fixed order (replace, multiply, add, screen, unlit add,
        // mixed): one layer per weight in use, sharing the source and mask.
        private void Matcap(Material m, AtlasUnifiedMaterial d, int index)
        {
            string p = index == 0 ? "_Matcap" : "_Matcap" + (index + 1);
            string ordinal = new[] { "", "2nd", "3rd", "4th" }[index];
            string circle = index == 0 ? "_Matcap0" : p;
            if (!On(m, p + "Enable")) return;
            int from = d.Layers.Count;

            var map = AtlasSourceMapping.Texture(m, p);
            var tiling = Vector2.one * 2f * Float(m, p + "Border", 0.5f);
            if (map != null)
            {
                // The matcap's own tiling and offset apply to the lookup coordinate.
                tiling = Vector2.Scale(tiling, map.Tiling);
                map.Space = AtlasTextureSpace.View;
                map.Tiling = Vector2.one;
                map.Scroll = (Vector2)AtlasMaterialReader.Vector(m, p + "Pan", Vector4.zero) * 0.05f;
            }
            var color = TintColor(m, d, p + "Color", p + "ColorThemeIndex") * Float(m, p + "Intensity", 1f);
            int mode = Mathf.Clamp(Int(m, p + "UVMode", 1), 0, 4);
            var mask = Mask(m, d, p + "Mask", Channels[Mathf.Clamp(Int(m, p + "MaskChannel", 0), 0, 3)]);
            if (mask != null) mask.Invert = On(m, p + "MaskInvert");
            mask = BusMask(m, mask, p + "MaskGlobalMask");

            var weights = new (string Property, AtlasBlend Blend, AtlasPlacement Placement, float Fallback)[]
            {
                ("Replace", AtlasBlend.Replace, AtlasPlacement.Surface, 1f), ("Multiply", AtlasBlend.Multiply, AtlasPlacement.Surface, 0f),
                ("Add", AtlasBlend.Add, AtlasPlacement.Surface, 0f), ("Screen", AtlasBlend.Screen, AtlasPlacement.Surface, 0f),
                ("AddToLight", AtlasBlend.Add, AtlasPlacement.Overlay, 0f), ("Mixed", AtlasBlend.Mixed, AtlasPlacement.Surface, 0f)
            };
            float emission = Float(m, p + "EmissionStrength", 0f);
            bool first = true;
            foreach (var weight in weights)
            {
                float opacity = Float(m, p + weight.Property, weight.Fallback);
                // A matcap that only glows still needs one layer to carry its emission.
                bool last = weight.Property == "Mixed";
                if (opacity <= 0f && !(last && first && emission > 0f)) continue;
                var layer = new AtlasLookupLayer
                {
                    Projection = MatcapProjections[mode],
                    Mask = mask,
                    Placement = weight.Placement,
                    Blend = weight.Blend,
                    Opacity = opacity,
                    SourceAlphaIsOpacity = true,
                    Tiling = tiling,
                    LookupRotation = Float(m, p + "Rotation", 0f) * 180f,
                    UVBlend = (Vector2)AtlasMaterialReader.Vector(m, index == 0 ? "_MatCapBlendUV1" : "_MatCap" + ordinal + "BlendUV1", Vector4.zero),
                    UVBlendSpace = UVSpace(m, d, p + "UVToBlend", 1),
                    MirrorFlip = mode != 3,
                    CentreEye = Float(m, index == 0 ? "_MatCapVRParallaxStrength" : "_MatCap" + ordinal + "VRParallaxStrength", 1f) < 0.5f,
                    SurfaceNormalAmount = Float(m, p + "Normal", 1f),
                    BaseColorMix = Float(m, p + "BaseColorMix", 0f),
                    LightColorMix = Float(m, p + "LightColorMix", 0f),
                    HideWithReflectionProbes = On(m, p + "HideWithReflProbe"),
                    OnOutlines = On(m, p + "ShowInOutline"),
                    HideInShadow = Float(m, p + "LightMask", 0f),
                    CircleMask = On(m, circle + "CircleMaskEnabled"),
                    CircleRadius = Float(m, circle + "CircleMaskBorder", 0.45f),
                    CircleBlur = Float(m, circle + "CircleMaskBlur", 0.1f),
                    // Mip blur as roughness: 1 − smoothness.
                    Blur = 1f - Float(m, p + "Smoothness", 1f),
                    // The emission goes with the first layer.
                    EmissionAmount = first ? emission : 0f
                };
                layer.Source.Map = map;
                layer.Source.Color = color;
                if (On(m, "_Matcap" + index + "CustomNormal"))
                {
                    layer.NormalMap = Texture(m, d, "_Matcap" + index + "NormalMap", AtlasTextureInterpretation.TangentNormal);
                    layer.NormalStrength = Float(m, "_Matcap" + index + "NormalMapScale", 1f);
                }
                d.Layers.Add(layer);
                first = false;
            }

            float parallax = Float(m, index == 0 ? "_MatCapVRParallaxStrength" : "_MatCap" + ordinal + "VRParallaxStrength", 1f);
            if (parallax > 0f && parallax < 1f)
                d.Report(AtlasIssueKind.Unmapped, "matcap.vr-parallax", "A matcap's VR parallax between the eyes and their centre isn't mapped yet; the nearer is used.");
            if (On(m, p + "MaskSmoothnessApply"))
                d.Report(AtlasIssueKind.Unmapped, "matcap.smoothness-mask", "A matcap's smoothness from its mask isn't mapped yet.");
            if (Float(m, p + "AlphaOverride", 0f) > 0f || On(m, p + "ApplyToAlphaEnabled"))
                d.Report(AtlasIssueKind.Unmapped, "matcap.alpha", "A matcap's alpha override and intensity to alpha aren't mapped yet.");
            MatcapAudioLink(m, d, index, p, from);
            LayerHue(m, d, from, p + "HueShift");
            if (On(m, p + "TPSDepthEnabled"))
                d.Report(AtlasIssueKind.Unsupported, "tps", "TPS depth masks aren't supported.");
        }

        // A cubemap blended into the base colour: replace, multiply or add, by its alpha × blend amount.
        private void CubeMap(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_CubeMapEnabled")) return;
            int mode = Mathf.Clamp(Int(m, "_CubeMapUVMode", 1), 0, 3);
            int blend = Int(m, "_CubemapBlendType", 0);
            var layer = new AtlasLookupLayer
            {
                Projection = new[] { AtlasLookupProjection.ViewDirection, AtlasLookupProjection.Reflection,
                    AtlasLookupProjection.WorldNormal, AtlasLookupProjection.ObjectNormal }[mode],
                Blend = blend == 1 ? AtlasBlend.Multiply : blend == 2 ? AtlasBlend.Add : AtlasBlend.Replace,
                Opacity = Float(m, "_CubeMapBlendAmount", 1f),
                SourceAlphaIsOpacity = true,
                EmissionAmount = Float(m, "_CubeMapEmissionStrength", 0f),
                HideInShadow = Float(m, "_CubeMapLightMask", 0f),
                Blur = 1f - Float(m, "_CubeMapSmoothness", 1f),
                // Poiyomi 11: the material's smoothness, with Reflections & Specular.
                BlurFromRoughness = On(m, "_CubeMapUseMaterialSmoothness") && On(m, "_MochieBRDF"),
                SurfaceNormalAmount = mode == 2 ? Float(m, "_CubeMapWorldNormalsStrength", 1f) : Int(m, "_CubeMapNormal", 1)
            };
            layer.Mask = Mask(m, d, "_CubeMapMask", Channels[Mathf.Clamp(Int(m, "_CubeMapMaskChannel", 0), 0, 3)]);
            if (layer.Mask != null) layer.Mask.Invert = On(m, "_CubeMapMaskInvert");
            var color = TintColor(m, d, "_CubeMapColor", "_CubeMapColorThemeIndex") * Float(m, "_CubeMapIntensity", 1f);
            layer.Source.Map = AtlasSourceMapping.Texture(m, "_CubeMap");
            // Without a cubemap the shader uses grey at half alpha.
            if (layer.Source.Map == null) color = new Color(color.r * 0.21763764f, color.g * 0.21763764f, color.b * 0.21763764f, 0.5f);
            layer.Source.Color = color;
            // Poiyomi turns the yz, xz and then xy planes; the layer turns xy, xz, yz. One axis maps exactly.
            var rotation = (Vector3)AtlasMaterialReader.Vector(m, "_CubeMapRotation", Vector4.zero);
            var spin = (Vector3)AtlasMaterialReader.Vector(m, "_CubeMapRotationPan", Vector4.zero);
            layer.Rotation = new Vector3(rotation.z, rotation.y, rotation.x);
            layer.Spin = new Vector3(spin.z, spin.y, spin.x);
            int axes = 0;
            for (int a = 0; a < 3; a++) if (rotation[a] != 0f || spin[a] != 0f) axes++;
            if (axes > 1)
                d.Report(AtlasIssueKind.Unmapped, "cubemap.rotation-order", "A cubemap turned around more than one axis is read in a different order.");
            d.Layers.Add(layer);
            // Hue, then brightness (added), contrast and saturation (its own luminance weights).
            if (On(m, "_CubeMapHueShiftEnabled"))
            {
                LayerHue(m, d, d.Layers.Count - 1, "_CubeMapHueShift");
                float brightness = Float(m, "_CubeMapBrightness", 0f), contrast = Float(m, "_CubeMapContrast", 1f), saturation = Float(m, "_CubeMapSaturation", 1f);
                if (brightness != 0f) LayerAdjust(d, layer, AtlasColorOperation.Lightness, brightness);
                if (contrast != 1f) LayerAdjust(d, layer, AtlasColorOperation.Contrast, contrast);
                if (saturation != 1f) LayerAdjust(d, layer, AtlasColorOperation.Saturation, saturation, new Vector3(0.3086f, 0.6094f, 0.082f));
            }
            layer.Mask = BusMask(m, layer.Mask, "_CubeMapMaskGlobalMask");
        }
    }
}
