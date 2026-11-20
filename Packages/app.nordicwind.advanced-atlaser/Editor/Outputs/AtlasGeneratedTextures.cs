using System;
using NordicWind.TextureWrapper;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // A texture the build makes besides the atlas maps (a combined matcap, a rim drawn into a
    // matcap): its linear pixels, made when the build gets to it and set on Reference before the
    // material is written. It's compressed and exported like the atlas maps, with the output's
    // formats for its kind.
    public sealed class AtlasGeneratedTexture
    {
        public AtlasTextureReference Reference;
        public string Name;
        public bool Srgb = true;
        // Its alpha holds something (a rim's strength).
        public bool Alpha;
        public int Width, Height;
        // Row by row from the bottom, Width × Height, linear.
        public Func<Color[]> Pixels;
    }

    public static class AtlasGeneratedTextures
    {
        /// <summary>The texture's pixels in a wrapper, with mipmaps, clamped.</summary>
        public static NordicsTextureWrapper Wrapper(AtlasGeneratedTexture generated)
        {
            var wrapper = new NordicsTextureWrapper(generated.Width, generated.Height, new TextureSettings
            {
                DataType = generated.Srgb ? TextureDataType.Color : TextureDataType.LinearData,
                SRGB = generated.Srgb, Mipmaps = true, WrapMode = TextureWrapMode.Clamp
            });
            try
            {
                var pixels = generated.Pixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    var c = pixels[i];
                    pixels[i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), Mathf.Clamp01(c.a));
                }
                wrapper.SetPixels(pixels);
                return wrapper;
            }
            catch
            {
                wrapper.Dispose();
                throw;
            }
        }

        /// <summary>The texture as the overview lists it and the build compresses it: its kind,
        /// size and memory with the output's formats.</summary>
        public static AtlasTextureUse Use(AtlasGeneratedTexture generated, AtlasOutputConfiguration config)
        {
            var kind = !generated.Srgb ? AtlasTextureKind.Data : generated.Alpha ? AtlasTextureKind.ColorAlpha : AtlasTextureKind.Color;
            return new AtlasTextureUse
            {
                Texture = new AtlasOutputTexture(generated.Name, ObjectNames.NicifyVariableName(generated.Name)) { SizeMode = AtlasTextureSizeMode.Square },
                Required = true, Enabled = true, Generated = true, Alpha = generated.Alpha, Kind = kind,
                Width = generated.Width, Height = generated.Height,
                PcBytes = AtlasTextureMemory.Bytes(generated.Width, generated.Height, AtlasTextureMemory.Format(config.pcFormats, kind)),
                AndroidBytes = AtlasTextureMemory.Bytes(generated.Width, generated.Height, AtlasTextureMemory.Format(config.androidFormats, kind))
            };
        }
    }
}
