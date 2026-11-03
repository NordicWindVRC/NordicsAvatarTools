using NUnit.Framework;
using UnityEngine;
using NordicWind.TextureWrapper;
using NordicWind.AdvancedAtlaser.Editor;

namespace NordicWind.AdvancedAtlaser.Tests
{
    public class AtlasNormalPaddingTests
    {
        [Test]
        public void DilationCopiesValidNormalAcrossUnusedPixels()
        {
            var settings = new TextureSettings { DataType = TextureDataType.NormalTangent,
                SRGB = false, Mipmaps = false, Compression = TextureCompression.None };
            using (var map = new NordicsTextureWrapper(16, 16, settings))
            {
                map.SetPixel(8, 8, new Color(0.6f, 0f, 0.8f, 1f));
                AtlasNormalPadding.DilateAndFinalize(map);
                Assert.That(map.GetPixel(0, 0).r, Is.EqualTo(0.6f).Within(0.001f));
                Assert.That(map.GetPixel(0, 0).g, Is.EqualTo(0f).Within(0.001f));
                Assert.That(map.GetPixel(0, 0).b, Is.EqualTo(0.8f).Within(0.001f));
                Assert.That(map.GetPixel(15, 15).r, Is.EqualTo(0.6f).Within(0.001f));
            }
        }

        [Test]
        public void DilationRejectsNullTexture()
        {
            Assert.Throws<System.ArgumentNullException>(() => AtlasNormalPadding.DilateAndFinalize(null));
        }
    }
}
