using System;
using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Shader-independent, non-serialized description of the *meaning* of a material.
    /// It contains no source shader property identifiers, parser IDs or raw shader enum values.
    /// Source parsers must translate their conventions into these semantics before returning.
    /// An output generator can implement any subset of the union and report unsupported features.
    /// </summary>
    public sealed class AtlasUnifiedMaterial
    {
        public readonly AtlasRenderDefinition Render = new AtlasRenderDefinition();
        public readonly AtlasSurfaceDefinition Surface = new AtlasSurfaceDefinition();
        public readonly AtlasLightingDefinition Lighting = new AtlasLightingDefinition();
        public readonly AtlasExternalLightingDefinition ExternalLighting = new AtlasExternalLightingDefinition();
        public readonly AtlasAudioReactiveDefinition AudioReactive = new AtlasAudioReactiveDefinition();
        public readonly AtlasColorProcessingDefinition ColorProcessing = new AtlasColorProcessingDefinition();

        // Layer order is significant (low index to high index). These are never shader-specific arrays.
        public readonly List<AtlasNormalLayer> Normals = new List<AtlasNormalLayer>();
        public readonly List<AtlasEmissionLayer> Emissions = new List<AtlasEmissionLayer>();
        public readonly List<AtlasDetailLayer> Details = new List<AtlasDetailLayer>();
        public readonly List<AtlasDecalLayer> Decals = new List<AtlasDecalLayer>();
        public readonly List<AtlasMatcapLayer> Matcaps = new List<AtlasMatcapLayer>();
        public readonly List<AtlasReflectionLayer> Reflections = new List<AtlasReflectionLayer>();
        public readonly List<AtlasRimLayer> Rims = new List<AtlasRimLayer>();
        public readonly List<AtlasSubsurfaceLayer> Subsurfaces = new List<AtlasSubsurfaceLayer>();
        public readonly List<AtlasNamedMask> Masks = new List<AtlasNamedMask>();
        // Reusable mask resources and semantic routing (independent of any shader identity).
        public readonly List<AtlasGlobalMaskResource> GlobalMaskResources = new List<AtlasGlobalMaskResource>();
        public readonly List<AtlasGlobalMaskBinding> GlobalMaskBindings = new List<AtlasGlobalMaskBinding>();
        public readonly List<AtlasColorMaskLayer> ColorMasks = new List<AtlasColorMaskLayer>();
        // Material properties which can be driven by an external animation clip.
        // Curves and target bindings are intentionally supplied separately by the avatar pipeline.
        public readonly List<AtlasAnimatableParameter> AnimatableParameters = new List<AtlasAnimatableParameter>();

        // Extensible union of additional semantically-defined features. IDs and parameter names
        // describe behavior, NEVER source shader property names (e.g. "lighting.indirect-rim").
        public readonly List<AtlasSemanticFeature> Features = new List<AtlasSemanticFeature>();
        // Normalization issues are explicit. A generator must not treat unresolved features as exact.
        public readonly List<AtlasNormalizationIssue> Issues = new List<AtlasNormalizationIssue>();

        public IEnumerable<AtlasTextureSample> Textures(AtlasTextureSemantic semantic)
        {
            switch (semantic)
            {
                case AtlasTextureSemantic.BaseColor:
                    if (Surface.BaseColor.Map != null && Surface.BaseColor.Map.Texture != null)
                        yield return Surface.BaseColor.Map;
                    break;
                case AtlasTextureSemantic.Normal:
                    foreach (var x in Normals) if (x.Enabled && x.Map != null && x.Map.Texture != null) yield return x.Map;
                    break;
                case AtlasTextureSemantic.Emission:
                    foreach (var x in Emissions) if (x.Enabled && x.Map != null && x.Map.Texture != null) yield return x.Map;
                    break;
                case AtlasTextureSemantic.Matcap:
                    foreach (var x in Matcaps) if (x.Enabled && x.Map != null && x.Map.Texture != null) yield return x.Map;
                    break;
                case AtlasTextureSemantic.GlobalMask:
                {
                    // A parser may describe the same global mask texture both as a resource
                    // and as a Global-purpose named mask; report each texture once.
                    var seen = new HashSet<Texture>();
                    foreach (var x in GlobalMaskResources)
                        if (x.Enabled && x.Texture != null && x.Texture.Texture != null && seen.Add(x.Texture.Texture)) yield return x.Texture;
                    foreach (var x in Masks)
                        if (x.Purpose == AtlasMaskPurpose.Global && x.Enabled && x.Map != null && x.Map.Texture != null && seen.Add(x.Map.Texture)) yield return x.Map;
                    break;
                }
                case AtlasTextureSemantic.Mask:
                    // Global masks are shared resources, not per-feature masks.
                    foreach (var x in Masks)
                        if (x.Purpose != AtlasMaskPurpose.Global && x.Enabled && x.Map != null && x.Map.Texture != null) yield return x.Map;
                    break;
                case AtlasTextureSemantic.Decal:
                    foreach (var x in Decals) if (x.Enabled && x.Map != null && x.Map.Texture != null) yield return x.Map;
                    break;
                case AtlasTextureSemantic.Metallic:
                    if (Surface.Metallic.Sample != null && Surface.Metallic.Sample.Texture != null) yield return Surface.Metallic.Sample;
                    break;
                case AtlasTextureSemantic.Roughness:
                    if (Surface.Roughness.Sample != null && Surface.Roughness.Sample.Texture != null) yield return Surface.Roughness.Sample;
                    break;
                case AtlasTextureSemantic.Occlusion:
                    if (Surface.Occlusion.Sample != null && Surface.Occlusion.Sample.Texture != null) yield return Surface.Occlusion.Sample;
                    break;
            }
        }

        public bool HasTexture(AtlasTextureSemantic semantic)
        {
            using (var it = Textures(semantic).GetEnumerator()) return it.MoveNext();
        }
    }

    public enum AtlasTextureSemantic { BaseColor, Normal, Emission, Matcap, Mask, GlobalMask, Decal, Metallic, Roughness, Occlusion }
    // A mode denotes an algorithm, not the shader that happens to implement it.
    // Closely related versions use one mode and behaviorally-equivalent defaults.
    public enum AtlasLightingMode
    {
        Unknown, Unlit, StandardMetallic, StandardSpecular, MobileVertexToon,
        ToonRamp, ToonMultilayer, Wrapped, Skin, ShadeMap, Flat, Realistic,
        Cloth, SignedDistanceField, LegacyToon
    }
    public enum AtlasSurfaceWorkflow { Metallic, Specular, Custom, None }
    public enum AtlasSurfaceShadingMode { UnityStandard, Filament, Other }
    public enum AtlasLightmapMode { Default, MonochromaticSphericalHarmonics, MonochromaticNoSpecular, Unknown }
    public enum AtlasDepthTest { Disabled, Never, Less, Equal, LessOrEqual, Greater, NotEqual, GreaterOrEqual, Always }
    public enum AtlasCullMode { Off, Front, Back }
    // Blend factors follow the conventional GPU blending equation; they are not a shader ID.
    public enum AtlasBlendFactor { Zero=0, One=1, DestinationColor=2, SourceColor=3,
        OneMinusDestinationColor=4, SourceAlpha=5, OneMinusSourceColor=6,
        DestinationAlpha=7, OneMinusDestinationAlpha=8, SourceAlphaSaturate=9,
        OneMinusSourceAlpha=10 }
    public enum AtlasSampleFilter { Default, Bicubic, Nearest, Stochastic, Supersampled }
    public enum AtlasDetailBlendMode { Alpha, Add, Multiply, MultiplyDouble, Replace, Overlay, Screen, Lerp, Unknown }
    public enum AtlasAudioEffectMode { Pulse, Scroll, Bar, UVHorizontal, Unknown }
    public enum AtlasColorMaskBlendMode { Multiply, Add, Unknown }
    public enum AtlasHueColorSpace { HSV, Oklab, Other }
    public enum AtlasMatcapUVMode { ViewNormal, UTSStyle, TopPinch, DoubleSided, Gradient, LightDirection, Unknown }

    public enum AtlasSurfaceMode { Opaque, Cutout, Transparent, Premultiplied, Additive, Unknown }
    public enum AtlasShadingModel { Unlit, StandardMetallic, StandardSpecular, Toon, Custom, Unknown }
    public enum AtlasBlendOperation { Replace, Multiply, Add, Alpha, Screen, Overlay, Subtract, Min, Max, MultiplyDouble, Unknown, Divide, Average }
    public enum AtlasCoordinateSpace { UV, ObjectPosition, WorldPosition, ViewPosition, ViewNormal, ReflectionVector, ScreenPosition, Triplanar, Panosphere, Polar, Distorted, Unknown }
    public enum AtlasColorInterpretation { ColorSRGB, LinearData, TangentNormal, ObjectNormal, WorldNormal }
    public enum AtlasChannel { Red, Green, Blue, Alpha, Luminance }
    public enum AtlasMaskPurpose { General, Alpha, BaseColor, Global }
    public enum AtlasNormalBlend { Replace, Additive, Whiteout, Reoriented, Unknown }
    public enum AtlasScalarCombineMode { Multiply, StrengthAroundOne }
    public enum AtlasNormalSpace { Tangent, Object, World }
    public enum AtlasMatcapProjection { ViewNormal, ReflectionVector, Unknown }
    public enum AtlasNormalizationQuality { Exact, Approximate, Unresolved }
    public enum AtlasNormalizationSeverity { Info, Warning, Error }

    public sealed class AtlasRenderDefinition
    {
        public AtlasSurfaceMode Mode = AtlasSurfaceMode.Opaque;
        public int Queue = -1;
        public float AlphaCutoff = 0.5f;
        public bool DoubleSided;
        public bool ZWrite = true;
        public bool AlphaToCoverage;
        public bool AlphaPremultiplied;
        public bool ApplyVertexColor;
        public bool ZClip = true;
        public float DepthOffsetFactor;
        public float DepthOffsetUnits;
        public int ColorWriteMask = 15;
        public bool IgnoreFog;
        public bool FlipBackfaceNormals;
        public bool EarlyDepth;
        public bool ReduceClipDistance;
        public bool Instancing;
        public AtlasCullMode Cull = AtlasCullMode.Back;
        public AtlasDepthTest DepthTest = AtlasDepthTest.LessOrEqual;
        public AtlasBlendOperation ColorBlend = AtlasBlendOperation.Replace;
        public AtlasBlendFactor SourceBlend = AtlasBlendFactor.One;
        public AtlasBlendFactor DestinationBlend = AtlasBlendFactor.Zero;
    }

    public sealed class AtlasSurfaceDefinition
    {
        public readonly AtlasColorInput BaseColor = new AtlasColorInput();
        // Canonical scalar meanings: metallic 0..1, roughness 0..1, occlusion 0..1.
        // Invert a smoothness map instead of exposing an input-shader-specific smoothness mode.
        public readonly AtlasScalarInput Metallic = new AtlasScalarInput { Constant = 0f };
        public readonly AtlasScalarInput Roughness = new AtlasScalarInput { Constant = 0.5f };
        public readonly AtlasScalarInput Occlusion = new AtlasScalarInput { Constant = 1f };
        public readonly AtlasColorInput Specular = new AtlasColorInput { Color = new Color(0.04f, 0.04f, 0.04f, 1f) };
        public AtlasMaskDefinition AlphaMask;
        public AtlasMaskDefinition BaseColorMask;
        public readonly AtlasScalarInput Height = new AtlasScalarInput();
        public AtlasShadingModel Model = AtlasShadingModel.Unknown;
        public AtlasSurfaceShadingMode ShadingMode = AtlasSurfaceShadingMode.UnityStandard;
        public AtlasSurfaceWorkflow Workflow = AtlasSurfaceWorkflow.Custom;
        public bool SpecularHighlights = true;
        public bool GlossyReflections = true;
        public bool GeometricSpecularAA;
        public float SpecularAAScreenSpaceVariance = 0.1f;
        public float SpecularAAThreshold = 0.2f;
        public float Reflectance = 0.04f;
        public float SpecularSharpness = 1f;
        public bool HasHeightMap;
        public float HeightStrength;
        public float HeightOffset;
        public int HeightSteps;
        public AtlasTextureSample PackedSurfaceMap;
        public bool UseVertexColors;
    }

    public sealed class AtlasLightingDefinition
    {
        public AtlasShadingModel Model = AtlasShadingModel.Unknown;
        public AtlasLightingMode Mode = AtlasLightingMode.Unknown;
        public AtlasTextureSample ShadowRamp;
        public float ShadowBoost;
        public float ShadowAlbedoTint;
        public float MinimumBrightness;
        public bool LimitBrightness;
        public float MaximumBrightness = 1f;
        public float ShadowStrength = 1f;
        public float ShadowBorder;
        public float ShadowBlur;
        public float ShadowOffset;
        public float WrapAmount;
        public float WrapNormalization;
        public float ShadowSoftness;
        public Color ShadowColor = Color.white;
        public bool ReceiveShadows = true;
        public bool UseNormalsForIndirect = true;
        public bool Monochromatic;
        public bool EnableAdditionalLights = true;
        public bool LimitAdditionalLights;
        public float AdditionalLightLimit = 1f;
        public AtlasLightmapMode LightmapMode = AtlasLightmapMode.Default;
        public Color AmbientTint = Color.white;
        public Color DirectTint = Color.white;
        public float DirectIntensity = 1f;
        public float IndirectIntensity = 1f;
        // Optional parameters are a semantic union, not one object per input lighting mode.
        public readonly Dictionary<string, AtlasSemanticValue> Parameters = new Dictionary<string, AtlasSemanticValue>();
        public readonly List<AtlasLightBand> ToonBands = new List<AtlasLightBand>();
    }
    public sealed class AtlasLightBand
    {
        public float Threshold;
        public float Softness;
        public float Strength = 1f;
        public UnityEngine.Color Color = UnityEngine.Color.white;
        public AtlasTextureSample ColorMap;
    }

    public sealed class AtlasUVMapping
    {
        // UV index is meaningful only for CoordinateSpace.UV; 0=UV0, 1=UV1, etc.
        public AtlasCoordinateSpace Space = AtlasCoordinateSpace.UV;
        // World/object choice for triplanar UV generation.
        public AtlasCoordinateSpace TriplanarBaseSpace = AtlasCoordinateSpace.ObjectPosition;
        public int UVIndex;
        public Vector2 Scale = Vector2.one;
        public Vector2 Offset = Vector2.zero;
        public float RotationDegrees;
        public Vector2 ScrollSpeed;
        public AtlasSampleFilter Sampling = AtlasSampleFilter.Default;
        public AtlasNormalizationQuality Quality = AtlasNormalizationQuality.Exact;
    }
    public sealed class AtlasTextureSample
    {
        public Texture Texture;
        public AtlasUVMapping Mapping = new AtlasUVMapping();
        public AtlasColorInterpretation Interpretation = AtlasColorInterpretation.ColorSRGB;
    }
    public sealed class AtlasMaskDefinition
    {
        // Null Sample means no texture mask. A uniform Constant alone is still valid.
        public AtlasTextureSample Sample;
        public AtlasChannel Channel = AtlasChannel.Red;
        public float Constant = 1f;
        public bool Invert;
        public float Strength = 1f;
    }
    // One resource can be referenced by many parameters; split sampling permits a unique
    // transform per RGBA channel without duplicating the actual texture.
    public sealed class AtlasGlobalMaskResource
    {
        public bool Enabled = true;
        public AtlasTextureSample Texture;
        public bool SplitChannels;
        public readonly AtlasUVMapping[] ChannelMappings = new AtlasUVMapping[4];
        public AtlasGlobalMaskResource()
        {
            for (int i=0; i<4; ++i) ChannelMappings[i] = new AtlasUVMapping();
        }
        // Never null: without a split or a texture, the first channel mapping is the default.
        public AtlasUVMapping MappingFor(AtlasChannel channel)
        {
            int index = (int)channel;
            if (SplitChannels && index >= 0 && index < 4) return ChannelMappings[index];
            return Texture != null ? Texture.Mapping : ChannelMappings[0];
        }
    }
    public enum AtlasGlobalMaskTarget
    {
        HueShift, Saturation, Brightness, Gamma, Alpha,
        DetailColor, DetailNormal, SecondaryNormalStrength,
        Emission, Matcap, CustomFeature
    }
    public sealed class AtlasGlobalMaskBinding
    {
        public AtlasGlobalMaskTarget Target;
        // Index into the corresponding feature's semantic layer list, where applicable.
        public int LayerIndex = -1;
        public int ResourceIndex = -1;
        public AtlasChannel Channel = AtlasChannel.Red;
        public AtlasBlendOperation Blend = AtlasBlendOperation.Multiply;
        // A future shader can add new targets without introducing input-shader properties.
        public string SemanticFeatureId;
        public bool Enabled = true;
    }
    public sealed class AtlasScalarInput
    {
        // value = Constant * sampledChannel (if Sample exists), after InvertSample.
        // All factors operate in normalized linear data space. No shader property IDs.
        public float Constant;
        public AtlasTextureSample Sample;
        public AtlasChannel Channel = AtlasChannel.Red;
        public bool InvertSample;
        // Applied after multiplying Constant by the sampled channel. Used for roughness=1-smoothness.
        public bool InvertResult;
        // StrengthAroundOne: 1 - (1 - sampled) * Constant (Unity-style occlusion strength).
        public AtlasScalarCombineMode CombineMode = AtlasScalarCombineMode.Multiply;
        public float Evaluate(float sampledChannel = 1f)
        {
            float channel = InvertSample ? 1f - sampledChannel : sampledChannel;
            float value = CombineMode == AtlasScalarCombineMode.StrengthAroundOne
                ? (Sample == null ? 1f : 1f - (1f - channel) * Constant)
                : Constant * (Sample == null ? 1f : channel);
            return InvertResult ? 1f - value : value;
        }
    }
    public sealed class AtlasColorInput
    {
        // Surface color = Color * Sample (or constant Color when Sample is null).
        public UnityEngine.Color Color = UnityEngine.Color.white;
        public AtlasTextureSample Map;
    }
    public abstract class AtlasLayerDefinition
    {
        public bool Enabled = true;
        public int Order;
        public AtlasTextureSample Map;
        public AtlasMaskDefinition Mask;
        // Additional independent masks are combined in sequence as required by the layer.
        public readonly List<AtlasMaskDefinition> AdditionalMasks = new List<AtlasMaskDefinition>();
        public AtlasBlendOperation Blend = AtlasBlendOperation.Alpha;
        public float Opacity = 1f;
        public Color Tint = Color.white;
        public AtlasNormalizationQuality Quality = AtlasNormalizationQuality.Exact;
    }
    public sealed class AtlasNormalLayer : AtlasLayerDefinition
    {
        public AtlasNormalSpace Space = AtlasNormalSpace.Tangent;
        public AtlasNormalBlend NormalBlend = AtlasNormalBlend.Unknown;
        public float Strength = 1f;
        public bool BentNormal;
        public bool StochasticSampling;
        public float SpecularOcclusionStrength = 1f;
        public bool AffectIndirectDiffuse;
        public AtlasGlobalMaskBinding StrengthGlobalMask;
    }
    public sealed class AtlasEmissionLayer : AtlasLayerDefinition
    {
        // Linear-light emission multiplier (not gamma encoded). Preserve HDR tint values.
        public float Intensity = 1f;
        public bool ReplaceBaseColor;
        public bool UseBaseColorAsMap;
        public float HueShift;
        public float Saturation = 1f;
        public bool BlinkEnabled;
        public float BlinkOffset;
        public bool AudioReactive;
    }
    public sealed class AtlasDetailLayer : AtlasLayerDefinition
    {
        public AtlasDetailBlendMode DetailBlend = AtlasDetailBlendMode.MultiplyDouble;
        public AtlasTextureSample NormalMap;
        public float NormalStrength = 1f;
    }
    public sealed class AtlasDecalLayer : AtlasLayerDefinition
    {
        // Decal projection is expressed in Map.Mapping, not a Poiyomi mode number.
        public bool AffectNormals;
        public Vector2 Position = new Vector2(0.5f, 0.5f);
        public Vector2 Scale = Vector2.one;
        public float RotationDegrees;
        public float RotationSpeed;
        public Vector4 SideOffset;
        public bool Tiled;
        public float EmissionStrength;
        public float Alpha = 1f;
        public float AlphaIntensity = 1f;
        public float Depth;
        public AtlasBlendOperation ColorBlend = AtlasBlendOperation.Replace;
        public AtlasBlendOperation AlphaBlend = AtlasBlendOperation.Replace;
        public AtlasSemanticFeature Options;
        public AtlasAnimatableParameter EmissionAnimation;
        public AtlasAnimatableParameter AlphaAnimation;
    }
    public sealed class AtlasMatcapLayer : AtlasLayerDefinition
    {
        public AtlasMatcapProjection Projection = AtlasMatcapProjection.ViewNormal;
        public AtlasMatcapUVMode UVMode = AtlasMatcapUVMode.ViewNormal;
        public float Intensity = 1f;
        public float Border;
        public float Rotation;
        public float BaseColorMix;
        public float LightColorMix;
        public float EmissionStrength;
        public float HideInShadow;
        public bool HideWithReflectionProbe;
        public AtlasTextureSample CustomNormal;
        public float CustomNormalStrength = 1f;
        // A shader may weight multiple blend contributions at once (Poiyomi).
        // Mobile Toon uses a single enum and sets only one contribution.
        public readonly AtlasBlendWeights BlendWeights = new AtlasBlendWeights();
    }
    public sealed class AtlasBlendWeights
    {
        public float Replace;
        public float Multiply;
        public float Add;
        public float Mixed;
        public float Screen;
        public float UnlitAdd;
    }
    public sealed class AtlasReflectionLayer : AtlasLayerDefinition
    {
        public float Intensity = 1f;
        public float Roughness;
    }
    public sealed class AtlasRimLayer : AtlasLayerDefinition
    {
        public float Power = 2f;
        public float Width = 0.5f;
        public float Range;
        public float Softness;
        public float AlbedoTint;
        public float Intensity = 1f;
        public bool Environmental;
        public bool Linear;
        public bool Invert;
        public float NormalStrength = 1f;
        public float BaseColorMix;
        public float EmissionStrength;
        public float Brightness = 1f;
        public AtlasHueColorSpace HueSpace = AtlasHueColorSpace.HSV;
        public AtlasSemanticFeature Options;
    }
    public sealed class AtlasSubsurfaceLayer : AtlasLayerDefinition
    {
        public float Thickness = 0.5f;
    }
    public sealed class AtlasNamedMask : AtlasLayerDefinition
    {
        public AtlasMaskPurpose Purpose = AtlasMaskPurpose.General;
        public AtlasChannel Channel = AtlasChannel.Red;
        public bool Invert;
    }

    public sealed class AtlasColorMaskLayer
    {
        public bool Enabled = true;
        public AtlasMaskDefinition Mask;
        public Color Tint = Color.white;
        public float EmissionStrength;
        public AtlasColorMaskBlendMode Blend = AtlasColorMaskBlendMode.Multiply;
        public int Index;
    }
    public sealed class AtlasColorProcessingDefinition
    {
        // One full hue revolution equals 1.0, regardless of source shader's units.
        public float HueShift;
        public float DetailHueShift;
        public float EmissionHueShift;
        public AtlasHueColorSpace HueColorSpace = AtlasHueColorSpace.HSV;
        public float Saturation = 1f;
        // Brightness is an additive offset; multiplier is a separate, shared semantic.
        public float Brightness;
        public float BrightnessMultiplier = 1f;
        public float Contrast = 1f;
        public float Gamma = 1f;
        public AtlasMaskDefinition HueMask;
        public bool Enabled;
        public bool ClampOutput;
        public float Chromatize;
        public AtlasColorInput Tint = new AtlasColorInput();
    }
    public sealed class AtlasAudioReactiveDefinition
    {
        public bool Enabled;
        public AtlasAudioEffectMode Mode = AtlasAudioEffectMode.Unknown;
        public AtlasBlendOperation Blend = AtlasBlendOperation.Replace;
        public AtlasMaskDefinition Mask;
        public Color Tint = Color.white;
        public float Intensity = 1f;
        public int Band;
        public float Smoothing;
        public bool UseFallback;
        public bool AnimatableToggle;
        public readonly float[] BandSmoothing = new float[4];
        public readonly float[] BandOverrides = new float[4];
        public float FallbackSpeed;
        public float ScrollScale = 1f;
        public bool CenterOut;
        public float BarSmoothing;
    }
    public sealed class AtlasExternalLightingDefinition
    {
        public bool EnableLightVolumes;
        public bool EnableLTCGI;
        public bool EnableAreaLit;
        public bool IgnoreRealtimeGI;
        public float LightVolumeIntensity = 1f;
        public float AreaLitStrength = 1f;
        public float LTCGIStrength = 1f;
        public bool LTCGIAnimationToggle;
        public Color LTCGIDiffuseTint = Color.white;
        public Color LTCGISpecularTint = Color.white;
        public bool LTCGIUseMaterialReflections;
        public bool LTCGIUseExposureOcclusion;
        public float LTCGISpecularMultiplier = 1f;
        public float LTCGIDiffuseMultiplier = 1f;
    }

    // Shader-independent extension point for features not yet promoted into strongly-typed classes.
    // Keys must describe cross-shader semantics; input-specific property names are prohibited.
    public sealed class AtlasSemanticFeature
    {
        public string Id;
        public bool Enabled = true;
        public AtlasNormalizationQuality Quality = AtlasNormalizationQuality.Exact;
        public readonly Dictionary<string, AtlasSemanticValue> Parameters = new Dictionary<string, AtlasSemanticValue>();
        public readonly Dictionary<string, AtlasTextureSample> Textures = new Dictionary<string, AtlasTextureSample>();
        public readonly Dictionary<string, AtlasScalarInput> Scalars = new Dictionary<string, AtlasScalarInput>();
        public readonly Dictionary<string, AtlasColorInput> Colors = new Dictionary<string, AtlasColorInput>();
        public readonly List<AtlasMaskDefinition> Masks = new List<AtlasMaskDefinition>();
    }
    public sealed class AtlasAnimatableParameter
    {
        // A stable semantic target such as "decal[0].alpha", never a Poiyomi property ID.
        public string Target;
        public bool MaterialAnimationEnabled;
        public bool CurvesAvailable;
    }
    public enum AtlasSemanticValueKind { Number, Boolean, Color, Vector, Choice }
    public sealed class AtlasSemanticValue
    {
        public AtlasSemanticValueKind Kind;
        public float Number;
        public bool Boolean;
        public Color Color;
        public Vector4 Vector;
        // Cross-shader semantic choice, NOT the integer enum of an input shader.
        public string Choice;
    }
    public sealed class AtlasNormalizationIssue
    {
        public string FeatureId;
        public AtlasNormalizationSeverity Severity;
        public string Message;
        public AtlasNormalizationIssue(string featureId, AtlasNormalizationSeverity severity, string message)
        { FeatureId = featureId; Severity = severity; Message = message; }
    }
}
