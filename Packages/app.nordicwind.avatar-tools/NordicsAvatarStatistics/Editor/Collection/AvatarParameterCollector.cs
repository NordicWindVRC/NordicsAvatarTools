using System;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AvatarStatistics.Editor
{
    // Reflection keeps the statistics package compatible with older avatar SDK versions.
    internal static class AvatarParameterCollector
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static void Collect(GameObject avatarRoot, AvatarStatisticsReport report)
        {
            if (avatarRoot == null) return;
            report.ExpressionParametersAvailable = false;
            report.SyncedParameterBits = 0;
            report.CustomParameterCount = 0;
            report.ParameterWidthUnknownCount = 0;
            Component descriptor = null;
            foreach (var component in avatarRoot.GetComponentsInChildren<Component>(true))
            {
                if (component != null && component.GetType().Name == "VRCAvatarDescriptor")
                {
                    descriptor = component;
                    break;
                }
            }
            if (descriptor == null) return;
            var asset = Member(descriptor, "expressionParameters");
            if (asset == null) return;
            var parameters = Member(asset, "parameters") as IEnumerable;
            if (parameters == null) return;
            report.ExpressionParametersAvailable = true;
            foreach (var parameter in parameters)
            {
                if (parameter == null) continue;
                report.CustomParameterCount++;
                var synced = Member(parameter, "networkSynced");
                if (!(synced is bool) || !(bool)synced) continue;
                var type = Member(parameter, "valueType");
                var typeName = type != null ? type.ToString() : "";
                if (typeName == "Bool") {
                    report.SyncedParameterBits++;
                    continue;
                }
                if (typeName != "Int" && typeName != "Float") continue;
                // SDK 3.10.6+ exposes GetBitWidthByType() on the Parameter.
                // Older SDKs have fixed 8-bit Int/Float network encoding.
                int width = 8;
                var method = parameter.GetType().GetMethod("GetBitWidthByType", Flags, null, Type.EmptyTypes, null);
                if (method != null)
                {
                    try {
                        var value = Convert.ToInt32(method.Invoke(parameter, null));
                        if (value >= 2 && value <= 8) width = value;
                        else report.ParameterWidthUnknownCount++;
                    }
                    catch { report.ParameterWidthUnknownCount++; }
                }
                report.SyncedParameterBits += width;
            }
        }

        private static object Member(object target, string name)
        {
            if (target == null) return null;
            try
            {
                var type = target.GetType();
                var field = type.GetField(name, Flags);
                if (field != null) return field.GetValue(target);
                var property = type.GetProperty(name, Flags);
                if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(target, null);
            }
            catch { }
            return null;
        }
    }
}
