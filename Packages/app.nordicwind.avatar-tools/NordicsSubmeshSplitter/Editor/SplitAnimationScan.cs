using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NordicWind.SubmeshSplitter.Editor
{
    // Finds animation bindings that target split parts. Parts are removed at build time,
    // so such bindings would silently stop working; animate the original renderer instead.
    internal static class SplitAnimationScan
    {
        internal static List<string> FindBindingsTo(GameObject avatar, IReadOnlyCollection<Transform> targets)
        {
            var found = new List<string>();
            if (avatar == null || targets.Count == 0) return found;
            foreach (var (root, controller) in Controllers(avatar))
            {
                var prefixes = targets.Where(t => t != null && t.IsChildOf(root))
                    .Select(t => AnimationUtility.CalculateTransformPath(t, root)).ToList();
                if (prefixes.Count == 0) continue;
                foreach (var clip in controller.animationClips.Where(c => c != null).Distinct())
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip)
                                 .Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                        if (prefixes.Any(p => binding.path == p || binding.path.StartsWith(p + "/")))
                            found.Add($"'{clip.name}' animates {binding.path} ({binding.propertyName})");
            }
            return found.Distinct().ToList();
        }

        // Controllers on Animator components (paths relative to the Animator) and on the
        // avatar descriptor's playable layers (paths relative to the avatar root). The
        // descriptor is read through serialization so no SDK version is required.
        private static IEnumerable<(Transform root, RuntimeAnimatorController controller)> Controllers(GameObject avatar)
        {
            var seen = new HashSet<(Transform, RuntimeAnimatorController)>();
            foreach (var animator in avatar.GetComponentsInChildren<Animator>(true))
                if (animator.runtimeAnimatorController != null && seen.Add((animator.transform, animator.runtimeAnimatorController)))
                    yield return (animator.transform, animator.runtimeAnimatorController);
            foreach (var component in avatar.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().Name != "VRCAvatarDescriptor") continue;
                var it = new SerializedObject(component).GetIterator();
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference &&
                        it.objectReferenceValue is RuntimeAnimatorController controller &&
                        seen.Add((avatar.transform, controller)))
                        yield return (avatar.transform, controller);
            }
        }
    }
}
