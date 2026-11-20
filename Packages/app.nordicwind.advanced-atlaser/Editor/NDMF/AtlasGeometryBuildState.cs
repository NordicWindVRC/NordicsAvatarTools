namespace NordicWind.AdvancedAtlaser.Editor
{
    // BuildContext.GetState<T> instantiates this once per NDMF build.
    // Shared by the early capture pass and late output preparation pass.
    public sealed class AtlasGeometryBuildState
    {
        public bool CaptureCompleted;
        public AtlasOriginalGeometry Original;
        // The renderer slots of the configured materials at the early capture; null once
        // followed (see AtlasMaterialSlots).
        public AtlasMaterialSlots Slots;
        // Mobile builds: the source materials held behind mobile placeholders until just
        // before AAO (see AtlasMobilePlaceholders); null once released.
        public AtlasMobilePlaceholders Placeholders;
    }
}
