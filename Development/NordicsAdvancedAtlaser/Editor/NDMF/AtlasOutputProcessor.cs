using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    public static class AtlasOutputProcessor
    {
        public static void Prepare(AtlasOutputJob job, AtlasBuildContext context)
        {
            if (job == null || job.Shader == null || job.Configuration == null) return;
            AtlasShaderRegistry.EnsureSettings(job.Configuration, job.Shader);
            var config=job.Configuration;
            var generatorJob=new AtlasGeneratorJob {
                OutputName=config.outputName, OutputShaderId=config.shaderId,
                RegionPadding=AtlasOutputLayout.EffectivePadding(config),
                MainTexture=config.mainTexture==null?null:new AtlasTextureSize {
                    width=config.mainTexture.width,height=config.mainTexture.height
                }
            };
            if (config.settings!=null)
                foreach(var setting in config.settings)
                {
                    if (setting==null) continue;
                    generatorJob.Settings.Add(new AtlasOutputSetting {
                        id=setting.id,kind=setting.kind,boolValue=setting.boolValue,
                        floatValue=setting.floatValue,intValue=setting.intValue,
                        colorValue=setting.colorValue,vectorValue=setting.vectorValue,
                        textValue=setting.textValue,sizeMode=setting.sizeMode,
                        customAspectWidth=setting.customAspectWidth,customAspectHeight=setting.customAspectHeight,
                        texture=setting.texture==null?null:new AtlasTextureDimensions {
                            enabled=setting.texture.enabled, width=setting.texture.width, height=setting.texture.height
                        }
                    });
                }
            foreach(var source in job.Sources)
            {
                if (source.Input == null || source.Input.Unified == null) continue;
                generatorJob.Sources.Add(new AtlasGeneratorSource {
                    Material=source.Input.Unified, UVRegion=source.UVRegion,
                    GeometryBakingAvailable=source.Bakes!=null, Bakes=source.Bakes,
                    PrebakedNormalMap=source.PrebakedNormalMap
                });
            }
            // No Unity material or source shader information is passed across this boundary.
            // Implementations do not generate output materials in this revision.
            job.Shader.Prepare(generatorJob);
        }
    }
}
