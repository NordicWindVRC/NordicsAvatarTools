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
        { return material == null ? null : Parsers.FirstOrDefault(p => p.CanParse(material)); }
        public static IAtlasOutputShader Output(string id)
        { return Outputs.FirstOrDefault(p => p.ShaderId == id); }
        public static void EnsureSettings(AtlasOutputConfiguration config, IAtlasOutputShader output)
        {
            if (config == null || output == null) return;
            if (config.Settings == null) config.Settings = new List<AtlasOutputSetting>();
            foreach (var field in output.Fields)
            {
                if (string.IsNullOrEmpty(field.Id) || config.Find(field.Id) != null) continue;
                config.Settings.Add(new AtlasOutputSetting {
                    Id = field.Id, Kind = field.Kind, BoolValue = field.DefaultEnabled,
                    FloatValue = field.DefaultFloat, IntValue = field.DefaultInt, ColorValue = field.DefaultColor,
                    Texture = new AtlasTextureDimensions { Enabled = field.DefaultEnabled, Width = field.DefaultWidth, Height = field.DefaultHeight },
                    SizeMode = field.SizeMode
                });
            }
        }
    }
}
