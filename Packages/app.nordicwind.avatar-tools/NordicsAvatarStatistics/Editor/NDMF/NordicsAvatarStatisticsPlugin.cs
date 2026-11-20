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

        // NDMF's ReportError also writes "[NDMF] Error Reported" to Unity's console as a warning,
        // whatever the severity, which makes the results look like a failure. Unity's logger is
        // set to errors only for the duration of the call: the NDMF Console still shows the
        // report, and Unity's console gets one plain line instead. Failures are reported as they are.
        private static void ReportInformation(IError information)
        {
            var logger = Debug.unityLogger;
            var filter = logger.filterLogType;
            logger.filterLogType = LogType.Error;
            try
            {
                ErrorReport.ReportError(information);
            }
            finally
            {
                logger.filterLogType = filter;
            }
        }

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
                        var info = new AvatarStatisticsInfo(snapshot);
                        ReportInformation(info);
                        Debug.Log(info.ToMessage() + " Details: Tools > NDM Framework > Show NDMF Console.");
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
