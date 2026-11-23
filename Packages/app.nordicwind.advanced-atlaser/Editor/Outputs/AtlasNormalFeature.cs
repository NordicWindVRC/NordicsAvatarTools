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
    // Baked into color: no normal map; the shading it would add under the virtual light rig is
    // baked into the base color (AtlasNormalShading), for outputs without normal maps or to save
    // the texture's memory.
    public sealed class AtlasNormalFeature : IAtlasSourceAwareFeature, IAtlasLitBakeFeature
    {
        public static readonly AtlasFeatureMode Baked = new AtlasFeatureMode
        {
            Id = "baked", Label = "Baked into color", Kind = AtlasFeatureModeKind.Baked,
            RequiredMaps = new[] { AtlasVirtualMap.BaseColor }
        };

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Normal map", Kind = AtlasFeatureModeKind.Native,
                RequiredMaps = new[] { AtlasVirtualMap.Normal }
            },
            Baked,
            AtlasFeatureMode.Off
        };

        public AtlasLitBake? LitBake(AtlasFeatureMode mode) => mode == Baked ? AtlasLitBake.NormalShading : (AtlasLitBake?)null;

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
            if (context.Mode == Baked)
            {
                BakeIntoColor(context, active);
                return;
            }
            float strength = 0f;
            foreach (int i in active) strength = Mathf.Max(strength, LargestStrength(job.Sources[i]));
            if (strength <= 0f) return;
            var normal = context.Output.Surface.Normal;
            normal.Strength = strength;
            var request = new AtlasMapRequest
            {
                Map = AtlasVirtualMap.Normal,
                Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.TangentNormal },
                Content = (index, material, width, height) => active.Contains(index)
                    ? AtlasMapEvaluator.Normal(job.Sources[index], width, height, strength)
                    : AtlasMapEvaluator.Flat(width, height),
                // Placed in the texture: corrections are smoothed to the size they're shown at.
                ContentPlaced = (index, material, width, height, placed) => active.Contains(index)
                    ? AtlasMapEvaluator.Normal(job.Sources[index], width, height, strength, placed, job.NormalBlock)
                    : AtlasMapEvaluator.Flat(width, height)
            };
            normal.Map = request.Reference;
            // Every source, so the ones without normals are flat rather than filled from a neighbor.
            for (int i = 0; i < job.Sources.Count; i++) request.Sources.Add(i);
            context.Maps.Add(request);

            var materials = active.Select(i => job.Sources[i].Material).ToList();
            if (materials.Any(m => NormalLayers(m).Any(l => !AtlasMapEvaluator.CanBakeNormalLayer(l, m))))
                context.Notes.Add("A normal layer that is placed, masked by a global mask written by layers or the second pass, on another UV, or lerped isn't baked yet; it's left out.");
        }

        // A contribution to the base color, after every other feature's (the shading stage): each
        // source's color × its normal-map shading under the rig, symmetric for sources whose UVs
        // overlap. The normals are evaluated at their own strengths, with the corrections faded at
        // the UV seams as in the normal map.
        private static void BakeIntoColor(AtlasFeatureContext context, HashSet<int> active)
        {
            var job = context.Job;
            var request = AtlasLiveMasks.Request(context, AtlasVirtualMap.BaseColor);
            if (request == null)
            {
                context.Notes.Add("Normal maps are baked into the base color, which is off; they're left out.");
                return;
            }
            var settings = job.Baking ?? new AtlasBakeSettings();
            float strength = Mathf.Clamp01(settings.strength) * Mathf.Clamp01(settings.normalShading);
            if (strength <= 0f) return;
            var unknown = active.Where(i => job.Sources[i].Surface == null).ToList();
            if (unknown.Count > 0)
                context.Notes.Add($"Normal-map shading needs the meshes, which aren't known here; it's left out on {AtlasLiveMasks.Numbers(unknown)}.");
            var symmetric = new HashSet<int>(active.Where(i => UsesSymmetricRig(settings, job.Sources[i])));
            var classicRig = AtlasLightRig.Create(settings, false);
            var symmetricRig = AtlasLightRig.Create(settings, true);
            var main = job.MainTexture;
            // Brighter than 1 where the detail faces the light more than the mesh does (the map's
            // range step).
            request.Add(new AtlasMapContribution
            {
                Feature = "normal", Label = "normal-map shading", Stage = AtlasMapStage.Shading, CanRaise = true,
                Applies = i => active.Contains(i) && job.Sources[i].Surface != null,
                Apply = content =>
                {
                    int i = content.Source;
                    var source = job.Sources[i];
                    var sections = source.Surface();
                    if (sections == null || sections.Count == 0) return;
                    // Placed in the main texture, the base color's, like the normal map would be.
                    Vector2Int? placed = main != null && main.width > 0 && main.height > 0
                        ? AtlasOutputLayout.IslandSize(source.UVRegion, main) : (Vector2Int?)null;
                    using (var normals = AtlasMapEvaluator.Normal(source, content.Width, content.Height, 1f, placed))
                        AtlasNormalShading.Apply(content.Map, normals, sections, symmetric.Contains(i) ? symmetricRig : classicRig, strength);
                }
            });
        }

        // Sources with overlapping UVs use the symmetric rig, so mirrored halves get the same
        // light (ATLAS_OUTPUT.md, "Overlapping UVs").
        public static bool UsesSymmetricRig(AtlasBakeSettings settings, AtlasGeneratorSource source) =>
            settings?.rig == AtlasLightRigKind.Symmetric || AtlasSurfaceGeometry.Overlaps(source?.Coverage);

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
            foreach (var layer in NormalLayers(material).Where(l => AtlasMapEvaluator.CanBakeNormalLayer(l, material)))
                foreach (var target in layer.Targets.Where(t => t.Target == AtlasSurfaceTarget.Normal))
                    strength = Mathf.Max(strength, target.Opacity);
            return strength;
        }
    }
}
