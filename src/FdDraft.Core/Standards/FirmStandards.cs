using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FdDraft.Core.Standards
{
    /// <summary>
    /// A plain INI file: [section] headers, key=value lines, ; or # comments.
    /// Plain text for the same reason FD-Pro's job files are: a drafter can open it
    /// in Notepad, read it, and fix it. Repeated keys are kept in order.
    /// </summary>
    public sealed class IniFile
    {
        private readonly Dictionary<string, List<KeyValuePair<string, string>>> _sections =
            new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _order = new List<string>();

        public static IniFile Parse(IEnumerable<string> lines)
        {
            var ini = new IniFile();
            string section = "";
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    ini.Entries(section, create: true);
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = StripComment(line.Substring(eq + 1)).Trim();
                ini.Entries(section, create: true)!.Add(new KeyValuePair<string, string>(key, value));
            }
            return ini;
        }

        /// <summary>Only " ;" starts a trailing comment, so a value may still contain a semicolon.</summary>
        private static string StripComment(string value)
        {
            int i = value.IndexOf(" ;", StringComparison.Ordinal);
            return i >= 0 ? value.Substring(0, i) : value;
        }

        public IEnumerable<string> SectionNames => _order;

        public List<KeyValuePair<string, string>>? Entries(string section, bool create = false)
        {
            if (_sections.TryGetValue(section, out var list)) return list;
            if (!create) return null;
            list = new List<KeyValuePair<string, string>>();
            _sections[section] = list;
            _order.Add(section);
            return list;
        }

        public IEnumerable<KeyValuePair<string, string>> Section(string section) =>
            Entries(section) ?? new List<KeyValuePair<string, string>>();

        public string? Get(string section, string key)
        {
            var list = Entries(section);
            if (list == null) return null;
            string? found = null; // last one wins
            foreach (var kv in list) if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) found = kv.Value;
            return found;
        }

        public double GetDouble(string section, string key, double fallback) =>
            double.TryParse(Get(section, key), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

        public int GetInt(string section, string key, int fallback) =>
            int.TryParse(Get(section, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

        public bool GetBool(string section, string key, bool fallback)
        {
            var v = Get(section, key);
            if (v == null) return fallback;
            v = v.Trim().ToLowerInvariant();
            if (v == "1" || v == "true" || v == "yes" || v == "on") return true;
            if (v == "0" || v == "false" || v == "no" || v == "off") return false;
            return fallback;
        }

        public string GetString(string section, string key, string fallback) => Get(section, key) ?? fallback;
    }

    /// <summary>A candidate drawing scale. ModelPerPaper = model units per paper unit (1:500 metric = 0.5 m per mm).</summary>
    public sealed class ScaleOption
    {
        public string Label { get; set; } = "";
        public double Denominator { get; set; }
        public double ModelPerPaper { get; set; }
        public override string ToString() => Label;
    }

    /// <summary>An axis-aligned rectangle in paper units.</summary>
    public struct Rect
    {
        public double X1, Y1, X2, Y2;
        public Rect(double x1, double y1, double x2, double y2)
        {
            X1 = Math.Min(x1, x2); Y1 = Math.Min(y1, y2); X2 = Math.Max(x1, x2); Y2 = Math.Max(y1, y2);
        }
        public double Width => X2 - X1;
        public double Height => Y2 - Y1;
        public double Area => Width * Height;
        public bool Overlaps(Rect o) => X1 < o.X2 - 1e-6 && o.X1 < X2 - 1e-6 && Y1 < o.Y2 - 1e-6 && o.Y1 < Y2 - 1e-6;
        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.#},{1:0.#})-({2:0.#},{3:0.#}) {4:0.#}x{5:0.#}", X1, Y1, X2, Y2, Width, Height);

        public static bool TryParse(string text, out Rect r)
        {
            r = default;
            var p = text.Split(',');
            if (p.Length != 4) return false;
            var v = new double[4];
            for (int i = 0; i < 4; i++)
                if (!double.TryParse(p[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return false;
            r = new Rect(v[0], v[1], v[2], v[3]);
            return r.Width > 0 && r.Height > 0;
        }
    }

    /// <summary>
    /// One paper-space layout of the template: its drawing frame and the areas
    /// inside it the plan must stay out of (title column, schedule box).
    /// </summary>
    public sealed class SheetDefinition
    {
        public string Layout { get; set; } = "";
        public Rect Frame { get; set; }
        public List<Rect> Keepouts { get; } = new List<Rect>();
        public double PaperWidth { get; set; }
        public double PaperHeight { get; set; }
    }

    public sealed class SheetFamily
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Layouts { get; } = new List<string>();
        /// <summary>Layer patterns frozen in the finished drawing for this kind of plan.</summary>
        public List<string> FreezeLayers { get; } = new List<string>();
    }

    /// <summary>
    /// Everything firm-specific that the .dwt does not already say: which codes get
    /// bearings and distances, how codes map to layers and blocks, text heights and
    /// styles, the scales the firm uses, where the plan goes on each sheet, and how
    /// the title-block text is filled in. The .dwt supplies the layers, linetypes,
    /// text styles, blocks and title blocks themselves.
    /// </summary>
    public sealed class FirmStandards
    {
        public string Name { get; set; } = "";

        // [general]
        public int DistanceDecimals { get; set; } = 2;
        public int BearingSecondsDecimals { get; set; }
        /// <summary>Added to every grid bearing before it is printed, for plans on a rotated (astronomic/deed) bearing reference.</summary>
        public double BearingRotationDeg { get; set; }
        /// <summary>"traverse": bearing in the direction the figure was shot. "reading": in the direction the text reads.</summary>
        public string BearingDirection { get; set; } = "traverse";
        /// <summary>Divide printed distances by the job's scale factor (grid to ground). Leave off when the job is already on ground.</summary>
        public bool GridToGround { get; set; }
        public bool DrawMeasuredLines { get; set; }
        /// <summary>Erase whatever sample content the template has in model space before drawing.</summary>
        public bool ClearTemplateModelSpace { get; set; }

        // [text] heights in paper millimetres
        public double BearingTextMm { get; set; } = 2.0;
        public double DistanceTextMm { get; set; } = 2.0;
        public double ArcTextMm { get; set; } = 2.0;
        public double PointNumberTextMm { get; set; } = 1.5;
        public double ElevationTextMm { get; set; } = 1.5;
        public double MonumentTextMm { get; set; } = 2.0;
        public double AreaTextMm { get; set; } = 2.5;
        public double LabelGapFactor { get; set; } = 0.5;
        /// <summary>Fallback symbol size (paper mm) for codes with no template block.</summary>
        public double SymbolSizeMm { get; set; } = 1.5;
        /// <summary>Template block insert scale per model-unit-per-mm: 1 when blocks are drawn 1 unit = 1 paper mm.</summary>
        public double BlockUnitMm { get; set; } = 1.0;

        // [text-styles]
        public Dictionary<string, string> TextStyles { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // [layers]
        public string BearingLayer { get; set; } = "PLAN-Bearing";
        public string DistanceLayer { get; set; } = "PLAN-Distance";
        public string ArcLayer { get; set; } = "PLAN-Distance";
        public string MonumentLabelLayer { get; set; } = "PLAN-Monument-Label";
        public string AreaLayer { get; set; } = "PLAN-Other-Label";
        public string DefaultLineLayer { get; set; } = "MISCELLANEOUS-LINE";

        // [point-layers] patterns with {feature} and {layer}
        public string PointSymbolPattern { get; set; } = "MSPOINT-{feature}";
        public string PointNumberPattern { get; set; } = "POINTNUMBER-{feature}";
        public string PointElevationPattern { get; set; } = "ELEVATION-{feature}";

        public Dictionary<string, string> FeatureMap { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public WildcardMap LineLayers { get; } = new WildcardMap();
        public WildcardMap PointSymbolLayers { get; } = new WildcardMap();
        public WildcardMap SymbolBlocks { get; } = new WildcardMap();
        public WildcardMap Monuments { get; } = new WildcardMap();

        // [labels]
        public List<string> LabelCodes { get; } = new List<string>();
        public bool LabelAreaFigures { get; set; } = true;
        public bool AreaLabels { get; set; } = true;
        public string AreaFormat { get; set; } = "AREA = {m2} m²";
        public bool PointNumbers { get; set; } = true;
        public bool PointElevations { get; set; } = true;
        /// <summary>Where a point's elevation sits, as an angle on the plan (degrees counter-clockwise
        /// from the plan's right; 45 = up-right). Kept on the plan through Surveyor View.</summary>
        public double ElevationAngleDeg { get; set; } = 45;
        /// <summary>Where a point's number sits, likewise (-45 = down-right, clear of the elevation).</summary>
        public double PointNumberAngleDeg { get; set; } = -45;
        public string ElevationFormat { get; set; } = "F2";
        public List<string> NoElevationCodes { get; } = new List<string>();
        public double MinLabelledCourseMm { get; set; } = 4.0;
        /// <summary>Monument label. Tokens: {mon} from [monuments], {note} the point's FD-Pro note.</summary>
        public string MonumentFormat { get; set; } = "{mon}";

        // [scales]
        public List<ScaleOption> Scales { get; } = new List<ScaleOption>();
        /// <summary>Paper units per millimetre: 1 for metric (paper in mm), 1/25.4 for imperial (paper in inches).</summary>
        public double PaperUnitsPerMm { get; set; } = 1.0;

        // [sheet]
        public double MarginPct { get; set; } = 3;
        public double MinLegibleFraction { get; set; } = 0.9;
        public string DefaultFamily { get; set; } = "";
        /// <summary>Remove the template's other layouts from the finished plan.</summary>
        public bool DeleteOtherLayouts { get; set; } = true;
        public List<SheetFamily> Families { get; } = new List<SheetFamily>();
        public Dictionary<string, SheetDefinition> Sheets { get; } = new Dictionary<string, SheetDefinition>(StringComparer.OrdinalIgnoreCase);

        // [viewport]
        /// <summary>Layer the plan viewport goes on - one that never plots.</summary>
        public string ViewportLayer { get; set; } = "Defpoints";
        // [north-arrow]
        public string NorthArrowBlock { get; set; } = "";
        public double NorthArrowScale { get; set; } = 1;
        /// <summary>Paper offset (mm) of the arrow's insertion point from the plan area's top-left corner.</summary>
        public double NorthArrowOffsetX { get; set; } = 15;
        public double NorthArrowOffsetY { get; set; } = 25;

        // [titleblock-replace]: regex = replacement, applied to every paper-space text of the chosen layout.
        public List<KeyValuePair<string, string>> TitleBlockReplacements { get; } = new List<KeyValuePair<string, string>>();
        // [scalebar]
        public bool ScaleBarRelabel { get; set; } = true;
        public string ScaleBarAnchor { get; set; } = "SCALE 1:#";

        public static FirmStandards Load(string path)
        {
            var s = FromIni(IniFile.Parse(File.ReadAllLines(path)));
            if (s.Name.Length == 0) s.Name = Path.GetFileNameWithoutExtension(path);
            return s;
        }

        public static FirmStandards Default() => FromIni(IniFile.Parse(new string[0]));

        public static FirmStandards FromIni(IniFile ini)
        {
            var s = new FirmStandards();
            s.Name = ini.GetString("general", "name", "");
            s.DistanceDecimals = ini.GetInt("general", "distance_decimals", s.DistanceDecimals);
            s.BearingSecondsDecimals = ini.GetInt("general", "bearing_seconds_decimals", s.BearingSecondsDecimals);
            s.BearingRotationDeg = ini.GetDouble("general", "bearing_rotation_deg", s.BearingRotationDeg);
            s.BearingDirection = ini.GetString("general", "bearing_direction", s.BearingDirection).Trim().ToLowerInvariant();
            s.GridToGround = ini.GetBool("general", "grid_to_ground", s.GridToGround);
            s.DrawMeasuredLines = ini.GetBool("general", "draw_measured_lines", s.DrawMeasuredLines);
            s.ClearTemplateModelSpace = ini.GetBool("general", "clear_template_model_space", s.ClearTemplateModelSpace);

            s.BearingTextMm = ini.GetDouble("text", "bearing", s.BearingTextMm);
            s.DistanceTextMm = ini.GetDouble("text", "distance", s.DistanceTextMm);
            s.ArcTextMm = ini.GetDouble("text", "arc", s.ArcTextMm);
            s.PointNumberTextMm = ini.GetDouble("text", "point_number", s.PointNumberTextMm);
            s.ElevationTextMm = ini.GetDouble("text", "elevation", s.ElevationTextMm);
            s.MonumentTextMm = ini.GetDouble("text", "monument", s.MonumentTextMm);
            s.AreaTextMm = ini.GetDouble("text", "area", s.AreaTextMm);
            s.LabelGapFactor = ini.GetDouble("text", "label_gap_factor", s.LabelGapFactor);
            s.SymbolSizeMm = ini.GetDouble("text", "symbol_size", s.SymbolSizeMm);
            s.BlockUnitMm = ini.GetDouble("text", "block_unit_mm", s.BlockUnitMm);

            foreach (var kv in ini.Section("text-styles")) s.TextStyles[kv.Key] = kv.Value;

            s.BearingLayer = ini.GetString("layers", "bearing", s.BearingLayer);
            s.DistanceLayer = ini.GetString("layers", "distance", s.DistanceLayer);
            s.ArcLayer = ini.GetString("layers", "arc", s.ArcLayer);
            s.MonumentLabelLayer = ini.GetString("layers", "monument_label", s.MonumentLabelLayer);
            s.AreaLayer = ini.GetString("layers", "area", s.AreaLayer);
            s.DefaultLineLayer = ini.GetString("layers", "default_line", s.DefaultLineLayer);

            s.PointSymbolPattern = ini.GetString("point-layers", "symbol", s.PointSymbolPattern);
            s.PointNumberPattern = ini.GetString("point-layers", "number", s.PointNumberPattern);
            s.PointElevationPattern = ini.GetString("point-layers", "elevation", s.PointElevationPattern);

            foreach (var kv in ini.Section("feature-map")) s.FeatureMap[kv.Key] = kv.Value;
            foreach (var kv in ini.Section("line-layers")) s.LineLayers.Add(kv.Key, kv.Value);
            foreach (var kv in ini.Section("point-symbol-layers")) s.PointSymbolLayers.Add(kv.Key, kv.Value);
            foreach (var kv in ini.Section("symbols")) s.SymbolBlocks.Add(kv.Key, kv.Value);
            foreach (var kv in ini.Section("monuments")) s.Monuments.Add(kv.Key, kv.Value);

            AddList(s.LabelCodes, ini.Get("labels", "label_codes"));
            s.LabelAreaFigures = ini.GetBool("labels", "label_area_figures", s.LabelAreaFigures);
            s.AreaLabels = ini.GetBool("labels", "area_label", s.AreaLabels);
            s.AreaFormat = ini.GetString("labels", "area_format", s.AreaFormat);
            s.PointNumbers = ini.GetBool("labels", "point_numbers", s.PointNumbers);
            s.PointElevations = ini.GetBool("labels", "point_elevations", s.PointElevations);
            s.ElevationAngleDeg = ini.GetDouble("labels", "elevation_angle", s.ElevationAngleDeg);
            s.PointNumberAngleDeg = ini.GetDouble("labels", "point_number_angle", s.PointNumberAngleDeg);
            s.ElevationFormat = ini.GetString("labels", "elevation_format", s.ElevationFormat);
            AddList(s.NoElevationCodes, ini.Get("labels", "no_elevation_codes"));
            s.MinLabelledCourseMm = ini.GetDouble("labels", "min_labelled_course_mm", s.MinLabelledCourseMm);
            s.MonumentFormat = ini.GetString("labels", "monument_format", s.MonumentFormat);

            string units = ini.GetString("scales", "units", "metric").Trim().ToLowerInvariant();
            s.PaperUnitsPerMm = units == "imperial" ? 1.0 / 25.4 : 1.0;
            string list = ini.GetString("scales", "list",
                units == "imperial" ? "10,20,30,40,50,60,100,200" : "100,150,200,250,300,400,500,600,750,1000,1250,1500,2000,2500,3000,4000,5000");
            foreach (var item in list.Split(','))
            {
                if (!double.TryParse(item.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || n <= 0) continue;
                s.Scales.Add(units == "imperial"
                    ? new ScaleOption { Label = "1\" = " + item.Trim() + "'", Denominator = n, ModelPerPaper = n }       // paper in, model ft
                    : new ScaleOption { Label = "1:" + item.Trim(), Denominator = n, ModelPerPaper = n / 1000.0 });   // paper mm, model m
            }
            s.Scales.Sort((a, b) => a.ModelPerPaper.CompareTo(b.ModelPerPaper));

            s.MarginPct = ini.GetDouble("sheet", "margin_pct", s.MarginPct);
            s.MinLegibleFraction = ini.GetDouble("sheet", "min_legible_fraction", s.MinLegibleFraction);
            s.DefaultFamily = ini.GetString("sheet", "default_family", s.DefaultFamily);
            s.DeleteOtherLayouts = ini.GetBool("sheet", "delete_other_layouts", s.DeleteOtherLayouts);

            foreach (var section in ini.SectionNames)
            {
                if (section.StartsWith("family.", StringComparison.OrdinalIgnoreCase))
                {
                    var f = new SheetFamily { Name = section.Substring(7) };
                    f.Description = ini.GetString(section, "description", "");
                    AddList(f.Layouts, ini.Get(section, "layouts"));
                    AddList(f.FreezeLayers, ini.Get(section, "freeze"));
                    s.Families.Add(f);
                }
                else if (section.StartsWith("sheet.", StringComparison.OrdinalIgnoreCase))
                {
                    var d = new SheetDefinition { Layout = section.Substring(6) };
                    foreach (var kv in ini.Section(section))
                    {
                        if (kv.Key.Equals("frame", StringComparison.OrdinalIgnoreCase) && Rect.TryParse(kv.Value, out var fr)) d.Frame = fr;
                        else if (kv.Key.StartsWith("keepout", StringComparison.OrdinalIgnoreCase) && Rect.TryParse(kv.Value, out var ko)) d.Keepouts.Add(ko);
                        else if (kv.Key.Equals("paper", StringComparison.OrdinalIgnoreCase))
                        {
                            var p = kv.Value.ToLowerInvariant().Split('x');
                            if (p.Length == 2 &&
                                double.TryParse(p[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double pw) &&
                                double.TryParse(p[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double ph))
                            {
                                d.PaperWidth = pw; d.PaperHeight = ph;
                            }
                        }
                    }
                    if (d.Frame.Width > 0) s.Sheets[d.Layout] = d;
                }
            }

            s.ViewportLayer = ini.GetString("viewport", "layer", s.ViewportLayer);
            s.NorthArrowBlock = ini.GetString("north-arrow", "block", s.NorthArrowBlock);
            s.NorthArrowScale = ini.GetDouble("north-arrow", "scale", s.NorthArrowScale);
            s.NorthArrowOffsetX = ini.GetDouble("north-arrow", "offset_x", s.NorthArrowOffsetX);
            s.NorthArrowOffsetY = ini.GetDouble("north-arrow", "offset_y", s.NorthArrowOffsetY);

            foreach (var kv in ini.Section("titleblock-replace")) s.TitleBlockReplacements.Add(kv);
            s.ScaleBarRelabel = ini.GetBool("scalebar", "relabel", s.ScaleBarRelabel);
            s.ScaleBarAnchor = ini.GetString("scalebar", "anchor", s.ScaleBarAnchor);
            return s;
        }

        private static void AddList(List<string> into, string? csv)
        {
            if (csv == null) return;
            foreach (var c in csv.Split(',')) if (c.Trim().Length > 0) into.Add(c.Trim());
        }

        public SheetFamily? Family(string? name)
        {
            if (string.IsNullOrEmpty(name)) name = DefaultFamily;
            foreach (var f in Families) if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f;
            return Families.Count > 0 && string.IsNullOrEmpty(name) ? Families[0] : null;
        }

        public string TextStyle(string kind) => TextStyles.TryGetValue(kind, out var s) ? s : "";

        /// <summary>Case-insensitive match with * and ? wildcards.</summary>
        public static bool WildcardMatch(string pattern, string text)
        {
            pattern = pattern.ToUpperInvariant();
            text = text.ToUpperInvariant();
            int p = 0, t = 0, star = -1, mark = 0;
            while (t < text.Length)
            {
                if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == text[t])) { p++; t++; }
                else if (p < pattern.Length && pattern[p] == '*') { star = p++; mark = t; }
                else if (star >= 0) { p = star + 1; t = ++mark; }
                else return false;
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }

        public static bool MatchesAny(IEnumerable<string> patterns, string text)
        {
            foreach (var pattern in patterns) if (WildcardMatch(pattern, text.Trim())) return true;
            return false;
        }

        public bool IsLabelledCode(string code) => MatchesAny(LabelCodes, code);
    }
}
