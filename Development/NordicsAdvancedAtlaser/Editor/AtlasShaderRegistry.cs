using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    public interface IAtlasSourceShaderParser
    {
        string ParserId { get; }
        string DisplayName { get; }
        int Priority { get; }
        bool CanParse(Material material);
        AtlasUnifiedMaterial Parse(Material material);
    }
    public enum AtlasConfigurationChange { MainTexture, ShaderSelection, Setting, AutoSetup }

    public interface IAtlasOutputShader
    {
        string ShaderId { get; }
        string DisplayName { get; }
        // The installed Unity shader the new materials are made with, or null when it isn't
        // installed. Output shaders are only offered, and only built, when it's installed.
        Shader FindShader();
        IReadOnlyList<AtlasOutputField> Fields { get; }
        // Called after shared main-texture size detection. May override any shared or shader-specific setting.
        void AutoConfigure(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputConfiguration output);
        void OnConfigurationChanged(AtlasOutputConfiguration configuration, AtlasConfigurationChange change);
        void Prepare(AtlasGeneratorJob job);
    }
    public sealed class AtlasOutputField
    {
        public string Id, Label, Description;
        public AtlasSettingKind Kind;
        public AtlasTextureSizeMode SizeMode;
        public bool DefaultEnabled;
        public int DefaultWidth = 1024, DefaultHeight = 1024;
        public float DefaultFloat;
        public int DefaultInt;
        public Color DefaultColor = Color.white;
        public AtlasOutputField(string id, string label, AtlasSettingKind kind, string description = null)
        { Id = id; Label = label; Kind = kind; Description = description; }
        public static AtlasOutputField Texture(string id, string label, AtlasTextureSizeMode sizing, bool enabled = false)
        { return new AtlasOutputField(id, label, AtlasSettingKind.Texture) { SizeMode = sizing, DefaultEnabled = enabled }; }
        public static AtlasOutputField Section(string title)
        { return new AtlasOutputField(null, title, AtlasSettingKind.Description); }
    }

    // New shader integration = one IAtlasOutputShader implementation; no central switch statement.
    [InitializeOnLoad]
    public static class AtlasShaderRegistry
    {
        private static readonly List<IAtlasSourceShaderParser> Parsers = new List<IAtlasSourceShaderParser>();
        private static readonly List<IAtlasOutputShader> Outputs = new List<IAtlasOutputShader>();
        public static IReadOnlyList<IAtlasSourceShaderParser> InputParsers => Parsers;
        public static IReadOnlyList<IAtlasOutputShader> OutputShaders => Outputs;
        public static bool IsInstalled(IAtlasOutputShader output) => output != null && output.FindShader() != null;
        // The output shaders whose Unity shader is installed in this project.
        public static IReadOnlyList<IAtlasOutputShader> InstalledOutputShaders => Outputs.Where(IsInstalled).ToList();
        static AtlasShaderRegistry()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<IAtlasSourceShaderParser>())
            {
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null) continue;
                RegisterInput((IAtlasSourceShaderParser)Activator.CreateInstance(type));
            }
            foreach (var type in TypeCache.GetTypesDerivedFrom<IAtlasOutputShader>())
            {
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null) continue;
                RegisterOutput((IAtlasOutputShader)Activator.CreateInstance(type));
            }
            Parsers.Sort((a,b) => b.Priority.CompareTo(a.Priority));
            Outputs.Sort((a,b) => StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName,b.DisplayName));
        }
        public static void RegisterInput(IAtlasSourceShaderParser parser)
        {
            if (parser == null || Parsers.Any(p => p.ParserId == parser.ParserId)) throw new ArgumentException("Duplicate or null parser");
            Parsers.Add(parser); Parsers.Sort((a,b) => b.Priority.CompareTo(a.Priority));
        }
        public static void RegisterOutput(IAtlasOutputShader shader)
        {
            if (shader == null || Outputs.Any(p => p.ShaderId == shader.ShaderId)) throw new ArgumentException("Duplicate or null output shader");
            Outputs.Add(shader);
        }
        public static IAtlasSourceShaderParser Detect(Material material)
        {
            if (material == null) return null;
            AtlasMaterialReader.ClearCache();
            try { return Parsers.FirstOrDefault(p => p.CanParse(material)); }
            finally { AtlasMaterialReader.ClearCache(); }
        }
        public static IAtlasOutputShader Output(string id)
        { return Outputs.FirstOrDefault(p => p.ShaderId == id); }
        public static void EnsureSettings(AtlasOutputConfiguration config, IAtlasOutputShader output)
        {
            if (config == null || output == null) return;
            if (config.settings == null) config.settings = new List<AtlasOutputSetting>();
            foreach (var field in output.Fields)
            {
                if (string.IsNullOrEmpty(field.Id) || config.Find(field.Id) != null) continue;
                config.settings.Add(new AtlasOutputSetting {
                    id = field.Id, kind = field.Kind, boolValue = field.DefaultEnabled,
                    floatValue = field.DefaultFloat, intValue = field.DefaultInt, colorValue = field.DefaultColor,
                    texture = new AtlasTextureDimensions { enabled = field.DefaultEnabled, width = field.DefaultWidth, height = field.DefaultHeight },
                    sizeMode = field.SizeMode
                });
            }
        }
    }
}
