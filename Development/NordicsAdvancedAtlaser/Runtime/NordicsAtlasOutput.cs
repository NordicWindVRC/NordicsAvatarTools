using System;
using System.Collections.Generic;
using UnityEngine;
using nadena.dev.ndmf;

namespace NordicWind.AdvancedAtlaser
{
    [Serializable]
    public sealed class AtlasSourceEntry
    {
        public Material Material;
        public Rect UVRegion = new Rect(0, 0, 1, 1);
    }

    [Serializable]
    public sealed class AtlasTextureDimensions
    {
        public bool Enabled = true;
        public int Width = 2048;
        public int Height = 2048;
    }

    public enum AtlasTextureSizeMode { MasterAspect, CustomAspect, Square }
    public enum AtlasSettingKind { Texture, Toggle, Color, Float, Integer, Vector, Description }

    [Serializable]
    public sealed class AtlasOutputSetting
    {
        public string Id;
        public AtlasSettingKind Kind;
        public bool BoolValue;
        public float FloatValue;
        public int IntValue;
        public Color ColorValue = Color.white;
        public Vector4 VectorValue;
        public string TextValue;
        public AtlasTextureDimensions Texture = new AtlasTextureDimensions();
        public AtlasTextureSizeMode SizeMode;
        public int CustomAspectWidth = 1;
        public int CustomAspectHeight = 1;
    }

    // The same serialization container is used by EVERY output shader.
    // Settings are a single semantic union, shared across all output shaders.
    [Serializable]
    public sealed class AtlasOutputConfiguration
    {
        public string OutputName = "New Atlas";
        public string ShaderId = "poiyomi10"; // preserved stable ID
        [Min(0)] public int RegionPadding = 8;
        public List<AtlasSourceEntry> Sources = new List<AtlasSourceEntry>();
        public AtlasTextureDimensions MainTexture = new AtlasTextureDimensions();
        public List<AtlasOutputSetting> Settings = new List<AtlasOutputSetting>();

        public AtlasOutputSetting Find(string id)
        {
            if (Settings == null || string.IsNullOrEmpty(id)) return null;
            return Settings.Find(x => x != null && x.Id == id);
        }
    }

    [AddComponentMenu("NordicWind/Nordic's Atlas Output")]
    public sealed class NordicsAtlasOutput : MonoBehaviour, INDMFEditorOnly
    {
        // The sole serialized output configuration. There are no legacy shadow fields,
        // migration callbacks or per-shader copies that can overwrite MainTexture.
        public AtlasOutputConfiguration Configuration = new AtlasOutputConfiguration();
    }
}
