namespace NordicWind.AdvancedAtlaser.Editor
{
    // 2: what the surface is made of (stage S1) and how it reflects light (S2).
    public sealed class AtlasSurface
    {
        // 2.1: base color = color input × vertex color (where enabled).
        public readonly AtlasColorInput BaseColor = new AtlasColorInput();
        public readonly AtlasVertexColor VertexColor = new AtlasVertexColor();
        // 2.2
        public readonly AtlasAlpha Alpha = new AtlasAlpha();
        // 2.3: the primary normal. Surface layers targeting the normal are composited on top.
        public readonly AtlasNormal Normal = new AtlasNormal();
        public readonly AtlasBentNormal BentNormal = new AtlasBentNormal();
        // 2.4
        public readonly AtlasSurfaceResponse Response = new AtlasSurfaceResponse();
        // 2.5
        public readonly AtlasParallax Parallax = new AtlasParallax();
    }

    // 2.1: how the mesh's vertex colors change the base color.
    public sealed class AtlasVertexColor
    {
        public bool MultiplyColor;
        public bool MultiplyAlpha;
        // 0–1: base × lerp(1, vertex color, amount).
        public float Amount = 1f;
        // 0–1: for MultiplyAlpha, alpha × lerp(1, vertex alpha, amount).
        public float AlphaAmount = 1f;
        // Off: vertex colors are used as stored, as linear values. On: converted from sRGB first.
        public bool SrgbData;
        // The color's hue shifted (after the conversion) by HueShift + HueSpeed × t turns, or set to it.
        public bool HueEnabled;
        public float HueShift;
        public float HueSpeed;
        public AtlasHueMethod HueMethod = AtlasHueMethod.Oklab;
        public bool HueSet;
    }

    public enum AtlasAlphaSource
    {
        // Color alpha × texture alpha.
        BaseColorAlpha,
        // The base color's color alpha alone, when the texture's alpha means something else.
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
        // Source BaseColorAlpha with a mask: how the mask combines with it (saturated).
        public AtlasAlphaMaskBlend MaskBlend = AtlasAlphaMaskBlend.Replace;
        // saturate(alpha + offset), before the modifiers.
        public float Offset;
        // A mask bus channel (1.5) combined with the alpha after the offset by BusBlend
        // (saturated), before the modifiers; −1: none.
        public int Bus = -1;
        public AtlasMaskBlend BusBlend = AtlasMaskBlend.Multiply;
        // In order, each multiplies alpha.
        public readonly System.Collections.Generic.List<AtlasAlphaModifier> Modifiers = new System.Collections.Generic.List<AtlasAlphaModifier>();
        // Alpha-to-coverage edges sharpened and kept across mip levels.
        public bool SharpenedCoverage;
        public float MipScale = 0.25f;
        // An 8×8 ordered dither lowers alpha where it's partial.
        public bool Dithering;
        public float DitherGradient = 0.1f;
        public float DitherBias;
        // Screen-door transparency instead: a pixel is kept where alpha exceeds the dither (from 1/64
        // to 1, less half a step), then alpha is 1.
        public bool DitherCutout;
        // 0–1: lerp(1, alpha, strength).
        public float Strength = 1f;
    }

    public enum AtlasAlphaMaskBlend { Replace, Multiply, Add, Subtract }

    public enum AtlasAlphaModifierKind
    {
        // lerp(MinAlpha, MaxAlpha, smoothstep(MinDistance, MaxDistance, distance to the camera)).
        DistanceFade,
        // saturate(1 − smoothstep(min(Sharpness, Width), Width, N·V)), lerp(1, that, Intensity), Invert.
        ViewAngleFade,
        // How directly the camera and the model face each other.
        AngleFade,
        // Amount added, saturated; for audio modulators.
        Add
    }

    public enum AtlasAngleFadeType { CameraFacesModel, ModelFacesCamera, Both }

