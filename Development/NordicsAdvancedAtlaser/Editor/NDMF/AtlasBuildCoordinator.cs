using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    internal static class AtlasBuildCoordinator
    {
        internal static void Execute(GameObject avatarRoot, AtlasOriginalGeometry original, bool captureCompleted = true)
        {
            if (avatarRoot == null) return;
            using(var context=new AtlasBuildContext(avatarRoot))
            {
                if (!captureCompleted)
                    context.Diagnostics.Add("Early Resolving geometry capture pass did not complete; flat normal fallback may be used.");
                else if (original == null)
                    context.Diagnostics.Add("Early capture completed but geometry snapshot is null.");
                else if (original.CapturedSectionCount == 0)
                    context.Diagnostics.Add("Early capture produced zero geometry sections. Check the early capture logs for discovery and material matching details.");
                var atlasers=avatarRoot.GetComponentsInChildren<NordicsAdvancedAtlaser>(true);
                for(int atlaserIndex=0;atlaserIndex<atlasers.Length;atlaserIndex++)
                    PrepareGroup(context,atlasers[atlaserIndex],atlaserIndex,original);
                AtlasBuildReporting.Publish(context, original);
                    // All prepared generators have run; no output materials/meshes are created yet.
                AtlasBuildCleanup.RemoveBuildComponents(avatarRoot);
            }
        }

        private static void PrepareGroup(AtlasBuildContext context,NordicsAdvancedAtlaser component,int atlaserIndex, AtlasOriginalGeometry original)
        {
            var group=new AtlasBuildGroup { Component=component };
            context.Groups.Add(group);
            // After AAO, Merge Skinned Mesh may already be consumed and its renderer destroyed.
            // Inspect the actual current avatar renderers rather than the prebuild AAO component.
            var renderers=context.AvatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r=>r!=null).ToList();
            group.Renderers.AddRange(renderers);
            var currentlyPresent=new HashSet<Material>();
            foreach(var r in renderers)
                foreach(var m in r.sharedMaterials)
                    if(m!=null) currentlyPresent.Add(m);
            group.DiscoveredMaterials.AddRange(currentlyPresent);

            var sources=new Dictionary<Material,AtlasInputMaterialJob>();
            var assigned=new HashSet<Material>();
            var outputs=component.GetComponents<NordicsAtlasOutput>();
            for(int outputIndex=0;outputIndex<outputs.Length;outputIndex++)
            {
                var output=outputs[outputIndex];
                var config=output.configuration;
                var job=new AtlasOutputJob {
                    Component=output, Configuration=config, Shader=AtlasShaderRegistry.Output(config.shaderId) };
                if(job.Shader==null)
                    context.Diagnostics.Add(output.name+": unregistered output shader "+config.shaderId);
                else if(!AtlasShaderRegistry.IsInstalled(job.Shader))
                {
                    // The new materials are made with the output shader, so it must be installed.
                    context.Diagnostics.Add(output.name+": "+job.Shader.DisplayName+" isn't installed; this output is skipped.");
                    job.Shader=null;
                }
                group.Outputs.Add(job);
                if(config.sources==null) continue;
                for(int sourceIndex=0;sourceIndex<config.sources.Count;sourceIndex++)
                {
                    var source=config.sources[sourceIndex];
                    if(source==null||source.material==null) continue;
                    if(!assigned.Add(source.material))
                    throw new InvalidOperationException("[Nordic's Advanced Atlaser] "+config.outputName+
                        ": material '"+source.material.name+"' is assigned more than once across outputs.");
                    AtlasInputMaterialJob input;
                    if(!sources.TryGetValue(source.material,out input))
                    {
                        input=new AtlasInputMaterialJob { SourceMaterial=source.material,
                            SourceShader=source.material.shader,
                            Parser=AtlasShaderRegistry.Detect(source.material) };
                        sources.Add(source.material,input);
                        group.Inputs.Add(input);
                        if(!currentlyPresent.Contains(source.material))
                            context.Diagnostics.Add(source.material.name+": no current renderer uses this material; an empty bake will be supplied.");
                    }
                    job.Sources.Add(new AtlasOutputSourceJob { Input=input,UVRegion=source.uvRegion,
                        SourceKey=AtlasOriginalGeometry.SourceKey(atlaserIndex,outputIndex,sourceIndex),
                        NormalCorrection=source.normalCorrection, PrebakedNormalMap=source.prebakedNormalMap });
                }
            }
            // Normalize each unique source material once, irrespective of how many outputs use it.
            foreach(var input in group.Inputs)
            {
                AtlasMaterialNormalizer.Prepare(input);
                if (input.Unified == null) continue;
                // Keep the issues visible to the author rather than silently treating
                // unresolved source effects as exact shader-independent features.
                foreach (var issue in input.Unified.Issues)
                    if (issue.IsWarning)
                        context.Diagnostics.Add(input.SourceMaterial.name + " [" + issue.Feature + "]: " + issue.Message);
            }

            // Validate *before* allocating any texture buffers, even when baking is disabled.
            // Only configured source materials are considered. Unrelated materials are ignored.
            foreach(var input in group.Inputs)
            {
                int surviving=AtlasSubmeshBaker.CountCurrent(input.SourceMaterial,renderers);
                if(surviving>1)
                    throw new InvalidOperationException("[Nordic's Advanced Atlaser] '"+input.SourceMaterial.name+
                        "' occurs on "+surviving+" surviving submeshes. Exactly one is required after merging.");
            }
            // Each source is baked at a size derived from its region in its atlas (see
            // AtlasOutputLayout.BakeSize). When disabled, no geometry textures are allocated; null
            // Bakes means unavailable, not an enabled bake whose submesh has been removed.
            if(component.enableGeometryBaking)
            {
                foreach(var job in group.Outputs)
                {
                    if(job.Shader==null) continue;
                    foreach(var source in job.Sources)
                    {
                        var size=AtlasOutputLayout.BakeSize(source.UVRegion,job.Configuration.mainTexture);
                        string key=size.x+"x"+size.y;
                        AtlasBakedInputSet baked;
                        if(!source.Input.Bakes.TryGetValue(key,out baked))
                        {
                            List<AtlasMeshSection> originalSections=null;
                            if(original!=null && !original.SourceSections.TryGetValue(source.SourceKey,out originalSections))
                            {
                                // Reference lookup is only a compatibility fallback when the
                                // source slots were changed by another build processor.
                                original.Sections.TryGetValue(source.Input.SourceMaterial,out originalSections);
                                context.Diagnostics.Add(source.Input.SourceMaterial.name+
                                    ": source slot '"+source.SourceKey+"' was not captured; material-key fallback " +
                                    (originalSections==null?"failed.":"succeeded."));
                            }
                            if(originalSections!=null)
                                context.Diagnostics.Add(source.Input.SourceMaterial.name+": source slot '"+
                                    source.SourceKey+"' resolved "+originalSections.Count+" original section(s).");
                            string debugFolder=null;
                            if(AtlasDebugSettings.ExportIntermediateMaps)
                            {
                                try { debugFolder=AtlasDebugExporter.DirectoryFor(component.name,
                                    source.Input.SourceMaterial.name,source.SourceKey); }
                                catch(Exception e) { context.Diagnostics.Add("Debug directory creation failed: "+e.Message); }
                            }
                            baked=AtlasSubmeshBaker.Bake(source.Input.SourceMaterial,size.x,size.y,
                                originalSections,renderers,context.AvatarRoot.transform,context.Diagnostics,debugFolder,
                                source.NormalCorrection);
                            source.Input.Bakes.Add(key,baked);
                        }
                        source.Bakes=baked;
                    }
                }
            }
            foreach(var job in group.Outputs) AtlasOutputProcessor.Prepare(job,context);
        }
    }
}
