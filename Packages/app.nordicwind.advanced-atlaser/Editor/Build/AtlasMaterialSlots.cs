using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Tools that run between the early capture and the build may swap a source material on
    // the renderers for a copy without changing other components' references to it (VRCFury
    // copies materials whose textures don't stream their mipmaps). Capture records the
    // renderer slots that hold each configured material; Follow, run before AAO (which keeps
    // the components' references and the renderers' in step from then on), points the
    // Atlaser's settings at the copy now in those slots. Slots can also move (Submesh
    // Splitter reassembly, other mesh tools), so only a copy counts: a material with the same
    // shader that isn't itself a configured material, while the configured one is on no
    // renderer.
    public sealed class AtlasMaterialSlots
    {
        private readonly List<(Renderer Renderer, int Slot, Material Material)> slots = new List<(Renderer, int, Material)>();

        public static AtlasMaterialSlots Capture(GameObject avatarRoot)
        {
            var result = new AtlasMaterialSlots();
            if (avatarRoot == null) return result;
            var configured = Configured(avatarRoot);
            if (configured.Count == 0) return result;
            foreach (var renderer in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && configured.Contains(materials[i]))
                        result.slots.Add((renderer, i, materials[i]));
            }
            return result;
        }

        /// <summary>The source materials of every Atlaser output under the root.</summary>
        public static HashSet<Material> Configured(GameObject avatarRoot) =>
            new HashSet<Material>(avatarRoot.GetComponentsInChildren<NordicsAtlasOutput>(true)
                .Where(o => o.configuration?.sources != null)
                .SelectMany(o => o.configuration.sources)
                .Where(s => s != null && s.material != null)
                .Select(s => s.material));

        /// <summary>The configured materials on no renderer under <paramref name="avatarRoot"/>,
        /// each with the one copy of it now in its slots. A material whose slots now hold
        /// several copies is left as it is and listed in <paramref name="notes"/>.</summary>
        public Dictionary<Material, Material> Swaps(GameObject avatarRoot, List<string> notes = null)
        {
            var swaps = new Dictionary<Material, Material>();
            if (avatarRoot == null) return swaps;
            var configured = Configured(avatarRoot);
            configured.UnionWith(slots.Select(s => s.Material));
            var present = new HashSet<Material>(avatarRoot.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials).Where(m => m != null));
            foreach (var group in slots.GroupBy(s => s.Material))
            {
                if (present.Contains(group.Key)) continue;
                var now = group.Where(s => s.Renderer != null)
                    .Select(s =>
                    {
                        var materials = s.Renderer.sharedMaterials;
                        return s.Slot < materials.Length ? materials[s.Slot] : null;
                    })
                    .Where(m => m != null && !configured.Contains(m) && m.shader == group.Key.shader).Distinct().ToList();
                if (now.Count == 0) continue;
                if (now.Count == 1) swaps[group.Key] = now[0];
                else notes?.Add(group.Key.name + ": replaced by different materials in its slots (" +
                                string.Join(", ", now.Select(m => m.name)) + ") before the build; the settings keep it.");
            }
            return swaps;
        }

        /// <summary>Points every material reference in the avatar's Atlaser components at its
        /// swap; returns how many references changed.</summary>
        public int Follow(GameObject avatarRoot, List<string> notes = null)
        {
            if (avatarRoot == null) return 0;
            var swaps = Swaps(avatarRoot, notes);
            if (swaps.Count == 0) return 0;
            int changed = Replace(avatarRoot, swaps);
            foreach (var swap in swaps)
                notes?.Add(swap.Key.name + ": replaced by '" + swap.Value.name + "' on the renderers before the build; the settings follow it.");
            return changed;
        }

        /// <summary>Points every material reference in the Atlaser components under
        /// <paramref name="root"/> at its swap; returns how many references changed.</summary>
        public static int Replace(GameObject root, IReadOnlyDictionary<Material, Material> swaps)
        {
            if (root == null || swaps.Count == 0) return 0;
            int changed = 0;
            var components = root.GetComponentsInChildren<NordicsAdvancedAtlaser>(true).Cast<Component>()
                .Concat(root.GetComponentsInChildren<NordicsAtlasOutput>(true));
            foreach (var component in components)
                using (var serialized = new SerializedObject(component))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(property.propertyType != SerializedPropertyType.String))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference ||
                            !(property.objectReferenceValue is Material material) || !swaps.TryGetValue(material, out var swap)) continue;
                        property.objectReferenceValue = swap;
                        changed++;
                    }
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            return changed;
        }
    }
}
