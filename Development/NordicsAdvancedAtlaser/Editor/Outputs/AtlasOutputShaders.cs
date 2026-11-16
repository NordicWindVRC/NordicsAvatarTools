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
                new AtlasOutputTexture("BaseColor", "Base colour") { SizeMode = AtlasTextureSizeMode.Main }
                    .With(AtlasVirtualMap.BaseColor, AtlasTextureChannels.RGB)
            }
        };

        // Poiyomi 10's main section: _Color (sRGB-authored, so set from the linear colour's
        // gamma value) times _MainTex. A new material is opaque, in the Geometry queue.
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
    public sealed class VRChatMobileToonOutputShader : IAtlasOutputShader
    {
        public string ShaderId => "mobiletoon";
        public string DisplayName => "VRChat Mobile Toon Standard";
        public Shader FindShader() => Shader.Find("VRChat/Mobile/Toon Standard");
        public AtlasOutputCapabilities Capabilities { get; } = new AtlasOutputCapabilities
        {
            Textures =
            {
                new AtlasOutputTexture("BaseColor", "Base colour") { SizeMode = AtlasTextureSizeMode.Main }
                    .With(AtlasVirtualMap.BaseColor, AtlasTextureChannels.RGB)
            }
        };

        // Toon Standard: _Color (sRGB-authored) times _MainTex. It's opaque only.
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
