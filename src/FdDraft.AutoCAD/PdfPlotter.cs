using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FdDraft.AutoCAD
{
    /// <summary>
    /// Plots one layout to PDF at 1:1 with AutoCAD's own "DWG To PDF.pc3". Keeps the
    /// template's page setup when it already targets that device; otherwise switches
    /// device and picks the PDF paper size closest to the layout's own.
    /// </summary>
    internal static class PdfPlotter
    {
        public const string Device = "DWG To PDF.pc3";

        public static void Plot(Document doc, string layoutName, string pdfPath)
        {
            var db = doc.Database;
            object bg = AcApp.GetSystemVariable("BACKGROUNDPLOT");
            AcApp.SetSystemVariable("BACKGROUNDPLOT", 0);
            try
            {
                if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
                    throw new InvalidOperationException("Another plot is in progress.");

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var layoutId = SheetSetup.LayoutId(db, tr, layoutName);
                    var lay = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
                    var settings = new PlotSettings(lay.ModelType);
                    settings.CopyFrom(lay);
                    var psv = PlotSettingsValidator.Current;

                    if (!string.Equals(lay.PlotConfigurationName, Device, StringComparison.OrdinalIgnoreCase))
                    {
                        Point2d want = lay.PlotPaperSize;
                        psv.SetPlotConfigurationName(settings, Device, null);
                        psv.RefreshLists(settings);
                        string? best = null;
                        double bestErr = double.MaxValue;
                        foreach (string media in psv.GetCanonicalMediaNameList(settings))
                        {
                            psv.SetCanonicalMediaName(settings, media);
                            Point2d size = settings.PlotPaperSize;
                            double err = Math.Min(Math.Abs(size.X - want.X) + Math.Abs(size.Y - want.Y),
                                                  Math.Abs(size.X - want.Y) + Math.Abs(size.Y - want.X));
                            if (err < bestErr) { bestErr = err; best = media; }
                        }
                        if (best != null) psv.SetCanonicalMediaName(settings, best);
                    }
                    psv.SetPlotType(settings, Autodesk.AutoCAD.DatabaseServices.PlotType.Layout);
                    psv.SetUseStandardScale(settings, true);
                    psv.SetStdScaleType(settings, StdScaleType.StdScale1To1);

                    var info = new PlotInfo { Layout = layoutId, OverrideSettings = settings };
                    var validator = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled };
                    validator.Validate(info);

                    using (var engine = PlotFactory.CreatePublishEngine())
                    {
                        engine.BeginPlot(null, null);
                        engine.BeginDocument(info, doc.Name, null, 1, true, pdfPath);
                        engine.BeginPage(new PlotPageInfo(), info, true, null);
                        engine.BeginGenerateGraphics(null);
                        engine.EndGenerateGraphics(null);
                        engine.EndPage(null);
                        engine.EndDocument(null);
                        engine.EndPlot(null);
                    }
                    tr.Commit();
                }
            }
            finally
            {
                AcApp.SetSystemVariable("BACKGROUNDPLOT", bg);
            }
        }
    }
}
