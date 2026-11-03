using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace NordicWind.AvatarStatistics.Editor
{
    /// <summary>
    /// Compatibility adapter for the SDK's own performance counter. No reference to
    /// newly introduced face-tracking members is emitted into the Unity assembly.
    /// If the SDK API or a particular metric is missing, show N/A rather than
    /// silently substituting a different algorithm.
    /// </summary>
    internal static class OfficialSdkPerformanceCollector
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        static readonly double[] CollisionPc = { 32, 128, 256, 512 };
        static readonly double[] CollisionQuest = { 0, 16, 32, 64 };
        static readonly double[] DepthPc = { 20, 50, 80, 100 };
        static readonly double[] DepthQuest = { 5, 15, 35, 50 };
        static readonly double[] ConstraintsPc = { 100, 250, 300, 350 };
        static readonly double[] ConstraintsQuest = { 30, 60, 120, 150 };
        static readonly double[] Operations = { 2000, 4000, 8000, 16000 };
        static readonly double[] Forwarded = { 10, 20, 30, 40 };

        static Type FindType(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.GetName().Name.StartsWith("VRC", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var match = assembly.GetTypes().FirstOrDefault(t => t.Name == name);
                    if (match != null) return match;
                }
                catch (ReflectionTypeLoadException ex)
                {
                    var match = ex.Types.FirstOrDefault(t => t != null && t.Name == name);
                    if (match != null) return match;
                }
            }
            return null;
        }
        static object Read(object source, string name)
        {
            if (source == null) return null;
            var type = source.GetType();
            var field = type.GetField(name, Flags);
            if (field != null) return field.GetValue(source);
            var prop = type.GetProperty(name, Flags);
            return prop != null && prop.GetIndexParameters().Length == 0 ? prop.GetValue(source, null) : null;
        }
        static double? Number(object value)
        {
            if (value == null) return null; // nullable metric unsupported / not calculated
            try { return Convert.ToDouble(value); }
            catch (Exception) { return null; }
        }
        static void Put(AvatarStatisticsReport report, string name, double[] pcLimits, double[] questLimits,
            double? pc, double? quest, string explanation)
        {
            var metric = report.Performance.FirstOrDefault(m => m.Name == name);
            if (metric == null)
            {
                metric = new PerformanceMetric { Name = name };
                report.Performance.Add(metric);
            }
            metric.OfficialSdk = true;
            metric.PcAvailable = pc.HasValue;
            metric.QuestAvailable = quest.HasValue;
            metric.Available = pc.HasValue || quest.HasValue;
            metric.Value = pc ?? quest ?? 0;
            metric.PcValue = pc;
            metric.QuestValue = quest;
            metric.Display = metric.Available ? metric.Value.ToString("N0") : "N/A";
            metric.PcDisplay = pc.HasValue ? pc.Value.ToString("N0") : "N/A";
            metric.QuestDisplay = quest.HasValue ? quest.Value.ToString("N0") : "N/A";
            metric.Pc = pcLimits;
            metric.Quest = questLimits;
            metric.Note = explanation;
        }
        internal static void Apply(GameObject root, AvatarStatisticsReport report)
        {
            object pcStats = null, questStats = null;
            string failure = null;
            try
            {
                var statsType = FindType("AvatarPerformanceStats");
                var calculatorType = FindType("AvatarPerformance");
                var method = calculatorType == null ? null : calculatorType.GetMethods(Flags)
                    .FirstOrDefault(m => m.Name == "CalculatePerformanceStats" && m.IsStatic &&
                        m.GetParameters().Length == 4 && statsType != null &&
                        m.GetParameters()[2].ParameterType.IsAssignableFrom(statsType) &&
                        m.GetParameters()[1].ParameterType.IsAssignableFrom(typeof(GameObject)));
                var constructor = statsType?.GetConstructor(Flags, null, new[] { typeof(bool) }, null);
                if (method == null || constructor == null)
                    throw new MissingMethodException("AvatarPerformance.CalculatePerformanceStats or AvatarPerformanceStats(bool) unavailable");

                // The SDK Builder refreshes native constraint execution groups before
                // calculating performance. Older SDKs may not have this entry point.
                var manager = FindType("VRCConstraintManager");
                var constraintType = FindType("VRCConstraintBase");
                var refresh = manager?.GetMethods(Flags).FirstOrDefault(m =>
                    m.Name == "Sdk_ManuallyRefreshGroups" && m.IsStatic && m.GetParameters().Length == 1);
                if (refresh != null && constraintType != null)
                {
                    var array = Array.CreateInstance(constraintType,
                        root.GetComponentsInChildren(constraintType, true).Length);
                    var components = root.GetComponentsInChildren(constraintType, true);
                    Array.Copy(components, array, components.Length);
                    try { refresh.Invoke(null, new object[] { array }); }
                    catch (Exception ex) { Debug.LogWarning("[Nordic's Avatar Statistics] constraint group refresh failed: " + ex.Message); }
                }
                pcStats = constructor.Invoke(new object[] { false });
                questStats = constructor.Invoke(new object[] { true });
                method.Invoke(null, new[] { root.name, (object)root, pcStats, false });
                method.Invoke(null, new[] { root.name, (object)root, questStats, true });
            }
            catch (Exception ex)
            {
                failure = ex.GetBaseException().Message;
                Debug.LogWarning("[Nordic's Avatar Statistics] official SDK performance counters unavailable: " + failure);
            }
            // SDK aabb is nullable in newer SDKs; retain Nordic's established
            // renderer-bounds calculation ONLY when that official field is missing.
            // Bounds are shared between the PC and Quest ranking displays.
            ApplySdkBounds(report, pcStats, questStats);
            string note = failure == null ? null : "Official VRChat SDK counter unavailable: " + failure;
            Put(report, "PhysBones Collision Check Count", CollisionPc, CollisionQuest,
                Number(Read(Read(pcStats, "physBone"), "collisionCheckCount")), Number(Read(Read(questStats, "physBone"), "collisionCheckCount")), note);
            Put(report, "Constraint Depth", DepthPc, DepthQuest,
                Number(Read(pcStats, "constraintDepth")), Number(Read(questStats, "constraintDepth")), note);
            Put(report, "Constraint Count", ConstraintsPc, ConstraintsQuest,
                Number(Read(pcStats, "constraintsCount")), Number(Read(questStats, "constraintsCount")), note);

            // Native face tracking fields do not exist in older SDKs. Hide those rows
            // entirely rather than showing zero or a misleading estimate.
            if (IsFaceTrackingEnabled(root) &&
                ((pcStats != null && HasMember(pcStats, "faceTrackingOperations")) ||
                 (questStats != null && HasMember(questStats, "faceTrackingOperations"))))
            {
                Put(report, "Face Tracking Operations", Operations, Operations,
                    Number(Read(pcStats, "faceTrackingOperations")), Number(Read(questStats, "faceTrackingOperations")), note);
                Put(report, "Forwarded Parameters", Forwarded, Forwarded,
                    Number(Read(pcStats, "forwardedParametersCount")), Number(Read(questStats, "forwardedParametersCount")), note);
            }
        }
        static void ApplySdkBounds(AvatarStatisticsReport report, object pcStats, object questStats)
        {
            object sdkBounds = Read(pcStats, "aabb") ?? Read(questStats, "aabb");
            if (sdkBounds == null) return;
            object sizeObj = Read(sdkBounds, "size");
            if (!(sizeObj is Vector3 size)) return;
            var metric = report.Performance.FirstOrDefault(m => m.Name == "Bounds");
            if (metric == null) return;
            metric.BoundsX = size.x;
            metric.BoundsY = size.y;
            metric.BoundsZ = size.z;
            metric.Display = string.Format("{0:F2} × {1:F2} × {2:F2} m", size.x, size.y, size.z);
            metric.Note = null;
        }
        // Keep native face tracking optional. Older SDKs have no enableFaceTracking
        // descriptor member, and disabled tracking must not produce performance rows.
        internal static bool IsFaceTrackingEnabled(GameObject root)
        {
            var descriptorType = FindType("VRCAvatarDescriptor");
            if (root == null || descriptorType == null) return false;
            var descriptor = root.GetComponent(descriptorType);
            if (descriptor == null || !HasMember(descriptor, "enableFaceTracking")) return false;
            return Read(descriptor, "enableFaceTracking") is bool enabled && enabled;
        }
        static bool HasMember(object obj, string name)
        {
            var type = obj.GetType();
            return type.GetField(name, Flags) != null || type.GetProperty(name, Flags) != null;
        }
    }
}
