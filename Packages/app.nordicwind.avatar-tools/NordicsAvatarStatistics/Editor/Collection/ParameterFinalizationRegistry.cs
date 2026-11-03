using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.AvatarStatistics.Editor
{
    // The SDK's late avatar preprocessing callbacks occur after NDMF PlatformFinish.
    // Keep this registry independent of VRCFury and other optional tools.
    public static class ParameterFinalizationRegistry
    {
        private static readonly Dictionary<int, AvatarStatisticsReport> Pending = new Dictionary<int, AvatarStatisticsReport>();
        private static string pendingAvatarName;
        public static void Register(GameObject root, AvatarStatisticsReport report)
        {
            if (root == null || report == null) return;
            Pending.Clear(); // Only the active avatar build is relevant.
            pendingAvatarName = root.name;
            report.BeforeSdkParameterBits = report.SyncedParameterBits;
            Pending[root.GetInstanceID()] = report;
        }

        // Called by the independent VRChat SDK integration assembly.
        public static void OnLateSdkPreprocess(GameObject root)
        {
            if (root == null) return;
            AvatarStatisticsReport report;
            if (!Pending.TryGetValue(root.GetInstanceID(), out report))
            {
                // SDK preprocessing can receive a different instance of the same
                // build avatar than NDMF PlatformFinish. There is only one pending
                // build, so use it when the avatar names match; never substitute an
                // unrelated avatar's expression parameters.
                if (Pending.Count != 1 || root.name != pendingAvatarName) return;
                foreach (var entry in Pending) { report = entry.Value; break; }
            }
            Pending.Clear();
            pendingAvatarName = null;
            // DO NOT move this collection into NDMF: VRCFury 1.1436's parameter
            // compressor runs as an SDK callback at int.MaxValue - 100, AFTER NDMF.
            // Nordic's int.MaxValue - 10 callback must read the compressed asset.
            // This snapshot, not the initial uncompressed snapshot, controls the
            // final synced-memory budget, color, and custom parameter count.
            AvatarParameterCollector.Collect(root, report);
            report.PostSdkParameterSnapshot = true;
        }
    }
}
