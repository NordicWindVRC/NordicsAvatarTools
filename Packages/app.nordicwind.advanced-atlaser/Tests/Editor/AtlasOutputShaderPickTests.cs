using System.Collections.Generic;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // ATLAS_OUTPUT.md, "Auto setup": a new output's shader by its sources' families.
    public sealed class AtlasOutputShaderPickTests
    {
        private sealed class Fake : IAtlasOutputShader
        {
            public Fake(string id, string family, int rank) { ShaderId = id; Family = family; Rank = rank; }
            public string ShaderId { get; }
            public string DisplayName => ShaderId;
            public string Family { get; }
            public int Rank { get; }
            public Shader FindShader() => null;
            public AtlasOutputCapabilities Capabilities => null;
            public void Write(AtlasUnifiedMaterial output, Material material) { }
        }

        private static readonly Fake Toon10 = new Fake("toon10", AtlasShaderFamilies.Poiyomi, 100);
        private static readonly Fake Toon11 = new Fake("toon11", AtlasShaderFamilies.Poiyomi, 110);
        private static readonly Fake Pro11 = new Fake("pro11", AtlasShaderFamilies.Poiyomi, 111);
        private static readonly Fake Mobile = new Fake("mobile", AtlasShaderFamilies.VRChat, 10);

        private static string Pick(IEnumerable<string> families, params IAtlasOutputShader[] installed) =>
            AtlasOutputShaderPick.Pick(families, installed)?.ShaderId;

        [Test]
        public void TheMostCommonFamilyWins()
        {
            var families = new[] { "vrchat", "poiyomi", "vrchat", null };
            Assert.AreEqual("mobile", Pick(families, Toon10, Mobile));
        }

        [Test]
        public void TheHighestInstalledVersionWinsAndProComesBeforeToon()
        {
            var families = new[] { "poiyomi", "poiyomi", "vrchat" };
            Assert.AreEqual("toon11", Pick(families, Toon10, Toon11, Mobile));
            Assert.AreEqual("pro11", Pick(families, Toon10, Toon11, Pro11, Mobile));
        }

        [Test]
        public void ATieGoesToTheFamilyFirstByName()
        {
            Assert.AreEqual("toon10", Pick(new[] { "vrchat", "poiyomi" }, Mobile, Toon10));
        }

        [Test]
        public void FamiliesWithoutAnInstalledOutputDontVote()
        {
            // Two VRChat materials, but only Poiyomi is installed.
            Assert.AreEqual("toon10", Pick(new[] { "vrchat", "vrchat", "poiyomi" }, Toon10));
        }

        [Test]
        public void WithoutAFamilyTheFirstFamilyByNameAtItsHighestVersion()
        {
            Assert.AreEqual("toon11", Pick(new string[] { null, null }, Mobile, Toon10, Toon11));
            Assert.AreEqual("toon11", Pick(new string[0], Mobile, Toon11));
        }

        [Test]
        public void NothingInstalledPicksNothing()
        {
            Assert.IsNull(Pick(new[] { "poiyomi" }));
        }

        [Test]
        public void ParsersAndOutputShadersNameTheirFamily()
        {
            Assert.AreEqual(AtlasShaderFamilies.Poiyomi, new AtlasPoiyomiToon10Parser().Family);
            Assert.AreEqual(AtlasShaderFamilies.Poiyomi, new AtlasPoiyomiPro11Parser().Family);
            Assert.AreEqual(AtlasShaderFamilies.VRChat, new AtlasVRChatToonStandardParser().Family);
            Assert.AreEqual(AtlasShaderFamilies.VRChat, new AtlasVRChatToonLitParser().Family);
            Assert.AreEqual(AtlasShaderFamilies.VRChat, new AtlasVRChatStandardLiteParser().Family);
            Assert.AreEqual(AtlasShaderFamilies.Unity, new AtlasUnityStandardParser().Family);
            Assert.AreEqual(AtlasShaderFamilies.Unity, new AtlasUnityStandardSpecularParser().Family);
            Assert.IsNull(new AtlasMochieUberParser().Family);
            Assert.AreEqual(AtlasShaderFamilies.Poiyomi, AtlasShaderRegistry.Output("poiyomi10").Family);
            Assert.AreEqual(AtlasShaderFamilies.VRChat, AtlasShaderRegistry.Output("mobiletoon").Family);
            Assert.AreEqual(AtlasShaderFamilies.Unity, AtlasShaderRegistry.Output("standard").Family);
        }

        // Unity Standard is built in, so its family always has an output: its materials pick
        // it, and it comes before VRChat Mobile where no family wins.
        [Test]
        public void UnityStandardMaterialsPickUnityStandard()
        {
            var standard = new Fake("standard", AtlasShaderFamilies.Unity, 10);
            Assert.AreEqual("standard", Pick(new[] { "unity", "unity", "poiyomi" }, Toon10, standard, Mobile));
            Assert.AreEqual("toon10", Pick(new[] { "unity", "poiyomi", "poiyomi" }, Toon10, standard, Mobile));
            Assert.AreEqual("standard", Pick(new string[] { null }, standard, Mobile));
            Assert.AreEqual("toon10", Pick(new string[] { null }, Toon10, standard, Mobile));
        }

        [Test]
        public void ANewOutputStartsWithoutAShaderAndAChosenOneIsKept()
        {
            var go = new GameObject("Shader pick test");
            try
            {
                var output = go.AddComponent<NordicsAtlasOutput>();
                // Added by script, not through the editor's factory: nothing is picked yet.
                if (!string.IsNullOrEmpty(output.configuration.shaderId))
                    Assert.Ignore("An editor hook picked the shader on AddComponent.");
                var expected = AtlasOutputShaderPick.Pick(new string[0], AtlasShaderRegistry.InstalledOutputShaders);
                Assert.AreEqual(expected != null, AtlasOutputShaderPick.ApplyIfUnset(output));
                Assert.AreEqual(expected?.ShaderId ?? "", output.configuration.shaderId);

                output.configuration.shaderId = "mobiletoon";
                Assert.IsFalse(AtlasOutputShaderPick.ApplyIfUnset(output));
                Assert.AreEqual("mobiletoon", output.configuration.shaderId);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
