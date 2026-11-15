namespace NordicWind.AdvancedAtlaser.Editor
{
    // 3.1: how the surface is shaded.
    public enum AtlasLightingFamily
    {
        // The base colour as is.
        Unlit,
        // Uniform light, no shading across the surface.
        Flat,
        // A shading term mapped through a ramp to brightness and shade colour.
        RampToon,
        // Energy-conserving diffuse plus the surface response.
        PhysicallyBased
    }

    // 3: lighting. Each family's parameters are added as parsers need them.
    public sealed class AtlasLighting
    {
        public AtlasLightingFamily Family = AtlasLightingFamily.PhysicallyBased;
    }
}
