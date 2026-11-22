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
            if (use.Generated || use.Source != null)
            {
                OtherTextureRow(use);
                return;
            }
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
        // A texture the build makes at a fixed size, or a source's texture used as it is.
        private static void OtherTextureRow(AtlasTextureUse use)
        {
            string tooltip = use.Source != null
                ? "A material's own texture, which the atlas material uses as it is. Its memory is as imported; on Android, " +
                  "its Android override, or otherwise the Android format for its kind."
                : "Made by the build at this size, compressed with the format for its kind.";
            Columns(out var name, out var size, out var pc, out var android);
            using (new IndentReset())
            {
                EditorGUI.LabelField(name, new GUIContent(use.Texture.Label, tooltip));
                SplitPair(size, out var first, out var times, out var second);
                Locked(first, use.Width, tooltip);
                EditorGUI.LabelField(times, "×", Centered);
                Locked(second, use.Height, tooltip);
                EditorGUI.LabelField(pc, AtlasTextureMemory.Label(use.PcBytes), RightMini);
                EditorGUI.LabelField(android, AtlasTextureMemory.Label(use.AndroidBytes), RightMini);
            }
        }

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

        // The Baking section (ATLAS_OUTPUT.md, "Virtual light rigs"), shown while a feature bakes
        // with the virtual light rig: the rig, the overall strength, the light color, one strength
        // per lit bake in use, and the lights. Folded by default.
        public static void Baking(SerializedProperty config, AtlasOutputOverview overview, IReadOnlyList<Material> materials,
            IEnumerable<Renderer> renderers)
        {
            var bakes = overview.Features
                .Where(s => s.Active.Count > 0 && s.Mode != null && s.Feature is IAtlasLitBakeFeature)
                .Select(s => ((IAtlasLitBakeFeature)s.Feature).LitBake(s.Mode))
                .Where(b => b.HasValue).Select(b => b.Value).ToList();
            // Items placed Baked in a feature with slots bake too.
            var features = config.FindPropertyRelative("features");
            foreach (var state in overview.Features.Where(s => s.Active.Count > 0 && s.Mode != null && s.Mode.Kind != AtlasFeatureModeKind.Off &&
                                                               s.InstancesCanBake && s.Instances.Count > 0))
            {
                var setting = FindFeature(features, state.Feature.Id);
                if (setting != null && state.Instances.Any(i => StoredValue(setting, AtlasInstancePlacements.Id(i.Key)) is SerializedProperty v &&
                                                                Mathf.RoundToInt(v.FindPropertyRelative("number").floatValue) == (int)AtlasInstancePlacement.Baked))
                    bakes.Add(AtlasLitBake.Matcaps);
            }
            bakes.AddRange(overview.OtherBakes);
            bakes = bakes.Distinct().OrderBy(b => b).ToList();
            if (bakes.Count == 0) return;
            EditorGUILayout.Space();
            if (!Fold("baking", "Baking", false, true)) return;
            using (new EditorGUI.IndentLevelScope())
            {
                Description("The virtual lights that baked shading is made with. The avatar faces them from the front.");
                var baking = config.FindPropertyRelative("baking");
                var rig = baking.FindPropertyRelative("rig");
                EditorGUILayout.PropertyField(rig, new GUIContent("Light Rig",
                    "Classic: the key light from one side and the fill from the other. Symmetric: every light centered, " +
                    "so both halves of the avatar get the same light."));
                if (rig.enumValueIndex == (int)AtlasLightRigKind.Classic)
                {
                    var overlapping = materials.Where(m => Overlaps(m, renderers)).Select(m => m.name).ToList();
                    if (overlapping.Count > 0)
                        Description("Overlapping UVs, baked with the symmetric rig so mirrored halves match: " + string.Join(", ", overlapping) + ".");
                }
                EditorGUILayout.PropertyField(baking.FindPropertyRelative("strength"), new GUIContent("Strength",
                    "Scales every baked shading."));
                EditorGUILayout.PropertyField(baking.FindPropertyRelative("colorInfluence"), new GUIContent("Light Color",
                    "How much of the lights' color is baked. A baked color stays in every world, so a little is usually enough."));
                foreach (var bake in bakes)
                    switch (bake)
                    {
                        case AtlasLitBake.Matcaps:
                            EditorGUILayout.PropertyField(baking.FindPropertyRelative("matcaps"), new GUIContent("Matcaps",
                                "How strongly matcaps show where they are baked: into the color, or as shading in the minimum light's emission."));
                            EditorGUILayout.PropertyField(baking.FindPropertyRelative("matcapViewSide"), new GUIContent("Matcap View Side",
                                "Where baked matcaps are seen from, in degrees to the side: positive is to your left as you face the avatar. " +
                                "Materials whose UVs overlap are seen from the middle, so mirrored halves match."));
                            EditorGUILayout.PropertyField(baking.FindPropertyRelative("matcapViewHeight"), new GUIContent("Matcap View Height",
                                "Where baked matcaps are seen from, in degrees above straight in front."));
                            break;
                        case AtlasLitBake.NormalShading:
                            EditorGUILayout.PropertyField(baking.FindPropertyRelative("normalShading"), new GUIContent("Normal-Map Shading",
                                "How strongly the shading of normal maps shows where it is baked: into the color, or into the minimum light's emission."));
                            break;
                        case AtlasLitBake.Occlusion:
                            EditorGUILayout.PropertyField(baking.FindPropertyRelative("occlusion"), new GUIContent("Occlusion",
                                "How strongly ambient occlusion darkens where it is baked: the color, or the minimum light's emission."));
                            break;
                    }
                if (Fold("baking.lights", "Lights", false))
                    using (new EditorGUI.IndentLevelScope())
                    {
                        BakeLight(baking.FindPropertyRelative("key"), "Key Light", "The main light: the form and the main highlight.");
                        BakeLight(baking.FindPropertyRelative("fill"), "Fill Light", "Lifts the shadow side without a second highlight.");
                        BakeLight(baking.FindPropertyRelative("back"), "Back Light", "A crisp edge on the silhouette and highlights on hair tops.");
                    }
            }
        }

        private static SerializedProperty FindFeature(SerializedProperty features, string featureId)
        {
            for (int i = 0; i < features.arraySize; i++)
            {
                var item = features.GetArrayElementAtIndex(i);
                if (item.FindPropertyRelative("featureId").stringValue == featureId) return item;
            }
            return null;
        }

        private static void BakeLight(SerializedProperty light, string label, string tooltip)
        {
            EditorGUILayout.LabelField(new GUIContent(label, tooltip), EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(light.FindPropertyRelative("intensity"), new GUIContent("Intensity"));
                EditorGUILayout.PropertyField(light.FindPropertyRelative("temperature"), new GUIContent("Temperature (K)",
                    "The light's color temperature: lower is warmer, higher is cooler."));
                EditorGUILayout.PropertyField(light.FindPropertyRelative("softness"), new GUIContent("Softness",
                    "Softens the edge between the lit and the shadow side, and widens highlights."));
            }
        }

        // Whether a source's UVs overlap, per material, kept until the scene or the project
        // changes (AtlasMaterialDiscovery.SceneVersion).
        private static readonly Dictionary<int, bool> overlaps = new Dictionary<int, bool>();
        private static int overlapsVersion = -1;

        private static bool Overlaps(Material material, IEnumerable<Renderer> renderers)
        {
            if (material == null || renderers == null) return false;
            if (overlapsVersion != AtlasMaterialDiscovery.SceneVersion)
            {
                overlaps.Clear();
                overlapsVersion = AtlasMaterialDiscovery.SceneVersion;
            }
            int key = material.GetInstanceID();
            if (!overlaps.TryGetValue(key, out bool result))
                overlaps[key] = result = AtlasSurfaceGeometry.Overlaps(AtlasCoverage.Triangles(material, renderers));
            return result;
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
            if (state.LiveSetups.Count > 1)
                using (new EditorGUI.IndentLevelScope())
                    LiveFrom(setting, state, materials);
            foreach (var option in state.Options)
                using (new EditorGUI.IndentLevelScope())
                    OptionRow(setting, option);
            foreach (var conflictSwitch in state.Switches)
                using (new EditorGUI.IndentLevelScope())
                    Switch(setting, conflictSwitch);
            // With one item in slots there is nothing to place, unless it can be kept as its
            // average or Automatic leaves it out: otherwise the mode and the per-material
            // toggles decide. Baked, each item is baked, left out or left to Automatic; in a
            // mode that keeps averages (an output without slots), averaged too.
            bool bakedMode = state.Mode != null && state.Mode.Kind == AtlasFeatureModeKind.Baked;
            bool averagedMode = state.Mode != null && state.Mode.Kind == AtlasFeatureModeKind.Emulated;
            if (state.Instances.Count > 1 || state.Instances.Any(i => i.LeftOut) ||
                ((bakedMode || state.InstancesCanAverage) && state.Instances.Count > 0))
                using (new EditorGUI.IndentLevelScope())
                    foreach (var instance in state.Instances)
                        Placement(setting, instance, state.Instances, state.InstancesAsMatcap, state.InstancesCanBake, state.InstancesCanAverage,
                            bakedMode, averagedMode, materials);
            if (open)
                using (new EditorGUI.IndentLevelScope())
                    Sources(setting, state, materials);
        }

        // The source a shared value is taken from, where the sources' values differ: automatic
        // (the one with the largest region, or the feature's own rule) or a chosen one.
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
                (feature is IAtlasAutomaticReference rule ? rule.AutomaticRule : "Automatic takes it from the material with the largest area in the atlas."));
            int next = PopupRow(label, index, options.ToArray());
            if (next != index) reference.objectReferenceValue = next == 0 ? null : choices[next - 1];
        }

        // Which material's live global masks stay live, where the materials' differ: automatic
        // (the setup on the largest area) or a chosen one; the others are baked as normally seen.
        private static void LiveFrom(SerializedProperty setting, AtlasFeatureState state, IReadOnlyList<Material> materials)
        {
            var stored = StoredValue(setting, AtlasLiveMasks.LiveFromId);
            int chosen = stored != null ? Mathf.RoundToInt(stored.FindPropertyRelative("number").floatValue) - 1 : -1;
            var options = new List<string> { "Automatic" };
            options.AddRange(state.LiveSetups.Select(i => "Source " + (i + 1) + " (" + materials[i].name + ")"));
            int index = state.LiveSetups.IndexOf(chosen) + 1;
            var label = new GUIContent("Live from",
                "The materials' global masks that change live (back faces, mirrors, cameras, distance, blacklights) differ, and the atlas " +
                "keeps one setup live. Automatic keeps the one on the largest area; the others are baked as normally seen.");
            int next = PopupRow(label, index, options.ToArray());
            if (next == index) return;
            if (stored == null) stored = AddValue(setting, AtlasLiveMasks.LiveFromId);
            stored.FindPropertyRelative("number").floatValue = next == 0 ? 0 : state.LiveSetups[next - 1] + 1;
        }

        // A feature's own setting: a slider lined up with the mode drop-downs.
        private static void OptionRow(SerializedProperty setting, AtlasFeatureOption option)
        {
            var stored = StoredValue(setting, AtlasFeatureOption.ValueId(option.Id));
            float value = stored != null ? stored.FindPropertyRelative("number").floatValue : option.Default;
            if (option.Choices != null)
            {
                int index = Mathf.Clamp(Mathf.RoundToInt(value), 0, option.Choices.Length - 1);
                int picked = PopupRow(new GUIContent(option.Label, option.Tooltip), index, option.Choices);
                if (picked == index) return;
                if (stored == null) stored = AddValue(setting, AtlasFeatureOption.ValueId(option.Id));
                stored.FindPropertyRelative("number").floatValue = picked;
                return;
            }
            var row = EditorGUILayout.GetControlRect();
            EditorGUI.LabelField(new Rect(row.x, row.y, EditorGUIUtility.labelWidth, row.height), new GUIContent(option.Label, option.Tooltip));
            var fieldRect = new Rect(row.x + EditorGUIUtility.labelWidth, row.y, row.width - EditorGUIUtility.labelWidth, row.height);
            float next;
            using (new IndentReset())
                next = EditorGUI.Slider(fieldRect, value, option.Min, option.Max);
            if (Mathf.Approximately(next, value)) return;
            if (stored == null) stored = AddValue(setting, AtlasFeatureOption.ValueId(option.Id));
            stored.FindPropertyRelative("number").floatValue = next;
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
                stored.FindPropertyRelative("text").stringValue = "";
            }
            stored.FindPropertyRelative("toggle").boolValue = next == 0;
        }

        // Where one item goes: Automatic, its own slot, baked, kept as its average, off, or
        // combined with another item.
        private static void Placement(SerializedProperty setting, AtlasFeatureInstance instance, List<AtlasFeatureInstance> instances, bool asMatcap,
            bool canBake, bool canAverage, bool bakedMode, bool averagedMode, IReadOnlyList<Material> materials)
        {
            var stored = StoredValue(setting, AtlasInstancePlacements.Id(instance.Key));
            // Baked or averaged, there are no slots to share.
            bool noSlots = bakedMode || averagedMode;
            var others = noSlots ? new List<AtlasFeatureInstance>() : instances.Where(i => i != instance).ToList();
            var placements = new List<AtlasInstancePlacement> { AtlasInstancePlacement.Automatic };
            if (!noSlots) placements.Add(AtlasInstancePlacement.Own);
            if (asMatcap && !noSlots) placements.Add(AtlasInstancePlacement.AsMatcap);
            if (canBake) placements.Add(AtlasInstancePlacement.Baked);
            if (canAverage) placements.Add(AtlasInstancePlacement.Averaged);
            placements.Add(AtlasInstancePlacement.Off);
            var options = placements.Select(p => p == AtlasInstancePlacement.Automatic ? "Automatic" : p == AtlasInstancePlacement.Own ? "Own slot"
                : p == AtlasInstancePlacement.AsMatcap ? "As matcap" : p == AtlasInstancePlacement.Baked ? "Baked into color"
                : p == AtlasInstancePlacement.Averaged ? "Average in color" : "Off").ToList();
            options.AddRange(others.Select(o => "Combine with " + o.Label));
            int index = 0;
            if (stored != null)
            {
                var mode = (AtlasInstancePlacement)Mathf.RoundToInt(stored.FindPropertyRelative("number").floatValue);
                string target = stored.FindPropertyRelative("text").stringValue;
                // A target that is gone shows as automatic, as the build treats it.
                if (mode == AtlasInstancePlacement.Combined) index = others.FindIndex(o => o.Key == target) is int at && at >= 0 ? placements.Count + at : 0;
                else index = Math.Max(0, placements.IndexOf(mode));
            }
            string what = instance.Label + ". ";
            string averaged = canAverage ? " Average in color keeps its average effect in the base color without a slot; what changes with the view is lost." : "";
            var label = new GUIContent(instance.Label, (bakedMode
                ? what + "Automatic bakes it into the color as seen from the front, unless it's a highlight that brightens (shine, glints), " +
                  "which would stay in place from every view; the build log says which."
                : averagedMode
                ? what + "Automatic keeps the average effect of a matcap that lights the surface, and leaves a rim or a highlight out; " +
                  "the build log says which. Baked into color draws it into the textures as seen from the front, where it stays in place " +
                  "from every view."
                : what + "Automatic combines it with others on the same parts, or gives it a slot while one is free, " +
                  "and otherwise combines it with the most similar one; the build log says which." +
                  (canBake ? " Baked into color draws it into the textures as seen from the front, instead of sharing a slot." : "")) + averaged);
            int next = instance.HasPreview ? PreviewRow(label, instance, materials, index, options.ToArray()) : PopupRow(label, index, options.ToArray());
            if (next == index) return;
            if (stored == null) stored = AddValue(setting, AtlasInstancePlacements.Id(instance.Key));
            var placement = next < placements.Count ? placements[next] : AtlasInstancePlacement.Combined;
            stored.FindPropertyRelative("number").floatValue = (int)placement;
            stored.FindPropertyRelative("text").stringValue = next >= placements.Count ? others[next - placements.Count].Key : "";
        }

        private static SerializedProperty StoredValue(SerializedProperty setting, string id)
        {
            var values = setting.FindPropertyRelative("values");
            for (int i = 0; i < values.arraySize; i++)
                if (values.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == id)
                    return values.GetArrayElementAtIndex(i);
            return null;
        }

        private static SerializedProperty AddValue(SerializedProperty setting, string id)
        {
            var values = setting.FindPropertyRelative("values");
            values.InsertArrayElementAtIndex(values.arraySize);
            var stored = values.GetArrayElementAtIndex(values.arraySize - 1);
            // A new array element copies the previous one, so every field is set explicitly.
            stored.FindPropertyRelative("id").stringValue = id;
            stored.FindPropertyRelative("toggle").boolValue = false;
            stored.FindPropertyRelative("number").floatValue = 0f;
            stored.FindPropertyRelative("color").colorValue = Color.white;
            stored.FindPropertyRelative("text").stringValue = "";
            return stored;
        }

        // A labeled drop-down whose field lines up with the feature rows' mode drop-downs.
        private const float PreviewSize = 36f;

        // An item's row with its texture next to it (tinted by its color, or the color alone):
        // its name and the choice on the first line; below, what the item is, how it ends up
        // shown and the materials it is on, wrapped onto as many lines as it needs. A click on
        // the texture shows where it is in the Project window.
        private static int PreviewRow(GUIContent label, AtlasFeatureInstance instance, IReadOnlyList<Material> materials, int index, string[] options)
        {
            float line = EditorGUIUtility.singleLineHeight;
            var names = instance.Sources.Where(i => i >= 0 && i < materials.Count && materials[i] != null).Select(i => materials[i].name).ToList();
            string on = names.Count == 0 ? ""
                : "On " + (names.Count <= 2 ? string.Join(" and ", names) : names[0] + ", " + names[1] + " and " + (names.Count - 2) + " more") + ".";
            string about = string.Join(" ", new[] { instance.Kind, instance.Way }.Where(s => !string.IsNullOrEmpty(s)).Select(s => s + ".")
                .Concat(new[] { on }.Where(s => s.Length > 0)));
            var kind = new GUIContent(about, string.Join(", ", names));
            // The second line's height at the width it gets, so nothing is cut off.
            float indent = EditorGUI.indentLevel * 15f;
            float kindWidth = Mathf.Max(60f, EditorGUIUtility.currentViewWidth - 40f - indent - PreviewSize - 4f);
            float kindHeight = EditorStyles.wordWrappedMiniLabel.CalcHeight(kind, kindWidth);
            var row = EditorGUILayout.GetControlRect(false, Mathf.Max(PreviewSize, line + kindHeight));
            var indented = EditorGUI.IndentedRect(row);
            var preview = new Rect(indented.x, row.y, PreviewSize, PreviewSize);
            var tint = instance.PreviewColor.gamma;
            tint = new Color(Mathf.Clamp01(tint.r), Mathf.Clamp01(tint.g), Mathf.Clamp01(tint.b), 1f);
            if (instance.Preview != null)
            {
                var previous = GUI.color;
                GUI.color = tint;
                GUI.DrawTexture(preview, instance.Preview, ScaleMode.ScaleToFit);
                GUI.color = previous;
                if (GUI.Button(preview, new GUIContent("", instance.Preview.name + ". Click to show it in the Project window."), GUIStyle.none))
                    EditorGUIUtility.PingObject(instance.Preview);
            }
            else EditorGUI.DrawRect(preview, tint);

            float left = indented.x + PreviewSize + 4f;
            // The materials are named below, so the first line holds the item's own name alone.
            var name = new GUIContent(instance.Name ?? label.text, label.tooltip);
            using (new IndentReset())
            {
                EditorGUI.LabelField(new Rect(left, row.y, Mathf.Max(0f, row.x + EditorGUIUtility.labelWidth - left), line), name);
                EditorGUI.LabelField(new Rect(left, row.y + line, Mathf.Max(0f, row.xMax - left), row.height - line), kind, EditorStyles.wordWrappedMiniLabel);
                return EditorGUI.Popup(new Rect(row.x + EditorGUIUtility.labelWidth, row.y, row.width - EditorGUIUtility.labelWidth, line), index, options);
            }
        }

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
