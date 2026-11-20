using System;
using System.Collections.Generic;
using UnityEngine;
using nadena.dev.ndmf;

namespace NordicWind.AdvancedAtlaser
{
    [Serializable]
    public sealed class AtlasSourceEntry
    {
        public Material material;
        public Rect uvRegion = new Rect(0, 0, 1, 1);
        [Tooltip("Bake a normal map that restores the original mesh's shading on the reduced mesh. " +
                 "Turn off to skip it for this material, for example when its mesh isn't reduced.")]
        public bool normalCorrection = true;
        [Tooltip("Optional normal map baked for this mesh, for example for a lower-detail (Quest) version " +
                 "of a PC mesh. It's applied at strength 1, beneath the build's normal correction and the " +
                 "material's own normal maps. It doesn't replace the build's correction, which only sees " +
                 "changes made during the build.")]
        public Texture2D prebakedNormalMap;
    }

    // The atlas's main texture size. Always used; other output textures can follow it.
    [Serializable]
    public sealed class AtlasTextureSize
    {
        public int width = 4096;
        public int height = 4096;
    }

    /// <summary>
    /// The user's choices for one output feature. Stored by feature ID, not by output shader,
    /// so changing the output shader keeps every choice the new shader supports.
    /// </summary>
    [Serializable]
    public sealed class AtlasFeatureSetting
    {
        /// <summary>The feature's stable ID.</summary>
        public string featureId;
        /// <summary>
        /// The chosen mode's ID. Empty: the feature's default. A mode the output shader doesn't
        /// support is kept, and the default is used until a shader supports it again.
        /// </summary>
        public string mode = "";
        /// <summary>The source whose values a shared setting takes. None: the source with the largest region.</summary>
        public Material referenceSource;
        /// <summary>Sources the feature is turned off for.</summary>
        public List<Material> disabledSources = new List<Material>();
        /// <summary>The chosen mode's settings, by ID.</summary>
        public List<AtlasSettingValue> values = new List<AtlasSettingValue>();
    }

    /// <summary>One value of a feature's mode. The feature declares which of the fields it uses.</summary>
    [Serializable]
    public sealed class AtlasSettingValue
    {
        /// <summary>The value's ID within its feature.</summary>
        public string id;
        /// <summary>The value when it is a switch.</summary>
        public bool toggle;
        /// <summary>The value when it is a number.</summary>
        public float number;
        /// <summary>The value when it is a color.</summary>
        public Color color = Color.white;
        /// <summary>The value when it names something, such as another item to combine with.</summary>
        public string text = "";
    }

    /// <summary>A GPU texture format an output texture can be compressed to.</summary>
    public enum AtlasTextureFormat
    {
        /// <summary>DXT1: RGB, 4 bits per pixel.</summary>
        BC1,
        /// <summary>DXT5: RGBA, 8 bits per pixel.</summary>
        BC3,
        /// <summary>Two channels, 8 bits per pixel: normal maps.</summary>
        BC5,
        /// <summary>RGBA, 8 bits per pixel, higher quality than BC3.</summary>
        BC7,
        /// <summary>ASTC 4×4: 8 bits per pixel.</summary>
        ASTC4x4,
        /// <summary>ASTC 5×5: about 5.1 bits per pixel.</summary>
        ASTC5x5,
        /// <summary>ASTC 6×6: about 3.6 bits per pixel.</summary>
        ASTC6x6,
        /// <summary>ASTC 8×8: 2 bits per pixel.</summary>
        ASTC8x8,
        /// <summary>ASTC 10×10: about 1.3 bits per pixel.</summary>
        ASTC10x10,
        /// <summary>ASTC 12×12: about 0.9 bits per pixel.</summary>
        ASTC12x12,
        /// <summary>Uncompressed RGBA, 32 bits per pixel.</summary>
        RGBA32
    }

    /// <summary>The formats one platform's output textures get, by kind of texture.</summary>
    [Serializable]
    public sealed class AtlasPlatformFormats
    {
        /// <summary>Color without alpha.</summary>
        public AtlasTextureFormat color;
        /// <summary>Color with alpha.</summary>
        public AtlasTextureFormat colorAlpha;
        /// <summary>Tangent-space normal maps.</summary>
        public AtlasTextureFormat normal;
        /// <summary>Linear data and masks.</summary>
        public AtlasTextureFormat data;
    }

    /// <summary>An output texture other than the main one: on or off, and its size.</summary>
    [Serializable]
    public sealed class AtlasTextureSetting
    {
        /// <summary>The texture's ID in the output shader's texture layout.</summary>
        public string textureId;
        /// <summary>Off: the features using it fall back (optional textures only).</summary>
        public bool enabled = true;
        /// <summary>Width in pixels.</summary>
        public int width = 1024;
        /// <summary>Height in pixels.</summary>
        public int height = 1024;
    }

