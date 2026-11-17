using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    public sealed class AtlasOutputTexturesTests
    {
        [Test]
        public void MemoryCountsBlocksOverTheMipChain()
        {
            // BC1: 8 bytes per 4×4 block; 4×4, 2×2 and 1×1 are one block each.
            Assert.AreEqual(24, AtlasTextureMemory.Bytes(4, 4, AtlasTextureFormat.BC1));
            Assert.AreEqual(8L * 1024 * 1024, AtlasTextureMemory.Bytes(4096, 4096, AtlasTextureFormat.BC1, mipmaps: false));
            Assert.AreEqual(1024L * 1024, AtlasTextureMemory.Bytes(1024, 1024, AtlasTextureFormat.BC7, mipmaps: false));
            // ASTC 6×6: 16 bytes per block, partial blocks counted whole.
            Assert.AreEqual(171L * 171 * 16, AtlasTextureMemory.Bytes(1024, 1024, AtlasTextureFormat.ASTC6x6, mipmaps: false));
            Assert.AreEqual(4L * 2048 * 1024, AtlasTextureMemory.Bytes(2048, 1024, AtlasTextureFormat.RGBA32, mipmaps: false));
        }

        [Test]
        public void MipChainAddsAboutAThird()
        {
            long single = AtlasTextureMemory.Bytes(2048, 2048, AtlasTextureFormat.BC7, mipmaps: false);
            long chain = AtlasTextureMemory.Bytes(2048, 2048, AtlasTextureFormat.BC7);
            Assert.That(chain / (double)single, Is.EqualTo(4.0 / 3.0).Within(0.01));
        }

        [Test]
        public void SizesFollowTheirSizeMode()
        {
            int width = 3000, height = 0;
            AtlasTextureSizes.Normalize(AtlasTextureSizeMode.MainAspect, 2048, 1024, ref width, ref height);
            Assert.AreEqual((2048, 1024), (width, height));
            width = 600;
            AtlasTextureSizes.Normalize(AtlasTextureSizeMode.MainAspect, 2048, 1024, ref width, ref height);
            Assert.AreEqual((512, 256), (width, height));
            width = 300;
            AtlasTextureSizes.Normalize(AtlasTextureSizeMode.Square, 2048, 1024, ref width, ref height);
            Assert.AreEqual((256, 256), (width, height));
            AtlasTextureSizes.Normalize(AtlasTextureSizeMode.Main, 2048, 1024, ref width, ref height);
            Assert.AreEqual((2048, 1024), (width, height));
            CollectionAssert.DoesNotContain(AtlasTextureSizes.MainAspectWidths(2048, 1024), 128, "Its height, 64, isn't a size.");
        }

        private static AtlasOutputCapabilities Capabilities() => new AtlasOutputCapabilities
        {
            Textures =
            {
                new AtlasOutputTexture("BaseColor", "Base color") { SizeMode = AtlasTextureSizeMode.Main }
                    .With(AtlasVirtualMap.BaseColor, AtlasTextureChannels.RGB)
            }
        };

        private static AtlasOutputOverview Overview(AtlasOutputConfiguration config, int sources)
        {
            var materials = new List<Material>();
            var unified = new List<AtlasUnifiedMaterial>();
            for (int i = 0; i < sources; i++)
            {
                materials.Add(null);
                unified.Add(new AtlasUnifiedMaterial());
            }
            return AtlasOutputOverview.Build(config, Capabilities(), materials, unified);
        }

        [Test]
        public void ColorNeedsTheMainBaseColorTexture()
        {
            var config = new AtlasOutputConfiguration { mainTexture = { width = 2048, height = 1024 } };
            var overview = Overview(config, 2);
            var use = overview.Textures.Single();
            Assert.AreEqual("BaseColor", use.Texture.Id);
            Assert.IsTrue(use.Required);
            Assert.IsTrue(use.Enabled);
            Assert.AreEqual(AtlasTextureKind.Color, use.Kind, "No alpha: RGB only.");
            Assert.AreEqual((2048, 1024), (use.Width, use.Height));
            Assert.AreEqual(AtlasTextureMemory.Bytes(2048, 1024, AtlasTextureFormat.BC1), use.PcBytes);
            Assert.AreEqual(AtlasTextureMemory.Bytes(2048, 1024, AtlasTextureFormat.ASTC6x6), use.AndroidBytes);
            Assert.AreEqual(use.PcBytes, overview.PcBytes);
            Assert.AreEqual(2, overview.Features.Single(f => f.Feature.Id == "color").Active.Count);
        }

        [Test]
        public void TexturesFollowTheFormatsChosen()
        {
            var config = new AtlasOutputConfiguration { mainTexture = { width = 1024, height = 1024 } };
            config.pcFormats.color = AtlasTextureFormat.BC7;
            config.androidFormats.color = AtlasTextureFormat.ASTC8x8;
            var use = Overview(config, 1).Textures.Single();
            Assert.AreEqual(AtlasTextureMemory.Bytes(1024, 1024, AtlasTextureFormat.BC7), use.PcBytes);
            Assert.AreEqual(AtlasTextureMemory.Bytes(1024, 1024, AtlasTextureFormat.ASTC8x8), use.AndroidBytes);
        }

        [Test]
        public void ColorOffNeedsNoTexture()
        {
            var config = new AtlasOutputConfiguration();
            config.features.Add(new AtlasFeatureSetting { featureId = "color", mode = "off" });
            var overview = Overview(config, 1);
            Assert.IsEmpty(overview.Textures);
            Assert.AreEqual(0, overview.PcBytes);
        }

        [Test]
        public void SourcesWithoutAReadableMaterialHaveNoFeatures()
        {
            var overview = AtlasOutputOverview.Build(new AtlasOutputConfiguration(), Capabilities(),
                new Material[] { null, null }, new AtlasUnifiedMaterial[] { new AtlasUnifiedMaterial(), null });
            CollectionAssert.AreEqual(new[] { 0 }, overview.Features.Single(f => f.Feature.Id == "color").Present);
        }
    }
}
