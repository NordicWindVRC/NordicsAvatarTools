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

    // An optional output texture with its own size, used by output-shader settings.
    [Serializable]
    public sealed class AtlasTextureDimensions
    {
        public bool enabled = true;
        public int width = 2048;
        public int height = 2048;
    }

    public enum AtlasTextureSizeMode { MasterAspect, CustomAspect, Square }
    public enum AtlasSettingKind { Texture, Toggle, Color, Float, Integer, Vector, Description }

    // One output-shader setting. Each output shader declares its own settings.
    [Serializable]
    public sealed class AtlasOutputSetting
    {
        public string id;
        public AtlasSettingKind kind;
        public bool boolValue;
        public float floatValue;
        public int intValue;
        public Color colorValue = Color.white;
        public Vector4 vectorValue;
        public string textValue;
        public AtlasTextureDimensions texture = new AtlasTextureDimensions();
        public AtlasTextureSizeMode sizeMode;
        public int customAspectWidth = 1;
        public int customAspectHeight = 1;
    }

    // The same serialization container is used by every output shader.
    [Serializable]
    public sealed class AtlasOutputConfiguration
    {
        public string outputName = "New Atlas";
        public string shaderId = "poiyomi10"; // stable output shader ID
        // Space between source regions in the atlas, in pixels. Only used with two or more sources.
        [Min(0)] public int regionPadding = 4;
        public List<AtlasSourceEntry> sources = new List<AtlasSourceEntry>();
        public AtlasTextureSize mainTexture = new AtlasTextureSize();
        public List<AtlasOutputSetting> settings = new List<AtlasOutputSetting>();

        public AtlasOutputSetting Find(string id)
        {
            if (settings == null || string.IsNullOrEmpty(id)) return null;
            return settings.Find(x => x != null && x.id == id);
        }
    }

    [AddComponentMenu("NordicWind/Nordic's Atlas Output")]
    public sealed class NordicsAtlasOutput : MonoBehaviour, INDMFEditorOnly
    {
        public AtlasOutputConfiguration configuration = new AtlasOutputConfiguration();
    }
}
