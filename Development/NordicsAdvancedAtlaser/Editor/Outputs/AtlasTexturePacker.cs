using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Step 4 of the pipeline (ATLAS_OUTPUT.md, "Virtual maps and textures"): one output texture
    // filled from the virtual maps it holds. Each map is baked at the texture's size, copied into
    // its channels and released before the next, so at most two full-size maps exist at once.
    public static class AtlasTexturePacker
    {
        // `timings` (optional) gets a "[Time] …" line for each step of the maps' bakes.
        public static NordicsTextureWrapper Pack(AtlasTextureUse use, AtlasOutputPlan plan, AtlasGeneratorJob job, List<string> timings = null)
        {
            var texture = use.Texture;
            var packed = new NordicsTextureWrapper(use.Width, use.Height, Settings(use));
            try
            {
                packed.Fill(Neutral(texture));
                foreach (var channel in texture.Channels)
                {
                    if (!use.Maps.Contains(channel.Map)) continue;
                    // One request per virtual map; a map no feature requested keeps its neutral value.
                    var request = plan.Maps.FirstOrDefault(r => r.Map == channel.Map);
                    if (request == null) continue;
                    using (var map = AtlasMapBaker.Bake(request, job, use.Width, use.Height, timings))
                    {
                        // A normal map is its whole texture: its vectors are copied as they are.
                        if (texture.Normal) packed.Blit(map, padding: 0);
                        else
                        {
                            if (channel.Invert) map.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Invert });
                            Copy(packed, map, channel.Channels);
                        }
                    }
                }
                return packed;
            }
            catch
            {
                packed.Dispose();
                throw;
            }
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

        private static readonly TextureChannel[] Rgba = { TextureChannel.R, TextureChannel.G, TextureChannel.B, TextureChannel.A };

        private static void Copy(NordicsTextureWrapper packed, NordicsTextureWrapper map, AtlasTextureChannels channels)
        {
            switch (channels)
            {
                case AtlasTextureChannels.RGBA:
                    packed.Blit(map, padding: 0);
                    return;
                case AtlasTextureChannels.RGB:
                    // Channel by channel, so the alpha another map holds is kept.
                    for (int k = 0; k < 3; k++)
                        packed.Blit(map, sourceChannel: Rgba[k], destinationChannel: Rgba[k]);
                    return;
                default:
                    // A one-value map: its RGB average into the channel.
                    packed.Blit(map, destinationChannel: Rgba[(int)channels - (int)AtlasTextureChannels.R]);
                    return;
            }
        }
    }
}
