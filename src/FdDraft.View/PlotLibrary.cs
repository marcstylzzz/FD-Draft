using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FdDraft.View
{
    /// <summary>A named paper size, in mm (portrait: width &lt;= height).</summary>
    public sealed class PaperSize
    {
        public string Name { get; }
        public double WidthMm { get; }
        public double HeightMm { get; }
        public PaperSize(string name, double w, double h) { Name = name; WidthMm = Math.Min(w, h); HeightMm = Math.Max(w, h); }
        public override string ToString() => Name;

        /// <summary>The sizes a survey office plots on: US letter/legal/tabloid, the ANSI and
        /// ARCH engineering sheets (R-plans are 22x34 / 24x36), and ISO A-sizes.</summary>
        public static readonly PaperSize[] Standard =
        {
            new PaperSize("Letter (8.5 x 11 in)", 215.9, 279.4),
            new PaperSize("Legal (8.5 x 14 in)", 215.9, 355.6),
            new PaperSize("Tabloid / 11x17 (11 x 17 in)", 279.4, 431.8),
            new PaperSize("ANSI C / 17x22 (17 x 22 in)", 431.8, 558.8),
            new PaperSize("ANSI D / 22x34 (22 x 34 in)", 558.8, 863.6),
            new PaperSize("ARCH D / 24x36 (24 x 36 in)", 609.6, 914.4),
            new PaperSize("ANSI E / 34x44 (34 x 44 in)", 863.6, 1117.6),
            new PaperSize("ARCH E / 36x48 (36 x 48 in)", 914.4, 1219.2),
            new PaperSize("A4 (210 x 297 mm)", 210, 297),
            new PaperSize("A3 (297 x 420 mm)", 297, 420),
            new PaperSize("A2 (420 x 594 mm)", 420, 594),
            new PaperSize("A1 (594 x 841 mm)", 594, 841),
            new PaperSize("A0 (841 x 1189 mm)", 841, 1189),
        };

        /// <summary>The standard size matching w x h mm (either way round, within 1.5 mm), if any.</summary>
        public static PaperSize? Match(double w, double h) =>
            Standard.FirstOrDefault(p => Math.Abs(p.WidthMm - Math.Min(w, h)) < 1.5 && Math.Abs(p.HeightMm - Math.Max(w, h)) < 1.5);
    }

    /// <summary>Finds plot style tables (.ctb) on this PC: FD-Draft's own folder, any folder
    /// the drafter added, the template's folder, and the "Plot Styles" folders AutoCAD and
    /// MicroSurvey keep under AppData / ProgramData.</summary>
    public static class PlotStyleLibrary
    {
        public static string OwnFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FD-Draft", "Plot Styles");

        /// <summary>Every .ctb found, by full path, first folder wins on a duplicate name.</summary>
        public static List<string> Find(IEnumerable<string> extraFolders)
        {
            var folders = new List<string> { OwnFolder };
            folders.AddRange(extraFolders.Where(f => !string.IsNullOrWhiteSpace(f)));
            foreach (var root in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            })
            {
                foreach (var vendor in new[] { "Autodesk", "MicroSurvey", "MicroSurvey Software Inc", "Bricsys" })
                {
                    var dir = Path.Combine(root ?? "", vendor);
                    if (Directory.Exists(dir)) folders.AddRange(PlotStyleFolders(dir, 6));
                }
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var f in folders.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                IEnumerable<string> files;
                try { files = Directory.Exists(f) ? Directory.EnumerateFiles(f, "*.ctb") : Enumerable.Empty<string>(); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { continue; }
                foreach (var file in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    if (seen.Add(Path.GetFileName(file))) result.Add(file);
            }
            return result;
        }

        /// <summary>Folders named "Plot Styles" (or "PlotStyles") under <paramref name="root"/>, to a depth.</summary>
        private static IEnumerable<string> PlotStyleFolders(string root, int depth)
        {
            var found = new List<string>();
            void Walk(string dir, int d)
            {
                if (d < 0) return;
                IEnumerable<string> subs;
                try { subs = Directory.EnumerateDirectories(dir).ToList(); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return; }
                foreach (var s in subs)
                {
                    var name = Path.GetFileName(s);
                    if (name.Equals("Plot Styles", StringComparison.OrdinalIgnoreCase) || name.Equals("PlotStyles", StringComparison.OrdinalIgnoreCase)) found.Add(s);
                    else Walk(s, d - 1);
                }
            }
            Walk(root, depth);
            return found;
        }
    }
}
