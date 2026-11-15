using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Any material no other parser recognises. While the parsers are rebuilt
    /// (UNIFIED_MATERIAL_REFACTOR.md) it reads only the base colour, under the common names.
    /// </summary>
    public sealed class AtlasFallbackParser : AtlasParserBase
    {
        public override string ParserId => "fallback";
        public override string DisplayName => "Fallback";
        public override int Priority => -1000;
        public override bool CanParse(Material material) => material != null;

        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            if (AtlasMaterialReader.Has(m, "_MainTex"))
                AtlasSourceMapping.Color(m, d.Surface.BaseColor, "_MainTex",
                    AtlasMaterialReader.Has(m, "_Color") ? "_Color" : "_BaseColor");
            else if (AtlasMaterialReader.Has(m, "_BaseMap"))
                AtlasSourceMapping.Color(m, d.Surface.BaseColor, "_BaseMap", "_BaseColor");
            else
                AtlasSourceMapping.Color(m, d.Surface.BaseColor, "_BaseColorMap", "_BaseColor");
            d.Report(AtlasIssueKind.Unmapped, "material",
                "Only the base colour is read while the Atlaser's input parsers are rebuilt.");
        }
    }
}
