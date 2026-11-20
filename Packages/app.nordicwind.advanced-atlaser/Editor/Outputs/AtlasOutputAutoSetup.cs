using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    public enum AtlasAutoSetupRegions { Replace, Keep }

    /// <summary>
    /// Auto Setup (ATLAS_OUTPUT.md, "Auto setup"): for the sources already assigned (it never
    /// adds or removes one), the UV regions packed by their base color textures' sizes, the main
    /// texture size of that atlas, every feature back to its default mode, and the texture
    /// settings back to their defaults.
    /// </summary>
    public static class AtlasOutputAutoSetup
    {
        public const int MaxSide = 4096;

        /// <summary>Sets up the output in one Undo step and logs what it did. Returns that
        /// summary, or null when it has no assigned source.</summary>
        public static string Apply(NordicsAtlasOutput component, AtlasAutoSetupRegions regions, bool stretch = true)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            if (Assigned(component.configuration).Count == 0) return null;
            Undo.RecordObject(component, "Auto Setup Atlas Output");
            string summary = Apply(component.configuration, regions, stretch);
            EditorUtility.SetDirty(component);
            Debug.Log("[Nordic's Advanced Atlaser] " + component.name + ": " + summary, component);
            return summary;
        }

        /// <summary>The same, on the configuration alone. <paramref name="stretch"/>: the regions
        /// stretch into the free space beside them, so the atlas is used whole.</summary>
        public static string Apply(AtlasOutputConfiguration config, AtlasAutoSetupRegions regions, bool stretch = true)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var entries = Assigned(config);
            if (entries.Count == 0) return null;
            string layoutPart;
            if (regions == AtlasAutoSetupRegions.Replace)
            {
                var layout = Layout(config, entries, stretch);
                for (int i = 0; i < entries.Count; i++) entries[i].uvRegion = layout.Region(i);
                if (config.mainTexture == null) config.mainTexture = new AtlasTextureSize();
                config.mainTexture.width = layout.Width;
                config.mainTexture.height = layout.Height;
                layoutPart = $"Auto Setup laid out {entries.Count} {(entries.Count == 1 ? "region" : "regions")} in a {layout.Width} × {layout.Height} atlas";
                if (layout.Percent < AtlasRegionPacker.Steps)
                    layoutPart += " (scaled to fit " + MaxSide + ": " + string.Join(", ",
                        entries.Select((e, i) => $"{e.material.name} {layout.Percents[i]}%")) + ")";
                if (stretch && StretchChangesLayout(config)) layoutPart += ", stretched to fill it";
            }
            else layoutPart = "Auto Setup kept the UV regions and the main texture size";
            ResetModes(config);
            config.textures?.Clear();
            return layoutPart + "; set every feature to its default mode.";
        }

        /// <summary>Whether a source's region differs from both the whole texture and the layout
        /// Auto Setup would make now: replacing it would lose an edit.</summary>
        public static bool HasEditedRegions(AtlasOutputConfiguration config)
        {
            var entries = Assigned(config);
            if (entries.Count == 0) return false;
            // Auto Setup's own layout, stretched or not, isn't an edit.
            var stretched = Layout(config, entries, true);
            var kept = Layout(config, entries, false);
            for (int i = 0; i < entries.Count; i++)
            {
                var region = entries[i].uvRegion;
                if (!Same(region, new Rect(0f, 0f, 1f, 1f)) && !Same(region, stretched.Region(i)) && !Same(region, kept.Region(i)))
                    return true;
            }
            return false;
        }

        /// <summary>Whether the packed regions leave free space that stretching would fill: only
        /// then is there a choice to make.</summary>
        public static bool StretchChangesLayout(AtlasOutputConfiguration config)
        {
            var entries = Assigned(config);
            if (entries.Count == 0) return false;
            var stretched = Layout(config, entries, true);
            var kept = Layout(config, entries, false);
            for (int i = 0; i < entries.Count; i++)
                if (!Same(stretched.Region(i), kept.Region(i))) return true;
            return false;
        }

        /// <summary>The packed atlas for the assigned sources' base color sizes.</summary>
        public static AtlasRegionLayout Layout(AtlasOutputConfiguration config, IReadOnlyList<AtlasSourceEntry> entries, bool stretch = true) =>
            AtlasRegionPacker.Pack(entries.Select(e => BaseColorSize(AtlasSourceAnalysisCache.Get(e.material)?.Unified)).ToList(), MaxSide, stretch);

        /// <summary>A source's base color texture size; the smallest texture size without one.
        /// Never a normal, decal or mask texture's.</summary>
        public static Vector2Int BaseColorSize(AtlasUnifiedMaterial source)
        {
            var texture = source?.Surface.BaseColor.Map?.Texture;
            int smallest = AtlasTextureSizes.All[0];
            if (texture == null || texture.width <= 0 || texture.height <= 0) return new Vector2Int(smallest, smallest);
            return new Vector2Int(texture.width, texture.height);
        }

        // Every stored mode back to the feature's default. The other choices are kept.
        public static void ResetModes(AtlasOutputConfiguration config)
        {
            if (config.features == null) return;
            foreach (var setting in config.features)
                if (setting != null) setting.mode = "";
        }

        private static List<AtlasSourceEntry> Assigned(AtlasOutputConfiguration config) =>
            config?.sources?.Where(e => e != null && e.material != null).ToList() ?? new List<AtlasSourceEntry>();

        private static bool Same(Rect a, Rect b) =>
            Mathf.Abs(a.x - b.x) < 1e-5f && Mathf.Abs(a.y - b.y) < 1e-5f &&
            Mathf.Abs(a.width - b.width) < 1e-5f && Mathf.Abs(a.height - b.height) < 1e-5f;
    }
}
