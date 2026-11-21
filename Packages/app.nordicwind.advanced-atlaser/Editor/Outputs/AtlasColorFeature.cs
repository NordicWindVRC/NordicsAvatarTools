using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
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
        // shared color is white. Where the baked map goes above 1, it is divided by its brightest
        // value, which becomes the shared color, so nothing is cut off at 1.
        public void Resolve(AtlasFeatureContext context)
        {
            var materials = context.Sources.Select(i => context.Job.Sources[i].Material).ToList();
            var first = materials[0].Surface.BaseColor.Color;
            bool shared = materials.All(m => Same(m.Surface.BaseColor.Color, first) && !AtlasColorStack.HasStack(m));
            var baseColor = context.Output.Surface.BaseColor;
            baseColor.Color = shared ? new Color(first.r, first.g, first.b, 1f) : Color.white;
            var reference = context.RequestMap(AtlasVirtualMap.BaseColor, AtlasTextureInterpretation.Color);
            var request = context.Maps[context.Maps.Count - 1];
            request.Content = (index, material, width, height) =>
                AtlasColorStack.Evaluate(material, width, height, multiplyColor: !shared);
            // The color the map leaves out, which contributions work with (AtlasMapRequest), and
            // which takes the map's brightest value where it goes above 1.
            var tint = baseColor.Color;
            request.SharedColor = tint;
            request.ContentCanRaise = !shared;
            request.Carry = peak => baseColor.Color = new Color(tint.r * peak, tint.g * peak, tint.b * peak, tint.a);
            baseColor.Map = reference;
            foreach (var note in materials.SelectMany(AtlasColorStack.LeftOut).Distinct()) context.Notes.Add(note);
        }

        // The brightest color channel in the map.
        public static float Peak(NordicsTextureWrapper map) =>
            Mathf.Max(0f, AtlasPixels.Max(map, c => Mathf.Max(c.r, Mathf.Max(c.g, c.b))));

        private static bool Same(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 1e-4f && Mathf.Abs(a.g - b.g) < 1e-4f && Mathf.Abs(a.b - b.b) < 1e-4f;
    }
}
