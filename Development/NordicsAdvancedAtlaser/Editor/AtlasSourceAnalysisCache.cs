using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // A source material as the inspector sees it: parsed into the unified material, or why it
    // couldn't be.
    public sealed class AtlasSourceAnalysis
    {
        public AtlasUnifiedMaterial Unified;
        public string ParserName;
        // Null when parsed.
        public string Error;
    }

    // Parsing every source on every redraw is too slow, so the inspector reads the sources
    // through this cache. An entry is rebuilt when its key changes (ATLAS_OUTPUT.md, "Source
    // analysis cache"): the material's dirty count, its shader, and a version raised when a
    // texture or shader is imported. A domain reload clears it. Builds parse on their own.
    public static class AtlasSourceAnalysisCache
    {
        private struct Key : IEquatable<Key>
        {
            public int DirtyCount, Shader, ImportVersion;
            public bool Equals(Key other) =>
                DirtyCount == other.DirtyCount && Shader == other.Shader && ImportVersion == other.ImportVersion;
        }

        private sealed class Entry
        {
            public Key Key;
            public AtlasSourceAnalysis Analysis;
        }

        private static readonly Dictionary<int, Entry> Entries = new Dictionary<int, Entry>();
        private static int importVersion;

        // How many times a material was parsed; for tests.
        public static int ParseCount { get; private set; }

        public static AtlasSourceAnalysis Get(Material material)
        {
            if (material == null) return null;
            var key = new Key
            {
                DirtyCount = EditorUtility.GetDirtyCount(material),
                Shader = material.shader != null ? material.shader.GetInstanceID() : 0,
                ImportVersion = importVersion
            };
            int id = material.GetInstanceID();
            if (Entries.TryGetValue(id, out var entry) && entry.Key.Equals(key)) return entry.Analysis;
            entry = new Entry { Key = key, Analysis = Analyse(material) };
            Entries[id] = entry;
            return entry.Analysis;
        }

        // Textures or shaders changed on disk: every entry is rebuilt on its next read.
        public static void Invalidate() => importVersion++;

        private static AtlasSourceAnalysis Analyse(Material material)
        {
            ParseCount++;
            var parser = AtlasShaderRegistry.Detect(material);
            if (parser == null) return new AtlasSourceAnalysis { Error = "No input parser can read this material." };
            try
            {
                return new AtlasSourceAnalysis { Unified = parser.Parse(material), ParserName = parser.DisplayName };
            }
            catch (Exception e)
            {
                return new AtlasSourceAnalysis { ParserName = parser.DisplayName, Error = $"{parser.DisplayName} couldn't read it: {e.Message}" };
            }
        }
    }

    // Raises the cache's version when a texture or shader is imported, moved or deleted: a
    // material reads them, so its analysis can change while the material itself doesn't.
    internal sealed class AtlasSourceAnalysisImports : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (deleted.Length > 0 || moved.Length > 0)
            {
                AtlasSourceAnalysisCache.Invalidate();
                return;
            }
            foreach (var path in imported)
            {
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                if (type != null && (typeof(Texture).IsAssignableFrom(type) || typeof(Shader).IsAssignableFrom(type)))
                {
                    AtlasSourceAnalysisCache.Invalidate();
                    return;
                }
            }
        }
    }
}
