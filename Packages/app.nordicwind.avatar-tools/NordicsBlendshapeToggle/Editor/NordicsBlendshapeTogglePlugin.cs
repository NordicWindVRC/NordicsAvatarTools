using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(NordicWind.BlendshapeToggle.Editor.NordicsBlendshapeTogglePlugin))]

namespace NordicWind.BlendshapeToggle.Editor
{
    public sealed class NordicsBlendshapeTogglePlugin : Plugin<NordicsBlendshapeTogglePlugin>
    {
        public override string QualifiedName => "app.nordicwind.blendshape-toggle";
        public override string DisplayName => "Nordic's Blendshape Toggle";

        // Meshia does not override QualifiedName, so NDMF uses its type's full name.
        private const string MeshiaPlugin = "Meshia.MeshSimplification.Ndmf.Editor.NdmfPlugin";
        private const string SubmeshSplitterPlugin = "app.nordicwind.submesh-splitter";
        private const string AvatarOptimizerPlugin = "com.anatawa12.avatar-optimizer";

        protected override void Configure()
        {
            // Names must exist before MA/VRCFury inspect meshes and build animations, so an
            // empty placeholder shape is added in Generating.
            InPhase(BuildPhase.Generating)
                .Run("Prepare toggle blendshapes", Prepare);

            // The geometry is written after per-mesh processors (MA cutters in Transforming,
            // Meshia in Optimizing) and before the Submesh Splitter reassembles and AAO merges.
            InPhase(BuildPhase.Optimizing)
                .AfterPlugin(MeshiaPlugin)
                .BeforePlugin(SubmeshSplitterPlugin)
                .BeforePlugin(AvatarOptimizerPlugin)
                .Run("Generate toggle blendshapes", Bake);
        }

        private static void Prepare(BuildContext context)
        {
            var toggles = context.AvatarRootObject.GetComponentsInChildren<NordicsBlendshapeToggle>(true);
            var plans = new Dictionary<NordicsBlendshapeToggle, ToggleGeometry>();
            var targets = new Dictionary<NordicsBlendshapeToggle, ToggleTarget>();
            // Validate the whole avatar before assigning any replacement meshes.
            foreach (var toggle in toggles.Where(t => t.enabled))
            {
                try
                {
                    var target = ToggleTarget.Resolve(toggle);
                    targets.Add(toggle, target);
                    plans.Add(toggle, ToggleGeometry.Validate(toggle, target.Renderer, target.Slot));
                    if (target.Placeholder != null && target.Placeholder.sharedMesh.GetBlendShapeIndex(toggle.blendshapeName) >= 0)
                        throw new InvalidOperationException("Blendshape name already exists on this mesh.");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Nordic's Blendshape Toggle] '{toggle.name}': {ex.Message}", toggle);
                    throw new InvalidOperationException($"[Nordic's Blendshape Toggle] Invalid configuration on '{toggle.name}'.", ex);
                }
            }
            foreach (var group in plans.Keys.GroupBy(t => targets[t].Renderer))
                foreach (var slotGroup in group.GroupBy(t => targets[t].Slot))
                    if (slotGroup.Count() > 1 && slotGroup.Any(t => t.reverseBlendshape))
                        throw new InvalidOperationException($"[Nordic's Blendshape Toggle] A reversed toggle must be the only toggle for material slot {slotGroup.First().materialSlot} on '{slotGroup.First().name}'.");
            // Names must be unique per animated renderer: the original renderer when split.
            var animated = plans.Keys.GroupBy(t => targets[t].Placeholder ?? targets[t].Renderer).ToArray();
            foreach (var group in animated)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var toggle in group)
                    if (!names.Add(toggle.blendshapeName))
                        throw new InvalidOperationException($"[Nordic's Blendshape Toggle] Duplicate toggle blendshape '{toggle.blendshapeName}' on '{group.Key.name}'.");
            }

            // Empty shapes with the final names on the renderer that tools and animations
            // address. When the renderer is split, the Submesh Splitter later takes the real
            // data from the part.
            foreach (var group in animated)
            {
                var renderer = group.Key;
                var source = renderer.sharedMesh;
                var clone = Object.Instantiate(source);
                clone.name = source.name + " (Nordic's Blendshape Toggle)";
                var empty = new Vector3[source.vertexCount];
                foreach (var toggle in group) clone.AddBlendShapeFrame(toggle.blendshapeName, 100f, empty, null, null);
                Replace(context, renderer, source, clone);
                foreach (var toggle in group) renderer.SetBlendShapeWeight(clone.GetBlendShapeIndex(toggle.blendshapeName), 0f);
            }

            var state = context.GetState<ToggleBuildState>();
            foreach (var toggle in plans.Keys)
                state.Plans.Add(new TogglePlan
                {
                    Name = toggle.blendshapeName, Reverse = toggle.reverseBlendshape, Scale = toggle.collapsedScale,
                    Center = plans[toggle].Center, Slot = targets[toggle].Slot, Target = targets[toggle].Renderer,
                });
            foreach (var toggle in toggles) Object.DestroyImmediate(toggle);
        }

        private static void Bake(BuildContext context)
        {
            var state = context.GetState<ToggleBuildState>();
            foreach (var group in state.Plans.Where(p => p.Target != null && p.Target.sharedMesh != null).GroupBy(p => p.Target))
            {
                var renderer = group.Key;
                var source = renderer.sharedMesh;
                if (!source.isReadable)
                    throw new InvalidOperationException($"[Nordic's Blendshape Toggle] The processed mesh of '{renderer.name}' is not readable.");
                var weights = Enumerable.Range(0, source.blendShapeCount)
                    .ToDictionary(source.GetBlendShapeName, renderer.GetBlendShapeWeight);
                Mesh clone;
                try { clone = ToggleBake.Apply(renderer, group.ToList()); }
                catch (InvalidOperationException ex)
                {
                    throw new InvalidOperationException($"[Nordic's Blendshape Toggle] '{renderer.name}' after mesh processing: {ex.Message}", ex);
                }
                Replace(context, renderer, source, clone);
                for (int i = 0; i < clone.blendShapeCount; i++)
                    renderer.SetBlendShapeWeight(i, weights.TryGetValue(clone.GetBlendShapeName(i), out float w) ? w : 0f);
            }
            state.Plans.Clear();
        }

        private static void Replace(BuildContext context, SkinnedMeshRenderer renderer, Mesh source, Mesh clone)
        {
            ObjectRegistry.RegisterReplacedObject(source, clone);
            context.AssetSaver.SaveAsset(clone);
            renderer.sharedMesh = clone;
        }
    }
}
