using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Reusable IMGUI building blocks, drawn from the shared feature descriptions and the output
    // overview. No output-shader-specific UI code in the inspector. Edits go through
    // SerializedProperty, so they can be undone; foldout states are per editor session.
    public static class AtlasOutputUI
    {
        private const string FoldPrefix = "NordicWind.AdvancedAtlaser.Fold.";
        private static GUIStyle boldFoldout;
        private static GUIStyle BoldFoldout => boldFoldout ?? (boldFoldout = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold });

        public static void Description(string text) { if (!string.IsNullOrEmpty(text)) EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel); }

        // A foldout whose state is remembered for the editor session.
        public static bool Fold(string key, string label, bool defaultOpen, bool bold = false) =>
            Fold(key, new GUIContent(label), defaultOpen, bold);

        public static bool Fold(string key, GUIContent label, bool defaultOpen, bool bold = false)
        {
            bool open = SessionState.GetBool(FoldPrefix + key, defaultOpen);
            bool next = EditorGUILayout.Foldout(open, label, true, bold ? BoldFoldout : EditorStyles.foldout);
            if (next != open) SessionState.SetBool(FoldPrefix + key, next);
            return next;
        }

        // True when the user changed the main texture size.
        public static bool Main(SerializedProperty main)
        {
            if (main == null) return false;
            var w = main.FindPropertyRelative("width");
            var h = main.FindPropertyRelative("height");
            EditorGUI.BeginChangeCheck();
            // The main texture is always generated; only its size is chosen.
            var size = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), new GUIContent("Main Texture Size"));
            using (new IndentReset())
                PairOfSizes(size, w, h);
            return EditorGUI.EndChangeCheck();
        }

        // The Textures section: the main texture size, a table of the textures the chosen modes
        // use with their size and memory, the totals, and the compression formats.
        public static void Textures(SerializedProperty config, AtlasOutputOverview overview)
        {
            EditorGUILayout.Space();
            if (!Fold("textures", "Textures", true, true)) return;
            using (new EditorGUI.IndentLevelScope())
            {
                var main = config.FindPropertyRelative("mainTexture");
                Main(main);
                int mainWidth = main.FindPropertyRelative("width").intValue, mainHeight = main.FindPropertyRelative("height").intValue;
                var textures = config.FindPropertyRelative("textures");
                if (overview.Textures.Count == 0)
                    Description("No texture: no source uses a feature that needs one.");
                else
                {
                    Columns(out var name, out var size, out var pc, out var android);
                    using (new IndentReset())
                    {
                        EditorGUI.LabelField(name, "Texture", EditorStyles.miniLabel);
                        EditorGUI.LabelField(size, "Size", EditorStyles.miniLabel);
                        EditorGUI.LabelField(pc, "PC", RightMini);
                        EditorGUI.LabelField(android, "Android", RightMini);
                    }
                    foreach (var use in overview.Textures)
                        TextureRow(textures, use, mainWidth, mainHeight);
                    Columns(out name, out size, out pc, out android);
                    using (new IndentReset())
                    {
                        EditorGUI.LabelField(name, new GUIContent("Total (estimate)",
                            "GPU memory of the compressed textures with their full mip chain, as VRChat's performance rank counts it. " +
                            "The mipmaps stream, so fewer levels may be loaded at a distance, but the rank counts the full texture. " +
                            "Crunch only changes the download size."), EditorStyles.boldLabel);
                        EditorGUI.LabelField(pc, AtlasTextureMemory.Label(overview.PcBytes), RightBold);
                        EditorGUI.LabelField(android, AtlasTextureMemory.Label(overview.AndroidBytes), RightBold);
                    }
                }
                if (Fold("formats", new GUIContent("GPU Compression Formats",
                        "The block compression each kind of texture is stored with, per platform. The memory above follows them."), false))
                    using (new EditorGUI.IndentLevelScope())
                    {
                        Formats(config.FindPropertyRelative("pcFormats"), "PC");
                        Formats(config.FindPropertyRelative("androidFormats"), "Android");
                    }
            }
        }

        // The memory columns are fixed and right-aligned, so their figures never clip; the name
        // column lines up with the inspector's labels and the size takes what's left.
        private const float MemoryColumn = 62f;
        private static void Columns(out Rect name, out Rect size, out Rect pc, out Rect android)
        {
            var row = EditorGUILayout.GetControlRect();
            var indented = EditorGUI.IndentedRect(row);
            name = new Rect(indented.x, row.y, Mathf.Max(40f, row.x + EditorGUIUtility.labelWidth - indented.x - 2f), row.height);
            android = new Rect(row.xMax - MemoryColumn, row.y, MemoryColumn, row.height);
            pc = new Rect(android.x - MemoryColumn - 4f, row.y, MemoryColumn, row.height);
            float sizeX = row.x + EditorGUIUtility.labelWidth;
            size = new Rect(sizeX, row.y, Mathf.Max(0f, pc.x - sizeX - 6f), row.height);
        }

        private static GUIStyle rightMini, rightBold;
        private static GUIStyle RightMini => rightMini ?? (rightMini = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight });
        private static GUIStyle RightBold => rightBold ?? (rightBold = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleRight });

        // Controls drawn into rects that are already indented. A class: a struct's `new()`
        // wouldn't run the constructor.
        private sealed class IndentReset : IDisposable
        {
            private readonly int level;
            public IndentReset() { level = EditorGUI.indentLevel; EditorGUI.indentLevel = 0; }
            public void Dispose() => EditorGUI.indentLevel = level;
        }

        private static void TextureRow(SerializedProperty textures, AtlasTextureUse use, int mainWidth, int mainHeight)
        {
            var texture = use.Texture;
            string holds = string.Join(", ", use.Maps.Select(m => ObjectNames.NicifyVariableName(m.ToString()).ToLowerInvariant()));
            var label = new GUIContent(texture.Label, "Holds: " + holds + (use.Required ? ". Required by the chosen modes." : ". Optional."));
            Columns(out var name, out var size, out var pc, out var android);
            using (new IndentReset())
            {
                if (use.Required)
                    EditorGUI.LabelField(name, label);
                else
                {
                    var setting = FindOrAddTexture(textures, use);
                    var enabled = setting.FindPropertyRelative("enabled");
                    enabled.boolValue = EditorGUI.ToggleLeft(name, label, enabled.boolValue);
                }
                using (new EditorGUI.DisabledScope(!use.Enabled))
                    Size(size, textures, use, mainWidth, mainHeight);
                EditorGUI.LabelField(pc, use.Enabled ? AtlasTextureMemory.Label(use.PcBytes) : "off", RightMini);
                EditorGUI.LabelField(android, use.Enabled ? AtlasTextureMemory.Label(use.AndroidBytes) : "off", RightMini);
            }
        }

        private static void Size(Rect rect, SerializedProperty textures, AtlasTextureUse use, int mainWidth, int mainHeight)
        {
            var mode = use.Texture.SizeMode;
            SplitPair(rect, out var first, out var times, out var second);
            if (mode == AtlasTextureSizeMode.Main)
            {
                // Always the main texture's size: both locked.
                Locked(first, use.Width, "Always the main texture's size.");
                EditorGUI.LabelField(times, "×", Centered);
                Locked(second, use.Height, "Always the main texture's size.");
                return;
            }
            int width = use.Width, height = use.Height;
            EditorGUI.BeginChangeCheck();
            if (mode == AtlasTextureSizeMode.MainAspect)
            {
                width = SizePopup(first, width, AtlasTextureSizes.MainAspectWidths(mainWidth, mainHeight));
                EditorGUI.LabelField(times, "×", Centered);
                Locked(second, AtlasTextureSizes.HeightAt(width, mainWidth, mainHeight), "Follows the width, keeping the main texture's aspect.");
            }
            else if (mode == AtlasTextureSizeMode.Square)
            {
                width = SizePopup(first, width, AtlasTextureSizes.All);
                EditorGUI.LabelField(times, "×", Centered);
                Locked(second, width, "Square: follows the width.");
            }
            else
            {
                width = SizePopup(first, width, AtlasTextureSizes.All);
                EditorGUI.LabelField(times, "×", Centered);
                height = SizePopup(second, height, AtlasTextureSizes.All);
            }
            if (!EditorGUI.EndChangeCheck()) return;
            AtlasTextureSizes.Normalize(mode, mainWidth, mainHeight, ref width, ref height);
            var setting = FindOrAddTexture(textures, use);
            setting.FindPropertyRelative("width").intValue = width;
            setting.FindPropertyRelative("height").intValue = height;
        }

        // A size that can't be changed, as a drop-down with that one value, so it lines up with
        // the sizes that can.
        private static void Locked(Rect rect, int value, string tooltip)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.Popup(rect, GUIContent.none, 0, new[] { new GUIContent(value.ToString(), tooltip) });
        }

        // Two sizes side by side with a "×" between them.
        private static void SplitPair(Rect rect, out Rect first, out Rect times, out Rect second)
        {
            const float gap = 16f;
            float half = Mathf.Max(0f, (rect.width - gap) / 2f);
            first = new Rect(rect.x, rect.y, half, rect.height);
            times = new Rect(first.xMax, rect.y, gap, rect.height);
            second = new Rect(times.xMax, rect.y, half, rect.height);
        }

        private static void PairOfSizes(Rect rect, SerializedProperty width, SerializedProperty height)
        {
            SplitPair(rect, out var first, out var times, out var second);
            width.intValue = SizePopup(first, width.intValue, AtlasTextureSizes.All);
            EditorGUI.LabelField(times, "×", Centered);
            height.intValue = SizePopup(second, height.intValue, AtlasTextureSizes.All);
        }

        private static GUIStyle centered;
        private static GUIStyle Centered => centered ?? (centered = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter });

        private static void Formats(SerializedProperty formats, string platform)
        {
            EditorGUILayout.LabelField(platform, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(formats.FindPropertyRelative("color"), new GUIContent("Color"));
            EditorGUILayout.PropertyField(formats.FindPropertyRelative("colorAlpha"), new GUIContent("Color with alpha"));
            EditorGUILayout.PropertyField(formats.FindPropertyRelative("normal"), new GUIContent("Normal map"));
            EditorGUILayout.PropertyField(formats.FindPropertyRelative("data"), new GUIContent("Data and masks"));
        }

        // Material Features: one row per feature a source uses and the output shader supports,
        // with its mode. Where several sources have it, the row folds open to turn it off for
        // single materials. Section headings only appear once there is more than one section.
        public static void Features(SerializedProperty config, AtlasOutputOverview overview, IReadOnlyList<Material> materials)
        {
            var features = config.FindPropertyRelative("features");
            EditorGUILayout.Space();
            if (!Fold("features", "Material Features", true, true)) return;
            using (new EditorGUI.IndentLevelScope())
            {
                if (materials.Count == 0)
                {
                    Description("Add source materials to see their features.");
                    return;
                }
                Description("How the atlas material reproduces what the source materials do.");
                var states = overview.Features.Where(s => s.Present.Count > 0).ToList();
                bool headings = states.Select(s => s.Feature.Section).Distinct().Count() > 1;
                foreach (AtlasFeatureSection section in Enum.GetValues(typeof(AtlasFeatureSection)))
                {
                    var inSection = states.Where(s => s.Feature.Section == section).ToList();
                    if (inSection.Count == 0) continue;
                    if (headings) EditorGUILayout.LabelField(ObjectNames.NicifyVariableName(section.ToString()), EditorStyles.miniBoldLabel);
                    foreach (var state in inSection)
                        Feature(features, state, materials);
                }
            }
        }

        private static void Feature(SerializedProperty features, AtlasFeatureState state, IReadOnlyList<Material> materials)
        {
            var feature = state.Feature;
            var setting = FindOrAddFeature(features, feature.Id);
            var stored = setting.FindPropertyRelative("mode");
            // A feature that is always on has nothing to turn off per material.
            bool several = state.Present.Count > 1 && !(feature is IAtlasAlwaysOnFeature);
            // Collapsed, the name says when the feature is off for some of the materials.
            string name = feature.DisplayName;
            if (several && state.Active.Count < state.Present.Count)
                name += $" ({state.Active.Count} of {state.Present.Count} materials)";
            var label = new GUIContent(name, several
                ? "Unfold to turn this feature off for single materials."
                : "The mode decides how the atlas material makes this feature.");
            var row = EditorGUILayout.GetControlRect();
            var labelRect = new Rect(row.x, row.y, EditorGUIUtility.labelWidth, row.height);
            var fieldRect = new Rect(row.x + EditorGUIUtility.labelWidth, row.y, row.width - EditorGUIUtility.labelWidth, row.height);
            bool open = false;
            if (several) open = FoldAt(labelRect, "feature." + feature.Id, label);
            else EditorGUI.LabelField(labelRect, label);
            int index = Math.Max(0, IndexOf(state.Modes, state.Mode));
            using (new IndentReset())
            {
                int next = EditorGUI.Popup(fieldRect, index, state.Modes.Select(m => m.Label).ToArray());
                if (next != index) stored.stringValue = state.Modes[next].Id;
            }
            if (state.SharedValuesDiffer)
                using (new EditorGUI.IndentLevelScope())
                    SharedValue(setting, state, materials);
            foreach (var conflictSwitch in state.Switches)
                using (new EditorGUI.IndentLevelScope())
                    Switch(setting, conflictSwitch);
            if (open)
                using (new EditorGUI.IndentLevelScope())
                    Sources(setting, state, materials);
        }

        // The source a shared value is taken from, where the sources' values differ: automatic
        // (the one with the largest region) or a chosen one.
        private static void SharedValue(SerializedProperty setting, AtlasFeatureState state, IReadOnlyList<Material> materials)
        {
            var feature = (IAtlasSharedValueFeature)state.Feature;
            var reference = setting.FindPropertyRelative("referenceSource");
            var choices = state.Active.Select(i => materials[i]).ToList();
            var options = new List<string> { "Automatic" };
            options.AddRange(state.Active.Select(i => "Source " + (i + 1) + " (" + materials[i].name + ")"));
            // A chosen source that is turned off or gone shows as automatic, as the build uses it.
            int index = choices.IndexOf(reference.objectReferenceValue as Material) + 1;
            var label = new GUIContent(feature.SharedValueName + " from",
                "The materials' " + feature.SharedValueName.ToLowerInvariant() + " differs, and the atlas material has one. " +
                "Automatic takes it from the material with the largest area in the atlas.");
            int next = PopupRow(label, index, options.ToArray());
            if (next != index) reference.objectReferenceValue = next == 0 ? null : choices[next - 1];
        }

        // A setting the materials disagree on, which the atlas material has once: on or off.
        private static void Switch(SerializedProperty setting, AtlasConflictSwitch conflictSwitch)
        {
            var values = setting.FindPropertyRelative("values");
            SerializedProperty stored = null;
            for (int i = 0; i < values.arraySize && stored == null; i++)
                if (values.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == conflictSwitch.Id)
                    stored = values.GetArrayElementAtIndex(i);
            bool on = stored != null ? stored.FindPropertyRelative("toggle").boolValue : conflictSwitch.Default;
            int index = on ? 0 : 1;
            int next = PopupRow(new GUIContent(conflictSwitch.Label, conflictSwitch.Tooltip), index, new[] { "On", "Off" });
            if (next == index) return;
            if (stored == null)
            {
                values.InsertArrayElementAtIndex(values.arraySize);
                stored = values.GetArrayElementAtIndex(values.arraySize - 1);
                // A new array element copies the previous one, so every field is set explicitly.
                stored.FindPropertyRelative("id").stringValue = conflictSwitch.Id;
                stored.FindPropertyRelative("number").floatValue = 0f;
                stored.FindPropertyRelative("color").colorValue = Color.white;
            }
            stored.FindPropertyRelative("toggle").boolValue = next == 0;
        }

        // A labeled drop-down whose field lines up with the feature rows' mode drop-downs.
        private static int PopupRow(GUIContent label, int index, string[] options)
        {
            var row = EditorGUILayout.GetControlRect();
            EditorGUI.LabelField(new Rect(row.x, row.y, EditorGUIUtility.labelWidth, row.height), label);
            var fieldRect = new Rect(row.x + EditorGUIUtility.labelWidth, row.y, row.width - EditorGUIUtility.labelWidth, row.height);
            using (new IndentReset())
                return EditorGUI.Popup(fieldRect, index, options);
        }

        private static bool FoldAt(Rect rect, string key, GUIContent label)
        {
            bool open = SessionState.GetBool(FoldPrefix + key, false);
            bool next = EditorGUI.Foldout(rect, open, label, true);
            if (next != open) SessionState.SetBool(FoldPrefix + key, next);
            return next;
        }

        // Each source that has the feature, on or off.
        private static void Sources(SerializedProperty setting, AtlasFeatureState state, IReadOnlyList<Material> materials)
        {
            var disabled = setting.FindPropertyRelative("disabledSources");
            foreach (int i in state.Present)
            {
                var material = materials[i];
                int at = IndexOf(disabled, material);
                bool on = EditorGUILayout.ToggleLeft(material.name, at < 0);
                if (on && at >= 0) RemoveAt(disabled, at);
                else if (!on && at < 0)
                {
                    disabled.InsertArrayElementAtIndex(disabled.arraySize);
                    disabled.GetArrayElementAtIndex(disabled.arraySize - 1).objectReferenceValue = material;
                }
            }
        }

        private static int IndexOf(IReadOnlyList<AtlasFeatureMode> modes, AtlasFeatureMode mode)
        {
            for (int i = 0; i < modes.Count; i++) if (modes[i] == mode) return i;
            return -1;
        }

        private static int IndexOf(SerializedProperty list, UnityEngine.Object value)
        {
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == value) return i;
            return -1;
        }

        // Deleting an object reference element first only clears it, so it's cleared explicitly.
        private static void RemoveAt(SerializedProperty list, int index)
        {
            list.GetArrayElementAtIndex(index).objectReferenceValue = null;
            list.DeleteArrayElementAtIndex(index);
        }

        private static SerializedProperty FindOrAddFeature(SerializedProperty features, string featureId)
        {
            for (int i = 0; i < features.arraySize; i++)
            {
                var item = features.GetArrayElementAtIndex(i);
                if (item.FindPropertyRelative("featureId").stringValue == featureId) return item;
            }
            int index = features.arraySize;
            features.InsertArrayElementAtIndex(index);
            var added = features.GetArrayElementAtIndex(index);
            // A new array element copies the previous one, so every field is set explicitly.
            added.FindPropertyRelative("featureId").stringValue = featureId;
            added.FindPropertyRelative("mode").stringValue = "";
            added.FindPropertyRelative("referenceSource").objectReferenceValue = null;
            added.FindPropertyRelative("disabledSources").ClearArray();
            added.FindPropertyRelative("values").ClearArray();
            return added;
        }

        private static SerializedProperty FindOrAddTexture(SerializedProperty textures, AtlasTextureUse use)
        {
            for (int i = 0; i < textures.arraySize; i++)
            {
                var item = textures.GetArrayElementAtIndex(i);
                if (item.FindPropertyRelative("textureId").stringValue == use.Texture.Id) return item;
            }
            int index = textures.arraySize;
            textures.InsertArrayElementAtIndex(index);
            var added = textures.GetArrayElementAtIndex(index);
            added.FindPropertyRelative("textureId").stringValue = use.Texture.Id;
            added.FindPropertyRelative("enabled").boolValue = true;
            added.FindPropertyRelative("width").intValue = use.Width;
            added.FindPropertyRelative("height").intValue = use.Height;
            return added;
        }

        private static int SizePopup(Rect rect, int current, int[] options)
        {
            if (options.Length == 0) return current;
            int index = Array.IndexOf(options, current);
            if (index < 0) index = Array.IndexOf(options, AtlasTextureSizes.Closest(current, options));
            int next = EditorGUI.Popup(rect, index, options.Select(v => v.ToString()).ToArray());
            return options[Mathf.Clamp(next, 0, options.Length - 1)];
        }
    }
}
