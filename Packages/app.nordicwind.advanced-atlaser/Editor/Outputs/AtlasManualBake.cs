using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Bake to Assets (ATLAS_OUTPUT.md, "Manual bake"): the output pipeline in edit mode, on the
    // source meshes as they are, with its textures and material written as assets. The build
    // runs the same steps on the reduced meshes.
    public static class AtlasManualBake
    {
        // `<folder>/<material>_<texture>.png`, for example `Assets/Atlas/Body_BaseColor.png`.
        public static string TextureName(string material, AtlasOutputTexture texture) => $"{material}_{texture.Id}";

        // Writes the textures and the material at `materialPath` (an `.mat` inside Assets).
        // Textures of the same names are overwritten; the caller asks before replacing a material.
        public static Material Bake(NordicsAtlasOutput output, string materialPath)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            var config = output.configuration;
            var shader = AtlasShaderRegistry.Output(config.shaderId);
            var unityShader = shader?.FindShader();
            if (unityShader == null)
                throw new InvalidOperationException(string.IsNullOrEmpty(config.shaderId)
                    ? $"[Nordic's Advanced Atlaser] {output.name}: no output shader is chosen."
                    : $"[Nordic's Advanced Atlaser] {output.name}: the output shader '{config.shaderId}' isn't installed.");
            string folder = Path.GetDirectoryName(materialPath)?.Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(materialPath);
            if (string.IsNullOrEmpty(folder) || !(folder == "Assets" || folder.StartsWith("Assets/", StringComparison.Ordinal)))
                throw new ArgumentException($"[Nordic's Advanced Atlaser] The material must be saved inside Assets: {materialPath}", nameof(materialPath));

            var timings = new List<string>();
            var total = new AtlasStageTimer("total", timings);
            AtlasGeneratorJob job;
            using (new AtlasStageTimer("parse", timings))
                job = Job(config, AtlasMaterialDiscovery.Discover(output.gameObject).Renderers,
                    AtlasSurfaceGeometry.AvatarRoot(output.transform));
            if (job.Sources.Count == 0)
                throw new InvalidOperationException($"[Nordic's Advanced Atlaser] {output.name}: no source material can be read.");
            var plan = AtlasOutputPipeline.Resolve(job, shader.Capabilities);
            var maps = plan.Maps.Select(r => r.Map).Distinct().ToList();
            var uses = AtlasOutputOverview.TextureUses(config, shader.Capabilities, maps, Array.Empty<AtlasVirtualMap>());
            var enabled = uses.Where(u => u.Enabled).ToList();
            try
            {
                using (AtlasProgress.Begin(name, enabled.Count))
                for (int i = 0; i < enabled.Count; i++)
                {
                    var use = enabled[i];
                    AtlasProgress.Texture(i, use.Texture.Label);
                    Texture2D texture;
                    string label = $"{use.Texture.Id} ({use.Width}×{use.Height})";
                    var pack = new AtlasStageTimer("pack " + label, timings);
                    using (var packed = AtlasTexturePacker.Pack(use, plan, job, timings))
                    {
                        pack.Dispose();
                        using (new AtlasStageTimer("export " + label, timings))
                            texture = AtlasTextureExport.Png(packed, folder, TextureName(name, use.Texture), use, config);
                    }
                    foreach (var request in plan.Maps)
                        if (use.Maps.Contains(request.Map)) request.Reference.Texture = texture;
                }
            }
            finally
            {
                NordicsTextureWrapper.ReleasePooledBuffers();
                AtlasPixels.Release();
            }

            foreach (var generated in plan.GeneratedTextures)
                using (var wrapper = AtlasGeneratedTextures.Wrapper(generated))
                    generated.Reference.Texture = AtlasTextureExport.Png(wrapper, folder, name + "_" + generated.Name,
                        AtlasGeneratedTextures.Use(generated, config), config);
            if (AtlasLightingFeature.NeedsGeneratedRamp(plan.Output, shader.Capabilities))
                plan.Output.Lighting.Ramp.Ramp = new AtlasTextureReference
                {
                    Texture = AtlasRampTexture.SavePng(plan.Output.Lighting.Ramp.Keys, folder, name + "_ShadowRamp")
                };
            foreach (var step in plan.BeforeWrite) step();
            var material = new Material(unityShader) { name = name };
            shader.Write(plan.Output, material);
            plan.Notes.AddRange(AtlasKeptMaterial.Notes(job, plan.Output, material));
            var saved = Save(material, materialPath);
            total.Dispose();
            Debug.Log($"[Nordic's Advanced Atlaser] Bake to Assets: {materialPath}. " + string.Join("; ", timings).Replace("[Time] ", "") +
                (plan.Notes.Count > 0 ? " Left out: " + string.Join(" ", plan.Notes) : ""), saved);
            return saved;
        }

        // The generator job for edit mode: every assigned source, parsed now, with its coverage
        // from the renderers, without geometry bakes (the build supplies those). With `root`, the
        // sources' surfaces are read from the renderers in its space for the lit bakes.
        public static AtlasGeneratorJob Job(AtlasOutputConfiguration config, IEnumerable<Renderer> renderers = null, Transform root = null)
        {
            var job = new AtlasGeneratorJob
            {
                OutputName = config.outputName, OutputShaderId = config.shaderId,
                RegionPadding = AtlasOutputLayout.EffectivePadding(config),
                NormalBlock = AtlasTextureMemory.NormalBlock(config),
                Baking = config.baking ?? new AtlasBakeSettings(),
                MainTexture = config.mainTexture == null ? null : new AtlasTextureSize { width = config.mainTexture.width, height = config.mainTexture.height }
            };
            var materials = new List<Material>();
            if (config.sources != null)
                foreach (var entry in config.sources)
                {
                    if (entry == null || entry.material == null || materials.Contains(entry.material)) continue;
                    var unified = AtlasShaderRegistry.Detect(entry.material)?.Parse(entry.material);
                    if (unified == null) continue;
                    materials.Add(entry.material);
                    var source = new AtlasGeneratorSource
                    {
                        Material = unified, UVRegion = entry.uvRegion,
                        GeometryBakingAvailable = false, PrebakedNormalMap = entry.prebakedNormalMap
                    };
                    source.Coverage.AddRange(AtlasCoverage.Triangles(entry.material, renderers));
                    if (root != null && renderers != null)
                    {
                        var material = entry.material;
                        source.Surface = () => AtlasSurfaceGeometry.Sections(material, renderers, root);
                    }
                    job.Sources.Add(source);
                }
            AtlasOutputPipeline.AddChoices(config, materials, job);
            return job;
        }

        // A material at the path is replaced in place, so what refers to it keeps working.
        private static Material Save(Material material, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(material, path);
                return material;
            }
            existing.shader = material.shader;
            EditorUtility.CopySerialized(material, existing);
            UnityEngine.Object.DestroyImmediate(material);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssetIfDirty(existing);
            return existing;
        }
    }
}
