using System;
using NUnit.Framework;
using UnityEngine;
namespace NordicWind.TextureWrapper.Tests
{
    public class ColorOperationTests
    {
        private static TextureSettings Linear() => new TextureSettings { SRGB = false, Mipmaps = false, WrapMode = TextureWrapMode.Clamp };
        private static void Near(double expected, float actual, double epsilon = 1e-4) => Assert.AreEqual(expected, actual, epsilon);

        // Reference OKLab (Björn Ottosson), independent of the wrapper's implementation.
        private static Vector3 OKLab(Color c)
        {
            double l = Math.Pow(.4122214708 * c.r + .5363325363 * c.g + .0514459929 * c.b, 1.0 / 3);
            double m = Math.Pow(.2119034982 * c.r + .6806995451 * c.g + .1073969566 * c.b, 1.0 / 3);
            double s = Math.Pow(.0883024619 * c.r + .2817188376 * c.g + .6299787005 * c.b, 1.0 / 3);
            return new Vector3((float)(.2104542553 * l + .7936177850 * m - .0040720468 * s),
                (float)(1.9779984951 * l - 2.4285922050 * m + .4505937099 * s),
                (float)(.0259040371 * l + .7827717662 * m - .8086757660 * s));
        }
        private static float Chroma(Vector3 lab) => new Vector2(lab.y, lab.z).magnitude;
        private static float Hue(Vector3 lab) => Mathf.Atan2(lab.z, lab.y);

        private static Color Apply(Color input, ColorOperationCode code, ColorOperationParameters parameters)
        {
            using (var w = new NordicsTextureWrapper(1, 1, Linear()))
            {
                w.Fill(input);
                w.ApplyColor(new ColorOperation { Opcode = code, Parameters = parameters });
                return w.GetPixel(0, 0);
            }
        }

        [Test] public void MixedBlend_BrightensByBaseTimesBlend()
        {
            using (var dst = new NordicsTextureWrapper(1, 1, Linear()))
            using (var src = new NordicsTextureWrapper(1, 1, Linear()))
            {
                dst.Fill(new Color(.5f, .2f, 0f, 1f));
                src.Fill(new Color(.4f, 1f, 1f, 1f));
                dst.Blit(src, blend: TextureBlendMode.Mixed);
                Color c = dst.GetPixel(0, 0);
                Near(.5 + .5 * .4, c.r); Near(.2 + .2 * 1, c.g); Near(0, c.b); // black stays black
            }
        }

        [Test] public void Colorize_TakesHueFromColorAndKeepsLightness()
        {
            var input = new Color(.40f, .30f, .25f, 1f);
            var tint = new Color(.2f, .4f, .8f, 1f);
            Color result = Apply(input, ColorOperationCode.Colorize, new ColorOperationParameters { Color = tint });
            Vector3 inLab = OKLab(input), outLab = OKLab(result), tintLab = OKLab(tint);
            Near(inLab.x, outLab.x, 1e-3);                          // lightness kept
            Near(Hue(tintLab), Hue(outLab), 1e-3);                  // hue from the color
            Near(Chroma(inLab) * (1 - .2 / .8), Chroma(outLab), 1e-3); // chroma × color saturation
            Near(1, result.a);
        }

        [Test] public void Colorize_GreyColorMakesGreyAndAlphaMixes()
        {
            var input = new Color(.6f, .2f, .1f, 1f);
            Color grey = Apply(input, ColorOperationCode.Colorize, new ColorOperationParameters { Color = new Color(.5f, .5f, .5f, 1f) });
            Near(grey.r, grey.g, 1e-3); Near(grey.g, grey.b, 1e-3);
            Near(OKLab(input).x, OKLab(grey).x, 1e-3);
            Color none = Apply(input, ColorOperationCode.Colorize, new ColorOperationParameters { Color = new Color(0, 0, 1, 0) });
            Near(input.r, none.r); Near(input.g, none.g); Near(input.b, none.b);
        }

        [Test] public void Chromatize_RaisesChromaWithinTheHuesLimit()
        {
            var input = new Color(.5f, .4f, .4f, 1f);
            Color same = Apply(input, ColorOperationCode.Chromatize, new ColorOperationParameters { Amount = 0f });
            Near(input.r, same.r, 1e-3); Near(input.g, same.g, 1e-3); Near(input.b, same.b, 1e-3);

            Color more = Apply(input, ColorOperationCode.Chromatize, new ColorOperationParameters { Amount = 1f });
            Vector3 inLab = OKLab(input), outLab = OKLab(more);
            Assert.That(Chroma(outLab), Is.GreaterThan(Chroma(inLab)));
            Near(Hue(inLab), Hue(outLab), 1e-2);

            Color most = Apply(input, ColorOperationCode.Chromatize, new ColorOperationParameters { Amount = 1000f });
            Assert.That(Chroma(OKLab(most)), Is.LessThanOrEqualTo(Chroma(OKLab(Color.red)) + 1e-3f));
            Assert.That(Mathf.Min(most.r, Mathf.Min(most.g, most.b)), Is.GreaterThanOrEqualTo(0f));
        }

        [Test] public void SaturationAndBrightness_UseStandardScales()
        {
            var input = new Color(.6f, .3f, .1f, 1f);
            Color unchanged = Apply(input, ColorOperationCode.Saturation, new ColorOperationParameters { Amount = 1f });
            Near(input.r, unchanged.r); Near(input.b, unchanged.b);
            Color grey = Apply(input, ColorOperationCode.Saturation, new ColorOperationParameters { Amount = 0f });
            float luminance = .6f * .30f + .3f * .59f + .1f * .11f;
            Near(luminance, grey.r); Near(luminance, grey.g); Near(luminance, grey.b);
            Color brighter = Apply(input, ColorOperationCode.Brightness, new ColorOperationParameters { Amount = 1.5f });
            Near(.9, brighter.r); Near(.45, brighter.g);
        }

        [Test] public void ColorMap_VariesTheColorPerPixel()
        {
            using (var w = new NordicsTextureWrapper(2, 1, Linear()))
            using (var map = new NordicsTextureWrapper(2, 1, Linear()))
            {
                w.Fill(Color.white);
                map.SetPixel(0, 0, Color.red); map.SetPixel(1, 0, Color.green);
                w.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Tint,
                    Parameters = new ColorOperationParameters { Color = new Color(.5f, .5f, .5f, 1f), ColorMap = map } });
                Color left = w.GetPixel(0, 0), right = w.GetPixel(1, 0);
                Near(.5, left.r); Near(0, left.g);
                Near(0, right.r); Near(.5, right.g);
            }
        }

        [Test] public void ColorMap_CannotBeTheDestination()
        {
            using (var w = new NordicsTextureWrapper(1, 1, Linear()))
                Assert.Throws<ArgumentException>(() => w.ApplyColor(new ColorOperation { Opcode = ColorOperationCode.Tint,
                    Parameters = new ColorOperationParameters { ColorMap = w } }));
        }
    }
}
