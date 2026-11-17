using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Auto setup: the main texture size from the sources' base color textures, and every
    /// feature back to its default mode, which is the best one the output shader supports.
    /// </summary>
    public static class AtlasOutputAutoSetup
    {
        private static readonly int[] SupportedSizes = { 128, 256, 512, 1024, 2048, 4096, 8192 };

        public static void Apply(NordicsAtlasOutput component)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            AtlasOutputConfiguration config = component.configuration;
            IAtlasOutputShader output = AtlasShaderRegistry.Output(config.shaderId);
            if (output == null) { Debug.LogWarning("Atlas output shader is not registered: " + config.shaderId, component); return; }

            // Complete parsing before changing serialized state; invalid inputs are ignored
            // individually rather than preventing auto configuration for the remaining sources.
            var parsed = new List<AtlasUnifiedMaterial>();
            var seen = new HashSet<Material>();
            if (config.sources != null)
            {
                foreach (var entry in config.sources)
                {
                    Material material = entry == null ? null : entry.material;
                    if (material == null || !seen.Add(material)) continue;
                    var parser = AtlasShaderRegistry.Detect(material);
                    if (parser == null) continue;
                    try { parsed.Add(parser.Parse(material)); }
                    catch (Exception ex) { Debug.LogWarning("Cannot parse '" + material.name + "' for Auto Setup: " + ex.Message, component); }
                }
            }
            if (parsed.Count == 0) { Debug.LogWarning("Auto Setup requires at least one parseable assigned material.", component); return; }

            Undo.RecordObject(component, "Auto Setup Atlas Output");
            if (config.mainTexture == null) config.mainTexture = new AtlasTextureSize();
            SetMainTextureDimensions(parsed, config.mainTexture);
            config.mainTexture.width = Closest(config.mainTexture.width);
            config.mainTexture.height = Closest(config.mainTexture.height);
            ResetModes(config);
            EditorUtility.SetDirty(component);
        }

        // Every stored mode back to the feature's default. The other choices are kept.
        public static void ResetModes(AtlasOutputConfiguration config)
        {
            if (config.features == null) return;
            foreach (var setting in config.features)
                if (setting != null) setting.mode = "";
        }

        public static void SetMainTextureDimensions(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasTextureSize main)
        {
            if (sources == null || main == null) return;
            Texture largest = null;
            long largestArea = -1;
            foreach (var source in sources)
            {
                // Only the base color sizes the atlas, never a large normal, decal or mask texture.
                var tex = source?.Surface.BaseColor.Map?.Texture;
                if (tex == null || tex.width <= 0 || tex.height <= 0) continue;
                long area = (long)tex.width * tex.height;
                if (area > largestArea)
                {
                    largest = tex;
                    largestArea = area;
                }
            }
            if (largest == null) return; // Keep user dimensions if no source main texture exists.
            main.width = Closest(largest.width);
            main.height = Closest(largest.height);
        }

        private static int Closest(int size)
        {
            // Clamp to sizes supported by the shared output UI; ties favor smaller values.
            return SupportedSizes.OrderBy(v => Math.Abs((long)v - size)).First();
        }
    }
}
