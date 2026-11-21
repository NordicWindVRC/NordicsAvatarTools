using System;
using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // What the build makes for one output: its material and textures, in memory.
    public sealed class AtlasGeneratedOutput
    {
        public Material Material;
        public readonly List<Texture2D> Textures = new List<Texture2D>();
        // "[Time] …" lines for each stage, for the build's diagnostics.
        public readonly List<string> Timings = new List<string>();
        // What the written material doesn't hold as the plan has it, for the diagnostics.
        public readonly List<string> Notes = new List<string>();
    }

    // Steps 3 to 5 of the pipeline for the build: each texture packed and compressed for the
    // platform being built, then the material written. The manual bake writes the same textures
    // as PNG assets instead.
    public static class AtlasOutputGenerator
    {
        public static AtlasGeneratedOutput Generate(AtlasGeneratorJob job, AtlasOutputPlan plan, AtlasOutputConfiguration config,
            IAtlasOutputShader shader, bool mobile)
        {
            var unityShader = shader.FindShader();
            if (unityShader == null)
                throw new InvalidOperationException($"[Nordic's Advanced Atlaser] {config.outputName}: the output shader '{shader.DisplayName}' isn't installed.");
            var maps = plan.Maps.Select(r => r.Map).Distinct().ToList();
            var uses = AtlasOutputOverview.TextureUses(config, shader.Capabilities, maps, Array.Empty<AtlasVirtualMap>());
            var result = new AtlasGeneratedOutput();
            var enabled = uses.Where(u => u.Enabled).ToList();
            try
            {
                using (AtlasProgress.Begin(config.outputName, enabled.Count))
                {
                    for (int t = 0; t < enabled.Count; t++)
                    {
                        var use = enabled[t];
                        AtlasProgress.Texture(t, use.Texture.Label);
                        Texture2D texture;
                        string label = $"{config.outputName} {use.Texture.Id} ({use.Width}×{use.Height})";
                        var pack = new AtlasStageTimer("pack " + label, result.Timings);
                        using (var packed = AtlasTexturePacker.Pack(use, plan, job, result.Timings))
                        {
                            pack.Dispose();
                            AtlasProgress.Step("compressing");
                            using (new AtlasStageTimer("compress " + label, result.Timings))
                                texture = Compress(packed, use, config, mobile, $"{config.outputName}_{use.Texture.Id}");
                        }
                        result.Textures.Add(texture);
                        foreach (var request in plan.Maps)
                            if (use.Maps.Contains(request.Map)) request.Reference.Texture = texture;
                    }
                }
                // Generated textures, compressed like the atlas maps with the formats for their kind.
                foreach (var generated in plan.GeneratedTextures)
                {
                    Texture2D texture;
                    using (var wrapper = AtlasGeneratedTextures.Wrapper(generated))
                        texture = Compress(wrapper, AtlasGeneratedTextures.Use(generated, config), config, mobile, $"{config.outputName}_{generated.Name}");
                    result.Textures.Add(texture);
                    generated.Reference.Texture = texture;
                }
                if (AtlasLightingFeature.NeedsGeneratedRamp(plan.Output, shader.Capabilities))
                {
                    var ramp = AtlasRampTexture.Make(plan.Output.Lighting.Ramp.Keys, config.outputName + "_ShadowRamp");
                    result.Textures.Add(ramp);
                    plan.Output.Lighting.Ramp.Ramp = new AtlasTextureReference { Texture = ramp };
                }
                foreach (var step in plan.BeforeWrite) step();
                result.Material = new Material(unityShader) { name = config.outputName };
                shader.Write(plan.Output, result.Material);
                result.Notes.AddRange(AtlasKeptMaterial.Notes(job, plan.Output, result.Material));
                return result;
            }
            catch
            {
                foreach (var texture in result.Textures) UnityEngine.Object.DestroyImmediate(texture);
                if (result.Material != null) UnityEngine.Object.DestroyImmediate(result.Material);
                throw;
            }
            finally
            {
                // The bake's reused buffers aren't needed until the next one.
                NordicsTextureWrapper.ReleasePooledBuffers();
                AtlasPixels.Release();
            }
        }

        // The packed texture in the platform's format, with Kaiser and streaming mipmaps, without a
        // CPU copy. Crunch, where the format has a crunched form, is kept only when it saves 10%.
        public static Texture2D Compress(NordicsTextureWrapper packed, AtlasTextureUse use, AtlasOutputConfiguration config, bool mobile, string name)
        {
            var format = AtlasTextureMemory.Format(mobile ? config.androidFormats : config.pcFormats, use.Kind);
            string platform = mobile ? MobilePlatform() : "Standalone";
            if (!mobile && config.crunch && AtlasTextureExport.Crunched(format) != null)
            {
                var crunched = AtlasTextureExport.Generate(packed, use.Alpha, AtlasTextureExport.ImporterFormat(format, true), platform, true, name);
                long plain = AtlasTextureMemory.Bytes(packed.Width, packed.Height, format, packed.Settings.Mipmaps);
                if (AtlasTextureExport.KeepCrunch(crunched.GetRawTextureData().LongLength, plain))
                {
                    crunched.Apply(false, true);
                    return crunched;
                }
                UnityEngine.Object.DestroyImmediate(crunched);
            }
            return AtlasTextureExport.Generate(packed, use.Alpha, AtlasTextureExport.ImporterFormat(format, false), platform, false, name);
        }

        // The importer's platform name for the mobile platform being built.
        private static string MobilePlatform() =>
            EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS ? "iPhone" : "Android";
    }
}
