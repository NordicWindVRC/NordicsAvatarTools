using UnityEngine;
namespace NordicWind.AdvancedAtlaser.Editor
{
    // Matches Poiyomi Toon when the family and shader variant are known,
    // but the exact major version cannot be resolved yet.
    public sealed class AtlasPoiyomiGenericParser : AtlasPoiyomiParserBase
    {
        public override string ParserId => "poiyomi-toon";
        public override string DisplayName => "Poiyomi Toon";
        public override int Priority => 100;
        public override bool CanParse(Material m)
            => AtlasPoiyomiVersionDetector.DetectVariant(m) == AtlasPoiyomiVariant.Toon
               && AtlasPoiyomiVersionDetector.Detect(m) == 0;
    }
}
