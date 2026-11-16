using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Colour (ATLAS_OUTPUT.md, "Feature inventory"): the base colour map, made of each
    // source's base colour texture placed in its region as it is.
    public sealed class AtlasColorFeature : IAtlasOutputFeature
    {
        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Base colour map", Kind = AtlasFeatureModeKind.Native,
                RequiredMaps = new[] { AtlasVirtualMap.BaseColor }
            },
            AtlasFeatureMode.Off
        };

        public string Id => "color";
        public string DisplayName => "Colour";
        public AtlasFeatureSection Section => AtlasFeatureSection.Surface;
        public int Order => 0;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        // Every material has a base colour.
        public bool IsPresent(AtlasUnifiedMaterial source) => source != null;

        // The map holds the sources' base colour textures, so the colour is white.
        public void Resolve(AtlasFeatureContext context)
        {
            var baseColor = context.Output.Surface.BaseColor;
            baseColor.Color = Color.white;
            baseColor.Map = context.RequestMap(AtlasVirtualMap.BaseColor, AtlasTextureInterpretation.Color);
        }
    }
}
