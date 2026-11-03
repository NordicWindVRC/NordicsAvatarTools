using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace NordicWind.AdvancedAtlaser.Editor
{
    [CustomEditor(typeof(NordicsAdvancedAtlaser))]
    internal sealed class NordicsAdvancedAtlaserEditor : UnityEditor.Editor
    {
        protected override void OnHeaderGUI()
        {
            GUILayout.Label("Nordic's Advanced Atlaser", EditorStyles.boldLabel);
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var enabled = serializedObject.FindProperty("EnableGeometryBaking");
            var resolution = serializedObject.FindProperty("GeometryBakingResolution");
            EditorGUILayout.LabelField("Geometry Baking", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(enabled, new GUIContent("Enable Geometry Baking"));
            using (new EditorGUI.DisabledScope(!enabled.boolValue))
            {
                resolution.intValue = EditorGUILayout.IntPopup("Resolution", resolution.intValue,
                    new[] {"256 x 256", "512 x 512", "1024 x 1024", "2048 x 2048", "4096 x 4096", "8192 x 8192"},
                    new[] {256, 512, 1024, 2048, 4096, 8192});
            }
            EditorGUILayout.LabelField("Square source UV-space maps shared by all outputs; independent of output texture dimensions.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Blend Shape Geometry Exclusion", EditorStyles.boldLabel);
            var ignore = serializedObject.FindProperty("IgnoreGeometryAffectedByBlendShape");
            var excluded = serializedObject.FindProperty("ExcludedBlendShapes");
            ignore.boolValue = EditorGUILayout.ToggleLeft("Exclude Blend Shape Geometry", ignore.boolValue);
            if (ignore.boolValue)
            {
                EditorGUILayout.HelpBox("Ignore mesh parts affected by selected blend shapes when creating normal corrections. Useful when mesh optimization tools remove those parts. This does not change your avatar's mesh.", MessageType.Info);
                var discoveredRenderers = AtlasMaterialDiscovery.Discover(((NordicsAdvancedAtlaser)target).gameObject).Renderers;
                using (new EditorGUI.DisabledScope(!enabled.boolValue))
                {
                    for (int i = 0; i < excluded.arraySize; ++i)
                    {
                        var entry = excluded.GetArrayElementAtIndex(i);
                        var rendererProp = entry.FindPropertyRelative("Renderer");
                        var shapeProp = entry.FindPropertyRelative("BlendShapeName");
                        var thresholdProp = entry.FindPropertyRelative("Threshold");
                        var modeProp = entry.FindPropertyRelative("SelectionMode");
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                EditorGUILayout.LabelField("Exclusion " + (i + 1), EditorStyles.boldLabel);
                                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                                {
                                    excluded.DeleteArrayElementAtIndex(i);
                                    break;
                                }
                            }
                            var renderer = rendererProp.objectReferenceValue as SkinnedMeshRenderer;
                            var labels = new List<string> { "(Select source renderer)" };
                            int selectedRenderer = 0;
                            for (int j = 0; j < discoveredRenderers.Count; ++j)
                            {
                                var candidate = discoveredRenderers[j];
                                labels.Add(candidate.name + " (" + AtlasOriginalGeometryCapture.GetPath(
                                    ((NordicsAdvancedAtlaser)target).transform, candidate.transform) + ")");
                                if (candidate == renderer) selectedRenderer = j + 1;
                            }
                            if (renderer != null && selectedRenderer == 0)
                                EditorGUILayout.HelpBox("Previously selected renderer is no longer among the detected Atlaser sources.", MessageType.Warning);
                            int choice = EditorGUILayout.Popup("Source Renderer", selectedRenderer, labels.ToArray());
                            if (choice != selectedRenderer)
                            {
                                renderer = choice == 0 ? null : discoveredRenderers[choice - 1];
                                rendererProp.objectReferenceValue = renderer;
                                shapeProp.stringValue = "";
                            }
                            var mesh = renderer != null ? renderer.sharedMesh : null;
                            var shapes = new List<string> { "(Select blend shape)" };
                            int selectedShape = 0;
                            if (mesh != null)
                                for (int j = 0; j < mesh.blendShapeCount; ++j)
                                {
                                    var name = mesh.GetBlendShapeName(j);
                                    shapes.Add(name);
                                    if (name == shapeProp.stringValue) selectedShape = j + 1;
                                }
                            int shapeChoice = EditorGUILayout.Popup("Blend Shape", selectedShape, shapes.ToArray());
                            if (shapeChoice != selectedShape)
                                shapeProp.stringValue = shapeChoice == 0 ? "" : shapes[shapeChoice];
                            if (!string.IsNullOrEmpty(shapeProp.stringValue) && selectedShape == 0)
                                EditorGUILayout.HelpBox("The selected blend shape is missing from this renderer.", MessageType.Warning);
                            EditorGUILayout.PropertyField(thresholdProp, new GUIContent("Threshold", "Minimum vertex position movement (mesh-local units) for exclusion."));
                            if (thresholdProp.floatValue < 0f) thresholdProp.floatValue = 0f;
                            EditorGUILayout.PropertyField(modeProp, new GUIContent("Selection Mode", "Any Vertex excludes a triangle when one vertex qualifies; All Vertices requires all three."));
                        }
                    }
                    if (GUILayout.Button("Add Blend Shape"))
                    {
                        int index = excluded.arraySize;
                        excluded.InsertArrayElementAtIndex(index);
                        var entry = excluded.GetArrayElementAtIndex(index);
                        entry.FindPropertyRelative("Renderer").objectReferenceValue = null;
                        entry.FindPropertyRelative("BlendShapeName").stringValue = "";
                        entry.FindPropertyRelative("Threshold").floatValue = 0.001f;
                        entry.FindPropertyRelative("SelectionMode").enumValueIndex = 0;
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space();
            var owner = ((NordicsAdvancedAtlaser)target).gameObject;
            var discovery = AtlasMaterialDiscovery.Discover(owner);
            var outputs = owner.GetComponents<NordicsAtlasOutput>();
            EditorGUILayout.HelpBox("Configuration-only iteration. No textures, materials, meshes or GameObjects are generated.", MessageType.Info);
            EditorGUILayout.LabelField("Source Mode", discovery.HasAAO ? "AAO Merge Skinned Mesh" : "Local SkinnedMeshRenderer");
            EditorGUILayout.LabelField("Unique Source Materials", discovery.Materials.Count.ToString());
            var assignment = new Dictionary<Material, string>();
            foreach (var output in outputs)
            {
                if (output.Configuration.Sources != null)
                    foreach (var source in output.Configuration.Sources)
                        if (source != null && source.Material != null && !assignment.ContainsKey(source.Material))
                            assignment.Add(source.Material, output.Configuration.OutputName);
            }
            foreach (var material in discovery.Materials)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.ObjectField(material, typeof(Material), false);
                    EditorGUI.EndDisabledGroup();
                    string outputName;
                    EditorGUILayout.LabelField(AtlasShaderRegistry.Detect(material)?.DisplayName ?? "Unknown", GUILayout.Width(190));
                    EditorGUILayout.LabelField(assignment.TryGetValue(material, out outputName) ? outputName : "Passthrough", GUILayout.Width(110));
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output Components", EditorStyles.boldLabel);
            foreach (var output in outputs)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(string.IsNullOrEmpty(output.Configuration.OutputName) ? "(Unnamed)" : output.Configuration.OutputName);
                }
            }
            if (GUILayout.Button("Add Atlas Output")) Undo.AddComponent<NordicsAtlasOutput>(owner);
            foreach (var warning in AtlasConfigurationValidator.Validate(owner, discovery)) EditorGUILayout.HelpBox(warning, MessageType.Warning);
            if (discovery.Materials.Count == 0 && string.IsNullOrEmpty(discovery.Warning))
                EditorGUILayout.HelpBox("No source materials detected.", MessageType.Info);
        }
    }
}
