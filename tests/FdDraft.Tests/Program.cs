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
    }
}
