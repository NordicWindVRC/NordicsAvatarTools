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
        // Already 0 when the output has fewer than two sources.
        public int RegionPadding;
        public AtlasTextureSize MainTexture;
        public readonly List<AtlasFeatureChoice> Features = new List<AtlasFeatureChoice>();
        public readonly List<AtlasGeneratorSource> Sources = new List<AtlasGeneratorSource>();
    }

    // The user's choices for one feature, with sources as indices into Sources.
    public sealed class AtlasFeatureChoice
    {
        public string FeatureId;
        // Empty: the feature's default.
        public string ModeId = "";
        // -1: the source with the largest region.
        public int ReferenceSource = -1;
        public readonly HashSet<int> DisabledSources = new HashSet<int>();
    }
    public sealed class AtlasGeneratorSource
    {
        public AtlasUnifiedMaterial Material;
        public Rect UVRegion;
        public bool GeometryBakingAvailable;
        // Optional normal map baked for this mesh by the author, applied at strength 1.
        //
        // A writer combines a source's normal maps into the output's main normal map in this
        // order, each on top of the previous one:
        //   1. PrebakedNormalMap (author-supplied, for example for a lower-detail mesh)
        //   2. Bakes' TangentNormalCorrection (changes made to the mesh during the build)
        //   3. the material's primary normal map (Material.Surface.Normal)
        //   4. its surface layers that target the normal, in layer order (secondary and detail
        //      normal maps)
        // Strengths are normalized to the largest strength among the atlas's sources.
        public Texture2D PrebakedNormalMap;
        // Null when disabled; missing submeshes instead have an explicit empty/flat bake.
        public AtlasBakedInputSet Bakes;
        // The UV triangles of the meshes that use the source, for its coverage; empty when
        // unknown, and then the source's whole texture is used.
        public readonly List<AtlasUVTriangles> Coverage = new List<AtlasUVTriangles>();
    }
}
