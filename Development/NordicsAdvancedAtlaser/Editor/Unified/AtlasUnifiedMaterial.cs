using System.Collections.Generic;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// What a material looks like, independent of the shader it came from. Input parsers fill it;
    /// output writers build their materials only from it. The specification is
    /// UNIFIED_MATERIAL_FEATURES.md; section numbers in comments refer to it.
    /// Built fresh for every build and never saved.
    /// </summary>
    public sealed class AtlasUnifiedMaterial
    {
        public readonly AtlasSurface Surface = new AtlasSurface();
        public readonly AtlasLighting Lighting = new AtlasLighting();
        // Applied in list order, each on top of the ones before.
        public readonly List<AtlasLayer> Layers = new List<AtlasLayer>();
        public readonly AtlasFur Fur = new AtlasFur();
        public readonly AtlasRender Render = new AtlasRender();
        public readonly List<AtlasIssue> Issues = new List<AtlasIssue>();

        // Adds a report line, at most one per kind and feature.
        public void Report(AtlasIssueKind kind, string feature, string message)
        {
            foreach (var issue in Issues)
                if (issue.Kind == kind && issue.Feature == feature) return;
            Issues.Add(new AtlasIssue { Kind = kind, Feature = feature, Message = message });
        }
    }

    public enum AtlasIssueKind
    {
        // Affects the look but has no typed home yet.
        Unmapped,
        // Can't be read at all, for example a shader whose source isn't available.
        Unsupported,
        // Deliberately not mapped, for example a world-only feature.
        Ignored,
        // Information about how the material was read.
        Note
    }

    public sealed class AtlasIssue
    {
        public AtlasIssueKind Kind;
        // What the line is about, for example "outline" or "shader.not-installed".
        public string Feature;
        public string Message;
        public bool IsWarning => Kind == AtlasIssueKind.Unmapped || Kind == AtlasIssueKind.Unsupported;
    }
}
