using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Output shaders declare what they can express and their texture layouts; the shared
    // output features decide what to produce. Registrations are discovered automatically by
    // AtlasShaderRegistry.
    public sealed class PoiyomiToon10OutputShader : IAtlasOutputShader
    {
        public string ShaderId => "poiyomi10";
        public string DisplayName => "Poiyomi Toon 10";
        public string Family => AtlasShaderFamilies.Poiyomi;
        public int Rank => 100;
        // Poiyomi 11 keeps 10's shader name; with Poiyomi Pro installed, 10 lives under Old Versions.
        public Shader FindShader()
        {
            foreach (string name in new[] { ".poiyomi/Poiyomi Toon", ".poiyomi/Old Versions/10.0/Poiyomi Toon" })
            {
                var shader = Shader.Find(name);
                if (shader == null) continue;
                int major = AtlasPoiyomiVersionDetector.DetectFromMetadata(shader.name,
                    UnityEditor.AssetDatabase.GetAssetPath(shader), AtlasPoiyomiVersionDetector.GetShaderMasterLabel(shader));
                if (major == 10) return shader;
            }
            return null;
        }
        public AtlasOutputCapabilities Capabilities { get; } = new AtlasOutputCapabilities
        {
            Textures =
            {
                new AtlasOutputTexture("BaseColor", "Base color") { SizeMode = AtlasTextureSizeMode.Main }
                    .With(AtlasVirtualMap.BaseColor, AtlasTextureChannels.RGB)
                    .With(AtlasVirtualMap.Alpha, AtlasTextureChannels.A),
                NormalTexture(),
                // Poiyomi's packed maps (_MochieMetallicMaps): R metallic, G smoothness, B reflection
                // mask, A specular mask, read as they are (multipliers 1).
                new AtlasOutputTexture("Metallic", "Metallic and smoothness") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false }
                    .With(AtlasVirtualMap.Metallic, AtlasTextureChannels.R, AtlasOutputTexture.Gray(0f))
                    .With(AtlasVirtualMap.Roughness, AtlasTextureChannels.G, AtlasOutputTexture.Gray(0f), invert: true)
                    .With(AtlasVirtualMap.ReflectionMask, AtlasTextureChannels.B, AtlasOutputTexture.Gray(0f))
                    .With(AtlasVirtualMap.SpecularMask, AtlasTextureChannels.A, AtlasOutputTexture.Gray(0f)),
                // _LightingAOMaps: occlusion in R.
                new AtlasOutputTexture("Occlusion", "Ambient occlusion") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false }
                    .With(AtlasVirtualMap.Occlusion, AtlasTextureChannels.R, AtlasOutputTexture.Gray(1f)),
                EmissionTexture()
            },
            SurfaceModes = { AtlasSurfaceMode.Cutout, AtlasSurfaceMode.Transparent, AtlasSurfaceMode.Fade },
            VRChatFallback = true,
            BlendedDepthWrite = true
        };

        // Poiyomi 10's rendering presets (_Mode), as its shader's on_value_actions set them, by
        // property: _Mode, queue, RenderType, _AlphaForceOpaque, _Cutoff, _SrcBlend, _DstBlend,
        // _AddSrcBlend, _ZWrite, _AlphaPremultiply, _OutlineSrcBlend, _OutlineDstBlend,
        // _OutlineDstBlendAlpha. The properties every preset sets alike follow in Write.
        private static readonly (AtlasSurfaceMode Mode, int Preset, int Queue, string Type, int ForceOpaque, float Cutoff,
            int Src, int Dst, int AddSrc, int ZWrite, int Premultiply, int OutlineSrc, int OutlineDst, int OutlineDstAlpha)[] Presets =
        {
            (AtlasSurfaceMode.Opaque, 0, 2000, "Opaque", 1, 0f, 1, 0, 1, 1, 0, 1, 0, 0),
            (AtlasSurfaceMode.Cutout, 1, 2450, "TransparentCutout", 0, 0.5f, 1, 0, 1, 1, 0, 1, 0, 1),
            (AtlasSurfaceMode.Fade, 2, 3000, "Transparent", 0, 0.002f, 5, 10, 5, 0, 0, 5, 10, 1),
            // TransClipping: a fade that writes depth.
            (AtlasSurfaceMode.Fade, 9, 2460, "TransparentCutout", 0, 0.01f, 5, 10, 5, 1, 0, 5, 10, 1),
            (AtlasSurfaceMode.Transparent, 3, 3000, "Transparent", 0, 0f, 1, 10, 1, 0, 1, 1, 10, 1)
        };

        // The main normal map: linear, a size keeping the main texture's aspect.
        public static AtlasOutputTexture NormalTexture() =>
            new AtlasOutputTexture("Normal", "Normal map") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false, Normal = true }
                .With(AtlasVirtualMap.Normal, AtlasTextureChannels.RGB, new Color(0f, 0f, 1f, 1f));

        // Poiyomi 10's main section: _Color (sRGB-authored, so set from the linear color's
        // gamma value) times _MainTex, whose alpha holds the baked alpha; the normal map at the
        // output's strength; then the rendering preset of the surface mode, and VRChat's fallback.
        public void Write(AtlasUnifiedMaterial output, Material material)
        {
            var baseColor = output.Surface.BaseColor;
            material.SetColor("_Color", baseColor.Color.gamma);
            material.SetTexture("_MainTex", baseColor.Map?.Texture);
            material.SetTextureScale("_MainTex", Vector2.one);
            material.SetTextureOffset("_MainTex", Vector2.zero);
            var normal = output.Surface.Normal;
            material.SetTexture("_BumpMap", normal.Map?.Texture);
            material.SetFloat("_BumpScale", normal.Map?.Texture != null ? normal.Strength : 1f);
            material.SetTextureScale("_BumpMap", Vector2.one);
            material.SetTextureOffset("_BumpMap", Vector2.zero);
            material.SetFloat("_BumpMapUV", 0);
            WriteSurfaceResponse(output.Surface.Response, material);
            WriteEmission(output, material);
            var render = output.Render;
            var preset = Array.Find(Presets, p => p.Mode == render.SurfaceMode &&
                (p.Mode != AtlasSurfaceMode.Fade || (p.ZWrite == 1) == render.Pass.DepthWrite));
            if (preset.Type == null) preset = Presets[0];
            material.SetFloat("_Mode", preset.Preset);
            material.SetOverrideTag("RenderType", preset.Type);
            material.SetFloat("_AlphaForceOpaque", preset.ForceOpaque);
            material.SetFloat("_Cutoff", preset.Mode == AtlasSurfaceMode.Cutout ? render.Cutoff : preset.Cutoff);
            material.SetFloat("_BlendOp", 0);
            material.SetFloat("_BlendOpAlpha", 4);
            material.SetFloat("_SrcBlend", preset.Src);
            material.SetFloat("_DstBlend", preset.Dst);
            material.SetFloat("_SrcBlendAlpha", 1);
            material.SetFloat("_DstBlendAlpha", 1);
            material.SetFloat("_AddSrcBlend", preset.AddSrc);
            material.SetFloat("_AddDstBlend", 1);
            material.SetFloat("_AddSrcBlendAlpha", 0);
            material.SetFloat("_AddDstBlendAlpha", 1);
            material.SetFloat("_AlphaToCoverage", preset.Mode == AtlasSurfaceMode.Cutout && render.AlphaToCoverage ? 1 : 0);
            material.SetFloat("_ZWrite", preset.ZWrite);
            material.SetFloat("_ZTest", 4);
            material.SetFloat("_AlphaPremultiply", preset.Premultiply);
            material.SetFloat("_OutlineSrcBlend", preset.OutlineSrc);
            material.SetFloat("_OutlineDstBlend", preset.OutlineDst);
            material.SetFloat("_OutlineSrcBlendAlpha", 1);
            material.SetFloat("_OutlineDstBlendAlpha", preset.OutlineDstAlpha);
            material.SetFloat("_OutlineBlendOp", 0);
            material.SetFloat("_OutlineBlendOpAlpha", 4);
            material.renderQueue = render.Queue >= 0 ? render.Queue : preset.Queue;
            material.SetOverrideTag("VRCFallback", Fallback(render));
        }

        // The emission map, sRGB, a size keeping the main texture's aspect.
        public static AtlasOutputTexture EmissionTexture() =>
            new AtlasOutputTexture("Emission", "Emission") { SizeMode = AtlasTextureSizeMode.MainAspect }
                .With(AtlasVirtualMap.Emission, AtlasTextureChannels.RGB, new Color(0f, 0f, 0f, 1f));

        // Emission 0 (_EnableEmission with its _EMISSION keyword): _EmissionColor (HDR, set from
        // the linear color's gamma value) × _EmissionMap × _EmissionStrength (0–20; above that the
        // rest goes into the color).
        private static void WriteEmission(AtlasUnifiedMaterial output, Material material)
        {
            var emission = AtlasEmissionFeature.Of(output);
            var map = emission?.Source.Map?.Texture;
            material.SetFloat("_EnableEmission", map != null ? 1 : 0);
            if (map == null)
            {
                material.DisableKeyword("_EMISSION");
                return;
            }
            material.EnableKeyword("_EMISSION");
            float total = emission.Targets.First(t => t.Target == AtlasSurfaceTarget.Emission).Opacity;
            float strength = Mathf.Min(total, 20f);
            float rest = total / Mathf.Max(strength, 1e-4f);
            material.SetTexture("_EmissionMap", map);
            material.SetTextureScale("_EmissionMap", Vector2.one);
            material.SetTextureOffset("_EmissionMap", Vector2.zero);
            material.SetFloat("_EmissionMapUV", 0);
            material.SetColor("_EmissionColor", new Color(rest, rest, rest, 1f).gamma);
            material.SetFloat("_EmissionStrength", strength);
        }

        // Reflections & Specular (MOCHIE_PBR) from the packed map: metallic = R, smoothness = G,
        // the reflection and specular masks B and A × their strengths; and the AO maps
        // (PROP_LIGHTINGAOMAPS): occlusion = lerp(1, R, strength). Thry sets these keywords from
        // its inspector; a written material sets them itself.
        private static void WriteSurfaceResponse(AtlasSurfaceResponse response, Material material)
        {
            var packed = response.Metallic.Map?.Texture;
            material.SetFloat("_MochieBRDF", packed != null ? 1 : 0);
            if (packed != null)
            {
                material.EnableKeyword("MOCHIE_PBR");
                material.SetTexture("_MochieMetallicMaps", packed);
                material.SetTextureScale("_MochieMetallicMaps", Vector2.one);
                material.SetTextureOffset("_MochieMetallicMaps", Vector2.zero);
                material.SetFloat("_MochieMetallicMapsUV", 0);
                material.SetFloat("_MochieMetallicMultiplier", 1);
                material.SetFloat("_MochieRoughnessMultiplier", 1);
                material.SetFloat("_MochieMetallicMapsMetallicChannel", 0);
                material.SetFloat("_MochieMetallicMapsRoughnessChannel", 1);
                material.SetFloat("_MochieMetallicMapsReflectionMaskChannel", response.Reflections.Mask != null ? 2 : 4);
                material.SetFloat("_MochieMetallicMapsSpecularMaskChannel", response.Highlights.Mask != null ? 3 : 4);
                material.SetFloat("_MochieMetallicMapInvert", 0);
                material.SetFloat("_MochieRoughnessMapInvert", 0);
                material.SetFloat("_MochieReflectionMaskInvert", 0);
                material.SetFloat("_MochieSpecularMaskInvert", 0);
                material.SetFloat("_MochieReflectionStrength", response.Reflections.Enabled ? Mathf.Clamp01(response.Reflections.Strength) : 0f);
                material.SetFloat("_MochieSpecularStrength", response.Highlights.Enabled ? Mathf.Clamp(response.Highlights.Strength, 0f, 5f) : 0f);
            }
            else material.DisableKeyword("MOCHIE_PBR");

            var occlusion = response.Occlusion.Map?.Texture;
            material.SetTexture("_LightingAOMaps", occlusion);
            if (occlusion != null)
            {
                material.EnableKeyword("PROP_LIGHTINGAOMAPS");
                material.SetTextureScale("_LightingAOMaps", Vector2.one);
                material.SetTextureOffset("_LightingAOMaps", Vector2.zero);
                material.SetFloat("_LightingAOMapsUV", 0);
                material.SetFloat("_LightDataAOStrengthR", Mathf.Clamp01(response.Occlusion.Constant));
                material.SetFloat("_LightDataAOStrengthG", 0);
                material.SetFloat("_LightDataAOStrengthB", 0);
                material.SetFloat("_LightDataAOStrengthA", 0);
            }
            else material.DisableKeyword("PROP_LIGHTINGAOMAPS");
        }

        // VRChat's fallback tag, as Thry's editor writes it: a shader type, the render type
        // (none for opaque) and DoubleSided when nothing is culled; or Hidden. A fade that writes
        // depth falls back to cutout, which keeps its solid parts solid.
        public static string Fallback(AtlasRender render)
        {
            if (render.Fallback == AtlasFallbackShader.Hidden) return "Hidden";
            string type = render.Fallback == AtlasFallbackShader.Unlit ? "Unlit"
                : render.Fallback == AtlasFallbackShader.Standard ? "Standard" : "Toon";
            string mode = render.SurfaceMode == AtlasSurfaceMode.Cutout || render.SurfaceMode == AtlasSurfaceMode.Dithered ? "Cutout"
                : render.SurfaceMode == AtlasSurfaceMode.Fade ? (render.Pass.DepthWrite ? "Cutout" : "Fade")
                : render.SurfaceMode == AtlasSurfaceMode.Transparent ? "Transparent" : "";
            return type + mode + (render.Pass.Cull == AtlasCull.Off ? "DoubleSided" : "");
        }
    }
    public sealed class VRChatMobileToonOutputShader : IAtlasOutputShader
    {
        public string ShaderId => "mobiletoon";
        public string DisplayName => "VRChat Mobile Toon Standard";
        public string Family => AtlasShaderFamilies.VRChat;
        public int Rank => 10;
        public Shader FindShader() => Shader.Find("VRChat/Mobile/Toon Standard");
        public AtlasOutputCapabilities Capabilities { get; } = new AtlasOutputCapabilities
        {
            Textures =
            {
                new AtlasOutputTexture("BaseColor", "Base color") { SizeMode = AtlasTextureSizeMode.Main }
                    .With(AtlasVirtualMap.BaseColor, AtlasTextureChannels.RGB),
                PoiyomiToon10OutputShader.NormalTexture(),
                // One texture for _MetallicMap (R), _GlossMap (G, smoothness) and _OcclusionMap (B).
                new AtlasOutputTexture("Surface", "Metallic, gloss and occlusion") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false }
                    .With(AtlasVirtualMap.Metallic, AtlasTextureChannels.R, AtlasOutputTexture.Gray(0f))
                    .With(AtlasVirtualMap.Roughness, AtlasTextureChannels.G, AtlasOutputTexture.Gray(0f), invert: true)
                    .With(AtlasVirtualMap.Occlusion, AtlasTextureChannels.B, AtlasOutputTexture.Gray(1f)),
                PoiyomiToon10OutputShader.EmissionTexture()
            },
            Reflectance = AtlasReflectanceSupport.FollowsSmoothness
        };

        // Toon Standard: _Color (sRGB-authored) times _MainTex; the normal map, behind its
        // USE_NORMAL_MAPS keyword. It's opaque only, and a VRChat shader, so it needs no fallback.
        public void Write(AtlasUnifiedMaterial output, Material material)
        {
            var baseColor = output.Surface.BaseColor;
            material.SetColor("_Color", baseColor.Color.gamma);
            material.SetTexture("_MainTex", baseColor.Map?.Texture);
            material.SetTextureScale("_MainTex", Vector2.one);
            material.SetTextureOffset("_MainTex", Vector2.zero);
            var normal = output.Surface.Normal;
            if (normal.Map?.Texture != null)
            {
                material.EnableKeyword("USE_NORMAL_MAPS");
                material.SetTexture("_BumpMap", normal.Map.Texture);
                material.SetFloat("_BumpScale", normal.Strength);
                material.SetTextureScale("_BumpMap", Vector2.one);
                material.SetTextureOffset("_BumpMap", Vector2.zero);
            }
            else material.DisableKeyword("USE_NORMAL_MAPS");

            // USE_SPECULAR: metallic = _MetallicMap's channel × strength, smoothness = _GlossMap's ×
            // strength, reflectance = gloss × _Reflectance. USE_OCCLUSION_MAP: lerp(1, channel, strength).
            var response = output.Surface.Response;
            var surface = response.Metallic.Map?.Texture;
            if (surface != null)
            {
                material.EnableKeyword("USE_SPECULAR");
                material.SetTexture("_MetallicMap", surface);
                material.SetFloat("_MetallicMapChannel", 0);
                material.SetFloat("_MetallicStrength", 1);
                material.SetTexture("_GlossMap", response.Roughness.Map?.Texture ?? surface);
                material.SetFloat("_GlossMapChannel", 1);
                material.SetFloat("_GlossStrength", 1);
                material.SetFloat("_Reflectance", Mathf.Clamp01(response.Reflectance.Constant));
            }
            else material.DisableKeyword("USE_SPECULAR");
            var occlusion = response.Occlusion.Map?.Texture;
            if (occlusion != null)
            {
                material.EnableKeyword("USE_OCCLUSION_MAP");
                material.SetTexture("_OcclusionMap", occlusion);
                material.SetFloat("_OcclusionMapChannel", 2);
                material.SetFloat("_OcclusionStrength", Mathf.Clamp01(response.Occlusion.Constant));
            }
            else material.DisableKeyword("USE_OCCLUSION_MAP");

            // Emission: _EmissionColor (set from the linear color's gamma value) × _EmissionStrength
            // (0–2; above that the rest goes into the color) × _EmissionMap on UV0.
            var emission = AtlasEmissionFeature.Of(output);
            var emissionMap = emission?.Source.Map?.Texture;
            material.SetTexture("_EmissionMap", emissionMap);
            if (emissionMap != null)
            {
                float total = emission.Targets.First(t => t.Target == AtlasSurfaceTarget.Emission).Opacity;
                float strength = Mathf.Min(total, 2f);
                float rest = total / Mathf.Max(strength, 1e-4f);
                material.SetColor("_EmissionColor", new Color(rest, rest, rest, 1f).gamma);
                material.SetFloat("_EmissionStrength", strength);
                material.SetFloat("_EmissionUV", 0);
                material.SetTextureScale("_EmissionMap", Vector2.one);
                material.SetTextureOffset("_EmissionMap", Vector2.zero);
            }
            else material.SetColor("_EmissionColor", Color.black);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }
    }
}
