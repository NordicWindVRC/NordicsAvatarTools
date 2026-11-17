using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Color (ATLAS_OUTPUT.md, "Feature inventory"): the base color map, made of each
    // source's base color texture placed in its region as it is.
    public sealed class AtlasColorFeature : IAtlasOutputFeature
    {
        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Base color map", Kind = AtlasFeatureModeKind.Native,
                RequiredMaps = new[] { AtlasVirtualMap.BaseColor }
            },
            AtlasFeatureMode.Off
        };

        public string Id => "color";
        public string DisplayName => "Color";
        public AtlasFeatureSection Section => AtlasFeatureSection.Surface;
        public int Order => 0;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        // Every material has a base color.
        public bool IsPresent(AtlasUnifiedMaterial source) => source != null;

        // The map holds each source's base color stack (AtlasColorStack). When every source has the
        // same color and nothing is layered on it, that color stays shared (also above 1, which
        // the texture couldn't hold); otherwise each color is multiplied into its texture, and the
        // shared color is white.
        public void Resolve(AtlasFeatureContext context)
        {
            var materials = context.Sources.Select(i => context.Job.Sources[i].Material).ToList();
            var first = materials[0].Surface.BaseColor.Color;
            bool shared = materials.All(m => Same(m.Surface.BaseColor.Color, first) && !AtlasColorStack.HasStack(m));
            var baseColor = context.Output.Surface.BaseColor;
            baseColor.Color = shared ? new Color(first.r, first.g, first.b, 1f) : Color.white;
            var reference = context.RequestMap(AtlasVirtualMap.BaseColor, AtlasTextureInterpretation.Color);
            context.Maps[context.Maps.Count - 1].Evaluate = (index, material, width, height) =>
                AtlasColorStack.Evaluate(material, width, height, multiplyColor: !shared);
            baseColor.Map = reference;
            foreach (var note in materials.SelectMany(AtlasColorStack.LeftOut).Distinct()) context.Notes.Add(note);
        }

        private static bool Same(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 1e-4f && Mathf.Abs(a.g - b.g) < 1e-4f && Mathf.Abs(a.b - b.b) < 1e-4f;
    }
}
