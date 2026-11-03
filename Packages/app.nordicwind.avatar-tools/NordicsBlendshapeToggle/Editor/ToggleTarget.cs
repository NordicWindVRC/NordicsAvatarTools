using System;
using System.Reflection;
using UnityEngine;

namespace NordicWind.BlendshapeToggle.Editor
{
    // Which mesh a toggle edits. Normally the renderer on the toggle's GameObject.
    // When Nordic's Submesh Splitter has split that renderer, the material slot lives in
    // its own split part (as submesh 0), and the original renderer gets a placeholder.
    // The Splitter is reached by reflection so neither tool depends on the other.
    internal readonly struct ToggleTarget
    {
        internal readonly SkinnedMeshRenderer Renderer;
        internal readonly int Slot;
        internal readonly SkinnedMeshRenderer Placeholder;

        private ToggleTarget(SkinnedMeshRenderer renderer, int slot, SkinnedMeshRenderer placeholder)
        { Renderer = renderer; Slot = slot; Placeholder = placeholder; }

        private const string SplitterType = "NordicWind.SubmeshSplitter.NordicsSubmeshSplitter";

        internal static ToggleTarget Resolve(NordicsBlendshapeToggle toggle)
        {
            var own = toggle.GetComponent<SkinnedMeshRenderer>();
            foreach (var component in toggle.GetComponents<Component>())
            {
                if (component == null || component.GetType().FullName != SplitterType) continue;
                var method = component.GetType().GetMethod("TryGetGeneratedRenderer", BindingFlags.Public | BindingFlags.Instance);
                if (method == null) break;
                var args = new object[] { toggle.materialSlot, null };
                if ((bool)method.Invoke(component, args) && args[1] is SkinnedMeshRenderer part && part.sharedMesh != null)
                    return new ToggleTarget(part, 0, own);
                break;
            }
            return new ToggleTarget(own, toggle.materialSlot, null);
        }
    }
}
