using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Materials swapped on the renderers between the early capture and the build (AtlasMaterialSlots).
    public sealed class AtlasMaterialSlotsTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private Material Material(string name)
        {
            var material = new Material(Shader.Find("Standard")) { name = name };
            created.Add(material);
            return material;
        }

        // An avatar with a renderer holding `materials` and an output whose sources are `sources`.
        private (GameObject Avatar, MeshRenderer Renderer, NordicsAtlasOutput Output) Avatar(Material[] materials, params Material[] sources)
        {
            var avatar = new GameObject("Avatar");
            created.Add(avatar);
            var body = new GameObject("Body");
            body.transform.SetParent(avatar.transform);
            var renderer = body.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            var output = body.AddComponent<NordicsAtlasOutput>();
            foreach (var source in sources) output.configuration.sources.Add(new AtlasSourceEntry { material = source });
            return (avatar, renderer, output);
        }

        [Test]
        public void TheSettingsFollowAMaterialSwappedInItsSlots()
        {
            var skin = Material("Skin");
            var cloth = Material("Cloth");
            var (avatar, renderer, output) = Avatar(new[] { skin, cloth }, skin, cloth);
            output.configuration.features.Add(new AtlasFeatureSetting { featureId = "emission", disabledSources = { skin } });
            var slots = AtlasMaterialSlots.Capture(avatar);

            var copy = new Material(skin) { name = "Skin (copy)" };
            created.Add(copy);
            renderer.sharedMaterials = new[] { copy, cloth };
            var notes = new List<string>();
            Assert.AreEqual(2, slots.Follow(avatar, notes), "the source and the feature's switch");
            Assert.AreSame(copy, output.configuration.sources[0].material);
            Assert.AreSame(cloth, output.configuration.sources[1].material, "not swapped");
            Assert.AreSame(copy, output.configuration.features[0].disabledSources[0]);
            StringAssert.Contains("Skin (copy)", string.Join(" | ", notes));
        }

        [Test]
        public void MobilePlaceholdersHoldTheSourcesPlacesUntilReleased()
        {
            if (Shader.Find("VRChat/Mobile/Toon Lit") == null || AtlasShaderRegistry.Output("mobiletoon").FindShader() == null)
                Assert.Ignore("The VRChat SDK's mobile shaders aren't installed.");
            var skin = Material("Skin");
            var other = Material("Other");
            var (avatar, renderer, output) = Avatar(new[] { skin, other, skin }, skin);
            output.configuration.shaderId = "poiyomi10";
            Assert.AreEqual(0, AtlasMobilePlaceholders.Hold(avatar).Count, "not a mobile output: left to VRCFury's warning");
            output.configuration.shaderId = "mobiletoon";
            var placeholders = AtlasMobilePlaceholders.Hold(avatar);
            Assert.AreEqual(1, placeholders.Count);
            var held = renderer.sharedMaterials;
            StringAssert.StartsWith("VRChat/Mobile/", held[0].shader.name, "a mobile shader, which VRCFury keeps");
            Assert.AreSame(held[0], held[2], "one placeholder per material");
            Assert.AreSame(other, held[1], "not a source");

            placeholders.Release(avatar);
            CollectionAssert.AreEqual(new[] { skin, other, skin }, renderer.sharedMaterials);
            Assert.IsTrue(held[0] == null, "the placeholder is destroyed");
        }

        [Test]
        public void AMaterialStillInASlotOrSwappedForSeveralStays()
        {
            var skin = Material("Skin");
            var (avatar, renderer, output) = Avatar(new[] { skin, skin }, skin);
            var slots = AtlasMaterialSlots.Capture(avatar);
            renderer.sharedMaterials = new[] { Material("A"), skin };
            Assert.AreEqual(0, slots.Follow(avatar), "still in a slot");

            renderer.sharedMaterials = new[] { new Material(skin) { name = "A" }, new Material(skin) { name = "B" } };
            created.AddRange(renderer.sharedMaterials);
            var notes = new List<string>();
            Assert.AreEqual(0, slots.Follow(avatar, notes));
            Assert.AreSame(skin, output.configuration.sources[0].material);
            StringAssert.Contains("different materials", string.Join(" | ", notes));
        }

        [Test]
        public void MovedSlotsDontSwapOneSourceForAnother()
        {
            var skin = Material("Skin");
            var cloth = Material("Cloth");
            var fluff = Material("Fluff");
            var (avatar, renderer, output) = Avatar(new[] { skin, cloth, fluff }, skin, cloth, fluff);
            var slots = AtlasMaterialSlots.Capture(avatar);
            // A tool reorders the slots (or drops one) before the build.
            renderer.sharedMaterials = new[] { cloth, fluff, skin };
            Assert.AreEqual(0, slots.Follow(avatar));
            CollectionAssert.AreEqual(new[] { skin, cloth, fluff }, output.configuration.sources.Select(s => s.material).ToArray());

            // Another shader isn't a copy.
            var other = new Material(Shader.Find("Unlit/Color")) { name = "Other" };
            created.Add(other);
            renderer.sharedMaterials = new[] { other, cloth, fluff };
            Assert.AreEqual(0, slots.Follow(avatar));
            Assert.AreSame(skin, output.configuration.sources[0].material);
        }
    }
}
