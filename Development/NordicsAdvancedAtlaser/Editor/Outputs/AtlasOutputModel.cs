using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // A quantity a feature produces, one per map: base color, roughness, a matcap mask. An
    // output shader's textures provide them, one or several per texture (ATLAS_OUTPUT.md,
    // "Virtual maps and textures").
    public enum AtlasVirtualMap
    {
        BaseColor,
        // The surface's alpha, after each source's conversion to the output's surface mode.
        Alpha,
        // The main tangent-space normal, every normal of a source combined.
        Normal,
        // One value each (2.4): metallic, perceptual roughness, the strength of reflections and of
        // highlights (relative to the output's), and ambient occlusion.
        Metallic,
        Roughness,
        ReflectionMask,
        SpecularMask,
        Occlusion,
        // Emission (S5), relative to the output's emission strength.
        Emission
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
        // Stored as 1 − value, where the shader reads the inverse (roughness as smoothness).
        // Neutral is the stored value.
        public bool Invert;
    }

    // One texture of an output shader's texture layout.
    public sealed class AtlasOutputTexture
    {
        // Stable: stored in the settings and used in exported file names.
        public string Id;
        public string Label;
        public AtlasTextureSizeMode SizeMode = AtlasTextureSizeMode.MainAspect;
        // Color textures are sRGB; data and masks are linear.
        public bool Srgb = true;
        // A tangent-space normal map.
        public bool Normal;
        // The size a new texture of mode Square or Custom starts at.
        public int DefaultSize = 512;
        public readonly List<AtlasTextureChannel> Channels = new List<AtlasTextureChannel>();

        public AtlasOutputTexture(string id, string label) { Id = id; Label = label; }

        public AtlasOutputTexture With(AtlasVirtualMap map, AtlasTextureChannels channels, Color? neutral = null, bool invert = false)
        {
            Channels.Add(new AtlasTextureChannel { Map = map, Channels = channels, Neutral = neutral ?? Color.white, Invert = invert });
            return this;
        }

        // A one-value neutral.
        public static Color Gray(float value) => new Color(value, value, value, value);

        public bool Provides(AtlasVirtualMap map) => Channels.Exists(c => c.Map == map);
    }

    // How an output shader sets reflectance, the reflectivity of non-metal surfaces.
    public enum AtlasReflectanceSupport
    {
        // Fixed by the shader (Poiyomi's Reflections & Specular: 4%, reflectance 0.5).
        None,
        // One value for the whole material: the output's Reflectance.Constant.
        Constant,
        // The smoothness map times one value, the output's Reflectance.Constant (Toon Standard:
        // the gloss map × _Reflectance).
        FollowsSmoothness
    }

    // What the output shader does with the mesh's vertex colors on the base color.
    [System.Flags]
    public enum AtlasVertexColorSupport
    {
        None = 0,
        // Multiplies the base color by the vertex color.
        Color = 1,
        // Multiplies the alpha by the vertex alpha.
        Alpha = 2,
        // Takes an amount: lerp(1, vertex color, amount).
        Amount = 4,
        // Can decode the vertex colors from sRGB.
        Srgb = 8
    }

    // What an output shader can express, in unified terms. Feature modes are offered only
    // when the capabilities they need are here. Grows with the features.
    public sealed class AtlasOutputCapabilities
    {
        public readonly List<AtlasOutputTexture> Textures = new List<AtlasOutputTexture>();
        // The surface modes the writer can set.
        public readonly HashSet<AtlasSurfaceMode> SurfaceModes = new HashSet<AtlasSurfaceMode> { AtlasSurfaceMode.Opaque };
        // The writer sets VRChat's fallback shader (what viewers with the shader blocked see).
        public bool VRChatFallback;
        // The writer can fade by alpha while writing depth, so parts at alpha 1 sort like opaque
        // ones (Poiyomi's TransClipping).
        public bool BlendedDepthWrite;
        public AtlasReflectanceSupport Reflectance;
        public AtlasVertexColorSupport VertexColors;

        public AtlasOutputTexture TextureFor(AtlasVirtualMap map) => Textures.Find(t => t.Provides(map));

        public bool Provides(AtlasVirtualMap map) => TextureFor(map) != null;
    }
}
