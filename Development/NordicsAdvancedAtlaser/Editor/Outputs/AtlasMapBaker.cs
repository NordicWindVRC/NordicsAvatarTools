using System.Collections.Generic;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Step 3 of the pipeline (ATLAS_OUTPUT.md, "Pipeline" and "Memory budget"): one requested
    // virtual map at a time, each source evaluated at its bake size, its UV islands grown over
    // the parts its meshes don't use, placed in its region of the atlas, and released before the
    // next. The pixels between the regions take the color of the nearest region, so filtering
    // and mipmaps at an island's or region's edge don't pull in other colors.
    public static class AtlasMapBaker
    {
        // `timings` (optional) gets a "[Time] …" line for each step.
        public static NordicsTextureWrapper Bake(AtlasMapRequest request, AtlasGeneratorJob job, int width, int height, List<string> timings = null)
        {
            var atlas = new NordicsTextureWrapper(width, height, AtlasMapEvaluator.Settings(request.Map));
            try
            {
                var size = new AtlasTextureSize { width = width, height = height };
                for (int k = 0; k < request.Sources.Count; k++)
                {
                    int index = request.Sources[k];
                    AtlasProgress.Source(request.Map, k, request.Sources.Count);
                    var source = job.Sources[index];
                    var bake = AtlasOutputLayout.BakeSize(source.UVRegion, size);
                    string label = $"{request.Map} source {index} ({bake.x}×{bake.y})";
                    var evaluate = new AtlasStageTimer("evaluate " + label, timings);
                    NordicsTextureWrapper evaluated;
                    // What the source's parts share (textures, bus channels, vertex colors) is made once.
                    using (AtlasEvaluationCache.Scope(timings, "evaluate " + label))
                        evaluated = request.Evaluate != null
                            ? request.Evaluate(index, source.Material, bake.x, bake.y)
                            : AtlasMapEvaluator.Evaluate(request.Map, source.Material, bake.x, bake.y);
                    using (var content = evaluated)
                    {
                        evaluate.Dispose();
                        NordicsTextureWrapper coverage;
                        using (new AtlasStageTimer("paint coverage " + label, timings))
                            coverage = AtlasCoverage.Paint(source.Coverage, bake.x, bake.y);
                        using (coverage)
                            if (coverage != null)
                                using (new AtlasStageTimer("grow islands (dilate) " + label, timings))
                                    AtlasCoverage.GrowIslands(content, coverage);
                        // The content goes inside the region, inset by the side padding, whose ring
                        // takes the content's edge; its usage is copied, so the dilation knows the
                        // regions. The mesh remap uses the same inset (AtlasOutputLayout.ContentRegion).
                        int padding = AtlasOutputLayout.SidePadding(job.RegionPadding, job.MainTexture, width);
                        using (new AtlasStageTimer("place " + label, timings))
                            atlas.Blit(content, region: source.UVRegion, padding: padding, sampling: TextureSamplingMode.Area);
                    }
                }
                using (new AtlasStageTimer($"fill between regions (dilate) {request.Map} ({width}×{height})", timings))
                    atlas.Dilate();
                request.Finish?.Invoke(atlas);
                return atlas;
            }
            catch
            {
                atlas.Dispose();
                throw;
            }
        }
    }
}
