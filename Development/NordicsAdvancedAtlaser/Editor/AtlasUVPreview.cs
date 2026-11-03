using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace NordicWind.AdvancedAtlaser.Editor
{
    internal static class AtlasUVPreview
    {
        internal static void Draw(List<AtlasSourceEntry> entries, int textureWidth, int textureHeight)
        {
            // Fit the full texture rectangle within both bounds while preserving aspect.
            float aspect = textureHeight > 0 ? (float)textureWidth / textureHeight : 1f;
            aspect = Mathf.Max(0.0001f, aspect);
            float maxWidth = Mathf.Min(Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 48f), 420f);
            float maxHeight = 360f;
            float width = Mathf.Min(maxWidth, maxHeight * aspect);
            float height = width / aspect;
            Rect preview = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));
            EditorGUI.DrawRect(preview, new Color(0.16f, 0.16f, 0.16f));
            if (entries == null) return;
            var ordered = new List<AtlasSourceEntry>();
            foreach (var entry in entries) if (entry != null && entry.Material != null) ordered.Add(entry);
            // Large first; smaller regions paint over larger regions. Stable insertion order for equal areas.
            ordered.Sort((a, b) => (b.UVRegion.width * b.UVRegion.height).CompareTo(a.UVRegion.width * a.UVRegion.height));
            foreach (var entry in ordered)
            {
                var uv = entry.UVRegion;
                Rect r = new Rect(preview.x + uv.x * preview.width,
                    preview.y + (1f - uv.yMax) * preview.height,
                    uv.width * preview.width, uv.height * preview.height);
                if (r.width <= 0 || r.height <= 0) continue;
                var tex = GetMainTexture(entry.Material);
                if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, false);
                else EditorGUI.DrawRect(r, new Color(.24f, .43f, .60f, 1f));
                Handles.BeginGUI();
                Handles.color = new Color(.95f, .75f, .2f, 1f);
                Handles.DrawSolidRectangleWithOutline(r, Color.clear, Handles.color);
                Handles.EndGUI();
                if (r.width > 48 && r.height > 18)
                    GUI.Label(new Rect(r.x + 3, r.y + 2, r.width - 6, 18), entry.Material.name, EditorStyles.whiteMiniLabel);
            }
            Handles.BeginGUI();
            Handles.color = Color.gray;
            Handles.DrawSolidRectangleWithOutline(preview, Color.clear, Handles.color);
            Handles.EndGUI();
        }
        private static Texture GetMainTexture(Material material)
        {
            foreach (var name in new[] { "_MainTex", "_BaseMap", "_BaseColorMap" })
                if (material.HasProperty(name))
                {
                    var tex = material.GetTexture(name);
                    if (tex != null) return tex;
                }
            return material.mainTexture;
        }
    }
}
