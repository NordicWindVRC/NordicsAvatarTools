using System;
using System.Collections.Generic;
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
                    .With(AtlasVirtualMap.Alpha, AtlasTextureChannels.A)
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

        // Poiyomi 10's main section: _Color (sRGB-authored, so set from the linear color's
        // gamma value) times _MainTex, whose alpha holds the baked alpha; then the rendering
        // preset of the surface mode, and VRChat's fallback.
        public void Write(AtlasUnifiedMaterial output, Material material)
        {
            var baseColor = output.Surface.BaseColor;
            material.SetColor("_Color", baseColor.Color.gamma);
            material.SetTexture("_MainTex", baseColor.Map?.Texture);
            material.SetTextureScale("_MainTex", Vector2.one);
            material.SetTextureOffset("_MainTex", Vector2.zero);
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
                    .With(AtlasVirtualMap.BaseColor, AtlasTextureChannels.RGB)
            }
        };

        // Toon Standard: _Color (sRGB-authored) times _MainTex. It's opaque only, and a VRChat
        // shader, so it needs no fallback.
        public void Write(AtlasUnifiedMaterial output, Material material)
        {
            var baseColor = output.Surface.BaseColor;
            material.SetColor("_Color", baseColor.Color.gamma);
            material.SetTexture("_MainTex", baseColor.Map?.Texture);
            material.SetTextureScale("_MainTex", Vector2.one);
            material.SetTextureOffset("_MainTex", Vector2.zero);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }
    }
}
