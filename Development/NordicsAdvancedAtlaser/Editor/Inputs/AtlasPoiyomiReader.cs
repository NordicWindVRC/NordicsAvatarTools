using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Reads one Poiyomi material into the unified material. Split over files by inspector section
    /// (AtlasPoiyomi*.cs). Section toggles are read from their properties: Thry's locking keeps
    /// them, and keywords can differ when locked. A reader is made for each parse and holds the
    /// state the sections share. See UNIFIED_MATERIAL_POIYOMI.md.
    /// </summary>
    internal sealed partial class AtlasPoiyomiReader
    {
        private const int Newest = AtlasPoiyomiParser.Newest;

        // The major version being read, for the few settings whose meaning changed (9 and older).
        private readonly int major;
        // Set while reading when an 8.x blend value had no 10 equivalent (reported by Legacy).
        private bool legacyColorBlend, legacyMaskBlend;

        // The Distortion UV section, which textures choosing Distorted UV share; null when off.
        private AtlasUVDistortion distortion;
        private int distortedChannel;

        public AtlasPoiyomiReader(int major) { this.major = major; }

        public void Read(Material m, AtlasUnifiedMaterial d)
        {
            ReportShader(m, d);
            UVOptions(m, d);
            Triplanar(m, d);
            MaskBus(m, d);
            ReportAudioLinkGlobals(m, d);
            Main(m, d);
            VideoScreen(m, d);
            Alpha(m, d);
            Normals(m, d);
            DepthEffect(m, d);
            SurfaceLayers(m, d);
            LookupLayers(m, d);
            AudioSpectrum(m, d);
            AudioVolumeColor(m, d);
            Flipbooks(m, d);
            Rims(m, d);
            EdgeLight(m, d);
            HighlightLayers(m, d);
            Patterns(m, d);
            Visibility(m, d);
            CellPatterns(m, d);
            ScreenShadows(m, d);
            ColorAdjust(m, d);
            Outline(m, d);
            Lighting(m, d);
            Specular(m, d);
            Render(m, d);
            Parallax(m, d);
            Vertex(m, d);
            GeometryVariants(m, d);
            Fur(m, d);
            Globals11(m, d);
            if (major < 10) Legacy(m, d);
        }

        // A version this parser doesn't know, and the world variants.
        private void ReportShader(Material m, AtlasUnifiedMaterial d)
        {
            if (AtlasPoiyomiVersionDetector.IsPoiyomi(m))
            {
                int detected = AtlasPoiyomiVersionDetector.Detect(m);
                if (detected == 0 || detected > Newest)
                    d.Report(AtlasIssueKind.Unmapped, "shader.version",
                        "This Poiyomi version isn't known; it's read as Poiyomi " + Newest + ", so its look may differ.");
            }
            if (AtlasPoiyomiVersionDetector.IsWorld(m))
                d.Report(AtlasIssueKind.Ignored, "shader.world", "A world variant: read as the avatar shader, its world-only features ignored.");
        }

        // Whether the material's shader is the variant that declares the marker property (Two Pass,
        // Grab Pass, the furs and so on differ from the base shader only by such properties). With
        // the shader installed, having the property settles it. Without it, a saved property may be
        // left over from a variant the material used before: the name the material was locked with
        // decides, and without one the variant is assumed and reported.
        private bool IsVariant(Material m, AtlasUnifiedMaterial d, string marker, params string[] names)
        {
            if (!AtlasMaterialReader.Has(m, marker)) return false;
            if (AtlasMaterialReader.Saved(m) == null) return true;
            string shader = AtlasMaterialReader.OriginalShaderName(m);
            if (shader.Length > 0)
            {
                // A name starting with '-' rules the variant out.
                bool named = false;
                foreach (string name in names)
                {
                    bool exclude = name[0] == '-';
                    if (shader.IndexOf(exclude ? name.Substring(1) : name, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (exclude) return false;
                    named = true;
                }
                return named;
            }
            d.Report(AtlasIssueKind.Unmapped, "shader.variant",
                "The shader isn't installed, so its variant (" + names[0] + ") is assumed from saved values, which may be left over from another shader.");
            return true;
        }

        private static readonly AtlasChannel[] Channels = { AtlasChannel.Red, AtlasChannel.Green, AtlasChannel.Blue, AtlasChannel.Alpha };

        // A Poiyomi texture: its UV choice (<name>UV), tiling and offset, panning (<name>Pan, in UV
        // per 20 seconds) and stochastic sampling (<name>Stochastic), with the UV channel's own
        // settings (UV Settings section) folded in. Null without a texture.
        private AtlasTextureReference Texture(Material m, AtlasUnifiedMaterial d, string property,
            AtlasTextureInterpretation interpretation = AtlasTextureInterpretation.Color)
        {
            property = Name(property);
            var map = AtlasSourceMapping.Texture(m, property, interpretation);
            if (map == null) return null;
            var pan = AtlasMaterialReader.Vector(m, property + "Pan", Vector4.zero);
            map.Scroll = new Vector2(pan.x, pan.y) * 0.05f;
            // Stochastic Sampling's mode: Deliot-Heitz is the stochastic sampling, None turns it off.
            if (Float(m, property + "Stochastic", 0f) > 0.5f && Int(m, "_StochasticMode", 0) != 2)
            {
                map.Sampling = AtlasTextureSampling.Stochastic;
                map.StochasticDensity = Float(m, "_StochasticDeliotHeitzDensity", 1f);
                if (Int(m, "_StochasticMode", 0) == 1)
                    d.Report(AtlasIssueKind.Unmapped, "uv.hextile", "Hextile stochastic sampling isn't mapped yet; read as stochastic.");
            }
            int uv = Int(m, property + "UV", 0);
            // 7.3's index 4 is the distorted UV (7 since 8).
            if (major <= 7 && uv == 4) uv = 7;
            switch (uv)
            {
                case 0: case 1: case 2: case 3:
                    map.Space = AtlasTextureSpace.UV0 + uv;
                    ChannelSettings(m, d, map, uv);
                    break;
                case 4: map.Space = AtlasTextureSpace.Panosphere; break;
                case 5: map.Space = AtlasTextureSpace.WorldPosition; break;
                case 6: map.Space = AtlasTextureSpace.Polar; break;
                case 8: map.Space = AtlasTextureSpace.ObjectPosition; break;
                case 9: map.Space = AtlasTextureSpace.View; break;
                case 10: map.Space = AtlasTextureSpace.Screen; break;
                default:
                    // Distorted UV: the distorted channel with the shared distortion; UV0 when off.
                    map.Space = AtlasTextureSpace.UV0 + (distortion == null ? 0 : distortedChannel);
                    ChannelSettings(m, d, map, distortion == null ? 0 : distortedChannel);
                    map.Distortion = distortion;
                    break;
            }
            if (map.Space == AtlasTextureSpace.Polar || map.Space == AtlasTextureSpace.Panosphere
                || map.Space == AtlasTextureSpace.ObjectPosition || map.Space == AtlasTextureSpace.WorldPosition)
                ReportProjectionSettings(m, d, map.Space);
            return map;
        }

        // UV Settings: uv × tiling + offset, rotated by angle (+ rotate × time) around 0.5, + pan × time,
        // before the texture's own tiling. Tiling, offset and pan fold into the texture; the
        // rotation becomes the texture's (which turns before its tiling), exact while the
        // channel's tiling and offset are (1, 1) and (0, 0).
        private void ChannelSettings(Material m, AtlasUnifiedMaterial d, AtlasTextureReference map, int channel)
        {
            string i = channel.ToString();
            var tiling = AtlasMaterialReader.Vector(m, "_UVSettingsTiling" + i, Vector4.one);
            var offset = AtlasMaterialReader.Vector(m, "_UVSettingsOffset" + i, Vector4.zero);
            var pan = AtlasMaterialReader.Vector(m, "_UVSettingsPan" + i, Vector4.zero);
            var scale = map.Tiling;
            map.Tiling = new Vector2(tiling.x * scale.x, tiling.y * scale.y);
            map.Offset = new Vector2(offset.x * scale.x, offset.y * scale.y) + map.Offset;
            map.Scroll += new Vector2(pan.x * scale.x, pan.y * scale.y);
            float angle = Float(m, "_UVSettingsAngle" + i, 0f), rotate = Float(m, "_UVSettingsRotate" + i, 0f);
            if (angle == 0f && rotate == 0f) return;
            map.Rotation += angle;
            map.Spin += rotate;
            if (tiling.x != 1f || tiling.y != 1f || offset.x != 0f || offset.y != 0f)
                d.Report(AtlasIssueKind.Note, "uv.channel-rotation", "A UV channel's rotation with its own tiling or offset is read as turning before them.");
        }

        // A mask texture's single channel with Poiyomi 11's options; null without a texture or a
        // vertex colour source.
        private AtlasMask Mask(Material m, AtlasUnifiedMaterial d, string property, AtlasChannel channel = AtlasChannel.Red)
        {
            var map = Texture(m, d, property, AtlasTextureInterpretation.Data);
            return MaskOptions(m, map == null ? null : new AtlasMask { Map = map, Channel = channel }, property, System.Array.IndexOf(Channels, channel));
        }

        // Masks whose vertex colour source is per channel; the others take it from x.
        private static readonly System.Collections.Generic.HashSet<string> FourChannelMasks = new System.Collections.Generic.HashSet<string>
        {
            "_DecalMask", "_RGBMask", "_MatcapMask", "_Matcap2Mask", "_Matcap3Mask", "_Matcap4Mask", "_AlphaMask", "_DetailMask",
            "_OutlineMask", "_LightingAOMaps", "_LightingShadowMasks", "_LightingDetailShadowMaps", "_ShadowBorderMask",
            "_MochieMetallicMaps", "_ClearCoatMaps", "_PoiPBRMaskMap", "_RGBAMetallicMaps", "_RGBASmoothnessMaps", "_RGBAAOMaps",
            "_ClothMetallicSmoothnessMap", "_MainColorAdjustTexture", "_PathingMaskMap", "_LookAtMask", "_VertexBasicsMask",
            "_PoiSkinDetailMap", "_FurMask", "_GrabPassBlendMap", "_GlobalMaskTexture0", "_GlobalMaskTexture1", "_GlobalMaskTexture2", "_GlobalMaskTexture3"
        };

        // Poiyomi 11's mask options, named after the mask texture: levels (<mask>ML*, with bounds to
        // normalise the input range), a vertex colour source (<mask>VertexMask: 1–4 multiply by R–A)
        // and the levels' invert (<mask>MLInvert), which comes after the vertex colour when there is
        // one. Masks with an Invert toggle of their own invert after everything (set by the caller).
        // Missing properties (Poiyomi 10) leave the mask as it is.
        private AtlasMask MaskOptions(Material m, AtlasMask mask, string property, int channel)
        {
            channel = Mathf.Clamp(channel, 0, 3);
            var sources = AtlasMaterialReader.Vector(m, property + "VertexMask", Vector4.zero);
            int source = Mathf.RoundToInt(FourChannelMasks.Contains(property) ? sources[channel] : sources.x);
            bool invert = AtlasMaterialReader.Vector(m, property + "MLInvert", Vector4.zero)[channel] >= 0.5f;
            if (mask == null)
            {
                // No texture: 1 × the vertex colour.
                if (source <= 0) return null;
                return new AtlasMask { VertexColor = true, Channel = Channels[Mathf.Clamp(source - 1, 0, 3)], Invert = invert };
            }
            mask.Levels = MaskLevels(m, property, channel);
            if (source > 0)
            {
                mask.TimesVertexColor = true;
                mask.VertexChannel = Channels[Mathf.Clamp(source - 1, 0, 3)];
                mask.Invert = invert;
            }
            else if (invert)
            {
                if (mask.Levels != null) mask.Levels.Invert = true;
                else mask.Invert = true;
            }
            return mask;
        }

        // Null for the identity settings.
        private AtlasMaskLevels MaskLevels(Material m, string property, int channel)
        {
            float Get(string name, float fallback) => AtlasMaterialReader.Vector(m, property + "ML" + name, Vector4.one * fallback)[channel];
            var levels = new AtlasMaskLevels
            {
                InputMin = Get("InputMin", 0f), InputMax = Get("InputMax", 1f), Midpoint = Get("Midpoint", 0.5f),
                OutputMin = Get("OutputMin", 0f), OutputMax = Get("OutputMax", 1f)
            };
            float boundsMin = Get("BoundsMin", 0f), boundsMax = Get("BoundsMax", 1f);
            if (Get("Normalize", 0f) > 0.5f && boundsMax > boundsMin)
            {
                levels.InputMin = Mathf.Lerp(boundsMin, boundsMax, levels.InputMin);
                levels.InputMax = Mathf.Lerp(boundsMin, boundsMax, levels.InputMax);
            }
            bool identity = levels.InputMin == 0f && levels.InputMax == 1f && levels.Midpoint == 0.5f && levels.OutputMin == 0f && levels.OutputMax == 1f;
            return identity ? null : levels;
        }


        private Color LinearColor(Material m, string property, Color fallback)
        {
            var color = AtlasMaterialReader.ColorLinear(m, property, fallback);
            return color;
        }

        private float Float(Material m, string property, float fallback) => AtlasMaterialReader.Float(m, Name(property), fallback);
        private int Int(Material m, string property, int fallback) => Mathf.RoundToInt(AtlasMaterialReader.Float(m, Name(property), fallback));
        private bool On(Material m, string property, float fallback = 0f) => AtlasMaterialReader.Float(m, Name(property), fallback) >= 0.5f;

        // 7.3's names for settings 8 renamed with the same meaning; other versions read 10's names.
        private static readonly System.Collections.Generic.Dictionary<string, string> Names7 = new System.Collections.Generic.Dictionary<string, string>
        {
            { "_MainTexUV", "_MainTextureUV" }, { "_AlphaForceOpaque", "_ForceOpaque" }, { "_AlphaDithering", "_DitheringEnabled" },
            { "_AlphaDitherGradient", "_DitherGradient" }, { "_AlphaToCoverage", "_AlphaToMask" }, { "_AlphaSharpenedA2C", "_MainAlphaToCoverage" },
            { "_AlphaMipScale", "_MainMipScale" }, { "_AlphaAngular", "_EnableRandom" }, { "_ShadingEnabled", "_EnableLighting" },
            { "_PoiParallax", "_ParallaxMap" }, { "_HeightMap", "_ParallaxHeightMap" }, { "_HeightStrength", "_ParallaxStrength" },
            { "_PoiInternalParallax", "_ParallaxInternalMapEnabled" },
            { "_AlphaDistanceFadeMin", "_MainDistanceFadeMin" }, { "_AlphaDistanceFadeMax", "_MainDistanceFadeMax" },
            { "_AlphaDistanceFadeMinAlpha", "_MainMinAlpha" }, { "_AlphaDistanceFadeMaxAlpha", "_MainMaxAlpha" },
            { "_AlphaDistanceFadeType", "_MainFadeType" }
        };

        private string Name(string property) =>
            major <= 7 && property != null && Names7.TryGetValue(property, out var old) ? old : property;
        private Vector2 Vector2Of(Material m, string property, Vector2 fallback) =>
            AtlasMaterialReader.Vector(m, property, new Vector4(fallback.x, fallback.y, 0f, 0f));
        private Vector3 Vector3Of(Material m, string property, Vector3 fallback) =>
            AtlasMaterialReader.Vector(m, property, new Vector4(fallback.x, fallback.y, fallback.z, 0f));
    }
}
