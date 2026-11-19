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
                    state.Slots = AtlasMaterialSlots.Capture(context.AvatarRootObject);
                    if (AtlasDebugSettings.VerboseLogging)
                        foreach (var message in state.Original.CaptureDiagnostics)
                            UnityEngine.Debug.Log("[Nordic's Advanced Atlaser] Early capture: " + message, context.AvatarRootObject);
                    state.CaptureCompleted = true;
                });
            // Mobile builds: VRCFury, which runs after this phase, empties the slots of materials
            // without a mobile shader; hold the sources' places with mobile placeholders.
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .Run("Hold Atlaser source materials for mobile builds", context =>
                {
                    if (!AtlasMobilePlaceholders.MobileTarget) return;
                    var state = context.GetState<AtlasGeometryBuildState>();
                    state.Placeholders = AtlasMobilePlaceholders.Hold(context.AvatarRootObject);
                });
            // Before AAO: put the held materials back, and follow materials that tools running
            // since the capture swapped on the renderers (AAO keeps the settings and the
            // renderers in step from then on).
            InPhase(BuildPhase.Optimizing)
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run("Follow swapped Atlaser source materials", context => FollowSwaps(context));
            // After AAO, so the meshes are merged and reduced: bake the per-material maps, build
            // the outputs, remap the meshes onto them, then remove both components from the clone.
            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("com.anatawa12.avatar-optimizer")
                .Run("Build Atlaser outputs and remap meshes", context =>
                {
                    // Without AAO the follow pass may not have run yet.
                    FollowSwaps(context);
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

        // Once per build: later, slots no longer mean what they meant at the capture.
        private static void FollowSwaps(BuildContext context)
        {
            var state = context.GetState<AtlasGeometryBuildState>();
            state.Placeholders?.Release(context.AvatarRootObject);
            state.Placeholders = null;
            if (state.Slots == null) return;
            var notes = new System.Collections.Generic.List<string>();
            state.Slots.Follow(context.AvatarRootObject, notes);
            state.Slots = null;
            if (AtlasDebugSettings.VerboseLogging)
                foreach (var note in notes)
                    UnityEngine.Debug.Log("[Nordic's Advanced Atlaser] " + note, context.AvatarRootObject);
        }
    }
}
