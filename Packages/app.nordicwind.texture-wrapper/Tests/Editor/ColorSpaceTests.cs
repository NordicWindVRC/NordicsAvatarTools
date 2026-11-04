using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
namespace NordicWind.TextureWrapper.Tests
{
    // The wrapper holds the values a shader reads: linear for sRGB textures, as stored
    // for linear textures. sRGB output is encoded on export. Alpha is never converted.
    public class ColorSpaceTests
    {
        // Reference sRGB transfer functions (IEC 61966-2-1), independent of the wrapper.
        private static double Decode(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        private static double Encode(double v) => v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055;
        private static void Near(double expected, float actual, double epsilon = 1e-5) =>
            Assert.AreEqual(expected, actual, epsilon);

        private static Texture2D Source(bool srgb, params byte[] values)
        {
            var texture = new Texture2D(values.Length, 1, TextureFormat.RGBA32, false, !srgb);
            var pixels = new Color32[values.Length];
            for (int i = 0; i < values.Length; i++) pixels[i] = new Color32(values[i], values[i], values[i], values[i]);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        [Test] public void Load_SRGBTexture_DecodesColorToLinear()
        {
            var texture = Source(true, 0, 64, 128, 255);
            try
            {
                using (var w = new NordicsTextureWrapper(texture))
                {
                    Assert.IsTrue(w.Settings.SRGB);
                    byte[] values = { 0, 64, 128, 255 };
                    for (int x = 0; x < values.Length; x++)
                    {
                        Color c = w.GetPixel(x, 0);
                        Near(Decode(values[x] / 255.0), c.r); Near(Decode(values[x] / 255.0), c.g); Near(Decode(values[x] / 255.0), c.b);
                        Near(values[x] / 255.0, c.a); // alpha stays as stored
                    }
                    Near(0.21586, w.GetPixel(2, 0).r); // sRGB 128 is about 0.216 linear
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test] public void Load_LinearTexture_KeepsStoredValues()
        {
            var texture = Source(false, 0, 64, 128, 255);
            try
            {
                using (var w = new NordicsTextureWrapper(texture))
                {
                    Assert.IsFalse(w.Settings.SRGB);
                    Near(128 / 255.0, w.GetPixel(2, 0).r);
                    Near(64 / 255.0, w.GetPixel(1, 0).a);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test] public void RoundTrip_SRGBTexture_KeepsEveryByteValue()
        {
            var values = new byte[256];
            for (int i = 0; i < values.Length; i++) values[i] = (byte)i;
            var texture = Source(true, values);
            try
            {
                using (var w = new NordicsTextureWrapper(texture))
                {
                    w.Settings.OutputChannels = TextureOutputChannels.RGBA;
                    Color[] encoded = w.GetEncodedPixels();
                    for (int i = 0; i < values.Length; i++)
                    {
                        Assert.AreEqual(i, Mathf.RoundToInt(encoded[i].r * 255f), "encoded value " + i);
                        Assert.AreEqual(i, Mathf.RoundToInt(encoded[i].a * 255f), "alpha " + i);
                    }
                    Texture2D output = w.GetTexture2D();
                    Assert.IsTrue(GraphicsFormatUtility.IsSRGBFormat(output.graphicsFormat));
                    Color32[] bytes = output.GetPixels32();
                    for (int i = 0; i < values.Length; i++)
                        Assert.AreEqual(values[i], bytes[i].r, "texture value " + i);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test] public void Export_SRGBOutputEncodesAndLinearOutputDoesNot()
        {
            float linear = (float)Decode(128 / 255.0);
            using (var w = new NordicsTextureWrapper(1, 1, new TextureSettings { SRGB = true, Mipmaps = false }))
            {
                w.Fill(new Color(linear, linear, linear, .5f));
                Color encoded = w.GetEncodedPixels()[0];
                Near(128 / 255.0, encoded.r, 1e-4); Near(.5, encoded.a);
                Near(linear, w.GetPixel(0, 0).r); // the buffer itself is unchanged
                w.Settings.SRGB = false;
                Near(linear, w.GetEncodedPixels()[0].r);
            }
        }

        [Test] public void Export_NormalMapsAreNeverEncoded()
        {
            using (var w = new NordicsTextureWrapper(1, 1, new TextureSettings { DataType = TextureDataType.NormalTangent, SRGB = true, Mipmaps = false }))
            {
                w.Fill(new Color(0, 0, 1, 1));
                Color encoded = w.GetEncodedPixels()[0];
                Near(.5, encoded.r); Near(.5, encoded.g); Near(1, encoded.b);
            }
        }

        [Test] public void Filtering_HappensInLinearSpace()
        {
            // Averaging sRGB black and white gives linear 0.5, like GPU filtering,
            // which is sRGB 0.735 (188), not 0.5 (128).
            var texture = Source(true, 0, 255);
            try
            {
                using (var w = new NordicsTextureWrapper(texture))
                using (var half = w.Resize(1, 1, sampling: TextureSamplingMode.Bilinear))
                {
                    Near(.5, half.GetPixel(0, 0).r);
                    Near(Encode(.5), half.GetEncodedPixels()[0].r);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
