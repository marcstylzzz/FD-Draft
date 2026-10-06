using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FdDraft.Core.Job
{
    /// <summary>One stored point, as FD-Pro's points.csv has it. Coordinates are in the job's units.</summary>
    public sealed class SurveyPoint
    {
        public int Id { get; set; }
        /// <summary>The point's name when it isn't a plain number ("CP1", "BM2") - such points get an Id out of the way (900001...).</summary>
        public string Name { get; set; } = "";
        public double Northing { get; set; }
        public double Easting { get; set; }
        public double Elevation { get; set; }
        /// <summary>The code picked from FD-Pro's map toolbar (PNEZD "D").</summary>
        public string Code { get; set; } = "";
        /// <summary>FD-Pro's "Point Notes" free text. Stored in points.csv under the Description header.</summary>
        public string Note { get; set; } = "";
        public string Time { get; set; } = "";
        public string VerticalDatum { get; set; } = "";
    }

    /// <summary>
    /// One figure from figures.csv. Mode is FD-Pro's DrawMode name:
    /// STRAIGHT, SPLINE, ARC, MEASURED or AREA.
    /// </summary>
    public sealed class Figure
    {
        public string Mode { get; set; } = "STRAIGHT";
        public List<int> PointIds { get; set; } = new List<int>();
        public long? ColorArgb { get; set; }
        /// <summary>The code of the point that started the line. Never changes midway (FD-Pro round 99).</summary>
        public string Code { get; set; } = "";
        /// <summary>An AREA figure deliberately left unfinished. It asserts no boundary and has no area.</summary>
        public bool Open { get; set; }
        /// <summary>A 3-point ARC closed into a full circle.</summary>
        public bool Circle { get; set; }
        /// <summary>Per-span shapes (STRAIGHT/SPLINE/ARC) for a mixed figure. Null for a single-shape figure.</summary>
        public List<string>? Segments { get; set; }
    }

    /// <summary>One row of the job's codes.csv (MSCAD columns followed by FD-Pro's own).</summary>
    public sealed class FeatureCode
    {
        public string Key { get; set; } = "";
        public string Description { get; set; } = "";
        public string LayerName { get; set; } = "";
        public string LineType { get; set; } = "";
        public int Aci { get; set; } = 7;
        public bool ConnectLines { get; set; }
        public string Symbol { get; set; } = "DOT";
        public string PointColorHex { get; set; } = "";
        public string LineColorHex { get; set; } = "";
    }

    public enum JobUnits { Meters, UsSurveyFeet, InternationalFeet }

    /// <summary>The parts of job.ini the drafting engine needs.</summary>
    public sealed class JobSettings
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Surveyor { get; set; } = "";
        public string Description { get; set; } = "";
        public string Notes { get; set; } = "";
        public JobUnits Units { get; set; } = JobUnits.Meters;
        public string CoordinateSystem { get; set; } = "";
        public string CoordinateZone { get; set; } = "";
        public double ScaleFactor { get; set; } = 1.0;
        public string VerticalDatum { get; set; } = "";
        public bool Is2D { get; set; }
        /// <summary>Every key in the file, including ones this build does not know about.</summary>
        public Dictionary<string, string> Raw { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A whole FD-Pro job folder, read-only. FD-Draft never writes into the job's own files.</summary>
    public sealed class FdJob
    {
        public string Folder { get; set; } = "";
        public JobSettings Settings { get; set; } = new JobSettings();
        public List<SurveyPoint> Points { get; } = new List<SurveyPoint>();
        public List<Figure> Figures { get; } = new List<Figure>();
        public List<FeatureCode> Codes { get; } = new List<FeatureCode>();
        /// <summary>Rows that could not be read, with the file they came from, so a loss is never silent.</summary>
        public List<string> Warnings { get; } = new List<string>();

        private Dictionary<int, SurveyPoint>? _byId;
        private Dictionary<string, FeatureCode>? _codeByKey;

        public SurveyPoint? Point(int id)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<int, SurveyPoint>();
                // Last row wins, the same as FD-Pro's own in-memory list after an edit.
                foreach (var p in Points) _byId[p.Id] = p;
            }
            return _byId.TryGetValue(id, out var point) ? point : null;
        }

        public FeatureCode? Code(string key)
        {
            if (_codeByKey == null)
            {
                _codeByKey = new Dictionary<string, FeatureCode>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in Codes)
                {
                    var k = NormalizeCode(c.Key);
                    if (k.Length > 0 && !_codeByKey.ContainsKey(k)) _codeByKey[k] = c;
                }
            }
            var n = NormalizeCode(key);
            return _codeByKey.TryGetValue(n, out var code) ? code : null;
        }

        /// <summary>
        /// Codes are matched trimmed, case-insensitive, with '_' and '-' treated alike
        /// (FD-Pro round 97: typing BLDG_NOELEV has to find BLDG-NOELEV).
        /// </summary>
        public static string NormalizeCode(string code) => (code ?? "").Trim().Replace('_', '-').ToUpperInvariant();

        public void InvalidateIndexes()
        {
            _byId = null;
            _codeByKey = null;
        }
    }

    public static class FdJobReader
    {
        public const string SettingsFile = "job.ini";
        public const string PointsFile = "points.csv";
        public const string FiguresFile = "figures.csv";
        public const string CodesFile = "codes.csv";

        /// <summary>The newest job.ini format this build understands (FD-Pro JobFolder.FORMAT_VERSION).</summary>
        public const int SupportedFormatVersion = 1;

        /// <summary>
        /// Reads a job folder. Accepts the folder itself or any file inside it
        /// (people will pick job.ini or points.csv in a file dialog).
        /// </summary>
        public static FdJob Read(string folderOrFile)
        {
            string folder = Directory.Exists(folderOrFile) ? folderOrFile : (Path.GetDirectoryName(folderOrFile) ?? folderOrFile);
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Job folder not found: " + folder);

            var job = new FdJob { Folder = folder };
            string ini = Path.Combine(folder, SettingsFile);
            if (File.Exists(ini)) job.Settings = ReadSettings(File.ReadAllLines(ini), job.Warnings);
            else job.Warnings.Add(SettingsFile + " is missing; job name and units defaulted (metres).");
            if (string.IsNullOrEmpty(job.Settings.Name)) job.Settings.Name = new DirectoryInfo(folder).Name;

            string points = Path.Combine(folder, PointsFile);
            if (!File.Exists(points)) throw new FileNotFoundException("This folder has no " + PointsFile + " - is it an FD-Pro job folder?", points);
            ReadPoints(File.ReadAllLines(points), job.Points, job.Warnings);

            string figures = Path.Combine(folder, FiguresFile);
            if (File.Exists(figures)) ReadFigures(File.ReadAllLines(figures), job.Figures, job.Warnings);

            string codes = Path.Combine(folder, CodesFile);
            if (File.Exists(codes)) ReadCodes(File.ReadAllLines(codes), job.Codes, job.Warnings);
            else job.Warnings.Add(CodesFile + " is missing; layers will be named after the codes themselves.");

            job.InvalidateIndexes();
            return job;
        }

        public static JobSettings ReadSettings(IEnumerable<string> lines, List<string> warnings)
        {
            var s = new JobSettings();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                s.Raw[line.Substring(0, eq).Trim()] = Unescape(line.Substring(eq + 1));
            }
            string Get(string key) => s.Raw.TryGetValue(key, out var v) ? v : "";

            if (int.TryParse(Get("FormatVersion"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int version) && version > SupportedFormatVersion)
            {
                warnings.Add("job.ini is format " + version + "; FD-Draft reads format " + SupportedFormatVersion + ". Settings may be misread - update FD-Draft.");
            }
            s.Id = Get("Id");
            s.Name = Get("Name");
            s.Surveyor = Get("Surveyor");
            s.Description = Get("Description");
            s.Notes = Get("Notes");
            s.CoordinateSystem = Get("CoordinateSystem");
            s.CoordinateZone = Get("CoordinateSystemZone");
            s.VerticalDatum = Get("VerticalDatum");
            s.Is2D = string.Equals(Get("Is2D"), "true", StringComparison.OrdinalIgnoreCase);
            if (double.TryParse(Get("ScaleFactor"), NumberStyles.Float, CultureInfo.InvariantCulture, out double sf) && sf > 0) s.ScaleFactor = sf;
            switch (Get("Units"))
            {
                case "US_SURVEY_FEET": s.Units = JobUnits.UsSurveyFeet; break;
                case "INTERNATIONAL_FEET": s.Units = JobUnits.InternationalFeet; break;
                default: s.Units = JobUnits.Meters; break;
            }
            return s;
        }

        /// <summary>FD-Pro's job.ini escaping: backslash, \n and \r only.</summary>
        private static string Unescape(string value)
        {
            if (value.IndexOf('\\') < 0) return value;
            var sb = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (ch == '\\' && i + 1 < value.Length)
                {
                    char next = value[i + 1];
                    if (next == 'n') { sb.Append('\n'); i++; continue; }
                    if (next == 'r') { sb.Append('\r'); i++; continue; }
                    if (next == '\\') { sb.Append('\\'); i++; continue; }
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }

        public static void ReadPoints(IList<string> lines, List<SurveyPoint> into, List<string> warnings)
        {
            var rows = NonBlank(lines);
            if (rows.Count == 0) return;
            var header = new Csv.Header(rows[0]);
            int id = header.IndexOfAny("Point", "PNT", "PT", "ID");
            int n = header.IndexOfAny("Northing", "N");
            int e = header.IndexOfAny("Easting", "E");
            int z = header.IndexOfAny("Elevation", "Z", "H");
            int code = header.IndexOf("Code");
            int desc = header.IndexOf("Description");
            int time = header.IndexOf("Time");
            int datum = header.IndexOf("VerticalDatum");
            int start = 1;
            if (id < 0 || n < 0 || e < 0)
            {
                // A bare PNEZD file with no header: point,northing,easting,elevation,code.
                // FD-Pro reads these too (PointsFormat round 176), so FD-Draft does.
                var first = Csv.Split(rows[0]);
                if (first.Count >= 3 && TryInt(first[0], out _) && TryDouble(first[1], out _))
                {
                    id = 0; n = 1; e = 2; z = 3; code = 4; desc = -1; time = -1; datum = -1; start = 0;
                }
                else
                {
                    warnings.Add(PointsFile + ": unrecognised header '" + rows[0] + "'; no points read.");
                    return;
                }
            }
            for (int r = start; r < rows.Count; r++)
            {
                var cells = Csv.Split(rows[r]);
                if (!TryInt(Csv.Cell(cells, id), out int pid) ||
                    !TryDouble(Csv.Cell(cells, n), out double northing) ||
                    !TryDouble(Csv.Cell(cells, e), out double easting))
                {
                    warnings.Add(PointsFile + ": skipped unreadable row '" + rows[r] + "'");
                    continue;
                }
                TryDouble(Csv.Cell(cells, z), out double elev);
                into.Add(new SurveyPoint
                {
                    Id = pid,
                    Northing = northing,
                    Easting = easting,
                    Elevation = elev,
                    Code = Csv.Cell(cells, code).Trim(),
                    Note = Csv.Cell(cells, desc),
                    Time = Csv.Cell(cells, time),
                    VerticalDatum = Csv.Cell(cells, datum),
                });
            }
        }

        public static void ReadFigures(IList<string> lines, List<Figure> into, List<string> warnings)
        {
            var rows = NonBlank(lines);
            if (rows.Count == 0) return;
            var header = new Csv.Header(rows[0]);
            int mode = header.IndexOf("Mode");
            int color = header.IndexOf("ColorArgb");
            int desc = header.IndexOf("Description");
            int ids = header.IndexOf("PointIds");
            int open = header.IndexOf("Open");
            int circle = header.IndexOf("Circle");
            int segments = header.IndexOf("Segments");
            if (mode < 0 || ids < 0)
            {
                warnings.Add(FiguresFile + ": unrecognised header '" + rows[0] + "'; no linework read.");
                return;
            }
            for (int r = 1; r < rows.Count; r++)
            {
                var cells = Csv.Split(rows[r]);
                var pointIds = new List<int>();
                foreach (var token in Csv.Cell(cells, ids).Trim().Split(' '))
                {
                    if (TryInt(token, out int pid)) pointIds.Add(pid);
                }
                bool isOpen = Flag(Csv.Cell(cells, open));
                // Same rule as FD-Pro: a finished figure needs two points, an open one needs one.
                if (pointIds.Count < (isOpen ? 1 : 2))
                {
                    warnings.Add(FiguresFile + ": skipped figure with too few points '" + rows[r] + "'");
                    continue;
                }
                List<string>? spans = null;
                var segText = Csv.Cell(cells, segments).Trim();
                if (segText.Length > 0)
                {
                    var list = new List<string>();
                    foreach (var t in segText.Split(' ')) if (t.Length > 0) list.Add(t.Trim().ToUpperInvariant());
                    if (list.Count == pointIds.Count - 1) spans = list;
                }
                long? argb = null;
                if (long.TryParse(Csv.Cell(cells, color).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long c)) argb = c;
                into.Add(new Figure
                {
                    Mode = Csv.Cell(cells, mode).Trim().ToUpperInvariant(),
                    PointIds = pointIds,
                    ColorArgb = argb,
                    Code = Csv.Cell(cells, desc).Trim(),
                    Open = isOpen,
                    Circle = Flag(Csv.Cell(cells, circle)),
                    Segments = spans,
                });
            }
        }

        public static void ReadCodes(IList<string> lines, List<FeatureCode> into, List<string> warnings)
        {
            var rows = NonBlank(lines);
            if (rows.Count == 0) return;
            var header = new Csv.Header(rows[0]);
            int key = header.IndexOfAny("KEY", "CODE");
            int desc = header.IndexOfAny("DESC TEXT", "DESCRIPTION", "DESC");
            int layer = header.IndexOfAny("LINE LAYER", "LAYER");
            int ltype = header.IndexOfAny("LINE TYPE", "LINETYPE");
            int aci = header.IndexOfAny("COLOR", "COLOUR");
            int lines1 = header.IndexOf("CONNECT");
            int lines2 = header.IndexOf("LINES");
            int symbol = header.IndexOf("SYMBOL");
            int pcolor = header.IndexOf("POINT COLOR");
            int lcolor = header.IndexOf("LINE COLOR");
            if (key < 0)
            {
                warnings.Add(CodesFile + ": no KEY column; code library ignored.");
                return;
            }
            for (int r = 1; r < rows.Count; r++)
            {
                var cells = Csv.Split(rows[r]);
                var k = Csv.Cell(cells, key).Trim();
                if (k.Length == 0) continue;
                int colorIndex = TryInt(Csv.Cell(cells, aci), out int a) && a >= 0 && a <= 256 ? a : 7;
                string connect = lines1 >= 0 ? Csv.Cell(cells, lines1) : Csv.Cell(cells, lines2);
                into.Add(new FeatureCode
                {
                    Key = k,
                    Description = Csv.Cell(cells, desc).Trim(),
                    LayerName = Csv.Cell(cells, layer).Trim(),
                    LineType = Csv.Cell(cells, ltype).Trim(),
                    Aci = colorIndex,
                    ConnectLines = connect.Trim() == "1",
                    Symbol = symbol >= 0 && Csv.Cell(cells, symbol).Trim().Length > 0 ? Csv.Cell(cells, symbol).Trim().ToUpperInvariant() : "DOT",
                    PointColorHex = Csv.Cell(cells, pcolor).Trim(),
                    LineColorHex = Csv.Cell(cells, lcolor).Trim(),
                });
            }
        }

        private static List<string> NonBlank(IList<string> lines)
        {
            var rows = new List<string>();
            foreach (var l in lines) if (!string.IsNullOrWhiteSpace(l)) rows.Add(l);
            // A UTF-8 BOM from Excel must not become part of the first header name.
            if (rows.Count > 0 && rows[0].Length > 0 && rows[0][0] == '﻿') rows[0] = rows[0].Substring(1);
            return rows;
        }

        private static bool Flag(string s)
        {
            s = s.Trim();
            return s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryInt(string s, out int value) =>
            int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        internal static bool TryDouble(string s, out double value) =>
            double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
