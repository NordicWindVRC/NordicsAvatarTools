namespace NordicWind.AdvancedAtlaser.Editor
{
    public static class AtlasMaterialNormalizer
    {
        public static void Prepare(AtlasInputMaterialJob job)
        {
            if(job == null || job.SourceMaterial == null) return;
            // Every registered parser returns the exact same agnostic representation.
            // Parsers return a typed semantic model with explicit normalization issues.
            // Output generators must never need to inspect source shader property names.
            job.Unified=job.Parser?.Parse(job.SourceMaterial);
        }
    }
}
