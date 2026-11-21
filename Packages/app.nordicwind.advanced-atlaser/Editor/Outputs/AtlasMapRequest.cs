using System;
using System.Collections.Generic;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Where a contribution runs among a map's contributions (ATLAS_OUTPUT.md, "Map
    // contributions"): in the order a shader computes them, whatever order the features were
    // resolved in, so each one sees what the stages before it made.
    public enum AtlasMapStage
    {
        // Layers on the surface: baked view layers (matcaps), coats, and the shifts for live ones.
        Layers,
        // Effects drawn over the surface (glitter points) and what is added after lighting.
        Overlays,
        // The light baked into the map: normal-map shading and ambient occlusion, which light
        // everything the stages before put there.
        Shading
    }

    // What a contribution does to the map as a picture of the surface.
    public enum AtlasContributionKind
    {
        // Puts a look into the map: the map still looks right on its own.
        Bake,
        // Moves the map away from the surface's own value so that a live layer brings it back:
        // right only together with that layer.
        Shift
    }

    // What a contribution works on: one source's content of the map, changed in place.
    public sealed class AtlasMapContent
    {
        // Index into AtlasGeneratorJob.Sources.
        public int Source;
        public AtlasUnifiedMaterial Material;
        public int Width, Height;
        // The size the source's content ends up at in the texture (its region, in pixels), when
        // the map is baked; null when it is only evaluated.
        public Vector2Int? Placed;
        // The map's value, the shared color included (AtlasMapRequest.SharedColor).
        public NordicsTextureWrapper Map;
    }

    // One feature's change to one map.
    public sealed class AtlasMapContribution
    {
        // The feature's ID and what the contribution is, for logs and timings.
        public string Feature;
        public string Label;
        public AtlasMapStage Stage;
        public AtlasContributionKind Kind;
        // It can raise values, so the map's range step is needed.
        public bool CanRaise;
        // The sources it changes; null: every source.
        public Func<int, bool> Applies;
        public Action<AtlasMapContent> Apply;
    }

    // A virtual map the output needs, the sources that contribute to it, and the texture
    // reference in the output unified material that points at it once it is packed. The map is
    // built per source (ATLAS_OUTPUT.md, "Map contributions"):
    // - its content, the map's own value, under the views added to it;
    // - the contributions of other features, in place, by stage;
    // - its range, once for the whole baked map (Finish).
    public sealed class AtlasMapRequest
    {
        public AtlasVirtualMap Map;
        public AtlasTextureReference Reference;
        // Indices into AtlasGeneratorJob.Sources.
        public readonly List<int> Sources = new List<int>();

        private Func<int, AtlasUnifiedMaterial, int, int, NordicsTextureWrapper> content;
        private readonly List<Func<int, AtlasUnifiedMaterial, IDisposable>> views = new List<Func<int, AtlasUnifiedMaterial, IDisposable>>();
        private readonly List<AtlasMapContribution> contributions = new List<AtlasMapContribution>();

        // The map's own value for one source (source index, its material, width, height), set
        // once by the feature that makes the map; null: AtlasMapEvaluator.Evaluate. Other
        // features add contributions.
        public Func<int, AtlasUnifiedMaterial, int, int, NordicsTextureWrapper> Content
        {
            get => content;
            set
            {
                if (content != null)
                    throw new InvalidOperationException($"The {Map} map's content is already set; a feature that changes the map adds a contribution.");
                content = value;
            }
        }

        // Optional: the content for the map baker, which also passes the size the source's
        // content ends up at in the texture, for evaluations that depend on it.
        public Func<int, AtlasUnifiedMaterial, int, int, Vector2Int, NordicsTextureWrapper> ContentPlaced;

        // The color the output multiplies the map by, which the content leaves out (the base
        // color's shared color). Contributions see the content × it, the surface's real value.
        public Color SharedColor = Color.white;

        // The content itself can go above 1 (colors multiplied into it).
        public bool ContentCanRaise;

        // Takes what the map can't hold: called with the baked map's brightest value, above 1,
        // once the map is divided by it. Null: the map has nothing to carry it and stays as it is.
        public Action<float> Carry;

        // The map is always divided by its brightest value, below 1 too, so a dim map uses the
        // texture's whole range (ATLAS_OUTPUT.md, "Small values need range"). The carrier is
        // called with 1 for a map that is black.
        public bool Stretch;

        public IReadOnlyList<AtlasMapContribution> Contributions => contributions;

        /// <summary>Adds a contribution after the ones of its stage and the stages before it.</summary>
        public void Add(AtlasMapContribution contribution)
        {
            int at = contributions.Count;
            while (at > 0 && contributions[at - 1].Stage > contribution.Stage) at--;
            contributions.Insert(at, contribution);
        }

        /// <summary>Adds a view the content is evaluated under: a scope opened around it for a
        /// source (null for none), such as the global masks with a source's live channels at 0.
        /// Contributions run outside it.</summary>
        public void AddView(Func<int, AtlasUnifiedMaterial, IDisposable> view) => views.Add(view);

        /// <summary>One source's map: its content under its views, then its contributions.</summary>
        public NordicsTextureWrapper Evaluate(int index, AtlasUnifiedMaterial material, int width, int height) =>
            Fold(index, material, width, height, null, views.Count, contributions);

        /// <summary>The same for the map baker, with the size the source's content ends up at.</summary>
        public NordicsTextureWrapper Evaluate(int index, AtlasUnifiedMaterial material, int width, int height, Vector2Int placed) =>
            Fold(index, material, width, height, placed, views.Count, contributions);

        /// <summary>The map as it is evaluated now: later views and contributions aren't part
        /// of it. For a feature that keeps what the map held before it changes it.</summary>
        public Func<int, AtlasUnifiedMaterial, int, int, NordicsTextureWrapper> Snapshot()
        {
            int count = views.Count;
            var edits = contributions.ToArray();
            return (index, material, width, height) => Fold(index, material, width, height, null, count, edits);
        }

        private NordicsTextureWrapper Fold(int index, AtlasUnifiedMaterial material, int width, int height, Vector2Int? placed, int viewCount,
            IReadOnlyList<AtlasMapContribution> edits)
        {
            var map = EvaluateContent(index, material, width, height, placed, viewCount);
            List<AtlasMapContribution> applying = null;
            foreach (var edit in edits)
            {
                if (edit.Applies != null && !edit.Applies(index)) continue;
                if (applying == null) applying = new List<AtlasMapContribution>();
                applying.Add(edit);
            }
            // Untouched sources keep their content exactly.
            if (applying == null) return map;
            try
            {
                bool tinted = Mathf.Abs(SharedColor.r - 1f) + Mathf.Abs(SharedColor.g - 1f) + Mathf.Abs(SharedColor.b - 1f) > 1e-6f;
                if (tinted) Scale(map, Factor(SharedColor.r), Factor(SharedColor.g), Factor(SharedColor.b));
                var target = new AtlasMapContent { Source = index, Material = material, Width = width, Height = height, Placed = placed, Map = map };
                foreach (var edit in applying) edit.Apply(target);
                if (tinted) Scale(map, 1f / Factor(SharedColor.r), 1f / Factor(SharedColor.g), 1f / Factor(SharedColor.b));
                return map;
            }
            catch
            {
                map.Dispose();
                throw;
            }
        }

        // A channel the shared color removes can't be divided back, so it stays as the content has it.
        private static float Factor(float shared) => shared > 1e-5f ? shared : 1f;

        private static void Scale(NordicsTextureWrapper map, float r, float g, float b) =>
            AtlasPixels.Map(map, c => new Color(c.r * r, c.g * g, c.b * b, c.a));

        // The content under the first `viewCount` views, the last one outermost.
        private NordicsTextureWrapper EvaluateContent(int index, AtlasUnifiedMaterial material, int width, int height, Vector2Int? placed, int viewCount)
        {
            if (viewCount > 0)
                using (views[viewCount - 1](index, material))
                    return EvaluateContent(index, material, width, height, placed, viewCount - 1);
            if (placed.HasValue && ContentPlaced != null) return ContentPlaced(index, material, width, height, placed.Value);
            if (content != null) return content(index, material, width, height);
            return AtlasMapEvaluator.Evaluate(Map, material, width, height);
        }

        /// <summary>The map's range step, on the whole baked map before it is packed: where the
        /// content or a contribution can go above 1, the map is divided by its brightest value,
        /// which the map's carrier takes (Carry). A stretched map is divided whatever its
        /// brightest value is.</summary>
        public void Finish(NordicsTextureWrapper map)
        {
            if (Carry == null || !(Stretch || ContentCanRaise || contributions.Exists(c => c.CanRaise))) return;
            float peak = AtlasColorFeature.Peak(map);
            if (Stretch)
            {
                if (peak <= 1e-4f)
                {
                    Carry(1f);
                    return;
                }
                Carry(peak);
                if (Mathf.Abs(peak - 1f) < 1e-4f) return;
            }
            else
            {
                if (peak <= 1f) return;
                Carry(peak);
            }
            map.ApplyColor(new ColorOperation
            {
                Opcode = ColorOperationCode.Tint, Parameters = { Color = new Color(1f / peak, 1f / peak, 1f / peak, 1f) }
            });
        }
    }
}
