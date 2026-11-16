using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Poiyomi Toon. Split over files by inspector section (AtlasPoiyomi*.cs). Section toggles are
    /// read from their properties: Thry's locking keeps them, and keywords can differ when locked.
    /// See UNIFIED_MATERIAL_POIYOMI.md. Rebuilt in refactor step 8.
    /// </summary>
    public abstract partial class AtlasPoiyomiParser : AtlasParserBase
    {
        public override int Priority => 100;
        protected abstract int Major { get; }

        // The Distortion UV section, which textures choosing Distorted UV share; null when off.
        [System.ThreadStatic] private static AtlasUVDistortion distortion;
        [System.ThreadStatic] private static int distortedChannel;

        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            UVOptions(m, d);
            MaskBus(m, d);
            ReportAudioLinkGlobals(m, d);
            Main(m, d);
            VideoScreen(m, d);
            Alpha(m, d);
            Normals(m, d);
            DepthEffect(m, d);
            SurfaceLayers(m, d);
            Dissolve(m, d);
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
            ColorAdjust(m, d);
            Outline(m, d);
            Lighting(m, d);
            Specular(m, d);
            Render(m, d);
            Parallax(m, d);
            Vertex(m, d);
            Fur(m, d);
        }

        private static readonly AtlasChannel[] Channels = { AtlasChannel.Red, AtlasChannel.Green, AtlasChannel.Blue, AtlasChannel.Alpha };

        // A Poiyomi texture: its UV choice (<name>UV), tiling and offset, panning (<name>Pan, in UV
        // per 20 seconds) and stochastic sampling (<name>Stochastic), with the UV channel's own
        // settings (UV Settings section) folded in. Null without a texture.
        protected static AtlasTextureReference Texture(Material m, AtlasUnifiedMaterial d, string property,
            AtlasTextureInterpretation interpretation = AtlasTextureInterpretation.Color)
        {
            var map = AtlasSourceMapping.Texture(m, property, interpretation);
            if (map == null) return null;
            var pan = AtlasMaterialReader.Vector(m, property + "Pan", Vector4.zero);
            map.Scroll = new Vector2(pan.x, pan.y) * 0.05f;
            // Stochastic Sampling's mode: Deliot-Heitz is the stochastic sampling, None turns it off.
            if (Float(m, property + "Stochastic", 0f) > 0.5f && Int(m, "_StochasticMode", 0) != 2)
            {
                map.Sampling = AtlasTextureSampling.Stochastic;
                if (Int(m, "_StochasticMode", 0) == 1)
                    d.Report(AtlasIssueKind.Unmapped, "uv.hextile", "Hextile stochastic sampling isn't mapped yet; read as stochastic.");
            }
            int uv = Int(m, property + "UV", 0);
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
        private static void ChannelSettings(Material m, AtlasUnifiedMaterial d, AtlasTextureReference map, int channel)
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

        // A mask texture's single channel; null without a texture.
        protected static AtlasMask Mask(Material m, AtlasUnifiedMaterial d, string property, AtlasChannel channel = AtlasChannel.Red)
        {
            var map = Texture(m, d, property, AtlasTextureInterpretation.Data);
            return map == null ? null : new AtlasMask { Map = map, Channel = channel };
        }


        protected static Color LinearColor(Material m, string property, Color fallback)
        {
            var color = AtlasMaterialReader.ColorLinear(m, property, fallback);
            return color;
        }

        protected static float Float(Material m, string property, float fallback) => AtlasMaterialReader.Float(m, property, fallback);
        protected static int Int(Material m, string property, int fallback) => Mathf.RoundToInt(AtlasMaterialReader.Float(m, property, fallback));
        protected static bool On(Material m, string property, float fallback = 0f) => AtlasMaterialReader.Float(m, property, fallback) >= 0.5f;
        protected static Vector3 Vector3Of(Material m, string property, Vector3 fallback) =>
            AtlasMaterialReader.Vector(m, property, new Vector4(fallback.x, fallback.y, fallback.z, 0f));
    }

    public sealed class AtlasPoiyomiToon10Parser : AtlasPoiyomiParser
    {
        public override string ParserId => "poiyomi-toon-10";
        public override string DisplayName => "Poiyomi Toon 10";
        protected override int Major => 10;
        public override bool CanParse(Material m) =>
            AtlasPoiyomiVersionDetector.DetectVariant(m) == AtlasPoiyomiVariant.Toon && AtlasPoiyomiVersionDetector.Detect(m) == 10;
    }
}
