using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    public static class AtlasOutputProcessor
    {
        // Builds the generator job (no Unity material or source shader crosses it) and resolves
        // the output into its plan. Baking, packing and writing follow from the plan.
        public static void Prepare(AtlasOutputJob job, AtlasBuildContext context)
        {
            if (job == null || job.Shader == null || job.Configuration == null) return;
            var generatorJob = CreateJob(job.Configuration, job, context.AvatarRoot.GetComponentsInChildren<Renderer>(true));
            job.GeneratorJob = generatorJob;
            job.Plan = AtlasOutputPipeline.Resolve(generatorJob, job.Shader.Capabilities);
            foreach (var note in job.Plan.Notes)
                context.Diagnostics.Add(job.Configuration.outputName + ": " + note);
        }

        public static AtlasGeneratorJob CreateJob(AtlasOutputConfiguration config, AtlasOutputJob job, IEnumerable<Renderer> renderers)
        {
            var generatorJob = new AtlasGeneratorJob
            {
                OutputName = config.outputName,
                OutputShaderId = config.shaderId,
                RegionPadding = AtlasOutputLayout.EffectivePadding(config),
                NormalBlock = AtlasTextureMemory.NormalBlock(config),
                MainTexture = config.mainTexture == null ? null : new AtlasTextureSize
                {
                    width = config.mainTexture.width, height = config.mainTexture.height
                }
            };
            var materials = new List<Material>();
            foreach (var source in job.Sources)
            {
                if (source.Input == null || source.Input.Unified == null) continue;
                materials.Add(source.Input.SourceMaterial);
                var generatorSource = new AtlasGeneratorSource
                {
                    Material = source.Input.Unified, UVRegion = source.UVRegion,
                    GeometryBakingAvailable = source.Bakes != null, Bakes = source.Bakes,
                    PrebakedNormalMap = source.PrebakedNormalMap
                };
                generatorSource.Coverage.AddRange(AtlasCoverage.Triangles(source.Input.SourceMaterial, renderers));
                generatorJob.Sources.Add(generatorSource);
            }
            AtlasOutputPipeline.AddChoices(config, materials, generatorJob);
            return generatorJob;
        }
    }
}
