using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NordicWind.AdvancedAtlaser.Editor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Tests
{
    // Hidden UV tiles: kept live, removed from the mesh, or off (ATLAS_OUTPUT_PLAN.md, 2.5b).
    public sealed class AtlasHiddenTilesTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Destroy()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        // Bit row × 4 + column.
        private static int Tile(int column, int row) => 1 << (row * 4 + column);

        private static AtlasUnifiedMaterial Hiding(int tiles, AtlasFaces faces = AtlasFaces.Both)
        {
            var material = new AtlasUnifiedMaterial();
            if (tiles != 0) material.Render.HiddenTiles.Add(new AtlasTileDiscard { Space = AtlasTextureSpace.UV0, Tiles = tiles, Faces = faces });
            return material;
        }

        private static AtlasGeneratorJob Job(params AtlasUnifiedMaterial[] materials)
        {
            var job = new AtlasGeneratorJob();
            for (int i = 0; i < materials.Length; i++)
                job.Sources.Add(new AtlasGeneratorSource { Material = materials[i], UVRegion = new Rect(i / (float)materials.Length, 0f, 1f / materials.Length, 1f) });
            return job;
        }

        private static void AssertUV(Vector2 expected, Vector2 actual, string message) =>
            Assert.That(Vector2.Distance(expected, actual), Is.LessThan(1e-5f), $"{message}: expected {expected}, was {actual}");

        [Test]
        public void TilesMoveOnlyWhereTheOutputWouldHideThemDifferently()
        {
            int a = Tile(1, 0), output = Tile(1, 0) | Tile(2, 0);
            AssertUV(new Vector2(1.5f, 0.5f), AtlasHiddenTilesFeature.Map(new Vector2(1.5f, 0.5f), a, 0, output, 0), "hidden for the source and the output: kept");
            AssertUV(new Vector2(-0.5f, -0.5f), AtlasHiddenTilesFeature.Map(new Vector2(2.5f, 0.5f), a, 0, output, 0), "shown for the source, hidden by another's: outside the grid");
            AssertUV(new Vector2(-0.5f, -0.75f), AtlasHiddenTilesFeature.Map(new Vector2(1.5f, 0.25f), 0, 0, output, 0), "a source without hidden tiles: outside the grid");
            AssertUV(new Vector2(5.5f, 0.5f), AtlasHiddenTilesFeature.Map(new Vector2(5.5f, 0.5f), 0, 0, output, 0), "outside the grid: never hidden");
        }

        [Test]
        public void LiveTilesGoOnAFreeUVChannelForEverySource()
        {
            var job = Job(Hiding(Tile(1, 0)), Hiding(Tile(2, 0)), Hiding(0));
            var plan = AtlasOutputPipeline.Resolve(job, AtlasShaderRegistry.Output("poiyomi10").Capabilities);
            var hidden = plan.Output.Render.HiddenTiles.Single();
            Assert.AreEqual(Tile(1, 0) | Tile(2, 0), hidden.Tiles);
            Assert.AreEqual(AtlasTextureSpace.UV1, hidden.Space);
            CollectionAssert.Contains(plan.UVChannels, 1);
            var preserved = plan.SourceRemaps[2].PreservedUVs.Single();
            Assert.AreEqual(1, preserved.Channel);
            AssertUV(new Vector2(-0.5f, -0.5f), preserved.Convert(new Vector2(1.5f, 0.5f)), "the source without hidden tiles");
            Assert.IsEmpty(plan.SourceRemaps[0].RemovedTiles);
        }

        [Test]
        public void WithoutTileDiscardTheTrianglesAreRemoved()
        {
            var job = Job(Hiding(Tile(1, 0)), Hiding(Tile(3, 3), AtlasFaces.Back));
            var plan = AtlasOutputPipeline.Resolve(job, AtlasShaderRegistry.Output("mobiletoon").Capabilities);
            Assert.IsEmpty(plan.Output.Render.HiddenTiles);
            Assert.AreEqual(Tile(1, 0), plan.SourceRemaps[0].RemovedTiles.Single().Tiles);
            Assert.IsFalse(plan.SourceRemaps.ContainsKey(1) && plan.SourceRemaps[1].RemovedTiles.Count > 0, "one side only can't be removed");
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("one side only")));
        }

        [Test]
        public void TheRemapRemovesHiddenTrianglesAndWritesPreservedUVs()
        {
            var a = AtlasTestMaterials.Standard();
            var output = AtlasTestMaterials.Standard();
            var mesh = new Mesh();
            created.AddRange(new Object[] { a, output, mesh });
            // Inside their tiles: a vertex at u = 1 is already in tile (1, 0).
            var quad = new[] { new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.9f, 0.9f), new Vector2(0.1f, 0.9f) };
            var uv = quad.Concat(quad.Select(q => q + new Vector2(1f, 0f))).ToArray();
            mesh.vertices = uv.Select((u, i) => new Vector3(i, 0f, 0f)).ToArray();
            mesh.uv = uv;
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 }, 0);
            var target = new AtlasRemapTarget { Region = new Rect(0f, 0f, 1f, 1f), Output = output };
            target.RemovedTiles.Add(new AtlasTileDiscard { Space = AtlasTextureSpace.UV0, Tiles = Tile(1, 0) });
            target.PreservedUVs.Add(new AtlasPreservedUV { Channel = 1, SourceChannel = 0, Convert = q => q + new Vector2(10f, 0f) });
            var result = AtlasMeshRemapper.Remap(mesh, new[] { a }, new Dictionary<Material, AtlasRemapTarget> { [a] = target });
            created.Add(result.Mesh);
            Assert.AreEqual(6, result.Mesh.GetTriangles(0).Length, "the quad in tile (1, 0) is gone");
            Assert.AreEqual(2, result.Removed[a]);
            var preserved = new List<Vector2>();
            result.Mesh.GetUVs(1, preserved);
            int corner = result.Mesh.GetTriangles(0)[1];
            AssertUV(uv[1] + new Vector2(10f, 0f), preserved[corner], "the source UV, converted");
        }

        [Test]
        public void TheWritersSetTheTilesTheParsersReadBack()
        {
            foreach (var shader in AtlasShaderRegistry.InstalledOutputShaders.Where(s => s.Capabilities.TileDiscard))
            {
                var unified = new AtlasUnifiedMaterial();
                unified.Render.HiddenTiles.Add(new AtlasTileDiscard { Space = AtlasTextureSpace.UV1, Tiles = Tile(1, 0) | Tile(2, 0) });
                unified.Render.HiddenTiles.Add(new AtlasTileDiscard { Space = AtlasTextureSpace.UV1, Tiles = Tile(3, 3), Faces = AtlasFaces.Back });
                var material = new Material(shader.FindShader());
                created.Add(material);
                shader.Write(unified, material);
                var parsed = AtlasShaderRegistry.Detect(material).Parse(material).Render.HiddenTiles;
                var both = parsed.Single(t => t.Faces == AtlasFaces.Both);
                Assert.AreEqual(Tile(1, 0) | Tile(2, 0), both.Tiles, shader.DisplayName);
                Assert.AreEqual(AtlasTextureSpace.UV1, both.Space, shader.DisplayName);
                var back = parsed.Single(t => t.Faces == AtlasFaces.Back);
                Assert.AreEqual(Tile(3, 3), back.Tiles, shader.DisplayName);

                var without = new Material(shader.FindShader());
                created.Add(without);
                shader.Write(new AtlasUnifiedMaterial(), without);
                Assert.IsFalse(AtlasShaderRegistry.Detect(without).Parse(without).Render.HiddenTiles.Any(t => t.Tiles != 0), shader.DisplayName + ": none");
            }
        }
    }
}
