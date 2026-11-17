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
                foreach (int index in request.Sources)
                {
                    var source = job.Sources[index];
                    var bake = AtlasOutputLayout.BakeSize(source.UVRegion, size);
                    string label = $"{request.Map} source {index} ({bake.x}×{bake.y})";
                    var evaluate = new AtlasStageTimer("evaluate " + label, timings);
                    using (var content = AtlasMapEvaluator.Evaluate(request.Map, source.Material, bake.x, bake.y))
                    {
                        evaluate.Dispose();
                        NordicsTextureWrapper coverage;
                        using (new AtlasStageTimer("paint coverage " + label, timings))
                            coverage = AtlasCoverage.Paint(source.Coverage, bake.x, bake.y);
                        using (coverage)
                            if (coverage != null)
                                using (new AtlasStageTimer("grow islands (dilate) " + label, timings))
                                    AtlasCoverage.GrowIslands(content, coverage);
                        // Padding 0: the content's usage is copied, so the dilation knows the regions.
                        using (new AtlasStageTimer("place " + label, timings))
                            atlas.Blit(content, region: source.UVRegion, padding: 0, sampling: TextureSamplingMode.Area);
                    }
                }
                using (new AtlasStageTimer($"fill between regions (dilate) {request.Map} ({width}×{height})", timings))
                    atlas.Dilate();
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
