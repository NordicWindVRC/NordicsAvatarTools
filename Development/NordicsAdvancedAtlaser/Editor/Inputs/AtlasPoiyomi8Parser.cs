using UnityEngine;
namespace NordicWind.AdvancedAtlaser.Editor
{
    public sealed class AtlasPoiyomi8Parser : AtlasPoiyomiParserBase
    {
        public override string ParserId => "poiyomi-toon-8";
        public override string DisplayName => "Poiyomi Toon 8.x";
        public override int Priority => 110;
        public override bool CanParse(Material m) => AtlasPoiyomiVersionDetector.DetectVariant(m) == AtlasPoiyomiVariant.Toon && AtlasPoiyomiVersionDetector.Detect(m) == 8;
    }
}
