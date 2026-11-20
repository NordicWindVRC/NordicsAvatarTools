using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Mobile builds: VRCFury empties the renderer slots of materials whose shaders aren't
    // VRChat/Mobile ones (between NDMF's Transforming and Optimizing phases), and AAO then
    // removes those submeshes, so the Atlaser would find nothing to bake. Hold puts a mobile
    // placeholder in each slot of a source of an output with a mobile shader, which replaces
    // it; Release, run before AAO, puts the materials back. Sources of other outputs are left
    // to VRCFury, whose warning holds for them.
    public sealed class AtlasMobilePlaceholders
    {
        private const string PlaceholderShader = "VRChat/Mobile/Toon Lit";

        // Placeholder → the material it holds the place of.
        private readonly Dictionary<Material, Material> held = new Dictionary<Material, Material>();

        public int Count => held.Count;

        public static bool MobileTarget =>
            EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android || EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS;

        /// <summary>Puts a placeholder in every renderer slot holding a configured material whose
        /// shader isn't a mobile one; returns the placeholders.</summary>
        public static AtlasMobilePlaceholders Hold(GameObject avatarRoot)
        {
            var result = new AtlasMobilePlaceholders();
            var shader = Shader.Find(PlaceholderShader);
            if (avatarRoot == null || shader == null) return result;
            var configured = new HashSet<Material>(avatarRoot.GetComponentsInChildren<NordicsAtlasOutput>(true)
                .Where(o => o.configuration?.sources != null && MobileOutput(o.configuration.shaderId))
                .SelectMany(o => o.configuration.sources)
                .Where(s => s != null && s.material != null)
                .Select(s => s.material));
            if (configured.Count == 0) return result;
            var made = new Dictionary<Material, Material>();
            foreach (var renderer in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null || !configured.Contains(material) || IsMobile(material)) continue;
                    if (!made.TryGetValue(material, out var placeholder))
                    {
                        placeholder = new Material(shader) { name = material.name };
                        made[material] = placeholder;
                        result.held[placeholder] = material;
                    }
                    materials[i] = placeholder;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
            return result;
        }

        /// <summary>Puts the held materials back wherever a placeholder is, and destroys the
        /// placeholders that aren't saved in the build's assets (NDMF saves the objects the
        /// avatar refers to between its hooks; those go with the build's assets).</summary>
        public void Release(GameObject avatarRoot)
        {
            if (held.Count == 0) return;
            if (avatarRoot != null)
                foreach (var renderer in avatarRoot.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < materials.Length; i++)
                        if (materials[i] != null && held.TryGetValue(materials[i], out var original))
                        {
                            materials[i] = original;
                            changed = true;
                        }
                    if (changed) renderer.sharedMaterials = materials;
                }
            foreach (var placeholder in held.Keys)
                if (placeholder != null && !EditorUtility.IsPersistent(placeholder)) Object.DestroyImmediate(placeholder);
            held.Clear();
        }

        private static bool IsMobile(Material material) => IsMobile(material.shader);

        private static bool IsMobile(Shader shader) => shader != null && shader.name.StartsWith("VRChat/Mobile/");

        // The output's materials are made with a mobile shader.
        private static bool MobileOutput(string shaderId) =>
            !string.IsNullOrEmpty(shaderId) && IsMobile(AtlasShaderRegistry.Output(shaderId)?.FindShader());
    }
}
