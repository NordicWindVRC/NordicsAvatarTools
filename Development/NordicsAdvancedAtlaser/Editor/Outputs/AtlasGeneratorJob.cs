using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Output-generation API boundary: no Material, Shader, parser, source property IDs,
    /// original renderer or input build context reaches an output implementation.
    /// Every shader generator consumes the same normalized union.
    /// </summary>
    public sealed class AtlasGeneratorJob
    {
        public string OutputName;
        public string OutputShaderId;
        public int RegionPadding;
        public AtlasTextureDimensions MainTexture;
        public readonly List<AtlasOutputSetting> Settings = new List<AtlasOutputSetting>();
        public readonly List<AtlasGeneratorSource> Sources = new List<AtlasGeneratorSource>();
    }
    public sealed class AtlasGeneratorSource
    {
        public AtlasUnifiedMaterial Material;
        public Rect UVRegion;
        public bool GeometryBakingAvailable;
        // Null when disabled; missing submeshes instead have an explicit empty/flat bake.
        public AtlasBakedInputSet Bakes;
    }
}
