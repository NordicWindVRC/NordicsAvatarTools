using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // What a matcap is for (ATLAS_OUTPUT.md, "What a matcap is for"): its effect measured on a
    // sphere of normals seen from around the avatar, and the role that follows.
    public sealed class AtlasMatcapLookTests
    {
        private const int Size = 32;
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        // A linear texture from its value over the lookup disc: x and y from −1 to 1, the disc's
        // edge at radius 1.
        private Texture2D Texture(Func<float, float, float> value, string name = "matcap")
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true) { name = name };
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float v = value((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f);
                    pixels[y * Size + x] = new Color(v, v, v, 1f);
                }
            texture.SetPixels(pixels);
            texture.Apply();
            created.Add(texture);
            return texture;
        }

        // A gray material with one matcap.
        private static AtlasUnifiedMaterial With(Texture2D texture, AtlasBlend blend, float opacity = 1f,
            AtlasLookupProjection projection = AtlasLookupProjection.ViewCorrected)
        {
            var material = new AtlasUnifiedMaterial();
            material.Surface.BaseColor.Color = new Color(0.5f, 0.5f, 0.5f, 1f);
            var layer = new AtlasLookupLayer { Blend = blend, Opacity = opacity, Projection = projection };
            layer.Source.Map = new AtlasTextureReference { Texture = texture, Space = AtlasTextureSpace.View };
            material.Layers.Add(layer);
            return material;
        }

        private static AtlasMatcapMeasure Measure(AtlasUnifiedMaterial material) =>
            AtlasMatcapLook.Measure(material.Layers.OfType<AtlasLookupLayer>().ToList(), material);

        [Test]
        public void TheSameEffectAllOverIsATint()
        {
            var added = Measure(With(Texture((x, y) => 0.3f), AtlasBlend.Add));
            Assert.AreEqual(AtlasMatcapRole.Tint, added.Role);
            Assert.AreEqual(1f, added.Evenness, 1e-3f);
            Assert.AreEqual(1f, added.Brightens, 1e-3f, "it only brightens");
            Assert.AreEqual(0f, added.BakeLoss, 1e-3f, "the same from every view: freezing it loses nothing");
            Assert.Greater(added.Impact, 0.05f);

            var multiplied = Measure(With(Texture((x, y) => 0.5f), AtlasBlend.Multiply));
            Assert.AreEqual(AtlasMatcapRole.Tint, multiplied.Role);
            Assert.AreEqual(0f, multiplied.Brightens, 1e-3f, "it only darkens");

            var weaker = Measure(With(Texture((x, y) => 0.1f), AtlasBlend.Add));
            Assert.Less(weaker.Impact, added.Impact, "a weaker matcap changes the look less");
        }

        [Test]
        public void ASmallBrightSpotIsAHighlightThatABakeWouldFreeze()
        {
            var glint = Measure(With(Texture((x, y) => x * x + y * y < 0.04f ? 1f : 0f), AtlasBlend.Add));
            Assert.AreEqual(AtlasMatcapRole.Highlight, glint.Role);
            Assert.GreaterOrEqual(glint.Spread, AtlasMatcapLook.HighlightSpread);
            Assert.Greater(glint.BakeLoss, 1f, "frozen in place, it costs more than leaving it out");
        }

        [Test]
        public void SkyAboveAndGroundBelowIsLighting()
        {
            var sky = Measure(With(Texture((x, y) => 0.25f + 0.25f * y), AtlasBlend.Add));
            Assert.AreEqual(AtlasMatcapRole.Lighting, sky.Role);
            Assert.AreEqual(AtlasMatcapRole.Lighting, Measure(With(Texture((x, y) => 0.25f + 0.25f * x), AtlasBlend.Add)).Role, "light from one side");
            Assert.Less(sky.BakeLoss, 1f, "it looks alike from around the avatar");
            Assert.Less(sky.Evenness, AtlasMatcapLook.TintEvenness);
        }

        [Test]
        public void AnEffectTowardTheEdgesAllAroundIsARim()
        {
            var rim = Measure(With(Texture((x, y) => x * x + y * y > 0.85f * 0.85f ? 0.6f : 0f), AtlasBlend.Add));
            Assert.AreEqual(AtlasMatcapRole.Rim, rim.Role);
            Assert.GreaterOrEqual(rim.EdgeWeight, AtlasMatcapLook.RimEdgeWeight);
            Assert.GreaterOrEqual(rim.FollowsFacing, AtlasMatcapLook.RimFollowsFacing);

            // Soft shading that darkens toward the edges is one too.
            var shading = Measure(With(Texture((x, y) => Mathf.Max(0f, 1f - 0.6f * (x * x + y * y))), AtlasBlend.Multiply));
            Assert.AreEqual(AtlasMatcapRole.Rim, shading.Role);
            Assert.AreEqual(0f, shading.Brightens, 1e-3f);

            // The same ring on one side only isn't alike all around.
            var side = Measure(With(Texture((x, y) => x > 0f && x * x + y * y > 0.85f * 0.85f ? 0.6f : 0f), AtlasBlend.Add));
            Assert.AreNotEqual(AtlasMatcapRole.Rim, side.Role);

            // A soft glow where the surface faces the viewer follows the facing too, away from
            // the edge: light, not a rim.
            var glow = Measure(With(Texture((x, y) => Mathf.Max(0f, 0.5f * (1f - (x * x + y * y)))), AtlasBlend.Add));
            Assert.AreEqual(AtlasMatcapRole.Lighting, glow.Role);
        }

        [Test]
        public void AReflectionLookupOrDetailThatMovesWithTheViewIsAReflection()
        {
            var lookedUp = Measure(With(Texture((x, y) => 0.25f + 0.25f * y), AtlasBlend.Add, 1f, AtlasLookupProjection.ReflectionPlanar));
            Assert.AreEqual(AtlasMatcapRole.Reflection, lookedUp.Role, "by its lookup");

            // Upright stripes: every step around the avatar shows another part of them.
            var stripes = Measure(With(Texture((x, y) => Mathf.FloorToInt((x + 1f) * 2f) % 2 == 0 ? 0.6f : 0f), AtlasBlend.Add));
            Assert.AreEqual(AtlasMatcapRole.Reflection, stripes.Role, "by what freezing it would cost");
            Assert.GreaterOrEqual(stripes.BakeLoss, AtlasMatcapLook.ReflectionBakeLoss);
        }

        [Test]
        public void TheRoleIsNamedInTheInspectorAndTheLog()
        {
            var job = new AtlasGeneratorJob();
            job.Sources.Add(new AtlasGeneratorSource
            {
                Material = With(Texture((x, y) => 0.3f, "warm"), AtlasBlend.Add), UVRegion = new Rect(0f, 0f, 1f, 1f)
            });
            var instance = new AtlasMatcapFeature().Instances(job, new[] { 0 }).Single();
            Assert.AreEqual("warm (source 1)", instance.Label, "the label stays the matcap's name");
            StringAssert.Contains("tint matcap", instance.Detail);
            StringAssert.Contains("brightening", instance.Detail);

            var plan = AtlasOutputPipeline.Resolve(job, AtlasShaderRegistry.Output("poiyomi10").Capabilities);
            StringAssert.Contains("warm (source 1): tint", plan.Notes.Single(n => n.Contains("Matcap roles")));
        }
    }
}
