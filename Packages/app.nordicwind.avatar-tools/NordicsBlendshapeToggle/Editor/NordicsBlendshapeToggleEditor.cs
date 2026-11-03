using System;
using UnityEditor;
using UnityEngine;

namespace NordicWind.BlendshapeToggle.Editor
{
    [CustomEditor(typeof(NordicsBlendshapeToggle))]
    internal sealed class NordicsBlendshapeToggleEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var toggle = (NordicsBlendshapeToggle)target;
            var renderer = toggle.GetComponent<SkinnedMeshRenderer>();
            var slot = serializedObject.FindProperty("materialSlot");
            if (renderer && renderer.sharedMesh)
            {
                int count = Math.Min(renderer.sharedMesh.subMeshCount, renderer.sharedMaterials.Length);
                var labels = new string[count + 1];
                labels[0] = "Select material slot";
                for (int i = 0; i < count; i++)
                    labels[i + 1] = $"{i} — {(renderer.sharedMaterials[i] ? renderer.sharedMaterials[i].name : "(Missing material)")}";
                int current = slot.intValue >= 0 && slot.intValue < count ? slot.intValue + 1 : 0;
                EditorGUI.BeginChangeCheck();
                int chosen = EditorGUILayout.Popup("Material Slot", current, labels);
                if (EditorGUI.EndChangeCheck()) slot.intValue = chosen - 1;
            }
            else EditorGUILayout.PropertyField(slot);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("blendshapeName"), new GUIContent("Blendshape Name"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("reverseBlendshape"), new GUIContent("Reverse Blendshape"));
            var mode = serializedObject.FindProperty("centerMode");
            EditorGUILayout.PropertyField(mode, new GUIContent("Center Mode"));
            if (mode.enumValueIndex == (int)ToggleCenterMode.Transform)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("targetTransform"), new GUIContent("Target Transform"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("collapsedScale"), new GUIContent("Collapsed Scale"));
            serializedObject.ApplyModifiedProperties();
            if (!renderer)
            {
                EditorGUILayout.HelpBox("Nordic's Blendshape Toggle requires a Skinned Mesh Renderer on this GameObject.", MessageType.Warning);
                return;
            }
            try
            {
                var geometry = ToggleGeometry.Validate(toggle);
                foreach (var other in toggle.GetComponents<NordicsBlendshapeToggle>())
                    if (other != toggle && other.enabled && toggle.enabled && other.blendshapeName == toggle.blendshapeName)
                        throw new InvalidOperationException("Another enabled toggle on this renderer uses the same blendshape name.");
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Selected Geometry", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Triangles", geometry.TriangleCount.ToString("N0"));
                EditorGUILayout.LabelField("Vertices Affected", geometry.Indices.Count.ToString("N0"));
                EditorGUILayout.Vector3Field("Center (Mesh Local)", geometry.Center);
            }
            catch (Exception ex) { EditorGUILayout.HelpBox(ex.Message, MessageType.Error); }
            if (toggle.collapsedScale == 0)
                EditorGUILayout.HelpBox("Scale 0 produces degenerate triangles at weight 100. A small nonzero scale is recommended.", MessageType.Warning);
            EditorGUILayout.HelpBox(toggle.reverseBlendshape
                ? "Generated during build: weight 0 = collapsed, 100 = visible."
                : "Generated during build: weight 0 = visible, 100 = collapsed.", MessageType.Info);
        }
    }
}
