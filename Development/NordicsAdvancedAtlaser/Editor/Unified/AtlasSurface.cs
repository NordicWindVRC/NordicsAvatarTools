namespace NordicWind.AdvancedAtlaser.Editor
{
    // 2: what the surface is made of (stage S1) and how it reflects light (S2).
    public sealed class AtlasSurface
    {
        // 2.1
        public readonly AtlasColorInput BaseColor = new AtlasColorInput();
        // 2.2
        public readonly AtlasAlpha Alpha = new AtlasAlpha();
        // 2.3: the primary normal. Surface layers targeting the normal are composited on top.
        public readonly AtlasNormal Normal = new AtlasNormal();
        // 2.4
        public readonly AtlasSurfaceResponse Response = new AtlasSurfaceResponse();
    }

    public enum AtlasAlphaSource { BaseColorAlpha, Mask, None }

    // 2.2: where the surface's alpha comes from. The render settings decide what alpha does.
    public sealed class AtlasAlpha
    {
        public AtlasAlphaSource Source = AtlasAlphaSource.BaseColorAlpha;
        // Used when Source is Mask.
        public AtlasMask Mask;
    }

    // 2.3: a tangent-space normal map. Without a map the surface is flat.
    public sealed class AtlasNormal
    {
        public AtlasTextureReference Map;
        public float Strength = 1f;
    }

    // 2.4: the inputs of the surface's light response.
    public sealed class AtlasSurfaceResponse
    {
        public readonly AtlasScalarInput Metallic = new AtlasScalarInput(0f);
        // Roughness, never smoothness: 0 smooth, 1 rough.
        public readonly AtlasScalarInput Roughness = new AtlasScalarInput(0.5f);
        // 0.5 = 4% reflectance at normal incidence (F0 = 0.16 × r²).
        public readonly AtlasScalarInput Reflectance = new AtlasScalarInput(0.5f);
        // The specular colour (F0) of the specular workflow; null for the metallic workflow.
        public AtlasColorInput SpecularColor;
        // The constant is the strength: 1 − (1 − map) × strength.
        public readonly AtlasScalarInput Occlusion = new AtlasScalarInput(1f) { Combine = AtlasScalarCombine.StrengthAroundOne };
        public bool Highlights = true;
        public bool Reflections = true;
    }
}
