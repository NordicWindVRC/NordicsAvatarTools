using Anatawa12.AvatarOptimizer.API;
using NordicWind.AvatarStatistics;

namespace NordicWind.AvatarStatistics.Editor
{
    // Optional AAO component registration. No dependencies on avatar resources are required.
    [ComponentInformation(typeof(NordicsAvatarStatistics))]
    public sealed class AvatarStatisticsAAOComponentInformation : ComponentInformation<NordicsAvatarStatistics>
    {
        protected override void CollectDependency(
            NordicsAvatarStatistics component,
            ComponentDependencyCollector collector)
        {
            collector.MarkEntrypoint();
        }
    }
}
