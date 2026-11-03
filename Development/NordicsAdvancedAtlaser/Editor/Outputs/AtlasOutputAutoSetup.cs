using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Shared auto-configuration orchestration. Source shader parsers produce unified materials;
    /// output shaders choose their own defaults without any input-shader-specific branching.
    /// </summary>
    public static class AtlasOutputAutoSetup
    {
        private static readonly int[] SupportedSizes = { 128, 256, 512, 1024, 2048, 4096, 8192 };

        public static void Apply(NordicsAtlasOutput component)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            AtlasOutputConfiguration config = component.Configuration;
            IAtlasOutputShader output = AtlasShaderRegistry.Output(config.ShaderId);
            if (output == null) { Debug.LogWarning("Atlas output shader is not registered: " + config.ShaderId, component); return; }

            // Complete parsing before changing serialized state; invalid inputs are ignored
            // individually rather than preventing auto configuration for the remaining sources.
            var parsed = new List<AtlasUnifiedMaterial>();
            var seen = new HashSet<Material>();
            if (config.Sources != null)
            {
                foreach (var entry in config.Sources)
                {
                    Material material = entry == null ? null : entry.Material;
                    if (material == null || !seen.Add(material)) continue;
                    var parser = AtlasShaderRegistry.Detect(material);
                    if (parser == null) continue;
                    try { parsed.Add(parser.Parse(material)); }
                    catch (Exception ex) { Debug.LogWarning("Cannot parse '" + material.name + "' for Auto Setup: " + ex.Message, component); }
                }
            }
            if (parsed.Count == 0) { Debug.LogWarning("Auto Setup requires at least one parseable assigned material.", component); return; }

            Undo.RecordObject(component, "Auto Setup Atlas Output");
            AtlasShaderRegistry.EnsureSettings(config, output);
            SetMainTextureDimensions(parsed, config.MainTexture);
            // Output shaders can override shared dimensions (and every other shared setting).
            output.AutoConfigure(parsed, config);
            output.OnConfigurationChanged(config, AtlasConfigurationChange.AutoSetup);
            if (config.MainTexture == null) config.MainTexture = new AtlasTextureDimensions();
            config.MainTexture.Width = Closest(config.MainTexture.Width);
            config.MainTexture.Height = Closest(config.MainTexture.Height);
            EditorUtility.SetDirty(component);

            // Reuse exactly the same constraint normalization as the inspector controls.
            var serialized = new SerializedObject(component);
            serialized.Update();
            var property = serialized.FindProperty("Configuration");
            AtlasOutputUI.NormalizeAll(property.FindPropertyRelative("Settings"), property.FindPropertyRelative("MainTexture"), output);
            serialized.ApplyModifiedProperties();
        }

        public static void SetMainTextureDimensions(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasTextureDimensions main)
        {
            if (sources == null || main == null) return;
            Texture largest = null;
            long largestArea = -1;
            foreach (var source in sources)
            {
                if (source == null) continue;
                // Prefer semantically normalized albedo. Do not accidentally size by a
                // large normal, decal or mask texture when no main texture exists.
                foreach (var sample in source.Textures(AtlasTextureSemantic.BaseColor))
                {
                    var tex = sample.Texture;
                    if (tex == null || tex.width <= 0 || tex.height <= 0) continue;
                    long area = (long)tex.width * tex.height;
                    if (area > largestArea)
                    {
                        largest = tex;
                        largestArea = area;
                    }
                }
            }
            if (largest == null) return; // Keep user dimensions if no source main texture exists.
            main.Enabled = true;
            main.Width = Closest(largest.width);
            main.Height = Closest(largest.height);
        }

        public static void SetTextureForSemantic(AtlasOutputConfiguration output, string fieldId,
            IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasTextureSemantic semantic)
        {
            var setting = output == null ? null : output.Find(fieldId);
            if (setting == null || setting.Texture == null) return;
            setting.Texture.Enabled = sources != null && sources.Any(source => source != null &&
                source.HasTexture(semantic));
        }

        private static int Closest(int size)
        {
            // Clamp to sizes supported by the shared output UI; ties favor smaller values.
            return SupportedSizes.OrderBy(v => Math.Abs((long)v - size)).First();
        }
    }
}
