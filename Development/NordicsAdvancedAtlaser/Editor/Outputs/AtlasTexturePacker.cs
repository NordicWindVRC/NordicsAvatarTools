using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Step 4 of the pipeline (ATLAS_OUTPUT.md, "Virtual maps and textures"): one output texture
    // filled from the virtual maps it holds. The baker works source by source: each map of the
    // texture is evaluated, copied into its channels of the source's content, and released
    // before the next.
    public static class AtlasTexturePacker
    {
        // `timings` (optional) gets a "[Time] …" line for each step of the bake.
        public static NordicsTextureWrapper Pack(AtlasTextureUse use, AtlasOutputPlan plan, AtlasGeneratorJob job, List<string> timings = null)
        {
            var texture = use.Texture;
            var parts = new List<AtlasBakePart>();
            foreach (var channel in texture.Channels)
            {
                if (!use.Maps.Contains(channel.Map)) continue;
                // One request per virtual map; a map no feature requested keeps its neutral value.
                var request = plan.Maps.FirstOrDefault(r => r.Map == channel.Map);
                if (request == null) continue;
                // A normal map is its whole texture: its vectors are used as they are.
                if (texture.Normal) parts.Add(new AtlasBakePart { Request = request });
                else
                {
                    var target = channel;
                    parts.Add(new AtlasBakePart
                    {
                        Request = request,
                        Copy = (content, map) =>
                        {
                            if (target.Invert) map.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Invert });
                            Copy(content, map, target.Channels);
                        }
                    });
                }
            }
            var neutral = texture.Normal ? new Color(0f, 0f, 1f, 1f) : Neutral(texture);
            if (parts.Count == 0)
            {
                var empty = new NordicsTextureWrapper(use.Width, use.Height, Settings(use));
                empty.Fill(neutral);
                return empty;
            }
            return AtlasMapBaker.Bake(parts, Settings(use), neutral, texture.Label, job, use.Width, use.Height, timings);
        }

        // Color textures sRGB, data linear; alpha only when a virtual map uses it.
        public static TextureSettings Settings(AtlasTextureUse use) => new TextureSettings
        {
            DataType = use.Texture.Normal ? TextureDataType.NormalTangent
                : use.Texture.Srgb ? TextureDataType.Color : TextureDataType.LinearData,
            SRGB = use.Texture.Srgb,
            Mipmaps = true,
            StreamingMipmaps = true,
            WrapMode = TextureWrapMode.Clamp,
            Compression = TextureCompression.Normal,
            OutputChannels = use.Alpha ? TextureOutputChannels.RGBA : TextureOutputChannels.RGB,
            AlphaIsTransparency = use.Alpha && use.Texture.Srgb
        };

        // Each channel's value where its virtual map isn't in use; alpha 1 unless a map sets it.
        private static Color Neutral(AtlasOutputTexture texture)
        {
            var color = new Color(0f, 0f, 0f, 1f);
            foreach (var channel in texture.Channels)
                for (int k = 0; k < 4; k++)
                    if (Covers(channel.Channels, k)) color[k] = channel.Neutral[k];
            return color;
        }

        private static bool Covers(AtlasTextureChannels channels, int k)
        {
            switch (channels)
            {
                case AtlasTextureChannels.RGBA: return true;
                case AtlasTextureChannels.RGB: return k < 3;
                case AtlasTextureChannels.R: return k == 0;
                case AtlasTextureChannels.G: return k == 1;
                case AtlasTextureChannels.B: return k == 2;
                default: return k == 3;
            }
        }

        // A map into its channels of a source's content, of the same size.
        private static void Copy(NordicsTextureWrapper content, NordicsTextureWrapper map, AtlasTextureChannels channels)
        {
            switch (channels)
            {
                case AtlasTextureChannels.RGBA:
                    AtlasPixels.Map(content, map, (c, m) => m);
                    return;
                case AtlasTextureChannels.RGB:
                    // The alpha another map holds is kept.
                    AtlasPixels.Map(content, map, (c, m) => new Color(m.r, m.g, m.b, c.a));
                    return;
                default:
                    // A one-value map: its RGB average into the channel.
                    int k = (int)channels - (int)AtlasTextureChannels.R;
                    AtlasPixels.Map(content, map, (c, m) =>
                    {
                        c[k] = (m.r + m.g + m.b) / 3f;
                        return c;
                    });
                    return;
            }
        }
    }
}
