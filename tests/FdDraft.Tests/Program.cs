using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

    public static class Tests
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
    }
}
