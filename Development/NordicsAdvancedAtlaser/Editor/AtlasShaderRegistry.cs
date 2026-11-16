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
    // An output shader keeps only what is specific to it (ATLAS_OUTPUT.md, "Output shaders"):
    // what it can express and its texture layout. Merging, baking and the settings are shared,
    // in the output features.
    public interface IAtlasOutputShader
    {
        string ShaderId { get; }
        string DisplayName { get; }
        // The installed Unity shader the new materials are made with, or null when it isn't
        // installed. Output shaders are only offered, and only built, when it's installed.
        Shader FindShader();
        AtlasOutputCapabilities Capabilities { get; }
        // Sets the material's properties from the output unified material, whose textures are
        // the packed output textures. The material is made with FindShader().
        void Write(AtlasUnifiedMaterial output, Material material);
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
    }
}
