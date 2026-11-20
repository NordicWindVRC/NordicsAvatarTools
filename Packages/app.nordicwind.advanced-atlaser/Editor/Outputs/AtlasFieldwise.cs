using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Field-by-field copying and comparing of unified material parts: plain classes of public
    // fields, lists, structs and Unity objects (by reference).
    public static class AtlasFieldwise
    {
        private const BindingFlags Fields = BindingFlags.Public | BindingFlags.Instance;

        /// <summary>Copies every public field of <paramref name="from"/> into <paramref name="into"/>:
        /// values and references as they are; read-only parts (nested classes, lists) field by
        /// field, lists by their items.</summary>
        public static void Copy(object from, object into)
        {
            if (from == null || into == null) return;
            foreach (var field in from.GetType().GetFields(Fields))
            {
                var value = field.GetValue(from);
                if (!field.IsInitOnly)
                {
                    field.SetValue(into, value);
                    continue;
                }
                var target = field.GetValue(into);
                // A fixed-size array: its elements in place (objects of the model field by field).
                if (target is Array array && value is Array source)
                {
                    for (int i = 0; i < Math.Min(source.Length, array.Length); i++)
                    {
                        var item = source.GetValue(i);
                        var existing = array.GetValue(i);
                        if (item != null && existing != null && !item.GetType().IsValueType && !(item is UnityEngine.Object) && !(item is string))
                            Copy(item, existing);
                        else array.SetValue(item, i);
                    }
                    continue;
                }
                if (target is IList list)
                {
                    list.Clear();
                    if (value is IList items) foreach (var item in items) list.Add(item);
                }
                else Copy(value, target);
            }
        }

        /// <summary>A text describing the object's public fields, equal for objects that compare
        /// equal (floats to 4 decimals, assets by GUID, other Unity objects by instance). <paramref name="skip"/> leaves
        /// out top-level fields by name.</summary>
        public static string Signature(object value, params string[] skip)
        {
            var text = new System.Text.StringBuilder();
            Write(value, text, skip, true);
            return text.ToString();
        }

        private static void Write(object value, System.Text.StringBuilder text, string[] skip, bool top)
        {
            if (value == null) { text.Append('∅'); return; }
            var type = value.GetType();
            if (value is float f) { text.Append(f.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)); return; }
            if (value is UnityEngine.Object o)
            {
                // Assets by GUID and file ID, so the text stays the same between sessions.
                if (o != null && UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string guid, out long file))
                    text.Append('#').Append(guid).Append(':').Append(file);
                else text.Append('#').Append(o != null ? o.GetInstanceID() : 0);
                return;
            }
            if (type.IsPrimitive || type.IsEnum || value is string) { text.Append(value); return; }
            if (value is IList list)
            {
                text.Append('[');
                foreach (var item in list) { Write(item, text, skip, false); text.Append(','); }
                text.Append(']');
                return;
            }
            text.Append('{');
            foreach (var field in type.GetFields(Fields))
            {
                if (top && Array.IndexOf(skip, field.Name) >= 0) continue;
                text.Append(field.Name).Append('=');
                Write(field.GetValue(value), text, skip, false);
                text.Append(';');
            }
            text.Append('}');
        }

        /// <summary>The paths of the fields that differ (floats within <paramref name="tolerance"/>).</summary>
        public static List<string> Differences(object a, object b, float tolerance = 1e-3f)
        {
            var paths = new List<string>();
            Compare(a, b, "", tolerance, paths);
            return paths;
        }

        private static void Compare(object a, object b, string path, float tolerance, List<string> paths)
        {
            if (ReferenceEquals(a, b)) return;
            if (a == null || b == null || a.GetType() != b.GetType())
            {
                paths.Add(path);
                return;
            }
            var type = a.GetType();
            if (a is float fa)
            {
                if (Mathf.Abs(fa - (float)b) > tolerance) paths.Add(path);
                return;
            }
            if (type.IsPrimitive || type.IsEnum || a is string || a is UnityEngine.Object)
            {
                if (!a.Equals(b)) paths.Add(path);
                return;
            }
            if (a is IList la)
            {
                var lb = (IList)b;
                if (la.Count != lb.Count)
                {
                    paths.Add(path);
                    return;
                }
                for (int i = 0; i < la.Count; i++) Compare(la[i], lb[i], $"{path}[{i}]", tolerance, paths);
                return;
            }
            foreach (var field in type.GetFields(Fields))
                Compare(field.GetValue(a), field.GetValue(b), path.Length == 0 ? field.Name : path + "." + field.Name, tolerance, paths);
        }
    }
}
