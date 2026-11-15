using System;
using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Output shader modules declare their own settings and generator. Settings are specific to
    // each output shader and are added as its writer is built: overriding texture resolutions,
    // turning off features, and controlling how features are emulated.
    // Registrations are discovered automatically by AtlasShaderRegistry.
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
        public IReadOnlyList<AtlasOutputField> Fields => Array.Empty<AtlasOutputField>();
        public void AutoConfigure(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputConfiguration output) { }
        public void OnConfigurationChanged(AtlasOutputConfiguration config, AtlasConfigurationChange change) { }
        public void Prepare(AtlasGeneratorJob job)
        {
            // Not generated yet: the writer will build the material from job.Sources[n].Material
            // (unified) and job.Sources[n].Bakes.
        }
    }
    public sealed class VRChatMobileToonOutputShader : IAtlasOutputShader
    {
        public string ShaderId => "mobiletoon";
        public string DisplayName => "VRChat Mobile Toon Standard";
        public Shader FindShader() => Shader.Find("VRChat/Mobile/Toon Standard");
        public IReadOnlyList<AtlasOutputField> Fields => Array.Empty<AtlasOutputField>();
        public void AutoConfigure(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputConfiguration output) { }
        public void OnConfigurationChanged(AtlasOutputConfiguration config, AtlasConfigurationChange change) { }
        public void Prepare(AtlasGeneratorJob job)
        {
            // Not generated yet.
        }
    }
}
