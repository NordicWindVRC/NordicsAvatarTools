using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // The world AO blocker (ATLAS_OUTPUT.md, "Feature inventory"): triangles whose UVs lie inside
    // the tile left of the default one (U −1 to 0, V 0 to 1) render only to depth, so screen-space
    // effects such as ambient occlusion treat them as solid while they stay invisible.
    // - Kept live, where the output has a blocker: one switch for the whole material. When every
    //   source's blocker reads UV0, the output's does too, and the remap leaves those triangles'
    //   UV0 in that tile instead of moving them into the atlas, where they'd show. Otherwise each
    //   source's blocker channel goes into a free UV channel the output's blocker reads (the other
    //   sources get UVs outside the tile); without a free channel, blockers on UV0 still stay live
    //   and the others are removed from the mesh, listed.
    // - Removed from the mesh, on any output: the triangles stay invisible but no longer block.
    // - Off: they're shown, as in the source shader without the option.
    public sealed class AtlasAOBlockerFeature : IAtlasOutputFeature
    {
        public const string FeatureId = "ao-blocker";

        private static readonly AtlasFeatureMode Live = new AtlasFeatureMode
        {
            Id = "live", Label = "Kept live (depth only)", Kind = AtlasFeatureModeKind.Native, Requires = c => c.AOBlocker
        };

        private static readonly AtlasFeatureMode Removed = new AtlasFeatureMode
        {
            Id = "removed", Label = "Removed from the mesh", Kind = AtlasFeatureModeKind.Native
        };

        private static readonly AtlasFeatureMode[] ModeList = { Live, Removed, AtlasFeatureMode.Off };

        public string Id => FeatureId;
        public string DisplayName => "World AO Blocker";
        public AtlasFeatureSection Section => AtlasFeatureSection.Render;
        public int Order => 3;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        public bool IsPresent(AtlasUnifiedMaterial source) => source?.Render.AOBlocker != null;

        private static int Channel(AtlasUnifiedMaterial material) =>
            Mathf.Clamp((int)(material.Render.AOBlocker.Space - AtlasTextureSpace.UV0), 0, 3);

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var active = context.Sources.ToList();
            if (active.Count == 0) return;
            if (context.Mode == Removed)
            {
                foreach (int i in active) context.Plan.Remap(i).RemoveBlockerChannel = Channel(job.Sources[i].Material);
                return;
            }

            // Flip Normal only changes depth-normal passes, which VRChat's built-in pipeline doesn't
            // render; it's kept where any source sets it.
            bool flip = active.Any(i => job.Sources[i].Material.Render.AOBlocker.FlipNormal);
            if (active.All(i => Channel(job.Sources[i].Material) == 0))
            {
                foreach (int i in active) context.Plan.Remap(i).KeepBlockerTile = true;
                context.Output.Render.AOBlocker = new AtlasAOBlocker { Space = AtlasTextureSpace.UV0, FlipNormal = flip };
                return;
            }
            int channel = context.Plan.AllocateUVChannel(context.Capabilities.UVChannels);
            if (channel < 0)
            {
                var removed = new List<int>();
                foreach (int i in active)
                {
                    if (Channel(job.Sources[i].Material) == 0) context.Plan.Remap(i).KeepBlockerTile = true;
                    else
                    {
                        context.Plan.Remap(i).RemoveBlockerChannel = Channel(job.Sources[i].Material);
                        removed.Add(i);
                    }
                }
                context.Notes.Add("No UV channel is free for the world AO blocker on another UV channel; " +
                                  $"it's removed from the mesh on {AtlasLiveMasks.Numbers(removed)}.");
                if (removed.Count < active.Count)
                    context.Output.Render.AOBlocker = new AtlasAOBlocker { Space = AtlasTextureSpace.UV0, FlipNormal = flip };
                return;
            }
            // The blocker's channel as it is for the sources that have one; a UV outside the tile
            // (0, 0) for the others.
            for (int i = 0; i < job.Sources.Count; i++)
            {
                bool has = active.Contains(i);
                context.Plan.Remap(i).PreservedUVs.Add(new AtlasPreservedUV
                {
                    Channel = channel, SourceChannel = has ? Channel(job.Sources[i].Material) : 0,
                    Convert = has ? (System.Func<Vector2, Vector2>)null : _ => Vector2.zero
                });
            }
            context.Output.Render.AOBlocker = new AtlasAOBlocker { Space = AtlasTextureSpace.UV0 + channel, FlipNormal = flip };
        }

        /// <summary>Whether a UV lies inside the blocker's tile (U −1 to 0, V 0 to 1, edges
        /// excluded), as Poiyomi tests each vertex.</summary>
        public static bool InTile(Vector2 uv) => uv.x < 0f && uv.x > -1f && uv.y < 1f && uv.y > 0f;
    }
}
