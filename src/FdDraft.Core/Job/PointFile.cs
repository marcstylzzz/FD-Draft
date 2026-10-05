using System;
using System.Collections.Generic;
using System.Globalization;

namespace FdDraft.Core.Job
{
    /// <summary>
    /// Plain coordinate files - P,N,E,Z,D (FieldGenius, FD-Pro's export, most data collectors) or
    /// P,E,N,Z,D - comma, tab or space delimited, with an optional header line and quoted
    /// descriptions. Lines that don't parse are counted, not fatal.
    /// </summary>
    public static class PointFile
    {
        /// <param name="eastingFirst">True for P,E,N,Z,D files.</param>
        public static List<SurveyPoint> Parse(IEnumerable<string> lines, bool eastingFirst, out int skipped)
        {
            var result = new List<SurveyPoint>();
            skipped = 0;
            int auto = 0;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                var f = Split(line);
                if (f.Count < 3) { skipped++; continue; }
                if (!double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double a)
                    || !double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double b))
                {
                    // A header line ("P,N,E,Z,D") is passed over; anything else is a bad line.
                    if (!IsHeader(f)) skipped++;
                    continue;
                }
                double z = 0;
                if (f.Count > 3) double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
                int id = int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
                if (id <= 0) id = 900000 + ++auto; // non-numeric names keep a place, numbered out of the way
                result.Add(new SurveyPoint
                {
                    Id = id,
                    Northing = eastingFirst ? b : a,
                    Easting = eastingFirst ? a : b,
                    Elevation = z,
                    Code = f.Count > 4 ? f[4] : "",
                    Note = f.Count > 5 ? string.Join(" ", f.GetRange(5, f.Count - 5)) : (id > 900000 ? f[0] : ""),
                });
            }
            return result;
        }

        private static bool IsHeader(List<string> f)
        {
            foreach (var s in f)
                if (s.Length > 0 && char.IsDigit(s[0])) return false;
            return true;
        }

        /// <summary>Comma or tab separated (quotes honoured); otherwise runs of spaces.</summary>
        public static List<string> Split(string line)
        {
            var result = new List<string>();
            char sep = line.Contains(',') ? ',' : line.Contains('\t') ? '\t' : ' ';
            var cur = new System.Text.StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else quoted = !quoted; continue; }
                if (!quoted && c == sep)
                {
                    if (sep != ' ' || cur.Length > 0) result.Add(cur.ToString().Trim());
                    cur.Clear();
                    continue;
                }
                cur.Append(c);
            }
            if (sep != ' ' || cur.Length > 0) result.Add(cur.ToString().Trim());
            return result;
        }
    }
}
