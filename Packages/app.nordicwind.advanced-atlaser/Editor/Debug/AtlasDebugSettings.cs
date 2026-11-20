using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // These settings are editor-local; never serialized into an avatar.
    public static class AtlasDebugSettings
    {
        private const string Prefix = "NordicWind.AdvancedAtlaser.";
        private const string VerboseKey = Prefix + "VerboseLogging";
        private const string ExportKey = Prefix + "ExportIntermediateMaps";
        private const string SummaryKey = Prefix + "BuildSummary";
        public static bool VerboseLogging => EditorPrefs.GetBool(VerboseKey, false);
        public static bool ExportIntermediateMaps => EditorPrefs.GetBool(ExportKey, false);
        public static bool BuildSummary => EditorPrefs.GetBool(SummaryKey, true);
        public static string OutputDirectory => Path.Combine(Directory.GetParent(Application.dataPath).FullName,
            "NordicWindDebug", "AdvancedAtlaser");

        [MenuItem("Tools/NordicWind/Advanced Atlaser/Verbose Logging", false, 100)]
        private static void ToggleVerbose() { EditorPrefs.SetBool(VerboseKey, !VerboseLogging); }
        [MenuItem("Tools/NordicWind/Advanced Atlaser/Verbose Logging", true)]
        private static bool ValidateVerbose() { Menu.SetChecked("Tools/NordicWind/Advanced Atlaser/Verbose Logging", VerboseLogging); return true; }

        [MenuItem("Tools/NordicWind/Advanced Atlaser/Export Intermediate Maps", false, 101)]
        private static void ToggleExport() { EditorPrefs.SetBool(ExportKey, !ExportIntermediateMaps); }
        [MenuItem("Tools/NordicWind/Advanced Atlaser/Export Intermediate Maps", true)]
        private static bool ValidateExport() { Menu.SetChecked("Tools/NordicWind/Advanced Atlaser/Export Intermediate Maps", ExportIntermediateMaps); return true; }

        [MenuItem("Tools/NordicWind/Advanced Atlaser/Build Summary", false, 102)]
        private static void ToggleSummary() { EditorPrefs.SetBool(SummaryKey, !BuildSummary); }
        [MenuItem("Tools/NordicWind/Advanced Atlaser/Build Summary", true)]
        private static bool ValidateSummary() { Menu.SetChecked("Tools/NordicWind/Advanced Atlaser/Build Summary", BuildSummary); return true; }

        [MenuItem("Tools/NordicWind/Advanced Atlaser/Open Debug Output Folder", false, 120)]
        private static void OpenDirectory() { Directory.CreateDirectory(OutputDirectory); EditorUtility.RevealInFinder(OutputDirectory); }
    }
}
