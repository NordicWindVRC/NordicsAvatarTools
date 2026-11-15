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
        // 0–1: base × lerp(1, vertex colour, amount).
        public float Amount = 1f;
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
        // Source Mask: the mask × this.
        public float MaskMultiplier = 1f;
        // 0–1: lerp(1, alpha, strength).
        public float Strength = 1f;
    }

    // 2.3: a tangent-space normal map. Without a map the surface is flat.
    public sealed class AtlasNormal
    {
        public AtlasTextureReference Map;
        public float Strength = 1f;
        // The triangle's face normal instead of the smooth normal: a faceted look.
        public bool FlatShading;
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
        // 0–1: how much occlusion darkens direct light, ambient light and reflections.
        public float OcclusionDirect;
        public float OcclusionIndirect = 1f;
        public float OcclusionReflections = 1f;
        // Occlusion darkens toward this colour instead of black; null: black.
        public AtlasColorInput OcclusionTint;
        public readonly AtlasHighlights Highlights = new AtlasHighlights();
        public readonly AtlasReflections Reflections = new AtlasReflections();
        // Highlights, reflections, matcaps and rims use the mesh normal, not the normal map.
        public bool SpecularFromMeshNormal;
        public readonly AtlasSpecularAntiAliasing SpecularAntiAliasing = new AtlasSpecularAntiAliasing();
    }

    // 2.4: direct-light highlights.
    public sealed class AtlasHighlights
    {
        public bool Enabled = true;
        public float Strength = 1f;
        // Linear.
        public UnityEngine.Color Tint = UnityEngine.Color.white;
        // Null: everywhere.
        public AtlasMask Mask;
        public bool RoughnessOverride;
        public float Roughness = 0.5f;
        // 0–1: blends the highlight toward a hard edge, lerp(h, smoothstep(0.5, 0.51, h), sharpness).
        public float Sharpness;
        // 0: smooth; otherwise floor(h × steps) / steps.
        public int Steps;
        public bool RealtimeLightOnly;
        // 0–1: the highlight colour toward the base colour.
        public float BaseColorMix;
        // Not scaled by the diffuse light.
        public bool IgnoreLightColor;
        public AtlasHighlightShape Shape = AtlasHighlightShape.Round;
        public readonly AtlasStretchedHighlight Stretched = new AtlasStretchedHighlight();
        // Shape Both: 1 = round, 0 = stretched; null: round.
        public AtlasMask ShapeMask;
    }

    public enum AtlasHighlightShape { Round, Stretched, Both }

    // Stretched along the bitangent (hair, brushed metal): D = saturate(1 / (w × f²)),
    // f = (T·H)² + (B·H)² / w² + (N·H)²; fresnel scaled by metallic.
    public sealed class AtlasStretchedHighlight
    {
        public float Width = 1f;
        // A second layer of width SecondWidth, added with SecondStrength, or laid over by its own value.
        public float SecondWidth = 1f;
        public float SecondStrength = 0.1f;
        public bool SecondOver;
        public float Strength = 1f;
        // 0: smooth.
        public int Steps;
        // Strand ripples along U.
        public float RippleFrequency = 1f;
        public float RippleAmplitude = 0.5f;
        public float RippleStrength;
        public float RippleContinuity = 1f;
    }

    public enum AtlasReflectionSource
    {
        // The scene's reflection probes.
        Probes,
        // A fixed cubemap.
        Cubemap,
        // A flat colour from the sky's ambient specular (Unity's reflections off).
        AmbientColor
    }

    // 2.4: environment reflections. Off: none.
    public sealed class AtlasReflections
    {
        public bool Enabled = true;
        public float Strength = 1f;
        // Linear.
        public UnityEngine.Color Tint = UnityEngine.Color.white;
        // Null: everywhere.
        public AtlasMask Mask;
        // 1 = full fresnel falloff, 0 = none.
        public float FresnelStrength = 1f;
        public bool RoughnessOverride;
        public float Roughness = 0.5f;
        public AtlasReflectionSource Source = AtlasReflectionSource.Probes;
        // For Source Cubemap.
        public AtlasTextureReference Cubemap;
        // A fixed cubemap scaled by the surroundings' brightness.
        public bool CubemapLit;
        // 0: smooth; otherwise quantised to this many steps, less on rough surfaces.
        public int Steps;
        // 0–1: stronger where lit, lerp(1, N·L + 0.3, amount).
        public float LightBased;
        // Used when the scene has no reflection probes; null: none.
        public AtlasTextureReference FallbackCubemap;
    }

    // Roughens where the normal changes quickly on screen, so small or curved detail doesn't
    // sparkle (projected-space normal filtering, Tokuyoshi 2021).
    public sealed class AtlasSpecularAntiAliasing
    {
        public bool Enabled;
        // Screen-space variance scale, 0–1.
        public float Variance = 0.1f;
        // The most it may add to roughness, 0–1.
        public float Threshold = 0.2f;
    }

    public enum AtlasParallaxMethod
    {
        // shift = (height − reference) × depth × view.xy / (view.z + 0.42)
        SingleStep,
        // March down from height 1 in Steps steps, then three halving refinement steps.
        MultiStep
    }

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
        // Multi-step.
        public int Steps = 25;
        public float HeightOffset;
    }
}
