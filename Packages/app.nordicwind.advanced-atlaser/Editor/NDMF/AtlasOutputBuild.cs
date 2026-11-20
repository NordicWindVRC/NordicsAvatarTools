using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // The build's last step: every prepared output's material and textures, and the avatar's
    // meshes remapped onto them. Everything is made before any renderer changes, so a failure
    // leaves the avatar as it was.
    internal static class AtlasOutputBuild
    {
        internal static void Apply(AtlasBuildContext context, BuildContext build)
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            bool mobile = target == BuildTarget.Android || target == BuildTarget.iOS;
            var outputs = new List<AtlasGeneratedOutput>();
            var targets = new Dictionary<Material, AtlasRemapTarget>();
            var remaps = new List<(Renderer Renderer, Mesh Mesh, AtlasRemapResult Result)>();
            try
            {
                foreach (var group in context.Groups)
                    foreach (var job in group.Outputs)
                    {
                        if (job.Shader == null || job.Plan == null || job.GeneratorJob == null || job.GeneratorJob.Sources.Count == 0) continue;
                        AtlasGeneratedOutput output;
                        using (context.Time("output: " + job.Configuration.outputName))
                            output = AtlasOutputGenerator.Generate(job.GeneratorJob, job.Plan, job.Configuration, job.Shader, mobile);
                        outputs.Add(output);
                        context.Diagnostics.AddRange(output.Timings);
                        foreach (var note in output.Notes) context.Diagnostics.Add(job.Configuration.outputName + ": " + note);
                        // The generator job's sources are these, in this order (AtlasOutputProcessor).
                        int index = 0;
                        foreach (var source in job.Sources)
                            if (source.Input?.Unified != null)
                            {
                                job.Plan.VertexColors.TryGetValue(index, out var vertexColor);
                                var remapTarget = new AtlasRemapTarget
                                {
                                    // Into the part the content fills, inside the padding.
                                    Region = AtlasOutputLayout.ContentRegion(source.UVRegion, job.Configuration.mainTexture,
                                        AtlasOutputLayout.EffectivePadding(job.Configuration)),
                                    Output = output.Material,
                                    VertexColor = vertexColor
                                };
                                if (job.Plan.SourceRemaps.TryGetValue(index, out var remap))
                                {
                                    remapTarget.PreservedUVs.AddRange(remap.PreservedUVs);
                                    remapTarget.RemovedTiles.AddRange(remap.RemovedTiles);
                                    remapTarget.RemoveBlockerChannel = remap.RemoveBlockerChannel;
                                    remapTarget.KeepBlockerTile = remap.KeepBlockerTile;
                                }
                                targets[source.Input.SourceMaterial] = remapTarget;
                                index++;
                            }
                        context.Diagnostics.Add($"{job.Configuration.outputName}: {output.Textures.Count} texture(s) for {job.Shader.DisplayName}, {(mobile ? "mobile" : "PC")} formats.");
                    }
                if (targets.Count > 0)
                    foreach (var renderer in context.AvatarRoot.GetComponentsInChildren<Renderer>(true))
                    {
                        var mesh = MeshOf(renderer);
                        AtlasRemapResult result;
                        using (context.Time("remap: " + renderer.name))
                            result = AtlasMeshRemapper.Remap(mesh, renderer.sharedMaterials, targets);
                        if (result != null) remaps.Add((renderer, mesh, result));
                    }
            }
            catch
            {
                foreach (var output in outputs)
                {
                    foreach (var texture in output.Textures) Object.DestroyImmediate(texture);
                    Object.DestroyImmediate(output.Material);
                }
                foreach (var remap in remaps) Object.DestroyImmediate(remap.Result.Mesh);
                throw;
            }

            foreach (var output in outputs)
            {
                foreach (var texture in output.Textures) build.AssetSaver.SaveAsset(texture);
                build.AssetSaver.SaveAsset(output.Material);
            }
            foreach (var (renderer, mesh, result) in remaps)
            {
                build.AssetSaver.SaveAsset(result.Mesh);
                ObjectRegistry.RegisterReplacedObject(mesh, result.Mesh);
                if (renderer is SkinnedMeshRenderer skinned) skinned.sharedMesh = result.Mesh;
                else renderer.GetComponent<MeshFilter>().sharedMesh = result.Mesh;
                renderer.sharedMaterials = result.Materials;
                context.Diagnostics.Add($"{renderer.name}: remapped onto the atlas; {result.SplitVertices} vertices split.");
                foreach (var clamped in result.Clamped)
                    context.Diagnostics.Add($"{clamped.Key.name}: {clamped.Value} triangle(s) straddle a UV tile edge and were clamped into the atlas region.");
                foreach (var removed in result.Removed)
                    context.Diagnostics.Add($"{removed.Key.name}: {removed.Value} triangle(s) in hidden UV tiles removed from the mesh.");
                foreach (var removed in result.RemovedBlockers)
                    context.Diagnostics.Add($"{removed.Key.name}: {removed.Value} world AO blocker triangle(s) removed from the mesh.");
            }
        }

        private static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            return filter != null ? filter.sharedMesh : null;
        }
    }
}
