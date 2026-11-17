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
                if (target is IList list)
                {
                    list.Clear();
                    if (value is IList items) foreach (var item in items) list.Add(item);
                }
                else Copy(value, target);
            }
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
