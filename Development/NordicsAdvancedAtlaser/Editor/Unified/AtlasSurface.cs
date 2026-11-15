namespace NordicWind.AdvancedAtlaser.Editor
{
    // 2: what the surface is made of (stage S1) and how it reflects light (S2).
    public sealed class AtlasSurface
    {
        // 2.1: base colour = colour input × vertex colour (where enabled).
        public readonly AtlasColorInput BaseColor = new AtlasColorInput();
        public readonly AtlasVertexColor VertexColor = new AtlasVertexColor();
        // 2.2
        public readonly AtlasAlpha Alpha = new AtlasAlpha();
        // 2.3: the primary normal. Surface layers targeting the normal are composited on top.
        public readonly AtlasNormal Normal = new AtlasNormal();
        // 2.4
        public readonly AtlasSurfaceResponse Response = new AtlasSurfaceResponse();
        // 2.5
        public readonly AtlasParallax Parallax = new AtlasParallax();
    }

    // 2.1: how the mesh's vertex colours change the base colour.
    public sealed class AtlasVertexColor
    {
        public bool MultiplyColor;
        public bool MultiplyAlpha;
        // Off: vertex colours are used as stored, as linear values. On: converted from sRGB first.
        public bool SrgbData;
    }

    public enum AtlasAlphaSource
    {
        // Colour alpha × texture alpha.
        BaseColorAlpha,
        // The base colour's colour alpha alone, when the texture's alpha means something else.
        BaseColorConstantAlpha,
        Mask,
        // 1
        None
    }

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
        // The value the shading uses; parsers convert gamma-encoded sliders.
        public readonly AtlasScalarInput Metallic = new AtlasScalarInput(0f);
        // Perceptual roughness, 1 − smoothness: 0 smooth, 1 rough.
        public readonly AtlasScalarInput Roughness = new AtlasScalarInput(0.5f);
        // 0.5 = 4% reflectance at normal incidence (F0 = 0.16 × r²).
        public readonly AtlasScalarInput Reflectance = new AtlasScalarInput(0.5f);
        // The specular colour (F0) of the specular workflow; null for the metallic workflow.
        public AtlasColorInput SpecularColor;
        // Darkens ambient light and reflections, not direct light. The constant is the strength:
        // 1 − (1 − map) × strength.
        public readonly AtlasScalarInput Occlusion = new AtlasScalarInput(1f) { Combine = AtlasScalarCombine.StrengthAroundOne };
        // Direct-light highlights.
        public bool Highlights = true;
        // Environment reflections; off: a flat colour from the ambient specular instead.
        public bool Reflections = true;
    }

    public enum AtlasParallaxMethod { SingleStep }

    // 2.5: shifts the surface texture lookups by the view angle, so the surface looks deeper.
    public sealed class AtlasParallax
    {
        public bool Enabled;
        // 0 low, 1 high.
        public readonly AtlasScalarInput Height = new AtlasScalarInput(1f);
        // UV shift scale.
        public float Depth = 0.02f;
        // The height that doesn't move.
        public float ReferenceHeight = 0.5f;
        // Single step: shift = (height − reference) × depth × view.xy / (view.z + 0.42).
        public AtlasParallaxMethod Method = AtlasParallaxMethod.SingleStep;
    }
}