    // 2.2: a live change to alpha (stage S7).
    public sealed class AtlasAlphaModifier
    {
        // Where it applies; null: everywhere.
        public AtlasMask Mask;
        public AtlasAlphaModifierKind Kind;
        // Distance fade: from the object's position or the pixel's.
        public bool PixelPosition = true;
        public float MinDistance, MaxDistance;
        public float MinAlpha, MaxAlpha = 1f;
        // View-angle fade.
        public float Intensity, Sharpness = 0.5f, Width = 0.5f;
        public bool Invert;
        // Angle fade: angle ranges in degrees, the model's forward direction (object space), compared
        // from the object or the vertex position.
        public AtlasAngleFadeType AngleType;
        public float CameraAngleMin, CameraAngleMax, ModelAngleMin, ModelAngleMax;
        public UnityEngine.Vector3 Forward = UnityEngine.Vector3.forward;
        public bool FromVertex;
        // Add.
        public float Amount;
    }

    // 2.3: the unoccluded direction, for specular occlusion and, optionally, indirect diffuse.
    public sealed class AtlasBentNormal
    {
        // Tangent space; null: none.
        public AtlasTextureReference Map;
        public float SpecularOcclusion = 1f;
        public bool IndirectDiffuse = true;
    }

    // 2.3: a tangent-space normal map. Without a map the surface is flat.
    public sealed class AtlasNormal
    {
        public AtlasTextureReference Map;
        public float Strength = 1f;
        // The mesh normal bent toward the horizontal direction from a vertical axis through
        // CorrectOrigin (object space), by CorrectAmount, before normal maps.
        public float CorrectAmount;
        public UnityEngine.Vector3 CorrectOrigin;
        // The amount × the mask (a vertex color channel); null: everywhere.
        public AtlasMask CorrectMask;
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
        // The specular color (F0) of the specular workflow; null for the metallic workflow.
        public AtlasColorInput SpecularColor;
        // Darkens ambient light and reflections, not direct light. The constant is the strength:
        // 1 − (1 − map) × strength.
        public readonly AtlasScalarInput Occlusion = new AtlasScalarInput(1f) { Combine = AtlasScalarCombine.StrengthAroundOne };
        // 0–1: how much occlusion darkens direct light, ambient light and reflections.
        public float OcclusionDirect;
        public float OcclusionIndirect = 1f;
        public float OcclusionReflections = 1f;
        // Occlusion darkens toward this color instead of black; null: black.
        public AtlasColorInput OcclusionTint;
        public readonly AtlasHighlights Highlights = new AtlasHighlights();
        public readonly AtlasReflections Reflections = new AtlasReflections();
        // Highlights, reflections, matcaps and rims use the mesh normal, not the normal map.
        public bool SpecularFromMeshNormal;
        public readonly AtlasSpecularAntiAliasing SpecularAntiAliasing = new AtlasSpecularAntiAliasing();
        // GGX anisotropy of highlights and reflections.
        public readonly AtlasAnisotropy Anisotropy = new AtlasAnisotropy();
        // A clear reflective layer over the surface; null: none.
        public AtlasClearCoat ClearCoat;
        // Occlusion brightened by interreflection on light surfaces (Jimenez's multi-bounce fit).
        public bool MultiBounceOcclusion;
        // A soft cloth-like lobe at grazing angles; null: none.
        public AtlasSheen Sheen;
        // Sparkling flakes in the highlights; null: none.
        public AtlasGlints Glints;
    }

    // 2.4: Charlie sheen: D = (2 + 1/α) sin(θh)^(1/α) / 2π, α = roughness² (roughness at least
    // 0.07), visibility 1 / (4(N·L + N·V − N·L N·V)), × Color × Strength; the base layers below give
    // up the sheen's energy.
    public sealed class AtlasSheen
    {
        // Linear.
        public UnityEngine.Color Color = UnityEngine.Color.white;
        public readonly AtlasScalarInput Strength = new AtlasScalarInput(0f);
        public float Roughness = 0.5f;
    }

