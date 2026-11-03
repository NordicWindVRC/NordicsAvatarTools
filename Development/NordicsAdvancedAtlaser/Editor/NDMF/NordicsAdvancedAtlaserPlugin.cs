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
            // After AAO, generate per-material maps, normalize, invoke output modules,
            // then destroy both components on the build clone only.
            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("com.anatawa12.avatar-optimizer")
                .Run("Prepare Atlaser output jobs and clean clone", context =>
                {
                    var state = context.GetState<AtlasGeometryBuildState>();
                    try
                    {
                        AtlasBuildCoordinator.Execute(context.AvatarRootObject,
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
