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
        private Texture2D Texture(Func<float, float, float> value, string name = "matcap") => Texture(value, Color.white, name);

        // The same in a color.
        private Texture2D Texture(Func<float, float, float> value, Color color, string name = "matcap")
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true) { name = name };
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float v = value((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f);
                    pixels[y * Size + x] = new Color(v * color.r, v * color.g, v * color.b, 1f);
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
        public void TheSameEffectAllOverIsLightingThatABakeKeeps()
        {
            var added = Measure(With(Texture((x, y) => 0.3f), AtlasBlend.Add));
            Assert.AreEqual(AtlasMatcapRole.Lighting, added.Role);
            Assert.AreEqual(1f, added.Evenness, 1e-3f);
            Assert.AreEqual(1f, added.Brightens, 1e-3f, "it only brightens");
            Assert.AreEqual(0f, added.BakeLoss, 1e-3f, "the same from every view: freezing it loses nothing");
            Assert.Greater(added.Impact, 0.05f);

            var multiplied = Measure(With(Texture((x, y) => 0.5f), AtlasBlend.Multiply));
            Assert.AreEqual(AtlasMatcapRole.Lighting, multiplied.Role);
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

        // A matcap's shading is its level and its contrast. A lighting matcap's bake is judged
        // by what it keeps of them, a highlight's point by point.
        [Test]
        public void ABakeOfALightingMatcapIsJudgedByItsLevelAndContrast()
        {
            var even = Measure(With(Texture((x, y) => 0.3f), AtlasBlend.Add));
            Assert.Greater(even.Level, 0.01f);
            Assert.AreEqual(0f, even.Contrast, 1e-3f, "the same all over");
            Assert.AreEqual(0f, even.AverageLoss, 1e-2f, "its average is all of it");
            Assert.AreEqual(0f, even.BakeShadingLoss, 1e-2f);
            Assert.AreEqual(even.BakeShadingLoss, even.BakeCost, 1e-6f, "lighting: by its shading");

            var sky = Measure(With(Texture((x, y) => 0.25f + 0.25f * y), AtlasBlend.Add));
            Assert.Greater(sky.Contrast, 0.005f);
            Assert.Greater(sky.AverageLoss, 0.1f, "its average loses its contrast");
            Assert.Less(sky.AverageLoss, 1f, "and keeps its level");
            Assert.Greater(sky.Range, sky.Contrast, "from black to white is more than it varies");

            var glint = Measure(With(Texture((x, y) => x * x + y * y < 0.04f ? 1f : 0f), AtlasBlend.Add));
            Assert.AreEqual(glint.BakeLoss, glint.BakeCost, 1e-6f, "a highlight: point by point");
        }

        [Test]
        public void SkyAboveAndGroundBelowIsLighting()
        {
            var sky = Measure(With(Texture((x, y) => 0.25f + 0.25f * y), AtlasBlend.Add));
            Assert.AreEqual(AtlasMatcapRole.Lighting, sky.Role);
            Assert.AreEqual(AtlasMatcapRole.Lighting, Measure(With(Texture((x, y) => 0.25f + 0.25f * x), AtlasBlend.Add)).Role, "light from one side");
            Assert.Less(sky.BakeLoss, 1f, "it looks alike from around the avatar");
            Assert.Less(sky.Evenness, 0.9f, "it varies over the disc");
        }

        [Test]
        public void AnEffectTowardTheEdgesAllAroundIsARim()
        {
            var rim = Measure(With(Texture((x, y) => x * x + y * y > 0.85f * 0.85f ? 0.6f : 0f), AtlasBlend.Add));
            Assert.AreEqual(AtlasMatcapRole.Rim, rim.Role);
            Assert.GreaterOrEqual(rim.EdgeWeight, AtlasMatcapLook.RimEdgeWeight);
            Assert.GreaterOrEqual(rim.Around, AtlasMatcapLook.RimAround);

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

        // Poiyomi's double-sided lookup goes by the reflection, and is used for every kind of
        // matcap: the lookup doesn't say what a matcap is for.
        [Test]
        public void TheLookupDoesNotDecideTheRole()
        {
            var sky = Measure(With(Texture((x, y) => 0.25f + 0.25f * y), AtlasBlend.Add, 1f, AtlasLookupProjection.ReflectionPlanar));
            Assert.AreEqual(AtlasMatcapRole.Lighting, sky.Role);
            // Upright stripes are neither concentrated, even, nor toward the edge: lighting too.
            var stripes = Measure(With(Texture((x, y) => Mathf.FloorToInt((x + 1f) * 2f) % 2 == 0 ? 0.6f : 0f), AtlasBlend.Add));
            Assert.AreEqual(AtlasMatcapRole.Lighting, stripes.Role);
        }

        [Test]
        public void FineDetailAllOverIsAHighlightToo()
        {
            // Facets: a checkerboard of eight squares across. Nothing of it is in one place.
            var facets = Measure(With(Texture((x, y) => (Mathf.FloorToInt((x + 1f) * 4f) + Mathf.FloorToInt((y + 1f) * 4f)) % 2 == 0 ? 0.6f : 0f), AtlasBlend.Add));
            Assert.Less(facets.Spread, AtlasMatcapLook.HighlightSpread);
            Assert.GreaterOrEqual(facets.Detail, AtlasMatcapLook.HighlightDetail);
            Assert.AreEqual(AtlasMatcapRole.Highlight, facets.Role);
            // A smooth gradient has none.
            Assert.Less(Measure(With(Texture((x, y) => 0.25f + 0.25f * y), AtlasBlend.Add)).Detail, 0.2f);
        }

        [Test]
        public void ASoftSphereWithACrispSpotIsLightingWithAHighlight()
        {
            Func<float, float, float> sphere = (x, y) => Mathf.Max(0f, 0.5f - 0.3f * (x * x + y * y));
            // A pearl: most of what it does is the soft body, with a hot spot up to the left.
            var pearl = Measure(With(Texture((x, y) => sphere(x, y) + ((x + 0.4f) * (x + 0.4f) + (y - 0.4f) * (y - 0.4f) < 0.12f * 0.12f ? 0.5f : 0f)), AtlasBlend.Add));
            Assert.AreEqual(AtlasMatcapRole.Lighting, pearl.Role);
            Assert.IsTrue(pearl.HasHighlight);
            Assert.AreEqual("Lighting with a highlight, brightens", pearl.Summary());

            var soft = Measure(With(Texture(sphere), AtlasBlend.Add));
            Assert.IsFalse(soft.HasHighlight, "the body alone has none");
            Assert.Less(soft.Standout, 0.6f);
        }

        [Test]
        public void AColoredMatcapTintsWhateverItsRole()
        {
            var red = new Color(1f, 0.2f, 0.2f, 1f);
            var sky = Measure(With(Texture((x, y) => 0.25f + 0.25f * y, red), AtlasBlend.Add));
            Assert.IsTrue(sky.Tints);
            Assert.AreEqual("Lighting, brightens, tints", sky.Summary());
            var glint = Measure(With(Texture((x, y) => x * x + y * y < 0.04f ? 1f : 0f, red), AtlasBlend.Add));
            Assert.AreEqual("Highlight, brightens, tints", glint.Summary());

            // A warm white doesn't, and neither does a gray matcap tinted by nothing.
            Assert.IsFalse(Measure(With(Texture((x, y) => 0.25f + 0.25f * y, new Color(1f, 0.9f, 0.8f, 1f)), AtlasBlend.Add)).Tints);
            Assert.AreEqual(0f, Measure(With(Texture((x, y) => 0.25f + 0.25f * y), AtlasBlend.Add)).Tint, 1e-4f);
        }

        // Sources with areas from the largest to the smallest.
        private static AtlasGeneratorJob Job(params AtlasUnifiedMaterial[] materials)
        {
            var job = new AtlasGeneratorJob();
            float x = 0f;
            for (int i = 0; i < materials.Length; i++)
            {
                float width = (materials.Length - i) / (materials.Length * (materials.Length + 1) / 2f);
                job.Sources.Add(new AtlasGeneratorSource { Material = materials[i], UVRegion = new Rect(x, 0f, width, 1f) });
                x += width;
            }
            return job;
        }

        private static readonly Func<float, float, float> Sky = (x, y) => 0.25f + 0.25f * y;
        private static readonly Func<float, float, float> Facets =
            (x, y) => (Mathf.FloorToInt((x + 1f) * 4f) + Mathf.FloorToInt((y + 1f) * 4f)) % 2 == 0 ? 0.6f : 0f;

        [Test]
        public void PlacementFollowsTheRole()
        {
            // Toon Standard's one slot goes to the lighting. The facets are a highlight by their
            // detail: an effect, left out and not averaged into the slot.
            var job = Job(With(Texture(Sky, "sky"), AtlasBlend.Add), With(Texture(Facets, "facets"), AtlasBlend.Add));
            var toon = AtlasOutputPipeline.Resolve(job, AtlasShaderRegistry.Output("mobiletoon").Capabilities);
            StringAssert.Contains("facets", toon.Notes.Single(n => n.Contains("effects")));
            Assert.IsFalse(toon.Notes.Any(n => n.Contains("combines")), string.Join(" | ", toon.Notes));
            StringAssert.Contains("facets (source 2): highlight", toon.Notes.Single(n => n.Contains("Matcap roles")));
        }

        [Test]
        public void LightingWithAHighlightTakesItsOwnSlotAndPlainLightingSharesAnother()
        {
            // Two slots for three lighting matcaps, the pearl on the smallest material.
            var capabilities = new AtlasOutputCapabilities();
            AtlasFieldwise.Copy(AtlasShaderRegistry.Output("poiyomi10").Capabilities, capabilities);
            capabilities.MatcapSlots = 2;
            Func<float, float, float> pearl = (x, y) =>
                Mathf.Max(0f, 0.5f - 0.3f * (x * x + y * y)) + ((x + 0.4f) * (x + 0.4f) + (y - 0.4f) * (y - 0.4f) < 0.12f * 0.12f ? 0.5f : 0f);
            var job = Job(With(Texture(Sky, "sky"), AtlasBlend.Add), With(Texture((x, y) => 0.25f + 0.25f * x, "side"), AtlasBlend.Add),
                With(Texture(pearl, "pearl"), AtlasBlend.Add));
            var plan = AtlasOutputPipeline.Resolve(job, capabilities);
            StringAssert.Contains("pearl (source 3): lighting with a highlight", plan.Notes.Single(n => n.Contains("Matcap roles")));
            string combines = plan.Notes.Single(n => n.Contains("combines"));
            StringAssert.Contains("sky", combines);
            StringAssert.Contains("side", combines);
            StringAssert.DoesNotContain("pearl", combines, "the pearl's spot isn't averaged with the others");
        }

        [Test]
        public void AMatcapThatChangesNothingSaysSo()
        {
            // Black, added: as a texture scale of 0 makes any matcap whose corner texel is black.
            var nothing = Measure(With(Texture((x, y) => 0f), AtlasBlend.Add));
            Assert.IsFalse(nothing.HasEffect);
            // A glint is tiny on average and still shows.
            Assert.IsTrue(Measure(With(Texture((x, y) => x * x + y * y < 0.01f ? 1f : 0f), AtlasBlend.Add)).HasEffect);
            Assert.AreEqual("No visible effect", nothing.Summary());
            Assert.AreEqual("Lighting, brightens", Measure(With(Texture((x, y) => 0.25f + 0.25f * y), AtlasBlend.Add)).Summary());
        }

        [Test]
        public void TheInspectorShowsTheMatcapWithItsRoleAndTheLogListsIt()
        {
            var job = new AtlasGeneratorJob();
            job.Sources.Add(new AtlasGeneratorSource
            {
                Material = With(Texture((x, y) => 0.3f, "warm"), AtlasBlend.Add), UVRegion = new Rect(0f, 0f, 1f, 1f)
            });
            var instance = new AtlasMatcapFeature().Instances(job, new[] { 0 }).Single();
            Assert.AreEqual("warm (source 1)", instance.Label, "the label stays the matcap's name and sources");
            Assert.AreEqual("warm", instance.Name);
            Assert.AreEqual("Lighting, brightens", instance.Kind);
            Assert.IsTrue(instance.HasPreview);
            Assert.AreEqual("warm", instance.Preview.name, "the matcap's texture, to recognize and find it by");

            var plan = AtlasOutputPipeline.Resolve(job, AtlasShaderRegistry.Output("poiyomi10").Capabilities);
            StringAssert.Contains("warm (source 1): lighting", plan.Notes.Single(n => n.Contains("Matcap roles")));
        }
    }
}
