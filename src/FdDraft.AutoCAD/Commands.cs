using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FdDraft.Core;
using FdDraft.Core.Job;
using FdDraft.Core.Layout;
using FdDraft.Core.Standards;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(FdDraft.AutoCAD.Commands))]

namespace FdDraft.AutoCAD
{
    /// <summary>
    /// FDDRAFT       - draft an FD-Pro job on the firm template, pick sheet and scale, save DWG (+ PDF)
    /// FDDRAFTSETUP  - choose the firm .dwt and standards file (remembered per user)
    /// </summary>
    public sealed class Commands
    {
        [CommandMethod("FDDRAFTSETUP", CommandFlags.Session)]
        public void Setup()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
            if (ed == null) return;
            var settings = PluginSettings.Load();
            if (!ChooseSetup(ed, settings, force: true)) return;
            settings.Save();
            ed.WriteMessage("\nFD-Draft will use:\n  template  " + settings.TemplatePath + "\n  standards " + settings.StandardsPath + "\n");
        }

        [CommandMethod("FDDRAFT", CommandFlags.Session)]
        public void Draft()
        {
            var docs = AcApp.DocumentManager;
            var ed = docs.MdiActiveDocument?.Editor;
            if (ed == null) return;
            var settings = PluginSettings.Load();
            if (!ChooseSetup(ed, settings, force: false)) return;

            // 1. The job.
            var pick = new PromptOpenFileOptions("\nFD-Pro job - pick its job.ini or points.csv")
            {
                DialogCaption = "FD-Draft: choose an FD-Pro job folder",
                Filter = "FD-Pro job files (job.ini;points.csv)|job.ini;points.csv|All files (*.*)|*.*",
                InitialDirectory = settings.LastJobFolder,
            };
            var jobFile = ed.GetFileNameForOpen(pick);
            if (jobFile.Status != PromptStatus.OK) return;

            FdJob job;
            FirmStandards std;
            try
            {
                job = FdJobReader.Read(jobFile.StringResult);
                std = FirmStandards.Load(settings.StandardsPath);
            }
            catch (System.Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                ed.WriteMessage("\nFD-Draft: " + e.Message);
                return;
            }
            settings.LastJobFolder = job.Folder;

            // 2. Sheet family (plan type).
            string? family = AskFamily(ed, std, settings.Family);
            if (family == null) return;
            settings.Family = family;
            settings.Save();

            // 3. Rank sheets and let the drafter accept or override.
            var result = DraftPipeline.Run(job, std, family);
            ed.WriteMessage("\n\nFD-Draft - " + job.Settings.Name + ": " + job.Points.Count + " points, " + job.Figures.Count + " figures");
            foreach (var c in result.Ranked)
                ed.WriteMessage("\n  " + c.Sheet.Layout.PadRight(14) + c.Scale.Label.PadRight(8) + c.Reason);
            if (result.Chosen == null)
            {
                foreach (var w in result.Document.Warnings) ed.WriteMessage("\n  ! " + w);
                return;
            }

            while (true)
            {
                var kw = new PromptKeywordOptions("\nDraft on " + result.Chosen!.Sheet.Layout + " at " + result.Chosen.Scale.Label + "? [Accept/Layout/Scale]", "Accept Layout Scale")
                {
                    AllowNone = true,
                };
                kw.Keywords.Default = "Accept";
                var answer = ed.GetKeywords(kw);
                if (answer.Status == PromptStatus.Cancel) return;
                string choice = answer.Status == PromptStatus.None ? "Accept" : answer.StringResult;
                if (choice == "Accept") break;
                if (choice == "Layout")
                {
                    var names = new List<string>();
                    foreach (var r in result.Ranked) names.Add(r.Sheet.Layout);
                    var lay = ed.GetString(new PromptStringOptions("\nLayout (" + string.Join(", ", names) + "): ") { AllowSpaces = false });
                    if (lay.Status != PromptStatus.OK) continue;
                    result = DraftPipeline.Run(job, std, family, lay.StringResult, null);
                }
                else
                {
                    var sc = ed.GetString(new PromptStringOptions("\nScale denominator (e.g. 300 for 1:300): ") { AllowSpaces = false });
                    if (sc.Status != PromptStatus.OK) continue;
                    result = DraftPipeline.Run(job, std, family, result.Chosen.Sheet.Layout, sc.StringResult);
                }
                foreach (var w in result.Document.Warnings) ed.WriteMessage("\n  ! " + w);
                if (result.Chosen == null) return;
            }

            // 4. New drawing from the firm template, drawn and set up.
            var report = new List<string>();
            Document doc;
            try { doc = DocumentCollectionExtension.Add(docs, settings.TemplatePath); }
            catch (Autodesk.AutoCAD.Runtime.Exception e) { ed.WriteMessage("\nFD-Draft: could not open the template: " + e.Message); return; }
            docs.MdiActiveDocument = doc;
            var ed2 = doc.Editor;
            var chosen = result.Chosen!;
            string outDir = Path.Combine(job.Folder, "export", "fd-draft");
            Directory.CreateDirectory(outDir);
            string baseName = SafeName(job.Settings.Name);
            string dwgPath = Path.Combine(outDir, baseName + ".dwg");

            using (doc.LockDocument())
            {
                var db = doc.Database;
                var previous = HostApplicationServices.WorkingDatabase;
                HostApplicationServices.WorkingDatabase = db;
                try
                {
                    ObjectId layoutId;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        layoutId = SheetSetup.LayoutId(db, tr, chosen.Sheet.Layout);
                        tr.Commit();
                    }
                    if (layoutId.IsNull)
                    {
                        ed2.WriteMessage("\nFD-Draft: the template has no layout named " + chosen.Sheet.Layout + " - check the standards' [sheet.*] names.");
                        return;
                    }
                    LayoutManager.Current.CurrentLayout = chosen.Sheet.Layout;

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var renderer = new AcadRenderer(db, tr, std, result.ModelPerMm, report);
                        if (std.ClearTemplateModelSpace) renderer.ClearModelSpace();
                        renderer.Draw(result.Document);
                        if (renderer.CreatedLayers.Count > 0)
                            report.Add("Layers not in the template were created: " + string.Join(", ", renderer.CreatedLayers));

                        var extents = result.Document.GeometryExtents();
                        SheetSetup.AddPlanViewport(db, tr, layoutId, chosen.Sheet.Area, extents, chosen.Scale, std.ViewportLayer, renderer);
                        SheetSetup.AddNorthArrow(db, tr, layoutId, chosen.Sheet.Area, std, report);

                        std.Sheets.TryGetValue(chosen.Sheet.Layout, out var def);
                        var filler = new TitleBlockFiller(std, job.Settings, chosen.Scale, chosen.Sheet.Layout, Path.GetFileName(dwgPath), DateTime.Today,
                                                          def?.PaperWidth ?? 0, def?.PaperHeight ?? 0);
                        int texts = SheetSetup.FillTitleBlock(tr, layoutId, filler, std, chosen.Scale);
                        report.Add("Title block: " + texts + " text(s) filled in.");

                        int frozen = SheetSetup.FreezeLayers(db, tr, result.Family?.FreezeLayers ?? new List<string>());
                        if (frozen > 0) report.Add("Froze " + frozen + " layer(s) for a " + result.Family!.Name + " plan.");
                        tr.Commit();
                    }

                    if (settings.DeleteOtherLayouts)
                    {
                        List<string> names;
                        using (var tr = db.TransactionManager.StartTransaction()) { names = SheetSetup.LayoutNames(db, tr); tr.Commit(); }
                        foreach (var n in names)
                            if (!string.Equals(n, chosen.Sheet.Layout, StringComparison.OrdinalIgnoreCase)) LayoutManager.Current.DeleteLayout(n);
                    }

                    db.SaveAs(dwgPath, true, DwgVersion.Current, db.SecurityParameters);
                    report.Add("Saved " + dwgPath);
                }
                finally
                {
                    HostApplicationServices.WorkingDatabase = previous;
                }
            }

            if (settings.PlotPdf)
            {
                string pdfPath = Path.ChangeExtension(dwgPath, ".pdf");
                try
                {
                    PdfPlotter.Plot(doc, chosen.Sheet.Layout, pdfPath);
                    report.Add("Plotted " + pdfPath);
                }
                catch (System.Exception e)
                {
                    report.Add("PDF not plotted: " + e.Message + " (the DWG is saved; plot it from AutoCAD).");
                }
            }

            ed2.WriteMessage("\n\nFD-Draft: " + job.Settings.Name + " on " + chosen.Sheet.Layout + " at " + chosen.Scale.Label);
            foreach (var w in result.Document.Warnings) ed2.WriteMessage("\n  ! " + w);
            foreach (var r in report) ed2.WriteMessage("\n  " + r);
            ed2.WriteMessage("\n");
        }

        // ---- prompts ---------------------------------------------------------------------

        private static bool ChooseSetup(Editor ed, PluginSettings s, bool force)
        {
            if (force || !File.Exists(s.TemplatePath))
            {
                var r = ed.GetFileNameForOpen(new PromptOpenFileOptions("\nFirm drawing template (.dwt)")
                {
                    DialogCaption = "FD-Draft: choose the firm's drawing template",
                    Filter = "Drawing template (*.dwt)|*.dwt",
                    InitialFileName = s.TemplatePath,
                });
                if (r.Status != PromptStatus.OK) return false;
                s.TemplatePath = r.StringResult;
            }
            if (force || !File.Exists(s.StandardsPath))
            {
                var r = ed.GetFileNameForOpen(new PromptOpenFileOptions("\nFD-Draft standards file (.ini)")
                {
                    DialogCaption = "FD-Draft: choose the standards file for this template",
                    Filter = "FD-Draft standards (*.ini)|*.ini",
                    InitialFileName = s.StandardsPath,
                });
                if (r.Status != PromptStatus.OK) return false;
                s.StandardsPath = r.StringResult;
            }
            s.Save();
            return true;
        }

        private static string? AskFamily(Editor ed, FirmStandards std, string last)
        {
            if (std.Families.Count == 0) return "";
            if (std.Families.Count == 1) return std.Families[0].Name;
            var names = new List<string>();
            foreach (var f in std.Families) names.Add(Capitalise(f.Name));
            var kw = new PromptKeywordOptions("\nPlan type [" + string.Join("/", names) + "]", string.Join(" ", names)) { AllowNone = true };
            var def = std.Family(string.IsNullOrEmpty(last) ? std.DefaultFamily : last);
            if (def != null) kw.Keywords.Default = Capitalise(def.Name);
            var r = ed.GetKeywords(kw);
            if (r.Status == PromptStatus.Cancel) return null;
            string chosen = r.Status == PromptStatus.None ? (def?.Name ?? std.Families[0].Name) : r.StringResult;
            return chosen.ToLowerInvariant();
        }

        private static string Capitalise(string s) =>
            s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.InvariantCulture) + s.Substring(1).ToLowerInvariant();

        private static string SafeName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Length == 0 ? "plan" : name;
        }
    }
}
