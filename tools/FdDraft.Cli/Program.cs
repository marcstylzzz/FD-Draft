using System;
using System.Globalization;
using System.IO;
using FdDraft.Cad;
using FdDraft.Core;
using FdDraft.Core.Export;
using FdDraft.Core.Job;
using FdDraft.Core.Layout;
using FdDraft.Core.Standards;

namespace FdDraft.Cli
{
    public static class Program
    {
        private const string Usage =
@"fddraft <job-folder> --template <firm.dwt> --standards <file.ini> [options]
fddraft inspect <firm.dwt>
fddraft render <drawing.dwg|dwt> <Model|layout> <out.svg>

  --template <dwt>   the firm's drawing template - the plan is drafted into a copy of it
  --standards <ini>  the firm standards file for that template
  --family <name>    plan type from the standards (topo, rplan ...)
  --layout <name>    force a layout (17X22, RPLAN-22X34 ...)
  --scale <n>        force a scale (500 or 1:500)
  --out <folder>     where to write the output (default: <job>\export\fd-draft)
  --dxf              also write a template-free DXF of the plan

Drafting writes:
  <job>.dwg         the plan, on the firm template: layers, blocks, viewport at scale,
                    title block filled in - opens in any DWG program
  <job>.pdf         the sheet as a true-scale vector PDF
  <job>.svg         a preview of the plan area on the chosen sheet
  <job>.report.txt  sheet ranking, parcels, what was created or missing

'inspect' lists a template's layouts, paper sizes, frames and free areas, and
prints a starter [sheet.*] block for a new firm's standards file.";

        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "-h" || args[0] == "--help") { Console.WriteLine(Usage); return args.Length == 0 ? 2 : 0; }
            try
            {
                if (args[0] == "inspect")
                {
                    if (args.Length < 2) { Console.Error.WriteLine("fddraft inspect <firm.dwt>"); return 2; }
                    Console.Write(TemplateInspector.Describe(args[1]));
                    return 0;
                }
                if (args[0] == "render")
                {
                    if (args.Length < 4) { Console.Error.WriteLine("fddraft render <drawing> <Model|layout> <out.svg>"); return 2; }
                    var doc = ACadSharp.IO.DwgReader.Read(args[1]);
                    var b = new FdDraft.View.SceneBuilder(doc);
                    var scene = args[2].Equals("Model", StringComparison.OrdinalIgnoreCase) ? b.Model() : b.Layout(args[2]);
                    FdDraft.View.SvgSceneWriter.Write(scene, args[3], 2000);
                    foreach (var n in scene.Notes) Console.WriteLine(n);
                    Console.WriteLine("Wrote " + args[3]);
                    return 0;
                }
                return Draft(args);
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                Console.Error.WriteLine("fddraft: " + e.Message);
                return 1;
            }
        }

        private static int Draft(string[] args)
        {
            string job = args[0];
            string? standards = null, family = null, layout = null, scale = null, outDir = null, template = null;
            bool dxf = false;
            for (int i = 1; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException(args[i] + " needs a value");
                switch (args[i])
                {
                    case "--standards": standards = Next(); break;
                    case "--template": template = Next(); break;
                    case "--family": family = Next(); break;
                    case "--layout": layout = Next(); break;
                    case "--scale": scale = Next(); break;
                    case "--out": outDir = Next(); break;
                    case "--dxf": dxf = true; break;
                    default: Console.Error.WriteLine("Unknown option " + args[i]); return 2;
                }
            }

            var std = standards != null ? FirmStandards.Load(standards) : FirmStandards.Default();
            var fdJob = FdJobReader.Read(job);
            var result = DraftPipeline.Run(fdJob, std, family, layout, scale);
            outDir ??= Path.Combine(fdJob.Folder, "export", "fd-draft");
            Directory.CreateDirectory(outDir);
            string baseName = Safe(fdJob.Settings.Name);

            var report = new StringWriter(CultureInfo.InvariantCulture);
            report.WriteLine("FD-Draft " + typeof(DraftPipeline).Assembly.GetName().Version?.ToString(3));
            report.WriteLine("Job:        " + fdJob.Settings.Name + "  (" + fdJob.Points.Count + " points, " + fdJob.Figures.Count + " figures)");
            report.WriteLine("Standards:  " + std.Name);
            report.WriteLine("Template:   " + (template ?? "(none - preview only)"));
            report.WriteLine("Plan type:  " + (result.Family?.Name ?? "(all layouts)"));
            var ext = result.Document.GeometryExtents();
            report.WriteLine(string.Format(CultureInfo.InvariantCulture, "Extents:    {0:F2} m E-W x {1:F2} m N-S", ext.Width, ext.Height));
            report.WriteLine();
            report.WriteLine("Sheet ranking:");
            foreach (var c in result.Ranked)
                report.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-14} {1,-7} area {2}  {3}", c.Sheet.Layout, c.Scale.Label, c.Sheet.Area, c.Reason));
            if (result.Chosen != null)
            {
                report.WriteLine();
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

            if (result.Chosen != null)
            {
                var area = result.Chosen.Sheet.Area;
                SvgPreview.Write(result.Document, Path.Combine(outDir, baseName + ".svg"), area.Width, area.Height, result.ModelPerMm,
                    fdJob.Settings.Name + " - " + result.Chosen.Sheet.Layout + " plan area at " + result.Chosen.Scale.Label);
                if (dxf) DxfWriter.Write(result.Document, Path.Combine(outDir, baseName + ".dxf"), result.ModelPerMm);

                if (template != null)
                {
                    string dwg = Path.Combine(outDir, baseName + ".dwg");
                    std.Sheets.TryGetValue(result.Chosen.Sheet.Layout, out var def);
                    var filler = new TitleBlockFiller(std, fdJob.Settings, result.Chosen.Scale, result.Chosen.Sheet.Layout,
                        Path.GetFileName(dwg), DateTime.Today, def?.PaperWidth ?? 0, def?.PaperHeight ?? 0);
                    var drafter = TemplateDrafter.Open(template, std);
                    var notes = drafter.Draft(result, filler);
                    drafter.Save(dwg);
                    // The sheet as a true-scale vector PDF, painted from the drafted drawing itself.
                    string pdf = Path.ChangeExtension(dwg, ".pdf");
                    var sheet = new FdDraft.View.SceneBuilder(drafter.Document).Layout(result.Chosen.Sheet.Layout);
                    FdDraft.View.PdfSceneWriter.Write(sheet, pdf, fdJob.Settings.Name + " - " + result.Chosen.Sheet.Layout + " " + result.Chosen.Scale.Label);
                    report.WriteLine();
                    report.WriteLine("Drawing:");
                    foreach (var n in notes) report.WriteLine("  " + n);
                    report.WriteLine("  Saved " + dwg);
                    report.WriteLine("  Plotted " + pdf + " (true scale)");
                }
            }

            Console.Write(report.ToString());
            File.WriteAllText(Path.Combine(outDir, baseName + ".report.txt"), report.ToString());
            return result.Chosen == null ? 1 : 0;
        }

        private static string Safe(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Length == 0 ? "plan" : name;
        }
    }
}
