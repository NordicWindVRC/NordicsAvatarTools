using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NordicWind.SubmeshSplitter.Editor
{
    // Project-wide settings shared by every NordicWind tool. They are stored in
    // ProjectSettings/NordicWindSettings.json, so they travel with the project, and any tool
    // can read the file without depending on this assembly. Each tool writes generated
    // assets to "<GeneratedAssetsFolder>/<Tool>/".
    internal static class NordicWindSettings
    {
        internal const string DefaultGeneratedFolder = "Assets/NordicWind/Generated";
        private const string FilePath = "ProjectSettings/NordicWindSettings.json";

        [Serializable]
        private sealed class Data { public string generatedAssetsFolder = DefaultGeneratedFolder; }

        private static Data _cache;
        private static DateTime _cacheTime;

        internal static string GeneratedAssetsFolder
        {
            get
            {
                var data = Load();
                return IsValidFolder(data.generatedAssetsFolder) ? data.generatedAssetsFolder.TrimEnd('/') : DefaultGeneratedFolder;
            }
        }

        internal static string FolderFor(string tool) => GeneratedAssetsFolder + "/" + tool;

        // Inside Assets, so generated files are normal project assets (packages are read-only).
        internal static bool IsValidFolder(string folder) =>
            !string.IsNullOrWhiteSpace(folder) && (folder.TrimEnd('/') == "Assets" || folder.StartsWith("Assets/", StringComparison.Ordinal))
            && folder.IndexOfAny(Path.GetInvalidPathChars()) < 0 && !folder.Contains("..");

        internal static void SetGeneratedAssetsFolder(string folder)
        {
            if (!IsValidFolder(folder)) throw new ArgumentException("The folder must be inside Assets.", nameof(folder));
            var data = Load();
            data.generatedAssetsFolder = folder.TrimEnd('/');
            File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
            _cache = null;
        }

        private static Data Load()
        {
            var time = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
            if (_cache != null && time == _cacheTime) return _cache;
            _cacheTime = time;
            try { _cache = time == DateTime.MinValue ? new Data() : JsonUtility.FromJson<Data>(File.ReadAllText(FilePath)) ?? new Data(); }
            catch (Exception ex)
            {
                Debug.LogWarning("[Nordic's Submesh Splitter] Could not read " + FilePath + ", using defaults: " + ex.Message);
                _cache = new Data();
            }
            return _cache;
        }

        internal static void EnsureFolder(string folder)
        {
            var parts = folder.Split('/');
            string path = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = path + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(path, parts[i]);
                path = next;
            }
        }
    }

    internal sealed class NordicWindSettingsProvider : SettingsProvider
    {
        private string _folder;

        private NordicWindSettingsProvider() : base("Project/NordicWind", SettingsScope.Project,
            new[] { "NordicWind", "generated", "output", "folder" }) { }

        [SettingsProvider]
        private static SettingsProvider Create() => new NordicWindSettingsProvider();

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement) =>
            _folder = NordicWindSettings.GeneratedAssetsFolder;

        public override void OnGUI(string searchContext)
        {
            EditorGUILayout.LabelField("Generated assets", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("NordicWind tools save the files they generate here, each in its own subfolder " +
                "(for example SubmeshSplitter). The folder must be inside Assets.", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                _folder = EditorGUILayout.TextField("Folder", _folder);
                if (GUILayout.Button("Choose…", GUILayout.Width(80)))
                {
                    string picked = EditorUtility.OpenFolderPanel("Generated assets folder", Application.dataPath, "");
                    string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/') + "/";
                    picked = picked.Replace('\\', '/');
                    if (picked.StartsWith(project, StringComparison.OrdinalIgnoreCase)) _folder = picked.Substring(project.Length);
                    else if (!string.IsNullOrEmpty(picked)) EditorUtility.DisplayDialog("NordicWind", "Choose a folder inside this project's Assets folder.", "OK");
                }
            }
            bool valid = NordicWindSettings.IsValidFolder(_folder);
            if (!valid) EditorGUILayout.HelpBox("The folder must be inside Assets.", MessageType.Error);
            bool changed = valid && _folder.TrimEnd('/') != NordicWindSettings.GeneratedAssetsFolder;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!changed))
                    if (GUILayout.Button("Apply", GUILayout.Width(80))) Apply(_folder.TrimEnd('/'));
                if (GUILayout.Button("Reset to default", GUILayout.Width(120))) _folder = NordicWindSettings.DefaultGeneratedFolder;
            }
        }

        // Moving keeps each asset's GUID, so every reference to it keeps working.
        private static void Apply(string folder)
        {
            string oldFolder = NordicWindSettings.GeneratedAssetsFolder;
            var existing = AssetDatabase.FindAssets("t:" + nameof(NordicsSubmeshAssetData), new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.StartsWith(oldFolder + "/", StringComparison.Ordinal))
                .ToList();
            bool move = existing.Count > 0 && EditorUtility.DisplayDialog("NordicWind",
                $"Move {existing.Count} existing generated file(s) from '{oldFolder}' to '{folder}'?\n\n" +
                "References in your scenes and prefabs keep working either way. Files you don't move stay where they are.",
                "Move", "Leave them");
            NordicWindSettings.SetGeneratedAssetsFolder(folder);
            if (!move) return;
            var failed = new List<string>();
            foreach (var path in existing)
            {
                string relative = path.Substring(oldFolder.Length + 1);
                string target = folder + "/" + relative;
                NordicWindSettings.EnsureFolder(Path.GetDirectoryName(target).Replace('\\', '/'));
                string error = AssetDatabase.MoveAsset(path, AssetDatabase.GenerateUniqueAssetPath(target));
                if (!string.IsNullOrEmpty(error)) failed.Add(path + ": " + error);
            }
            AssetDatabase.SaveAssets();
            if (failed.Count > 0)
                Debug.LogWarning("[Nordic's Submesh Splitter] Some generated files could not be moved:\n" + string.Join("\n", failed));
        }
    }
}
