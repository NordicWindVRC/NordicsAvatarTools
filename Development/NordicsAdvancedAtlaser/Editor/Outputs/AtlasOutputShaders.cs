using System;
using System.Collections.Generic;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Shader-specific output modules implement ONLY their UI schema and their generator.
    // Registrations are discovered automatically by AtlasShaderRegistry.
    public sealed class PoiyomiToon10OutputShader : IAtlasOutputShader
    {
        public string ShaderId => "poiyomi10";
        public string DisplayName => "Poiyomi Toon 10";
        private static readonly AtlasOutputField[] Schema = {
            AtlasOutputField.Section("Base Maps"),
            AtlasOutputField.Texture("normal", "Normal", AtlasTextureSizeMode.MasterAspect),
            AtlasOutputField.Texture("emission", "Emission", AtlasTextureSizeMode.MasterAspect),
            AtlasOutputField.Texture("globalMasks", "Global Masks", AtlasTextureSizeMode.MasterAspect),
            AtlasOutputField.Section("Additional Controls (optional)"),
            new AtlasOutputField("tint", "Base Tint", AtlasSettingKind.Color, "Tint to apply when material generation is implemented."),
            new AtlasOutputField("emissionStrength", "Emission Strength", AtlasSettingKind.Float) { DefaultFloat = 1f },
        };
        public IReadOnlyList<AtlasOutputField> Fields => Schema;
        public void AutoConfigure(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputConfiguration output)
        {
            AtlasOutputAutoSetup.SetTextureForSemantic(output, "normal", sources, AtlasTextureSemantic.Normal);
            AtlasOutputAutoSetup.SetTextureForSemantic(output, "emission", sources, AtlasTextureSemantic.Emission);
            AtlasOutputAutoSetup.SetTextureForSemantic(output, "globalMasks", sources, AtlasTextureSemantic.GlobalMask);
        }
        public void OnConfigurationChanged(AtlasOutputConfiguration config, AtlasConfigurationChange change)
        {
            // Standard aspect constraints follow the shared field schema.
            // Override this hook when a shader needs additional relationships.
        }
        public void Prepare(AtlasGeneratorJob job)
        {
            // Future: use job.Sources[n].Material (unified) and job.Sources[n].Bakes.
            // Bakes expose Texture Wrapper masks + original/current world normals + correction.
            // Do not create or modify Unity materials in this revision.
        }
    }
    public sealed class VRChatMobileToonOutputShader : IAtlasOutputShader
    {
        public string ShaderId => "mobiletoon";
        public string DisplayName => "VRChat Mobile Toon Standard";
        private static readonly AtlasOutputField[] Schema = {
            AtlasOutputField.Section("Base Maps"),
            AtlasOutputField.Texture("normal", "Normal", AtlasTextureSizeMode.MasterAspect),
            AtlasOutputField.Texture("matcap", "Matcap (square)", AtlasTextureSizeMode.Square),
            AtlasOutputField.Texture("emission", "Emission", AtlasTextureSizeMode.MasterAspect),
            new AtlasOutputField("matcapColor", "Matcap Tint", AtlasSettingKind.Color),
        };
        public IReadOnlyList<AtlasOutputField> Fields => Schema;
        public void AutoConfigure(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputConfiguration output)
        {
            AtlasOutputAutoSetup.SetTextureForSemantic(output, "normal", sources, AtlasTextureSemantic.Normal);
            AtlasOutputAutoSetup.SetTextureForSemantic(output, "matcap", sources, AtlasTextureSemantic.Matcap);
            AtlasOutputAutoSetup.SetTextureForSemantic(output, "emission", sources, AtlasTextureSemantic.Emission);
        }
        public void OnConfigurationChanged(AtlasOutputConfiguration config, AtlasConfigurationChange change)
        {
            // Standard aspect constraints follow the shared field schema.
            // Override this hook when a shader needs additional relationships.
        }
        public void Prepare(AtlasGeneratorJob job)
        {
            // Future: generator using agnostic AtlasUnifiedMaterial and Texture Wrapper sources.
        }
    }
}
