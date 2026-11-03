namespace NordicWind.AdvancedAtlaser.Editor
{
    // BuildContext.GetState<T> instantiates this once per NDMF build.
    // Shared by the early capture pass and late output preparation pass.
    public sealed class AtlasGeometryBuildState
    {
        public bool CaptureCompleted;
        public AtlasOriginalGeometry Original;
    }
}
