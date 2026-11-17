using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // The shader families of ATLAS_OUTPUT.md, "Auto setup".
    public static class AtlasShaderFamilies
    {
        public const string Poiyomi = "poiyomi";
        public const string VRChat = "vrchat";
    }

    /// <summary>
    /// Picks a new output's shader (ATLAS_OUTPUT.md, "Auto setup"), once, when the output appears
    /// without one: the family most of the Atlaser's unused source materials belong to, at its
    /// highest installed version. A shader chosen before is never changed.
    /// </summary>
    [InitializeOnLoad]
    public static class AtlasOutputShaderPick
    {
        static AtlasOutputShaderPick()
        {
            // The Add Component menu; the Atlaser's Add Atlas Output button and the output's
            // inspector also call ApplyIfUnset, for the other ways an output appears.
            ObjectFactory.componentWasAdded += added =>
            {
                if (added is NordicsAtlasOutput output) ApplyIfUnset(output);
            };
        }

        /// <summary>Picks the output's shader when it has none. True when it picked one.</summary>
        public static bool ApplyIfUnset(NordicsAtlasOutput output)
        {
            if (output == null || output.configuration == null || !string.IsNullOrEmpty(output.configuration.shaderId)) return false;
            var pick = Pick(Candidates(output).Select(m => AtlasShaderRegistry.Detect(m)?.Family),
                AtlasShaderRegistry.InstalledOutputShaders);
            if (pick == null) return false;
            Undo.RecordObject(output, "Pick Output Shader");
            output.configuration.shaderId = pick.ShaderId;
            EditorUtility.SetDirty(output);
            return true;
        }

        /// <summary>The materials the Atlaser found that no other output uses; all of them when
        /// every one is used.</summary>
        public static List<Material> Candidates(NordicsAtlasOutput output)
        {
            var found = AtlasMaterialDiscovery.Discover(output.gameObject).Materials.Where(m => m != null).ToList();
            var used = new HashSet<Material>();
            foreach (var other in output.GetComponents<NordicsAtlasOutput>())
                if (other != output && other.configuration?.sources != null)
                    foreach (var entry in other.configuration.sources)
                        if (entry?.material != null) used.Add(entry.material);
            var unused = found.Where(m => !used.Contains(m)).ToList();
            return unused.Count > 0 ? unused : found;
        }

        /// <summary>
        /// The family with the most votes among those with an installed output shader (a tie goes
        /// to the family ID first in alphabetical order), at its highest rank. Without such a
        /// family, the installed output shader first by family ID and highest rank. Null when
        /// none is installed.
        /// </summary>
        public static IAtlasOutputShader Pick(IEnumerable<string> sourceFamilies, IReadOnlyList<IAtlasOutputShader> installed)
        {
            var outputs = installed?.Where(o => o != null).ToList() ?? new List<IAtlasOutputShader>();
            if (outputs.Count == 0) return null;
            var best = outputs.Where(o => !string.IsNullOrEmpty(o.Family))
                .GroupBy(o => o.Family)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(o => o.Rank).First());
            var winner = (sourceFamilies ?? Enumerable.Empty<string>())
                .Where(f => f != null && best.ContainsKey(f))
                .GroupBy(f => f)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Key)
                .FirstOrDefault();
            if (winner != null) return best[winner];
            return outputs.OrderBy(o => o.Family ?? "", StringComparer.Ordinal).ThenByDescending(o => o.Rank).First();
        }
    }
}
