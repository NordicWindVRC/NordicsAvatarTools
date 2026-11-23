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
            var owner = ((NordicsAdvancedAtlaser)target).gameObject;
            var discovery = AtlasMaterialDiscovery.Cached(owner);
            var outputs = owner.GetComponents<NordicsAtlasOutput>();
            EditorGUILayout.LabelField("Source Mode", discovery.HasAAO ? "AAO Merge Skinned Mesh" : "Local SkinnedMeshRenderer");
            EditorGUILayout.LabelField("Unique Source Materials", discovery.Materials.Count.ToString());
            var assignment = new Dictionary<Material, string>();
            foreach (var output in outputs)
            {
                if (output.configuration.sources != null)
                    foreach (var source in output.configuration.sources)
                        if (source != null && source.material != null && !assignment.ContainsKey(source.material))
                            assignment.Add(source.material, output.configuration.outputName);
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
            GeometryBaking();
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output Components", EditorStyles.boldLabel);
            foreach (var output in outputs)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(string.IsNullOrEmpty(output.configuration.outputName) ? "(Unnamed)" : output.configuration.outputName);
                }
            }
            if (GUILayout.Button("Add Atlas Output"))
                AtlasOutputShaderPick.ApplyIfUnset(Undo.AddComponent<NordicsAtlasOutput>(owner));
            foreach (var warning in AtlasMaterialDiscovery.Warnings(owner)) EditorGUILayout.HelpBox(warning, MessageType.Warning);
            if (discovery.Materials.Count == 0 && string.IsNullOrEmpty(discovery.Warning))
                EditorGUILayout.HelpBox("No source materials detected.", MessageType.Info);
        }

        private void GeometryBaking()
        {
            var enabled = serializedObject.FindProperty("enableGeometryBaking");
            var excluded = serializedObject.FindProperty("excludedBlendShapes");
            var hidden = serializedObject.FindProperty("excludeHiddenGeometry");
            if (!AtlasOutputUI.Fold("atlaser.geometry", "Geometry Baking", true, true)) return;
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(enabled, new GUIContent("Enable Geometry Baking",
                "Bakes a normal map that brings back the shading lost when a mesh is simplified during the build."));
            if (enabled.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(hidden, new GUIContent("Exclude Hidden Geometry (Depth Test)",
                    "Leaves out surfaces hidden behind another one in the same texture area, such as body parts inside " +
                    "the mesh, so the bake follows what is visible."));
                EditorGUILayout.LabelField("Excluded Blend Shapes", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Parts moved by these blend shapes are left out of the bake. Use it for parts you " +
                    "hide on purpose, such as clothing shrunk away by a blend shape.", EditorStyles.wordWrappedMiniLabel);
                var discoveredRenderers = AtlasMaterialDiscovery.Cached(((NordicsAdvancedAtlaser)target).gameObject).Renderers;
                {
                    for (int i = 0; i < excluded.arraySize; ++i)
                    {
                        var entry = excluded.GetArrayElementAtIndex(i);
                        var rendererProp = entry.FindPropertyRelative("renderer");
                        var shapeProp = entry.FindPropertyRelative("blendShapeName");
                        var thresholdProp = entry.FindPropertyRelative("threshold");
                        var modeProp = entry.FindPropertyRelative("selectionMode");
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
                            EditorGUILayout.PropertyField(thresholdProp, new GUIContent("Threshold", "How far a vertex must move to count as moved by the blend shape."));
                            if (thresholdProp.floatValue < 0f) thresholdProp.floatValue = 0f;
                            EditorGUILayout.PropertyField(modeProp, new GUIContent("Selection Mode", "Any Vertex: a triangle is left out when one of its corners moves. All Vertices: only when all three move."));
                        }
                    }
                    if (GUILayout.Button("Add Blend Shape"))
                    {
                        int index = excluded.arraySize;
                        excluded.InsertArrayElementAtIndex(index);
                        var entry = excluded.GetArrayElementAtIndex(index);
                        entry.FindPropertyRelative("renderer").objectReferenceValue = null;
                        entry.FindPropertyRelative("blendShapeName").stringValue = "";
                        entry.FindPropertyRelative("threshold").floatValue = 0.001f;
                        entry.FindPropertyRelative("selectionMode").enumValueIndex = 0;
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
        }
    }
}
