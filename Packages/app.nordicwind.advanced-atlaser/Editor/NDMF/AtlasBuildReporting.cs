using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using nadena.dev.ndmf;
using UnityEngine;
using UnityEngine.UIElements;

namespace NordicWind.AdvancedAtlaser.Editor
{
    internal static class AtlasBuildReporting
    {
        internal static void Publish(AtlasBuildContext ctx, AtlasOriginalGeometry original)
        {
            foreach (var diagnostic in ctx.Diagnostics)
            {
                bool issue = diagnostic.Contains("unavailable") || diagnostic.Contains("failed") ||
                    diagnostic.Contains("not captured") || diagnostic.Contains("unregistered") ||
                    diagnostic.Contains("did not complete") || diagnostic.Contains("snapshot is null") ||
                    diagnostic.Contains("zero geometry sections") || diagnostic.Contains("[Warning]") ||
                    diagnostic.Contains("[Error]");
                if(issue)
                    ErrorReport.ReportError(new AtlasMessageError(ErrorSeverity.NonFatal,
                        "Nordic's Advanced Atlaser", diagnostic));
            }
            if (AtlasDebugSettings.BuildSummary)
            {
                foreach(var group in ctx.Groups)
                {
                    int originals = 0, survivors = 0, bakes = 0;
                    foreach(var input in group.Inputs)
                    {
                        var bake = input.Bakes.Values.FirstOrDefault();
                        if(bake != null)
                        {
                            bakes++;
                            survivors += bake.Submeshes.Count(x=>!x.CurrentSubmeshMissing);
                        }
                    }
                    if(original != null)
                        foreach(var job in group.Outputs)
                            foreach(var source in job.Sources)
                                if(original.SourceSections.TryGetValue(source.SourceKey,out var sections))
                                    originals += sections.Count;
                    var summary = new StringBuilder();
                    summary.AppendLine("Atlaser: " + (group.Component==null?"<removed>":group.Component.name));
                    summary.AppendLine("Source materials: " + group.Inputs.Count);
                    summary.AppendLine("Original sections: " + originals);
                    summary.AppendLine("Surviving material submeshes: " + survivors);
                    summary.AppendLine("Geometry bakes: " + bakes);
                    summary.AppendLine("Geometry baking: " + (group.Component!=null && group.Component.enableGeometryBaking
                        ? "enabled (each material at 1.5× its atlas area, up to " + AtlasOutputLayout.BakeLimit + " per side)" : "disabled"));
                    ReportInformation(new AtlasMessageError(ErrorSeverity.Information,
                        "Nordic's Advanced Atlaser — Build Results", summary.ToString()));
                }
            }
            if (AtlasDebugSettings.VerboseLogging && ctx.Diagnostics.Count>0)
            {
                // The diagnostics stay out of the NDMF Console, which is for what needs the
                // user's attention: they run to hundreds of lines, and its text can't be
                // selected or searched. They go where they can be: a file in the debug output
                // folder, replaced on every build, and one information line in Unity's console
                // (and its Editor.log) that names the file.
                string text = string.Join("\n", ctx.Diagnostics);
                string path = WriteDiagnosticsFile(text);
                Debug.Log("[Nordic's Advanced Atlaser] Diagnostics" + (path != null ? " (also in " + path + ")" : "") + ":\n" + text);
            }
        }

        // The file's path, or null where it couldn't be written.
        private static string WriteDiagnosticsFile(string text)
        {
            string path = System.IO.Path.Combine(AtlasDebugSettings.OutputDirectory, "Diagnostics.txt");
            try
            {
                System.IO.Directory.CreateDirectory(AtlasDebugSettings.OutputDirectory);
                System.IO.File.WriteAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{text}\n");
                return path;
            }
            catch (Exception e)
            {
                // Debug output only: a failed write never affects the build.
                Debug.LogWarning($"[Nordic's Advanced Atlaser] Couldn't write {path}: {e.Message}");
                return null;
            }
        }

        // NDMF's ReportError also writes "[NDMF] Error Reported" to Unity's console as a warning,
        // whatever the severity, which makes information look like a failure. For information,
        // Unity's logger is set to errors only for the duration of the call: the NDMF Console
        // still lists the report, and Unity's console doesn't get the warning. Warnings and
        // errors are reported as they are.
        internal static void ReportInformation(IError information)
        {
            var logger = Debug.unityLogger;
            var filter = logger.filterLogType;
            logger.filterLogType = LogType.Error;
            try
            {
                ErrorReport.ReportError(information);
            }
            finally
            {
                logger.filterLogType = filter;
            }
        }
    }
    // Minimal NDMF-native informational/warning report; no Unity console spam.
    internal sealed class AtlasMessageError : IError
    {
        private readonly string title, details;
        public ErrorSeverity Severity { get; }
        public AtlasMessageError(ErrorSeverity severity, string title, string details)
        { Severity=severity;this.title=title;this.details=details; }
        public void AddReference(ObjectReference obj) { }
        public VisualElement CreateVisualElement(ErrorReport report)
        {
            var root = new VisualElement();
            var header = new Label(title) { style = { unityFontStyleAndWeight = FontStyle.Bold } };
            root.Add(header);
            root.Add(new Label(details) { style = { whiteSpace = WhiteSpace.Normal } });
            return root;
        }
        public string ToMessage() => title + ": " + details;
    }
}
