using nadena.dev.ndmf;
[assembly: ExportsPlugin(typeof(NordicWind.AdvancedAtlaser.Editor.NordicsAdvancedAtlaserPlugin))]
namespace NordicWind.AdvancedAtlaser.Editor
{
    public sealed class NordicsAdvancedAtlaserPlugin : Plugin<NordicsAdvancedAtlaserPlugin>
    {
        public override string QualifiedName => "app.nordicwind.advanced-atlaser";
        public override string DisplayName => "Nordic's Advanced Atlaser";
        protected override void Configure()
        {
            // Snapshot the original vertex/submesh geometry before AAO changes it.
            InPhase(BuildPhase.Resolving)
                .Run("Capture original Atlaser geometry", context =>
                {
                    var state = context.GetState<AtlasGeometryBuildState>();
                    state.Original = AtlasOriginalGeometryCapture.Capture(context.AvatarRootObject);
                    if (AtlasDebugSettings.VerboseLogging)
                        foreach (var message in state.Original.CaptureDiagnostics)
                            UnityEngine.Debug.Log("[Nordic's Advanced Atlaser] Early capture: " + message, context.AvatarRootObject);
                    state.CaptureCompleted = true;
                });
            // After AAO, so the meshes are merged and reduced: bake the per-material maps, build
            // the outputs, remap the meshes onto them, then remove both components from the clone.
            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("com.anatawa12.avatar-optimizer")
                .Run("Build Atlaser outputs and remap meshes", context =>
                {
                    var state = context.GetState<AtlasGeometryBuildState>();
                    try
                    {
                        AtlasBuildCoordinator.Execute(context,
                            state.CaptureCompleted ? state.Original : null, state.CaptureCompleted);
                    }
                    finally
                    {
                        state.Original = null;
                        state.CaptureCompleted = false;
                    }
                });
        }
    }
}
