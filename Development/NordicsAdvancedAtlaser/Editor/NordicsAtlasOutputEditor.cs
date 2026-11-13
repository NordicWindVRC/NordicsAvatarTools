using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace NordicWind.AdvancedAtlaser.Editor
{
    [CustomEditor(typeof(NordicsAtlasOutput))]
    internal sealed class NordicsAtlasOutputEditor : UnityEditor.Editor
    {
        
        private bool previewOpen = true;
        protected override void OnHeaderGUI()
        {
            GUILayout.Label("Nordic's Atlas Output", EditorStyles.boldLabel);
        }
        public override void OnInspectorGUI()
        {
            var output = (NordicsAtlasOutput)target;
            AtlasShaderRegistry.EnsureSettings(output.configuration, AtlasShaderRegistry.Output(output.configuration.shaderId));
            serializedObject.Update();
            var config = serializedObject.FindProperty("configuration");
            var owner = output.gameObject;
            var main = owner.GetComponent<NordicsAdvancedAtlaser>();
            if (main == null) EditorGUILayout.HelpBox("Add Nordic's Advanced Atlaser to this GameObject to enable source discovery and coordination.", MessageType.Warning);
            EditorGUILayout.HelpBox("Configuration only. This output does not generate or modify any materials.", MessageType.Info);
            EditorGUILayout.PropertyField(config.FindPropertyRelative("outputName"));
            EditorGUI.BeginChangeCheck();
            DrawShaderSelection(config.FindPropertyRelative("shaderId"));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                var selected = AtlasShaderRegistry.Output(output.configuration.shaderId);
                AtlasShaderRegistry.EnsureSettings(output.configuration, selected);
                if (selected != null)
                    selected.OnConfigurationChanged(output.configuration, AtlasConfigurationChange.ShaderSelection);
                EditorUtility.SetDirty(output);
                serializedObject.Update();
                config = serializedObject.FindProperty("configuration");
            }
            // Padding separates source regions, so it only applies with two or more sources.
            bool padded = AtlasOutputLayout.AssignedSourceCount(output.configuration) > 1;
            using (new EditorGUI.DisabledScope(!padded))
                EditorGUILayout.PropertyField(config.FindPropertyRelative("regionPadding"), new GUIContent("Region Padding"));
            if (!padded)
                EditorGUILayout.LabelField("Padding is only used with two or more source materials.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Source Materials & UV Regions", EditorStyles.boldLabel);
            var sources = config.FindPropertyRelative("sources");
            var discovery = AtlasMaterialDiscovery.Discover(owner);
            var assignedElsewhere = new HashSet<Material>();
            foreach (var other in owner.GetComponents<NordicsAtlasOutput>())
            {
                if (other == output || other.configuration.sources == null) continue;
                foreach (var entry in other.configuration.sources)
                    if (entry != null && entry.material != null) assignedElsewhere.Add(entry.material);
            }
            var selectedHere = new HashSet<Material>();
            for (int i = 0; i < sources.arraySize; ++i)
            {
                var entry = sources.GetArrayElementAtIndex(i);
                var property = entry.FindPropertyRelative("material");
                var current = property.objectReferenceValue as Material;
                if (current != null) selectedHere.Add(current);
            }
            for (int i = 0; i < sources.arraySize; ++i)
            {
                var item = sources.GetArrayElementAtIndex(i);
                var materialProperty = item.FindPropertyRelative("material");
                var current = materialProperty.objectReferenceValue as Material;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                bool remove;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Source " + (i + 1), EditorStyles.boldLabel);
                    remove = GUILayout.Button("Remove", GUILayout.Width(70));
                }
                if (remove)
                {
                    // Delete the serialized list element once. Unity may clear object references
                    // on the first delete, but the list itself contains serializable classes.
                    int before = sources.arraySize;
                    sources.DeleteArrayElementAtIndex(i);
                    if (sources.arraySize == before) sources.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndVertical();
                    break;
                }
                var choices = new List<Material>();
                var labels = new List<string>();
                int selected = 0;
                if (current != null && !discovery.Materials.Contains(current))
                {
                    choices.Add(current);
                    labels.Add(current.name + " (not detected)");
                }
                else if (current != null && assignedElsewhere.Contains(current))
                {
                    choices.Add(current);
                    labels.Add(current.name + " (assigned to another output)");
                }
                else if (current != null)
                {
                    choices.Add(current);
                    labels.Add(current.name);
                }
                else
                {
                    choices.Add(null);
                    labels.Add("Select detected material...");
                }
                foreach (var material in discovery.Materials)
                {
                    if (material == current || assignedElsewhere.Contains(material) || selectedHere.Contains(material)) continue;
                    choices.Add(material);
                    labels.Add(material.name);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("Material");
                    EditorGUI.BeginDisabledGroup(choices.Count <= 1);
                    int choice = EditorGUILayout.Popup(selected, labels.ToArray());
                    EditorGUI.EndDisabledGroup();
                    if (choice != selected)
                    {
                        if (current != null) selectedHere.Remove(current);
                        materialProperty.objectReferenceValue = choices[choice];
                        if (choices[choice] != null) selectedHere.Add(choices[choice]);
                    }
                }
                EditorGUILayout.PropertyField(item.FindPropertyRelative("uvRegion"), new GUIContent("UV Region"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("normalCorrection"), new GUIContent("Normal Correction"));
                var prebaked = item.FindPropertyRelative("prebakedNormalMap");
                EditorGUILayout.PropertyField(prebaked, new GUIContent("Pre-baked Normal Map"));
                if (prebaked.objectReferenceValue is Texture2D prebakedTexture &&
                    AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(prebakedTexture)) is TextureImporter importer &&
                    importer.textureType != TextureImporterType.NormalMap)
                    EditorGUILayout.HelpBox("This texture isn't imported as a normal map. Set its Texture Type to Normal map.", MessageType.Warning);
                EditorGUILayout.EndVertical();
            }
            var available = discovery.Materials.Where(m => !assignedElsewhere.Contains(m) && !selectedHere.Contains(m)).ToList();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(available.Count == 0);
                if (GUILayout.Button("Add Source ▼"))
                {
                    // Apply pending inspector changes before opening the delayed menu.
                    serializedObject.ApplyModifiedProperties();
                    var menu = new GenericMenu();
                    foreach (var material in available)
                    {
                        var picked = material;
                        menu.AddItem(new GUIContent(picked.name), false, () => AddSource(output, picked));
                    }
                    menu.ShowAsContext();
                }
                if (GUILayout.Button("Add All Unassigned"))
                {
                    foreach (var material in available)
                    {
                        int index = sources.arraySize;
                        sources.InsertArrayElementAtIndex(index);
                        var item = sources.GetArrayElementAtIndex(index);
                        item.FindPropertyRelative("material").objectReferenceValue = material;
                        item.FindPropertyRelative("uvRegion").rectValue = new Rect(0, 0, 1, 1);
                        // New array elements copy the previous one, so set the default explicitly.
                        item.FindPropertyRelative("normalCorrection").boolValue = true;
                        item.FindPropertyRelative("prebakedNormalMap").objectReferenceValue = null;
                    }
                }
                EditorGUI.EndDisabledGroup();
            }
            if (available.Count == 0)
                EditorGUILayout.LabelField("All detected materials are assigned, or none are available.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!HasAssignedMaterials(sources) || AtlasShaderRegistry.Output(config.FindPropertyRelative("shaderId").stringValue) == null))
            {
                if (GUILayout.Button("Auto Setup Output Configuration"))
                {
                    // Commit pending source assignment, shader selection and UV edits first.
                    serializedObject.ApplyModifiedProperties();
                    AtlasOutputAutoSetup.Apply(output);
                    serializedObject.Update();
                    config = serializedObject.FindProperty("configuration");
                }
            }
            EditorGUILayout.LabelField("General Output Texture Settings", EditorStyles.boldLabel);
            var sharedMain = config.FindPropertyRelative("mainTexture");
            bool mainChanged = AtlasOutputUI.Main(sharedMain);
            if (mainChanged)
            {
                // Persist the Main settings *before* touching any secondary texture.
                // This also ensures the preview reads the newly committed values.
                serializedObject.ApplyModifiedProperties();
                serializedObject.Update();
                config = serializedObject.FindProperty("configuration");
                sharedMain = config.FindPropertyRelative("mainTexture");
                var activeShader = AtlasShaderRegistry.Output(config.FindPropertyRelative("shaderId").stringValue);
                AtlasOutputUI.NormalizeAll(config.FindPropertyRelative("settings"), sharedMain, activeShader);
                serializedObject.ApplyModifiedProperties();
                var configuredShader = AtlasShaderRegistry.Output(output.configuration.shaderId);
                if (configuredShader != null)
                {
                    configuredShader.OnConfigurationChanged(output.configuration, AtlasConfigurationChange.MainTexture);
                    EditorUtility.SetDirty(output);
                }
                serializedObject.Update();
                config = serializedObject.FindProperty("configuration");
                sharedMain = config.FindPropertyRelative("mainTexture");
            }
            AtlasOutputUI.DrawShaderFields(config.FindPropertyRelative("settings"), sharedMain,
                AtlasShaderRegistry.Output(config.FindPropertyRelative("shaderId").stringValue));
            serializedObject.ApplyModifiedProperties();
            previewOpen = EditorGUILayout.Foldout(previewOpen, "UV Layout Preview", true);
            if (previewOpen)
            {
                var dimensions = output.configuration.mainTexture;
                AtlasUVPreview.Draw(output.configuration.sources,
                    dimensions != null ? dimensions.width : 1,
                    dimensions != null ? dimensions.height : 1);
                EditorGUILayout.LabelField("Preview uses main textures; no shader-specific baking or padding is applied.", EditorStyles.wordWrappedMiniLabel);
            }
            if (main != null)
            {
                var discovered = AtlasMaterialDiscovery.Discover(owner);
                foreach (var warning in AtlasConfigurationValidator.Validate(owner, discovered))
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }
        }
        private static bool HasAssignedMaterials(SerializedProperty entries)
        {
            for (int i=0; i<entries.arraySize; i++)
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("material").objectReferenceValue != null) return true;
            return false;
        }
        private static void AddSource(NordicsAtlasOutput output, Material material)
        {
            if (output == null || material == null) return;
            var discovery = AtlasMaterialDiscovery.Discover(output.gameObject);
            if (!discovery.Materials.Contains(material)) return;
            foreach (var other in output.GetComponents<NordicsAtlasOutput>())
            {
                if (other.configuration.sources != null && other.configuration.sources.Any(e => e != null && e.material == material)) return;
            }
            Undo.RecordObject(output, "Add atlas source material");
            if (output.configuration.sources == null) output.configuration.sources = new List<AtlasSourceEntry>();
            output.configuration.sources.Add(new AtlasSourceEntry { material = material, uvRegion = new Rect(0, 0, 1, 1) });
            EditorUtility.SetDirty(output);
        }
        private void DrawShaderSelection(SerializedProperty id)
        {
            var options = AtlasShaderRegistry.OutputShaders;
            var labels = options.Select(o => o.DisplayName).ToArray();
            int index = -1;
            for (int i = 0; i < options.Count; ++i) if (options[i].ShaderId == id.stringValue) index = i;
            if (index < 0) EditorGUILayout.HelpBox("Unknown shader ID: " + id.stringValue, MessageType.Warning);
            int next = EditorGUILayout.Popup("Output Shader", Mathf.Max(0, index), labels);
            if (next >= 0 && next < options.Count && next != index) id.stringValue = options[next].ShaderId;
        }
    }
}
