using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NordicWind.AvatarStatistics.Editor
{
    public sealed class AvatarStatisticsInfo : IError
    {
        private readonly AvatarStatisticsReport data;
        private static readonly Color Muted = new Color(0.67f, 0.69f, 0.72f);
        private static readonly Color Accent = new Color(0.47f, 0.73f, 0.94f);
        private static Color Panel => EditorGUIUtility.isProSkin ? new Color(0.19f, 0.20f, 0.22f, 0.78f) : new Color(0.84f, 0.85f, 0.87f, 0.65f);
        private static Color Alternate => EditorGUIUtility.isProSkin ? new Color(0.23f, 0.24f, 0.26f, 0.65f) : new Color(0.91f, 0.92f, 0.94f, 0.7f);

        public AvatarStatisticsInfo(AvatarStatisticsReport data) { this.data = data; }
        public ErrorSeverity Severity => ErrorSeverity.Information;
        public void AddReference(ObjectReference obj) { }
        public string ToMessage() => "Nordic's Avatar Statistics — Build Results: " + data.TriangleCount.ToString("N0") + " triangles, " + data.MaterialSlots + " material slots, " + data.Textures.Count + " unique textures, " + MiB(data.GpuTextureBytes) + " estimated GPU texture memory (" + MiB(data.TextureBytes) + " uncompressed equivalent).";

        public VisualElement CreateVisualElement(ErrorReport report)
        {
            var root = new VisualElement();
            root.style.flexGrow = 1;
            root.style.minWidth = 280;
            root.style.paddingTop = 5;
            root.style.paddingBottom = 6;

            root.Add(Text("Nordic's Avatar Statistics  ·  Build Results", true, 14));
            var summary = new VisualElement();
            summary.style.flexDirection = FlexDirection.Row;
            summary.style.flexWrap = Wrap.Wrap;
            summary.style.marginTop = 7;
            summary.style.marginBottom = 7;
            summary.Add(Stat("Triangles", data.TriangleCount.ToString("N0")));
            summary.Add(Stat("Meshes (skinned / regular)", data.SkinnedCount + " / " + data.RegularCount));
            summary.Add(Stat("Material slots", data.MaterialSlots.ToString("N0")));
            summary.Add(Stat("Unique materials", data.Materials.Count.ToString("N0")));
            summary.Add(Stat("Unique textures", data.Textures.Count.ToString("N0")));
            summary.Add(Stat("Shared textures", data.Textures.Count(x => x.MaterialKeys.Count > 1).ToString("N0")));
            summary.Add(Stat("Blend shapes", data.BlendShapeCount.ToString("N0")));
            summary.Add(RankStat("PC Performance", data.Performance, false));
            summary.Add(RankStat("Quest Performance", data.Performance, true));
            root.Add(summary);
            root.Add(Note("Performance tiles combine established Nordic estimates and official SDK counters where noted."));

            var memory = PanelBox();
            var primary = Horizontal();
            primary.Add(Text("Estimated GPU texture memory", true, 12));
            var gpuValue = Text(MiB(data.GpuTextureBytes), true, 15);
            gpuValue.style.color = Accent;
            gpuValue.style.marginLeft = 12;
            primary.Add(gpuValue);
            memory.Add(primary);
            memory.Add(Text("Uncompressed equivalent: " + MiB(data.TextureBytes), false, 11));
            memory.Add(Note("Current Unity graphics formats; estimates may differ from VRChat on other build targets."));
            root.Add(memory);

            root.Add(ParameterBudget("Synced Parameter Memory", data.SyncedParameterBits, 256, "bits"));
            root.Add(ParameterBudget("Custom Parameter Count", data.CustomParameterCount, 8192, "parameters"));

            root.Add(PerformanceSection("VRChat Performance Metrics — PC", false));
            root.Add(PerformanceSection("VRChat Performance Metrics — Quest", true));

            var tabs = Horizontal();
            tabs.style.marginTop = 8;
            tabs.style.marginBottom = 5;
            var contents = new VisualElement();
            contents.style.flexGrow = 1;
            root.Add(tabs);
            root.Add(contents);
            var names = new[] { "Meshes (" + data.Meshes.Count + ")", "Materials (" + data.Materials.Count + ")", "Textures (" + data.Textures.Count + ")" };
            var buttons = new Button[3];
            bool[] created = new bool[3];
            VisualElement[] panels = { new VisualElement(), new VisualElement(), new VisualElement() };
            for (int index = 0; index < 3; index++)
            {
                var i = index;
                var b = new Button(() =>
                {
                    for (var j = 0; j < panels.Length; j++)
                    {
                        panels[j].style.display = j == i ? DisplayStyle.Flex : DisplayStyle.None;
                        buttons[j].style.unityFontStyleAndWeight = j == i ? FontStyle.Bold : FontStyle.Normal;
                    }
                    if (!created[i])
                    {
                        created[i] = true;
                        if (i == 0) BuildMeshes(panels[i]);
                        else if (i == 1) BuildMaterials(panels[i]);
                        else BuildTextures(panels[i]);
                    }
                }) { text = names[i] };
                b.style.marginRight = 5;
                b.style.flexShrink = 1;
                buttons[i] = b;
                tabs.Add(b);
                contents.Add(panels[i]);
                panels[i].style.display = DisplayStyle.None;
            }
            // Open the textures tab by default.
            panels[2].style.display = DisplayStyle.Flex;
            buttons[2].style.unityFontStyleAndWeight = FontStyle.Bold;
            created[2] = true;
            BuildTextures(panels[2]);
            return root;
        }

        // Resource budgets, not VRChat performance rankings.
        // IMPORTANT: VRCFury compresses expression parameters in a late SDK callback,
        // AFTER NDMF has registered this report. The original UI captured the early
        // value once (e.g. 305 bits), even though the callback later wrote 256 bits.
        // Refresh this panel whenever the report's parameter snapshot changes.
        // Do not remove the late refresh: it preserves post-VRCFury compression counts.
        private VisualElement ParameterBudget(string title, int unusedInitialValue, int limit, string unit)
        {
            var panel = PanelBox();
            panel.style.marginTop = 4;
            string lastSnapshot = null;
            Action update = () =>
            {
                bool memory = title == "Synced Parameter Memory";
                int used = memory ? data.SyncedParameterBits : data.CustomParameterCount;
                string signature = used + "/" + data.ExpressionParametersAvailable + "/" +
                    data.PostSdkParameterSnapshot + "/" + data.BeforeSdkParameterBits + "/" +
                    data.ParameterWidthUnknownCount;
                if (signature == lastSnapshot) return;
                lastSnapshot = signature;
                panel.Clear();
                var row = Horizontal();
                row.style.justifyContent = Justify.SpaceBetween;
                row.Add(Text(title, true, 11));
                var value = Text(data.ExpressionParametersAvailable
                    ? used.ToString("N0") + " / " + (memory ? limit.ToString("N0") : "8192") + " " + unit
                    : "Unavailable", true, 11);
                row.Add(value);
                panel.Add(row);
                if (!data.ExpressionParametersAvailable)
                {
                    panel.Add(Note("No expression parameters asset was found on the final avatar descriptor."));
                    return;
                }
                var track = new VisualElement();
                track.style.height = 10;
                track.style.marginTop = 5;
                track.style.marginBottom = 3;
                track.style.backgroundColor = Alternate;
                var fill = new VisualElement();
                fill.style.height = 10;
                fill.style.width = Length.Percent(Mathf.Clamp01((float)used / limit) * 100f);
                // At the exact synced-memory cap, uncompressed data is green;
                // compressed data is orange. Over-budget values are red.
                bool compressedAtLimit = memory && used == limit &&
                    data.PostSdkParameterSnapshot && data.BeforeSdkParameterBits > used;
                fill.style.backgroundColor = used > limit ? FromHex("#A8162C") :
                    compressedAtLimit ? FromHex("#F5A623") :
                    used == limit ? FromHex("#279B45") :
                    used > limit * 0.9f ? FromHex("#F5A623") : FromHex("#279B45");
                track.Add(fill);
                panel.Add(track);
                if (memory)
                {
                    if (data.PostSdkParameterSnapshot && data.BeforeSdkParameterBits != data.SyncedParameterBits)
                        panel.Add(Note(data.BeforeSdkParameterBits.ToString("N0") + " bits uncompressed"));
                    if (data.ParameterWidthUnknownCount > 0)
                        panel.Add(Note("Unverified: " + data.ParameterWidthUnknownCount + " Int/Float widths could not be read from SDK GetBitWidthByType()."));
                }
            };
            update();
            // UI Toolkit executes scheduled jobs while the panel is attached. This
            // covers both report creation before and after SDK parameter finalization.
            panel.schedule.Execute(() => update()).Every(250);
            return panel;
        }

        private static readonly Color[] RankColors = {
            FromHex("#39E75F"), FromHex("#279B45"), FromHex("#F5A623"), FromHex("#D65A42"), FromHex("#A8162C")
        };
        private static readonly string[] RankNames = { "Excellent", "Good", "Medium", "Poor", "Very Poor" };
        private static Color FromHex(string hex) { ColorUtility.TryParseHtmlString(hex, out var result); return result; }
        // VRChat bounds are three independent dimensions. Rank uses the first
        // threshold whose X, Y and Z limits are all satisfied.
        private static readonly Vector3[] BoundsLimits = {
            new Vector3(2.5f, 2.5f, 2.5f),
            new Vector3(4f, 4f, 4f),
            new Vector3(5f, 6f, 5f),
            new Vector3(5f, 6f, 5f)
        };
        internal static int? QuestHardLimit(PerformanceMetric metric)
        {
            switch (metric.Name)
            {
                case "PhysBones Components": return 8;
                case "PhysBones Affected Transforms": return 64;
                case "PhysBones Colliders": return 16;
                case "Contacts": return 16;
                case "PhysBones Collision Check Count": return 64;
                case "Constraint Count": return 150;
                case "Constraint Depth": return 50;
                default: return null;
            }
        }
        private static bool IsStripped(PerformanceMetric metric, bool quest)
        {
            return quest && (metric.Name == "Lights" || metric.Name == "Audio Sources" || metric.Name == "Cloths" || metric.Name == "Total Cloth Vertices" || metric.Name == "Physics Colliders" || metric.Name == "Physics Rigidbodies");
        }
        private static bool IsInvalid(PerformanceMetric metric, bool quest)
        {
            var limit = quest ? QuestHardLimit(metric) : null;
            return limit.HasValue && metric.Available && (!metric.OfficialSdk || (quest ? metric.QuestAvailable : metric.PcAvailable)) && ValueFor(metric, quest) > limit.Value;
        }
        private static bool OverallInvalid(System.Collections.Generic.IEnumerable<PerformanceMetric> metrics, bool quest)
        {
            return quest && metrics.Any(m => IsInvalid(m, quest));
        }
        private static double ValueFor(PerformanceMetric metric, bool quest)
        {
            return (quest ? metric.QuestValue : metric.PcValue) ?? metric.Value;
        }
        private static string DisplayFor(PerformanceMetric metric, bool quest)
        {
            return (quest ? metric.QuestDisplay : metric.PcDisplay) ?? metric.Display;
        }
        internal static int Rank(PerformanceMetric metric, bool quest)
        {
            if (IsStripped(metric, quest)) return -1;
            if (metric.OfficialSdk && !(quest ? metric.QuestAvailable : metric.PcAvailable)) return -1;
            if (metric.IsBounds)
            {
                if (!metric.Available) return -1;
                for (int i = 0; i < BoundsLimits.Length; i++)
                {
                    var limit = BoundsLimits[i];
                    if (metric.BoundsX <= limit.x && metric.BoundsY <= limit.y && metric.BoundsZ <= limit.z) return i;
                }
                return 4;
            }
            var limits = quest ? metric.Quest : metric.Pc;
            if (!metric.Available || limits == null) return -1;
            for (int i = 0; i < limits.Length; i++) if (ValueFor(metric, quest) <= limits[i]) return i;
            return 4;
        }
        private static double BoundsFraction(PerformanceMetric metric, int rank)
        {
            if (rank >= 4) return 1d;
            Vector3 previous = rank == 0 ? Vector3.zero : BoundsLimits[rank - 1];
            Vector3 next = BoundsLimits[rank];
            // The limiting dimension is the one closest to exceeding its next
            // threshold. Equal consecutive limits (Medium/Poor) saturate the band.
            double x = next.x > previous.x ? (metric.BoundsX - previous.x) / (next.x - previous.x) : 1d;
            double y = next.y > previous.y ? (metric.BoundsY - previous.y) / (next.y - previous.y) : 1d;
            double z = next.z > previous.z ? (metric.BoundsZ - previous.z) / (next.z - previous.z) : 1d;
            return Math.Max(0d, Math.Min(1d, Math.Max(x, Math.Max(y, z))));
        }
        private static int OverallRank(System.Collections.Generic.IEnumerable<PerformanceMetric> metrics, bool quest)
        {
            int highest = 0;
            foreach (var metric in metrics) highest = Math.Max(highest, Math.Max(0, Rank(metric,quest)));
            return highest;
        }
        private static VisualElement RankStat(string title, System.Collections.Generic.IEnumerable<PerformanceMetric> metrics, bool quest)
        {
            int rank = OverallRank(metrics,quest);
            bool invalid = OverallInvalid(metrics,quest);
            var box = Stat(title,invalid ? "Invalid" : RankNames[rank]);
            var labels = box.Query<Label>().ToList();
            if (labels.Count > 1) labels[labels.Count-1].style.color = invalid ? FromHex("#A8162C") : RankColors[rank];
            return box;
        }
        private VisualElement PerformanceSection(string title, bool quest)
        {
            var fold = new Foldout { text=title, value=false };
            fold.style.backgroundColor=Panel;
            fold.style.paddingTop=4;
            fold.style.paddingBottom=4;
            fold.style.paddingLeft=5;
            fold.style.marginTop=5;
            var content = new VisualElement();
            fold.Add(content);
            bool built=false;
            fold.RegisterValueChangedCallback(e => {
                if (!e.newValue || built) return;
                built=true;
                content.Add(Note("Estimated ranking based on collected metrics. Zero-value metrics are hidden; they still contribute to ranking. Some complex calculations may differ from VRChat SDK."));
                if(quest) content.Add(Note("Quest texture memory uses Android importer settings, with current-import fallbacks where needed. Orange Stripped means removed on mobile; Invalid means a measured hard component limit is exceeded."));
                // Presentation-only sorting: retain the original metric objects and all values.
                foreach(var metric in data.Performance.OrderBy(m => PerformanceOrder(m.Name)))
                    if (metric.OfficialSdk || metric.Value != 0 || (metric.IsBounds && (metric.BoundsX != 0 || metric.BoundsY != 0 || metric.BoundsZ != 0)))
                        AddPerformanceRow(content,metric,quest);
            });
            return fold;
        }
        // Logical ordering only; no visible group headers.
        private static readonly string[] MetricOrder = {
            "Triangles", "Texture Memory", "Material Slots", "Skinned Meshes", "Basic Meshes", "Bones", "Bounds",
            "PhysBones Components", "PhysBones Affected Transforms", "PhysBones Colliders", "PhysBones Collision Check Count", "Contacts",
            "Constraint Count", "Constraint Depth", "Face Tracking Operations", "Forwarded Parameters", "Animators", "Particle Systems", "Total Particles Active",
            "Mesh Particle Active Polys", "Particle Trails Enabled", "Particle Collision Enabled", "Trail Renderers",
            "Line Renderers", "Lights", "Physics Colliders", "Physics Rigidbodies", "Cloths", "Total Cloth Vertices",
            "Audio Sources", "Raycasts"
        };
        private static int PerformanceOrder(string name)
        {
            int position=Array.IndexOf(MetricOrder,name);
            return position>=0 ? position : int.MaxValue;
        }
        private void AddPerformanceRow(VisualElement parent, PerformanceMetric metric, bool quest)
        {
            int rank=Rank(metric,quest);
            bool stripped=IsStripped(metric,quest);
            bool invalid=IsInvalid(metric,quest);
            var panel=PanelBox();
            panel.style.marginTop=3;
            var row=Horizontal();
            row.style.justifyContent=Justify.SpaceBetween;
            var left=Text(metric.Name,true,11);
            row.Add(left);
            var measurement = Text(":  " + DisplayFor(metric, quest), true, 11);
            measurement.style.marginLeft = 0;
            row.Add(measurement);
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            row.Add(spacer);
            var level=Text(invalid ? "Invalid" : stripped ? (ValueFor(metric, quest) > 0 ? "Stripped" : "Stripped (0)") : rank<0 ? "N/A" : RankNames[rank],true,11);
            if(invalid) level.style.color=FromHex("#A8162C");
            else if(stripped) level.style.color=ValueFor(metric, quest) > 0 ? FromHex("#F5A623") : Muted;
            else if(rank>=0) level.style.color=RankColors[rank]; else level.style.color=Muted;
            row.Add(level);
            panel.Add(row);
            var limits=quest?metric.Quest:metric.Pc;
            if(rank>=0)
            {
                var track=Horizontal();
                track.style.height=12;
                track.style.position=Position.Relative;
                track.style.flexWrap=Wrap.NoWrap;
                track.style.marginTop=3;
                track.style.marginBottom=3;
                for(int i=0;i<5;i++)
                {
                    var part=new VisualElement();
                    part.style.flexGrow=1;
                    part.style.flexBasis=0;
                    part.style.height=9;
                    part.style.backgroundColor=RankColors[i];
                    part.style.opacity=(i<=rank)?1f:0.27f;
                    part.style.marginRight=2;
                    track.Add(part);
                }
                // A white marker shows the measured value within its ranking band.
                // Each rating band takes 1/5 of the track width; values within
                // a band interpolate between its numerical boundaries.
                double fraction;
                if (metric.IsBounds) fraction = BoundsFraction(metric, rank);
                else
                {
                    double lower = rank == 0 ? 0d : limits[rank-1];
                    double upper = rank < 4 ? limits[rank] : Math.Max(limits[3] * 1.5, limits[3] + 1d);
                    fraction = upper > lower ? (ValueFor(metric, quest) - lower) / (upper - lower) : 1d;
                }
                fraction = Math.Max(0d, Math.Min(1d, fraction));
                double position = 20d * rank + 20d * fraction;
                // Keep marker visible even when the measured value is zero or above scale.
                position = Math.Max(0.7d, Math.Min(99.3d, position));
                var marker = new VisualElement();
                marker.style.position = Position.Absolute;
                marker.style.left = Length.Percent((float)position);
                marker.style.top = -2;
                marker.style.height = 13;
                marker.style.width = 3;
                marker.style.backgroundColor = Color.white;
                marker.style.borderTopLeftRadius = 1;
                marker.style.borderTopRightRadius = 1;
                marker.style.borderBottomLeftRadius = 1;
                marker.style.borderBottomRightRadius = 1;
                track.Add(marker);
                panel.Add(track);
                if (metric.IsBounds)
                    panel.Add(Note("Bounds maxima (Excellent / Good / Medium / Poor): 2.5 × 2.5 × 2.5 / 4 × 4 × 4 / 5 × 6 × 5 / 5 × 6 × 5 m"));
                else
                    panel.Add(Note("Thresholds: " + string.Join(" / ", limits.Select(x=>x.ToString("N0"))) + " → Very Poor above " + limits[3].ToString("N0")));
            }
            if(invalid) panel.Add(Note("Quest hard limit exceeded: " + metric.Value.ToString("N0") + " / " + QuestHardLimit(metric).Value.ToString("N0") + ". VRChat removes limited components on mobile."));
            else if(quest && QuestHardLimit(metric).HasValue && metric.Available) panel.Add(Note("Quest hard component limit: " + QuestHardLimit(metric).Value.ToString("N0")));
            if (!string.IsNullOrEmpty(metric.Note)) panel.Add(Note(metric.Note));
            parent.Add(panel);
        }

        private void BuildMeshes(VisualElement parent)
        {
            parent.Add(Header("Renderer / submeshes", "Triangles · slots"));
            foreach (var mesh in data.Meshes)
            {
                var entry = Entry(parent, mesh.Path.Split('/').Last(), mesh.Triangles.ToString("N0") + " tris  ·  " + mesh.Slots + " slots");
                entry.Add(Note("Hierarchy: " + mesh.Path));
                entry.Add(Text("Type: " + mesh.Type + "    Mesh: " + mesh.Name));
                entry.Add(Text("Submeshes", true));
                foreach (var sub in mesh.Submeshes) entry.Add(Text(sub));
                AddPing(entry, mesh.AssetPath, "Ping mesh in Project");
            }
        }

        private void BuildMaterials(VisualElement parent)
        {
            parent.Add(Header("Material / shader", "Slot references"));
            foreach (var mat in data.Materials)
            {
                var entry = Entry(parent, mat.Name, mat.Slots + " slots");
                entry.Add(Text("Shader: " + mat.Shader));
                entry.Add(Text("Texture properties (" + mat.Textures.Count + ")", true));
                foreach (var tex in mat.Textures) entry.Add(Text("• " + tex));
                AddPing(entry, mat.AssetPath, "Ping material in Project");
            }
        }

        private void BuildTextures(VisualElement parent)
        {
            parent.Add(Note("All unique textures · sorted by estimated GPU-format memory (largest first). Select a texture to view its material references."));
            parent.Add(Header("Texture / GPU format", "GPU · raw memory"));
            foreach (var tex in data.Textures.OrderByDescending(x => x.GpuBytes).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var label = tex.Name;
                if (tex.MaterialKeys.Count > 1) label += "  [shared]";
                var storedSize = tex.CrunchCompressed && tex.CrunchStorageBytes >= 0
                    ? " (" + MiB(tex.CrunchStorageBytes) + " compressed)" : "";
                var suffix = " - " + MiB(tex.Bytes);
                if (!string.IsNullOrEmpty(tex.DisplayType)) suffix += " - " + tex.DisplayType;
                suffix += " - " + label;
                var header = "<b>" + EscapeRichText(MiB(tex.GpuBytes))
                    + " VRAM</b>" + EscapeRichText(storedSize + suffix);
                var entry = RichEntry(parent, header);
                entry.Add(Text("Resolution: " + tex.Width + " × " + tex.Height + "    Format: " + tex.Format));
                entry.Add(Text("Dimension: " + tex.Dimension + "    Mip levels: " + tex.Mips + "    Referencing materials: " + tex.MaterialKeys.Count));
                if (tex.HasImporter)
                {
                    entry.Add(Text("Mip streaming: " + (tex.StreamingMipmaps ? "Enabled (priority " + tex.StreamingPriority + ")" : "Disabled")
                        + "    Mipmap filtering: " + (tex.Mips > 1 ? tex.MipmapFilter : "N/A (no mipmaps)")));
                    entry.Add(Text("Crunch compression: " + (tex.CrunchCompressed ? "Enabled (quality " + tex.CrunchQuality + ")" : "Disabled")
                        + (tex.CrunchCompressed && tex.CrunchStorageBytes >= 0 ? "    Imported compressed size: " + MiB(tex.CrunchStorageBytes) : "")));
                    if (tex.CrunchCompressed && tex.CrunchStorageBytes < 0)
                        entry.Add(Note("Imported Crunch storage size unavailable in this Unity Editor version."));
                }
                else entry.Add(Note("Texture importer settings unavailable for generated or embedded texture."));
                entry.Add(Text("Materials / shader properties (" + tex.References.Count + ")", true));
                foreach (var reference in tex.References) entry.Add(Text("• " + reference));
                AddPing(entry, tex.AssetPath, "Ping texture in Project");
                if (string.IsNullOrEmpty(tex.AssetPath)) entry.Add(Note("Generated or embedded texture; no persistent Project asset available."));
            }
        }

        private static VisualElement Stat(string label, string value)
        {
            var box = PanelBox();
            box.style.width = 153;
            box.style.marginRight = 5;
            box.style.marginBottom = 5;
            box.Add(Note(label));
            box.Add(Text(value, true, 15));
            return box;
        }

        private static VisualElement PanelBox()
        {
            var e = new VisualElement();
            e.style.backgroundColor = Panel;
            e.style.paddingLeft = 9;
            e.style.paddingRight = 9;
            e.style.paddingTop = 6;
            e.style.paddingBottom = 6;
            e.style.marginBottom = 4;
            return e;
        }

        private static VisualElement Horizontal()
        {
            return new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center } };
        }

        private static Label Text(string value, bool bold = false, int fontSize = 11)
        {
            var label = new Label(value);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = fontSize;
            label.style.marginBottom = 2;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        private static Label Note(string value)
        {
            var label = Text(value, false, 10);
            label.style.color = Muted;
            return label;
        }

        private static VisualElement Header(string title, string right)
        {
            var row = Horizontal();
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginTop = 4;
            row.style.marginBottom = 5;
            row.Add(Text(title, true));
            row.Add(Note(right));
            return row;
        }

        // Content is constructed only when the row first opens.
        private static string EscapeRichText(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        // Only texture rows need mixed emphasis; the existing foldout and its behavior stay intact.
        private static VisualElement RichEntry(VisualElement parent, string richHeader)
        {
            var fold = new Foldout { value = false, text = richHeader };
            fold.style.backgroundColor = parent.childCount % 2 == 0 ? Alternate : Panel;
            fold.style.marginBottom = 2;
            fold.style.paddingTop = 2;
            fold.style.paddingBottom = 2;
            fold.style.paddingRight = 5;
            var toggleLabel = fold.Q<Toggle>()?.Q<Label>();
            if (toggleLabel != null) toggleLabel.enableRichText = true;
            var details = new VisualElement();
            details.style.paddingLeft = 9;
            details.style.paddingTop = 4;
            details.style.unityFontStyleAndWeight = FontStyle.Normal;
            fold.Add(details);
            parent.Add(fold);
            return details;
        }

        private static VisualElement Entry(VisualElement parent, string title, string measure)
        {
            var fold = new Foldout { value = false, text = title + "     |     " + measure };
            fold.style.backgroundColor = parent.childCount % 2 == 0 ? Alternate : Panel;
            fold.style.marginBottom = 2;
            fold.style.paddingTop = 2;
            fold.style.paddingBottom = 2;
            fold.style.paddingRight = 5;
            // Give the principal value strong emphasis even where Foldout titles have editor-native styling.
            var details = new VisualElement();
            details.style.paddingLeft = 9;
            details.style.paddingTop = 4;
            fold.Add(details);
            details.style.unityFontStyleAndWeight = FontStyle.Normal;
            parent.Add(fold);
            return details;
        }

        private static void AddPing(VisualElement parent, string path, string title)
        {
            if (string.IsNullOrEmpty(path)) return;
            var button = new Button(() =>
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset != null) EditorGUIUtility.PingObject(asset);
            }) { text = title };
            button.style.alignSelf = Align.FlexStart;
            button.style.marginTop = 4;
            parent.Add(button);
        }

        internal static string MiB(long bytes) => (bytes / 1048576.0).ToString("N2") + " MiB";
    }

    public sealed class AvatarStatisticsFailure : IError
    {
        private readonly string message;
        public AvatarStatisticsFailure(string message) { this.message = message; }
        public ErrorSeverity Severity => ErrorSeverity.NonFatal;
        public void AddReference(ObjectReference obj) { }
        public VisualElement CreateVisualElement(ErrorReport report) => new Label("Nordic's Avatar Statistics failed: " + message);
        public string ToMessage() => "Nordic's Avatar Statistics failed: " + message;
    }
}
