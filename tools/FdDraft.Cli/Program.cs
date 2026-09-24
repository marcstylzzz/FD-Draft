using System;
using System.Globalization;
using System.IO;
using FdDraft.Core;
using FdDraft.Core.Export;
using FdDraft.Core.Job;
using FdDraft.Core.Standards;

namespace FdDraft.Cli
{
    public static class Program
    {
        private const string Usage =
@"fddraft <job-folder> --standards <file.ini> [options]

  --family <name>    sheet family from the standards (topo, rplan ...)
  --layout <name>    force a layout (17X22, RPLAN-22X34 ...)
  --scale <n>        force a scale (500 or 1:500)
  --out <folder>     where to write the DXF, SVG and report (default: <job>\export\fd-draft)

Reads an FD-Pro job folder, ranks every sheet and scale in the family, drafts the
plan at the winner and writes <job>.dxf (open in any CAD program), <job>.svg (a
preview of the plan area on the chosen sheet) and <job>.report.txt.";

        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "-h" || args[0] == "--help") { Console.WriteLine(Usage); return args.Length == 0 ? 2 : 0; }
            string job = args[0];
            string? standards = null, family = null, layout = null, scale = null, outDir = null;
            for (int i = 1; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException(args[i] + " needs a value");
                switch (args[i])
                {
                    case "--standards": standards = Next(); break;
                    case "--family": family = Next(); break;
                    case "--layout": layout = Next(); break;
                    case "--scale": scale = Next(); break;
                    case "--out": outDir = Next(); break;
                    default: Console.Error.WriteLine("Unknown option " + args[i]); return 2;
                }
            }

            try
            {
                var std = standards != null ? FirmStandards.Load(standards) : FirmStandards.Default();
                var fdJob = FdJobReader.Read(job);
                var result = DraftPipeline.Run(fdJob, std, family, layout, scale);
                outDir ??= Path.Combine(fdJob.Folder, "export", "fd-draft");
                Directory.CreateDirectory(outDir);
                string baseName = Safe(fdJob.Settings.Name);

                var report = new StringWriter(CultureInfo.InvariantCulture);
                report.WriteLine("FD-Draft " + typeof(DraftPipeline).Assembly.GetName().Version);
                report.WriteLine("Job:        " + fdJob.Settings.Name + "  (" + fdJob.Points.Count + " points, " + fdJob.Figures.Count + " figures)");
                report.WriteLine("Standards:  " + std.Name);
                report.WriteLine("Family:     " + (result.Family?.Name ?? "(all layouts)"));
                var ext = result.Document.GeometryExtents();
                report.WriteLine(string.Format(CultureInfo.InvariantCulture, "Extents:    {0:F2} m E-W x {1:F2} m N-S", ext.Width, ext.Height));
                report.WriteLine();
                report.WriteLine("Sheet ranking:");
                foreach (var c in result.Ranked)
                    report.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-14} {1,-7} area {2}  {3}", c.Sheet.Layout, c.Scale.Label, c.Sheet.Area, c.Reason));
                report.WriteLine();
                if (result.Chosen != null)
                {
                    report.WriteLine("Chosen:     " + result.Chosen.Sheet.Layout + " at " + result.Chosen.Scale.Label);
                    report.WriteLine();
                    report.WriteLine("Parcels:");
                    foreach (var p in result.Document.Parcels)
                        report.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-8} area {1:F1} m2, perimeter {2:F3} m, points {3}", p.Code, p.Area, p.Perimeter, string.Join(" ", p.PointIds)));
                }
                if (result.Document.Warnings.Count > 0)
                {
                    report.WriteLine();
                    report.WriteLine("Warnings:");
                    foreach (var w in result.Document.Warnings) report.WriteLine("  - " + w);
                }
                Console.Write(report.ToString());
                File.WriteAllText(Path.Combine(outDir, baseName + ".report.txt"), report.ToString());

                if (result.Chosen == null) return 1;
                DxfWriter.Write(result.Document, Path.Combine(outDir, baseName + ".dxf"), result.ModelPerMm);
                var area = result.Chosen.Sheet.Area;
                SvgPreview.Write(result.Document, Path.Combine(outDir, baseName + ".svg"), area.Width, area.Height, result.ModelPerMm,
                    fdJob.Settings.Name + " - " + result.Chosen.Sheet.Layout + " plan area at " + result.Chosen.Scale.Label);
                Console.WriteLine();
                Console.WriteLine("Wrote " + Path.Combine(outDir, baseName) + ".dxf / .svg / .report.txt");
                return 0;
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException)
            {
                Console.Error.WriteLine("fddraft: " + e.Message);
                return 1;
            }
        }

        private static string Safe(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Length == 0 ? "plan" : name;
        }
    }
}
