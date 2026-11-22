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
        Emission,
        // One value each (3.2): the ramp toon shadow's strength mask, and the detail shadow that
        // multiplies the shading term.
        ShadowStrengthMask,
        DetailShadow,
        // Color (3.2): each band's shade texture of banded lighting.
        ShadowColor1,
        ShadowColor2,
        ShadowColor3,
        // One value (3.3): the pre-integrated skin's thickness.
        SkinThickness,
        // The mask allocator's places: one value each, in the output's mask textures.
        Mask1, Mask2, Mask3, Mask4, Mask5, Mask6, Mask7, Mask8,
        // RGB (4.4): the backlight's color over the atlas, black where a source has none.
        BacklightColor,
        // One value each (2.4): the clear coat's strength (0 without one), its roughness, and its
        // reflection and highlight strengths relative to the strongest.
        ClearCoatStrength, ClearCoatRoughness, ClearCoatReflectionMask, ClearCoatSpecularMask,
        // RGB (1.5): the emission whose global masks change live, shown through its channel.
        EmissionLive,
        // One value each: the live detail texture's and detail normal's strength × mask, 0 where
        // a source has none (tiled layers kept live).
        DetailTextureMask, DetailNormalMask,
        // RGB (4.5): each source's subsurface color × strength, black without; one value: its
        // thickness relative to the output's offset.
        SubsurfaceColor, SubsurfaceThickness,
        // One value: where the base color's hue cycles (its hue mask, 0 where it doesn't).
        HueCyclingMask,
        // RGB: emissions whose hue cycles, each in an emission slot of its own.
        EmissionCycle1, EmissionCycle2,
        // RGB: the AudioLink emission's color × gain per source, black without (Toon Standard).
        AudioLinkColor,
        // RGB (4.4): the second backlight's color, as BacklightColor (Poiyomi 11).
        BacklightColor2
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
        // A new texture of mode MainAspect starts at the main size divided by this (masks need
        // less detail than colors).
        public int MainDivisor = 1;
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
        Srgb = 8,
        // Has no switch for it: the base color is always multiplied by the vertex color, so
        // the sources that don't use theirs get white on the mesh.
        Always = 16
    }

    // The blends an output's matcap slot can take.
    [System.Flags]
    public enum AtlasMatcapBlends
    {
        None = 0,
        Add = 1,
        Multiply = 2,
        Replace = 4,
        Screen = 8,
        Mixed = 16,
        // Added after lighting.
        AddToLight = 32,
        All = Add | Multiply | Replace | Screen | Mixed | AddToLight
    }

    // What an output shader can express, in unified terms: counts and what is available.
    // Feature modes are offered only when the capabilities they need are here. Grows with the
    // features. How the shader computes what it has no settings for isn't here: its own
    // parser describes that (AtlasKeptMaterial.Described).
    public sealed class AtlasOutputCapabilities
    {
        // The output shader the capabilities belong to, set when it is registered; null for
        // capabilities made by hand.
        public string ShaderId;
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
        // The output multiplies its base color texture by a color. Without one, every source's
        // color goes into the texture, which cuts off what is brighter than 1.
        public bool BaseColorTint = true;
        // Hides UV tiles on both sides (Poiyomi's UV tile discard), and on one chosen side.
        public bool TileDiscard;
        public bool FaceTileDiscard;
        // The world AO blocker, on a UV channel it's told.
        public bool AOBlocker;
        // How many UV channels after UV0 the output's features can read (preserved UVs).
        public int UVChannels;
        // The writer shades only through a ramp texture: a ramp described otherwise (bands,
        // keys) is generated as one (AtlasRampTexture).
        public bool GeneratedRamp;
        // The writer reads an SDF face texture through a UV channel it's told, so the reference
        // source's texture stays live on its preserved UVs (an SDF is sampled mirrored, which an
        // atlas can't keep).
        public bool SdfFace;
        // Matcaps: how many slots, the blends a slot can take, and whether its mask can be read
        // inverted.
        public int MatcapSlots;
        public AtlasMatcapBlends MatcapBlends;
        public bool MatcapMaskInvert;
        // A slot tints its texture by a color; without, a tinted matcap gets a generated texture.
        public bool MatcapColor;
        // A slot can multiply its matcap by the base color; without, the base color's brightness
        // goes into the slot's mask.
        public bool MatcapBaseColorMix;
        // A slot glows by its matcap (an emission strength).
        public bool MatcapEmission;
        // A slot takes several blends at once, each by its weight (otherwise one at a time).
        public bool MatcapWeights;
        // A slot applies its texture's scale and offset to the lookup coordinate; without, a
        // matcap with them gets a generated texture.
        public bool MatcapTextureTransform;
        // A slot looks its texture up as the matcap does: by its projection, tiling (border) and
        // rotation. Without, the slot looks up by the view-space normal over the whole texture,
        // and a matcap looked up otherwise gets a generated texture redrawn for that lookup.
        public bool MatcapLookup;
        // Rims (4.4): how many view-angle rim slots, and whether a slot has a mask. What a slot
        // shows of a rim (its profile, placement, blend and glow) isn't here: it is read from
        // a rim written to a material of the shader (AtlasKeptMaterial.Shown).
        public int RimSlots;
        public bool RimMask;
        // A rim slot can show its color × the scene's light and ambient, so it can stand in
        // for an environmental rim or a backlight where the output has none.
        public bool RimFollowsSceneLight;
        // One environmental rim (the probe's color at the edge), with a mask.
        public bool EnvironmentalRim;
        // How many backlights, each with its color from a texture (BacklightColor,
        // BacklightColor2).
        public int Backlights;
        // Tiled layers kept live (a detail texture, a detail normal): the highest UV channel a
        // detail can read, the detail texture's blends, whether it has a tint and strength of its
        // own, whether there's a detail normal, and whether the detail mask picks a channel (one
        // allocated mask for both; otherwise DetailTextureMask and DetailNormalMask).
        public int DetailUVChannels;
        public readonly System.Collections.Generic.HashSet<AtlasBlend> DetailTextureBlends = new System.Collections.Generic.HashSet<AtlasBlend>();
        public bool DetailStrength;
        public bool DetailNormal;
        public bool DetailMaskByChannel;
        // Glitter (4.6): the glitter pattern, its mask by channel and its UV channel.
        public bool Glitter;
        // Proximity color (a camera distance tint of the final color or emission), without a mask.
        public bool ProximityColor;
        // Hue cycling (a hue shift with a speed) of the base color (its mask in HueCyclingMask) and
        // the final color (an allocated mask), and of the slots that have their own hue shift.
        public bool HueCycling;
        // A GT7 tone map of the final color, and a neutral surface emission; neither has a mask.
        public bool ToneMapping;
        public bool NeutralEmission;
        // Shadows from the depth texture, each with a mask by channel (inverted): screen-space
        // ambient occlusion, and contact shadows.
        public bool ScreenOcclusion;
        public bool ContactShadows;
        // Emission slots: the first holds the baked emission, the second the live global mask
        // emission, the rest emissions whose hue cycles.
        public int EmissionSlots = 1;
        // AudioLink: each section has its own (Poiyomi), or one AudioLink emission (Toon Standard).
        public bool AudioLinkPerSection;
        // Global masks (1.5): the mask textures double as the first global mask textures (Mask1
        // to Mask8 are channels 0–7), and these steps run live on a channel.
        public bool GlobalMasks;
        public readonly System.Collections.Generic.HashSet<AtlasMaskBusStepKind> LiveMaskSteps =
            new System.Collections.Generic.HashSet<AtlasMaskBusStepKind>();

        public AtlasOutputTexture TextureFor(AtlasVirtualMap map) => Textures.Find(t => t.Provides(map));

        public bool Provides(AtlasVirtualMap map) => TextureFor(map) != null;
    }
}
