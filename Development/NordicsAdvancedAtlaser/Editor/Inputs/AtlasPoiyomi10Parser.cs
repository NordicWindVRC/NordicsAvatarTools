using UnityEngine;
namespace NordicWind.AdvancedAtlaser.Editor
{
    public sealed class AtlasPoiyomi10Parser : AtlasPoiyomiParserBase
    {
        public override string ParserId => "poiyomi-toon-10";
        public override string DisplayName => "Poiyomi Toon 10.x";
        public override int Priority => 110;
        public override bool CanParse(Material m) => AtlasPoiyomiVersionDetector.DetectVariant(m) == AtlasPoiyomiVariant.Toon && AtlasPoiyomiVersionDetector.Detect(m) == 10;
    }
}
