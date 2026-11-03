using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.SubmeshSplitter.Editor
{
    // Rebuilds the original renderer from its processed parts. The original renderer is
    // the authority for everything general (materials, bounds, blendshape list, weights,
    // enabled state); parts only contribute geometry changed by explicit per-part actions.
    internal static class SplitReassembly
    {
        private const string Prefix = "[Nordic's Submesh Splitter] ";

        internal static void Run(NordicsSubmeshSplitter splitter, GameObject avatar, SourceSnapshot snapshot, BuildContext context = null)
        {
            var destination = splitter.GetComponent<SkinnedMeshRenderer>();
            string where = $" on '{Path(splitter.transform, avatar)}'";
            if (destination == null || destination.sharedMesh == null)
                throw new InvalidOperationException(Prefix + "Reassembly: the original renderer or its mesh is missing" + where + ".");
            if (snapshot == null)
                throw new InvalidOperationException(Prefix + "Reassembly: the split was not validated at the start of the build" + where + ".");
            var allParts = splitter.generatedRenderers.ToArray();
            if (allParts.Any(r => r == null) || splitter.generatedRoot == null)
                throw new InvalidOperationException(Prefix + "Reassembly: a split renderer or the split container is missing" + where + ".");

            // A part takes part in the build when its own GameObject and renderer are enabled.
            // The container's active state is ignored: it may be hidden for previewing.
            var included = Enumerable.Range(0, allParts.Length)
                .Where(i => allParts[i].gameObject.activeSelf && allParts[i].enabled).ToArray();
            if (included.Length == 0)
                throw new InvalidOperationException(Prefix + "Reassembly: every split renderer is disabled" + where + ". Enable at least one part.");
            var inputs = included.Select(i => allParts[i]).ToArray();
            if (inputs.Any(r => r.sharedMesh == null))
                throw new InvalidOperationException(Prefix + "Reassembly: an enabled split renderer has no mesh" + where + ".");

            var current = destination.sharedMesh;
            // e.g. VRCFury face tracking renames shapes (SIL → VF_FT_SIL) on the original;
            // the parts still carry the old names.
            var renames = FindRenames(snapshot, current);
            var problems = new List<string>();
            CheckOriginalMesh(current, snapshot, inputs, renames, problems);
            CheckLeftoverComponents(splitter.generatedRoot, inputs, problems);
            foreach (var binding in SplitAnimationScan.FindBindingsTo(avatar,
                         allParts.Select(r => r.transform).Append(splitter.generatedRoot).ToList()).Take(10))
                problems.Add("Animation targets a split part, which is removed at build time; animate the original renderer instead: " + binding);
            if (problems.Count > 0)
                throw new InvalidOperationException(Prefix + "Cannot reassemble" + where + ":\n- " + string.Join("\n- ", problems));

            var plan = new BlendshapePlan();
            for (int i = 0; i < current.blendShapeCount; i++) plan.Authority.Add(current.GetBlendShapeName(i));
            foreach (var rename in renames) plan.PartNames[rename.Key] = rename.Value;
            var weightsBefore = SplitBuildIsolation.TakeWeightsAtHide(destination) ?? snapshot.Weights;
            var renamedFrom = new HashSet<string>(renames.Values, StringComparer.Ordinal);
            foreach (var removed in snapshot.BlendShapeNames.Where(n => current.GetBlendShapeIndex(n) < 0 && !renamedFrom.Contains(n)))
                plan.Bake[removed] = weightsBefore.TryGetValue(removed, out float w) ? w : 0f;

            var weights = new Dictionary<string, float>();
            for (int i = 0; i < current.blendShapeCount; i++)
                weights[current.GetBlendShapeName(i)] = destination.GetBlendShapeWeight(i);
            var sourceMaterials = destination.sharedMaterials;

            var result = SplitMeshReassembler.Merge(destination, inputs, current, plan, out var mergedBones);
            result.name = current.name + "_NordicReassembled";
            if (context != null)
            {
                ObjectRegistry.RegisterReplacedObject(current, result);
                context.AssetSaver.SaveAsset(result);
            }
            destination.sharedMesh = result;
            destination.sharedMaterials = MergedMaterials(sourceMaterials, snapshot.SubMeshCount, included, inputs);
            destination.bones = mergedBones;
            // Bounds stay as authored on the original (or as fixed by other tools):
            // localBounds is in root-bone space, so the mesh's own bounds would be wrong here.
            destination.enabled = splitter.originalRendererWasEnabled;
            for (int i = 0; i < result.blendShapeCount; i++)
                if (weights.TryGetValue(result.GetBlendShapeName(i), out float value))
                    destination.SetBlendShapeWeight(i, value);

            // The original renderer and GameObject are kept: existing animation bindings
            // and descriptor references keep their identity. Split objects are removed.
            foreach (var part in allParts) if (part != null) Object.DestroyImmediate(part.gameObject);
            if (splitter.generatedRoot != null) Object.DestroyImmediate(splitter.generatedRoot.gameObject);
            Object.DestroyImmediate(splitter);
        }

        // Before reassembly the original mesh may lose blendshapes (baked by a general
        // optimizer), rename them (keeping their data), or gain shapes whose data an
        // explicit part action provides (e.g. a Blendshape Toggle placeholder). Anything
        // else would be discarded by reassembly.
        internal static void CheckOriginalMesh(Mesh current, SourceSnapshot snapshot, SkinnedMeshRenderer[] inputs,
            IReadOnlyDictionary<string, string> renames, List<string> problems)
        {
            if (current == snapshot.Mesh) return;
            const string advice = " Put mesh-editing components on the split parts instead of the original renderer.";
            if (current.vertexCount != snapshot.VertexCount)
                problems.Add($"Another tool changed the original mesh's vertex count ({snapshot.VertexCount} → {current.vertexCount}) before reassembly." + advice);
            if (current.subMeshCount != snapshot.SubMeshCount)
                problems.Add($"Another tool changed the original mesh's submesh count ({snapshot.SubMeshCount} → {current.subMeshCount}) before reassembly." + advice);
            var known = new HashSet<string>(snapshot.BlendShapeNames, StringComparer.Ordinal);
            for (int i = 0; i < current.blendShapeCount; i++)
            {
                string name = current.GetBlendShapeName(i);
                if (!known.Contains(name) && !renames.ContainsKey(name) && inputs.All(r => r.sharedMesh.GetBlendShapeIndex(name) < 0))
                    problems.Add($"Another tool added blendshape '{name}' to the original mesh before reassembly." + advice);
            }
        }

        // New name → old name for shapes that another tool renamed on the original mesh. A
        // rename keeps the shape's frames and deltas, so a new name is matched to a
        // vanished old name with identical data, preferring the same index.
        internal static Dictionary<string, string> FindRenames(SourceSnapshot snapshot, Mesh current)
        {
            var renames = new Dictionary<string, string>(StringComparer.Ordinal);
            var original = snapshot.Mesh;
            if (original == null || current == original) return renames;
            var currentNames = new HashSet<string>(Enumerable.Range(0, current.blendShapeCount).Select(current.GetBlendShapeName), StringComparer.Ordinal);
            var vanished = snapshot.BlendShapeNames.Where(n => !currentNames.Contains(n) && original.GetBlendShapeIndex(n) >= 0).ToList();
            if (vanished.Count == 0) return renames;
            var known = new HashSet<string>(snapshot.BlendShapeNames, StringComparer.Ordinal);
            for (int i = 0; i < current.blendShapeCount; i++)
            {
                string name = current.GetBlendShapeName(i);
                if (known.Contains(name)) continue;
                var candidates = vanished.OrderBy(n => original.GetBlendShapeIndex(n) == i ? 0 : 1);
                var match = candidates.FirstOrDefault(old => SameShape(original, original.GetBlendShapeIndex(old), current, i));
                if (match == null) continue;
                renames[name] = match;
                vanished.Remove(match);
            }
            return renames;
        }

        private static bool SameShape(Mesh a, int shapeA, Mesh b, int shapeB)
        {
            int frames = a.GetBlendShapeFrameCount(shapeA);
            if (a.vertexCount != b.vertexCount || frames != b.GetBlendShapeFrameCount(shapeB)) return false;
            int n = a.vertexCount;
            var av = new Vector3[n]; var an = new Vector3[n]; var at = new Vector3[n];
            var bv = new Vector3[n]; var bn = new Vector3[n]; var bt = new Vector3[n];
            for (int f = 0; f < frames; f++)
            {
                if (!Mathf.Approximately(a.GetBlendShapeFrameWeight(shapeA, f), b.GetBlendShapeFrameWeight(shapeB, f))) return false;
                a.GetBlendShapeFrameVertices(shapeA, f, av, an, at);
                b.GetBlendShapeFrameVertices(shapeB, f, bv, bn, bt);
                for (int v = 0; v < n; v++)
                    if (av[v] != bv[v] || an[v] != bn[v] || at[v] != bt[v]) return false;
            }
            return true;
        }

        // Components that are still on a part when reassembly runs never did their work
        // (processors such as MA and Meshia remove their components once applied). They
        // would be deleted silently with the part, so report them.
        internal static void CheckLeftoverComponents(Transform container, SkinnedMeshRenderer[] inputs, List<string> problems)
        {
            var leftovers = container.GetComponents<Component>()
                .Concat(inputs.SelectMany(r => r.GetComponentsInChildren<Component>(true)))
                .Where(c => c != null && !(c is Transform) && !(c is SkinnedMeshRenderer));
            foreach (var c in leftovers)
                problems.Add($"'{c.GetType().Name}' on split part '{c.name}' has not been applied and would be lost. " +
                             "Only explicit per-part actions belong on split parts; general tools (AAO, VRCFury and similar) go on the original renderer.");
        }

        // The original renderer owns materials: part i gets the original's slot i. Extra
        // submeshes a processor created on a part keep that part's own materials. Extra
        // material slots beyond the original's submeshes re-render the last submesh, so
        // they are kept only while that last part is included.
        internal static Material[] MergedMaterials(Material[] source, int sourceSubmeshes, int[] included, SkinnedMeshRenderer[] inputs)
        {
            var result = new List<Material>();
            for (int k = 0; k < inputs.Length; k++)
            {
                int slot = included[k], count = inputs[k].sharedMesh.subMeshCount;
                var own = inputs[k].sharedMaterials;
                for (int s = 0; s < count; s++)
                    result.Add(s == 0 && slot < source.Length ? source[slot] : s < own.Length ? own[s] : null);
            }
            if (source.Length > sourceSubmeshes && included.Length > 0 && included[included.Length - 1] == sourceSubmeshes - 1)
                result.AddRange(source.Skip(sourceSubmeshes));
            return result.ToArray();
        }

        private static string Path(Transform t, GameObject avatar)
        {
            var names = new List<string>();
            for (; t != null && t.gameObject != avatar; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
