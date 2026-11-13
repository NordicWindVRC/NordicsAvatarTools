using System;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>Sizes derived from an output's layout, shared by the inspector and the build.</summary>
    public static class AtlasOutputLayout
    {
        // Geometry bakes are 1.5× a source's final area in the atlas, for headroom when the
        // bake is resampled into the atlas, but not above BakeLimit per axis. A region larger
        // than the limit is baked 1:1.
        public const float BakeScale = 1.5f;
        public const int BakeLimit = 4096;

        public static int AssignedSourceCount(AtlasOutputConfiguration configuration)
        {
            int count = 0;
            if (configuration?.sources != null)
                foreach (var source in configuration.sources)
                    if (source != null && source.material != null) count++;
            return count;
        }

        /// <summary>Padding separates source regions, so it only applies with two or more sources.</summary>
        public static int EffectivePadding(AtlasOutputConfiguration configuration) =>
            AssignedSourceCount(configuration) > 1 ? Math.Max(0, configuration.regionPadding) : 0;

        /// <summary>A source region's size in the atlas, in pixels, before padding.</summary>
        public static Vector2Int IslandSize(Rect uvRegion, AtlasTextureSize main)
        {
            int width = main != null ? main.width : 1, height = main != null ? main.height : 1;
            return new Vector2Int(Math.Max(1, Mathf.RoundToInt(uvRegion.width * width)),
                                  Math.Max(1, Mathf.RoundToInt(uvRegion.height * height)));
        }

        /// <summary>The geometry bake size for a source region.</summary>
        public static Vector2Int BakeSize(Rect uvRegion, AtlasTextureSize main)
        {
            var island = IslandSize(uvRegion, main);
            return new Vector2Int(BakeAxis(island.x), BakeAxis(island.y));
        }
        private static int BakeAxis(int island) =>
            Math.Min(Mathf.RoundToInt(island * BakeScale), Math.Max(island, BakeLimit));
    }
}
