using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;

[assembly: ExportsPlugin(typeof(NordicWind.SubmeshSplitter.Editor.NordicsSubmeshSplitterPlugin))]

namespace NordicWind.SubmeshSplitter.Editor
{
    public sealed class NordicsSubmeshSplitterPlugin : Plugin<NordicsSubmeshSplitterPlugin>
    {
        public override string QualifiedName => "app.nordicwind.submesh-splitter";
        public override string DisplayName => "Nordic's Submesh Splitter";

        // Meshia does not override QualifiedName, so NDMF uses its type's full name.
        internal const string MeshiaPlugin = "Meshia.MeshSimplification.Ndmf.Editor.NdmfPlugin";
        internal const string AvatarOptimizerPlugin = "com.anatawa12.avatar-optimizer";

        protected override void Configure()
        {
            // Resolving runs before any tool changes meshes: stop stale splits early and
            // record the original renderer so reassembly can tell allowed changes apart.
            InPhase(BuildPhase.Resolving)
                .Run("Validate split meshes", Validate);

            // Per-part actions run before this: MA (Transforming) and Meshia (Optimizing).
            // General optimizers such as AAO run after it, on the reassembled original.
            InPhase(BuildPhase.Optimizing)
                .AfterPlugin(MeshiaPlugin)
                .BeforePlugin(AvatarOptimizerPlugin)
                .Run("Reassemble processed submeshes", context =>
                {
                    SplitBuildIsolation.Restore(context.AvatarRootObject); // no-op unless an SDK build isolated the parts
                    var state = context.GetState<SplitBuildState>();
                    if (state.ValidationFailed) return; // already reported; the build is failing
                    foreach (var splitter in context.AvatarRootObject.GetComponentsInChildren<NordicsSubmeshSplitter>(true))
                    {
                        state.Sources.TryGetValue(splitter, out var snapshot);
                        try { SplitReassembly.Run(splitter, context.AvatarRootObject, snapshot, context); }
                        catch (InvalidOperationException ex) { SplitBuildFailure.Raise(WithoutPrefix(ex.Message)); }
                    }
                });
        }

        private const string Prefix = "[Nordic's Submesh Splitter] ";
        private static string WithoutPrefix(string message) =>
            message.StartsWith(Prefix, StringComparison.Ordinal) ? message.Substring(Prefix.Length) : message;

        private static void Validate(BuildContext context)
        {
            var state = context.GetState<SplitBuildState>();
            var errors = new List<string>();
            foreach (var splitter in context.AvatarRootObject.GetComponentsInChildren<NordicsSubmeshSplitter>(true))
            {
                var error = SplitMeshGenerator.ValidateGenerated(splitter);
                if (error != null) { errors.Add($"'{splitter.name}': {error}"); continue; }
                state.Sources[splitter] = SourceSnapshot.Take(splitter.GetComponent<SkinnedMeshRenderer>());
            }
            if (errors.Count > 0)
            {
                state.ValidationFailed = true;
                SplitBuildFailure.Raise("Split meshes are out of date. " +
                    "Open the prefab or scene and click Update split meshes:\n- " + string.Join("\n- ", errors));
            }
        }
    }
}
