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
            AtlasShaderRegistry.EnsureSettings(output.Configuration, AtlasShaderRegistry.Output(output.Configuration.ShaderId));
            serializedObject.Update();
            var config = serializedObject.FindProperty("Configuration");
            var owner = output.gameObject;
            var main = owner.GetComponent<NordicsAdvancedAtlaser>();
            if (main == null) EditorGUILayout.HelpBox("Add Nordic's Advanced Atlaser to this GameObject to enable source discovery and coordination.", MessageType.Warning);
            EditorGUILayout.HelpBox("Configuration only. This output does not generate or modify any materials.", MessageType.Info);
            EditorGUILayout.PropertyField(config.FindPropertyRelative("OutputName"));
            EditorGUI.BeginChangeCheck();
            DrawShaderSelection(config.FindPropertyRelative("ShaderId"));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                var selected = AtlasShaderRegistry.Output(output.Configuration.ShaderId);
                AtlasShaderRegistry.EnsureSettings(output.Configuration, selected);
                if (selected != null)
                    selected.OnConfigurationChanged(output.Configuration, AtlasConfigurationChange.ShaderSelection);
                EditorUtility.SetDirty(output);
                serializedObject.Update();
                config = serializedObject.FindProperty("Configuration");
            }
            EditorGUILayout.PropertyField(config.FindPropertyRelative("RegionPadding"));
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Source Materials & UV Regions", EditorStyles.boldLabel);
            var sources = config.FindPropertyRelative("Sources");
            var discovery = AtlasMaterialDiscovery.Discover(owner);
            var assignedElsewhere = new HashSet<Material>();
            foreach (var other in owner.GetComponents<NordicsAtlasOutput>())
            {
                if (other == output || other.Configuration.Sources == null) continue;
                foreach (var entry in other.Configuration.Sources)
                    if (entry != null && entry.Material != null) assignedElsewhere.Add(entry.Material);
            }
            var selectedHere = new HashSet<Material>();
            for (int i = 0; i < sources.arraySize; ++i)
            {
                var entry = sources.GetArrayElementAtIndex(i);
                var property = entry.FindPropertyRelative("Material");
                var current = property.objectReferenceValue as Material;
                if (current != null) selectedHere.Add(current);
            }
            for (int i = 0; i < sources.arraySize; ++i)
            {
                var item = sources.GetArrayElementAtIndex(i);
                var materialProperty = item.FindPropertyRelative("Material");
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
                EditorGUILayout.PropertyField(item.FindPropertyRelative("UVRegion"), new GUIContent("UV Region"));
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
                        item.FindPropertyRelative("Material").objectReferenceValue = material;
                        item.FindPropertyRelative("UVRegion").rectValue = new Rect(0, 0, 1, 1);
                    }
                }
                EditorGUI.EndDisabledGroup();
            }
            if (available.Count == 0)
                EditorGUILayout.LabelField("All detected materials are assigned, or none are available.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!HasAssignedMaterials(sources) || AtlasShaderRegistry.Output(config.FindPropertyRelative("ShaderId").stringValue) == null))
            {
                if (GUILayout.Button("Auto Setup Output Configuration"))
                {
                    // Commit pending source assignment, shader selection and UV edits first.
                    serializedObject.ApplyModifiedProperties();
                    AtlasOutputAutoSetup.Apply(output);
                    serializedObject.Update();
                    config = serializedObject.FindProperty("Configuration");
                }
            }
            EditorGUILayout.LabelField("General Output Texture Settings", EditorStyles.boldLabel);
            var sharedMain = config.FindPropertyRelative("MainTexture");
            bool mainChanged = AtlasOutputUI.Main(sharedMain);
            if (mainChanged)
            {
                // Persist the Main settings *before* touching any secondary texture.
                // This also ensures the preview reads the newly committed values.
                serializedObject.ApplyModifiedProperties();
                serializedObject.Update();
                config = serializedObject.FindProperty("Configuration");
                sharedMain = config.FindPropertyRelative("MainTexture");
                var activeShader = AtlasShaderRegistry.Output(config.FindPropertyRelative("ShaderId").stringValue);
                AtlasOutputUI.NormalizeAll(config.FindPropertyRelative("Settings"), sharedMain, activeShader);
                serializedObject.ApplyModifiedProperties();
                var configuredShader = AtlasShaderRegistry.Output(output.Configuration.ShaderId);
                if (configuredShader != null)
                {
                    configuredShader.OnConfigurationChanged(output.Configuration, AtlasConfigurationChange.MainTexture);
                    EditorUtility.SetDirty(output);
                }
                serializedObject.Update();
                config = serializedObject.FindProperty("Configuration");
                sharedMain = config.FindPropertyRelative("MainTexture");
            }
            AtlasOutputUI.DrawShaderFields(config.FindPropertyRelative("Settings"), sharedMain,
                AtlasShaderRegistry.Output(config.FindPropertyRelative("ShaderId").stringValue));
            serializedObject.ApplyModifiedProperties();
            previewOpen = EditorGUILayout.Foldout(previewOpen, "UV Layout Preview", true);
            if (previewOpen)
            {
                var dimensions = output.Configuration.MainTexture;
                AtlasUVPreview.Draw(output.Configuration.Sources,
                    dimensions != null && dimensions.Enabled ? dimensions.Width : 1,
                    dimensions != null && dimensions.Enabled ? dimensions.Height : 1);
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
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("Material").objectReferenceValue != null) return true;
            return false;
        }
        private static void AddSource(NordicsAtlasOutput output, Material material)
        {
            if (output == null || material == null) return;
            var discovery = AtlasMaterialDiscovery.Discover(output.gameObject);
            if (!discovery.Materials.Contains(material)) return;
            foreach (var other in output.GetComponents<NordicsAtlasOutput>())
            {
                if (other.Configuration.Sources != null && other.Configuration.Sources.Any(e => e != null && e.Material == material)) return;
            }
            Undo.RecordObject(output, "Add atlas source material");
            if (output.Configuration.Sources == null) output.Configuration.Sources = new List<AtlasSourceEntry>();
            output.Configuration.Sources.Add(new AtlasSourceEntry { Material = material, UVRegion = new Rect(0, 0, 1, 1) });
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
