using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // A quantity a feature produces, one per map: base colour, roughness, a matcap mask. An
    // output shader's textures provide them, one or several per texture (ATLAS_OUTPUT.md,
    // "Virtual maps and textures").
    public enum AtlasVirtualMap
    {
        BaseColor
    }

    // Which channels of a texture a virtual map fills.
    public enum AtlasTextureChannels { RGB, RGBA, R, G, B, A }

    // How a texture's size follows the main texture's.
    public enum AtlasTextureSizeMode
    {
        // The main texture itself: the size chosen as the main texture size.
        Main,
        // A size from those that keep the main texture's aspect.
        MainAspect,
        // Square, any size: matcaps.
        Square,
        // Any width and height: ramps and other lookups.
        Custom
    }

    // One virtual map in a texture: where it goes, and its value where it isn't in use.
    public sealed class AtlasTextureChannel
    {
        public AtlasVirtualMap Map;
        public AtlasTextureChannels Channels;
        public Color Neutral = Color.white;
    }

    // One texture of an output shader's texture layout.
    public sealed class AtlasOutputTexture
    {
        // Stable: stored in the settings and used in exported file names.
        public string Id;
        public string Label;
        public AtlasTextureSizeMode SizeMode = AtlasTextureSizeMode.MainAspect;
        // Colour textures are sRGB; data and masks are linear.
        public bool Srgb = true;
        // A tangent-space normal map.
        public bool Normal;
        // The size a new texture of mode Square or Custom starts at.
        public int DefaultSize = 512;
        public readonly List<AtlasTextureChannel> Channels = new List<AtlasTextureChannel>();

        public AtlasOutputTexture(string id, string label) { Id = id; Label = label; }

        public AtlasOutputTexture With(AtlasVirtualMap map, AtlasTextureChannels channels, Color? neutral = null)
        {
            Channels.Add(new AtlasTextureChannel { Map = map, Channels = channels, Neutral = neutral ?? Color.white });
            return this;
        }

        public bool Provides(AtlasVirtualMap map) => Channels.Exists(c => c.Map == map);
    }

    // What an output shader can express, in unified terms. Feature modes are offered only
    // when the capabilities they need are here. Grows with the features.
    public sealed class AtlasOutputCapabilities
    {
        public readonly List<AtlasOutputTexture> Textures = new List<AtlasOutputTexture>();

        public AtlasOutputTexture TextureFor(AtlasVirtualMap map) => Textures.Find(t => t.Provides(map));

        public bool Provides(AtlasVirtualMap map) => TextureFor(map) != null;
    }
}
