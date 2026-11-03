using Anatawa12.AvatarOptimizer.API;
namespace NordicWind.AdvancedAtlaser.Editor
{
    // AAO's API is referenced only from the optional AAO integration assembly.
    [ComponentInformation(typeof(NordicsAdvancedAtlaser))]
    public sealed class AtlaserAAOComponentInformation : ComponentInformation<NordicsAdvancedAtlaser>
    {
        protected override void CollectDependency(NordicsAdvancedAtlaser component, ComponentDependencyCollector collector)
        {
            collector.MarkEntrypoint();
        }
    }

    [ComponentInformation(typeof(NordicsAtlasOutput))]
    public sealed class AtlasOutputAAOComponentInformation : ComponentInformation<NordicsAtlasOutput>
    {
        protected override void CollectDependency(NordicsAtlasOutput component, ComponentDependencyCollector collector)
        {
            collector.MarkEntrypoint();
        }
    }
}
