using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Normal maps (ATLAS_OUTPUT.md, "Feature inventory"): each source's normals combined into the
    // main normal map, in the established order: the pre-baked normal, the build's normal
    // correction, the primary normal map, then the surface layers targeting the normal, each by
    // whiteout. The output's strength is the largest among the sources, and each one is baked at
    // its share of it, so the writer's one strength reproduces every source's. Sources without
    // normals are flat.
    public sealed class AtlasNormalFeature : IAtlasSourceAwareFeature
    {
        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Normal map", Kind = AtlasFeatureModeKind.Native,
                RequiredMaps = new[] { AtlasVirtualMap.Normal }
            },
            AtlasFeatureMode.Off
        };

        public string Id => "normal";
        public string DisplayName => "Normal Maps";
        public AtlasFeatureSection Section => AtlasFeatureSection.Surface;
        public int Order => 2;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        public bool IsPresent(AtlasUnifiedMaterial source) =>
            source != null && (source.Surface.Normal.Map?.Texture != null || NormalLayers(source).Any());

        // A geometry-bake correction counts only where it bends the normal: a flat one (the mesh
        // wasn't reduced) adds nothing. Before the build there is none, so the inspector lists
        // the sources with a normal map or a pre-baked one.
        public bool IsPresent(AtlasGeneratorSource source) =>
            source != null && (IsPresent(source.Material) || source.PrebakedNormalMap != null || AtlasMapEvaluator.Correction(source) != null);

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var active = new HashSet<int>(context.Sources);
            float strength = 0f;
            foreach (int i in active) strength = Mathf.Max(strength, LargestStrength(job.Sources[i]));
            if (strength <= 0f) return;
            var normal = context.Output.Surface.Normal;
            normal.Strength = strength;
            var request = new AtlasMapRequest
            {
                Map = AtlasVirtualMap.Normal,
                Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.TangentNormal },
                Evaluate = (index, material, width, height) => active.Contains(index)
                    ? AtlasMapEvaluator.Normal(job.Sources[index], width, height, strength)
                    : AtlasMapEvaluator.Flat(width, height)
            };
            normal.Map = request.Reference;
            // Every source, so the ones without normals are flat rather than filled from a neighbor.
            for (int i = 0; i < job.Sources.Count; i++) request.Sources.Add(i);
            context.Maps.Add(request);

            var materials = active.Select(i => job.Sources[i].Material).ToList();
            if (materials.Any(m => NormalLayers(m).Any(l => !AtlasMapEvaluator.CanBakeNormalLayer(l))))
                context.Notes.Add("A normal layer that is placed, masked by the mask bus or vertex colors, on another UV, or lerped isn't baked yet; it's left out.");
        }

        // The surface layers that change the normal.
        public static IEnumerable<AtlasSurfaceLayer> NormalLayers(AtlasUnifiedMaterial source) =>
            source == null ? Enumerable.Empty<AtlasSurfaceLayer>()
                : source.Layers.OfType<AtlasSurfaceLayer>().Where(l => l.Enabled && l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Normal));

        // The largest strength a source's normals are combined at: the pre-baked map and the
        // correction at 1, the primary normal and each baked layer at their own.
        public static float LargestStrength(AtlasGeneratorSource source)
        {
            float strength = 0f;
            if (source.PrebakedNormalMap != null) strength = 1f;
            if (AtlasMapEvaluator.Correction(source) != null) strength = 1f;
            var material = source.Material;
            if (material?.Surface.Normal.Map?.Texture != null) strength = Mathf.Max(strength, material.Surface.Normal.Strength);
            foreach (var layer in NormalLayers(material).Where(AtlasMapEvaluator.CanBakeNormalLayer))
                foreach (var target in layer.Targets.Where(t => t.Target == AtlasSurfaceTarget.Normal))
                    strength = Mathf.Max(strength, target.Opacity);
            return strength;
        }
    }
}
