using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Reusable IMGUI building blocks. No output-shader-specific UI code in the inspector.
    public static class AtlasOutputUI
    {
        private static readonly int[] Sizes = { 128, 256, 512, 1024, 2048, 4096, 8192 };
        public static void Section(string title) { EditorGUILayout.Space(); EditorGUILayout.LabelField(title, EditorStyles.boldLabel); }
        public static void Description(string text) { if (!string.IsNullOrEmpty(text)) EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel); }
        public static void Toggle(SerializedProperty value, string label) { value.boolValue = EditorGUILayout.Toggle(label, value.boolValue); }
        public static void Color(SerializedProperty value, string label) { EditorGUILayout.PropertyField(value, new GUIContent(label)); }
        public static void Value(SerializedProperty value, string label) { EditorGUILayout.PropertyField(value, new GUIContent(label)); }
        // Return true only when the user actually changed a shared Main field.
        // The inspector commits those edits before recomputing dependent textures.
        public static bool Main(SerializedProperty main)
        {
            if (main == null) return false;
            var enabled = main.FindPropertyRelative("Enabled");
            var w = main.FindPropertyRelative("Width");
            var h = main.FindPropertyRelative("Height");
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                enabled.boolValue = EditorGUILayout.ToggleLeft("Main", enabled.boolValue, GUILayout.Width(115));
                using (new EditorGUI.DisabledScope(!enabled.boolValue))
                {
                    w.intValue = SizePopup(w.intValue, Sizes);
                    h.intValue = SizePopup(h.intValue, Sizes);
                }
            }
            return EditorGUI.EndChangeCheck();
        }
        public static void DrawShaderFields(SerializedProperty allSettings, SerializedProperty main, IAtlasOutputShader shader)
        {
            if (shader == null || main == null || allSettings == null) return;
            foreach (var field in shader.Fields)
            {
                if (string.IsNullOrEmpty(field.Id)) { Section(field.Label); Description(field.Description); continue; }
                var p = Find(allSettings, field.Id);
                if (p == null) continue;
                Description(field.Description);
                switch (field.Kind)
                {
                    case AtlasSettingKind.Texture: Texture(p, main, field.Label); break;
                    case AtlasSettingKind.Toggle: Toggle(p.FindPropertyRelative("BoolValue"), field.Label); break;
                    case AtlasSettingKind.Color: Color(p.FindPropertyRelative("ColorValue"), field.Label); break;
                    case AtlasSettingKind.Integer: Value(p.FindPropertyRelative("IntValue"), field.Label); break;
                    case AtlasSettingKind.Vector: Value(p.FindPropertyRelative("VectorValue"), field.Label); break;
                    case AtlasSettingKind.Float: Value(p.FindPropertyRelative("FloatValue"), field.Label); break;
                    case AtlasSettingKind.Description: Description(field.Label); break;
                }
            }
        }
        private static SerializedProperty Find(SerializedProperty items, string id)
        {
            for (int i=0;i<items.arraySize;i++)
            {
                var p=items.GetArrayElementAtIndex(i);
                if (p.FindPropertyRelative("Id").stringValue == id) return p;
            }
            return null;
        }
        public static void NormalizeAll(SerializedProperty settings, SerializedProperty main, IAtlasOutputShader shader = null)
        {
            if (settings == null || main == null) return;
            if (shader == null) return;
            foreach (var field in shader.Fields)
            {
                if (field.Kind != AtlasSettingKind.Texture || string.IsNullOrEmpty(field.Id)) continue;
                var p = Find(settings, field.Id);
                if (p == null) continue;
                NormalizeTexture(p, main);
            }
        }
        private static void Texture(SerializedProperty item, SerializedProperty main, string label)
        {
            var tex = item.FindPropertyRelative("Texture");
            var enabled = tex.FindPropertyRelative("Enabled");
            var width = tex.FindPropertyRelative("Width");
            var height = tex.FindPropertyRelative("Height");
            var mode = (AtlasTextureSizeMode)item.FindPropertyRelative("SizeMode").enumValueIndex;
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                enabled.boolValue = EditorGUILayout.ToggleLeft(label, enabled.boolValue, GUILayout.Width(145));
                using (new EditorGUI.DisabledScope(!enabled.boolValue))
                {
                    if (mode == AtlasTextureSizeMode.MasterAspect)
                    {
                        int mw = main.FindPropertyRelative("Enabled").boolValue ? main.FindPropertyRelative("Width").intValue : 1;
                        int mh = main.FindPropertyRelative("Enabled").boolValue ? main.FindPropertyRelative("Height").intValue : 1;
                        int[] valid = Sizes.Where(w => Valid(w, mw, mh)).ToArray();
                        if (valid.Length > 0) width.intValue = SizePopup(width.intValue, valid);
                        using (new EditorGUI.DisabledScope(true))
                            EditorGUILayout.IntField(height.intValue);
                    }
                    else if (mode == AtlasTextureSizeMode.Square)
                    {
                        width.intValue = SizePopup(width.intValue, Sizes);
                        using (new EditorGUI.DisabledScope(true))
                            EditorGUILayout.IntField(height.intValue);
                    }
                    else
                    {
                        width.intValue = SizePopup(width.intValue, Sizes);
                        height.intValue = SizePopup(height.intValue, Sizes);
                    }
                }
            }
            // Don't normalize on Layout/Repaint: those are not user edits.
            // Otherwise an unchanged/aborted Main edit can mutate secondary widths.
            if (EditorGUI.EndChangeCheck()) NormalizeTexture(item, main);
        }
        private static void NormalizeTexture(SerializedProperty item, SerializedProperty main)
        {
            var tex=item.FindPropertyRelative("Texture"); if(tex==null) return;
            var w=tex.FindPropertyRelative("Width"); var h=tex.FindPropertyRelative("Height");
            var mode=(AtlasTextureSizeMode)item.FindPropertyRelative("SizeMode").enumValueIndex;
            if(mode == AtlasTextureSizeMode.MasterAspect)
            {
                int mw=main.FindPropertyRelative("Enabled").boolValue ? main.FindPropertyRelative("Width").intValue : 1;
                int mh=main.FindPropertyRelative("Enabled").boolValue ? main.FindPropertyRelative("Height").intValue : 1;
                int[] valid=Sizes.Where(v=>Valid(v,mw,mh)).ToArray();
                if(valid.Length > 0) {w.intValue=Closest(w.intValue,valid); h.intValue=(int)((long)w.intValue*mh/mw);}
            }
            else if(mode==AtlasTextureSizeMode.Square) {w.intValue=Closest(w.intValue,Sizes);h.intValue=w.intValue;}
            else {w.intValue=Closest(w.intValue,Sizes);h.intValue=Closest(h.intValue,Sizes);}
        }
        private static bool Valid(int w,int mw,int mh)
        {
            if (mw <= 0 || mh <= 0) return false;
            long scaled=(long)w*mh; if (scaled%mw!=0) return false;
            long h=scaled/mw;
            return h>=Sizes[0] && h<=Sizes[Sizes.Length-1] && (h&(h-1))==0;
        }
        private static int Closest(int value,int[] options)
        { return options.OrderBy(v=>Math.Abs((long)v-value)).FirstOrDefault(); }
        private static int SizePopup(int current,int[] options)
        {
            if(options.Length==0) return current;
            int index=Array.IndexOf(options,current);
            int next=EditorGUILayout.Popup(Math.Max(index,0),options.Select(v=>v.ToString()).ToArray());
            return options[Mathf.Clamp(next,0,options.Length-1)];
        }
    }
}
