using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FdDraft.Core.Standards
{
    public enum PaletteToolKind { Text, Line, Command }

    /// <summary>One button on the tool palette.</summary>
    public sealed class PaletteTool
    {
        public string Label { get; set; } = "";
        public PaletteToolKind Kind { get; set; } = PaletteToolKind.Command;
        /// <summary>Layer made current first (created if the drawing doesn't have it). Empty = leave as is.</summary>
        public string Layer { get; set; } = "";
        /// <summary>Text style for a text tool (the template's, e.g. L80-2.0MM). Empty = the drawing's current one.</summary>
        public string Style { get; set; } = "";
        /// <summary>Text height in paper mm, turned into drawing units at the sheet's scale. 0 = ask as usual.</summary>
        public double HeightMm { get; set; }
        /// <summary>For a command tool: what's typed at the command line (e.g. "AREA", "VPSCALE 1:250").</summary>
        public string Command { get; set; } = "";
        /// <summary>A short hover tip.</summary>
        public string Tip { get; set; } = "";
    }

    public sealed class PaletteTab
    {
        public string Name { get; set; } = "";
        public List<PaletteTool> Tools { get; } = new List<PaletteTool>();
    }

    /// <summary>
    /// The right-hand tool palette: tabs of one-click shortcuts, like MSCAD's tool palettes.
    /// Kept in an editable file (palette.ini) so a firm sets its own label and line presets:
    /// <code>
    /// [Text Styles]
    /// PART NUMBER = text | layer=PLAN-Other-Label | style=L100-2.5MM | height=2.5
    /// [Line Styles]
    /// LOT LINE = line | layer=PLAN-LotLine
    /// [Useful Tools]
    /// Inverse = INV
    /// </code>
    /// A value starting "text" or "line" is a preset (layer / style / height, then TEXT or LINE);
    /// anything else is typed at the command line.
    /// </summary>
    public sealed class ToolPalette
    {
        public List<PaletteTab> Tabs { get; } = new List<PaletteTab>();

        public static ToolPalette Parse(IEnumerable<string> lines)
        {
            var ini = IniFile.Parse(lines);
            var p = new ToolPalette();
            foreach (var section in ini.SectionNames)
            {
                if (section.Length == 0) continue;
                var tab = new PaletteTab { Name = section };
                foreach (var kv in ini.Section(section))
                {
                    var tool = ParseTool(kv.Key, kv.Value);
                    if (tool != null) tab.Tools.Add(tool);
                }
                p.Tabs.Add(tab);
            }
            return p;
        }

        private static PaletteTool? ParseTool(string label, string value)
        {
            label = label.Trim();
            if (label.Length == 0) return null;
            var parts = value.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (parts.Count == 0) return null;
            var tool = new PaletteTool { Label = label };
            string head = parts[0];
            if (head.Equals("text", StringComparison.OrdinalIgnoreCase)) tool.Kind = PaletteToolKind.Text;
            else if (head.Equals("line", StringComparison.OrdinalIgnoreCase)) tool.Kind = PaletteToolKind.Line;
            else { tool.Kind = PaletteToolKind.Command; tool.Command = head; }
            foreach (var opt in parts.Skip(1))
            {
                int eq = opt.IndexOf('=');
                if (eq <= 0) continue;
                string k = opt.Substring(0, eq).Trim().ToLowerInvariant(), v = opt.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "layer": tool.Layer = v; break;
                    case "style": tool.Style = v; break;
                    case "height": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double h) && h > 0) tool.HeightMm = h; break;
                    case "tip": tool.Tip = v; break;
                }
            }
            return tool;
        }

        /// <summary>Useful Tools added after a firm's palette.ini was first written - put into an
        /// existing file once (each is added only if its command isn't there yet).</summary>
        public static readonly (string Line, string Command)[] AddedTools =
        {
            ("Surveyor View = SV | tip=turn the plan so north isn't up - sheets and north arrow too", "SV"),
            ("World View (north up) = WV | tip=turn the plan back to north up", "WV"),
            ("Return to Surveyor View = RSV | tip=back to the last surveyor view", "RSV"),
            ("Text to Multiline Text = TXT2MTXT | tip=combine the selected texts into one", "TXT2MTXT"),
            ("Layers Off = LAYOFF | tip=pick entities to turn their layers off", "LAYOFF"),
        };

        /// <summary>
        /// An existing palette.ini with the newer Useful Tools added at the end of its
        /// [Useful Tools] tab (or a new tab if it has none). Returns the text unchanged when
        /// every one is already there - a firm's own edits are never touched.
        /// </summary>
        public static string WithAddedTools(string text)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            var existing = Parse(lines);
            var commands = new HashSet<string>(existing.Tabs.SelectMany(t => t.Tools).Where(t => t.Kind == PaletteToolKind.Command)
                .Select(t => t.Command.Split(' ')[0].ToUpperInvariant()));
            var missing = AddedTools.Where(a => !commands.Contains(a.Command)).Select(a => a.Line).ToList();
            if (missing.Count == 0) return text;
            int header = lines.FindIndex(l => l.Trim().Equals("[Useful Tools]", StringComparison.OrdinalIgnoreCase));
            if (header < 0)
            {
                lines.Add("");
                lines.Add("[Useful Tools]");
                lines.AddRange(missing);
            }
            else
            {
                // After the tab's last entry: before the next [section], skipping trailing blanks.
                int next = lines.FindIndex(header + 1, l => l.TrimStart().StartsWith("["));
                int at = next < 0 ? lines.Count : next;
                while (at > header + 1 && lines[at - 1].Trim().Length == 0) at--;
                lines.InsertRange(at, missing);
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// The palette FD-Draft starts with - the tabs and entries of Marc's MSCAD palette, pointed
        /// at the ProVision template's layers and styles where it has them. Every entry is only a
        /// starting guess at the firm's layer; edit palette.ini to match.
        /// </summary>
        public const string DefaultText = @"; FD-Draft tool palette - edit freely, FD-Draft rereads it when it starts.
; Each [section] is a tab. Each line is one button:
;   LABEL = text | layer=<layer> | style=<text style> | height=<paper mm> | tip=<hover text>
;       makes the layer current, then starts TEXT at that height (scaled to the sheet)
;   LABEL = line | layer=<layer> | tip=<hover text>
;       makes the layer current, then starts LINE
;   LABEL = <command>     types the command, e.g. AREA, INV, VPSCALE 1:250
; Layers the drawing doesn't have yet are created when first used.

[Text Styles]
BUILDING LABEL = text | layer=PLAN-Other-Label | style=L80-2.0MM | height=2.0
COMPARISON = text | layer=PLAN-BDComparison | style=L80-2.0MM | height=2.0 | tip=record vs. measured bearings/distances
DEDICATION TEXT = text | layer=PLAN-Other-Label | style=L100-2.5MM | height=2.5
FENCE LABEL = text | layer=PLAN-Other-Label | style=L60-1.5MM | height=1.5
INSTRUMENT NUMBER = text | layer=PLAN-Other-Label | style=L80-2.0MM | height=2.0
PART NUMBER = text | layer=PLAN-Other-Label | style=L100-2.5MM | height=2.5
REGISTERED PLAN = text | layer=PLAN-Other-Label | style=L100-2.5MM | height=2.5
PIN NUMBER = text | layer=PLAN-Other-Label | style=L80-2.0MM | height=2.0
ROAD NAME = text | layer=PLAN-Other-Label | style=L100-2.5MM | height=2.5
RPLAN TEXT = text | layer=PLAN-Other-Label | style=L80-2.0MM | height=2.0
TIES = text | layer=PLAN-Distance | style=L60-1.5MM | height=1.5
TOPO LABELING = text | layer=PLAN-Other-Label | style=L60-1.5MM | height=1.5
MONUMENT TEXT = text | layer=PLAN-Monument-Label | style=L80-2.0MM | height=2.0
NO PLOT = text | layer=Defpoints | style=L80-2.0MM | height=2.0 | tip=on Defpoints, which never plots

[Useful Tools]
Inverse = INV | tip=bearing and distance between points
ID point = ID | tip=read a point's N/E
Area = AREA | tip=area and perimeter
Label course = LABEL | tip=bearing/distance on the selected lines
Flip label = FLIP | tip=move labels to the other side of their line
House tie (dimension) = DIM | tip=aligned dimension between two points
Change to current layer = LAYER | tip=move the selection to the current layer
Join into polyline = JOIN
Offset = OFFSET
Trim = TRIM
Extend = EXTEND
Fillet = FILLET
Add viewport to sheet = MVIEW
Re-scale sheet = VPSCALE | tip=change the sheet's scale, title block and labels
Surveyor View = SV | tip=turn the plan so north isn't up - sheets and north arrow too
World View (north up) = WV | tip=turn the plan back to north up
Return to Surveyor View = RSV | tip=back to the last surveyor view
Text to Multiline Text = TXT2MTXT | tip=combine the selected texts into one
Layers Off = LAYOFF | tip=pick entities to turn their layers off
Print = PRINT

[Line Styles]
BOUNDARY = line | layer=PLAN-SubjectBoundary
RPLAN LINE = line | layer=PLAN-RPlan-Line
LOT LINE = line | layer=PLAN-Lot-Line
STREET LINE = line | layer=PLAN-Street-Line
";
    }
}
