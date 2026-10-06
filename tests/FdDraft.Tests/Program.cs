using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FdDraft.Cad;
using FdDraft.Cad.Editing;
using FdDraft.Core;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;
using FdDraft.Core.Job;
using FdDraft.Core.Layout;
using FdDraft.Core.Standards;
using FdDraft.View;

namespace FdDraft.Tests
{
    /// <summary>Every public static void method named Test* is a test.</summary>
    public static class Program
    {
        public static int Main()
        {
            int pass = 0, fail = 0;
            foreach (var m in typeof(Tests).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name.StartsWith("Test")).OrderBy(m => m.Name))
            {
                try { m.Invoke(null, null); pass++; Console.WriteLine("  ok    " + m.Name); }
                catch (TargetInvocationException e) { fail++; Console.WriteLine("  FAIL  " + m.Name + ": " + e.InnerException?.Message); }
            }
            Console.WriteLine();
            Console.WriteLine(pass + " passed, " + fail + " failed");
            return fail == 0 ? 0 : 1;
        }
    }

    internal static class Assert
    {
        public static void True(bool c, string what) { if (!c) throw new Exception(what); }
        public static void Equal<T>(T expected, T actual, string what = "")
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception(what + " expected <" + expected + "> got <" + actual + ">");
        }
        public static void Near(double expected, double actual, double tol, string what = "")
        {
            if (Math.Abs(expected - actual) > tol) throw new Exception(what + " expected " + expected + " got " + actual);
        }
    }

    public static partial class Tests
    {
        private static string Root()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "Directory.Build.props"))) dir = Path.GetDirectoryName(dir);
            return dir ?? throw new Exception("repo root not found");
        }

        private static string Demo => Path.Combine(Root(), "samples", "demo-lot");
        private static FirmStandards ProVision => FirmStandards.Load(Path.Combine(Root(), "standards", "provision-2024.standards.ini"));
        private static double Deg(double d, double m = 0, double s = 0) => (d + m / 60 + s / 3600) * Math.PI / 180;

        // ---- bearings -------------------------------------------------------------------

        public static void TestBearingQuadrants()
        {
            Assert.Equal("N00°00'00\"E", Angles.FormatBearing(0));
            Assert.Equal("N45°30'00\"E", Angles.FormatBearing(Deg(45, 30)));
            Assert.Equal("S10°30'30\"E", Angles.FormatBearing(Deg(180) - Deg(10, 30, 30)));
            Assert.Equal("S10°30'30\"W", Angles.FormatBearing(Deg(180) + Deg(10, 30, 30)));
            Assert.Equal("N01°02'03\"W", Angles.FormatBearing(Deg(360) - Deg(1, 2, 3)));
        }

        public static void TestBearingSecondsCarryNeverPrints60()
        {
            Assert.Equal("N12°35'00\"E", Angles.FormatBearing(Deg(12, 34, 59.7)));
            Assert.Equal("N13°00'00\"E", Angles.FormatBearing(Deg(12, 59, 59.6)));
            Assert.Equal("N12°34'59.7\"E", Angles.FormatBearing(Deg(12, 34, 59.7), 1));
        }

        public static void TestReadableRotationNeverUpsideDown()
        {
            double r = Angles.ReadableRotation(new Vec2(10, 0), new Vec2(0, 0)); // drawn right-to-left
            Assert.Near(0, r, 1e-9, "westward line reads left to right");
            r = Angles.ReadableRotation(new Vec2(0, 10), new Vec2(0, 0));       // drawn downward
            Assert.Near(Math.PI / 2, r, 1e-9, "vertical text reads bottom to top");
        }

        // ---- arcs & areas ---------------------------------------------------------------

        public static void TestThreePointArcDirectionAndBulge()
        {
            // Upper half circle, left to right through the top: clockwise, sweep -180.
            var arc = Arc.ThroughThreePoints(new Vec2(-1, 0), new Vec2(0, 1), new Vec2(1, 0))!;
            Assert.Near(1, arc.Radius, 1e-9, "radius");
            Assert.Near(-Math.PI, arc.Sweep, 1e-9, "sweep");
            Assert.Near(-1, arc.Bulge, 1e-9, "bulge of a clockwise semicircle");
            Assert.Near(Math.PI, arc.Length, 1e-9, "length");
            // Same three points the other way round: counter-clockwise.
            var back = Arc.ThroughThreePoints(new Vec2(1, 0), new Vec2(0, 1), new Vec2(-1, 0))!;
            Assert.Near(Math.PI, back.Sweep, 1e-9, "ccw sweep");
            Assert.True(Arc.ThroughThreePoints(new Vec2(0, 0), new Vec2(1, 1), new Vec2(2, 2)) == null, "collinear -> null");
        }

        public static void TestArcSplitKeepsTotalLength()
        {
            var arc = Arc.ThroughThreePoints(new Vec2(0, 0), new Vec2(10, 1.18), new Vec2(20.11, 0.52))!;
            var halves = arc.SplitAt(new Vec2(10, 1.18));
            Assert.Near(arc.Length, halves[0].Length + halves[1].Length, 1e-9, "split length");
            Assert.Near(10, halves[0].End.X, 1e-9, "split point");
        }

        public static void TestAreaWithArcSegment()
        {
            // A half disc of radius 1: diameter from (1,0) to (-1,0), then a CCW semicircle back.
            var v = new List<Vec2> { new Vec2(-1, 0), new Vec2(1, 0) };
            var b = new List<double> { 0, 1 }; // second segment (1,0)->(-1,0) bulge 1 = CCW semicircle through (0,1)
            Assert.Near(Math.PI / 2, Polygon.SignedArea(v, b), 1e-9, "half disc");
        }

        // ---- job reader -----------------------------------------------------------------

        public static void TestReadsDemoJob()
        {
            var job = FdJobReader.Read(Demo);
            Assert.Equal("24-0DEMO", job.Settings.Name);
            Assert.Equal(21, job.Points.Count, "points");
            Assert.Equal(4, job.Figures.Count, "figures");
            Assert.Equal("(OU)", job.Point(1)!.Note, "note column");
            var boundary = job.Figures[0];
            Assert.Equal(5, boundary.Segments!.Count, "segments");
            Assert.Equal("PLAN-MONUMENT", job.Code("fdib")!.LayerName, "code lookup is case-insensitive");
            Assert.Near(0.9996, job.Settings.ScaleFactor, 1e-9, "scale factor");
        }

        public static void TestHeaderlessPnezdAndBom()
        {
            var list = new List<SurveyPoint>();
            var warn = new List<string>();
            FdJobReader.ReadPoints(new[] { "1,100.0,200.0,10.0,FDIB", "2,110.0,200.0,,SETIB" }, list, warn);
            Assert.Equal(2, list.Count, "bare PNEZD");
            Assert.Equal("SETIB", list[1].Code);
            list.Clear();
            FdJobReader.ReadPoints(new[] { "﻿Point,Northing,Easting,Elevation,Code", "7,1,2,3,X" }, list, warn);
            Assert.Equal(7, list[0].Id, "BOM stripped from header");
        }

        public static void TestCodeNormalisationMatchesFdPro()
        {
            Assert.Equal(FdJob.NormalizeCode("bldg_noelev"), FdJob.NormalizeCode("BLDG-NOELEV"));
        }

        // ---- building -------------------------------------------------------------------

        public static void TestBoundaryFigureBecomesClosedParcelOnBoundaryLayer()
        {
            var job = FdJobReader.Read(Demo);
            var doc = DraftBuilder.Build(job, ProVision);
            var boundary = doc.Entities.OfType<DraftPolyline>().First(p => p.Code == "FDIB");
            Assert.Equal("PLAN-SubjectBoundary", boundary.Layer, "boundary layer");
            Assert.True(boundary.Closed, "closed");
            Assert.Equal(5, boundary.Vertices.Count, "returning point is not duplicated");
            // Point 2 is a monument in the middle of the arc: two arc spans, both curved.
            Assert.True(boundary.Bulges[0] != 0 && boundary.Bulges[1] != 0 && boundary.Bulges[2] == 0, "arc bulges");
            var parcel = doc.Parcels.First(p => p.Code == "FDIB");
            Assert.True(parcel.Area > 800 && parcel.Area < 840, "parcel area " + parcel.Area);
            Assert.Equal(5, doc.Courses.Count(c => c.Labelled), "labelled courses: arcs 1-2, 2-3, then 3-4, 4-5, 5-1");
        }

        public static void TestPointLayersFollowTemplateScheme()
        {
            var job = FdJobReader.Read(Demo);
            var doc = DraftBuilder.Build(job, ProVision);
            var syms = doc.Entities.OfType<DraftSymbol>().ToDictionary(s => s.PointId);
            Assert.Equal("MSPOINT-MONUMENT", syms[1].Layer, "monument symbol layer via feature-map");
            Assert.Equal("POINTNUMBER-MONUMENT", syms[1].NumberLayer);
            Assert.Equal("PLAN-FOUND MONUMENT", syms[1].BlockName);
            Assert.Equal("PLAN-SET MONUMENT", syms[3].BlockName, "SET* wildcard");
            Assert.Equal("SIB", syms[2].MonumentText);
            Assert.Equal("MSPOINT-FENCECHAINLINK", syms[20].Layer, "dashes removed");
            Assert.Equal("TOPO-MANHOLE", syms[30].BlockName);
            Assert.True(!syms[1].ShowElevation && syms[30].ShowElevation, "no elevations on monuments");
        }

        public static void TestMixedFigureOddArcSpanIsStraight()
        {
            var job = new FdJob();
            for (int i = 1; i <= 4; i++) job.Points.Add(new SurveyPoint { Id = i, Northing = i * i, Easting = i * 10, Code = "BDY" });
            job.Figures.Add(new Figure { Mode = "STRAIGHT", Code = "BDY", PointIds = new List<int> { 1, 2, 3, 4 }, Segments = new List<string> { "STRAIGHT", "ARC", "ARC" } });
            job.InvalidateIndexes();
            var doc = DraftBuilder.Build(job, ProVision);
            var pl = doc.Entities.OfType<DraftPolyline>().Single();
            Assert.Equal(3, pl.Vertices.Count, "1 straight + one 3-point arc = 3 vertices");
            job.Figures[0].Segments = new List<string> { "ARC", "ARC", "ARC" }; // odd arc span at the end -> straight
            doc = DraftBuilder.Build(job, ProVision);
            pl = doc.Entities.OfType<DraftPolyline>().Single();
            Assert.True(pl.Bulges[0] != 0 && pl.Bulges[1] == 0, "arc then straight leftover");
        }

        public static void TestSharedCourseLabelledOnce()
        {
            var job = new FdJob();
            job.Points.Add(new SurveyPoint { Id = 1, Northing = 0, Easting = 0, Code = "FDIB" });
            job.Points.Add(new SurveyPoint { Id = 2, Northing = 0, Easting = 30, Code = "FDIB" });
            job.Points.Add(new SurveyPoint { Id = 3, Northing = 30, Easting = 0, Code = "FDIB" });
            job.Points.Add(new SurveyPoint { Id = 4, Northing = -30, Easting = 0, Code = "FDIB" });
            job.Figures.Add(new Figure { Mode = "STRAIGHT", Code = "FDIB", PointIds = new List<int> { 1, 2, 3, 1 } });
            job.Figures.Add(new Figure { Mode = "STRAIGHT", Code = "FDIB", PointIds = new List<int> { 2, 1, 4, 2 } });
            job.InvalidateIndexes();
            var doc = DraftBuilder.Build(job, ProVision);
            Assert.Equal(5, doc.Courses.Count, "1-2 shared between the two lots");
        }

        // ---- standards ------------------------------------------------------------------

        public static void TestWildcardMapExactBeatsPattern()
        {
            var std = ProVision;
            Assert.Equal("BUILDING-FOUNDATION", std.LineLayers.Get("FDN", ""), "FDN is a foundation");
            Assert.Equal("PLAN-SubjectBoundary", std.LineLayers.Get("SETSTAKE", ""), "SET* pattern");
            Assert.Equal("", std.LineLayers.Get("BLDG", ""), "unlisted");
        }

        public static void TestStandardsFileIsComplete()
        {
            var std = ProVision;
            Assert.Equal(10, std.Sheets.Count, "sheet definitions");
            Assert.Equal(2, std.Families.Count, "families");
            Assert.Equal(0, TitleBlockFiller.Validate(std).Count, "regexes compile");
            Assert.Near(10, std.BlockUnitMm, 1e-9, "block unit");
            Assert.True(std.Scales.First().Denominator == 100 && std.Scales.Last().Denominator == 5000, "scales sorted");
        }

        // ---- sheets ---------------------------------------------------------------------

        public static void TestFreeRectsAroundScheduleBox()
        {
            var std = ProVision;
            var def = std.Sheets["RPLAN-17X22"];
            var rects = SheetPicker.FreeRects(def.Frame, def.Keepouts);
            Assert.Equal(2, rects.Count, "left of schedule / below schedule");
            Assert.True(rects.Any(r => r.X2 == 331 && r.Y2 == 417), "full-height rect left of the schedule");
            Assert.True(rects.Any(r => r.X2 == 411 && r.Y2 == 379), "full-width rect under the schedule");
        }

        public static void TestPicksSmallestLegibleSheet()
        {
            var result = DraftPipeline.Run(FdJobReader.Read(Demo), ProVision);
            Assert.Equal("11X17", result.Chosen!.Sheet.Layout);
            Assert.Equal("1:250", result.Chosen.Scale.Label);
            Assert.True(result.Ranked.Count == 5, "every topo layout ranked");
        }

        public static void TestForcedLayoutAndScale()
        {
            var result = DraftPipeline.Run(FdJobReader.Read(Demo), ProVision, "rplan", "RPLAN-22X34", "300");
            Assert.Equal("RPLAN-22X34", result.Chosen!.Sheet.Layout);
            Assert.Equal("1:300", result.Chosen.Scale.Label);
        }

        public static void TestLabelsScaleWithTheSheet()
        {
            var job = FdJobReader.Read(Demo);
            var result = DraftPipeline.Run(job, ProVision, null, null, "500");
            var brg = result.Document.Entities.OfType<DraftText>().First(t => t.Kind == TextKind.Bearing);
            Assert.Equal("PLAN-Bearing", brg.Layer);
            Assert.Equal("PLAN-Bearing", brg.Style);
            Assert.Near(2.0, brg.HeightMm, 1e-9, "paper height is constant");
            Assert.Near(0.5, result.ModelPerMm, 1e-9, "1:500 = 0.5 m per mm");
            Assert.True(result.Document.Entities.OfType<DraftText>().Any(t => t.Kind == TextKind.Area && t.Text.StartsWith("AREA = 8")), "lot area label");
            Assert.True(!result.Document.Entities.OfType<DraftText>().Any(t => t.Kind == TextKind.Area && t.Text.StartsWith("AREA = 16")), "no area on the building");
        }

        // ---- title block ------------------------------------------------------------------

        public static void TestTitleBlockReplacements()
        {
            var std = ProVision;
            var job = FdJobReader.Read(Demo);
            var f = new TitleBlockFiller(std, job.Settings, std.Scales.First(s => s.Denominator == 500), "17X22", "24-0DEMO.dwg", new DateTime(2026, 9, 24), 558.8, 431.8);
            Assert.Equal("JOB NUMBER: 24-0DEMO", f.Apply("JOB NUMBER: 24-0XX"));
            Assert.Equal("SCALE 1:500", f.Apply("SCALE 1:300"));
            Assert.Equal("THE INTENDED PLOT SIZE OF THIS PLAN IS 559mm IN WIDTH BY 432mm IN HEIGHT WHEN PLOTTED AT A SCALE OF 1:500.",
                f.Apply("THE INTENDED PLOT SIZE OF THIS PLAN IS 457mm IN WIDTH BY 610mm IN HEIGHT WHEN PLOTTED AT A SCALE OF 1:750."));
            Assert.True(f.Apply("ONTARIO LAND SURVEYORS") == null, "untouched text");
        }

        public static void TestSimplePatternLanguage()
        {
            Assert.Equal("SCALE 1:250.", SimplePattern.Replace("SCALE 1:#", "SCALE 1:300.", "SCALE 1:250"), "trailing full stop kept");
            Assert.Equal("JOB NUMBER: 24-012", SimplePattern.Replace("JOB NUMBER:*", "JOB NUMBER: 24-0XX", "JOB NUMBER: 24-012"));
            Assert.Equal("scale 1:7.5 ok", SimplePattern.Replace("SCALE 1:#", "scale 1:12.25 ok", "scale 1:7.5"), "case-insensitive, decimals");
            Assert.True(!SimplePattern.IsMatch("SCALE 1:#", "SCALE 1:"), "# needs a digit");
        }

        // ---- DWG output (needs a template: set FDDRAFT_TEST_DWT to a .dwt path) -----------

        private static string? TestTemplate => Environment.GetEnvironmentVariable("FDDRAFT_TEST_DWT");

        public static void TestDraftsIntoTemplateAndReadsBack()
        {
            if (string.IsNullOrEmpty(TestTemplate)) { Console.Write("  (skipped: FDDRAFT_TEST_DWT not set) "); return; }
            var std = ProVision;
            var job = FdJobReader.Read(Demo);
            var result = DraftPipeline.Run(job, std);
            var def = std.Sheets[result.Chosen!.Sheet.Layout];
            var filler = new TitleBlockFiller(std, job.Settings, result.Chosen.Scale, result.Chosen.Sheet.Layout, "t.dwg", new DateTime(2026, 9, 24), def.PaperWidth, def.PaperHeight);
            var drafter = FdDraft.Cad.TemplateDrafter.Open(TestTemplate!, std);
            drafter.Draft(result, filler);
            string path = Path.Combine(Path.GetTempPath(), "fdd-test.dwg");
            drafter.Save(path);

            var back = ACadSharp.IO.DwgReader.Read(path);
            var layout = back.Layouts.First(l => l.Name == "11X17");
            var vp = layout.AssociatedBlock.Entities.OfType<ACadSharp.Entities.Viewport>().Single(v => v.Layer.Name == "Defpoints");
            Assert.Near(0.25, vp.ViewHeight / vp.Height, 1e-9, "viewport at 1:250");
            Assert.True(layout.AssociatedBlock.Entities.OfType<ACadSharp.Entities.TextEntity>().Any(t => t.Value == "JOB NUMBER: 24-0DEMO"), "title block filled");
            var ms = back.ModelSpace.Entities.ToList();
            Assert.True(ms.OfType<ACadSharp.Entities.Insert>().Any(i => i.Block.Name == "PLAN-FOUND MONUMENT" && Math.Abs(i.XScale - 2.5) < 1e-9), "monument block at 1:250 (10 mm units)");
            Assert.True(ms.OfType<ACadSharp.Entities.TextEntity>().Any(t => t.Value == "S89%%d13'52\"W" && t.Layer.Name == "PLAN-Bearing"), "bearing on PLAN-Bearing");
            Assert.True(back.Layers["POINTNUMBER-MONUMENT"].Flags.HasFlag(ACadSharp.Tables.LayerFlags.Frozen), "point numbers frozen for topo");
            Assert.True(!back.Layouts.Any(l => l.Name == "36X48"), "unused layouts removed");
        }

        public static void TestViewerSceneAndPdfFromDraftedSheet()
        {
            if (string.IsNullOrEmpty(TestTemplate)) { Console.Write("  (skipped: FDDRAFT_TEST_DWT not set) "); return; }
            var std = ProVision;
            var job = FdJobReader.Read(Demo);
            var result = DraftPipeline.Run(job, std);
            var def = std.Sheets[result.Chosen!.Sheet.Layout];
            var drafter = FdDraft.Cad.TemplateDrafter.Open(TestTemplate!, std);
            drafter.Draft(result, new TitleBlockFiller(std, job.Settings, result.Chosen.Scale, "11X17", "t.dwg", DateTime.Today, def.PaperWidth, def.PaperHeight));

            var sheet = new FdDraft.View.SceneBuilder(drafter.Document).Layout("11X17");
            Assert.True(sheet.IsPaper && Math.Abs(sheet.Paper!.Value.Width - 431.8) < 0.5, "sheet is 11x17 landscape");
            var vpGroup = sheet.Groups.First(g => g.Clip.HasValue);
            Assert.True(vpGroup.Prims.Any(p => p.Kind == FdDraft.View.PrimKind.Text && p.Text == "S89°13'52\"W"), "bearing seen through the viewport, degree decoded");
            // Paper text height = 2 mm, whatever the scale.
            var brg = vpGroup.Prims.First(p => p.Text == "S89°13'52\"W");
            Assert.Near(2.0, brg.Height, 1e-6, "bearing is 2 mm on paper");
            // Monument 1 (FDIB) lands inside the viewport on paper and is snappable.
            var p1 = job.Point(1)!;
            var vp = drafter.Document.Layouts.First(l => l.Name == "11X17").AssociatedBlock.Entities.OfType<ACadSharp.Entities.Viewport>().First(v => !v.RepresentsPaper);
            double s = vp.Height / vp.ViewHeight;
            var onPaper = new Vec2((p1.Easting - vp.ViewCenter.X) * s + vp.Center.X, (p1.Northing - vp.ViewCenter.Y) * s + vp.Center.Y);
            var snap = sheet.Snap(onPaper, 1.0);
            Assert.True(snap.HasValue && Vec2.Distance(snap.Value.Point, onPaper) < 0.01, "monument snaps on the sheet");

            string pdf = Path.Combine(Path.GetTempPath(), "fdd-test.pdf");
            FdDraft.View.PdfSceneWriter.Write(sheet, pdf, "test");
            var head = File.ReadAllText(pdf, System.Text.Encoding.Latin1);
            Assert.True(head.StartsWith("%PDF-1.4") && head.Contains("/MediaBox [0 0 1224 792]") && head.TrimEnd().EndsWith("%%EOF"), "11x17 PDF page");
        }

        public static void TestInverseAndViewTransform()
        {
            var inv = FdDraft.View.InverseResult.Between(new Vec2(0, 0), new Vec2(10, -10));
            Assert.Equal("S45°00'00\"E", inv.Bearing);
            Assert.Near(Math.Sqrt(200), inv.Distance, 1e-9);
            var vt = new FdDraft.View.ViewTransform { ScreenWidth = 800, ScreenHeight = 600 };
            vt.Fit(new Rect(100, 200, 300, 400), 0);
            var sc = vt.ToScreen(new Vec2(200, 300));
            Assert.Near(400, sc.X, 1e-9); Assert.Near(300, sc.Y, 1e-9);
            vt.ZoomAt(100, 100, 2);
            var back = vt.ToScene(100, 100);
            var before = new FdDraft.View.ViewTransform { ScreenWidth = 800, ScreenHeight = 600 };
            before.Fit(new Rect(100, 200, 300, 400), 0);
            var w0 = before.ToScene(100, 100);
            Assert.Near(w0.X, back.X, 1e-9, "zoom keeps the point under the cursor");
            Assert.Near(w0.Y, back.Y, 1e-9);
            Assert.Equal("47°", FdDraft.View.SceneBuilder.PlainMText("{\\fArial|b0;47%%d}")[0], "mtext codes stripped, %%d decoded");
        }

        public static void TestInspectorFindsTitleColumn()
        {
            if (string.IsNullOrEmpty(TestTemplate)) { Console.Write("  (skipped: FDDRAFT_TEST_DWT not set) "); return; }
            var doc = ACadSharp.IO.DwgReader.Read(TestTemplate!);
            var g = FdDraft.Cad.TemplateInspector.Guess(doc).First(x => x.Layout == "17X22");
            Assert.True(g.FrameFound, "frame");
            Assert.Near(523, g.Frame.Width, 1, "frame width");
            Assert.True(g.Keepouts.Any(k => Math.Abs(k.X1 - 411) < 6 && Math.Abs(k.Y2 - 417) < 1), "title column from x=411, full height");
            Assert.Near(558.8, g.PaperWidth, 0.5, "landscape paper width");
        }

        public static void TestScaleBarTicks()
        {
            Assert.Equal("10", TitleBlockFiller.RelabelTick("6", 300, 500));
            Assert.Equal("40m", TitleBlockFiller.RelabelTick("24m", 300, 500));
            Assert.Equal("5", TitleBlockFiller.RelabelTick("7.5", 750, 500));
            Assert.Equal("2.5", TitleBlockFiller.RelabelTick("7.5", 750, 250));
            Assert.True(TitleBlockFiller.RelabelTick("SCALE 1:300", 300, 500) == null, "not a tick");
            Assert.Near(750, TitleBlockFiller.DenominatorIn("SCALE 1:750")!.Value, 1e-9);
        }

        // ---- the app's editing tools: COGO parsing, undo/redo, move/rotate/erase --------

        public static void TestBearingParsing()
        {
            Assert.Near(45.0, Cogo.ParseBearing("N45E") * 180 / Math.PI, 1e-9, "N45E");
            Assert.Near(135.0, Cogo.ParseBearing("S45E") * 180 / Math.PI, 1e-9, "S45E");
            Assert.Near(225.0, Cogo.ParseBearing("S45W") * 180 / Math.PI, 1e-9, "S45W");
            Assert.Near(315.0, Cogo.ParseBearing("N45W") * 180 / Math.PI, 1e-9, "N45W");
            Assert.Near(45.5, Cogo.ParseBearing("N45-30-00E") * 180 / Math.PI, 1e-9, "DMS with dashes");
            Assert.Near(45.5, Cogo.ParseBearing("N45d30m00sE") * 180 / Math.PI, 1e-6, "DMS with letters");
            Assert.Near(125.5, Cogo.ParseBearing("125.5") * 180 / Math.PI, 1e-9, "plain azimuth");

            Assert.True(Cogo.TryParseLeg("N45-30-00E 125.50", out double az, out double dist), "leg parses");
            Assert.Near(45.5, az * 180 / Math.PI, 1e-9, "leg azimuth");
            Assert.Near(125.50, dist, 1e-9, "leg distance");
            Assert.True(!Cogo.TryParseLeg("just one word", out _, out _), "one word is not a leg");
            Assert.True(!Cogo.TryParseLeg("N45E 0", out _, out _), "zero distance is rejected");
            Assert.Near(30 + 30 / 3600.0, Cogo.ParseBearing("ne30.0030") * 180 / Math.PI, 1e-9, "NE + DD.MMSS");
            Assert.Near(180 - (45 + 30 / 60.0 + 15 / 3600.0), Cogo.ParseBearing("SE45.3015") * 180 / Math.PI, 1e-9, "SE + DD.MMSS");
            Assert.Near(180 + 10.5, Cogo.ParseBearing("sw10-30-00") * 180 / Math.PI, 1e-9, "SW + dashes");
            Assert.Near(360 - 89, Cogo.ParseBearing("NW89") * 180 / Math.PI, 1e-9, "NW whole degrees");
            Assert.True(Cogo.TryParseLeg("ne30.0030 125.5", out double qaz, out double qd) && Math.Abs(qd - 125.5) < 1e-12
                && Math.Abs(qaz * 180 / Math.PI - (30 + 30 / 3600.0)) < 1e-9, "Marc's leg: ne30.0030 125.5");
            Assert.True(Cogo.TryParseLeg("NE 30.0030 125.5", out double qaz2, out _) && Math.Abs(qaz2 - qaz) < 1e-12, "quadrant typed apart");
            Assert.True(!Cogo.TryParseLeg("NE30.7000 10", out _, out _), "70 minutes is rejected");

            Assert.True(Cogo.TryParseCoordinate("500.25,1200.75", out double e, out double n), "coordinate parses");
            Assert.Near(500.25, e, 1e-9, "coordinate E"); Assert.Near(1200.75, n, 1e-9, "coordinate N");
        }

        public static void TestEditCommandsAddEraseUndoRedo()
        {
            var doc = new ACadSharp.CadDocument();
            var undo = new UndoStack();
            var line = new ACadSharp.Entities.Line(new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(10, 0, 0));

            undo.Push(new AddEntitiesCommand(doc.ModelSpace, new ACadSharp.Entities.Entity[] { line }, "Line"));
            Assert.True(doc.ModelSpace.Entities.Any(x => x == line), "added to model space");
            Assert.True(line.Handle != 0, "handle assigned");

            var erase = new RemoveEntitiesCommand(new ACadSharp.Entities.Entity[] { line }, "Erase");
            undo.Push(erase);
            Assert.True(!doc.ModelSpace.Entities.Any(x => x == line), "erased");
            undo.Undo();
            Assert.True(doc.ModelSpace.Entities.Any(x => x == line), "undo erase puts it back");
            undo.Redo();
            Assert.True(!doc.ModelSpace.Entities.Any(x => x == line), "redo erase removes it again");
            undo.Undo(); // leave it in place for the transform tests below
        }

        public static void TestEditCommandsMoveAndRotate()
        {
            var doc = new ACadSharp.CadDocument();
            var line = new ACadSharp.Entities.Line(new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(10, 0, 0));
            doc.ModelSpace.Entities.Add(line);
            var undo = new UndoStack();

            undo.Push(TransformEntitiesCommand.Move(new ACadSharp.Entities.Entity[] { line }, 5, 3, "Move"));
            Assert.Near(5, line.StartPoint.X, 1e-9, "move start.X"); Assert.Near(3, line.StartPoint.Y, 1e-9, "move start.Y");
            Assert.Near(15, line.EndPoint.X, 1e-9, "move end.X"); Assert.Near(3, line.EndPoint.Y, 1e-9, "move end.Y");
            undo.Undo();
            Assert.Near(0, line.StartPoint.X, 1e-9, "undo move start.X"); Assert.Near(10, line.EndPoint.X, 1e-9, "undo move end.X");

            // +90 degrees about the origin (standard math convention: counter-clockwise).
            undo.Push(TransformEntitiesCommand.Rotate(new ACadSharp.Entities.Entity[] { line }, new CSMath.XYZ(0, 0, 0), Math.PI / 2, "Rotate"));
            Assert.Near(0, line.EndPoint.X, 1e-6, "rotate 90: end.X"); Assert.Near(10, line.EndPoint.Y, 1e-6, "rotate 90: end.Y");
            undo.Undo();
            Assert.Near(10, line.EndPoint.X, 1e-6, "undo rotate: end.X back"); Assert.Near(0, line.EndPoint.Y, 1e-6, "undo rotate: end.Y back");

            // Rotation about a pivot away from the origin - the real MOVE/ROTATE use case.
            var line2 = new ACadSharp.Entities.Line(new CSMath.XYZ(10, 0, 0), new CSMath.XYZ(20, 0, 0));
            var rot = TransformEntitiesCommand.Rotate(new ACadSharp.Entities.Entity[] { line2 }, new CSMath.XYZ(10, 0, 0), Math.PI / 2, "Rotate about pivot");
            Assert.Near(10, line2.EndPoint.X, 1e-6, "pivot rotate: end.X"); Assert.Near(10, line2.EndPoint.Y, 1e-6, "pivot rotate: end.Y");
            Assert.Near(10, line2.StartPoint.X, 1e-6, "pivot rotate: start unmoved X"); Assert.Near(0, line2.StartPoint.Y, 1e-6, "pivot rotate: start unmoved Y");
        }

        public static void TestEditCommandsChangeLayerAndText()
        {
            var doc = new ACadSharp.CadDocument();
            var zero = doc.Layers["0"];
            var boundary = new ACadSharp.Tables.Layer("PLAN-SubjectBoundary");
            doc.Layers.Add(boundary);
            var line = new ACadSharp.Entities.Line(new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(10, 0, 0)) { Layer = zero };
            doc.ModelSpace.Entities.Add(line);
            var undo = new UndoStack();

            undo.Push(new ChangeLayerCommand(new ACadSharp.Entities.Entity[] { line }, boundary, "Set layer"));
            Assert.True(line.Layer == boundary, "layer changed");
            undo.Undo();
            Assert.True(line.Layer == zero, "undo layer change restores the old layer");
            undo.Redo();
            Assert.True(line.Layer == boundary, "redo layer change reapplies it");

            var text = new ACadSharp.Entities.TextEntity { Value = "LOT 5" };
            doc.ModelSpace.Entities.Add(text);
            undo.Push(new EditTextCommand(text, "LOT 5A", "Edit text"));
            Assert.True(text.Value == "LOT 5A", "text edited");
            undo.Undo();
            Assert.True(text.Value == "LOT 5", "undo text edit restores the old text");

            var mtext = new ACadSharp.Entities.MText { Value = "N45-30-00E" };
            doc.ModelSpace.Entities.Add(mtext);
            undo.Push(new EditTextCommand(mtext, "N46-00-00E", "Edit text"));
            Assert.True(mtext.Value == "N46-00-00E", "mtext edited");
            undo.Undo();
            Assert.True(mtext.Value == "N45-30-00E", "undo mtext edit restores the old text");
        }

        public static void TestSetPropertyCommandOnRadiusAndTextHeight()
        {
            var undo = new UndoStack();
            var circle = new ACadSharp.Entities.Circle { Radius = 5 };
            undo.Push(new SetPropertyCommand<double>(circle.Radius, 8, v => circle.Radius = v, "Set radius"));
            Assert.Near(8, circle.Radius, 1e-9, "radius changed");
            undo.Undo();
            Assert.Near(5, circle.Radius, 1e-9, "undo radius restores the old value");
            undo.Redo();
            Assert.Near(8, circle.Radius, 1e-9, "redo radius reapplies it");

            var text = new ACadSharp.Entities.TextEntity { Value = "LOT 5", Height = 0.2 };
            undo.Push(new SetPropertyCommand<double>(text.Height, 0.3, v => text.Height = v, "Set text height"));
            Assert.Near(0.3, text.Height, 1e-9, "text height changed");
            undo.Undo();
            Assert.Near(0.2, text.Height, 1e-9, "undo text height restores the old value");
        }

        public static void TestCompositeCommandUndoesAllFieldsAsOneStep()
        {
            var arc = new ACadSharp.Entities.Arc { Radius = 5, StartAngle = 0, EndAngle = Math.PI / 2 };
            var undo = new UndoStack();
            // A Properties-panel edit touching more than one field (radius and both angles)
            // commits as one CompositeCommand, so a single Ctrl+Z undoes every field together.
            var edits = new IEditCommand[]
            {
                new SetPropertyCommand<double>(arc.Radius, 8, v => arc.Radius = v, "Set radius"),
                new SetPropertyCommand<double>(arc.StartAngle, 0.1, v => arc.StartAngle = v, "Set start angle"),
                new SetPropertyCommand<double>(arc.EndAngle, 2.0, v => arc.EndAngle = v, "Set end angle"),
            };
            undo.Push(new CompositeCommand(edits, "Edit properties"));
            Assert.Near(8, arc.Radius, 1e-9, "radius applied"); Assert.Near(0.1, arc.StartAngle, 1e-9, "start angle applied"); Assert.Near(2.0, arc.EndAngle, 1e-9, "end angle applied");

            undo.Undo();
            Assert.Near(5, arc.Radius, 1e-9, "one undo restores radius"); Assert.Near(0, arc.StartAngle, 1e-9, "and start angle"); Assert.Near(Math.PI / 2, arc.EndAngle, 1e-9, "and end angle - all in the same step");

            undo.Redo();
            Assert.Near(8, arc.Radius, 1e-9, "one redo reapplies radius"); Assert.Near(0.1, arc.StartAngle, 1e-9, "and start angle"); Assert.Near(2.0, arc.EndAngle, 1e-9, "and end angle");
        }

        public static void TestMTextRotationViaAlignmentPointDirection()
        {
            // MText.Rotation is read-only in ACadSharp, derived from AlignmentPoint treated as a
            // direction vector - this is the trick the Properties panel's rotation field uses to
            // actually change it, so it needs its own test rather than relying on SetPropertyCommand's
            // own generic coverage.
            var mt = new ACadSharp.Entities.MText { AlignmentPoint = new CSMath.XYZ(1, 0, 0) };
            Assert.Near(0, mt.Rotation, 1e-9, "starts at 0 degrees");
            double toRad = Math.PI / 180.0;
            var undo = new UndoStack();
            undo.Push(new SetPropertyCommand<double>(mt.Rotation, 90, v => mt.AlignmentPoint = new CSMath.XYZ(Math.Cos(v * toRad), Math.Sin(v * toRad), 0), "Set text rotation"));
            Assert.Near(Math.PI / 2, mt.Rotation, 1e-9, "rotation is now 90 degrees (radians)");
            undo.Undo();
            Assert.Near(0, mt.Rotation, 1e-9, "undo restores 0 degrees");
        }

        public static void TestStretchVertexKeepsConnectedLinesTogether()
        {
            var doc = new ACadSharp.CadDocument();
            // Two lines sharing a corner at (10,0), like two lot lines meeting at a survey point.
            var a = new ACadSharp.Entities.Line(new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(10, 0, 0));
            var b = new ACadSharp.Entities.Line(new CSMath.XYZ(10, 0, 0), new CSMath.XYZ(10, 10, 0));
            var unrelated = new ACadSharp.Entities.Line(new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(0, -10, 0));
            doc.ModelSpace.Entities.Add(a); doc.ModelSpace.Entities.Add(b); doc.ModelSpace.Entities.Add(unrelated);

            var hits = VertexEditing.FindCoincident(new ACadSharp.Entities.Entity[] { a, b, unrelated }, new CSMath.XYZ(10, 0, 0), 1e-6);
            Assert.True(hits.Count == 2, "finds both lines' shared endpoint, not the unrelated line");

            var undo = new UndoStack();
            undo.Push(new StretchVertexCommand(hits, new CSMath.XYZ(12, 1, 0), "Stretch"));
            Assert.Near(12, a.EndPoint.X, 1e-9, "a's end moved"); Assert.Near(1, a.EndPoint.Y, 1e-9, "a's end moved");
            Assert.Near(12, b.StartPoint.X, 1e-9, "b's start moved with it"); Assert.Near(1, b.StartPoint.Y, 1e-9, "b's start moved with it");
            Assert.Near(0, a.StartPoint.X, 1e-9, "a's other end untouched");
            Assert.Near(0, unrelated.StartPoint.X, 1e-9, "unrelated line untouched");

            undo.Undo();
            Assert.Near(10, a.EndPoint.X, 1e-9, "undo restores a's end"); Assert.Near(0, a.EndPoint.Y, 1e-9, "undo restores a's end");
            Assert.Near(10, b.StartPoint.X, 1e-9, "undo restores b's start"); Assert.Near(0, b.StartPoint.Y, 1e-9, "undo restores b's start");
            undo.Redo();
            Assert.Near(12, b.StartPoint.X, 1e-9, "redo re-applies the stretch");
        }

        public static void TestStretchVertexOnPolyline()
        {
            var poly = new ACadSharp.Entities.LwPolyline();
            poly.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(0, 0)));
            poly.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(10, 0)));
            poly.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(10, 10)));

            var hits = VertexEditing.FindCoincident(new ACadSharp.Entities.Entity[] { poly }, new CSMath.XYZ(10, 0, 0), 1e-6);
            Assert.True(hits.Count == 1, "finds the one polyline vertex at that point");
            var undo = new UndoStack();
            undo.Push(new StretchVertexCommand(hits, new CSMath.XYZ(11, -1, 0), "Stretch"));
            Assert.Near(11, poly.Vertices[1].Location.X, 1e-9, "polyline vertex moved"); Assert.Near(-1, poly.Vertices[1].Location.Y, 1e-9, "polyline vertex moved");
            Assert.Near(0, poly.Vertices[0].Location.X, 1e-9, "adjacent vertex untouched");
            undo.Undo();
            Assert.Near(10, poly.Vertices[1].Location.X, 1e-9, "undo restores the polyline vertex");
        }

        public static void TestPropertiesPanelCanTypeInAPolylineVertex()
        {
            // The Properties panel's "Vertex #" fields are the type-in alternative to STRETCH's
            // pick-and-drag: SetPropertyCommand<double> over a VertexRef, one field at a time,
            // rather than StretchVertexCommand's move-every-coincident-vertex-together behavior.
            var poly = new ACadSharp.Entities.LwPolyline();
            poly.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(0, 0)));
            poly.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(10, 0)));
            poly.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(10, 10)));

            var vref = new VertexRef(poly, 1);
            var undo = new UndoStack();
            var edits = new IEditCommand[]
            {
                new SetPropertyCommand<double>(vref.Get().X, 15, v => vref.Set(new CSMath.XYZ(v, vref.Get().Y, 0)), "Set vertex position"),
                new SetPropertyCommand<double>(vref.Get().Y, -2, v => vref.Set(new CSMath.XYZ(vref.Get().X, v, 0)), "Set vertex position"),
            };
            undo.Push(new CompositeCommand(edits, "Edit properties"));
            Assert.Near(15, poly.Vertices[1].Location.X, 1e-9, "vertex E typed in"); Assert.Near(-2, poly.Vertices[1].Location.Y, 1e-9, "vertex N typed in");
            Assert.Near(0, poly.Vertices[0].Location.X, 1e-9, "adjacent vertex untouched");

            undo.Undo();
            Assert.Near(10, poly.Vertices[1].Location.X, 1e-9, "one undo restores both fields"); Assert.Near(0, poly.Vertices[1].Location.Y, 1e-9, "one undo restores both fields");
        }

        public static void TestLeaderEntityAddsAndRenders()
        {
            var doc = new ACadSharp.CadDocument();
            var leader = new ACadSharp.Entities.Leader
            {
                ArrowHeadEnabled = true,
                Style = ACadSharp.Tables.DimensionStyle.Default,
            };
            leader.Vertices.Add(new CSMath.XYZ(0, 0, 0));
            leader.Vertices.Add(new CSMath.XYZ(5, 5, 0));
            var undo = new UndoStack();

            // Adding it to the document resolves Style against doc.DimensionStyles (registers
            // "Standard" if the document doesn't have it yet) - this is the real failure mode to
            // catch: a Leader built off-document with a detached DimensionStyle instance.
            undo.Push(new AddEntitiesCommand(doc.ModelSpace, new ACadSharp.Entities.Entity[] { leader }, "Leader"));
            Assert.True(doc.ModelSpace.Entities.Any(e => e == leader), "leader added to model space");
            Assert.True(doc.DimensionStyles.Any(s => s.Name == leader.Style.Name), "its dimension style is registered in the document");

            var scene = new SceneBuilder(doc).Model();
            int prims = scene.Groups.Sum(g => g.Prims.Count);
            Assert.True(prims >= 2, "the leader draws its shaft and an arrowhead, not just gets skipped");

            undo.Undo();
            Assert.True(!doc.ModelSpace.Entities.Any(e => e == leader), "undo removes it");
        }

        public static void TestLeaderEntitySurvivesDwgRoundTrip()
        {
            // Unlike TestDraftsIntoTemplateAndReadsBack this needs no firm .dwt - a real DWG
            // write/read of a fresh document, to catch a writer-side issue with the Leader's
            // DimensionStyle table reference that an in-memory-only test could miss.
            var doc = new ACadSharp.CadDocument();
            var leader = new ACadSharp.Entities.Leader { ArrowHeadEnabled = true, Style = ACadSharp.Tables.DimensionStyle.Default };
            leader.Vertices.Add(new CSMath.XYZ(1, 2, 0));
            leader.Vertices.Add(new CSMath.XYZ(6, 7, 0));
            doc.ModelSpace.Entities.Add(leader);
            string path = Path.Combine(Path.GetTempPath(), "fdd-leader-test.dwg");
            ACadSharp.IO.DwgWriter.Write(path, doc);

            var back = ACadSharp.IO.DwgReader.Read(path);
            var readBack = back.ModelSpace.Entities.OfType<ACadSharp.Entities.Leader>().Single();
            Assert.True(readBack.ArrowHeadEnabled, "arrowhead flag survives the round trip");
            Assert.True(readBack.Vertices.Count == 2, "both vertices survive");
            Assert.Near(6, readBack.Vertices[1].X, 1e-6, "vertex position survives");
            Assert.True(readBack.Style != null && readBack.Style.Name == "Standard", "the dimension style reference survives and resolves");
        }

        public static void TestModelAtFallsBackToPaperWhenNoViewport()
        {
            // A sheet with a real, working viewport: outside it, a pick is ambiguous (null).
            var withVp = new Scene { IsPaper = true };
            withVp.Groups.Add(new SceneGroup { Clip = new Rect(0, 0, 100, 100), ToModel = Affine.Translate(5, 5) });
            Assert.True(withVp.ModelAt(new Vec2(50, 50)).HasValue, "inside the real viewport resolves");
            Assert.True(withVp.ModelAt(new Vec2(150, 150)) == null, "outside every real viewport on a sheet that has one is ambiguous");

            // A sheet with only the DWG-mandated background viewport (SceneBuilder skips it, so no
            // clipped group at all) - a real MSCAD job commonly draws its plan straight onto paper.
            var noVp = new Scene { IsPaper = true };
            noVp.Groups.Add(new SceneGroup());
            var p = new Vec2(42, 7);
            var result = noVp.ModelAt(p);
            Assert.True(result.HasValue, "no working viewport at all: the paper point is still usable");
            Assert.Near(p.X, result!.Value.X, 1e-9, "paper point used directly (X)");
            Assert.Near(p.Y, result.Value.Y, 1e-9, "paper point used directly (Y)");
        }
            // ---- COPY / MIRROR / OFFSET (v0.4.8) ------------------------------------------------

        public static void TestCopyAddsTranslatedCopiesBesideTheirSource()
        {
            var doc = new ACadSharp.CadDocument();
            var layer = new ACadSharp.Tables.Layer("PLAN-Fence");
            doc.Layers.Add(layer);
            var line = new ACadSharp.Entities.Line(new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(10, 0, 0)) { Layer = layer };
            doc.ModelSpace.Entities.Add(line);
            var undo = new UndoStack();

            var pairs = EntityOps.Copies(new ACadSharp.Entities.Entity[] { line }, 5, 7);
            undo.Push(EntityOps.AddBesideSources(pairs, "Copy")!);
            var copy = (ACadSharp.Entities.Line)pairs[0].Copy;
            Assert.True(doc.ModelSpace.Entities.Contains(copy), "copy lands in the source's block");
            Assert.True(copy.Layer == layer, "copy keeps the document's own layer, not a detached clone");
            Assert.Near(5, copy.StartPoint.X, 1e-9, "copy moved E"); Assert.Near(7, copy.EndPoint.Y, 1e-9, "copy moved N");
            Assert.Near(0, line.StartPoint.X, 1e-9, "source untouched");
            undo.Undo();
            Assert.True(!doc.ModelSpace.Entities.Contains(copy), "undo removes the copy");
        }

        public static void TestMirrorReflectsGeometryAndKeepsTextReadable()
        {
            // Mirror across the north-south axis E = 0.
            var a = new Vec2(0, 0); var b = new Vec2(0, 10);
            var line = new ACadSharp.Entities.Line(new CSMath.XYZ(2, 1, 0), new CSMath.XYZ(5, 3, 0));
            var ml = (ACadSharp.Entities.Line)EntityOps.Mirrored(line, a, b)!;
            Assert.Near(-2, ml.StartPoint.X, 1e-9, "line start reflected"); Assert.Near(1, ml.StartPoint.Y, 1e-9, "N unchanged");
            Assert.Near(-5, ml.EndPoint.X, 1e-9, "line end reflected");

            // Quarter arc centred (5,0) from east (0 rad) to north (90 deg), CCW.
            var arc = new ACadSharp.Entities.Arc { Center = new CSMath.XYZ(5, 0, 0), Radius = 2, StartAngle = 0, EndAngle = Math.PI / 2 };
            var ma = (ACadSharp.Entities.Arc)EntityOps.Mirrored(arc, a, b)!;
            Assert.Near(-5, ma.Center.X, 1e-9, "arc centre reflected");
            Assert.Near(Math.PI / 2, ma.StartAngle, 1e-9, "mirrored arc runs from north ..."); Assert.Near(Math.PI, ma.EndAngle, 1e-9, "... to west");

            var poly = new ACadSharp.Entities.LwPolyline();
            poly.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(1, 0)) { Bulge = 0.5 });
            poly.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(3, 0)));
            var mp = (ACadSharp.Entities.LwPolyline)EntityOps.Mirrored(poly, a, b)!;
            Assert.Near(-1, mp.Vertices[0].Location.X, 1e-9, "vertex reflected"); Assert.Near(-0.5, mp.Vertices[0].Bulge, 1e-9, "bulge changes sign");
            Assert.Near(1, poly.Vertices[0].Location.X, 1e-9, "source polyline untouched");

            // A label reading east, sitting above its anchor: mirrored it must still read
            // left-to-right (rotation 0, not 180) and still sit above, now anchored at its right end.
            var text = new ACadSharp.Entities.TextEntity
            {
                Value = "N90°E 10.000", InsertPoint = new CSMath.XYZ(2, 1, 0), AlignmentPoint = new CSMath.XYZ(2, 1, 0), Height = 0.2, Rotation = 0,
                HorizontalAlignment = ACadSharp.Entities.TextHorizontalAlignment.Left, VerticalAlignment = ACadSharp.Entities.TextVerticalAlignmentType.Bottom,
            };
            var mt = (ACadSharp.Entities.TextEntity)EntityOps.Mirrored(text, a, b)!;
            Assert.Near(0, mt.Rotation, 1e-9, "mirrored text still reads forwards");
            Assert.Near(-2, mt.AlignmentPoint.X, 1e-9, "anchor reflected");
            Assert.True(mt.HorizontalAlignment == ACadSharp.Entities.TextHorizontalAlignment.Right, "now anchored at its right end, so it extends away from the axis like the mirror image");
            Assert.True(mt.VerticalAlignment == ACadSharp.Entities.TextVerticalAlignmentType.Bottom, "still sits above its anchor");
        }

        public static void TestConstructOffsetPolylineMitresCornersAndKeepsArcsConcentric()
        {
            // A 10 x 10 square, counter-clockwise; offset 1 to the left (inside) -> 8 x 8.
            var sq = new List<Vec2> { new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(0, 10) };
            var inside = Construct.OffsetPolyline(sq, null, true, 1)!.Value;
            Assert.Near(1, inside.Points[0].X, 1e-9, "corner 0 E"); Assert.Near(1, inside.Points[0].Y, 1e-9, "corner 0 N");
            Assert.Near(9, inside.Points[2].X, 1e-9, "corner 2 E"); Assert.Near(9, inside.Points[2].Y, 1e-9, "corner 2 N");
            Assert.Near(64, Polygon.SignedArea(inside.Points, inside.Bulges), 1e-9, "offset square area");
            Assert.Equal(1, Construct.SideOfPolyline(sq, null, true, new Vec2(5, 5)), "a pick inside a CCW square is on its left");

            // An open line then a tangent quarter arc (a road allowance corner): (0,0)->(10,0),
            // then CCW about (10,5) up to (15,5). Offsetting 1 to the right (outside the curve)
            // gives radius 6 and keeps the joint tangent at (10,-1).
            var pts = new List<Vec2> { new Vec2(0, 0), new Vec2(10, 0), new Vec2(15, 5) };
            var bulges = new List<double> { 0, Math.Tan(Math.PI / 8), 0 };
            var outside = Construct.OffsetPolyline(pts, bulges, false, -1)!.Value;
            Assert.Near(0, outside.Points[0].X, 1e-9, "start E"); Assert.Near(-1, outside.Points[0].Y, 1e-9, "start N");
            Assert.Near(10, outside.Points[1].X, 1e-9, "tangent joint E"); Assert.Near(-1, outside.Points[1].Y, 1e-9, "tangent joint N");
            Assert.Near(16, outside.Points[2].X, 1e-9, "arc end on radius 6"); Assert.Near(5, outside.Points[2].Y, 1e-9, "arc end N");
            Assert.Near(Math.Tan(Math.PI / 8), outside.Bulges[1], 1e-9, "still a quarter arc");
        }

        public static void TestOffsetEntitiesTowardThePickedSide()
        {
            var line = new ACadSharp.Entities.Line(new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(10, 0, 0));
            var up = (ACadSharp.Entities.Line)EntityOps.Offset(line, 2, new Vec2(4, 5))!;
            Assert.Near(2, up.StartPoint.Y, 1e-9, "offset toward the pick (north)");
            var down = (ACadSharp.Entities.Line)EntityOps.Offset(line, 2, new Vec2(4, -5))!;
            Assert.Near(-2, down.EndPoint.Y, 1e-9, "offset toward the pick (south)");

            var circle = new ACadSharp.Entities.Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 5 };
            Assert.Near(4, ((ACadSharp.Entities.Circle)EntityOps.Offset(circle, 1, new Vec2(1, 0))!).Radius, 1e-9, "pick inside shrinks");
            Assert.Near(6, ((ACadSharp.Entities.Circle)EntityOps.Offset(circle, 1, new Vec2(9, 0))!).Radius, 1e-9, "pick outside grows");
            Assert.True(EntityOps.Offset(circle, 6, new Vec2(1, 0)) == null, "offset past the centre is refused");

            var poly = new ACadSharp.Entities.LwPolyline { IsClosed = true };
            foreach (var (x, y) in new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0) })
                poly.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(x, y)));
            var grown = (ACadSharp.Entities.LwPolyline)EntityOps.Offset(poly, 1, new Vec2(20, 5))!;
            Assert.Near(-1, grown.Vertices[0].Location.X, 1e-9, "outside offset corner E"); Assert.Near(-1, grown.Vertices[0].Location.Y, 1e-9, "outside offset corner N");
            Assert.Near(11, grown.Vertices[2].Location.X, 1e-9, "opposite corner E");
        }
            // ---- polyline vertex insert / delete (v0.4.9) ---------------------------------------

        private static ACadSharp.Entities.LwPolyline Poly(bool closed, params (double X, double Y, double Bulge)[] v)
        {
            var p = new ACadSharp.Entities.LwPolyline { IsClosed = closed };
            foreach (var (x, y, b) in v) p.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(x, y)) { Bulge = b });
            return p;
        }

        public static void TestDeleteVertexJoinsItsNeighboursAndUndoes()
        {
            // Open: (0,0) -arc-> (10,0) -> (10,10). Deleting vertex 1 leaves one straight span.
            var p = Poly(false, (0, 0, 0.3), (10, 0, 0), (10, 10, 0));
            var undo = new UndoStack();
            undo.Push(new DeleteVertexCommand(p, 1, "Delete vertex"));
            Assert.Equal(2, p.Vertices.Count, "one vertex gone");
            Assert.Near(10, p.Vertices[1].Location.Y, 1e-9, "joined straight to the next vertex");
            Assert.Near(0, p.Vertices[0].Bulge, 1e-9, "the merged span is straight");
            undo.Undo();
            Assert.Equal(3, p.Vertices.Count, "undo puts it back");
            Assert.Near(10, p.Vertices[1].Location.X, 1e-9, "at its old index");
            Assert.Near(0.3, p.Vertices[0].Bulge, 1e-9, "and the previous span's arc");
            Assert.True(!DeleteVertexCommand.CanDelete(Poly(false, (0, 0, 0), (1, 0, 0))), "a 2-vertex open polyline can't lose one");

            // Closed: deleting vertex 0 straightens the span arriving from the last vertex.
            var sq = Poly(true, (0, 0, 0), (10, 0, 0), (10, 10, 0), (0, 10, 0.2));
            undo.Push(new DeleteVertexCommand(sq, 0, "Delete vertex"));
            Assert.Equal(3, sq.Vertices.Count, "closed: one vertex gone");
            Assert.Near(10, sq.Vertices[0].Location.X, 1e-9, "old vertex 1 is now first");
            Assert.Near(0, sq.Vertices[2].Bulge, 1e-9, "wrap-around span straightened");
            undo.Undo();
            Assert.Near(0, sq.Vertices[0].Location.X, 1e-9, "undo restores vertex 0");
            Assert.Near(0.2, sq.Vertices[3].Bulge, 1e-9, "and the wrap-around arc");
            undo.Redo();
            Assert.Equal(3, sq.Vertices.Count, "redo deletes again");
        }

        public static void TestInsertVertexSplitsStraightAndArcSpans()
        {
            var p = Poly(false, (0, 0, 0), (10, 0, 0));
            var undo = new UndoStack();
            var ins = new InsertVertexCommand(p, 0, new CSMath.XY(4, 0), "Insert vertex");
            undo.Push(ins);
            Assert.Equal(1, ins.Index, "new vertex index");
            Assert.Equal(3, p.Vertices.Count, "vertex added");
            Assert.Near(4, p.Vertices[1].Location.X, 1e-9, "at the typed spot");
            undo.Undo();
            Assert.Equal(2, p.Vertices.Count, "undo removes it");

            // A CCW quarter arc about the origin from (5,0) to (0,5), split at 45 degrees.
            var arc = Poly(false, (5, 0, Math.Tan(Math.PI / 8)), (0, 5, 0));
            var mid = Construct.Span.FromBulge(new Vec2(5, 0), new Vec2(0, 5), Math.Tan(Math.PI / 8)).Midpoint;
            Assert.Near(5 / Math.Sqrt(2), mid.X, 1e-9, "arc midpoint is on the curve");
            undo.Push(new InsertVertexCommand(arc, 0, new CSMath.XY(mid.X, mid.Y), "Insert vertex"));
            Assert.Near(Math.Tan(Math.PI / 16), arc.Vertices[0].Bulge, 1e-9, "first half keeps the curve");
            Assert.Near(Math.Tan(Math.PI / 16), arc.Vertices[1].Bulge, 1e-9, "second half keeps the curve");
            undo.Undo();
            Assert.Near(Math.Tan(Math.PI / 8), arc.Vertices[0].Bulge, 1e-9, "undo restores the whole arc");

            // Picking near the arc projects onto the curve, not the chord.
            int span = VertexEditing.NearestSpan(arc, new Vec2(4, 4), out var on, out _);
            Assert.Equal(0, span, "nearest span");
            Assert.Near(5, on.Length, 1e-9, "projected onto the radius-5 curve");
            Assert.Equal(2, VertexEditing.NearestVertex(Poly(false, (0, 0, 0), (5, 0, 0), (9, 1, 0)), new Vec2(8, 1), out _), "nearest vertex");
        }
            // ---- FLIP labels (v0.4.10) -----------------------------------------------------------

        private static ACadSharp.Entities.TextEntity Label(string text, double e, double n, ACadSharp.Entities.TextVerticalAlignmentType v) => new ACadSharp.Entities.TextEntity
        {
            Value = text, InsertPoint = new CSMath.XYZ(e, n, 0), AlignmentPoint = new CSMath.XYZ(e, n, 0), Height = 0.2,
            HorizontalAlignment = ACadSharp.Entities.TextHorizontalAlignment.Center, VerticalAlignment = v,
        };

        public static void TestFlipMovesLabelsToTheOtherSideOfTheirCourse()
        {
            // An east-west course with the bearing just above it and the distance just below,
            // the way Annotator places them; plus an unrelated course further away.
            var course = new ACadSharp.Entities.Line(new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(10, 0, 0));
            var other = new ACadSharp.Entities.Line(new CSMath.XYZ(0, 5, 0), new CSMath.XYZ(10, 5, 0));
            var bearing = Label("N90°00'00\"E", 5, 0.1, ACadSharp.Entities.TextVerticalAlignmentType.Bottom);
            var distance = Label("10.000", 5, -0.1, ACadSharp.Entities.TextVerticalAlignmentType.Top);
            var undo = new UndoStack();

            var cmd = LabelFlip.Flip(new ACadSharp.Entities.Entity[] { bearing, distance }, new ACadSharp.Entities.Entity[] { course, other }, 2.0, out int flipped);
            undo.Push(cmd!);
            Assert.Equal(2, flipped, "both labels flipped");
            Assert.Near(-0.1, bearing.AlignmentPoint.Y, 1e-9, "bearing now just below the course");
            Assert.True(bearing.VerticalAlignment == ACadSharp.Entities.TextVerticalAlignmentType.Top, "and hangs from its anchor");
            Assert.Near(0.1, distance.AlignmentPoint.Y, 1e-9, "distance now just above");
            Assert.True(distance.VerticalAlignment == ACadSharp.Entities.TextVerticalAlignmentType.Bottom, "and sits on its anchor");
            Assert.Near(5, bearing.AlignmentPoint.X, 1e-9, "still at the same place along the course");
            undo.Undo();
            Assert.Near(0.1, bearing.AlignmentPoint.Y, 1e-9, "one undo restores the bearing");
            Assert.True(bearing.VerticalAlignment == ACadSharp.Entities.TextVerticalAlignmentType.Bottom, "and its anchoring");
            Assert.Near(-0.1, distance.AlignmentPoint.Y, 1e-9, "and the distance");

            // Curve data flips radially: outside the radius-10 arc -> the same gap inside it.
            var arc = new ACadSharp.Entities.Arc { Center = new CSMath.XYZ(0, 0, 0), Radius = 10, StartAngle = 0, EndAngle = Math.PI / 2 };
            var outer = Label("R=10.000", 10.3 / Math.Sqrt(2), 10.3 / Math.Sqrt(2), ACadSharp.Entities.TextVerticalAlignmentType.Bottom);
            LabelFlip.Flip(new ACadSharp.Entities.Entity[] { outer }, new ACadSharp.Entities.Entity[] { arc }, 2.0, out _);
            Assert.Near(9.7, new Vec2(outer.AlignmentPoint.X, outer.AlignmentPoint.Y).Length, 1e-9, "same gap, inside the curve");

            var far = Label("LOT 5", 50, 50, ACadSharp.Entities.TextVerticalAlignmentType.Middle);
            Assert.True(LabelFlip.Flip(new ACadSharp.Entities.Entity[] { far }, new ACadSharp.Entities.Entity[] { course }, 2.0, out _) == null, "a label nowhere near a course is left alone");
        }
            // ---- traverse closure and AREA (v0.4.11) ----------------------------------------------

        public static void TestTraverseClosureReportsMisclosurePrecisionAndArea()
        {
            // Four 100 m legs around a square that come back 0.03 E, 0.04 N off the start.
            var pts = new List<Vec2> { new Vec2(0, 0), new Vec2(100, 0), new Vec2(100, 100), new Vec2(0, 100), new Vec2(0.03, 0.04) };
            var r = ClosureReport.Of(pts);
            Assert.Near(0.05, r.Misclosure, 1e-9, "linear misclosure");
            Assert.Near(0.03, r.DeltaE, 1e-9, "dE"); Assert.Near(0.04, r.DeltaN, 1e-9, "dN");
            Assert.Near(300 + Math.Sqrt(0.03 * 0.03 + 99.96 * 99.96), r.TraverseLength, 1e-9, "traverse length as run");
            Assert.Equal("1:7,999", ClosureReport.FormatPrecision(r.Precision), "precision ratio");
            Assert.Near(10000, r.Area, 3, "closed area");
            Assert.Near(Angles.Azimuth(new Vec2(0.03, 0.04), new Vec2(0, 0)), r.ClosingAzimuth, 1e-12, "closing course runs back to the start");
            Assert.Equal("perfect closure", ClosureReport.FormatPrecision(ClosureReport.Of(new List<Vec2> { new Vec2(0, 0), new Vec2(1, 0), new Vec2(0, 0) }).Precision));
        }

        public static void TestFigureMeasureCountsArcs()
        {
            // A half-disc of radius 5: diameter along X, then a CCW semicircle back (bulge 1).
            var pts = new List<Vec2> { new Vec2(5, 0), new Vec2(-5, 0) };
            var bulges = new List<double> { 0, 1 };
            Assert.Near(Math.PI * 25 / 2, FigureMeasure.Area(pts, bulges), 1e-9, "half-disc area");
            Assert.Near(10 + Math.PI * 5, FigureMeasure.Perimeter(pts, bulges, true), 1e-9, "diameter plus half circumference");
        }
            // ---- drag-box selection (v0.4.12) -----------------------------------------------------

        public static void TestBoxSelectWindowVersusCrossing()
        {
            var scene = new Scene();
            var g = new SceneGroup();
            scene.Groups.Add(g);
            // 1: short line fully inside the box; 2: long line passing through it with both ends
            // outside; 3: text whose anchor is inside; 4: a circle the box edge cuts;
            // 5: a line far away; 6: an entity drawn as two prims, only one of them inside.
            g.Prims.Add(new Prim { Kind = PrimKind.Polyline, Points = { new Vec2(2, 2), new Vec2(4, 4) }, Handle = 1 });
            g.Prims.Add(new Prim { Kind = PrimKind.Polyline, Points = { new Vec2(-5, 5), new Vec2(15, 5) }, Handle = 2 });
            g.Prims.Add(new Prim { Kind = PrimKind.Text, Center = new Vec2(6, 6), Text = "LOT 5", Height = 1, Handle = 3 });
            g.Prims.Add(new Prim { Kind = PrimKind.Circle, Center = new Vec2(10, 1), Radius = 2, Handle = 4 });
            g.Prims.Add(new Prim { Kind = PrimKind.Polyline, Points = { new Vec2(50, 50), new Vec2(60, 60) }, Handle = 5 });
            g.Prims.Add(new Prim { Kind = PrimKind.Polyline, Points = { new Vec2(1, 1), new Vec2(2, 1) }, Handle = 6 });
            g.Prims.Add(new Prim { Kind = PrimKind.Polyline, Points = { new Vec2(30, 1), new Vec2(31, 1) }, Handle = 6 });
            var box = new Rect(0, 0, 9, 9);

            var window = BoxSelect.Handles(scene, box, crossing: false);
            Assert.True(window.SetEquals(new ulong[] { 1, 3 }), "window takes only what is wholly inside: " + string.Join(",", window));
            var crossing = BoxSelect.Handles(scene, box, crossing: true);
            Assert.True(crossing.SetEquals(new ulong[] { 1, 2, 3, 4, 6 }), "crossing also takes what the box touches: " + string.Join(",", crossing));

            // On a sheet, a viewport group only offers what shows through its clip.
            var sheet = new Scene { IsPaper = true };
            var vp = new SceneGroup { Clip = new Rect(0, 0, 3, 3) };
            vp.Prims.Add(new Prim { Kind = PrimKind.Polyline, Points = { new Vec2(5, 5), new Vec2(6, 6) }, Handle = 7 });
            sheet.Groups.Add(vp);
            Assert.True(BoxSelect.Handles(sheet, new Rect(0, 0, 10, 10), crossing: true).Count == 0, "linework clipped out of the viewport can't be boxed");
        }
            // ---- aligned DIMENSION (v0.4.13) ------------------------------------------------------

        public static void TestAlignedDimensionDrawsItsOwnPicture()
        {
            var doc = new ACadSharp.CadDocument();
            var undo = new UndoStack();
            // 10 m along east, dimension line 2 m north of it.
            var dim = DimensionBuilder.Aligned(new Vec2(0, 0), new Vec2(10, 0), new Vec2(5, 2));
            undo.Push(new AddDimensionCommand(doc.ModelSpace, dim, 0.25, "Dimension"));
            Assert.Equal("10.000", dim.Text, "text is the measured distance");
            Assert.Near(2, dim.DefinitionPoint.Y, 1e-9, "dimension line stands 2 off");
            var block = dim.Block;
            Assert.True(block != null && doc.BlockRecords.Contains(block), "its picture block is registered in the document");
            var lines = block!.Entities.OfType<ACadSharp.Entities.Line>().ToList();
            Assert.Equal(3, lines.Count, "two extension lines and the dimension line");
            Assert.Equal(2, block.Entities.OfType<ACadSharp.Entities.Solid>().Count(), "an arrowhead at each end");
            var arrows = block.Entities.OfType<ACadSharp.Entities.Solid>().Select(a => a.FirstCorner.X).OrderBy(x => x).ToList();
            Assert.Near(0, arrows[0], 1e-9, "first arrow tip on the first extension line");
            Assert.Near(10, arrows[1], 1e-9, "second arrow tip on the second - not both at one end");
            var text = block.Entities.OfType<ACadSharp.Entities.MText>().Single();
            Assert.Near(0.25, text.Height, 1e-9, "text height as asked");
            Assert.True(text.InsertPoint.Y > 2, "text sits above the dimension line");

            var scene = new SceneBuilder(doc).Model();
            Assert.True(scene.AllPrims().Count(pr => pr.Handle == dim.Handle) >= 6, "FD-Draft's own canvas draws the picture");

            // MOVE redraws the picture where the dimension went.
            undo.Push(TransformEntitiesCommand.Move(new ACadSharp.Entities.Entity[] { dim }, 100, 50, "Move"));
            var dimLine = dim.Block.Entities.OfType<ACadSharp.Entities.Line>().OrderBy(l => Math.Abs(l.StartPoint.Y - l.EndPoint.Y)).First();
            Assert.Near(52, dimLine.StartPoint.Y, 1e-9, "picture followed the move");
            Assert.True(dim.Block.Entities.OfType<ACadSharp.Entities.MText>().Single().Height == 0.25, "and kept its text height");
            undo.Undo(); undo.Undo();
            Assert.True(!doc.ModelSpace.Entities.Contains(dim), "undo removes it");
            undo.Redo();
            Assert.True(doc.ModelSpace.Entities.Contains(dim) && dim.Block.Entities.Count > 0, "redo puts it back with its picture");
        }

        public static void TestAlignedDimensionSurvivesDwgRoundTrip()
        {
            var doc = new ACadSharp.CadDocument();
            var undo = new UndoStack();
            var dim = DimensionBuilder.Aligned(new Vec2(1, 1), new Vec2(4, 5), new Vec2(0, 3));
            undo.Push(new AddDimensionCommand(doc.ModelSpace, dim, 0.2, "Dimension"));
            // Round-trip after an undo/redo too - the path most likely to leave a stale block.
            undo.Undo(); undo.Redo();
            string path = Path.Combine(Path.GetTempPath(), "fdd-dim-test.dwg");
            ACadSharp.IO.DwgWriter.Write(path, doc);

            var back = ACadSharp.IO.DwgReader.Read(path);
            var d = back.ModelSpace.Entities.OfType<ACadSharp.Entities.DimensionAligned>().Single();
            Assert.Near(5, d.Measurement, 1e-6, "3-4-5 measurement survives");
            Assert.Equal("5.000", d.Text, "text survives");
            Assert.True(d.Block != null && d.Block.Entities.OfType<ACadSharp.Entities.Line>().Count() == 3, "its picture block survives with its lines");
            Assert.True(new SceneBuilder(back).Model().AllPrims().Any(), "and renders after reading back");
        }
            public static void TestCopyAndMirrorRebuildDimensionsWithTheirOwnPicture()
        {
            var doc = new ACadSharp.CadDocument();
            var undo = new UndoStack();
            var dim = DimensionBuilder.Aligned(new Vec2(0, 0), new Vec2(10, 0), new Vec2(5, 2));
            undo.Push(new AddDimensionCommand(doc.ModelSpace, dim, 0.3, "Dimension"));

            var pairs = EntityOps.Copies(new ACadSharp.Entities.Entity[] { dim }, 0, 20);
            undo.Push(EntityOps.AddBesideSources(pairs, "Copy")!);
            var copy = (ACadSharp.Entities.DimensionAligned)pairs[0].Copy;
            Assert.True(copy.Block != null && copy.Block != dim.Block, "the copy has a picture block of its own");
            Assert.Near(22, copy.DefinitionPoint.Y, 1e-9, "copy moved");
            Assert.Near(0.3, copy.Block!.Entities.OfType<ACadSharp.Entities.MText>().Single().Height, 1e-9, "and kept the text height");
            Assert.Near(2, dim.Block.Entities.OfType<ACadSharp.Entities.Line>().Max(l => l.EndPoint.Y), 1, "the source's picture is untouched");

            var mirrored = (ACadSharp.Entities.DimensionAligned)EntityOps.Mirrored(dim, new Vec2(0, -5), new Vec2(10, -5))!;
            undo.Push(EntityOps.AddBesideSources(new[] { ((ACadSharp.Entities.Entity)dim, (ACadSharp.Entities.Entity)mirrored) }, "Mirror")!);
            Assert.Near(-12, mirrored.DefinitionPoint.Y, 1e-9, "dimension line reflected to the other side");
            Assert.Equal("10.000", mirrored.Text, "same measured text");
            Assert.True(mirrored.Block.Entities.OfType<ACadSharp.Entities.MText>().Single().InsertPoint.Y > -12, "text still sits above its dimension line, reading forwards");
        }
            // ---- sheet scale (VPSCALE, v0.4.14) ---------------------------------------------------

        public static void TestSheetScaleRezoomsViewportTitleBlockAndAnnotation()
        {
            var doc = new ACadSharp.CadDocument();
            var layout = new ACadSharp.Objects.Layout("SHEET-11X17");
            doc.Layouts.Add(layout);
            // A 200 x 100 mm plan viewport showing 50 m of height: 1:500 in metres on a mm sheet.
            var vp = new ACadSharp.Entities.Viewport { Center = new CSMath.XYZ(150, 100, 0), Width = 200, Height = 100, ViewHeight = 50, ViewCenter = new CSMath.XY(1000, 2000) };
            layout.AddViewport(vp);
            var sheet = layout.AssociatedBlock.Entities;
            var scaleText = new ACadSharp.Entities.TextEntity { Value = "SCALE 1:500", InsertPoint = new CSMath.XYZ(10, 10, 0) };
            var tick0 = new ACadSharp.Entities.TextEntity { Value = "0", InsertPoint = new CSMath.XYZ(10, 15, 0) };
            var tick20 = new ACadSharp.Entities.TextEntity { Value = "20m", InsertPoint = new CSMath.XYZ(50, 15, 0) };
            var note = new ACadSharp.Entities.TextEntity { Value = "PLAN OF SURVEY", InsertPoint = new CSMath.XYZ(10, 80, 0) };
            foreach (var t in new[] { scaleText, tick0, tick20, note }) sheet.Add(t);

            var label = new ACadSharp.Entities.TextEntity { Value = "N45°E 10.000", Height = 1.25 };
            doc.ModelSpace.Entities.Add(label);
            var sym = new ACadSharp.Tables.BlockRecord("SIB");
            doc.BlockRecords.Add(sym);
            var ins = new ACadSharp.Entities.Insert(sym) { XScale = 2, YScale = 2, ZScale = 2 };
            doc.ModelSpace.Entities.Add(ins);

            Assert.Equal(500.0, SheetScale.StatedDenominator(layout, "SCALE 1:#"), "reads the sheet's stated scale");
            var r = SheetScale.Change(doc, layout, 250, resizeAnnotation: true);
            var undo = new UndoStack();
            undo.Push(r.Command!);
            Assert.Near(25, vp.ViewHeight, 1e-9, "viewport zoomed to 1:250");
            Assert.Near(1000, vp.ViewCenter.X, 1e-9, "about its own centre");
            Assert.Equal("SCALE 1:250", scaleText.Value, "title block scale text");
            Assert.Equal("10m", tick20.Value, "scale-bar tick relabelled");
            Assert.Equal("0", tick0.Value, "zero tick stays");
            Assert.Equal("PLAN OF SURVEY", note.Value, "unrelated text untouched");
            Assert.Near(0.625, label.Height, 1e-9, "model label halved to keep its paper size");
            Assert.Near(1, ins.XScale, 1e-9, "symbol halved too");

            undo.Undo();
            Assert.Near(50, vp.ViewHeight, 1e-9, "one undo restores the viewport");
            Assert.Equal("SCALE 1:500", scaleText.Value, "and the title block");
            Assert.Equal("20m", tick20.Value, "and the scale bar");
            Assert.Near(1.25, label.Height, 1e-9, "and the labels");
            Assert.Near(2, ins.YScale, 1e-9, "and the symbols");

            Assert.True(SheetScale.Change(doc, layout, 500, true).Command == null, "same scale: nothing to do");
        }
            // ---- LABEL existing courses (v0.4.15) -------------------------------------------------

        public static void TestLabelCommandUsesThePipelinesLabelRules()
        {
            var doc = new ACadSharp.CadDocument();
            var std = FirmStandards.Default();
            ACadSharp.Tables.Layer L(string name)
            {
                if (doc.Layers.TryGetValue(name, out var l)) return l;
                l = new ACadSharp.Tables.Layer(name); doc.Layers.Add(l); return l;
            }
            // 1:500 metric: 0.5 m per paper mm. A 30 m course due east.
            var line = new ACadSharp.Entities.Line(new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(30, 0, 0));
            var labels = CourseLabelling.For(line, doc, std, 0.5, L).Cast<ACadSharp.Entities.TextEntity>().ToList();
            Assert.Equal(2, labels.Count, "bearing and distance");
            var bearing = labels.Single(t => t.Layer.Name == std.BearingLayer);
            var distance = labels.Single(t => t.Layer.Name == std.DistanceLayer);
            Assert.Equal("N90%%d00'00\"E", bearing.Value, "bearing text in plan form (degree as %%d for SHX fonts)");
            Assert.Equal(30.0.ToString("F" + std.DistanceDecimals, System.Globalization.CultureInfo.InvariantCulture), distance.Value, "distance to the standards' decimals");
            Assert.Near(std.BearingTextMm * 0.5, bearing.Height, 1e-9, "paper mm times model per mm");
            Assert.True(bearing.AlignmentPoint.Y > 0 && distance.AlignmentPoint.Y < 0, "bearing above, distance below");
            Assert.Near(15, bearing.AlignmentPoint.X, 1e-9, "centred on the course");

            // A polyline with a straight span and a CCW quarter arc: two + two labels.
            var poly = new ACadSharp.Entities.LwPolyline();
            poly.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(0, 0)));
            poly.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(10, 0)) { Bulge = Math.Tan(Math.PI / 8) });
            poly.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(15, 5)));
            var pl = CourseLabelling.For(poly, doc, std, 0.5, L).Cast<ACadSharp.Entities.TextEntity>().ToList();
            Assert.Equal(4, pl.Count, "each span labelled");
            Assert.True(pl.Any(t => t.Value.StartsWith("R=5.") && t.Value.Contains("A=7.85")), "curve data: radius 5, arc length 7.854: " + string.Join(" | ", pl.Select(t => t.Value)));
        }
            // ---- TRIM / EXTEND / FILLET (v0.4.16) -------------------------------------------------

        private static ACadSharp.Entities.Line Ln(double x1, double y1, double x2, double y2) =>
            new ACadSharp.Entities.Line(new CSMath.XYZ(x1, y1, 0), new CSMath.XYZ(x2, y2, 0));

        public static void TestTrimShortensSplitsOrErases()
        {
            var doc = new ACadSharp.CadDocument();
            var line = Ln(0, 0, 10, 0);
            var edgeA = Ln(3, -1, 3, 1); var edgeB = Ln(7, -1, 7, 1);
            foreach (var e in new[] { line, edgeA, edgeB }) doc.ModelSpace.Entities.Add(e);
            var undo = new UndoStack();

            // Pick between the two edges: the middle goes, the line splits in two.
            undo.Push(EntityOps.Trim(line, new Vec2(5, 0), new ACadSharp.Entities.Entity[] { edgeA, edgeB })!);
            Assert.Near(3, line.EndPoint.X, 1e-9, "first piece ends at the first edge");
            var rest = doc.ModelSpace.Entities.OfType<ACadSharp.Entities.Line>().Single(l => Math.Abs(l.StartPoint.X - 7) < 1e-9 && Math.Abs(l.StartPoint.Y) < 1e-9);
            Assert.Near(10, rest.EndPoint.X, 1e-9, "second piece runs from the second edge to the old end");
            undo.Undo();
            Assert.Near(10, line.EndPoint.X, 1e-9, "undo restores the whole line");
            Assert.True(!doc.ModelSpace.Entities.Contains(rest), "and removes the split-off piece");

            // Pick past the last edge: just that end is cut back.
            undo.Push(EntityOps.Trim(line, new Vec2(9, 0), new ACadSharp.Entities.Entity[] { edgeA, edgeB })!);
            Assert.Near(0, line.StartPoint.X, 1e-9, "start kept"); Assert.Near(7, line.EndPoint.X, 1e-9, "end cut back to the edge");
            Assert.True(EntityOps.Trim(Ln(0, 5, 10, 5), new Vec2(5, 5), new ACadSharp.Entities.Entity[] { edgeA }) == null, "an edge that doesn't cross it: nothing to trim");

            // A circle edge cuts too.
            var circle = new ACadSharp.Entities.Circle { Center = new CSMath.XYZ(20, 0, 0), Radius = 2 };
            var through = Ln(15, 0, 25, 0);
            doc.ModelSpace.Entities.Add(through);
            undo.Push(EntityOps.Trim(through, new Vec2(20, 0), new ACadSharp.Entities.Entity[] { circle })!);
            Assert.Near(18, through.EndPoint.X, 1e-9, "trimmed out of the circle");
        }

        public static void TestExtendRunsTheNearerEndToTheBoundary()
        {
            var line = Ln(0, 0, 5, 0);
            var wall = Ln(12, -5, 12, 5);
            var arc = new ACadSharp.Entities.Arc { Center = new CSMath.XYZ(0, 0, 0), Radius = 8, StartAngle = Math.PI / 2, EndAngle = 3 * Math.PI / 2 };
            var undo = new UndoStack();
            undo.Push(EntityOps.Extend(line, new Vec2(4.5, 0), new ACadSharp.Entities.Entity[] { wall, arc })!);
            Assert.Near(12, line.EndPoint.X, 1e-9, "end runs out to the wall");
            Assert.Near(0, line.StartPoint.X, 1e-9, "start untouched");
            undo.Push(EntityOps.Extend(line, new Vec2(0.5, 0), new ACadSharp.Entities.Entity[] { wall, arc })!);
            Assert.Near(-8, line.StartPoint.X, 1e-9, "start runs back to the arc (on its sweep only)");
            undo.Undo(); undo.Undo();
            Assert.Near(5, line.EndPoint.X, 1e-9, "undo restores");
            Assert.True(EntityOps.Extend(Ln(0, 20, 5, 20), new Vec2(5, 20), new ACadSharp.Entities.Entity[] { wall }) == null, "nothing ahead: no extend");
        }

        public static void TestFilletRoundsACornerTangentially()
        {
            var doc = new ACadSharp.CadDocument();
            // Two lot lines meeting at (10,0): one east from the origin, one north up to (10,10).
            var l1 = Ln(0, 0, 10, 0); var l2 = Ln(10, 0, 10, 10);
            doc.ModelSpace.Entities.Add(l1); doc.ModelSpace.Entities.Add(l2);
            var undo = new UndoStack();
            var cmd = EntityOps.Fillet(l1, new Vec2(2, 0), l2, new Vec2(10, 8), 3, out var arc);
            undo.Push(cmd!);
            Assert.Near(7, l1.EndPoint.X, 1e-9, "first line cut back to its tangent point");
            Assert.Near(0, l1.StartPoint.X, 1e-9, "its far end kept");
            Assert.Near(3, l2.StartPoint.Y, 1e-9, "second line cut back to its tangent point");
            Assert.True(arc != null && doc.ModelSpace.Entities.Contains(arc), "the curve is added");
            Assert.Near(7, arc!.Center.X, 1e-9, "centre E"); Assert.Near(3, arc.Center.Y, 1e-9, "centre N");
            Assert.Near(3 * Math.PI / 2, arc.StartAngle, 1e-9, "curve starts at the first tangent point (south of centre)");
            Assert.Near(0, arc.EndAngle, 1e-9, "and ends at the second (east of centre)");
            undo.Undo();
            Assert.Near(10, l1.EndPoint.X, 1e-9, "undo restores the corner");
            Assert.True(!doc.ModelSpace.Entities.Contains(arc), "and removes the curve");

            // Radius 0 closes a gap to a sharp corner; lines that cross keep the picked sides.
            var a = Ln(0, 0, 8, 0); var b = Ln(10, -3, 10, 10);
            doc.ModelSpace.Entities.Add(a); doc.ModelSpace.Entities.Add(b);
            undo.Push(EntityOps.Fillet(a, new Vec2(1, 0), b, new Vec2(10, 6), 0, out var none)!);
            Assert.True(none == null, "radius 0 adds no curve");
            Assert.Near(10, a.EndPoint.X, 1e-9, "first line runs to the corner");
            Assert.Near(0, b.StartPoint.Y, 1e-9, "second line trimmed to the corner, keeping the picked north part");
            Assert.Near(10, b.EndPoint.Y, 1e-9, "north end kept");
            Assert.True(EntityOps.Fillet(Ln(0, 0, 10, 0), new Vec2(1, 0), Ln(0, 5, 10, 5), new Vec2(1, 5), 1, out _) == null, "parallel lines can't be filleted");
        }
            // ---- JOIN (v0.4.17) -------------------------------------------------------------------

        public static void TestJoinChainsLinesAndArcsIntoAClosedPolyline()
        {
            var doc = new ACadSharp.CadDocument();
            var layer = new ACadSharp.Tables.Layer("PLAN-SubjectBoundary");
            doc.Layers.Add(layer);
            // A lot drawn as loose pieces, some backwards: south line, a corner-rounding arc,
            // east line (reversed), north line, west line - plus a stray line touching nothing.
            var south = Ln(0, 0, 17, 0);
            var round = new ACadSharp.Entities.Arc { Center = new CSMath.XYZ(17, 3, 0), Radius = 3, StartAngle = 3 * Math.PI / 2, EndAngle = 0 };
            var east = Ln(20, 20, 20, 3);
            var north = Ln(20, 20, 0, 20);
            var west = Ln(0, 20, 0, 0);
            var stray = Ln(50, 50, 60, 50);
            var all = new ACadSharp.Entities.Entity[] { south, round, east, north, west, stray };
            foreach (var e in all) { e.Layer = layer; doc.ModelSpace.Entities.Add(e); }
            var undo = new UndoStack();

            undo.Push(EntityOps.Join(all, 1e-6, out var made)!);
            Assert.Equal(1, made.Count, "one polyline");
            var pl = made[0];
            Assert.True(pl.IsClosed, "the ring closes");
            Assert.Equal(5, pl.Vertices.Count, "five vertices");
            Assert.True(pl.Layer == layer, "on the pieces' layer");
            Assert.Near(20 * 20 - (9 - Math.PI * 9 / 4), FigureMeasure.Area(pl.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList(), pl.Vertices.Select(v => v.Bulge).ToList()), 1e-9, "area includes the rounded corner exactly");
            Assert.True(!doc.ModelSpace.Entities.Contains(south) && !doc.ModelSpace.Entities.Contains(round), "the pieces are replaced");
            Assert.True(doc.ModelSpace.Entities.Contains(stray), "a piece touching nothing is left alone");

            undo.Undo();
            Assert.True(doc.ModelSpace.Entities.Contains(south) && doc.ModelSpace.Entities.Contains(round) && doc.ModelSpace.Entities.Contains(east), "undo puts the pieces back");
            Assert.True(!doc.ModelSpace.Entities.Contains(pl), "and removes the polyline");

            Assert.True(EntityOps.Join(new ACadSharp.Entities.Entity[] { stray }, 1e-6, out _) == null, "nothing to join");
        }
            // ---- linear and radius dimensions (v0.4.18) ------------------------------------------

        public static void TestLinearDimensionPicksHorizontalOrVerticalAndTurnsWithRotate()
        {
            var doc = new ACadSharp.CadDocument();
            var undo = new UndoStack();
            var p1 = new Vec2(0, 0); var p2 = new Vec2(30, 40);
            // Placed above both points: horizontal, measuring dE = 30.
            var h = DimensionBuilder.Linear(p1, p2, new Vec2(15, 45));
            Assert.Near(0, h.Rotation, 1e-12, "horizontal"); Assert.Equal("30.000", h.Text, "measures dE");
            // Placed beside them: vertical, measuring dN = 40.
            var v = DimensionBuilder.Linear(p1, p2, new Vec2(35, 20));
            Assert.Near(Math.PI / 2, v.Rotation, 1e-12, "vertical"); Assert.Equal("40.000", v.Text, "measures dN");
            undo.Push(new AddDimensionCommand(doc.ModelSpace, h, 0.2, "Dimension"));
            var dimLine = h.Block.Entities.OfType<ACadSharp.Entities.Line>().Single(l => Math.Abs(l.StartPoint.Y - l.EndPoint.Y) < 1e-9 && Math.Abs(l.StartPoint.Y - 45) < 1e-9);
            Assert.Near(30, Math.Abs(dimLine.EndPoint.X - dimLine.StartPoint.X), 1e-9, "dimension line spans dE at the placed height");
            Assert.Equal(3, h.Block.Entities.OfType<ACadSharp.Entities.Line>().Count(), "two extension lines + dimension line");

            // ROTATE 90 degrees about the origin: the measuring direction turns with it.
            undo.Push(TransformEntitiesCommand.Rotate(new ACadSharp.Entities.Entity[] { h }, new CSMath.XYZ(0, 0, 0), Math.PI / 2, "Rotate"));
            Assert.Near(Math.PI / 2, h.Rotation, 1e-9, "rotation followed the ROTATE");
            Assert.Near(-40, h.SecondPoint.X, 1e-9, "points rotated");
            Assert.True(h.Block.Entities.OfType<ACadSharp.Entities.Line>().Any(l => Math.Abs(l.StartPoint.X - l.EndPoint.X) < 1e-9 && Math.Abs(Math.Abs(l.EndPoint.Y - l.StartPoint.Y) - 30) < 1e-9), "picture redrawn: dimension line now north-south, still 30 long");
        }

        public static void TestRadiusDimensionAndDwgRoundTrip()
        {
            var doc = new ACadSharp.CadDocument();
            var undo = new UndoStack();
            var r = DimensionBuilder.Radius(new Vec2(10, 10), 7.5, new Vec2(20, 10));
            undo.Push(new AddDimensionCommand(doc.ModelSpace, r, 0.25, "Dimension"));
            Assert.Equal("R7.500", r.Text, "radius text");
            Assert.Near(17.5, r.AngleVertex.X, 1e-9, "points at the curve toward the pick");
            Assert.Near(7.5, r.Measurement, 1e-9, "measurement is the radius");
            Assert.Equal(1, r.Block.Entities.OfType<ACadSharp.Entities.Solid>().Count(), "one arrowhead, on the curve");
            Assert.Near(17.5, r.Block.Entities.OfType<ACadSharp.Entities.Solid>().Single().FirstCorner.X, 1e-9, "arrow tip on the curve");

            var lin = DimensionBuilder.Linear(new Vec2(0, 0), new Vec2(5, 3), new Vec2(2, 6));
            undo.Push(new AddDimensionCommand(doc.ModelSpace, lin, 0.2, "Dimension"));
            // Copies rebuild their own picture, of the same kind.
            var pairs = EntityOps.Copies(new ACadSharp.Entities.Entity[] { r, lin }, 100, 0);
            undo.Push(EntityOps.AddBesideSources(pairs, "Copy")!);
            Assert.True(pairs[0].Copy is ACadSharp.Entities.DimensionRadius rc && rc.Block != r.Block && Math.Abs(rc.AngleVertex.X - 117.5) < 1e-9, "radius dimension copied with its own picture");
            Assert.True(pairs[1].Copy is ACadSharp.Entities.DimensionLinear lc && lc.Rotation == 0 && lc.Text == "5.000", "linear dimension copied as linear");

            string path = Path.Combine(Path.GetTempPath(), "fdd-dim2-test.dwg");
            ACadSharp.IO.DwgWriter.Write(path, doc);
            var back = ACadSharp.IO.DwgReader.Read(path);
            var rb = back.ModelSpace.Entities.OfType<ACadSharp.Entities.DimensionRadius>().ToList();
            var lb = back.ModelSpace.Entities.OfType<ACadSharp.Entities.DimensionLinear>().ToList();
            Assert.Equal(2, rb.Count, "both radius dimensions survive");
            Assert.Equal(2, lb.Count, "both linear dimensions survive");
            Assert.True(rb.All(d => d.Text == "R7.500" && d.Block != null && d.Block.Entities.Any()), "with their text and pictures");
            Assert.True(lb.All(d => Math.Abs(d.Measurement - 5) < 1e-6), "linear measurement survives");
        }
            // ---- angular and diameter dimensions (v0.4.19) ---------------------------------------

        public static void TestAngularAndDiameterDimensions()
        {
            var doc = new ACadSharp.CadDocument();
            var undo = new UndoStack();
            var v = new Vec2(0, 0); var east = new Vec2(10, 0); var north = new Vec2(0, 10);
            // Arc placed in the north-east quadrant: the 90° angle; in the south-west: the 270° one.
            var ang = DimensionBuilder.Angular(v, east, north, new Vec2(4, 4));
            Assert.Equal("90%%d00'00\"", ang.Text, "interior angle in DMS");
            Assert.Equal("270%%d00'00\"", DimensionBuilder.Angular(v, east, north, new Vec2(-4, -4)).Text, "the other way round");
            Assert.Equal("45%%d30'15\"", DimensionBuilder.Dms((45 + 30 / 60.0 + 15 / 3600.0) * Math.PI / 180), "DMS formatting");
            Assert.Equal("13%%d00'00\"", DimensionBuilder.Dms((12 + 59 / 60.0 + 59.6 / 3600.0) * Math.PI / 180), "seconds carry, never 60\"");
            undo.Push(new AddDimensionCommand(doc.ModelSpace, ang, 0.2, "Dimension"));
            var arc = ang.Block.Entities.OfType<ACadSharp.Entities.Arc>().Single();
            Assert.Near(Math.Sqrt(32), arc.Radius, 1e-9, "arc through the placed point");
            Assert.Near(0, arc.StartAngle, 1e-9, "from the east ray"); Assert.Near(Math.PI / 2, arc.EndAngle, 1e-9, "to the north ray");
            Assert.Equal(2, ang.Block.Entities.OfType<ACadSharp.Entities.Solid>().Count(), "an arrow at each end of the arc");

            var dia = DimensionBuilder.Diameter(new Vec2(20, 0), 3, new Vec2(20, 9));
            undo.Push(new AddDimensionCommand(doc.ModelSpace, dia, 0.2, "Dimension"));
            Assert.Equal("%%c6.000", dia.Text, "diameter text");
            Assert.Near(6, dia.Measurement, 1e-9, "measures the diameter");
            Assert.Near(3, dia.AngleVertex.Y, 1e-9, "runs toward the pick"); Assert.Near(-3, dia.DefinitionPoint.Y, 1e-9, "and right across");

            // MOVE redraws; COPY rebuilds the same kinds.
            undo.Push(TransformEntitiesCommand.Move(new ACadSharp.Entities.Entity[] { ang }, 5, 5, "Move"));
            Assert.Near(5, ang.Block.Entities.OfType<ACadSharp.Entities.Arc>().Single().Center.X, 1e-9, "angular picture followed the move");
            var pairs = EntityOps.Copies(new ACadSharp.Entities.Entity[] { ang, dia }, 0, 50);
            undo.Push(EntityOps.AddBesideSources(pairs, "Copy")!);
            Assert.True(pairs[0].Copy is ACadSharp.Entities.DimensionAngular3Pt && pairs[1].Copy is ACadSharp.Entities.DimensionDiameter, "copied as the same kinds");

            string path = Path.Combine(Path.GetTempPath(), "fdd-dim3-test.dwg");
            ACadSharp.IO.DwgWriter.Write(path, doc);
            var back = ACadSharp.IO.DwgReader.Read(path);
            var ab = back.ModelSpace.Entities.OfType<ACadSharp.Entities.DimensionAngular3Pt>().ToList();
            var db = back.ModelSpace.Entities.OfType<ACadSharp.Entities.DimensionDiameter>().ToList();
            Assert.Equal(2, ab.Count, "angular dimensions survive the DWG round trip");
            Assert.Equal(2, db.Count, "diameter dimensions survive");
            Assert.True(ab.All(d => d.Text == "90%%d00'00\"" && d.Block != null && d.Block.Entities.OfType<ACadSharp.Entities.Arc>().Any()), "with their text and pictures");
            Assert.Near(Math.PI / 2, ab[0].Measurement, 1e-6, "angle survives");
        }
            // ---- Polyline2D vertex insert/delete (v0.4.20) ---------------------------------------

        public static void TestPolyline2DVertexInsertDeleteAndDwgRoundTrip()
        {
            var doc = new ACadSharp.CadDocument();
            var p = new ACadSharp.Entities.Polyline2D();
            p.Vertices.Add(new ACadSharp.Entities.Vertex2D(new CSMath.XYZ(0, 0, 0)));
            p.Vertices.Add(new ACadSharp.Entities.Vertex2D(new CSMath.XYZ(5, 0, 0)) { Bulge = Math.Tan(Math.PI / 8) });
            p.Vertices.Add(new ACadSharp.Entities.Vertex2D(new CSMath.XYZ(10, 5, 0)));
            p.Vertices.Add(new ACadSharp.Entities.Vertex2D(new CSMath.XYZ(10, 10, 0)));
            var layer = new ACadSharp.Tables.Layer("PLAN-Fence"); doc.Layers.Add(layer);
            p.Layer = layer;
            doc.ModelSpace.Entities.Add(p);
            var undo = new UndoStack();
            ACadSharp.Entities.Polyline2D Current() => doc.ModelSpace.Entities.OfType<ACadSharp.Entities.Polyline2D>().Single();

            var del = PolylineVertices.Delete(p, 3, "Delete vertex");
            undo.Push(del);
            var d = Current();
            Assert.True(d != p && d == ((ReplacePolyline2DCommand)del).Replacement, "a rebuilt polyline is swapped in");
            Assert.Equal(3, d.Vertices.Count, "vertex removed");
            Assert.True(d.Layer == layer, "on the same layer");
            Assert.Near(Math.Tan(Math.PI / 8), d.Vertices[1].Bulge, 1e-12, "the arc before it untouched");
            undo.Undo();
            Assert.True(Current() == p && p.Vertices.Count == 4, "undo swaps the untouched original back");
            Assert.Near(10, p.Vertices[3].Location.Y, 1e-9, "vertices in order");

            // Split the quarter arc (centre (5,5), radius 5) at its midpoint.
            var mid = Construct.Span.FromBulge(new Vec2(5, 0), new Vec2(10, 5), Math.Tan(Math.PI / 8)).Midpoint;
            undo.Push(PolylineVertices.Insert(p, 1, mid, "Insert vertex", out int at));
            var ins = Current();
            Assert.Equal(2, at, "new vertex index");
            Assert.Equal(5, ins.Vertices.Count, "vertex added");
            Assert.Near(mid.X, ins.Vertices[2].Location.X, 1e-9, "at the split point, in order");
            Assert.Near(10, ins.Vertices[4].Location.Y, 1e-9, "the rest follow in order");
            Assert.Near(Math.Tan(Math.PI / 16), ins.Vertices[1].Bulge, 1e-12, "first half keeps the curve");
            Assert.Near(Math.Tan(Math.PI / 16), ins.Vertices[2].Bulge, 1e-12, "second half keeps the curve");
            Assert.True(!PolylineVertices.CanDelete(new ACadSharp.Entities.LwPolyline()), "an empty polyline can't lose a vertex");

            string path = Path.Combine(Path.GetTempPath(), "fdd-poly2d-test.dwg");
            ACadSharp.IO.DwgWriter.Write(path, doc);
            var back = ACadSharp.IO.DwgReader.Read(path).ModelSpace.Entities.OfType<ACadSharp.Entities.Polyline2D>().Single();
            Assert.Equal(5, back.Vertices.Count, "the rebuilt polyline survives a DWG round trip");
            Assert.Near(mid.X, back.Vertices[2].Location.X, 1e-9, "with the new vertex in place");
            undo.Undo();
            Assert.True(Current() == p && p.Vertices.Count == 4, "undo restores the original");
        }
            // ---- MVIEW: put model space onto a blank sheet (v0.4.21) ------------------------------

        public static void TestMviewPutsModelSpaceOntoABlankSheet()
        {
            var doc = new ACadSharp.CadDocument();
            // A legacy job: the survey is in model space, and a sheet tab with only a title-block line.
            doc.ModelSpace.Entities.Add(Ln(309500, 4869100, 309560, 4869140));
            var layout = new ACadSharp.Objects.Layout("11X17");
            doc.Layouts.Add(layout);
            layout.AssociatedBlock.Entities.Add(Ln(10, 10, 420, 10));
            var before = new SceneBuilder(doc).Layout("11X17");
            Assert.True(!before.Groups.Any(g => g.Clip.HasValue), "starts with no working viewport - the 'blank sheet' case");

            var area = new Rect(20, 20, 300, 260);
            double mpp = SheetViewports.FitScale(60, 40, area, 1.0, out double den);
            Assert.Equal(250.0, den, "60 x 40 m in a 280 x 240 mm box rounds up to 1:250");
            Assert.Near(0.25, mpp, 1e-12, "0.25 m per mm");
            var vp = SheetViewports.Create(area, new Vec2(309530, 4869120), mpp, doc.Layers["0"]);
            var undo = new UndoStack();
            undo.Push(new AddEntitiesCommand(layout.AssociatedBlock, new ACadSharp.Entities.Entity[] { vp }, "Viewport"));
            Assert.True(!vp.RepresentsPaper, "a real plan viewport, not the paper background one");
            Assert.True(SheetScale.PlanViewport(layout) == vp, "found as the sheet's plan viewport");

            var after = new SceneBuilder(doc).Layout("11X17");
            var vpGroup = after.Groups.SingleOrDefault(g => g.Clip.HasValue);
            Assert.True(vpGroup != null && vpGroup.Prims.Any(), "the survey now shows through the sheet");
            var shown = vpGroup!.Prims.SelectMany(p => p.Points).ToList();
            Assert.True(shown.All(p => p.X >= 20 - 1e-6 && p.X <= 300 + 1e-6 && p.Y >= 20 - 1e-6 && p.Y <= 260 + 1e-6), "drawn inside the viewport, at sheet scale");
            Assert.True(after.ModelAt(new Vec2(160, 140)).HasValue, "picks inside it resolve to model coordinates");
            undo.Undo();
            Assert.True(SheetScale.PlanViewport(layout) == null, "undo takes it off again");
        }
            public static void TestALonePlanViewportIsNotMistakenForThePaperBackground()
        {
            // A sheet tab never opened in AutoCAD can hold only its plan viewport. ACadSharp numbers
            // viewports by position, so that one comes out as #1 - "represents paper" - and used to
            // be skipped, leaving the tab showing only its title block.
            var doc = new ACadSharp.CadDocument();
            doc.ModelSpace.Entities.Add(Ln(309500, 4869100, 309560, 4869140));
            var layout = new ACadSharp.Objects.Layout("RPLAN-22X34") { PaperWidth = 863.6, PaperHeight = 558.8 };
            doc.Layouts.Add(layout);
            // Reading a DWG adds viewports in file order and never inserts a background one, so a
            // tab whose only viewport is its plan reads back as this: the lone #1 viewport, set up
            // to look at the survey. (In memory ACadSharp won't let the background one be removed,
            // so the lone one is turned into the plan viewport instead - the same end state.)
            var plan = layout.AssociatedBlock.Entities.OfType<ACadSharp.Entities.Viewport>().Single();
            plan.Center = new CSMath.XYZ(360, 280, 0); plan.Width = 680; plan.Height = 520;
            plan.ViewCenter = new CSMath.XY(309530, 4869120); plan.ViewHeight = 520 * 0.25;
            plan.Status = ACadSharp.Entities.ViewportStatusFlags.CurrentlyAlwaysEnabled;
            Assert.True(plan.RepresentsPaper, "ACadSharp calls the lone viewport #1 / paper - the trap");
            Assert.True(!ViewportRules.IsPaperBackground(plan, 863.6, 558.8), "but it looks at the survey's coordinates, so it's the plan");
            var scene = new SceneBuilder(doc).Layout("RPLAN-22X34");
            Assert.True(scene.Groups.Any(g => g.Clip.HasValue && g.Prims.Any()), "the survey shows through it");
            Assert.True(SheetScale.PlanViewport(layout) == plan, "VPSCALE finds it too");
            var info = ViewportRules.Describe(layout);
            Assert.True(info.Count == 2 && info[1].Contains("SHOWS MODEL") && info[1].Contains("1:250"), "VPINFO explains it: " + string.Join(" | ", info));

            // The ordinary case is unchanged: with a real background viewport, #1 is still paper.
            var normal = new ACadSharp.Objects.Layout("11X17") { PaperWidth = 431.8, PaperHeight = 279.4 };
            doc.Layouts.Add(normal);
            var bg = normal.AssociatedBlock.Entities.OfType<ACadSharp.Entities.Viewport>().Single();
            bg.ViewCenter = new CSMath.XY(215, 140); bg.ViewHeight = 279.4; bg.Height = 279.4; bg.Width = 431.8;
            Assert.True(ViewportRules.IsPaperBackground(bg, 431.8, 279.4), "a lone viewport looking at the sheet itself is the background");
            var added = SheetViewports.Create(new Rect(20, 20, 300, 260), new Vec2(309530, 4869120), 0.25, doc.Layers["0"]);
            normal.AssociatedBlock.Entities.Add(added);
            Assert.True(ViewportRules.IsPaperBackground(bg, 431.8, 279.4) && !ViewportRules.IsPaperBackground(added, 431.8, 279.4), "with two, #1 is the background and the other the plan");
        }
            public static void TestMTextWrapsToItsBoxWidth()
        {
            var doc = new ACadSharp.CadDocument();
            var note = new ACadSharp.Entities.MText
            {
                Value = "THE INTENDED PLOT SIZE OF THIS PLAN IS 559mm IN WIDTH BY 432mm IN HEIGHT WHEN PLOTTED AT A SCALE OF 1:300.\\PSECOND PARAGRAPH",
                InsertPoint = new CSMath.XYZ(0, 100, 0), Height = 2, RectangleWidth = 100, AttachmentPoint = ACadSharp.Entities.AttachmentPointType.TopLeft,
            };
            doc.ModelSpace.Entities.Add(note);
            // (No style = txt.shx: drawn a word at a time; a line is the words at one height.)
            List<string> Lines() => new SceneBuilder(doc).Model().AllPrims().Where(p => p.Kind == PrimKind.Text)
                .GroupBy(p => Math.Round(p.Center.Y, 6)).OrderByDescending(g => g.Key)
                .Select(g => string.Join(" ", g.OrderBy(p => p.Center.X).Select(p => p.Text))).ToList();
            var lines = Lines();
            Assert.True(lines.Count >= 3, "the long paragraph wraps onto more than one line: " + string.Join(" | ", lines));
            Assert.True(lines.All(l => ShxMetrics.Width(l, 2) <= 100 + 1e-9), "every line fits the box");
            Assert.Equal("SECOND PARAGRAPH", lines.Last(), "paragraph breaks still start a new line");
            Assert.True(string.Join(" ", lines.Take(lines.Count - 1)).StartsWith("THE INTENDED PLOT SIZE OF THIS PLAN IS 559mm"), "words kept in order");

            note.RectangleWidth = 0;
            Assert.Equal(2, Lines().Count, "no box width: no wrapping, one line per paragraph");
        }
            // ---- moving drafted labels (v0.4.25) --------------------------------------------------

        private static Vec2 DrawnAt(ACadSharp.CadDocument doc, ulong handle) =>
            new SceneBuilder(doc).Model().AllPrims().First(p => p.Handle == handle && p.Kind == PrimKind.Text).Center;

        public static void TestMovingAndCopyingDraftedLabelsActuallyMovesThem()
        {
            var doc = new ACadSharp.CadDocument();
            // A point number exactly as TemplateDrafter writes it: aligned bottom-left, so AutoCAD
            // and FD-Draft both place it by its AlignmentPoint.
            var at = new CSMath.XYZ(306498.873, 4894680.060, 0);
            var number = new ACadSharp.Entities.TextEntity
            {
                Value = "1003", InsertPoint = at, AlignmentPoint = at, Height = 0.4,
                HorizontalAlignment = ACadSharp.Entities.TextHorizontalAlignment.Left, VerticalAlignment = ACadSharp.Entities.TextVerticalAlignmentType.Bottom,
            };
            // A centred bearing label.
            var bearing = new ACadSharp.Entities.TextEntity
            {
                Value = "N45%%d00'00\"E", InsertPoint = new CSMath.XYZ(10, 10, 0), AlignmentPoint = new CSMath.XYZ(10, 10, 0), Height = 0.4,
                HorizontalAlignment = ACadSharp.Entities.TextHorizontalAlignment.Center, VerticalAlignment = ACadSharp.Entities.TextVerticalAlignmentType.Bottom,
            };
            doc.ModelSpace.Entities.Add(number); doc.ModelSpace.Entities.Add(bearing);
            var undo = new UndoStack();

            undo.Push(TransformEntitiesCommand.Move(new ACadSharp.Entities.Entity[] { number, bearing }, 1.5, -2, "Move"));
            Assert.Near(at.X + 1.5, DrawnAt(doc, number.Handle).X, 1e-9, "the point number is drawn where it was moved to (E)");
            Assert.Near(at.Y - 2, DrawnAt(doc, number.Handle).Y, 1e-9, "(N)");
            Assert.Near(11.5, DrawnAt(doc, bearing.Handle).X, 1e-9, "the centred bearing moved too");
            Assert.Near(at.X + 1.5, number.InsertPoint.X, 1e-9, "insertion point kept in step for AutoCAD");
            undo.Undo();
            Assert.Near(at.X, DrawnAt(doc, number.Handle).X, 1e-9, "undo puts it back");

            // ROTATE turns it about the pivot, anchor and all.
            undo.Push(TransformEntitiesCommand.Rotate(new ACadSharp.Entities.Entity[] { bearing }, new CSMath.XYZ(0, 0, 0), Math.PI / 2, "Rotate"));
            Assert.Near(-10, DrawnAt(doc, bearing.Handle).X, 1e-9, "rotated anchor E"); Assert.Near(10, DrawnAt(doc, bearing.Handle).Y, 1e-9, "rotated anchor N");
            undo.Undo();

            // COPY lands the copy at the offset, not on top of the original.
            var pairs = EntityOps.Copies(new ACadSharp.Entities.Entity[] { number }, 5, 0);
            undo.Push(EntityOps.AddBesideSources(pairs, "Copy")!);
            Assert.Near(at.X + 5, DrawnAt(doc, pairs[0].Copy.Handle).X, 1e-9, "copy drawn at the offset");
            Assert.Near(at.X, DrawnAt(doc, number.Handle).X, 1e-9, "original stays");

            // ROTATE turns an MTEXT (ACadSharp moves it but never turns it).
            var note = new ACadSharp.Entities.MText { Value = "LOT 5", InsertPoint = new CSMath.XYZ(0, 0, 0), Height = 0.5, AlignmentPoint = new CSMath.XYZ(1, 0, 0) };
            doc.ModelSpace.Entities.Add(note);
            undo.Push(TransformEntitiesCommand.Rotate(new ACadSharp.Entities.Entity[] { note }, new CSMath.XYZ(0, 0, 0), Math.PI / 2, "Rotate"));
            Assert.Near(Math.PI / 2, note.Rotation, 1e-9, "MTEXT turned 90 degrees");
            undo.Undo();
            Assert.Near(0, note.Rotation, 1e-9, "and back");

            // A block's attribute (e.g. a monument's number) moves with the block.
            var blk = new ACadSharp.Tables.BlockRecord("PLAN-FOUND MONUMENT");
            doc.BlockRecords.Add(blk);
            var ins = new ACadSharp.Entities.Insert(blk) { InsertPoint = new CSMath.XYZ(0, 0, 0) };
            var att = new ACadSharp.Entities.AttributeEntity
            {
                Tag = "PT", Value = "12", InsertPoint = new CSMath.XYZ(1, 1, 0), AlignmentPoint = new CSMath.XYZ(1, 1, 0), Height = 0.3,
                HorizontalAlignment = ACadSharp.Entities.TextHorizontalAlignment.Center,
            };
            ins.Attributes.Add(att);
            doc.ModelSpace.Entities.Add(ins);
            undo.Push(TransformEntitiesCommand.Move(new ACadSharp.Entities.Entity[] { ins }, 3, 0, "Move"));
            Assert.Near(4, att.AlignmentPoint.X, 1e-9, "the attribute's anchor moved with its block");
        }
            public static void TestClickingAnywhereOnALabelHitsIt()
        {
            // Point number "1003", anchored at its bottom-left corner, cap height 1.
            var number = new Prim { Kind = PrimKind.Text, Text = "1003", Center = new Vec2(100, 100), Height = 1, H = HAlign.Left, V = VAlign.Bottom };
            double w = PdfSceneWriter.MeasureText("1003", 1);
            Assert.True(w > 2 && w < 4, "four digits are about 3 cap heights wide: " + w);
            Assert.Near(0, TextHit.Distance(number, new Vec2(100 + w * 0.75, 100.5)), 1e-12, "a click on the last digits hits it - not just near the anchor corner");
            Assert.True(TextHit.Distance(number, new Vec2(100 + w + 1, 100.5)) > 0.7, "a click clear of the text misses");
            Assert.True(TextHit.Distance(number, new Vec2(100.5, 97)) > 1.5, "below it misses");

            // A centred bearing running up at 30 degrees: hit along its own direction.
            double r = Math.PI / 6;
            var bearing = new Prim { Kind = PrimKind.Text, Text = "N60°00'00\"E", Center = new Vec2(0, 0), Height = 1, Rotation = r, H = HAlign.Center, V = VAlign.Bottom };
            double bw = PdfSceneWriter.MeasureText(bearing.Text, 1);
            var alongRight = new Vec2(Math.Cos(r), Math.Sin(r)) * (bw * 0.4) + new Vec2(-Math.Sin(r), Math.Cos(r)) * 0.5;
            Assert.Near(0, TextHit.Distance(bearing, alongRight), 1e-12, "a click on its right half, along the rotated baseline, hits");
            Assert.True(TextHit.Distance(bearing, new Vec2(bw * 0.4, 0) + new Vec2(0, -1.5)) > 0, "the same distance straight east, off the rotated text, misses");
            var corners = TextHit.Corners(bearing);
            Assert.Equal(4, corners.Length, "highlight outline has four corners");
            Assert.Near(r, Math.Atan2(corners[1].Y - corners[0].Y, corners[1].X - corners[0].X), 1e-9, "and runs along the text");
        }
            public static void TestWindowOpensInsideTheScreen()
        {
            // 1920x1080 at 150 % scaling, taskbar at the bottom: 1280 x 672 of usable work area.
            var work = new Rect(0, 0, 1280, 672);
            var desk = new Rect(0, 0, 1280, 720);
            var first = WindowFit.Place(null, work, desk);
            Assert.True(first.X1 >= 0 && first.Y1 >= 0 && first.X2 <= 1280 && first.Y2 <= 672, "first run fits the work area, taskbar clear: " + first);
            Assert.True(first.Width > 1200 && first.Height > 600, "and uses most of it");

            // Saved on a big monitor that's since been unplugged: back onto this screen.
            var gone = WindowFit.Place(new Rect(2200, 100, 3640, 1000), work, desk);
            Assert.True(gone.X2 <= 1280 && gone.Y2 <= 672, "an off-screen saved window is brought back");

            // A saved spot that's still on screen is kept.
            var kept = WindowFit.Place(new Rect(100, 50, 900, 600), work, desk);
            Assert.Near(100, kept.X1, 1e-9, "kept left"); Assert.Near(50, kept.Y1, 1e-9, "kept top"); Assert.Near(800, kept.Width, 1e-9, "kept width");

            // Saved taller than this screen: trimmed so the bottom is reachable.
            var tall = WindowFit.Place(new Rect(0, 0, 1200, 900), work, desk);
            Assert.True(tall.Y1 >= 0 && tall.Y2 <= 672, "a too-tall saved window is trimmed above the taskbar: " + tall);
            var low = WindowFit.Place(new Rect(100, 300, 900, 900), work, desk);
            Assert.True(low.Y2 <= 672 && Math.Abs(low.Height - 600) < 1e-9, "one hanging below the taskbar is moved up, not shrunk: " + low);
        }
            // ---- clicking a point's entities finds the point (v0.4.27) ---------------------------

        public static void TestPointLinksSurviveDwgAndFallBackToPosition()
        {
            var points = new List<SurveyPoint>
            {
                new SurveyPoint { Id = 103, Easting = 306493.050, Northing = 4894691.035, Elevation = 97.481, Code = "MH" },
                new SurveyPoint { Id = 106, Easting = 306493.053, Northing = 4894691.055, Elevation = 97.495, Code = "MH" },
            };
            var doc = new ACadSharp.CadDocument();
            // Point 106's elevation label, dragged well away from its point, tagged the way Draft tags it.
            var elev = new ACadSharp.Entities.TextEntity { Value = "97.50", InsertPoint = new CSMath.XYZ(306480, 4894700, 0), AlignmentPoint = new CSMath.XYZ(306480, 4894700, 0), Height = 0.3, VerticalAlignment = ACadSharp.Entities.TextVerticalAlignmentType.Top };
            doc.ModelSpace.Entities.Add(elev);
            PointLinks.Tag(elev, 106);
            Assert.Equal(106, PointLinks.Find(elev, points)?.Id, "tagged label finds its point even after being dragged away");
            PointLinks.Tag(elev, 106); // re-tagging doesn't stack records
            Assert.Equal(106, PointLinks.Tagged(elev), "re-tag is idempotent");

            string path = Path.Combine(Path.GetTempPath(), "fdd-pointlink-test.dwg");
            ACadSharp.IO.DwgWriter.Write(path, doc);
            var back = ACadSharp.IO.DwgReader.Read(path).ModelSpace.Entities.OfType<ACadSharp.Entities.TextEntity>().Single();
            Assert.Equal(106, PointLinks.Tagged(back), "the tag is saved in the DWG");

            // Untagged (an older drawing): by position. Points 103 and 106 are 2 cm apart - a node
            // exactly on 106 must pick 106, not the nearer-in-the-list 103.
            var node = new ACadSharp.Entities.Point(new CSMath.XYZ(306493.053, 4894691.055, 97.495));
            Assert.Equal(106, PointLinks.Find(node, points)?.Id, "untagged node on a point");
            var number = new ACadSharp.Entities.TextEntity { Value = "103", InsertPoint = new CSMath.XYZ(306493.6, 4894691.2, 0), Height = 0.3 };
            Assert.Equal(103, PointLinks.Find(number, points)?.Id, "untagged point number names its point, even with another point closer");
            var far = new ACadSharp.Entities.TextEntity { Value = "NOTE", InsertPoint = new CSMath.XYZ(306400, 4894600, 0), Height = 0.3 };
            Assert.True(PointLinks.Find(far, points) == null, "unrelated text belongs to no point");
        }
            // ---- plotting: .ctb plot styles and page layout (v0.4.28) -------------------------------

        private static string CtbText()
        {
            // The shape of a real .ctb: header values, plot_style entries (entry n = colour n+1),
            // and the lineweight table the entries index into.
            var sb = new System.Text.StringBuilder();
            sb.Append("description=\"FD test pens\n");
            sb.Append("aci_table_available=TRUE\nscale_factor=1.0\napply_factor=FALSE\ncustom_lineweight_display_units=0\n");
            sb.Append("aci_table{\n 0=\"Color_1\n}\n");
            sb.Append("plot_style{\n");
            for (int n = 0; n < 255; n++)
            {
                // Colour 1 (red): black, 0.50 mm. Colour 2: object colour, 50 % screen, object lineweight.
                // Colour 8: grayscale. Everything else: black, object lineweight.
                string color = n == 1 || n == 7 ? "-1" : "-16777216";
                int lw = n == 0 ? 13 : 0;
                int policy = n == 7 ? 2 : 5;
                int screen = n == 1 ? 50 : 100;
                sb.Append(" " + n + "{\n  name=\"Color_" + (n + 1) + "\n  localized_name=\"Color_" + (n + 1) + "\n  description=\"\n");
                sb.Append("  color=" + color + "\n  mode_color=" + color + "\n  color_policy=" + policy + "\n  physical_pen_number=0\n  virtual_pen_number=0\n");
                sb.Append("  screen=" + screen + "\n  linepattern_size=0.5\n  linetype=31\n  adaptive_linetype=TRUE\n  lineweight=" + lw + "\n");
                sb.Append("  fill_style=73\n  end_style=4\n  join_style=5\n }\n");
            }
            sb.Append("}\ncustom_lineweight_table{\n");
            for (int i = 0; i < PlotStyleTable.StandardLineweights.Length; i++)
                sb.Append(" " + i + "=" + PlotStyleTable.StandardLineweights[i].ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        public static void TestCtbFileIsReadIntoPens()
        {
            string path = Path.Combine(Path.GetTempPath(), "fdd-test.ctb");
            File.WriteAllBytes(path, PlotStyleTable.Compress(CtbText()));
            Assert.True(PlotStyleTable.Decompress(File.ReadAllBytes(path)).StartsWith("description="), "compressed format round-trips");
            var t = PlotStyleTable.Load(path);
            Assert.Equal("FD test pens", t.Description, "description");
            Assert.Equal(0x000000u, t.Pen(1).Color, "colour 1 plots black");
            Assert.Near(0.50, t.Pen(1).LineWeightMm ?? -1, 1e-9, "colour 1 plots 0.50 mm (lineweight index 13)");
            Assert.True(t.Pen(2).Color == null, "colour 2 keeps the object's colour");
            Assert.Equal(50, t.Pen(2).Screen, "colour 2 screened to 50 %");
            Assert.True(t.Pen(2).LineWeightMm == null, "colour 2 keeps the object's lineweight");
            Assert.True(t.Pen(8).Grayscale, "colour 8 plots grey");
            Assert.Equal(0x000000u, t.Pen(200).Color, "colour 200 black");
            Assert.True(t.Pen(-1).Color == null, "a true colour plots as itself");

            // Screening yellow (255,255,0) to 50 % -> (255,255,128); grey of pure red.
            Assert.Equal(0xFFFF80u, PlotStyleTable.PlotColor(t.Pen(2), 0xFFFF00), "screening fades toward white");
            Assert.Equal(0x4C4C4Cu, PlotStyleTable.PlotColor(new PlotPen { Grayscale = true }, 0xFF0000), "grayscale of red");
            Assert.Equal(0x000000u, PlotStyleTable.PlotColor(PlotStyleTable.Monochrome().Pen(4), 0x00FFFF), "built-in monochrome plots cyan black");
            bool threw = false;
            try { PlotStyleTable.Decompress(new byte[100]); } catch (InvalidDataException) { threw = true; }
            Assert.True(threw, "a non-ctb file is refused cleanly");
        }

        public static void TestPlotComposerPlacesScalesAndPens()
        {
            var doc = new ACadSharp.CadDocument();
            var red = new ACadSharp.Tables.Layer("PLAN-SubjectBoundary") { Color = new ACadSharp.Color(1), LineWeight = ACadSharp.LineWeightType.W35 };
            doc.Layers.Add(red);
            // A 100 m x 50 m boundary in model space on a red, 0.35 mm layer.
            var pl = new ACadSharp.Entities.LwPolyline { IsClosed = true, Layer = red };
            foreach (var (x, y) in new[] { (0.0, 0.0), (100.0, 0.0), (100.0, 50.0), (0.0, 50.0) })
                pl.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(x, y)));
            doc.ModelSpace.Entities.Add(pl);
            var scene = new SceneBuilder(doc).Model();
            var prim = scene.AllPrims().Single();
            Assert.Equal((short)1, prim.Aci, "colour number resolved through ByLayer");
            Assert.Near(0.35, prim.LineWeightMm, 1e-9, "lineweight resolved through ByLayer");
            Assert.Equal(0xFF0000u, prim.PlotRgb, "true red for plotting");

            // 1:500 (2 mm per metre) on 11x17 landscape, centred, no plot styles.
            var setup = new PlotSetup { PaperWidthMm = 279.4, PaperHeightMm = 431.8, Landscape = true, Area = PlotArea.Extents, MmPerUnit = 2, Center = true };
            var page = PlotComposer.Compose(scene, setup);
            Assert.Near(431.8, page.WidthMm, 1e-9, "landscape: the long side across");
            Assert.Near(200, page.Placed.Width, 1e-9, "100 m at 1:500 is 200 mm");
            Assert.Near((431.8 - 200) / 2, page.Placed.X1, 1e-9, "centred across");
            var line = page.Page.AllPrims().Single();
            Assert.Near(0.35, line.PenMm, 1e-9, "object lineweight plots");
            Assert.Equal(0xFF0000u, line.Rgb, "object colour plots with no table");

            // With the monochrome table and lineweights off: black hairline.
            setup.Styles = PlotStyleTable.Monochrome(); setup.PlotLineweights = false;
            line = PlotComposer.Compose(scene, setup).Page.AllPrims().Single();
            Assert.Equal(0x000000u, line.Rgb, "monochrome plots it black");
            Assert.Near(0, line.PenMm, 1e-9, "lineweights off = thinnest pen");

            // Fit to Letter portrait: 100 x 50 m into 215.9 mm wide.
            var fit = PlotComposer.Compose(scene, new PlotSetup { PaperWidthMm = 215.9, PaperHeightMm = 279.4, Landscape = false, Area = PlotArea.Extents, FitToPaper = true, Center = true });
            Assert.Near(2.159, fit.MmPerUnit, 1e-9, "fit scale is set by the width");
            Assert.True(fit.Notes.Count == 0, "fits, no warning");
            // Too big: 1:100 won't fit on Letter - warned.
            Assert.True(PlotComposer.Compose(scene, new PlotSetup { Area = PlotArea.Extents, MmPerUnit = 10 }).Notes.Any(n => n.Contains("cut off")), "an oversize plot is flagged");

            // Upside down: the boundary's first corner lands at the opposite corner of the page.
            var up = PlotComposer.Compose(scene, new PlotSetup { PaperWidthMm = 279.4, PaperHeightMm = 431.8, Landscape = true, Area = PlotArea.Extents, MmPerUnit = 2, UpsideDown = true });
            var first = up.Page.AllPrims().Single().Points[0];
            Assert.Near(431.8, first.X, 1e-9, "upside down: E flipped"); Assert.Near(279.4, first.Y, 1e-9, "N flipped");

            // The PDF carries the pen width (0.35 mm = 0.992 pt).
            setup.Styles = null; setup.PlotLineweights = true;
            string pdf = Path.Combine(Path.GetTempPath(), "fdd-plot-test.pdf");
            PdfSceneWriter.WritePage(PlotComposer.Compose(scene, setup), pdf, "test");
            Assert.True(File.Exists(pdf) && new FileInfo(pdf).Length > 200, "PDF written");
        }
            public static void TestLayoutPageSetupRoundTripsThroughTheDwg()
        {
            var doc = new ACadSharp.CadDocument();
            var layout = new ACadSharp.Objects.Layout("RPLAN-22X34");
            doc.Layouts.Add(layout);
            var setup = new PlotSetup
            {
                PaperWidthMm = 863.6, PaperHeightMm = 558.8, PaperName = PaperSize.Match(558.8, 863.6)!.Name, Landscape = true,
                Area = PlotArea.Layout, MmPerUnit = 1, Center = true, PlotLineweights = true, PlotWithStyles = true,
            };
            LayoutPlotSetup.Write(layout, setup, @"C:\\Plot Styles\\FD-Mono.ctb", "HP DesignJet T650");
            string path = Path.Combine(Path.GetTempPath(), "fdd-pagesetup-test.dwg");
            ACadSharp.IO.DwgWriter.Write(path, doc);
            var back = ACadSharp.IO.DwgReader.Read(path).Layouts.First(l => l.Name == "RPLAN-22X34");
            var read = LayoutPlotSetup.Read(back, out var ctb, out var printer);
            Assert.Equal("FD-Mono.ctb", ctb, "plot style table saved by file name, as AutoCAD does");
            Assert.Equal("HP DesignJet T650", printer, "printer saved");
            Assert.True(read.Landscape && !read.UpsideDown, "landscape saved as a 90 degree turn");
            Assert.Near(558.8, Math.Min(read.PaperWidthMm, read.PaperHeightMm), 0.01, "paper size");
            Assert.Equal("ANSI D / 22x34 (22 x 34 in)", read.PaperName, "recognised as 22x34");
            Assert.True(read.Center && read.PlotLineweights && read.PlotWithStyles && !read.FitToPaper, "flags");
            Assert.Near(1, read.MmPerUnit, 1e-9, "1:1");
            Assert.True(read.Area == PlotArea.Layout, "plots the layout");

            // A model-space window at 1:500 (metres), fit off; then fit on.
            var model = new PlotSetup { Area = PlotArea.Window, Region = new Rect(10, 20, 110, 70), MmPerUnit = 2, Landscape = false };
            LayoutPlotSetup.Write(layout, model, null, LayoutPlotSetup.PdfPrinter);
            var m = LayoutPlotSetup.Read(layout, out var noCtb, out _);
            Assert.True(noCtb == null, "no table");
            Assert.Near(2, m.MmPerUnit, 1e-9, "1 mm = 0.5 m kept as a ratio");
            Assert.True(m.Area == PlotArea.Window && m.Region.HasValue && Math.Abs(m.Region.Value.X2 - 110) < 1e-9, "window kept");
            Assert.True(!m.Landscape, "portrait");
            model.FitToPaper = true;
            LayoutPlotSetup.Write(layout, model, null, LayoutPlotSetup.PdfPrinter);
            Assert.True(LayoutPlotSetup.Read(layout, out _, out _).FitToPaper, "fit kept");
        }
            // ---- tool palette (v0.4.29) ------------------------------------------------------------

        public static void TestToolPaletteFileParses()
        {
            var p = ToolPalette.Parse(ToolPalette.DefaultText.Split('\n'));
            Assert.True(p.Tabs.Select(t => t.Name).SequenceEqual(new[] { "Text Styles", "Useful Tools", "Line Styles" }), "three tabs, in file order");
            var part = p.Tabs[0].Tools.Single(t => t.Label == "PART NUMBER");
            Assert.True(part.Kind == PaletteToolKind.Text && part.Layer == "PLAN-Other-Label" && part.Style == "L100-2.5MM", "text preset: layer and style");
            Assert.Near(2.5, part.HeightMm, 1e-9, "text preset: paper height");
            Assert.Equal(14, p.Tabs[0].Tools.Count, "all fourteen text styles, in order");
            Assert.Equal("NO PLOT", p.Tabs[0].Tools.Last().Label, "last text style");
            var boundary = p.Tabs[2].Tools[0];
            Assert.True(boundary.Kind == PaletteToolKind.Line && boundary.Layer == "PLAN-SubjectBoundary", "line preset");
            var area = p.Tabs[1].Tools.Single(t => t.Label == "Area");
            Assert.True(area.Kind == PaletteToolKind.Command && area.Command == "AREA" && area.Tip.Length > 0, "command tool with a tip");

            var custom = ToolPalette.Parse(new[] { "[Mine]", "Rescale 250 = VPSCALE 1:250", "Bad =", "EASEMENT = text | layer=PLAN-Easement | height=oops" });
            Assert.Equal("VPSCALE 1:250", custom.Tabs[0].Tools[0].Command, "a command with an argument");
            Assert.Equal(2, custom.Tabs[0].Tools.Count, "an empty entry is skipped");
            Assert.Near(0, custom.Tabs[0].Tools[1].HeightMm, 1e-12, "a bad height is ignored, not a crash");
        }
            public static void TestEntitiesFindTheirCode()
        {
            var codes = new List<FeatureCode>
            {
                new FeatureCode { Key = "FDS", Description = "Found standard iron bar", LayerName = "PLAN-FOUND" },
                new FeatureCode { Key = "FC", Description = "Chain link fence", LayerName = "FENCE" },
                new FeatureCode { Key = "FW", Description = "Wood fence", LayerName = "FENCE" },
                new FeatureCode { Key = "BLDG", Description = "Building", LayerName = "BUILDING" },
            };
            var points = new List<SurveyPoint> { new SurveyPoint { Id = 100, Easting = 10, Northing = 10, Code = "FDS2" } };
            var doc = new ACadSharp.CadDocument();
            var fence = new ACadSharp.Tables.Layer("FENCE"); doc.Layers.Add(fence);
            var bldg = new ACadSharp.Tables.Layer("BUILDING"); doc.Layers.Add(bldg);

            // Drafted linework carries its code, even though FC and FW share the FENCE layer.
            var wood = new ACadSharp.Entities.LwPolyline { Layer = fence };
            doc.ModelSpace.Entities.Add(wood);
            PointLinks.TagCode(wood, "FW");
            Assert.Equal("FW", PointLinks.CodeOf(wood, points, codes)?.Key, "tagged linework");

            // A point entity: its point's code, "FDS2" -> FDS by prefix. Both tags coexist.
            var node = new ACadSharp.Entities.Point(new CSMath.XYZ(10, 10, 0));
            doc.ModelSpace.Entities.Add(node);
            PointLinks.Tag(node, 100);
            PointLinks.TagCode(node, "FDS2");
            Assert.Equal(100, PointLinks.Tagged(node), "point tag kept alongside the code tag");
            Assert.Equal("FDS", PointLinks.CodeOf(node, points, codes)?.Key, "code with a suffix matches its code");

            string path = Path.Combine(Path.GetTempPath(), "fdd-codelink-test.dwg");
            ACadSharp.IO.DwgWriter.Write(path, doc);
            var back = ACadSharp.IO.DwgReader.Read(path);
            Assert.Equal("FW", PointLinks.TaggedCode(back.ModelSpace.Entities.OfType<ACadSharp.Entities.LwPolyline>().Single()), "code tag saved in the DWG");

            // Untagged: by layer, only when one code owns it.
            var line = new ACadSharp.Entities.Line { Layer = bldg };
            Assert.Equal("BLDG", PointLinks.CodeOf(line, points, codes)?.Key, "the only code on BUILDING");
            var loose = new ACadSharp.Entities.Line { Layer = fence };
            Assert.True(PointLinks.CodeOf(loose, points, codes) == null, "FENCE is shared by FC and FW - no guess");
        }
            // ---- object snap modes (v0.4.31) -------------------------------------------------------

        public static void TestObjectSnapModes()
        {
            var doc = new ACadSharp.CadDocument();
            // Two lines crossing at (5,5), and a circle radius 2 at (20,0).
            doc.ModelSpace.Entities.Add(Ln(0, 0, 10, 10));
            doc.ModelSpace.Entities.Add(Ln(0, 10, 10, 0));
            doc.ModelSpace.Entities.Add(new ACadSharp.Entities.Circle { Center = new CSMath.XYZ(20, 0, 0), Radius = 2 });
            var scene = new SceneBuilder(doc).Model();

            var x = scene.Snap(new Vec2(5.2, 4.9), 0.5, SnapModes.Intersection, null);
            Assert.True(x.HasValue && x.Value.Kind == SnapKind.Intersection, "intersection found");
            Assert.Near(5, x!.Value.Point.X, 1e-9, "at the crossing E"); Assert.Near(5, x.Value.Point.Y, 1e-9, "N");
            Assert.True(scene.Snap(new Vec2(5.2, 4.9), 0.5, SnapModes.Endpoint, null) == null, "with only Endpoint on, the crossing isn't offered");

            var q = scene.Snap(new Vec2(21.9, 0.2), 0.5, SnapModes.Quadrant, null);
            Assert.True(q.HasValue && q.Value.Kind == SnapKind.Quadrant && Math.Abs(q.Value.Point.X - 22) < 1e-9, "circle quadrant");

            // Perpendicular from (0,5) onto the first line (y = x): foot at (2.5,2.5).
            var perp = scene.Snap(new Vec2(2.6, 2.3), 0.5, SnapModes.Perpendicular, new Vec2(0, 5));
            Assert.True(perp.HasValue && perp.Value.Kind == SnapKind.Perpendicular, "perpendicular found");
            Assert.Near(2.5, perp!.Value.Point.X, 1e-9, "foot E"); Assert.Near(2.5, perp.Value.Point.Y, 1e-9, "foot N");

            var near = scene.Snap(new Vec2(7.1, 7.3), 0.5, SnapModes.Nearest, null);
            Assert.True(near.HasValue && Math.Abs(near!.Value.Point.X - near.Value.Point.Y) < 1e-9, "nearest lands on the line");

            // Nearest never beats something exact in reach.
            var mixed = scene.Snap(new Vec2(9.8, 9.9), 0.5, SnapModes.Nearest | SnapModes.Endpoint, null);
            Assert.True(mixed.HasValue && mixed.Value.Kind == SnapKind.Endpoint, "endpoint wins over nearest");
            Assert.True(scene.Snap(new Vec2(9.8, 9.9), 0.5, SnapModes.None, null) == null, "all snaps off");
        }
            public static void TestModelSpaceDrawsForABlackBackground()
        {
            var doc = new ACadSharp.CadDocument();
            var yellow = new ACadSharp.Tables.Layer("YELLOW") { Color = new ACadSharp.Color(2) };
            doc.Layers.Add(yellow);
            doc.ModelSpace.Entities.Add(new ACadSharp.Entities.Line(new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(1, 0, 0)) { Color = new ACadSharp.Color(7) });
            doc.ModelSpace.Entities.Add(new ACadSharp.Entities.Line(new CSMath.XYZ(0, 1, 0), new CSMath.XYZ(1, 1, 0)) { Layer = yellow });
            var dark = new SceneBuilder(doc) { DarkModel = true }.Model();
            Assert.True(dark.DarkBackground, "flagged for a black background");
            var prims = dark.AllPrims().ToList();
            Assert.Equal(0xFFFFFFu, prims[0].Rgb, "colour 7 draws white on black");
            Assert.Equal(0xFFFF00u, prims[1].Rgb, "yellow keeps its full brightness on black");
            Assert.Equal(0x000000u, prims[0].PlotRgb, "but still plots black on paper");
            var light = new SceneBuilder(doc).Model();
            Assert.True(!light.DarkBackground && light.AllPrims().First().Rgb == 0x000000u, "default (CLI, SVG, tests) is unchanged: black on white");
        }
            // ---- MSCAD annotate styles (v0.4.32) ---------------------------------------------------

        public static void TestCourseAnnotationStyles()
        {
            var std = FirmStandards.Default();
            double mpm = 0.5; // 1:500 in metres
            var a = new Vec2(0, 0); var b = new Vec2(40, 0);   // 40 m due east
            var abovePick = new Vec2(20, 3); var belowPick = new Vec2(20, -3);
            CourseLabelResult L(CourseLabelStyle s, Vec2 pick) => CourseAnnotation.Layout(a, b, pick, s, std, mpm);
            string dist = 40.0.ToString("F" + std.DistanceDecimals, System.Globalization.CultureInfo.InvariantCulture);

            var split = L(CourseLabelStyle.BearingOnLine, abovePick);
            Assert.Equal(1, split.Texts.Count, "one bearing");
            Assert.Near(0, split.Texts[0].Position.Y, 1e-9, "centred on the line");
            Assert.True(split.Texts[0].V == VAlign.Middle, "middle-anchored so it sits in the gap");
            Assert.True(split.GapFrom < 0.5 && split.GapTo > 0.5 && Math.Abs(split.GapFrom!.Value + split.GapTo!.Value - 1) < 1e-9, "a gap centred on the line");
            double gapM = (split.GapTo!.Value - split.GapFrom.Value) * 40;
            Assert.True(gapM > 2 && gapM < 12, "gap about the bearing's width at 1:500: " + gapM);
            Assert.Equal(dist, L(CourseLabelStyle.DistanceOnLine, abovePick).Texts[0].Text, "distance on line");

            var offAbove = L(CourseLabelStyle.BearingOffLine, abovePick).Texts[0];
            var offBelow = L(CourseLabelStyle.BearingOffLine, belowPick).Texts[0];
            Assert.True(offAbove.Position.Y > 0 && offAbove.V == VAlign.Bottom, "off line, picked above: above, sitting on its anchor");
            Assert.True(offBelow.Position.Y < 0 && offBelow.V == VAlign.Top, "picked below: below, hanging from its anchor");
            Assert.True(L(CourseLabelStyle.DistanceOffLine, belowPick).Texts[0].Position.Y < 0, "distance off line follows the pick");

            var both = L(CourseLabelStyle.BearingDistance, belowPick).Texts;
            Assert.True(both[0].Kind == TextKind.Bearing && both[0].Position.Y > 0 && both[1].Position.Y < 0, "bearing above, distance below, whatever the pick");

            var dash = L(CourseLabelStyle.BearingDashDistance, abovePick).Texts.Single();
            Assert.True(dash.Text.StartsWith("N90") && dash.Text.EndsWith(dist) && dash.Position.Y > 0, "one line: bearing then distance, on the picked side");

            var stackedAbove = L(CourseLabelStyle.BearingOverDistance, abovePick).Texts;
            var brg = stackedAbove.Single(t => t.Kind == TextKind.Bearing); var dst = stackedAbove.Single(t => t.Kind == TextKind.Distance);
            Assert.True(brg.Position.Y > dst.Position.Y && dst.Position.Y > 0, "bearing over distance, both above");
            var stackedBelow = L(CourseLabelStyle.BearingOverDistance, belowPick).Texts;
            Assert.True(stackedBelow.Single(t => t.Kind == TextKind.Bearing).Position.Y > stackedBelow.Single(t => t.Kind == TextKind.Distance).Position.Y
                && stackedBelow.All(t => t.Position.Y < 0), "still bearing over distance when both go below");
            var db = L(CourseLabelStyle.DistanceOverBearing, abovePick).Texts;
            Assert.True(db.Single(t => t.Kind == TextKind.Distance).Position.Y > db.Single(t => t.Kind == TextKind.Bearing).Position.Y, "distance over bearing");
            // The two stacked lines don't overlap: the gap between them is at least a text gap.
            double upper = brg.Position.Y, lowerTop = dst.Position.Y + std.DistanceTextMm * mpm;
            Assert.True(upper >= lowerTop - 1e-9, "stacked lines clear of each other");
        }
            public static void TestAnnotateStylesOnDrawingEntities()
        {
            var doc = new ACadSharp.CadDocument();
            var std = FirmStandards.Default();
            ACadSharp.Tables.Layer L(string n) { if (!doc.Layers.TryGetValue(n, out var l)) { l = new ACadSharp.Tables.Layer(n); doc.Layers.Add(l); } return l; }
            var undo = new UndoStack();

            // Split bearing on a Line: the line becomes two with a gap round the centred bearing.
            var line = Ln(0, 0, 40, 0);
            doc.ModelSpace.Entities.Add(line);
            undo.Push(CourseLabelling.Annotate(line, new Vec2(20, 1), CourseLabelStyle.BearingOnLine, doc, std, 0.5, L, out _)!);
            var lines = doc.ModelSpace.Entities.OfType<ACadSharp.Entities.Line>().ToList();
            Assert.Equal(2, lines.Count, "the line is broken in two");
            Assert.True(line.EndPoint.X < 20 && lines.Any(l => l != line && l.StartPoint.X > 20 && Math.Abs(l.EndPoint.X - 40) < 1e-9), "a gap either side of the middle");
            var brg = doc.ModelSpace.Entities.OfType<ACadSharp.Entities.TextEntity>().Single();
            Assert.True(brg.Value.StartsWith("N90") && Math.Abs(brg.AlignmentPoint.Y) < 1e-9 && Math.Abs(brg.AlignmentPoint.X - 20) < 1e-9, "bearing centred in the gap");
            undo.Undo();
            Assert.True(doc.ModelSpace.Entities.OfType<ACadSharp.Entities.Line>().Single() == line && Math.Abs(line.EndPoint.X - 40) < 1e-9, "one undo rejoins the line");
            Assert.True(!doc.ModelSpace.Entities.OfType<ACadSharp.Entities.TextEntity>().Any(), "and removes the label");

            // Split distance on a closed lot: picked on its east side, it opens there into one polyline.
            var lot = new ACadSharp.Entities.LwPolyline { IsClosed = true };
            foreach (var (x, y) in new[] { (0.0, 10.0), (30.0, 10.0), (30.0, 40.0), (0.0, 40.0) })
                lot.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(x, y)));
            doc.ModelSpace.Entities.Add(lot);
            PointLinks.TagCode(lot, "BDY");
            undo.Push(CourseLabelling.Annotate(lot, new Vec2(30.5, 25), CourseLabelStyle.DistanceOnLine, doc, std, 0.5, L, out _)!);
            var opened = doc.ModelSpace.Entities.OfType<ACadSharp.Entities.LwPolyline>().Single();
            Assert.True(opened != lot && !opened.IsClosed, "the closed lot is replaced by an open one");
            Assert.Equal(6, opened.Vertices.Count, "four corners plus the two gap ends");
            Assert.True(Math.Abs(opened.Vertices[0].Location.X - 30) < 1e-9 && opened.Vertices[0].Location.Y > 25, "starts just past the gap");
            Assert.True(Math.Abs(opened.Vertices[5].Location.X - 30) < 1e-9 && opened.Vertices[5].Location.Y < 25, "ends just before it");
            Assert.Equal("BDY", PointLinks.TaggedCode(opened), "keeps its code tag");
            undo.Undo();
            Assert.True(doc.ModelSpace.Entities.OfType<ACadSharp.Entities.LwPolyline>().Single() == lot, "undo brings the closed lot back");

            // Off-line style: nothing is broken, the label goes on the picked side.
            undo.Push(CourseLabelling.Annotate(lot, new Vec2(15, 8), CourseLabelStyle.BearingOverDistance, doc, std, 0.5, L, out _)!);
            var two = doc.ModelSpace.Entities.OfType<ACadSharp.Entities.TextEntity>().ToList();
            Assert.True(two.Count == 2 && two.All(t => t.AlignmentPoint.Y < 10), "bearing and distance both below the south line, where it was picked");
            Assert.True(doc.ModelSpace.Entities.OfType<ACadSharp.Entities.LwPolyline>().Single() == lot, "the lot is untouched");
        }

        // ---- Draw toolbar (ported from the v0.4.35-40 branch) ----------------------------------

        public static void TestDrawToolbarShapes()
        {
            var r = Shapes.Rectangle(new Vec2(5, 8), new Vec2(1, 2));
            Assert.True(r[0].X == 1 && r[0].Y == 2 && r[2].X == 5 && r[2].Y == 8, "rectangle from any two opposite corners");
            var hex = Shapes.RegularPolygon(6, new Vec2(0, 0), new Vec2(10, 0), true);
            Assert.True(hex.Count == 6 && hex.All(v => Math.Abs(v.Length - 10) < 1e-9), "inscribed: vertices on the radius");
            Assert.Near(10, Vec2.Distance(hex[0], hex[1]), 1e-9, "a hexagon's side equals its radius");
            var sq = Shapes.RegularPolygon(4, new Vec2(0, 0), new Vec2(5, 0), false);
            Assert.Near(10, Vec2.Distance(sq[0], sq[1]), 1e-9, "circumscribed: the pick is a side's midpoint");
            var tri = Shapes.PolygonOnEdge(3, new Vec2(0, 0), new Vec2(10, 0));
            Assert.True(tri[2].Y > 0 && Math.Abs(tri[2].X - 5) < 1e-9, "on an edge, built to its left");
            var (pts, bulges) = Shapes.RevisionCloud(Shapes.Rectangle(new Vec2(0, 0), new Vec2(10, 5)), 2.5);
            Assert.Equal(12, pts.Count, "4 + 2 + 4 + 2 arcs round a 10 x 5 box at 2.5");
            Assert.True(bulges.All(b => b > 0), "positive bulges on a counter-clockwise outline");
            var firstArc = Construct.Span.FromBulge(pts[0], pts[1], bulges[0]);
            Assert.True(firstArc.Midpoint.Y < 0, "the scallops swing outward, below the bottom edge");
            var (cwPts, cwB) = Shapes.RevisionCloud(Shapes.Rectangle(new Vec2(0, 0), new Vec2(10, 5)).AsEnumerable().Reverse().ToList(), 2.5);
            Assert.True(Construct.Span.FromBulge(cwPts[0], cwPts[1], cwB[0]).Midpoint.X > 10 || Construct.Span.FromBulge(cwPts[0], cwPts[1], cwB[0]).Midpoint.Y > 5, "outward on a clockwise outline too");
            var c3 = Shapes.CircleThreePoints(new Vec2(1, 0), new Vec2(0, 1), new Vec2(-1, 0))!.Value;
            Assert.True(c3.Center.Length < 1e-9 && Math.Abs(c3.Radius - 1) < 1e-9, "circle through three points");
            Assert.True(Shapes.CircleThreePoints(new Vec2(0, 0), new Vec2(1, 1), new Vec2(2, 2)) == null, "none through three in line");
        }

        public static void TestDonutWidthsAreFilledInTheViewer()
        {
            var doc = new ACadSharp.CadDocument();
            var pl = new ACadSharp.Entities.LwPolyline { IsClosed = true, ConstantWidth = 0.25 };
            pl.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(-0.375, 0)) { Bulge = 1 });
            pl.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(0.375, 0)) { Bulge = 1 });
            doc.ModelSpace.Entities.Add(pl);
            var fills = new SceneBuilder(doc).Model().AllPrims().Where(p => p.Kind == PrimKind.Fill).ToList();
            Assert.Equal(2, fills.Count, "each half of the donut is a filled band");
            double maxR = fills.SelectMany(f => f.Points).Max(p => p.Length), minR = fills.SelectMany(f => f.Points).Min(p => p.Length);
            Assert.Near(0.5, maxR, 1e-6, "outside diameter 1");
            Assert.Near(0.25, minR, 1e-6, "inside diameter 0.5");
        }

        public static void TestSplineThroughPicks()
        {
            var pts = new List<Vec2> { new Vec2(0, 0), new Vec2(10, 5), new Vec2(20, 0), new Vec2(30, 6) };
            foreach (bool closed in new[] { false, true })
            {
                var sp = SplineEditing.Through(pts, closed);
                Assert.True(sp.TryPolygonalVertexes(200, out var v) && v.Count > 10, "it tessellates (closed=" + closed + ")");
                foreach (var p in pts)
                    Assert.True(v.Min(q => Vec2.Distance(p, new Vec2(q.X, q.Y))) < 0.2, "passes through " + p + " (closed=" + closed + ")");
                var doc = new ACadSharp.CadDocument();
                doc.ModelSpace.Entities.Add(sp);
                var prims = new SceneBuilder(doc).Model().AllPrims().ToList();
                Assert.True(prims.Count == 1 && prims[0].Points.Count > 10, "the viewer draws it");
            }
        }

        public static void TestNestedIslandsDontDependOnDrawingOrder()
        {
            ACadSharp.Entities.LwPolyline Sq(double a, double b)
            {
                var p = new ACadSharp.Entities.LwPolyline { IsClosed = true };
                foreach (var (x, y) in new[] { (a, a), (b, a), (b, b), (a, b) }) p.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(x, y)));
                return p;
            }
            var outer = Sq(0, 100); var mid = Sq(20, 80); var inner = Sq(40, 60);
            foreach (var order in new[] { new[] { outer, mid, inner }, new[] { inner, mid, outer } })
            {
                var b = HatchEditing.BoundaryAt(new Vec2(10, 10), order)!;
                Assert.True(b.Count == 2 && b[1].Source == mid, "the middle square is the island; the one inside it isn't");
            }
        }

        public static void TestHatchBoundaryIslandsAndRendering()
        {
            var doc = new ACadSharp.CadDocument();
            var lot = new ACadSharp.Entities.LwPolyline { IsClosed = true };
            foreach (var (x, y) in new[] { (0.0, 0.0), (30.0, 0.0), (30.0, 40.0), (0.0, 40.0) }) lot.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(x, y)));
            var house = new ACadSharp.Entities.LwPolyline { IsClosed = true };
            foreach (var (x, y) in new[] { (10.0, 10.0), (20.0, 10.0), (20.0, 20.0), (10.0, 20.0) }) house.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(x, y)));
            var well = new ACadSharp.Entities.Circle { Center = new CSMath.XYZ(5, 35, 0), Radius = 1 };
            foreach (var e in new ACadSharp.Entities.Entity[] { lot, house, well }) doc.ModelSpace.Entities.Add(e);

            var inHouse = HatchEditing.BoundaryAt(new Vec2(15, 15), doc.ModelSpace.Entities)!;
            Assert.True(inHouse.Count == 1 && inHouse[0].Source == house, "inside the house: just the house");
            var inYard = HatchEditing.BoundaryAt(new Vec2(25, 30), doc.ModelSpace.Entities)!;
            Assert.True(inYard.Count == 3 && inYard[0].Source == lot, "the yard: the lot, with the house and the well as islands");
            Assert.True(HatchEditing.BoundaryAt(new Vec2(50, 50), doc.ModelSpace.Entities) == null, "outside everything: nothing");

            var lines = HatchEditing.Create(inHouse, 1.0, Math.PI / 4, false, new ACadSharp.Tables.Layer("HATCH"));
            doc.ModelSpace.Entities.Add(lines);
            var path = Path.Combine(Path.GetTempPath(), "fdd-hatch-" + Guid.NewGuid().ToString("N") + ".dwg");
            try
            {
                ACadSharp.IO.DwgWriter.Write(path, doc);
                var back = ACadSharp.IO.DwgReader.Read(path);
                var hb = back.ModelSpace.Entities.OfType<ACadSharp.Entities.Hatch>().Single();
                Assert.True(!hb.IsSolid && hb.Paths.Count == 1 && hb.Pattern.Lines.Count == 1, "the HATCH command's hatch survives a DWG round trip");
                Assert.Near(1.0, Math.Abs(hb.Pattern.Lines[0].LineOffset), 1e-6, "with its spacing");
                Assert.True(new SceneBuilder(back).Model().AllPrims().Count(p => p.Handle == hb.Handle) > 5, "and the viewer draws its lines");
            }
            finally { File.Delete(path); }
        }

        // ---- extra object snaps (ported) ---------------------------------------------------

        public static void TestInsertionAndTangentSnaps()
        {
            var doc = new ACadSharp.CadDocument();
            doc.ModelSpace.Entities.Add(new ACadSharp.Entities.TextEntity { Value = "LOT 1", InsertPoint = new CSMath.XYZ(50, 50, 0), Height = 1 });
            doc.ModelSpace.Entities.Add(new ACadSharp.Entities.Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 5 });
            doc.ModelSpace.Entities.Add(new ACadSharp.Entities.Arc { Center = new CSMath.XYZ(100, 0, 0), Radius = 5, StartAngle = 0, EndAngle = Math.PI });
            var scene = new SceneBuilder(doc).Model();
            var ins = scene.Snap(new Vec2(50.3, 50.2), 1, SnapModes.Insertion, null);
            Assert.True(ins.HasValue && ins.Value.Kind == SnapKind.Insertion && Vec2.Distance(ins.Value.Point, new Vec2(50, 50)) < 1e-9, "text insertion point");
            Assert.True(!scene.Snap(new Vec2(50.3, 50.2), 1, SnapModes.Endpoint, null).HasValue, "only when Ins is on");
            // From (0, 10) the tangents to a radius-5 circle at the origin touch at 30° above horizontal.
            var tan = scene.Snap(new Vec2(4.3, 2.5), 1, SnapModes.Tangent, new Vec2(0, 10));
            Assert.True(tan.HasValue && tan.Value.Kind == SnapKind.Tangent, "a tangent point on the circle");
            Assert.Near(4.330, tan.Value.Point.X, 0.001, "exact on a circle (x)");
            Assert.Near(2.5, tan.Value.Point.Y, 0.001, "exact on a circle (y)");
            var arcTan = scene.Snap(new Vec2(104.3, 2.5), 1, SnapModes.Tangent, new Vec2(100, 10));
            Assert.True(arcTan.HasValue && Vec2.Distance(arcTan.Value.Point, new Vec2(104.330, 2.5)) < 0.2, "close on an arc's chords: " + (arcTan.HasValue ? arcTan.Value.Point.ToString() : "none"));
        }

        public static void TestExtensionSnap()
        {
            var doc = new ACadSharp.CadDocument();
            doc.ModelSpace.Entities.Add(Ln(0, 0, 10, 0));
            var scene = new SceneBuilder(doc).Model();
            var s = scene.Snap(new Vec2(13, 0.3), 1, SnapModes.Extension, null);
            Assert.True(s.HasValue && s.Value.Kind == SnapKind.Extension && Math.Abs(s.Value.Point.X - 13) < 1e-9 && Math.Abs(s.Value.Point.Y) < 1e-9, "on the line carried past its end");
            Assert.True(!scene.Snap(new Vec2(5, 0.3), 1, SnapModes.Extension, null).HasValue, "not on the line itself");
            Assert.True(!scene.Snap(new Vec2(13, 3), 1, SnapModes.Extension, null).HasValue, "not when off the extension");
        }

        public static void TestExtensionNeverBeatsTheEndpoint()
        {
            var doc = new ACadSharp.CadDocument();
            doc.ModelSpace.Entities.Add(Ln(0, 0, 10, 0));
            var s = new SceneBuilder(doc).Model().Snap(new Vec2(10.2, 0.05), 1, SnapModes.Endpoint | SnapModes.Extension, null);
            Assert.True(s.HasValue && s.Value.Kind == SnapKind.Endpoint, "just past the end, the end itself wins");
        }

        // ---- labels follow their course (ported) -------------------------------------------

        public static void TestLabelsFollowTheirCourse()
        {
            var doc = new ACadSharp.CadDocument();
            var std = FirmStandards.Default();
            ACadSharp.Tables.Layer L(string n) { if (!doc.Layers.TryGetValue(n, out var l)) { l = new ACadSharp.Tables.Layer(n); doc.Layers.Add(l); } return l; }
            var undo = new UndoStack();
            var lot = new ACadSharp.Entities.LwPolyline();
            foreach (var (x, y) in new[] { (0.0, 0.0), (40.0, 0.0), (40.0, 30.0) }) lot.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(x, y)));
            doc.ModelSpace.Entities.Add(lot);
            var made = CourseLabelling.ForLinked(lot, doc, std, 0.5, L);
            undo.Push(new AddEntitiesCommand(doc.ModelSpace, made.Select(m => m.Label), "Label"));
            foreach (var (t, span, kind) in made) CourseLinks.Tag(t, lot, span, kind);
            var dist0 = (ACadSharp.Entities.TextEntity)made.Single(m => m.Span == 0 && m.Kind == CourseLinks.Kinds.Distance).Label;
            var brg1 = (ACadSharp.Entities.TextEntity)made.Single(m => m.Span == 1 && m.Kind == CourseLinks.Kinds.Bearing).Label;
            Assert.Equal("40.00", dist0.Value, "first course 40 m");
            var link = CourseLinks.Read(dist0)!.Value;
            Assert.True(link.Course == lot.Handle && link.Span == 0, "linked to its span");

            // Stretch the shared corner from (40,0) to (50,0): both courses change.
            var verts = VertexEditing.FindCoincident(new[] { lot }, new CSMath.XYZ(40, 0, 0), 1e-6);
            undo.Push(new StretchVertexCommand(verts, new CSMath.XYZ(50, 0, 0), "Stretch"));
            var rel = CourseLinks.Relabel(doc, doc.ModelSpace.Entities, new HashSet<ulong> { lot.Handle }, std, 0.5, 1.0, out int n)!;
            undo.Push(rel);
            Assert.Equal(4, n, "all four labels rewritten");
            Assert.Equal("50.00", dist0.Value, "the distance follows");
            Assert.Near(25, dist0.AlignmentPoint.X, 1e-9, "and sits at the new middle");
            Assert.True(dist0.AlignmentPoint.Y < 0, "still below the line");
            Assert.True(brg1.Value != "N00%%d00'00\"E" && brg1.Value.StartsWith("N"), "the second course's bearing changes: " + brg1.Value);
            Assert.True(CourseLinks.Relabel(doc, doc.ModelSpace.Entities, null, std, 0.5, 1.0, out _) == null, "then everything matches");

            undo.Undo();
            Assert.Equal("40.00", dist0.Value, "undoing the relabel restores the text");
            undo.Undo();
            Assert.Near(40, lot.Vertices[1].Location.X, 1e-9, "and the stretch");

            // Dragged or flipped to the other side, a label stays on that side.
            dist0.AlignmentPoint = new CSMath.XYZ(dist0.AlignmentPoint.X, 1.5, 0); dist0.InsertPoint = dist0.AlignmentPoint;
            undo.Push(new StretchVertexCommand(VertexEditing.FindCoincident(new[] { lot }, new CSMath.XYZ(0, 0, 0), 1e-6), new CSMath.XYZ(-20, 0, 0), "Stretch"));
            undo.Push(CourseLinks.Relabel(doc, doc.ModelSpace.Entities, new HashSet<ulong> { lot.Handle }, std, 0.5, 1.0, out _)!);
            Assert.True(dist0.Value == "60.00" && Math.Abs(dist0.AlignmentPoint.Y - 1.5) < 1e-9 && Math.Abs(dist0.AlignmentPoint.X - 10) < 1e-9, "60 m, still above, at the middle");

            // A vertex added on the other course renumbers nothing it labels wrongly.
            undo.Push(PolylineVertices.Insert(lot, 1, new Vec2(40, 15), "Insert", out _));
            var relink = CourseLinks.Relabel(doc, doc.ModelSpace.Entities, null, std, 0.5, 1.0, out _);
            Assert.Equal("60.00", dist0.Value, "the first course's label is untouched");
            Assert.True(CourseLinks.Read(dist0)!.Value.Span == 0, "and keeps its span");
        }

        public static void TestLinkedLabelSurvivesDwg()
        {
            var doc = new ACadSharp.CadDocument();
            var std = FirmStandards.Default();
            ACadSharp.Tables.Layer L(string n) { if (!doc.Layers.TryGetValue(n, out var l)) { l = new ACadSharp.Tables.Layer(n); doc.Layers.Add(l); } return l; }
            var line = Ln(0, 0, 0, 25);
            doc.ModelSpace.Entities.Add(line);
            var undo = new UndoStack();
            undo.Push(CourseLabelling.Annotate(line, new Vec2(1, 12), CourseLabelStyle.BearingDashDistance, doc, std, 0.5, L, out _)!);
            var t = doc.ModelSpace.Entities.OfType<ACadSharp.Entities.TextEntity>().Single();
            Assert.Equal(CourseLinks.Kinds.BearingDistance, CourseLinks.Read(t)!.Value.Kind, "annotate tools link their labels");
            var path = Path.Combine(Path.GetTempPath(), "fdd-links-" + Guid.NewGuid().ToString("N") + ".dwg");
            try
            {
                ACadSharp.IO.DwgWriter.Write(path, doc);
                var back = ACadSharp.IO.DwgReader.Read(path);
                var bt = back.ModelSpace.Entities.OfType<ACadSharp.Entities.TextEntity>().Single();
                var bl = back.ModelSpace.Entities.OfType<ACadSharp.Entities.Line>().Single();
                var link = CourseLinks.Read(bt);
                Assert.True(link.HasValue && link.Value.Course == bl.Handle, "the link survives the DWG, pointing at the same line");
                bl.EndPoint = new CSMath.XYZ(0, 30, 0);
                CourseLinks.Relabel(back, back.ModelSpace.Entities, null, std, 0.5, 1.0, out int n);
                Assert.True(n == 1 && bt.Value.EndsWith("30.00"), "and relabels after reopening: " + bt.Value);
            }
            finally { File.Delete(path); }
        }

        public static void TestDraftedCourseLabelsKnowTheirCourse()
        {
            var result = DraftPipeline.Run(FdJobReader.Read(Demo), ProVision);
            var doc = result.Document;
            var labels = doc.Entities.OfType<DraftText>().Where(t => t.Kind == TextKind.Bearing || t.Kind == TextKind.Distance || t.Kind == TextKind.ArcData).ToList();
            Assert.True(labels.Count > 0 && labels.All(t => t.CourseKind.Length > 0), "every course label says what it is");
            var spans = doc.Entities.OfType<DraftPolyline>().SelectMany(p => Construct.Spans(p.Vertices, p.Bulges, p.Closed)).ToList();
            int found = labels.Count(t => spans.Any(sp => Vec2.Distance(sp.A, t.CourseA) < 1e-6 && Vec2.Distance(sp.B, t.CourseB) < 1e-6 || Vec2.Distance(sp.A, t.CourseB) < 1e-6 && Vec2.Distance(sp.B, t.CourseA) < 1e-6));
            Assert.Equal(labels.Count, found, "and its course is a span of the drawn linework, so the drafter can link it");
        }

        public static void TestLinksSurviveEraseUndoJoinAndRebuilds()
        {
            var doc = new ACadSharp.CadDocument();
            var std = FirmStandards.Default();
            ACadSharp.Tables.Layer L(string n) { if (!doc.Layers.TryGetValue(n, out var l)) { l = new ACadSharp.Tables.Layer(n); doc.Layers.Add(l); } return l; }
            var undo = new UndoStack();
            var line = Ln(0, 0, 25, 0);
            doc.ModelSpace.Entities.Add(line);
            var made = CourseLabelling.ForLinked(line, doc, std, 0.5, L);
            undo.Push(new AddEntitiesCommand(doc.ModelSpace, made.Select(m => m.Label), "Label"));
            foreach (var (t, span, kind) in made) CourseLinks.Tag(t, line, span, kind);
            var dist = (ACadSharp.Entities.TextEntity)made[1].Label;
            ulong h = line.Handle;

            undo.Push(new RemoveEntitiesCommand(new ACadSharp.Entities.Entity[] { line }, "Erase"));
            undo.Undo();
            Assert.Equal(h, line.Handle, "erase then undo keeps the line's handle");
            line.EndPoint = new CSMath.XYZ(30, 0, 0);
            CourseLinks.Relabel(doc, doc.ModelSpace.Entities, null, std, 0.5, 1.0, out int n);
            Assert.True(n == 2 && dist.Value == "30.00", "so its labels still follow it");

            // JOIN: the line and a second one become a polyline; the label moves its link to it.
            var second = Ln(30, 0, 30, 20);
            doc.ModelSpace.Entities.Add(second);
            var join = EntityOps.Join(new ACadSharp.Entities.Entity[] { line, second }, 0.001, out var polys)!;
            var re = CourseLinks.Rehome(doc.ModelSpace.Entities.OfType<ACadSharp.Entities.TextEntity>().ToList(), new Dictionary<ulong, IList<ACadSharp.Entities.Entity>> { [h] = polys.Cast<ACadSharp.Entities.Entity>().ToList(), [second.Handle] = polys.Cast<ACadSharp.Entities.Entity>().ToList() })!;
            Assert.Equal(polys[0].Handle, CourseLinks.Read(dist)!.Value.Course, "after JOIN the label is linked to the polyline");
            var verts = VertexEditing.FindCoincident(polys, new CSMath.XYZ(0, 0, 0), 1e-6);
            new StretchVertexCommand(verts, new CSMath.XYZ(-5, 0, 0), "Stretch");
            CourseLinks.Relabel(doc, doc.ModelSpace.Entities, null, std, 0.5, 1.0, out _);
            Assert.Equal("35.00", dist.Value, "and follows the polyline from then on");
        }

        public static void TestPolyline2DVertexEditKeepsLabelsLinked()
        {
            var doc = new ACadSharp.CadDocument();
            var std = FirmStandards.Default();
            ACadSharp.Tables.Layer L(string n) { if (!doc.Layers.TryGetValue(n, out var l)) { l = new ACadSharp.Tables.Layer(n); doc.Layers.Add(l); } return l; }
            var p2 = new ACadSharp.Entities.Polyline2D(new[] { new ACadSharp.Entities.Vertex2D(new CSMath.XYZ(0, 0, 0)), new ACadSharp.Entities.Vertex2D(new CSMath.XYZ(20, 0, 0)), new ACadSharp.Entities.Vertex2D(new CSMath.XYZ(20, 10, 0)) }, false);
            doc.ModelSpace.Entities.Add(p2);
            var made = CourseLabelling.ForLinked(p2, doc, std, 0.5, L);
            new AddEntitiesCommand(doc.ModelSpace, made.Select(m => m.Label), "Label");
            foreach (var (t, span, kind) in made) CourseLinks.Tag(t, p2, span, kind);
            var cmd = (ReplacePolyline2DCommand)PolylineVertices.Insert(p2, 1, new Vec2(20, 5), "Insert", out _);
            ulong oldHandle = HandleKeeper.HandleOf(p2);
            Assert.True(oldHandle != 0 && p2.Handle == 0, "the old polyline is out, its handle remembered");
            CourseLinks.Rehome(doc.ModelSpace.Entities.OfType<ACadSharp.Entities.TextEntity>().ToList(), new Dictionary<ulong, IList<ACadSharp.Entities.Entity>> { [oldHandle] = new List<ACadSharp.Entities.Entity> { cmd.Replacement } });
            var first = (ACadSharp.Entities.TextEntity)made.First(m => m.Span == 0 && m.Kind == CourseLinks.Kinds.Distance).Label;
            Assert.Equal(cmd.Replacement.Handle, CourseLinks.Read(first)!.Value.Course, "relinked to the rebuilt polyline");
            var vs = VertexEditing.FindCoincident(new[] { cmd.Replacement }, new CSMath.XYZ(0, 0, 0), 1e-6);
            new StretchVertexCommand(vs, new CSMath.XYZ(-10, 0, 0), "Stretch");
            CourseLinks.Relabel(doc, doc.ModelSpace.Entities, null, std, 0.5, 1.0, out _);
            Assert.Equal("30.00", first.Value, "and follows it from then on");
        }

        public static void TestArcLabelsFollowTheCurve()
        {
            var doc = new ACadSharp.CadDocument();
            var std = FirmStandards.Default();
            ACadSharp.Tables.Layer L(string n) { if (!doc.Layers.TryGetValue(n, out var l)) { l = new ACadSharp.Tables.Layer(n); doc.Layers.Add(l); } return l; }
            var arc = new ACadSharp.Entities.Arc { Center = new CSMath.XYZ(0, 0, 0), Radius = 10, StartAngle = 0, EndAngle = Math.PI / 2 };
            doc.ModelSpace.Entities.Add(arc);
            var made = CourseLabelling.ForLinked(arc, doc, std, 0.5, L);
            new AddEntitiesCommand(doc.ModelSpace, made.Select(m => m.Label), "Label");
            foreach (var (t, span, kind) in made) CourseLinks.Tag(t, arc, span, kind);
            var outer = (ACadSharp.Entities.TextEntity)made[0].Label;
            double off0 = new Vec2(outer.AlignmentPoint.X, outer.AlignmentPoint.Y).Length - 10;
            arc.Radius = 20;
            CourseLinks.Relabel(doc, doc.ModelSpace.Entities, null, std, 0.5, 1.0, out int n);
            Assert.Equal(2, n, "both curve labels rewritten");
            Assert.True(outer.Value.StartsWith("R=20"), "with the new radius: " + outer.Value);
            Assert.Near(off0, new Vec2(outer.AlignmentPoint.X, outer.AlignmentPoint.Y).Length - 20, 1e-6, "the same gap off the curve");
            Assert.Near(Math.PI / 4, Math.Atan2(outer.AlignmentPoint.Y, outer.AlignmentPoint.X), 0.02, "at the same place round it");
        }

        // ---- points read from an opened drawing (v0.6.9) ----------------------------------------

        public static void TestReadsMscadPointsFromADrawing()
        {
            var doc = new ACadSharp.CadDocument();
            var layer = new ACadSharp.Tables.Layer("MSPOINT-MONUMENT");
            doc.Layers.Add(layer);
            void Mscad(double e, double n, double z, string desc, string num)
            {
                var pt = new ACadSharp.Entities.Point(new CSMath.XYZ(e, n, z)) { Layer = layer };
                doc.ModelSpace.Entities.Add(pt);
                var xd = new ACadSharp.XData.ExtendedData();
                xd.Records.Add(new ACadSharp.XData.ExtendedDataString(desc));
                xd.Records.Add(new ACadSharp.XData.ExtendedDataString(num));
                xd.Records.Add(new ACadSharp.XData.ExtendedDataReal(z));
                pt.ExtendedData.Add(DrawingPoints.MscadApp, xd);
            }
            Mscad(309689.861, 4869234.205, 90.784, "FDIB", "1");
            Mscad(309675.843, 4869229.765, 91.068, "FDSIB", "2");
            Mscad(309600.0, 4869200.0, 92.0, "BM", "CP1");
            Mscad(309610.0, 4869210.0, 92.5, "FDIB 6", "7");
            doc.ModelSpace.Entities.Add(new ACadSharp.Entities.Point(new CSMath.XYZ(0, 0, 0))); // a plain point isn't a survey point
            var job = DrawingPoints.Read(doc, @"C:\jobs\17 Empire.dwg", out string source)!;
            Assert.Equal(4, job.Points.Count, "the four MSCAD points");
            Assert.True(source.Contains("MicroSurvey"), "says where they came from");
            var p1 = job.Point(1)!;
            Assert.True(Math.Abs(p1.Northing - 4869234.205) < 1e-6 && Math.Abs(p1.Easting - 309689.861) < 1e-6 && Math.Abs(p1.Elevation - 90.784) < 1e-6 && p1.Code == "FDIB", "N, E, Z and code");
            var cp = job.Points.Single(p => p.Name == "CP1");
            Assert.True(cp.Id > 900000, "a named point keeps its name");
            Assert.True(job.Codes.Count == 3 && job.Codes.All(c => c.LayerName == "MSPOINT-MONUMENT"), "a code per description's first word, with its layer");
            Assert.Equal("FDIB 6", job.Point(7)!.Code, "the point keeps its whole description");
            Assert.Equal("17 Empire", job.Settings.Name, "named after the drawing");

            // Clicking the point number text finds the point.
            var label = new ACadSharp.Entities.TextEntity { Value = "2", InsertPoint = new CSMath.XYZ(309676.2, 4869230.0, 0), Height = 0.5 };
            doc.ModelSpace.Entities.Add(label);
            Assert.Equal(2, PointLinks.Find(label, job.Points)!.Id, "its number label leads back to it");
        }

        public static void TestReadsFdDraftsOwnTaggedPoints()
        {
            var doc = new ACadSharp.CadDocument();
            ACadSharp.Tables.Layer L(string n) { if (!doc.Layers.TryGetValue(n, out var l)) { l = new ACadSharp.Tables.Layer(n); doc.Layers.Add(l); } return l; }
            var pts = new List<SurveyPoint> { new SurveyPoint { Id = 101, Northing = 5000, Easting = 1000, Elevation = 100, Code = "IB" } };
            new AddEntitiesCommand(doc.ModelSpace, new ACadSharp.Entities.Entity[] { new ACadSharp.Entities.Point(new CSMath.XYZ(1000, 5000, 100)) { Layer = L("PTS") } }, "pt");
            var pt = doc.ModelSpace.Entities.OfType<ACadSharp.Entities.Point>().Single();
            PointLinks.Tag(pt, 101); PointLinks.TagCode(pt, "IB");
            var job = DrawingPoints.Read(doc, "plan.dwg", out string source)!;
            Assert.True(job.Points.Single().Id == 101 && job.Points[0].Code == "IB" && source.Contains("FD-Draft"), "FD-Draft's tagged points read back");
            Assert.True(DrawingPoints.Read(new ACadSharp.CadDocument(), "x.dwg", out _) == null, "no points, no job");
        }

        // ---- CHANGESHEET (v0.6.10) ----------------------------------------------------------------

        private static ACadSharp.CadDocument TinyTemplate()
        {
            var t = new ACadSharp.CadDocument();
            foreach (var (name, w, h) in new[] { ("17X22", 558.8, 431.8), ("22X34", 863.6, 558.8) })
            {
                var lay = new ACadSharp.Objects.Layout(name) { PaperWidth = w, PaperHeight = h, PaperSize = name + "_SIZE" };
                t.Layouts.Add(lay);
                var frame = new ACadSharp.Entities.LwPolyline { IsClosed = true, Layer = new ACadSharp.Tables.Layer("BORDER") };
                foreach (var (x, y) in new[] { (18.0, 15.0), (w - 18, 15.0), (w - 18, h - 15), (18.0, h - 15) }) frame.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex(new CSMath.XY(x, y)));
                lay.AssociatedBlock.Entities.Add(frame);
                lay.AssociatedBlock.Entities.Add(new ACadSharp.Entities.TextEntity { Value = "JOB NUMBER: XXXX", InsertPoint = new CSMath.XYZ(w - 100, 40, 0), Height = 3, Style = new ACadSharp.Tables.TextStyle("TITLES") });
                var mt = new ACadSharp.Entities.MText { Value = "SCALE 1:500", InsertPoint = new CSMath.XYZ(w - 100, 60, 0), Height = 3 };
                lay.AssociatedBlock.Entities.Add(mt);
            }
            return t;
        }

        public static void TestChangeSheetCopiesTheTemplateSheetAndFitsThePlan()
        {
            var std = ProVision;
            var template = TinyTemplate();
            var doc = new ACadSharp.CadDocument();
            var lot = Ln(1000, 5000, 1060, 5000);
            doc.ModelSpace.Entities.Add(lot);
            doc.ModelSpace.Entities.Add(Ln(1060, 5000, 1060, 5040));
            var plan = new Extents(); plan.Add(new Vec2(1000, 5000)); plan.Add(new Vec2(1060, 5040));
            var job = new JobSettings { Name = "24-0123" };
            var r = SheetChange.Apply(doc, template, std, "22X34", plan, null,
                (scale, sheet) => new TitleBlockFiller(std, job, scale, sheet, "t.dwg", new DateTime(2026, 10, 5), 863.6, 558.8), out string why)!;
            Assert.True(r != null, "applied: " + why);
            var lay = doc.Layouts.Single(l => l.Name == "22X34");
            Assert.Near(863.6, lay.PaperWidth, 1e-9, "the template sheet's paper");
            Assert.Equal("22X34_SIZE", lay.PaperSize, "and plot settings");
            var ents = lay.AssociatedBlock.Entities.ToList();
            Assert.True(ents.OfType<ACadSharp.Entities.LwPolyline>().Any(p => p.Layer.Name == "BORDER") && doc.Layers.Contains("BORDER"), "its frame, with its layer brought across");
            Assert.True(doc.TextStyles.Contains("TITLES"), "and text style");
            Assert.True(ents.OfType<ACadSharp.Entities.TextEntity>().Any(t => t.Value == "JOB NUMBER: 24-0123"), "the title block filled for this job");
            var vp = ents.OfType<ACadSharp.Entities.Viewport>().Single(v => v.Id != 1 && !(v.Width > 863));
            Assert.True(Math.Abs(vp.ViewCenter.X - 1030) < 1e-9 && Math.Abs(vp.ViewCenter.Y - 5020) < 1e-9, "the plan centred in the viewport");
            Assert.True(r.Scale.Denominator <= 250, "the largest scale that fits a 60 x 40 m plan on 22x34: 1:" + r.Scale.Denominator);
            Assert.True(r.Area.X2 <= 716 + 1e-9, "the viewport stays out of the title column");
            Assert.Near(vp.Height * r.Scale.ModelPerPaper, vp.ViewHeight, 1e-9, "at that scale");

            var path = Path.Combine(Path.GetTempPath(), "fdd-sheet-" + Guid.NewGuid().ToString("N") + ".dwg");
            try
            {
                ACadSharp.IO.DwgWriter.Write(path, doc);
                var back = ACadSharp.IO.DwgReader.Read(path);
                var sheet = new SceneBuilder(back).Layout("22X34");
                Assert.True(sheet.IsPaper && sheet.Groups.Any(g => g.Clip.HasValue && g.ToModel.HasValue), "reopened, the new sheet shows model space through its viewport");
            }
            finally { File.Delete(path); }

            // A second one of the same sheet gets its own name; undo takes a sheet back out.
            var r2 = SheetChange.Apply(doc, template, std, "22X34", plan, std.Scales.First(s => s.Denominator == 500), null, out _)!;
            Assert.Equal("22X34 (2)", r2.Layout.Name, "named apart");
            Assert.Equal(500.0, r2.Scale.Denominator, "a forced scale is kept");
            r2.Command.Undo();
            Assert.True(!doc.Layouts.Any(l => l.Name == "22X34 (2)"), "undone");
            r2.Command.Redo();
            Assert.True(doc.Layouts.Any(l => l.Name == "22X34 (2)"), "and redone");
            Assert.True(SheetChange.Apply(doc, template, std, "36X48", plan, null, null, out string none) == null && none.Contains("no sheet"), "a sheet the template lacks is refused");
        }
    }
}
