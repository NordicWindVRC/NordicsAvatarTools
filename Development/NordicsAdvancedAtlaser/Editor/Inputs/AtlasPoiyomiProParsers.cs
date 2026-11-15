using UnityEngine;
namespace NordicWind.AdvancedAtlaser.Editor
{
    public sealed class AtlasPoiyomiPro11Parser : AtlasPoiyomiParserBase
    {
        public override string ParserId => "poiyomi-pro-11";
        public override string DisplayName => "Poiyomi Pro 11.x";
        public override int Priority => 110;
        protected override bool IsPro => true;
        public override bool CanParse(Material m) => AtlasPoiyomiVersionDetector.DetectVariant(m) == AtlasPoiyomiVariant.Pro && AtlasPoiyomiVersionDetector.Detect(m) == 11;
    }

    public sealed class AtlasPoiyomiPro10Parser : AtlasPoiyomiParserBase
    {
        public override string ParserId => "poiyomi-pro-10";
        public override string DisplayName => "Poiyomi Pro 10.x";
        public override int Priority => 110;
        protected override bool IsPro => true;
        public override bool CanParse(Material m) => AtlasPoiyomiVersionDetector.DetectVariant(m) == AtlasPoiyomiVariant.Pro && AtlasPoiyomiVersionDetector.Detect(m) == 10;
    }

    // Poiyomi Pro whose major version can't be resolved.
    public sealed class AtlasPoiyomiProGenericParser : AtlasPoiyomiParserBase
    {
        public override string ParserId => "poiyomi-pro";
        public override string DisplayName => "Poiyomi Pro";
        public override int Priority => 100;
        protected override bool IsPro => true;
        public override bool CanParse(Material m)
        {
            if (AtlasPoiyomiVersionDetector.DetectVariant(m) != AtlasPoiyomiVariant.Pro) return false;
            int major = AtlasPoiyomiVersionDetector.Detect(m);
            return major != 10 && major != 11;
        }
    }
}
