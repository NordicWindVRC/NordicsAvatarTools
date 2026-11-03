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
                    summary.AppendLine("Geometry baking: " + (group.Component!=null && group.Component.EnableGeometryBaking?"enabled":"disabled"));
                    if(group.Component!=null && group.Component.EnableGeometryBaking)
                        summary.AppendLine("Resolution: "+group.Component.GeometryBakingResolution+" x "+group.Component.GeometryBakingResolution);
                    ErrorReport.ReportError(new AtlasMessageError(ErrorSeverity.Information,
                        "Nordic's Advanced Atlaser — Build Results", summary.ToString()));
                }
            }
            if (AtlasDebugSettings.VerboseLogging && ctx.Diagnostics.Count>0)
                ErrorReport.ReportError(new AtlasMessageError(ErrorSeverity.Information,
                    "Nordic's Advanced Atlaser — Diagnostics", string.Join("\n",ctx.Diagnostics)));
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