    /// <summary>The directions of the virtual lights that bakes use (ATLAS_OUTPUT.md, "Virtual
    /// light rigs").</summary>
    public enum AtlasLightRigKind
    {
        /// <summary>The key light from one side, the fill from the other. Sources with
        /// overlapping UVs use the symmetric rig.</summary>
        Classic,
        /// <summary>Every light centered, so mirrored halves get the same light.</summary>
        Symmetric
    }

    /// <summary>One light of the virtual rig. Its direction is set by the rig.</summary>
    [Serializable]
    public sealed class AtlasBakeLight
    {
        /// <summary>Multiplies the light's role weights.</summary>
        [Min(0)] public float intensity = 1f;
        /// <summary>The light's color temperature, in kelvin.</summary>
        [Range(1500, 15000)] public float temperature = 6500f;
        /// <summary>0: a sharp light; 1: a very wide one. Softens the edge between the lit
        /// and the shadow side, and widens highlights.</summary>
        [Range(0, 1)] public float softness = 0.5f;

        public AtlasBakeLight() { }

        public AtlasBakeLight(float temperature, float softness)
        {
            this.temperature = temperature;
            this.softness = softness;
        }
    }

    /// <summary>The settings of the bakes that use the virtual light rig (the inspector's
    /// Baking section).</summary>
    [Serializable]
    public sealed class AtlasBakeSettings
    {
        public AtlasLightRigKind rig = AtlasLightRigKind.Classic;
        /// <summary>Scales every lit bake.</summary>
        [Range(0, 1)] public float strength = 1f;
        /// <summary>How much of the lights' color is baked: 0, their brightness alone; 1, their
        /// full color.</summary>
        [Range(0, 1)] public float colorInfluence = 0.25f;
        /// <summary>Normal-map shading baked into the base color.</summary>
        [Range(0, 1)] public float normalShading = 1f;
        /// <summary>Ambient occlusion baked into the base color.</summary>
        [Range(0, 1)] public float occlusion = 1f;
        /// <summary>The main light: the form and the main highlight.</summary>
        public AtlasBakeLight key = new AtlasBakeLight(5000f, 0.4f);
        /// <summary>Lifts the shadow side without a second highlight.</summary>
        public AtlasBakeLight fill = new AtlasBakeLight(7500f, 0.9f);
        /// <summary>A crisp edge on the silhouette and highlights on hair tops.</summary>
        public AtlasBakeLight back = new AtlasBakeLight(6500f, 0.15f);
    }

    // The same serialization container is used by every output shader.
    [Serializable]
    public sealed class AtlasOutputConfiguration
    {
        public string outputName = "New Atlas";
        // Stable output shader ID. Empty until the editor picks one for a new output
        // (ATLAS_OUTPUT.md, "Auto setup") or the user chooses one.
        public string shaderId = "";
        // Space between source regions in the atlas, in pixels. Only used with two or more sources.
        [Min(0)] public int regionPadding = 4;
        public List<AtlasSourceEntry> sources = new List<AtlasSourceEntry>();
        public AtlasTextureSize mainTexture = new AtlasTextureSize();
        public List<AtlasFeatureSetting> features = new List<AtlasFeatureSetting>();
        public List<AtlasTextureSetting> textures = new List<AtlasTextureSetting>();
        /// <summary>
        /// Crunch compression where a format has a crunched form (BC1, BC3), kept only when it
        /// makes the texture at least 10% smaller.
        /// </summary>
        public bool crunch = true;
        /// <summary>The formats the textures get on PC; the memory estimates follow them.</summary>
        public AtlasPlatformFormats pcFormats = new AtlasPlatformFormats
        {
            color = AtlasTextureFormat.BC1, colorAlpha = AtlasTextureFormat.BC7,
            normal = AtlasTextureFormat.BC5, data = AtlasTextureFormat.BC7
        };
        /// <summary>The formats the textures get on Android; the memory estimates follow them.</summary>
        public AtlasPlatformFormats androidFormats = new AtlasPlatformFormats
        {
            color = AtlasTextureFormat.ASTC6x6, colorAlpha = AtlasTextureFormat.ASTC6x6,
            normal = AtlasTextureFormat.ASTC4x4, data = AtlasTextureFormat.ASTC6x6
        };
        /// <summary>The virtual light rig and the strengths of the bakes that use it.</summary>
        public AtlasBakeSettings baking = new AtlasBakeSettings();

        public AtlasFeatureSetting Feature(string featureId)
        {
            if (features == null || string.IsNullOrEmpty(featureId)) return null;
            return features.Find(x => x != null && x.featureId == featureId);
        }

        public AtlasTextureSetting Texture(string textureId)
        {
            if (textures == null || string.IsNullOrEmpty(textureId)) return null;
            return textures.Find(x => x != null && x.textureId == textureId);
        }
    }

    [AddComponentMenu("NordicWind/Nordic's Atlas Output")]
    public sealed class NordicsAtlasOutput : MonoBehaviour, INDMFEditorOnly
    {
        public AtlasOutputConfiguration configuration = new AtlasOutputConfiguration();
    }
}
