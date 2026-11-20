using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    public sealed class AtlasOutputLayoutTests
    {
        private static AtlasTextureSize Main(int width, int height) => new AtlasTextureSize { width = width, height = height };

        [Test]
        public void BakeSize_Is50PercentLargerThanTheIsland()
        {
            // A 100×200 px island in a 1000×1000 atlas is baked at 150×300.
            var size = AtlasOutputLayout.BakeSize(new Rect(.3f, .4f, .1f, .2f), Main(1000, 1000));
            Assert.AreEqual(new Vector2Int(150, 300), size);
        }

        [Test]
        public void BakeSize_IsCappedAt4096ButNeverBelowTheIsland()
        {
            // 3000 px × 1.5 = 4500, capped at 4096.
            Assert.AreEqual(4096, AtlasOutputLayout.BakeSize(new Rect(0, 0, 3000f / 4096f, 1f), Main(4096, 4096)).x);
            // A whole 4096 atlas stays 4096.
            Assert.AreEqual(new Vector2Int(4096, 4096), AtlasOutputLayout.BakeSize(new Rect(0, 0, 1, 1), Main(4096, 4096)));
            // A 6000 px island in an 8192 atlas is larger than the cap, so it's baked 1:1.
            Assert.AreEqual(6000, AtlasOutputLayout.BakeSize(new Rect(0, 0, 6000f / 8192f, 1f), Main(8192, 8192)).x);
        }

        [Test]
        public void BakeSize_IsAtLeastOnePixel()
        {
            Assert.AreEqual(new Vector2Int(2, 2), AtlasOutputLayout.BakeSize(new Rect(0, 0, 0f, 0f), Main(4096, 4096)));
        }

        [Test]
        public void Padding_OnlyAppliesWithTwoOrMoreSources()
        {
            var config = new AtlasOutputConfiguration { regionPadding = 6 };
            var material = new Material(Shader.Find("Standard") ?? Shader.Find("Hidden/InternalErrorShader"));
            var other = new Material(material);
            try
            {
                Assert.AreEqual(0, AtlasOutputLayout.EffectivePadding(config));
                config.sources.Add(new AtlasSourceEntry { material = material });
                config.sources.Add(new AtlasSourceEntry()); // empty entries don't count
                Assert.AreEqual(0, AtlasOutputLayout.EffectivePadding(config));
                config.sources.Add(new AtlasSourceEntry { material = other });
                Assert.AreEqual(6, AtlasOutputLayout.EffectivePadding(config));
            }
            finally { Object.DestroyImmediate(material); Object.DestroyImmediate(other); }
        }
    }
}
