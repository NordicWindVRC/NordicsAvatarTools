using nadena.dev.ndmf;
using NordicWind.AvatarStatistics;
using UnityEngine;

[assembly: ExportsPlugin(typeof(NordicWind.AvatarStatistics.Editor.NordicsAvatarStatisticsPlugin))]
namespace NordicWind.AvatarStatistics.Editor
{
    public sealed class NordicsAvatarStatisticsPlugin : Plugin<NordicsAvatarStatisticsPlugin>
    {
        public override string QualifiedName => "app.nordicwind.avatar-statistics";
        public override string DisplayName => "Nordic's Avatar Statistics";
        private sealed class State { internal bool FaceTrackingAtStart; }

        protected override void Configure()
        {
            // Record the avatar's own setting before other tools can change it.
            InPhase(BuildPhase.Resolving)
                .Run("Record native face tracking setting", context =>
                    context.GetState<State>().FaceTrackingAtStart =
                        AvatarStatisticsCollector.NativeFaceTrackingEnabled(context.AvatarRootObject));

            InPhase(BuildPhase.PlatformFinish)
                .Run("Collect Nordic's Avatar Statistics", context =>
                {
                    var root = context.AvatarRootObject;
                    if (root == null || root.GetComponent<NordicsAvatarStatistics>() == null) return;
                    try
                    {
                        var snapshot = AvatarStatisticsCollector.Collect(root);
                        if (!context.GetState<State>().FaceTrackingAtStart && AvatarStatisticsCollector.NativeFaceTrackingEnabled(root))
                            AvatarStatisticsCollector.NoteFaceTrackingEnabledDuringBuild(snapshot);
                        ParameterFinalizationRegistry.Register(root, snapshot);
                        ErrorReport.ReportError(new AvatarStatisticsInfo(snapshot));
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogException(ex);
                        ErrorReport.ReportError(new AvatarStatisticsFailure(ex.Message));
                    }
                });
        }
    }
}
