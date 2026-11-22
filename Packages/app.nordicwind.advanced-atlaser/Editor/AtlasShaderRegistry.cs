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
        // The output shader family its materials vote for when a new output picks its shader
        // (ATLAS_OUTPUT.md, "Auto setup"); null when no output shader belongs to it.
        string Family { get; }
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
        // Its family (as on the parsers) and its rank within the family. Poiyomi: the major
        // version × 10, plus 1 for Pro, so the highest version wins and Pro comes before Toon at
        // the same one. VRChat Mobile and Unity: higher for the shader that keeps more.
        string Family { get; }
        int Rank { get; }
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
        public static bool IsInstalled(IAtlasOutputShader output) => Find(output) != null;
        // The output shaders whose Unity shader is installed in this project.
        public static IReadOnlyList<IAtlasOutputShader> InstalledOutputShaders => installed ?? (installed = Outputs.Where(IsInstalled).ToList());

        // Looking a shader up by name is slow, most of all for one that isn't installed, and
        // the inspectors ask on every redraw. What each output shader's lookup gave is kept
        // until a shader is imported, moved or deleted (ShadersChanged).
        private static readonly Dictionary<IAtlasOutputShader, Shader> Found = new Dictionary<IAtlasOutputShader, Shader>();
        private static IReadOnlyList<IAtlasOutputShader> installed;

        /// <summary>The output shader's installed Unity shader (its FindShader), or null.</summary>
        public static Shader Find(IAtlasOutputShader output)
        {
            if (output == null) return null;
            // A kept shader that was destroyed since (reimported) is looked up again.
            if (Found.TryGetValue(output, out var shader) && (ReferenceEquals(shader, null) || shader != null)) return shader;
            shader = output.FindShader();
            // Unity's null for a missing object isn't a null reference: keep a real one.
            Found[output] = shader != null ? shader : null;
            return Found[output];
        }

        /// <summary>The project's shaders changed: the installed ones are looked up again.</summary>
        public static void ShadersChanged()
        {
            Found.Clear();
            installed = null;
        }
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
            // The capabilities name their shader, so what its parser says of it can be found.
            shader.Capabilities.ShaderId = shader.ShaderId;
            Outputs.Add(shader);
            installed = null;
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
