using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // A UV channel the remap writes for one source: the source's own UVs from SourceChannel,
    // converted, into the output mesh's Channel (1–3).
    public sealed class AtlasPreservedUV
    {
        public int Channel;
        public int SourceChannel;
        public Func<Vector2, Vector2> Convert;
    }

    // What the remap does to one source's triangles besides moving them into its region.
    public sealed class AtlasSourceRemap
    {
        public readonly List<AtlasPreservedUV> PreservedUVs = new List<AtlasPreservedUV>();
        // Triangles with a vertex in one of these tiles (hidden on both sides) are left out.
        public readonly List<AtlasTileDiscard> RemovedTiles = new List<AtlasTileDiscard>();
        // World AO blocker: triangles with a vertex in its tile (AtlasAOBlockerFeature.InTile) on
        // this UV channel are left out; −1: none.
        public int RemoveBlockerChannel = -1;
        // World AO blocker: triangles with a vertex in its tile on UV0 keep their UV0 as it is, so
        // the output's blocker on UV0 still hides them.
        public bool KeepBlockerTile;
    }

    // Hidden UV tiles (ATLAS_OUTPUT.md, "UV channels"): triangles a source hides by their UV tile
    // (Poiyomi's UV tile discard).
    // - Live: the remap writes each source's UVs into a free UV channel with their tiles mapped
    //   to the output's hidden tiles, and the output hides those tiles (both sides, and one side
    //   where it can). A tile keeps its number where the sources agree on it, so animations of
    //   the tile toggles keep their meaning there; sources without hidden tiles get UVs outside
    //   the 4 × 4 grid, which is never hidden.
    // - Removed: the remap leaves out the triangles in tiles hidden on both sides; any output.
    // - Off: the triangles are shown.
    public sealed class AtlasHiddenTilesFeature : IAtlasOutputFeature
    {
        public const string FeatureId = "hidden-tiles";

        private static readonly AtlasFeatureMode Live = new AtlasFeatureMode
        {
            Id = "live", Label = "Kept live (tile discard)", Kind = AtlasFeatureModeKind.Native,
            Requires = c => c.TileDiscard && c.UVChannels > 0
        };

        private static readonly AtlasFeatureMode Removed = new AtlasFeatureMode
        {
            Id = "removed", Label = "Removed from the mesh", Kind = AtlasFeatureModeKind.Native
        };

        private static readonly AtlasFeatureMode[] ModeList = { Live, Removed, AtlasFeatureMode.Off };

        public string Id => FeatureId;
        public string DisplayName => "Hidden UV Tiles";
        public AtlasFeatureSection Section => AtlasFeatureSection.Render;
        public int Order => 2;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        public bool IsPresent(AtlasUnifiedMaterial source) => source != null && source.Render.HiddenTiles.Any(t => t.Tiles != 0);

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var active = context.Sources.ToList();
            if (context.Mode == Removed)
            {
                var oneSided = new List<int>();
                foreach (int i in active)
                {
                    foreach (var set in job.Sources[i].Material.Render.HiddenTiles.Where(t => t.Tiles != 0))
                        if (set.Faces == AtlasFaces.Both) context.Plan.Remap(i).RemovedTiles.Add(set);
                        else if (!oneSided.Contains(i)) oneSided.Add(i);
                }
                if (oneSided.Count > 0)
                    context.Notes.Add($"Tiles hidden on one side only can't be removed from the mesh; they're shown on {Numbers(oneSided)}.");
                return;
            }

            int channel = context.Plan.AllocateUVChannel(context.Capabilities.UVChannels);
            if (channel < 0)
            {
                context.Notes.Add("No UV channel is free for the hidden UV tiles; they're shown.");
                return;
            }
            // One side can be hidden live where the output has it: the first side the sources use.
            var face = context.Capabilities.FaceTileDiscard
                ? active.SelectMany(i => job.Sources[i].Material.Render.HiddenTiles).Where(t => t.Tiles != 0 && t.Faces != AtlasFaces.Both)
                    .Select(t => (AtlasFaces?)t.Faces).FirstOrDefault()
                : null;
            var plans = new Dictionary<int, (int Both, int Face, int Space)>();
            var shown = new List<int>();
            foreach (int i in active)
            {
                var sets = job.Sources[i].Material.Render.HiddenTiles.Where(t => t.Tiles != 0).ToList();
                var both = sets.Where(t => t.Faces == AtlasFaces.Both).ToList();
                var space = (both.Count > 0 ? both[0] : sets[0]).Space;
                int bothTiles = both.Where(t => t.Space == space).Aggregate(0, (m, t) => m | t.Tiles);
                int faceTiles = sets.Where(t => face.HasValue && t.Faces == face.Value && t.Space == space).Aggregate(0, (m, t) => m | t.Tiles);
                // Sets on another UV channel, or on the side the output doesn't hide, stay shown.
                if (sets.Any(t => t.Space != space || (t.Faces != AtlasFaces.Both && (!face.HasValue || t.Faces != face.Value)))) shown.Add(i);
                plans[i] = (bothTiles, faceTiles, (int)space - (int)AtlasTextureSpace.UV0);
            }
            int hiddenBoth = plans.Values.Aggregate(0, (m, p) => m | p.Both);
            int hiddenFace = plans.Values.Aggregate(0, (m, p) => m | p.Face);
            var inexact = new List<int>();
            for (int i = 0; i < job.Sources.Count; i++)
            {
                if (!plans.TryGetValue(i, out var plan)) plan = (0, 0, 0);
                if (Enumerable.Range(0, 16).Any(t => Pick(t, plan.Both, plan.Face, hiddenBoth, hiddenFace) < -1)) inexact.Add(i);
                int b = plan.Both, f = plan.Face;
                context.Plan.Remap(i).PreservedUVs.Add(new AtlasPreservedUV
                {
                    Channel = channel, SourceChannel = plan.Space,
                    Convert = uv => Map(uv, b, f, hiddenBoth, hiddenFace)
                });
            }
            var output = context.Output.Render.HiddenTiles;
            var outputSpace = AtlasTextureSpace.UV0 + channel;
            if (hiddenBoth != 0) output.Add(new AtlasTileDiscard { Space = outputSpace, Tiles = hiddenBoth, Faces = AtlasFaces.Both });
            if (hiddenFace != 0 && face.HasValue) output.Add(new AtlasTileDiscard { Space = outputSpace, Tiles = hiddenFace, Faces = face.Value });
            if (shown.Count > 0)
                context.Notes.Add($"Hidden UV tiles on another UV channel, or on a side the output doesn't hide, are shown on {Numbers(shown)}.");
            if (inexact.Count > 0)
                context.Notes.Add($"The hidden UV tiles of {Numbers(inexact)} can't all be kept apart from the other sources'; some of their triangles may be hidden on a different side.");
        }

        private static string Numbers(IEnumerable<int> indices)
        {
            var list = indices.Select(i => (i + 1).ToString()).ToList();
            return (list.Count == 1 ? "source " : "sources ") + string.Join(", ", list);
        }

        private static bool Has(int tiles, int tile) => tile >= 0 && ((tiles >> tile) & 1) != 0;

        // The output tile for a source tile (−1: outside the grid), or −2 when none hides it the
        // same way. The tile itself where it fits, so its number keeps its meaning.
        private static int Pick(int tile, int sourceBoth, int sourceFace, int outputBoth, int outputFace)
        {
            bool both = Has(sourceBoth, tile), face = Has(sourceFace, tile);
            bool Fits(int t) => t >= 0 ? Has(outputBoth, t) == both && Has(outputFace, t) == face : !both && !face;
            if (Fits(tile)) return tile;
            if (!both && !face) return -1;
            for (int t = 0; t < 16; t++)
                if (Fits(t)) return t;
            return -2;
        }

        /// <summary>A source vertex's UV (in its hidden tiles' channel) for the output's channel:
        /// moved by whole tiles to one the output hides as the source did, keeping its place in
        /// the tile.</summary>
        public static Vector2 Map(Vector2 uv, int sourceBoth, int sourceFace, int outputBoth, int outputFace)
        {
            int x = Mathf.FloorToInt(uv.x), y = Mathf.FloorToInt(uv.y);
            int tile = x >= 0 && x < 4 && y >= 0 && y < 4 ? y * 4 + x : -1;
            int picked = Pick(tile, sourceBoth, sourceFace, outputBoth, outputFace);
            if (picked == tile || picked == -2) return uv;
            var within = new Vector2(uv.x - x, uv.y - y);
            return picked == -1 ? within - Vector2.one : within + new Vector2(picked % 4, picked / 4);
        }
    }
}
