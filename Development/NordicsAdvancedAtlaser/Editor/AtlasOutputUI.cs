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

        public static void Section(string title) { EditorGUILayout.Space(); EditorGUILayout.LabelField(title, EditorStyles.boldLabel); }
        public static void Description(string text) { if (!string.IsNullOrEmpty(text)) EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel); }

        // A foldout whose state is remembered for the editor session.
        public static bool Fold(string key, string label, bool defaultOpen, bool bold = false)
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
            using (new EditorGUILayout.HorizontalScope())
            {
                // The main texture is always generated; only its size is chosen.
                EditorGUILayout.PrefixLabel("Main Texture Size");
                w.intValue = SizePopup(w.intValue, AtlasTextureSizes.All);
                h.intValue = SizePopup(h.intValue, AtlasTextureSizes.All);
            }
            return EditorGUI.EndChangeCheck();
        }

        // The Textures section: the main texture size, every texture the chosen modes use with
        // its size and memory, the totals, and the formats the memory follows.
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
                foreach (var use in overview.Textures)
                    TextureRow(textures, use, mainWidth, mainHeight);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel(new GUIContent("Total (estimate)",
                        "GPU memory of the compressed textures with their mipmaps, as VRChat's performance rank counts it. Crunch only changes the download size."));
                    EditorGUILayout.LabelField(MemoryText(overview.PcBytes, overview.AndroidBytes), EditorStyles.boldLabel);
                }
                if (Fold("formats", "Formats", false))
                    using (new EditorGUI.IndentLevelScope())
                    {
                        Formats(config.FindPropertyRelative("pcFormats"), "PC");
                        Formats(config.FindPropertyRelative("androidFormats"), "Android");
                    }
            }
        }

        private static void TextureRow(SerializedProperty textures, AtlasTextureUse use, int mainWidth, int mainHeight)
        {
            var texture = use.Texture;
            string holds = string.Join(", ", use.Maps.Select(m => ObjectNames.NicifyVariableName(m.ToString()).ToLowerInvariant()));
            var label = new GUIContent(texture.Label, "Holds: " + holds + (use.Required ? ". Required by the chosen modes." : ". Optional."));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (use.Required)
                    EditorGUILayout.PrefixLabel(label);
                else
                {
                    var setting = FindOrAddTexture(textures, use);
                    var enabled = setting.FindPropertyRelative("enabled");
                    enabled.boolValue = EditorGUILayout.ToggleLeft(label, enabled.boolValue, GUILayout.Width(EditorGUIUtility.labelWidth));
                }
                using (new EditorGUI.DisabledScope(!use.Enabled))
                    Size(textures, use, mainWidth, mainHeight);
                EditorGUILayout.LabelField(MemoryText(use.PcBytes, use.AndroidBytes), EditorStyles.miniLabel);
            }
        }

        private static void Size(SerializedProperty textures, AtlasTextureUse use, int mainWidth, int mainHeight)
        {
            var mode = use.Texture.SizeMode;
            if (mode == AtlasTextureSizeMode.Main)
            {
                EditorGUILayout.LabelField($"{use.Width} × {use.Height} (main)", GUILayout.MinWidth(90));
                return;
            }
            int width = use.Width, height = use.Height;
            EditorGUI.BeginChangeCheck();
            if (mode == AtlasTextureSizeMode.MainAspect)
            {
                width = SizePopup(width, AtlasTextureSizes.MainAspectWidths(mainWidth, mainHeight));
                EditorGUILayout.LabelField("× " + AtlasTextureSizes.HeightAt(width, mainWidth, mainHeight), GUILayout.Width(50));
            }
            else if (mode == AtlasTextureSizeMode.Square)
            {
                width = SizePopup(width, AtlasTextureSizes.All);
                EditorGUILayout.LabelField("× " + width, GUILayout.Width(50));
            }
            else
            {
                width = SizePopup(width, AtlasTextureSizes.All);
                height = SizePopup(height, AtlasTextureSizes.All);
            }
            if (!EditorGUI.EndChangeCheck()) return;
            AtlasTextureSizes.Normalize(mode, mainWidth, mainHeight, ref width, ref height);
            var setting = FindOrAddTexture(textures, use);
            setting.FindPropertyRelative("width").intValue = width;
            setting.FindPropertyRelative("height").intValue = height;
        }

        private static void Formats(SerializedProperty formats, string platform)
        {
            EditorGUILayout.LabelField(platform, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(formats.FindPropertyRelative("color"), new GUIContent("Colour"));
            EditorGUILayout.PropertyField(formats.FindPropertyRelative("colorAlpha"), new GUIContent("Colour with alpha"));
            EditorGUILayout.PropertyField(formats.FindPropertyRelative("normal"), new GUIContent("Normal map"));
            EditorGUILayout.PropertyField(formats.FindPropertyRelative("data"), new GUIContent("Data and masks"));
        }

        private static string MemoryText(long pc, long android) =>
            $"PC {AtlasTextureMemory.Label(pc)} · Android {AtlasTextureMemory.Label(android)}";

        // The feature sections: each feature a source uses and the output shader supports, folded
        // to a summary line, with its mode and the sources it applies to.
        public static void Features(SerializedProperty config, AtlasOutputOverview overview, IReadOnlyList<Material> materials)
        {
            var features = config.FindPropertyRelative("features");
            if (materials.Count == 0)
            {
                Section("Features");
                Description("Add source materials to see their features.");
                return;
            }
            foreach (AtlasFeatureSection section in Enum.GetValues(typeof(AtlasFeatureSection)))
            {
                var states = overview.Features.Where(s => s.Feature.Section == section && s.Present.Count > 0).ToList();
                if (states.Count == 0) continue;
                EditorGUILayout.Space();
                if (!Fold("section." + section, ObjectNames.NicifyVariableName(section.ToString()), true, true)) continue;
                using (new EditorGUI.IndentLevelScope())
                    foreach (var state in states)
                        Feature(features, state, materials);
            }
        }

        private static void Feature(SerializedProperty features, AtlasFeatureState state, IReadOnlyList<Material> materials)
        {
            var feature = state.Feature;
            string header = $"{feature.DisplayName} — {state.Mode.Label} — {state.Active.Count} of {materials.Count} sources";
            if (!Fold("feature." + feature.Id, header, false)) return;
            using (new EditorGUI.IndentLevelScope())
            {
                var setting = FindOrAddFeature(features, feature.Id);
                var stored = setting.FindPropertyRelative("mode");
                int index = Math.Max(0, IndexOf(state.Modes, state.Mode));
                int next = EditorGUILayout.Popup("Mode", index, state.Modes.Select(m => m.Label).ToArray());
                if (next != index) stored.stringValue = state.Modes[next].Id;
                if (state.Present.Count > 1 && Fold("sources." + feature.Id, "Sources", false))
                    using (new EditorGUI.IndentLevelScope())
                        Sources(setting, state, materials);
            }
        }

        // Each source that has the feature: on or off, and the reference source where the mode
        // takes shared values from one.
        private static void Sources(SerializedProperty setting, AtlasFeatureState state, IReadOnlyList<Material> materials)
        {
            var disabled = setting.FindPropertyRelative("disabledSources");
            var reference = setting.FindPropertyRelative("referenceSource");
            foreach (int i in state.Present)
            {
                var material = materials[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    int at = IndexOf(disabled, material);
                    bool on = EditorGUILayout.ToggleLeft(material.name, at < 0);
                    if (on && at >= 0) RemoveAt(disabled, at);
                    else if (!on && at < 0)
                    {
                        disabled.InsertArrayElementAtIndex(disabled.arraySize);
                        disabled.GetArrayElementAtIndex(disabled.arraySize - 1).objectReferenceValue = material;
                    }
                    if (!state.Mode.UsesReferenceSource) continue;
                    bool isReference = reference.objectReferenceValue == material;
                    if (EditorGUILayout.ToggleLeft("Reference", isReference, GUILayout.Width(90)) && !isReference)
                        reference.objectReferenceValue = material;
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

        private static int SizePopup(int current, int[] options)
        {
            if (options.Length == 0) return current;
            int index = Array.IndexOf(options, current);
            if (index < 0) index = Array.IndexOf(options, AtlasTextureSizes.Closest(current, options));
            int next = EditorGUILayout.Popup(index, options.Select(v => v.ToString()).ToArray(), GUILayout.MinWidth(60));
            return options[Mathf.Clamp(next, 0, options.Length - 1)];
        }
    }
}
