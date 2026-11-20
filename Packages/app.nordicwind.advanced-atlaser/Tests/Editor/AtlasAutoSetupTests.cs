using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // ATLAS_OUTPUT.md, "Auto setup": the region packer and the Auto Setup button.
    public sealed class AtlasAutoSetupTests
    {
        private static Vector2Int S(int width, int height) => new Vector2Int(width, height);

        private static void AssertPacked(AtlasRegionLayout layout)
        {
            var cells = layout.Cells;
            int width = layout.Width * AtlasRegionPacker.Steps, height = layout.Height * AtlasRegionPacker.Steps;
            for (int i = 0; i < cells.Length; i++)
            {
                Assert.That(cells[i].x >= 0 && cells[i].y >= 0 && cells[i].xMax <= width && cells[i].yMax <= height,
                    $"cell {i} {cells[i]} inside {layout.Width} × {layout.Height}");
                for (int j = 0; j < i; j++)
                    Assert.IsFalse(cells[i].Overlaps(cells[j]), $"cells {j} and {i} overlap");
            }
        }

        // The regions cover the atlas whole.
        private static void AssertFilled(AtlasRegionLayout layout)
        {
            long area = layout.Cells.Sum(c => (long)c.width * c.height);
            Assert.AreEqual((long)layout.Width * layout.Height * AtlasRegionPacker.Steps * AtlasRegionPacker.Steps, area, "no free space left");
        }

        [Test]
        public void ThePackerChoosesTheSmallestAtlasThatHoldsEverySourceAtFullSize()
        {
            var sizes = new[] { S(1024, 1024), S(512, 512), S(512, 512) };
            var layout = AtlasRegionPacker.Pack(sizes);
            AssertPacked(layout);
            Assert.AreEqual((2048, 1024), (layout.Width, layout.Height));
            Assert.AreEqual(100, layout.Percent);
            for (int i = 0; i < sizes.Length; i++)
            {
                Assert.GreaterOrEqual(layout.Region(i).width, sizes[i].x / 2048f, "at least full size");
                Assert.GreaterOrEqual(layout.Region(i).height, sizes[i].y / 1024f, "at least full size");
            }
            AssertFilled(layout);
        }

        [Test]
        public void FourEqualSourcesTileTheAtlasInQuarters()
        {
            foreach (int side in new[] { 2048, 4096 })
            {
                var layout = AtlasRegionPacker.Pack(new[] { S(side, side), S(side, side), S(side, side), S(side, side) }, 4096);
                AssertPacked(layout);
                Assert.AreEqual((4096, 4096), (layout.Width, layout.Height), $"{side}: clamped to 4096");
                for (int i = 0; i < 4; i++)
                {
                    var region = layout.Region(i);
                    Assert.AreEqual(0.5f, region.width, 1e-6f, $"{side}: region {i}");
                    Assert.AreEqual(0.5f, region.height, 1e-6f, $"{side}: region {i}");
                }
            }
        }

        [Test]
        public void SourcesThatDontFitAreScaledDownAlikeAsLittleAsNeeded()
        {
            var sizes = new[] { S(4096, 4096), S(1024, 1024), S(1024, 1024), S(1024, 1024), S(512, 512) };
            var layout = AtlasRegionPacker.Pack(sizes, 4096);
            AssertPacked(layout);
            Assert.AreEqual((4096, 4096), (layout.Width, layout.Height));
            // The large one beside a column of the others: 4096 s + 1024 s = 4096, so s = 80%,
            // and the regions are exact decimals. The 512 grows to its full size in the column's
            // free space; the 1024s can't, since 0.25 beside 0.8 doesn't fit.
            Assert.AreEqual(80, layout.Percent);
            CollectionAssert.AreEqual(new[] { 80, 80, 80, 80, 100 }, layout.Percents);
            // Then the body stretches up into the free strip above it, and the 512 into the
            // column's free space: the atlas is used whole.
            var expected = new[] { new Rect(0f, 0f, 0.8f, 1f), new Rect(0.8f, 0f, 0.2f, 0.2f), new Rect(0.8f, 0.2f, 0.2f, 0.2f),
                new Rect(0.8f, 0.4f, 0.2f, 0.2f), new Rect(0.8f, 0.6f, 0.2f, 0.4f) };
            for (int i = 0; i < sizes.Length; i++)
                Assert.AreEqual(expected[i], layout.Region(i), $"region {i}");
            AssertFilled(layout);
            var large = AtlasRegionPacker.Pack(new[] { S(8192, 8192) }, 4096);
            Assert.AreEqual((4096, 4096), (large.Width, large.Height), "larger than the largest side");
            Assert.AreEqual(50, large.Percent);
            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), large.Region(0));
        }

        [Test]
        public void WithoutStretchingTheRegionsKeepTheirProportions()
        {
            var sizes = new[] { S(4096, 4096), S(1024, 1024), S(1024, 1024), S(1024, 1024), S(512, 512) };
            var layout = AtlasRegionPacker.Pack(sizes, 4096, stretch: false);
            AssertPacked(layout);
            var expected = new[] { 0.8f, 0.2f, 0.2f, 0.2f, 0.125f };
            for (int i = 0; i < sizes.Length; i++)
            {
                Assert.AreEqual(expected[i], layout.Region(i).width, $"region {i}");
                Assert.AreEqual(expected[i], layout.Region(i).height, $"region {i}");
            }
        }

        [Test]
        public void RegionsTileTheAtlasWithoutPadding()
        {
            var layout = AtlasRegionPacker.Pack(new[] { S(512, 256) });
            Assert.AreEqual((512, 256), (layout.Width, layout.Height));
            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), layout.Region(0));
        }

        [Test]
        public void ContentGoesInsideTheRegionByHalfThePadding()
        {
            var main = new AtlasTextureSize { width = 4096, height = 4096 };
            var content = AtlasOutputLayout.ContentRegion(new Rect(0.5f, 0f, 0.5f, 0.5f), main, 4);
            Assert.AreEqual(2050f, content.x * 4096f, 1e-3f);
            Assert.AreEqual(2f, content.y * 4096f, 1e-3f);
            Assert.AreEqual(2044f, content.width * 4096f, 1e-3f);
            Assert.AreEqual(2044f, content.height * 4096f, 1e-3f);
            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), AtlasOutputLayout.ContentRegion(new Rect(0f, 0f, 1f, 1f), main, 0), "no padding");
            Assert.AreEqual(3, AtlasOutputLayout.SidePadding(5));
            Assert.AreEqual(1, AtlasOutputLayout.SidePadding(4, main, 1024), "scaled to a smaller texture");
        }

        private static Material WithTexture(List<Object> created, int width, int height)
        {
            var material = AtlasTestMaterials.Standard();
            var texture = new Texture2D(width, height);
            created.Add(material);
            created.Add(texture);
            material.SetTexture("_MainTex", texture);
            return material;
        }

        [Test]
        public void AutoSetupLaysOutTheAssignedSourcesOnly()
        {
            var created = new List<Object>();
            try
            {
                var config = new AtlasOutputConfiguration();
                config.sources.Add(new AtlasSourceEntry { material = WithTexture(created, 256, 256), uvRegion = new Rect(0f, 0f, 1f, 1f) });
                config.sources.Add(new AtlasSourceEntry());
                config.sources.Add(new AtlasSourceEntry { material = WithTexture(created, 128, 128), uvRegion = new Rect(0f, 0f, 1f, 1f) });
                config.features.Add(new AtlasFeatureSetting { featureId = "color", mode = "off" });
                Assert.IsFalse(AtlasOutputAutoSetup.HasEditedRegions(config), "whole-texture regions aren't edits");

                string summary = AtlasOutputAutoSetup.Apply(config, AtlasAutoSetupRegions.Replace);
                Assert.IsNotNull(summary);
                Assert.AreEqual(3, config.sources.Count, "no source added or removed");
                Assert.IsNull(config.sources[1].material);
                Assert.AreEqual((512, 256), (config.mainTexture.width, config.mainTexture.height));
                var first = config.sources[0].uvRegion;
                var second = config.sources[2].uvRegion;
                Assert.IsFalse(first.Overlaps(second));
                // The regions tile: 256 px of 512, and the 128 stretched over the other half; the
                // padding is applied inside them later.
                Assert.AreEqual(new Rect(0f, 0f, 0.5f, 1f), first);
                Assert.AreEqual(new Rect(0.5f, 0f, 0.5f, 1f), second);
                Assert.AreEqual("", config.features[0].mode, "the default mode");
                Assert.IsFalse(AtlasOutputAutoSetup.HasEditedRegions(config), "its own layout isn't an edit");

                Assert.IsTrue(AtlasOutputAutoSetup.StretchChangesLayout(config), "the 128 leaves free space");
                AtlasOutputAutoSetup.Apply(config, AtlasAutoSetupRegions.Replace, stretch: false);
                Assert.AreEqual(new Rect(0.5f, 0f, 0.25f, 0.5f), config.sources[2].uvRegion, "in proportion");
                Assert.IsFalse(AtlasOutputAutoSetup.HasEditedRegions(config), "the layout in proportion isn't an edit either");

                config.sources[2].uvRegion = new Rect(0.5f, 0.5f, 0.25f, 0.25f);
                Assert.IsTrue(AtlasOutputAutoSetup.HasEditedRegions(config));
            }
            finally { foreach (var o in created) Object.DestroyImmediate(o); }
        }

        [Test]
        public void KeepingTheRegionsKeepsTheMainTextureSize()
        {
            var created = new List<Object>();
            try
            {
                var config = new AtlasOutputConfiguration { mainTexture = { width = 1024, height = 1024 } };
                var edited = new Rect(0.25f, 0.25f, 0.5f, 0.5f);
                config.sources.Add(new AtlasSourceEntry { material = WithTexture(created, 256, 256), uvRegion = edited });
                config.features.Add(new AtlasFeatureSetting { featureId = "color", mode = "off" });
                Assert.IsTrue(AtlasOutputAutoSetup.HasEditedRegions(config));
                Assert.IsNotNull(AtlasOutputAutoSetup.Apply(config, AtlasAutoSetupRegions.Keep));
                Assert.AreEqual(edited, config.sources[0].uvRegion);
                Assert.AreEqual((1024, 1024), (config.mainTexture.width, config.mainTexture.height));
                Assert.AreEqual("", config.features[0].mode);
            }
            finally { foreach (var o in created) Object.DestroyImmediate(o); }
        }

        [Test]
        public void WithoutAssignedSourcesNothingChanges()
        {
            var config = new AtlasOutputConfiguration { mainTexture = { width = 1024, height = 512 } };
            config.sources.Add(new AtlasSourceEntry());
            Assert.IsNull(AtlasOutputAutoSetup.Apply(config, AtlasAutoSetupRegions.Replace));
            Assert.AreEqual((1024, 512), (config.mainTexture.width, config.mainTexture.height));
        }
    }
}
