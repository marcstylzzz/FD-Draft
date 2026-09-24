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
    }
}
