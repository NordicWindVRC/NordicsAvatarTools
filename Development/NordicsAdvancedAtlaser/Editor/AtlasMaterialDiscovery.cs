using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    public sealed class AtlasDiscoveryResult
    {
        public readonly List<Material> Materials = new List<Material>();
        public readonly List<SkinnedMeshRenderer> Renderers = new List<SkinnedMeshRenderer>();
        public bool HasAAO;
        public string Warning;
    }

    public static class AtlasMaterialDiscovery
    {
        // AAO is optional. Resolve the effective PrefabSafeSet, including prefab/scene
        // overrides, instead of reading only the serialized base mainSet array.
        public static AtlasDiscoveryResult Discover(GameObject owner)
        {
            var result = new AtlasDiscoveryResult();
            if (owner == null) return result;

            var visitedMerges = new HashSet<Component>();
            var activeMerges = new HashSet<Component>();
            var visitedRenderers = new HashSet<SkinnedMeshRenderer>();
            var warnings = new List<string>();
            var merge = FindMerge(owner);
            if (merge != null)
            {
                result.HasAAO = true;
                ExpandMerge(merge, result.Renderers, visitedMerges, activeMerges,
                    visitedRenderers, warnings);
            }
            else
            {
                var renderer = owner.GetComponent<SkinnedMeshRenderer>();
                if (renderer != null) result.Renderers.Add(renderer);
            }

            var seen = new HashSet<Material>();
            foreach (var renderer in result.Renderers)
                if (renderer != null)
                    foreach (var material in renderer.sharedMaterials)
                        if (material != null && seen.Add(material)) result.Materials.Add(material);
            if (warnings.Count != 0) result.Warning = string.Join("\n", warnings.ToArray());
            return result;
        }

        private static Component FindMerge(GameObject owner)
        {
            if (owner == null) return null;
            foreach (var component in owner.GetComponents<Component>())
            {
                if (component == null) continue;
                var type = component.GetType();
                if (type.FullName == "Anatawa12.AvatarOptimizer.MergeSkinnedMesh" ||
                    (type.Name == "MergeSkinnedMesh" && type.Namespace != null &&
                     type.Namespace.Contains("AvatarOptimizer"))) return component;
            }
            return null;
        }

        private static void ExpandMerge(Component merge, List<SkinnedMeshRenderer> leaves,
            HashSet<Component> visitedMerges, HashSet<Component> activeMerges,
            HashSet<SkinnedMeshRenderer> visitedRenderers, List<string> warnings)
        {
            if (merge == null) return;
            if (activeMerges.Contains(merge))
            {
                warnings.Add("Cyclic AAO Merge Skinned Mesh source reference at '" +
                    merge.gameObject.name + "'; the recursive reference was ignored.");
                return;
            }
            if (!visitedMerges.Add(merge)) return;
            activeMerges.Add(merge);
            try
            {
                List<SkinnedMeshRenderer> sources;
                string failure;
                if (!TryReadEffectiveSources(merge, out sources, out failure))
                {
                    warnings.Add("AAO Merge Skinned Mesh on '" + merge.gameObject.name +
                        "': " + failure + ". Its source geometry was not captured.");
                    return;
                }

                foreach (var renderer in sources)
                {
                    if (renderer == null) continue;
                    // Merge component takes precedence over any local renderer, including
                    // empty placeholders. This is the same rule at every recursion level.
                    var nested = FindMerge(renderer.gameObject);
                    if (nested != null)
                        ExpandMerge(nested, leaves, visitedMerges, activeMerges,
                            visitedRenderers, warnings);
                    else if (visitedRenderers.Add(renderer))
                        leaves.Add(renderer);
                }
            }
            finally
            {
                activeMerges.Remove(merge);
            }
        }

        private static bool TryReadEffectiveSources(Component merge,
            out List<SkinnedMeshRenderer> sources, out string failure)
        {
            sources = new List<SkinnedMeshRenderer>();
            failure = null;
            // AAO PrefabSafeSet.mainSet is only the base layer. GetAsList() applies
            // prefabLayers and onSceneLayer, so it matches the AAO inspector's sources.
            try
            {
                var field = merge.GetType().GetField("renderersSet",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    var set = field.GetValue(merge);
                    if (set != null)
                    {
                        var method = set.GetType().GetMethod("GetAsList",
                            BindingFlags.Instance | BindingFlags.Public,
                            null, Type.EmptyTypes, null);
                        if (method != null)
                        {
                            var values = method.Invoke(set, null) as IEnumerable;
                            if (values == null)
                            {
                                failure = "AAO source collection returned no enumerable";
                                return false;
                            }
                            foreach (var value in values)
                                if (value is SkinnedMeshRenderer renderer && renderer != null)
                                    sources.Add(renderer);
                            return true; // An empty effective set is valid; do not resurrect base entries.
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Do not silently use mainSet: that loses effective prefab overrides.
                failure = "could not evaluate AAO's prefab-aware source set (" +
                    (ex is TargetInvocationException && ex.InnerException != null
                        ? ex.InnerException.Message : ex.Message) + ")";
                return false;
            }

            // Compatibility fallback for AAO versions without GetAsList().
            // This covers simple configurations but may not include prefab overrides.
            var serialized = new SerializedObject(merge);
            serialized.Update();
            var mainSet = serialized.FindProperty("renderersSet.mainSet");
            if (mainSet == null || !mainSet.isArray)
            {
                failure = "no readable AAO renderer source collection";
                return false;
            }
            for (int i = 0; i < mainSet.arraySize; ++i)
            {
                var renderer = mainSet.GetArrayElementAtIndex(i).objectReferenceValue
                    as SkinnedMeshRenderer;
                if (renderer != null) sources.Add(renderer);
            }
            return true;
        }
    }
}
