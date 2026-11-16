using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Step 3 of the pipeline (ATLAS_OUTPUT.md, "Pipeline" and "Memory budget"): one requested
    // virtual map at a time, each source evaluated at its bake size, placed in its region of the
    // atlas, and released before the next. The pixels between the regions take the colour of the
    // nearest region, so filtering and mipmaps at a region's edge don't pull in other colours.
    public static class AtlasMapBaker
    {
        public static NordicsTextureWrapper Bake(AtlasMapRequest request, AtlasGeneratorJob job, int width, int height)
        {
            var atlas = new NordicsTextureWrapper(width, height, AtlasMapEvaluator.Settings(request.Map));
            try
            {
                var size = new AtlasTextureSize { width = width, height = height };
                foreach (int index in request.Sources)
                {
                    var source = job.Sources[index];
                    var bake = AtlasOutputLayout.BakeSize(source.UVRegion, size);
                    using (var content = AtlasMapEvaluator.Evaluate(request.Map, source.Material, bake.x, bake.y))
                        // Padding 0: the content's usage is copied, so the dilation knows the regions.
                        atlas.Blit(content, region: source.UVRegion, padding: 0, sampling: TextureSamplingMode.Area);
                }
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
