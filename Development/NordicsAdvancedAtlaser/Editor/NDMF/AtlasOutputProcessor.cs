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
                OutputName=config.OutputName, OutputShaderId=config.ShaderId,
                RegionPadding=config.RegionPadding,
                MainTexture=config.MainTexture==null?null:new AtlasTextureDimensions {
                    Enabled=config.MainTexture.Enabled,Width=config.MainTexture.Width,Height=config.MainTexture.Height
                }
            };
            if (config.Settings!=null)
                foreach(var setting in config.Settings)
                {
                    if (setting==null) continue;
                    generatorJob.Settings.Add(new AtlasOutputSetting {
                        Id=setting.Id,Kind=setting.Kind,BoolValue=setting.BoolValue,
                        FloatValue=setting.FloatValue,IntValue=setting.IntValue,
                        ColorValue=setting.ColorValue,VectorValue=setting.VectorValue,
                        TextValue=setting.TextValue,SizeMode=setting.SizeMode,
                        CustomAspectWidth=setting.CustomAspectWidth,CustomAspectHeight=setting.CustomAspectHeight,
                        Texture=setting.Texture==null?null:new AtlasTextureDimensions {
                            Enabled=setting.Texture.Enabled, Width=setting.Texture.Width, Height=setting.Texture.Height
                        }
                    });
                }
            foreach(var source in job.Sources)
            {
                if (source.Input == null || source.Input.Unified == null) continue;
                generatorJob.Sources.Add(new AtlasGeneratorSource {
                    Material=source.Input.Unified, UVRegion=source.UVRegion,
                    GeometryBakingAvailable=source.Bakes!=null, Bakes=source.Bakes
                });
            }
            // No Unity material or source shader information is passed across this boundary.
            // Implementations do not generate output materials in this revision.
            job.Shader.Prepare(generatorJob);
        }
    }
}