    // 2.4: glints: a multi-scale grid of flakes on a UV at Density flakes per UV unit, each with a
    // hashed position and normal; a flake sparkles where its normal is the half vector (spread by
    // FlakeRoughness, sized by Size), filtered by the pixel's footprint so distant flakes average
    // into a smooth lobe of Roughness. Flake color lerp(Color, the base F0, BaseReflectanceMix),
    // varied toward rainbow hues by ColorVariation; the mask keeps that fraction of the flakes.
    public sealed class AtlasGlints
    {
        public AtlasTextureSpace Space = AtlasTextureSpace.UV0;
        // Null: everywhere.
        public AtlasMask Mask;
        // Linear.
        public UnityEngine.Color Color = new UnityEngine.Color(0.95f, 0.95f, 0.95f, 1f);
        public float BaseReflectanceMix;
        public float ColorVariation;
        public float Roughness = 0.5f;
        public float Density = 2000f;
        public float FlakeRoughness = 0.02f;
        public float Size;
        // 0–1: the view from each eye toward the view from between them (VR).
        public float EyeBlend;
        // 0–2: sparkle from the ambient light.
        public float Ambient;
        public bool VertexLights;
    }

    // 2.4: anisotropic GGX. a = clamp(Amount + (sample × 2 − 1), −1, 1), the sample from the map's
    // channel or a vertex color channel; the highlight stretches along the tangent (a > 0) or the
    // bitangent, the reflection direction by ReflectionStretch, and RoughnessAnisotropy keeps
    // that much of the roughness along the stretch.
    public sealed class AtlasAnisotropy
    {
        public bool Enabled;
        public float Amount;
        // Data; null: no sample (0).
        public AtlasTextureReference Map;
        public AtlasChannel Channel = AtlasChannel.Red;
        // Instead of the map; null: none.
        public AtlasChannel? VertexColorChannel;
        public float ReflectionStretch = 1f;
        public float RoughnessAnisotropy = 1f;
        // Tangent-space direction (RG, 0.5 neutral) the stretch follows; null: the tangent.
        public AtlasTextureReference DirectionMap;
        // a = clamp(Amount × sample, −1, 1) instead (a missing map samples 1).
        public bool MapMultiplies;
        // Degrees: the stretch direction turned around the normal from the tangent.
        public float Rotation;
    }

    // 2.4: a dielectric coat (F0 4%) over the surface, with its own highlights and reflections.
    public sealed class AtlasClearCoat
    {
        // 0–1: where and how much.
        public readonly AtlasScalarInput Strength = new AtlasScalarInput(1f);
        public readonly AtlasScalarInput Roughness = new AtlasScalarInput(0f);
        public readonly AtlasHighlights Highlights = new AtlasHighlights();
        public readonly AtlasReflections Reflections = new AtlasReflections();
        // 0–1: how much the coat follows the normal map (0: the mesh normal).
        public float NormalMapAmount;
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
        // 0–1: the highlight color toward the base color.
        public float BaseColorMix;
        // Not scaled by the diffuse light.
        public bool IgnoreLightColor;
        // Not darkened by the received shadow.
        public bool IgnoreShadows;
        // A second lobe, the brighter of the two showing: strength (0: none) and its roughness
        // input's constant (the map and inversion shared).
        public float SecondLobeStrength;
        public float SecondLobeRoughnessConstant;
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
        // A flat color from the sky's ambient specular (Unity's reflections off).
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
        // 0: smooth; otherwise quantized to this many steps, less on rough surfaces.
        public int Steps;
        // 0–1: stronger where lit, lerp(1, N·L + 0.3, amount).
        public float LightBased;
        // Used when the scene has no reflection probes; null: none.
        public AtlasTextureReference FallbackCubemap;
        // 0–1: fades the reflection where the direct light is dim (exposure-based occlusion).
        public float ExposureOcclusion;
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
