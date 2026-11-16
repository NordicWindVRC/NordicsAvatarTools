using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Poiyomi Toon and Pro, one parser per version. The reading is in AtlasPoiyomiReader.
    /// See UNIFIED_MATERIAL_POIYOMI.md.
    /// </summary>
    public abstract class AtlasPoiyomiParser : AtlasParserBase
    {
        public override int Priority => 100;
        protected abstract int Major { get; }
        // Toon and Pro read the same way: Pro's extra sections are read where their properties
        // exist, from Pro 9 on. The variant only names the parser.
        protected abstract AtlasPoiyomiVariant Variant { get; }

        // The newest parser also takes versions it doesn't know (newer ones, or no version found).
        // Pro before 9 is read as Toon of its version: its Pro features aren't read.
        public override bool CanParse(Material m)
        {
            var variant = AtlasPoiyomiVersionDetector.DetectVariant(m);
            int detected = AtlasPoiyomiVersionDetector.Detect(m);
            if (variant == AtlasPoiyomiVariant.Pro && detected > 0 && detected < FirstPro) variant = AtlasPoiyomiVariant.Toon;
            if (variant != Variant) return false;
            return detected == Major || (Major == Newest && (detected == 0 || detected > Newest));
        }

        internal const int Newest = 11;
        // The first Pro version whose Pro features are read.
        internal const int FirstPro = 9;

        // Each parse reads through its own reader, which holds the state the sections share.
        protected override void Populate(Material m, AtlasUnifiedMaterial d) => new AtlasPoiyomiReader(Major).Read(m, d);
    }

    // Toon 7.3 read on 10's parser, as 8 with 7.3's names, lighting types and UVs.
    public sealed class AtlasPoiyomiToon7Parser : AtlasPoiyomiParser
    {
        public override string ParserId => "poiyomi-toon-7";
        public override string DisplayName => "Poiyomi Toon 7";
        protected override int Major => 7;
        protected override AtlasPoiyomiVariant Variant => AtlasPoiyomiVariant.Toon;
    }

    // Toon 8.0–8.1 read on 10's parser, as 9 with 8's own differences.
    public sealed class AtlasPoiyomiToon8Parser : AtlasPoiyomiParser
    {
        public override string ParserId => "poiyomi-toon-8";
        public override string DisplayName => "Poiyomi Toon 8";
        protected override int Major => 8;
        protected override AtlasPoiyomiVariant Variant => AtlasPoiyomiVariant.Toon;
    }

    // Toon 9.0–9.3 read on 10's parser: a few meanings differ by version, and the features
    // Poiyomi 10 rewrote are read with 10's meaning and noted (AtlasPoiyomiLegacy.cs).
    public sealed class AtlasPoiyomiToon9Parser : AtlasPoiyomiParser
    {
        public override string ParserId => "poiyomi-toon-9";
        public override string DisplayName => "Poiyomi Toon 9";
        protected override int Major => 9;
        protected override AtlasPoiyomiVariant Variant => AtlasPoiyomiVariant.Toon;
    }

    public sealed class AtlasPoiyomiToon10Parser : AtlasPoiyomiParser
    {
        public override string ParserId => "poiyomi-toon-10";
        public override string DisplayName => "Poiyomi Toon 10";
        protected override int Major => 10;
        protected override AtlasPoiyomiVariant Variant => AtlasPoiyomiVariant.Toon;
    }

    // Toon 11 keeps 10's names; its additions read as their defaults (no effect) in 10.
    public sealed class AtlasPoiyomiToon11Parser : AtlasPoiyomiParser
    {
        public override string ParserId => "poiyomi-toon-11";
        public override string DisplayName => "Poiyomi Toon 11";
        protected override int Major => 11;
        protected override AtlasPoiyomiVariant Variant => AtlasPoiyomiVariant.Toon;
    }

    // Pro is Toon plus extras. Pro 9.0–9.3 is read like Toon 9; the Pro features Pro 10 rewrote
    // are read with 10's meaning and noted (AtlasPoiyomiLegacy.cs).
    public sealed class AtlasPoiyomiPro9Parser : AtlasPoiyomiParser
    {
        public override string ParserId => "poiyomi-pro-9";
        public override string DisplayName => "Poiyomi Pro 9";
        protected override int Major => 9;
        protected override AtlasPoiyomiVariant Variant => AtlasPoiyomiVariant.Pro;
    }

    public sealed class AtlasPoiyomiPro10Parser : AtlasPoiyomiParser
    {
        public override string ParserId => "poiyomi-pro-10";
        public override string DisplayName => "Poiyomi Pro 10";
        protected override int Major => 10;
        protected override AtlasPoiyomiVariant Variant => AtlasPoiyomiVariant.Pro;
    }

    public sealed class AtlasPoiyomiPro11Parser : AtlasPoiyomiParser
    {
        public override string ParserId => "poiyomi-pro-11";
        public override string DisplayName => "Poiyomi Pro 11";
        protected override int Major => 11;
        protected override AtlasPoiyomiVariant Variant => AtlasPoiyomiVariant.Pro;
    }
}
