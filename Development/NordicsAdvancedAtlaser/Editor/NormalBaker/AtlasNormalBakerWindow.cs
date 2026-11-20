using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>Dockable window for baking a normal map from an original onto a current mesh.</summary>
    internal sealed class AtlasNormalBakerWindow : EditorWindow
    {
        private static readonly int[] Sizes = { 512, 1024, 2048, 4096 };

        // Kept while the window is open; not saved with the scene or project.
        [SerializeField] private Object original;
        [SerializeField] private Object current;
        [SerializeField] private int originalSubmesh;
        [SerializeField] private int currentSubmesh;
        [SerializeField] private int width = 4096;
        [SerializeField] private int height = 4096;
        [SerializeField] private float maxAngle = AtlasNormalBaker.DefaultMaxAngle;
        [SerializeField] private string summary;
        [SerializeField] private bool summaryIsWarning;
        private Vector2 scroll;

        [MenuItem("Tools/NordicWind/Advanced Atlaser/Normal Baker")]
        private static void Open() => GetWindow<AtlasNormalBakerWindow>("Normal Baker").Show();

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("Bakes the shading of an original mesh onto a current, for example lower-detail, mesh " +
                "as a normal map. Both must use the same UV layout. Use the result as a source's Pre-baked Normal Map.",
                MessageType.Info);

            var originalSource = Slot("Original", "The detailed mesh whose shading you want.", ref original, ref originalSubmesh);
            var currentSource = Slot("Current", "The mesh the normal map is for.", ref current, ref currentSubmesh);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Size");
                width = SizePopup(width);
                GUILayout.Label("×", GUILayout.Width(12));
                height = SizePopup(height);
            }
            maxAngle = EditorGUILayout.Slider(new GUIContent("Max Correction Angle",
                "Corrections above half of this are compressed smoothly toward it: large ones more likely come from a mismatch than from real shape."),
                maxAngle, 10f, 90f);

            string orientation = AtlasNormalBaker.OrientationWarning(originalSource, currentSource);
            if (orientation != null) EditorGUILayout.HelpBox(orientation, MessageType.Warning);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!originalSource.Valid || !currentSource.Valid))
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Generate…", GUILayout.Width(120))) Generate(originalSource, currentSource);
            }
            if (!string.IsNullOrEmpty(summary))
                EditorGUILayout.HelpBox(summary, summaryIsWarning ? MessageType.Warning : MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        private static AtlasNormalBaker.Source Slot(string label, string tooltip, ref Object slot, ref int submesh)
        {
            EditorGUILayout.Space();
            slot = EditorGUILayout.ObjectField(new GUIContent(label, tooltip + " Drag in a mesh, a model, or a scene object " +
                "with a Skinned Mesh Renderer or Mesh Renderer."), slot, typeof(Object), true);
            var source = AtlasNormalBaker.Resolve(slot);
            if (source.Error != null) { EditorGUILayout.HelpBox(source.Error, MessageType.Error); return source; }
            if (!source.Valid) return source;
            var labels = AtlasNormalBaker.SubmeshLabels(source);
            submesh = Mathf.Clamp(submesh, 0, Math.Max(0, labels.Length - 1));
            EditorGUI.indentLevel++;
            submesh = EditorGUILayout.Popup("Submesh", submesh, labels);
            EditorGUILayout.LabelField(source.InScene ? "Scene object: its transform and current pose are used."
                : "Mesh asset: used as it is, without a transform or pose.", EditorStyles.wordWrappedMiniLabel);
            EditorGUI.indentLevel--;
            return source;
        }

        private static int SizePopup(int value)
        {
            int index = Math.Max(0, Array.IndexOf(Sizes, value));
            if (Array.IndexOf(Sizes, value) < 0) index = Sizes.Length - 1;
            return Sizes[EditorGUILayout.Popup(index, Sizes.Select(s => s.ToString()).ToArray(), GUILayout.Width(70))];
        }

        private void Generate(AtlasNormalBaker.Source originalSource, AtlasNormalBaker.Source currentSource)
        {
            summaryIsWarning = true;
            if (!AtlasNormalBaker.TryCapture(originalSource, originalSubmesh, out var originalSection, out string error) ||
                !AtlasNormalBaker.TryCapture(currentSource, currentSubmesh, out var currentSection, out error))
            {
                summary = error;
                return;
            }
            AtlasNormalBaker.Result result = null;
            try
            {
                EditorUtility.DisplayProgressBar("Normal Baker", "Baking the normal map…", .5f);
                result = AtlasNormalBaker.Bake(originalSection, currentSection, width, height, maxAngle);
            }
            catch (Exception e)
            {
                summary = "The bake failed: " + e.Message;
                Debug.LogException(e);
                return;
            }
            finally { EditorUtility.ClearProgressBar(); }
            using (result)
            {
                summary = result.Summary;
                if (!result.UVsMatch) return;
                string name = currentSource.Mesh.name + "_" + currentSubmesh + "_Normal";
                string path = EditorUtility.SaveFilePanel("Save Normal Map", AtlasNormalBaker.StartFolder(), name, "png");
                if (string.IsNullOrEmpty(path)) { summary += " Not saved."; return; }
                AtlasNormalBaker.SavePng(result.Map, path);
                summaryIsWarning = false;
                string assetPath = AtlasNormalBaker.AssetPath(path);
                summary = result.Summary + (assetPath != null ? " Saved to " + assetPath + " as a normal map."
                    : " Saved to " + path + ". It's outside Assets, so import it as a normal map yourself.");
                if (assetPath != null) EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath));
            }
        }
    }
}
