using System;
using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // A virtual map's part of a baked texture: its request, and how its evaluated content goes
    // into the texture's channels. Without Copy, the evaluated content is the source's whole
    // content (a texture of one map, such as a normal map).
    public sealed class AtlasBakePart
    {
        public AtlasMapRequest Request;
        public Action<NordicsTextureWrapper, NordicsTextureWrapper> Copy;
    }

    // Step 3 of the pipeline (ATLAS_OUTPUT.md, "Pipeline" and "Memory budget"): one texture at a
    // time, source by source: each of its virtual maps evaluated at the source's bake size and
    // copied into the source's content, the source's UV islands grown over the parts its meshes
    // don't use, and the content placed in its region of the atlas. The maps of a source share
    // its coverage, so it is painted, grown and placed once for all of them. The pixels between
    // the regions take the color of the nearest region, so filtering and mipmaps at an island's
    // or region's edge don't pull in other colors.
    public static class AtlasMapBaker
    {
        /// <summary>One virtual map on its own, as it is evaluated.</summary>
        public static NordicsTextureWrapper Bake(AtlasMapRequest request, AtlasGeneratorJob job, int width, int height, List<string> timings = null) =>
            Bake(new[] { new AtlasBakePart { Request = request } }, AtlasMapEvaluator.Settings(request.Map), Color.clear, request.Map.ToString(),
                job, width, height, timings);

        /// <summary>A texture of several maps: each source's content starts at
        /// <paramref name="neutral"/>, the value of the channels no map fills.
        /// `timings` (optional) gets a "[Time] …" line for each step.</summary>
        public static NordicsTextureWrapper Bake(IReadOnlyList<AtlasBakePart> parts, TextureSettings settings, Color neutral, string name,
            AtlasGeneratorJob job, int width, int height, List<string> timings = null)
        {
            var atlas = new NordicsTextureWrapper(width, height, settings);
            try
            {
                var size = new AtlasTextureSize { width = width, height = height };
                var sources = Enumerable.Range(0, job.Sources.Count).Where(i => parts.Any(p => p.Request.Sources.Contains(i))).ToList();
                if (sources.Count == 0)
                {
                    atlas.Fill(neutral);
                    return atlas;
                }
                int padding = AtlasOutputLayout.SidePadding(job.RegionPadding, job.MainTexture, width);
                for (int k = 0; k < sources.Count; k++)
                {
                    int index = sources[k];
                    AtlasProgress.Source(name, k, sources.Count);
                    var source = job.Sources[index];
                    var bake = AtlasOutputLayout.BakeSize(source.UVRegion, size);
                    string label = $"{name} source {index} ({bake.x}×{bake.y})";
                    var content = new NordicsTextureWrapper(bake.x, bake.y, settings);
                    try
                    {
                        content.Fill(neutral);
                        // What the source's maps share (textures, bus channels, vertex colors) is made once.
                        using (AtlasEvaluationCache.Scope(timings))
                            foreach (var part in parts)
                            {
                                if (!part.Request.Sources.Contains(index)) continue;
                                string map = $"{part.Request.Map} source {index} ({bake.x}×{bake.y})";
                                AtlasEvaluationCache.Name("evaluate " + map);
                                NordicsTextureWrapper evaluated;
                                using (new AtlasStageTimer("evaluate " + map, timings))
                                    evaluated = part.Request.Evaluate != null
                                        ? part.Request.Evaluate(index, source.Material, bake.x, bake.y)
                                        : AtlasMapEvaluator.Evaluate(part.Request.Map, source.Material, bake.x, bake.y);
                                if (part.Copy == null)
                                {
                                    content.Dispose();
                                    content = evaluated;
                                }
                                else
                                    using (evaluated)
                                        part.Copy(content, evaluated);
                            }
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
                        using (new AtlasStageTimer("place " + label, timings))
                            atlas.Blit(content, region: source.UVRegion, padding: padding, sampling: TextureSamplingMode.Area);
                    }
                    finally
                    {
                        content.Dispose();
                    }
                }
                using (new AtlasStageTimer($"fill between regions (dilate) {name} ({width}×{height})", timings))
                    atlas.Dilate();
                // A map's Finish sees the texture that holds it, the map in RGB.
                foreach (var part in parts) part.Request.Finish?.Invoke(atlas);
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
