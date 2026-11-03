using UnityEngine;
namespace NordicWind.AdvancedAtlaser.Editor
{
    public sealed class AtlasStandardParser : AtlasParserBase
    {
        public override string ParserId => "standard";
        public override string DisplayName => "Standard";
        public override bool CanParse(Material m) => Name(m) == "Standard";
        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            base.Populate(m,d);
            AtlasStandardFeatureParser.Populate(m,d,false,true,false);
        }
    }
    public sealed class AtlasStandardSpecularParser : AtlasParserBase
    {
        public override string ParserId => "standard-specular";
        public override string DisplayName => "Standard (Specular setup)";
        public override bool CanParse(Material m) => Name(m) == "Standard (Specular setup)";
        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            base.Populate(m,d);
            AtlasStandardFeatureParser.Populate(m,d,true,true,false);
        }
    }
}
