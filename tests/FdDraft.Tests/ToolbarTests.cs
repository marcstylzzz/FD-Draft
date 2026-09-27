using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ACadSharp.Entities;
using FdDraft.Cad;
using FdDraft.Cad.Editing;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;
using FdDraft.Core.Standards;
using FdDraft.View;
using FdDraft.View.Toolbars;

namespace FdDraft.Tests
{
    /// <summary>The toolbars (catalog, icons, command coverage) and the survey tools behind them.</summary>
    public static partial class Tests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FD-Draft.sln"))) dir = dir.Parent;
            if (dir == null) throw new Exception("repo root not found");
            return dir.FullName;
        }

        public static void TestToolbarIconsAllParseAndExist()
        {
            int n = 0;
            foreach (var key in ToolIcons.Keys)
            {
                var prims = ToolIcons.Get(key);
                Assert.True(prims.Count > 0, "icon " + key + " draws something");
                foreach (var p in prims)
                {
                    if (p.Kind == IconPrimKind.Text) { Assert.True(p.Data.Length > 0, key + " text"); continue; }
                    if (p.Kind == IconPrimKind.Path) Assert.True(Regex.IsMatch(p.Data, @"^[MLAQCZ0-9 .\-]+$"), key + " path uses only M L A Q C Z: " + p.Data);
                    else Assert.True(p.N.All(v => v > -3 && v < 27), key + " stays on its 24x24 grid");
                }
                n++;
            }
            Assert.True(n >= 180, "the full icon set is there: " + n);
            foreach (var bar in ToolbarCatalog.All)
                foreach (var b in bar.Buttons.Where(b => b.Kind != ToolButtonKind.Separator && b.Kind != ToolButtonKind.Custom))
                    Assert.True(ToolIcons.Has(b.Icon), bar.Name + " / " + b.Name + " has its icon " + b.Icon);
        }

        public static void TestToolbarCatalogMatchesMscadLayout()
        {
            int Count(string key) => ToolbarCatalog.Find(key)!.Buttons.Count(b => b.Kind != ToolButtonKind.Separator);
            // Button for button with MSCAD's icad.cui toolbars (MS Labels 1 = 15, MS Ties = 13, ...).
            Assert.Equal(15, Count("labels"), "FD Labels");
            Assert.Equal(13, Count("ties"), "FD Ties");
            Assert.Equal(17, Count("textedit"), "FD Text Edit");
            Assert.Equal(11, Count("fdlayer"), "FD Layer");
            Assert.Equal(18, Count("layer_tools"), "Layer Tools");
            Assert.Equal(30, Count("dimensioning"), "Dimensioning");
            Assert.Equal(5, Count("text"), "Text");
            Assert.Equal(18, Count("main"), "FD Main Control");
            Assert.Equal(15, Count("calcs"), "FD Calcs");
            Assert.Equal(18, Count("coordinate"), "FD Coordinate");
            Assert.True(ToolbarCatalog.All.Where(b => b.FromMscad).All(b => b.Name.StartsWith("FD ") || !b.Name.StartsWith("MS")), "FD-Draft's own names, not MicroSurvey's");
            var keys = ToolbarCatalog.All.Select(b => b.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count(), "toolbar keys are unique");
            foreach (var b in ToolbarCatalog.All.SelectMany(t => t.Buttons).Where(b => b.Kind == ToolButtonKind.Command && b.Command == null))
                Assert.True(b.Note.Length > 0, b.Name + " says why it isn't available");
        }

        /// <summary>Every command a toolbar button runs is one the app's command line knows.</summary>
        public static void TestEveryToolbarCommandIsHandled()
        {
            var app = Path.Combine(RepoRoot(), "src", "FdDraft.App");
            var source = string.Join("\n", Directory.GetFiles(app, "*.cs").Select(File.ReadAllText));
            var handled = new HashSet<string>(Regex.Matches(source, "case \"([A-Z0-9?]+)\"").Cast<Match>().Select(m => m.Groups[1].Value));
            foreach (var verb in ToolbarCatalog.CommandVerbs)
                Assert.True(handled.Contains(verb), "toolbar command " + verb + " has a case in RunCommand/ExecuteMsTool");
            foreach (var t in ToolbarCatalog.All.SelectMany(b => b.Buttons).Where(b => b.Kind == ToolButtonKind.Toggle))
                Assert.True(t.Command == "PAN" || t.Command!.StartsWith("SNAPMODE "), "toggle " + t.Name + " is one the app keeps in step");
        }

        public static void TestSplitBearingAndDistanceBearingStyles()
        {
            var std = FirmStandards.Default();
            var a = new Vec2(0, 0); var b = new Vec2(30, 30); // N45°E
            var split = CourseAnnotation.Layout(a, b, new Vec2(10, 20), CourseLabelStyle.SplitBearing, std, 0.5);
            Assert.Equal(2, split.Texts.Count, "two halves");
            Assert.Equal("N45°", split.Texts[0].Text, "direction and degrees");
            Assert.True(split.Texts[1].Text.StartsWith("00'00") && split.Texts[1].Text.EndsWith("E"), "minutes, seconds and quadrant: " + split.Texts[1].Text);
            var up = new Vec2(-Math.Sin(Math.PI / 4), Math.Cos(Math.PI / 4));
            Assert.True(Vec2.Dot(split.Texts[0].Position - new Vec2(15, 15), up) > 0 && Vec2.Dot(split.Texts[1].Position - new Vec2(15, 15), up) < 0, "degrees on the picked side, the rest across the line");
            Assert.True(split.GapFrom == null, "the line isn't broken");
            var db = CourseAnnotation.Layout(a, b, new Vec2(20, 10), CourseLabelStyle.DistanceDashBearing, std, 0.5).Texts.Single();
            Assert.True(db.Text.StartsWith("42.4") && db.Text.EndsWith("E") && Vec2.Dot(db.Position - new Vec2(15, 15), up) < 0, "distance then bearing, on the picked side: " + db.Text);
        }

        public static void TestCurveSolverFromAnyTwo()
        {
            var truth = CurveElements.FromRadiusDelta(100, 50 * Math.PI / 180);
            var pairs = new (char, double, char, double)[]
            {
                ('R', truth.R, 'L', truth.L), ('R', truth.R, 'C', truth.C), ('R', truth.R, 'T', truth.T), ('R', truth.R, 'E', truth.E), ('R', truth.R, 'M', truth.M),
                ('D', truth.Delta, 'L', truth.L), ('D', truth.Delta, 'C', truth.C), ('D', truth.Delta, 'T', truth.T),
                ('L', truth.L, 'C', truth.C), ('T', truth.T, 'C', truth.C), ('L', truth.L, 'M', truth.M), ('C', truth.C, 'E', truth.E),
            };
            foreach (var (k1, v1, k2, v2) in pairs)
            {
                var r = SurveyCalcs.SolveCurve(new Dictionary<char, double> { [k1] = v1, [k2] = v2 });
                Assert.True(r != null, k1 + "+" + k2 + " solves");
                Assert.Near(100, r!.R, 1e-6, k1 + "+" + k2 + " radius");
                Assert.Near(truth.Delta, r.Delta, 1e-9, k1 + "+" + k2 + " delta");
            }
            var parsed = SurveyCalcs.ParseCurveInput("R=100 D=50-00-00");
            Assert.Near(truth.L, SurveyCalcs.SolveCurve(parsed!)!.L, 1e-9, "typed input, delta in d-m-s");
            Assert.True(SurveyCalcs.SolveCurve(new Dictionary<char, double> { ['R'] = 10, ['C'] = 25 }) == null, "a chord longer than the diameter is impossible");
            Assert.Equal("50°00'00\"", SurveyCalcs.Dms(truth.Delta), "dms");
            Assert.True(SurveyCalcs.TryParseAngle("90-15-30", out double ang) && Math.Abs(ang - (90 + 15 / 60.0 + 30 / 3600.0) * Math.PI / 180) < 1e-12, "d-m-s angle");
        }

        public static void TestBestFitLineAndArc()
        {
            var pts = new List<Vec2> { new Vec2(0, 0.01), new Vec2(10, -0.01), new Vec2(20, 0.02), new Vec2(30, -0.02) };
            var line = SurveyCalcs.BestFitLine(pts)!.Value;
            Assert.Near(0, line.A.X, 1e-2, "spans from the first point"); Assert.Near(30, line.B.X, 1e-2, "to the last");
            Assert.True(line.RmsOffset < 0.02, "small scatter");
            var c = new Vec2(50, -20);
            var arcPts = Enumerable.Range(0, 7).Select(i => c + new Vec2(Math.Cos(0.2 + i * 0.15), Math.Sin(0.2 + i * 0.15)) * 75 + new Vec2(i % 2 == 0 ? 0.005 : -0.005, 0)).ToList();
            var exact = SurveyCalcs.BestFitArc(arcPts.Select((p, i) => p - new Vec2(i % 2 == 0 ? 0.005 : -0.005, 0)).ToList())!.Value;
            Assert.Near(75, exact.Arc.Radius, 1e-9, "exact points give the exact radius");
            Assert.True(Vec2.Distance(exact.Arc.Center, c) < 1e-9, "and centre");
            var fit = SurveyCalcs.BestFitArc(arcPts)!.Value;
            // Checked against scipy's least_squares on the same points: R 75.0293.
            Assert.Near(75.0293, fit.Arc.Radius, 1e-3, "noisy points: the geometric least-squares radius");
            Assert.Near(0.9, Math.Abs(fit.Arc.Sweep), 1e-3, "runs from the first point to the last");
            Assert.True(SurveyCalcs.BestFitArc(new List<Vec2> { new Vec2(0, 0), new Vec2(1, 1), new Vec2(2, 2) }) == null, "collinear points have no arc");
        }

        public static void TestCogoHelpers()
        {
            // Occupy the origin, backsight due north, turn 90° right, 10 m: due east.
            var q = SurveyCalcs.TurnedAngle(new Vec2(0, 0), new Vec2(0, 50), Math.PI / 2, 10);
            Assert.Near(10, q.X, 1e-9, "east"); Assert.Near(0, q.Y, 1e-9, "north");
            var so = SurveyCalcs.StationOffset(new Vec2(0, 0), new Vec2(100, 0), 25, 3);
            Assert.Near(25, so.X, 1e-9, "station"); Assert.Near(-3, so.Y, 1e-9, "positive offset is right of the line (south of an eastbound line)");
            var back = SurveyCalcs.ToStationOffset(new Vec2(0, 0), new Vec2(100, 0), so);
            Assert.Near(25, back.Station, 1e-9, "round trip station"); Assert.Near(3, back.Offset, 1e-9, "round trip offset");
            var tps = SurveyCalcs.TangentPoints(new Vec2(10, 0), new Vec2(0, 0), 5);
            Assert.Equal(2, tps.Count, "two tangents from outside");
            foreach (var t in tps) Assert.Near(0, Vec2.Dot(t - new Vec2(0, 0), t - new Vec2(10, 0)), 1e-9, "tangent is square to the radius");
            Assert.Equal(0, SurveyCalcs.TangentPoints(new Vec2(1, 0), new Vec2(0, 0), 5).Count, "none from inside");
            var arc = SurveyCalcs.CurveOffTangent(new Vec2(0, 0), new Vec2(1, 0), 10, Math.PI * 5, true);
            Assert.Near(10, arc.End.X, 1e-9, "a quarter circle turning left ends 10 east"); Assert.Near(10, arc.End.Y, 1e-9, "and 10 north");
            var ang = SurveyCalcs.AngleBetween(new Vec2(0, 0), new Vec2(10, 0), new Vec2(8, 0), new Vec2(0, 0), new Vec2(0, 10), new Vec2(0, 8))!.Value;
            Assert.Near(Math.PI / 2, ang.Sweep, 1e-9, "right angle between the picked legs");
            var obtuse = SurveyCalcs.AngleBetween(new Vec2(-10, 0), new Vec2(10, 0), new Vec2(-8, 0), new Vec2(0, 0), new Vec2(10, 10), new Vec2(5, 5))!.Value;
            Assert.Near(3 * Math.PI / 4, obtuse.Sweep, 1e-9, "the angle between the parts picked, not its supplement");
            Assert.True(SurveyCalcs.AngleBetween(new Vec2(0, 0), new Vec2(1, 0), new Vec2(0, 0), new Vec2(0, 1), new Vec2(1, 1), new Vec2(0, 1)) == null, "parallel lines");
        }

        public static void TestTextOnArcReadsUpright()
        {
            var c = new Vec2(0, 0);
            var over = SurveyCalcs.TextOnArc("ABC", c, 50, Math.PI / 2, 2);
            Assert.Equal(3, over.Count, "one per letter");
            Assert.True(over[0].At.X < over[1].At.X && over[1].At.X < over[2].At.X, "over the top it reads left to right");
            Assert.Near(0, over[1].Rotation, 1e-9, "the middle letter is level at the top");
            var under = SurveyCalcs.TextOnArc("ABC", c, 50, -Math.PI / 2, 2);
            Assert.True(under[0].At.X < under[2].At.X, "under the curve it still reads left to right");
            Assert.Near(0, under[1].Rotation, 1e-9, "and upright");
        }

        public static void TestHouseTieAndTables()
        {
            var doc = new ACadSharp.CadDocument();
            ACadSharp.Tables.Layer L(string n) { if (!doc.Layers.TryGetValue(n, out var l)) { l = new ACadSharp.Tables.Layer(n); doc.Layers.Add(l); } return l; }
            var building = new List<Vec2> { new Vec2(5, 5), new Vec2(15, 5), new Vec2(15, 12), new Vec2(5, 12) };
            var tie = SurveyDrafting.AutoTie(building, new Vec2(0, 0), new Vec2(0, 50))!.Value;
            Assert.Near(5, Vec2.Distance(tie.Corner, tie.Foot), 1e-9, "nearest corner to the west lot line");
            Assert.Near(0, tie.Foot.X, 1e-9, "square to it");
            var ents = SurveyDrafting.HouseTie(tie.Corner, tie.Foot, true, 0.5, 2, 0.5, 2, L("TIES"), "TIES", "", doc, L);
            Assert.Equal(1, ents.OfType<Line>().Count(), "one tie line");
            Assert.Equal(2, ents.OfType<Solid>().Count(), "an arrowhead each end");
            Assert.Equal("5.00", ents.OfType<TextEntity>().Single().Value, "its length");
            var plain = SurveyDrafting.HouseTie(tie.Corner, tie.Foot, false, 0.5, 2, 0.5, 2, L("TIES"), "TIES", "", doc, L);
            Assert.True(!plain.OfType<Solid>().Any(), "no arrows when not asked");

            var rows = new List<IList<string>> { new List<string> { "L1", "N45°00'00\"E", "12.345" }, new List<string> { "L2", "S10°00'00\"W", "7.000" } };
            var table = SurveyDrafting.Table("LINE TABLE", new List<string> { "LINE", "BEARING", "DISTANCE" }, rows, new Vec2(100, 100), 2, 0.5, "TBL", "TBL", "", doc, L);
            var texts = table.OfType<TextEntity>().Select(t => t.Value).ToList();
            Assert.True(texts.Contains("LINE TABLE") && texts.Contains("BEARING") && texts.Contains("12.345") && texts.Contains("L2"), "title, header and cells");
            Assert.Equal(4 + 4, table.OfType<Line>().Count(), "rules: 4 horizontal (header + 2 rows) and 4 vertical (3 columns)");

            var arc = new FdDraft.Core.Geometry.Arc { Center = new Vec2(0, 0), Radius = 100, StartAngle = 0, Sweep = Math.PI / 2, Start = new Vec2(100, 0), End = new Vec2(0, 100) };
            var data = SurveyDrafting.CurveData(arc, FirmStandards.Default(), 1);
            Assert.True(data[0] == "R=100.000" || data[0].StartsWith("R=100"), "radius first: " + data[0]);
            Assert.True(data.Any(d => d.StartsWith("Δ=90°00'00")), "delta");
            Assert.True(data.Any(d => d.StartsWith("CB=N45")), "chord bearing");
            var back = SurveyDrafting.ToCore(SurveyDrafting.ToEntity(arc, L("A")));
            Assert.Near(Math.PI / 2, back.Sweep, 1e-9, "an arc round-trips through a DWG ARC");
        }

        public static void TestDimensionTextEditAndPlacement()
        {
            var doc = new ACadSharp.CadDocument();
            var undo = new UndoStack();
            var dim = DimensionBuilder.Aligned(new Vec2(0, 0), new Vec2(20, 0), new Vec2(10, 5), 3);
            undo.Push(new AddDimensionCommand(doc.ModelSpace, dim, 1.0, "Dimension"));
            MText Label() => dim.Block!.Entities.OfType<MText>().Single();
            var home = Label().InsertPoint;
            undo.Push(DimensionBuilder.SetText(dim, "<> (M)", 3, "text")!);
            Assert.Equal("20.000 (M)", Label().Value, "<> becomes the measurement");
            undo.Push(DimensionBuilder.PlaceText(dim, new Vec2(10, 12), null, false, "move")!);
            Assert.Near(12, Label().InsertPoint.Y, 1e-9, "text moved where picked");
            // It travels with the dimension.
            undo.Push(TransformEntitiesCommand.Move(new[] { dim }, 100, 0, "move dim"));
            Assert.Near(110, Label().InsertPoint.X, 1e-9, "moved text follows a MOVE");
            undo.Undo();
            undo.Push(DimensionBuilder.PlaceText(dim, null, Math.PI / 2, false, "rotate")!);
            var dir = Label().AlignmentPoint;
            Assert.True(Math.Abs(dir.X) < 1e-9 && Math.Abs(dir.Y - 1) < 1e-9, "text turned to 90°");
            undo.Push(DimensionBuilder.PlaceText(dim, null, null, true, "home")!);
            Assert.True(Math.Abs(Label().InsertPoint.X - home.X) < 1e-9 && Math.Abs(Label().InsertPoint.Y - home.Y) < 1e-9, "home puts it back");
            undo.Undo(); undo.Undo();
            Assert.Near(12, Label().InsertPoint.Y, 1e-9, "undo steps back through the placements");
        }
    }
}

namespace FdDraft.Tests
{
    public static partial class Tests
    {
        public static void TestToolbarRowsPackToTheWindow()
        {
            // Marc's screen: 1920 px at 150% = 1280 device-independent pixels.
            var small = ToolbarLayout.Pack(ToolbarCatalog.All, 16, 1256);
            int rows16 = small.Values.Max(v => v.Band) + 1;
            var large = ToolbarLayout.Pack(ToolbarCatalog.All, 24, 1256);
            int rows24 = large.Values.Max(v => v.Band) + 1;
            Assert.True(rows16 <= 5, "16 px icons fit in five rows or fewer: " + rows16);
            Assert.True(ToolbarLayout.Pack(ToolbarCatalog.All, 12, 1256).Values.Max(v => v.Band) + 1 <= 4, "12 px icons in four");
            Assert.True(rows24 > rows16, "bigger icons need more rows");
            foreach (var band in small.Values.GroupBy(v => v.Band))
            {
                double w = ToolbarCatalog.All.Where(b => small[b.Key].Band == band.Key).Sum(b => ToolbarLayout.EstimateWidth(b, 16));
                Assert.True(w <= 1256 || band.Count() == 1, "row " + band.Key + " fits: " + w);
            }
            Assert.True(ToolbarCatalog.All.Select(b => small[b.Key]).Distinct().Count() == ToolbarCatalog.All.Count, "every bar has its own place");
            // Height: buttons shrink to about half the old footprint (24 icon + 8 chrome = 32).
            Assert.True((16 + ToolbarLayout.ButtonChrome(16)) / 32.0 <= 0.7, "a medium button is about two-thirds of the old one each way");
        }
    }
}

namespace FdDraft.Tests
{
    public static partial class Tests
    {
        public static void TestExplodePolylineBlockAndUndo()
        {
            var doc = new ACadSharp.CadDocument();
            var layer = new ACadSharp.Tables.Layer("LOT"); doc.Layers.Add(layer);
            var lot = new LwPolyline { IsClosed = true, Layer = layer };
            lot.Vertices.Add(new LwPolyline.Vertex(new CSMath.XY(0, 0)));
            lot.Vertices.Add(new LwPolyline.Vertex(new CSMath.XY(20, 0)) { Bulge = Math.Tan(Math.PI / 8) }); // a half-round... quarter bulge
            lot.Vertices.Add(new LwPolyline.Vertex(new CSMath.XY(20, 20)));
            doc.ModelSpace.Entities.Add(lot);
            PointLinks.TagCode(lot, "BDY");

            var block = new ACadSharp.Tables.BlockRecord("POST");
            block.Entities.Add(new Line(new CSMath.XYZ(-1, 0, 0), new CSMath.XYZ(1, 0, 0)));
            block.Entities.Add(new Circle { Center = CSMath.XYZ.Zero, Radius = 0.5 });
            doc.BlockRecords.Add(block);
            var ins = new Insert(block) { InsertPoint = new CSMath.XYZ(100, 50, 0), XScale = 2, YScale = 2, ZScale = 2, Layer = layer };
            doc.ModelSpace.Entities.Add(ins);

            var undo = new UndoStack();
            undo.Push(Exploder.Explode(new Entity[] { lot, ins }, out int n, out var made)!);
            Assert.Equal(2, n, "both exploded");
            var lines = doc.ModelSpace.Entities.OfType<Line>().ToList();
            var arcs = doc.ModelSpace.Entities.OfType<ACadSharp.Entities.Arc>().ToList();
            Assert.Equal(2 + 1, lines.Count, "the lot's two straight spans plus the post's line");
            Assert.Equal(1, arcs.Count, "the lot's curved span is an arc");
            Assert.True(!doc.ModelSpace.Entities.Contains(lot) && !doc.ModelSpace.Entities.Contains(ins), "the originals are gone");
            Assert.True(made.All(m => m.Layer == layer), "parts on the original's layer (block parts on 0 take the insert's)");
            Assert.True(made.Where(m => m is Line || m is ACadSharp.Entities.Arc).Take(3).All(m => PointLinks.TaggedCode(m) == "BDY"), "the lot's pieces keep its code");
            var post = lines.Single(l => l.StartPoint.X > 90);
            Assert.Near(98, post.StartPoint.X, 1e-9, "block part placed and scaled where the block showed it");
            var circle = doc.ModelSpace.Entities.OfType<Circle>().Single(c => !(c is ACadSharp.Entities.Arc));
            Assert.Near(1, circle.Radius, 1e-9, "circle scaled with the block");
            undo.Undo();
            Assert.True(doc.ModelSpace.Entities.Contains(lot) && doc.ModelSpace.Entities.Contains(ins) && !doc.ModelSpace.Entities.OfType<Line>().Any(), "one undo puts both back");
            Assert.True(Exploder.Explode(new Entity[] { new Line() }, out _, out _) == null, "a line doesn't explode");
        }
    }
}

namespace FdDraft.Tests
{
    public static partial class Tests
    {
        public static void TestViewTwistRoundTripsAndFits()
        {
            var v = new ViewTransform { Center = new Vec2(1000, 2000), Zoom = 3, ScreenWidth = 800, ScreenHeight = 600, Twist = 0.4 };
            var p = new Vec2(1012.5, 1994.25);
            var s = v.ToScreen(p);
            var back = v.ToScene(s.X, s.Y);
            Assert.Near(p.X, back.X, 1e-9, "x round trip"); Assert.Near(p.Y, back.Y, 1e-9, "y round trip");
            // A line along the twist-cancelling direction shows level on screen.
            v.Twist = SurveyorView.TwistToLevel(new Vec2(0, 0), new Vec2(10, 7));
            var a = v.ToScreen(new Vec2(1000, 2000)); var b = v.ToScreen(new Vec2(1010, 2007));
            Assert.Near(a.Y, b.Y, 1e-9, "the picked line runs level");
            Assert.True(b.X > a.X, "and reads left to right");
            // Pointing a bearing up: N45°E straight up the screen.
            v.Twist = SurveyorView.TwistToPointUp(Math.PI / 4);
            var c = v.ToScreen(new Vec2(1000, 2000)); var d = v.ToScreen(new Vec2(1010, 2010));
            Assert.Near(c.X, d.X, 1e-9, "N45E points straight up"); Assert.True(d.Y < c.Y, "up, not down");
            // Pan moves the drawing with the mouse, turned or not.
            var before = v.ToScreen(p);
            v.PanPixels(30, -20);
            var after = v.ToScreen(p);
            Assert.Near(before.X + 30, after.X, 1e-9, "pan x"); Assert.Near(before.Y - 20, after.Y, 1e-9, "pan y");
            // Fitting a turned rectangle keeps all of it on screen.
            v.Fit(new FdDraft.Core.Standards.Rect(0, 0, 100, 40), 0);
            foreach (var q in new[] { new Vec2(0, 0), new Vec2(100, 0), new Vec2(100, 40), new Vec2(0, 40) })
            {
                var sq = v.ToScreen(q);
                Assert.True(sq.X >= -1e-6 && sq.X <= 800 + 1e-6 && sq.Y >= -1e-6 && sq.Y <= 600 + 1e-6, "corner on screen");
            }
        }

        private static (ACadSharp.CadDocument Doc, ACadSharp.Objects.Layout Layout, ACadSharp.Entities.Viewport Vp, Insert Arrow) TwistSheet()
        {
            var doc = new ACadSharp.CadDocument();
            doc.ModelSpace.Entities.Add(new Line(new CSMath.XYZ(990, 2000, 0), new CSMath.XYZ(1010, 2000, 0)));
            var layout = new ACadSharp.Objects.Layout("SHEET") { PaperWidth = 420, PaperHeight = 297 };
            doc.Layouts.Add(layout);
            var vp = SheetViewports.Create(new FdDraft.Core.Standards.Rect(20, 20, 400, 277), new Vec2(1000, 2000), 0.5, doc.Layers["0"]);
            layout.AssociatedBlock.Entities.Add(vp);
            var block = new ACadSharp.Tables.BlockRecord("Plan-NORTH ARROW");
            block.Entities.Add(new Line(CSMath.XYZ.Zero, new CSMath.XYZ(0, 4, 0)));
            doc.BlockRecords.Add(block);
            var arrow = new Insert(block) { InsertPoint = new CSMath.XYZ(40, 250, 0) };
            layout.AssociatedBlock.Entities.Add(arrow);
            return (doc, layout, vp, arrow);
        }

        public static void TestSurveyorViewTurnsSheetsAndNorthArrow()
        {
            var (doc, layout, vp, arrow) = TwistSheet();
            var undo = new UndoStack();
            double twist = 30 * Math.PI / 180;
            undo.Push(SurveyorView.Apply(doc, twist, "Plan-NORTH ARROW", out int sheets, out int arrows)!);
            Assert.Equal(1, sheets, "the sheet's plan viewport turned"); Assert.Equal(1, arrows, "and its north arrow");
            Assert.Near(twist, vp.TwistAngle, 1e-12, "viewport twist");
            var mid = SurveyorView.ModelCenter(vp);
            Assert.Near(1000, mid.X, 1e-9, "same model point in the middle"); Assert.Near(2000, mid.Y, 1e-9, "same model point in the middle");
            Assert.Near(twist, arrow.Rotation, 1e-12, "north arrow turned by the same angle");
            Assert.Near(twist, SurveyorView.CurrentTwist(doc), 1e-12, "the drawing knows its twist");

            // The sheet shows the plan turned: the east-west line now rises at 30° on paper,
            // centred in the viewport.
            var scene = new SceneBuilder(doc).Layout("SHEET");
            var line = scene.Groups.Where(g => g.ToModel.HasValue).SelectMany(g => g.Prims).Single(p => p.Kind == PrimKind.Polyline);
            var p0 = line.Points[0]; var p1 = line.Points[1];
            Assert.Near(twist, Math.Atan2(p1.Y - p0.Y, p1.X - p0.X), 1e-9, "turned on paper");
            Assert.Near(210, (p0.X + p1.X) / 2, 1e-6, "still centred on the viewport (x)"); Assert.Near(148.5, (p0.Y + p1.Y) / 2, 1e-6, "(y)");
            var g0 = scene.Groups.First(g => g.ToModel.HasValue);
            var m = g0.ToModel!.Value.Apply(new Vec2(210, 148.5));
            Assert.Near(1000, m.X, 1e-9, "picking on the sheet finds the right model point"); Assert.Near(2000, m.Y, 1e-9, "(y)");

            undo.Undo();
            Assert.Near(0, vp.TwistAngle, 1e-12, "undo: north up again"); Assert.Near(0, arrow.Rotation, 1e-12, "arrow back");
            Assert.Near(1000, vp.ViewCenter.X, 1e-9, "view centre back");
        }

        public static void TestTurnedBoxSelect()
        {
            // A 10 m line running N45E, boxed on a view where that line runs level.
            var doc = new ACadSharp.CadDocument();
            var ln = new Line(new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(7.0710678, 7.0710678, 0));
            doc.ModelSpace.Entities.Add(ln);
            var scene = new SceneBuilder(doc).Model();
            double twist = SurveyorView.TwistToLevel(new Vec2(0, 0), new Vec2(1, 1));
            var v = new ViewTransform { Center = new Vec2(3.5, 3.5), Zoom = 20, ScreenWidth = 800, ScreenHeight = 600, Twist = twist };
            // A thin screen box round the level line: in the drawing it's a turned box.
            var s0 = v.ToScreen(new Vec2(0, 0)); var s1 = v.ToScreen(new Vec2(7.0710678, 7.0710678));
            var a = v.ToScene(s0.X - 5, s0.Y - 5); var b = v.ToScene(s1.X + 5, s1.Y + 5);
            Assert.True(BoxSelect.Handles(scene, twist, a, b, crossing: false).Contains(ln.Handle), "the turned window takes the line");
            var c = v.ToScene(s0.X - 5, s0.Y - 5); var d = v.ToScene((s0.X + s1.X) / 2, s1.Y + 5);
            Assert.True(!BoxSelect.Handles(scene, twist, c, d, crossing: false).Contains(ln.Handle), "half a window doesn't");
            Assert.True(BoxSelect.Handles(scene, twist, c, d, crossing: true).Contains(ln.Handle), "half a crossing does");
        }

        public static void TestPaletteGainsNewUsefulTools()
        {
            string old = "[Text Styles]\nA = text | layer=X\n\n[Useful Tools]\nInverse = INV\nMine = LABEL\n\n[Line Styles]\nB = line | layer=Y\n";
            var updated = ToolPalette.WithAddedTools(old);
            var p = ToolPalette.Parse(updated.Split('\n'));
            var useful = p.Tabs.Single(t => t.Name == "Useful Tools").Tools.Select(t => t.Command).ToList();
            Assert.True(useful.Take(2).SequenceEqual(new[] { "INV", "LABEL" }), "the firm's own entries stay first, untouched");
            Assert.True(useful.Contains("SV") && useful.Contains("WV") && useful.Contains("RSV"), "surveyor view tools added");
            Assert.Equal(1, p.Tabs.Single(t => t.Name == "Line Styles").Tools.Count, "other tabs untouched");
            Assert.Equal(updated, ToolPalette.WithAddedTools(updated), "added only once");
            Assert.True(ToolPalette.Parse(ToolPalette.DefaultText.Split('\n')).Tabs.Single(t => t.Name == "Useful Tools").Tools.Any(t => t.Command == "SV"), "and in the starting palette");
        }
    }
}

namespace FdDraft.Tests
{
    public static partial class Tests
    {
        public static void TestLabelsFollowSurveyorView()
        {
            var doc = new ACadSharp.CadDocument();
            var ms = doc.ModelSpace.Entities;
            TextEntity T(string v, double x, double y, double rot, string layer = "0") =>
                new TextEntity { Value = v, InsertPoint = new CSMath.XYZ(x, y, 0), AlignmentPoint = new CSMath.XYZ(x, y, 0), Height = 1, Rotation = rot,
                    HorizontalAlignment = TextHorizontalAlignment.Left, VerticalAlignment = TextVerticalAlignmentType.Bottom,
                    Layer = doc.Layers.TryGetValue(layer, out var l) ? l : AddLayer(doc, layer) };
            // Point 101 at (100,100): node, symbol, number (down-right) and elevation (below-right, the old way).
            var node = new Point(new CSMath.XYZ(100, 100, 0)); ms.Add(node); PointLinks.Tag(node, 101);
            var sym = new Insert(new ACadSharp.Tables.BlockRecord("IB") ) { InsertPoint = new CSMath.XYZ(100, 100, 0) };
            doc.BlockRecords.Add(sym.Block); ms.Add(sym); PointLinks.Tag(sym, 101);
            var num = T("101", 101, 99, 0); ms.Add(num); PointLinks.Tag(num, 101);
            var elev = T("123.45", 101.4, 99.6, 0, "ELEVATION-GRND"); ms.Add(elev); PointLinks.Tag(elev, 101);
            // An east-west line with its bearing along it, a north-south line whose label reads upward, and a note.
            ms.Add(new Line(new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(50, 0, 0)));
            var ew = T("N90°00'00\"E", 20, 0.5, 0); ms.Add(ew);
            ms.Add(new Line(new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(0, 50, 0)));
            var ns = T("N00°00'00\"E", -0.5, 20, Math.PI / 2); ms.Add(ns);
            var note = T("LOT 12", 200, 200, 0); ms.Add(note);

            double twist = Math.PI / 2; // the plan turned a quarter turn counter-clockwise
            var undo = new UndoStack();
            undo.Push(SurveyorView.Relabel(doc, 0, twist, 45, out int changed)!);
            Assert.True(changed >= 5, "labels moved: " + changed);
            Assert.True(Angles.ReadsLeftToRight(note.Rotation, twist) && Math.Abs(Math.Sin(note.Rotation + twist)) < 1e-9, "the note reads level on the turned plan: " + note.Rotation);
            Assert.Near(0, ew.Rotation, 1e-9, "a bearing along its line keeps the line's angle");
            Assert.True(Angles.ReadsLeftToRight(ns.Rotation, twist), "the north-south label is turned to read left to right in the new view");
            Assert.Near(-twist, sym.Rotation, 1e-9, "the symbol stands upright on the plan (turned back against the view)");
            Assert.True(Math.Abs(Math.Sin(num.Rotation + twist)) < 1e-9 && Math.Cos(num.Rotation + twist) > 0, "the point number reads level on the plan");
            // Elevation: up-right of the point as the plan is seen.
            var v = new ViewTransform { Center = new Vec2(100, 100), Zoom = 10, ScreenWidth = 400, ScreenHeight = 400, Twist = twist };
            var sp = v.ToScreen(new Vec2(100, 100)); var se = v.ToScreen(new Vec2(elev.AlignmentPoint.X, elev.AlignmentPoint.Y));
            Assert.True(se.X > sp.X + 1 && se.Y < sp.Y - 1, "the elevation sits up and to the right of its point on the plan");
            Assert.Near(Math.Atan2(sp.Y - se.Y, se.X - sp.X), Math.PI / 4, 1e-6, "at 45°");
            Assert.True(elev.VerticalAlignment == TextVerticalAlignmentType.Middle && elev.HorizontalAlignment == TextHorizontalAlignment.Left, "starting at the point, centred on its line");
            Assert.Near(Math.PI / 4, elev.Rotation + twist, 1e-9, "and the text itself reads up the 45° line, like Marc's 98.58");
            undo.Undo();
            Assert.Near(0, note.Rotation, 1e-9, "undo: note back"); Assert.Near(0, sym.Rotation, 1e-9, "symbol back");
            Assert.Near(101.4, elev.AlignmentPoint.X, 1e-9, "elevation back");
        }

        private static ACadSharp.Tables.Layer AddLayer(ACadSharp.CadDocument doc, string name) { var l = new ACadSharp.Tables.Layer(name); doc.Layers.Add(l); return l; }

        public static void TestNewLabelsReadInTheTurnedView()
        {
            try
            {
                Angles.ViewTwist = Math.PI / 2;
                // A north-south course: north-up it reads upward (90°); turned a quarter turn it would
                // then read upside down, so it runs the other way.
                double r = Angles.ReadableRotation(new Vec2(0, 0), new Vec2(0, 10));
                Assert.True(Angles.ReadsLeftToRight(r, Math.PI / 2), "reads left to right in the turned view");
                Assert.Near(-Math.PI / 2, r, 1e-9, "i.e. runs downward in the drawing");
                var (at, h, v) = Annotator.PointLabelPlace(new Vec2(0, 0), 2, 45, Math.PI / 2);
                Assert.True(at.X > 0 && at.Y < 0 && h == HAlign.Left && v == VAlign.Bottom, "an elevation drafted on a turned plan goes up-right as seen (down-right in the drawing, turned a quarter turn)");
            }
            finally { Angles.ViewTwist = 0; }
            Assert.Near(Math.PI / 2, Angles.ReadableRotation(new Vec2(0, 0), new Vec2(0, 10)), 1e-9, "north up again: reads upward");
        }
    }
}

namespace FdDraft.Tests
{
    public static partial class Tests
    {
        /// <summary>Marc's screenshot: an elevation 0.5 mm out from its node read as "to the right",
        /// not 45°. It now sits clear on the diagonal.</summary>
        public static void TestElevationSitsPlainlyOnItsDiagonal()
        {
            double mpm = 1; // 1 unit = 1 paper mm
            double d = Annotator.PointLabelDistance(0.7 * 1.5 * mpm, 1.5 * mpm);
            var (at, h, v) = Annotator.PointLabelPlace(new Vec2(0, 0), d, 45, 0);
            Assert.True(at.X > 0 && at.Y > 0, "up-right of the point");
            Assert.Near(at.X, at.Y, 1e-9, "on the 45° line");
            var (eat, erot, eh, ev) = Annotator.ElevationPlace(new Vec2(0, 0), 1, 45, 0);
            Assert.Near(Math.PI / 4, erot, 1e-12, "the elevation text runs up the 45° line");
            Assert.Near(eat.X, eat.Y, 1e-12, "starting on that line");
            Assert.True(eh == HAlign.Left && ev == VAlign.Middle, "from the point outward, centred on the line");
            var (_, back, bh, _) = Annotator.ElevationPlace(new Vec2(0, 0), 1, 135, 0);
            Assert.True(Angles.ReadsLeftToRight(back, 0) && bh == HAlign.Right, "an up-left angle still reads left to right, toward the point");
        }
    }
}

namespace FdDraft.Tests
{
    public static partial class Tests
    {
        /// <summary>Marc: the north arrow distorted and inverted when moved - after Surveyor View had
        /// turned it, ACadSharp's own transform garbled a rotated block's scale.</summary>
        public static void TestRotatedBlockSurvivesMoveRotateMirror()
        {
            var doc = new ACadSharp.CadDocument();
            var b = new ACadSharp.Tables.BlockRecord("NORTH"); doc.BlockRecords.Add(b);
            foreach (var rot in new[] { 0.0, 0.5, 2.0, -1.0, Math.PI })
            {
                var ins = new Insert(b) { InsertPoint = new CSMath.XYZ(40, 250, 0), XScale = 3, YScale = 3, ZScale = 3, Rotation = rot };
                doc.ModelSpace.Entities.Add(ins);
                var undo = new UndoStack();
                undo.Push(TransformEntitiesCommand.Move(new Entity[] { ins }, 10, 5, "m"));
                Assert.Near(3, ins.XScale, 1e-9, "move keeps x scale at rotation " + rot); Assert.Near(3, ins.YScale, 1e-9, "and y scale");
                Assert.Near(Math.Sin(rot), Math.Sin(ins.Rotation), 1e-9, "and the rotation"); Assert.Near(Math.Cos(rot), Math.Cos(ins.Rotation), 1e-9, "(cos)");
                Assert.Near(50, ins.InsertPoint.X, 1e-9, "moved"); Assert.Near(255, ins.InsertPoint.Y, 1e-9, "moved");
                undo.Push(TransformEntitiesCommand.Rotate(new Entity[] { ins }, new CSMath.XYZ(0, 0, 0), 0.3, "r"));
                Assert.Near(3, ins.XScale, 1e-9, "rotate keeps the scale"); Assert.Near(3, ins.YScale, 1e-9, "rotate keeps the scale");
                Assert.Near(Math.Sin(rot + 0.3), Math.Sin(ins.Rotation), 1e-9, "turned by the rotation");
                undo.Undo(); undo.Undo();
                Assert.Near(3, ins.XScale, 1e-9, "undo leaves it whole"); Assert.Near(3, ins.YScale, 1e-9, "undo leaves it whole");
                Assert.Near(40, ins.InsertPoint.X, 1e-9, "and back in place");
            }
            // A north arrow already mangled by the old bug is mended.
            var layout = new ACadSharp.Objects.Layout("S1"); doc.Layouts.Add(layout);
            var bad = new Insert(b) { InsertPoint = new CSMath.XYZ(10, 10, 0), XScale = -0.9035, YScale = 4.1453, ZScale = 3, Rotation = 0.5 };
            layout.AssociatedBlock.Entities.Add(bad);
            var fix = SurveyorView.RepairNorthArrows(doc, "", out int fixedArrows);
            Assert.True(fix != null && fixedArrows == 1, "the arrow is found");
            Assert.True(bad.XScale > 0 && Math.Abs(bad.XScale - bad.YScale) < 1e-9, "even and not mirrored");
            Assert.Near(0, bad.Rotation, 1e-9, "pointing true north on a north-up sheet");
        }
    
        public static void TestTraverseLegAppliesScaleFactorAndCorrection()
        {
            // MSCAD's Line Computations example: N73°10'10"E, 36.948 from 4878046.806 N 280371.227 E.
            var from = new FdDraft.Core.Geometry.Vec2(280371.227, 4878046.806);
            var leg = FdDraft.Core.Geometry.Cogo.TraverseLegFrom(from, "N73.1010E", "36.948", 0, 1.0, out string err);
            Assert.True(leg != null, "N73.1010E reads: " + err);
            Assert.Near(280406.593, leg!.Value.To.X, 2e-3, "to E");
            Assert.Near(4878057.504, leg.Value.To.Y, 2e-3, "to N");
            foreach (var form in new[] { "NE73.1010", "n73-10-10e", "73.1010", "73-10-10" })
            {
                var l2 = FdDraft.Core.Geometry.Cogo.TraverseLegFrom(from, form, "36.948", 0, 1.0, out _);
                Assert.True(l2 != null && Math.Abs(l2.Value.Azimuth - leg.Value.Azimuth) < 1e-9, "same bearing as " + form);
            }
            // Input scale: 100.000 ground at SF 0.9996 is drawn 99.960 grid.
            var s = FdDraft.Core.Geometry.Cogo.TraverseLegFrom(new FdDraft.Core.Geometry.Vec2(0, 0), "0", "100", 0, 0.9996, out _)!.Value;
            Assert.Near(99.96, s.GridDistance, 1e-9, "grid = ground x SF"); Assert.Near(100, s.TypedDistance, 1e-12, "typed kept");
            Assert.Near(99.96, s.To.Y, 1e-9, "due north");
            // Bearing correction is added: N0E + 0°01'30" (typed DD.MMSS 0.0130).
            Assert.True(FdDraft.Core.Geometry.Cogo.TryParseAngleText("0.0130", out double corr), "correction reads");
            Assert.Near(90 / 3600.0, corr * 180 / Math.PI, 1e-12, "0.0130 = 1'30\"");
            Assert.True(FdDraft.Core.Geometry.Cogo.TryParseAngleText("-0°01'30\"", out double neg) && Math.Abs(neg + corr) < 1e-12, "negative D M S");
            var c = FdDraft.Core.Geometry.Cogo.TraverseLegFrom(new FdDraft.Core.Geometry.Vec2(0, 0), "N0E", "10", corr, 1, out _)!.Value;
            Assert.Near(corr, c.Azimuth, 1e-12, "corrected");
            // Bad input is refused with a reason, not drawn.
            Assert.True(FdDraft.Core.Geometry.Cogo.TraverseLegFrom(from, "N73.1010E", "0", 0, 1, out string e1) == null && e1.Length > 0, "zero distance refused");
            Assert.True(FdDraft.Core.Geometry.Cogo.TraverseLegFrom(from, "banana", "5", 0, 1, out string e2) == null && e2.Length > 0, "nonsense bearing refused");
            Assert.True(FdDraft.Core.Geometry.Cogo.TraverseLegFrom(from, "N73.7010E", "5", 0, 1, out _) == null, "70 minutes refused");
            // LINE keeps a plain azimuth in decimal degrees.
            Assert.Near(125.5, FdDraft.Core.Geometry.Cogo.ParseBearing("125.5") * 180 / Math.PI, 1e-9, "LINE unchanged");
        }

        public static void TestLineComputationNumbers()
        {
            var c = new FdDraft.Core.Geometry.LineComputation(280371.227, 4878046.806, 0, 280406.593, 4878057.504, 0, 1 / 0.9998);
            Assert.Near(36.948, c.Horizontal, 1e-3, "horizontal (from 3-decimal coordinates)");
            Assert.Near(73 + 10 / 60.0 + 10 / 3600.0, c.Azimuth * 180 / Math.PI, 2.0 / 3600, "N73°10'10\"E within 2 seconds (the shown coordinates are rounded)");
            Assert.Near(c.Horizontal / 0.9998, c.ScaledHorizontal, 1e-9, "scaled = grid / SF (as labels print)");
            Assert.Near(0, c.GradePercent, 1e-12, "flat");
            var s = new FdDraft.Core.Geometry.LineComputation(0, 0, 10, 30, 40, 12.5);
            Assert.Near(50, s.Horizontal, 1e-12, "3-4-5"); Assert.Near(5, s.GradePercent, 1e-12, "grade"); Assert.Near(2.5, s.DeltaZ, 1e-12, "dZ");
            Assert.Near(Math.Sqrt(2500 + 6.25), s.Slope, 1e-12, "slope");
        }

        private static ACadSharp.Entities.Spline GlyphSpline(double cx, double cy, bool wrapped)
        {
            // A closed cubic outline round (cx, cy) - what exploded text and logo art are made of.
            var corners = new[] { new CSMath.XYZ(cx - 5, cy - 5, 0), new CSMath.XYZ(cx + 5, cy - 5, 0), new CSMath.XYZ(cx + 5, cy + 5, 0), new CSMath.XYZ(cx - 5, cy + 5, 0) };
            var sp = new ACadSharp.Entities.Spline { Degree = 3 };
            var ctrl = wrapped ? corners.Concat(corners.Take(3)).ToList() : corners.ToList();
            foreach (var c in ctrl) sp.ControlPoints.Add(c);
            int knots = wrapped ? ctrl.Count + 3 + 1 : ctrl.Count + 2 * 3 + 1;
            for (int k = 0; k < knots; k++) sp.Knots.Add(k);
            sp.IsClosed = true;
            return sp;
        }

        public static void TestClosedSplinesDoNotRayToTheOrigin()
        {
            foreach (bool wrapped in new[] { true, false })
            {
                var sp = GlyphSpline(50, 50, wrapped);
                var pts = FdDraft.View.SplinePoints.Of(sp);
                Assert.True(pts.Count > 20, "sampled (" + (wrapped ? "wrapped" : "unwrapped") + ")");
                Assert.True(pts.All(q => q.X > 44 && q.X < 56 && q.Y > 44 && q.Y < 56), "every point on the outline, none at 0,0 (" + (wrapped ? "wrapped" : "unwrapped") + ")");
                Assert.True(Math.Abs(pts[0].X - pts[pts.Count - 1].X) < 1e-9 && Math.Abs(pts[0].Y - pts[pts.Count - 1].Y) < 1e-9, "closed");
            }
            // In a block inserted far from the origin (a title-block logo), nothing is drawn back at the insert point.
            var doc = new ACadSharp.CadDocument();
            var blk = new ACadSharp.Tables.BlockRecord("LOGO");
            blk.Entities.Add(GlyphSpline(50, 50, true));
            doc.BlockRecords.Add(blk);
            doc.Entities.Add(new ACadSharp.Entities.Insert(blk) { InsertPoint = new CSMath.XYZ(1000, 2000, 0) });
            var scene = new FdDraft.View.SceneBuilder(doc).Model();
            var all = scene.AllPrims().Where(pr => pr.Points != null).SelectMany(pr => pr.Points!).ToList();
            Assert.True(all.Count > 20, "the logo is drawn");
            Assert.True(all.All(q => q.X > 1044 && q.Y > 2044), "no ray to the insertion point (1000, 2000)");
        }

        public static ACadSharp.Entities.Hatch LetterO(double cx, double cy, bool solid)
        {
            // An "O": a 10 x 10 square with a round hole - outer as lines, hole as a clockwise arc
            // edge, a bulged polyline loop beside it.
            var h = new ACadSharp.Entities.Hatch { IsSolid = solid };
            var outer = new ACadSharp.Entities.Hatch.BoundaryPath();
            var c = new[] { (cx - 5, cy - 5), (cx + 5, cy - 5), (cx + 5, cy + 5), (cx - 5, cy + 5) };
            for (int i = 0; i < 4; i++)
                outer.Edges.Add(new ACadSharp.Entities.Hatch.BoundaryPath.Line { Start = new CSMath.XY(c[i].Item1, c[i].Item2), End = new CSMath.XY(c[(i + 1) % 4].Item1, c[(i + 1) % 4].Item2) });
            h.Paths.Add(outer);
            var hole = new ACadSharp.Entities.Hatch.BoundaryPath();
            hole.Edges.Add(new ACadSharp.Entities.Hatch.BoundaryPath.Arc { Center = new CSMath.XY(cx, cy), Radius = 3, StartAngle = 0, EndAngle = 2 * Math.PI, CounterClockWise = false });
            h.Paths.Add(hole);
            if (!solid)
            {
                h.Pattern = new ACadSharp.Entities.HatchPattern("ANSI31");
                h.Pattern.Lines.Add(new ACadSharp.Entities.HatchPattern.Line { Angle = Math.PI / 4, BasePoint = new CSMath.XY(0, 0), Offset = new CSMath.XY(-0.7071, 0.7071) });
            }
            return h;
        }

        public static void TestHatchesFillWithTheirHolesOpen()
        {
            var doc = new ACadSharp.CadDocument();
            doc.Entities.Add(LetterO(0, 0, true));
            var pl = new ACadSharp.Entities.Hatch { IsSolid = true };
            var path = new ACadSharp.Entities.Hatch.BoundaryPath();
            var poly = new ACadSharp.Entities.Hatch.BoundaryPath.Polyline { IsClosed = true };
            poly.Vertices.Add(new CSMath.XYZ(20, 0, 1)); poly.Vertices.Add(new CSMath.XYZ(30, 0, 1)); // two half circles: a disc of radius 5 about (25, 0)
            path.Edges.Add(poly); pl.Paths.Add(path);
            doc.Entities.Add(pl);
            doc.Entities.Add(LetterO(50, 0, false));
            var scene = new FdDraft.View.SceneBuilder(doc).Model();
            var fills = scene.AllPrims().Where(p => p.Kind == FdDraft.View.PrimKind.Fill).ToList();
            Assert.Equal(2, fills.Count, "two solid hatches fill");
            var o = fills.First(f => f.Points.All(q => q.X < 10));
            Assert.True(o.Holes != null && o.Holes.Count == 1, "the O keeps its hole");
            Assert.True(o.Holes![0].All(q => Math.Abs(Math.Sqrt(q.X * q.X + q.Y * q.Y) - 3) < 1e-9), "hole on the radius-3 circle");
            var disc = fills.First(f => f.Points.All(q => q.X >= 19.99));
            Assert.True(disc.Points.All(q => Math.Abs(Math.Sqrt((q.X - 25) * (q.X - 25) + q.Y * q.Y) - 5) < 1e-6), "bulged loop is the circle");
            Assert.True(disc.Points.Any(q => q.Y > 4.9) && disc.Points.Any(q => q.Y < -4.9), "both halves");
            // The pattern hatch: 45-degree lines, all inside the square and none inside the hole.
            var lines = scene.AllPrims().Where(p => p.Kind == FdDraft.View.PrimKind.Polyline && p.Points.All(q => q.X > 40)).ToList();
            Assert.True(lines.Count > 10, "pattern lines drawn: " + lines.Count);
            foreach (var l in lines)
            {
                var m = (l.Points[0] + l.Points[1]) * 0.5;
                Assert.True(Math.Abs(m.X - 50) <= 5 + 1e-6 && Math.Abs(m.Y) <= 5 + 1e-6, "inside the square");
                Assert.True(Math.Sqrt((m.X - 50) * (m.X - 50) + m.Y * m.Y) > 3 - 1e-6, "not in the hole");
            }
        }

        public static void TestShxTextKeepsAutoCadsSpacing()
        {
            var doc = new ACadSharp.CadDocument();
            var shx = new ACadSharp.Tables.TextStyle("L80") { Filename = "romans.shx" };
            var ttf = new ACadSharp.Tables.TextStyle("ARIAL") { Filename = "arial.ttf" };
            doc.TextStyles.Add(shx); doc.TextStyles.Add(ttf);
            doc.Entities.Add(new ACadSharp.Entities.TextEntity { Value = "SHOWN HEREON ARE GROUND", Height = 2, Style = shx, InsertPoint = new CSMath.XYZ(0, 0, 0) });
            doc.Entities.Add(new ACadSharp.Entities.TextEntity { Value = "SHOWN HEREON ARE GROUND", Height = 2, Style = ttf, InsertPoint = new CSMath.XYZ(0, 10, 0) });
            var scene = new FdDraft.View.SceneBuilder(doc).Model();
            var t = scene.AllPrims().Where(p => p.Kind == FdDraft.View.PrimKind.Text).OrderBy(p => p.Center.Y).ToList();
            Assert.True(t[0].WideSpaces && !t[1].WideSpaces, "SHX text gets wide spaces, TrueType doesn't");
            Assert.Equal("SHOWN HEREON ARE GROUND", t[0].Text, "the text itself unchanged");
            double drawn = FdDraft.View.PdfSceneWriter.MeasureText(t[0].Text, 2, true) * t[0].WidthFactor;
            Assert.Near(FdDraft.View.ShxMetrics.Width("SHOWN HEREON ARE GROUND", 2), drawn, 1e-9, "drawn as long as AutoCAD draws it");
            Assert.True(drawn > FdDraft.View.PdfSceneWriter.MeasureText(t[1].Text, 2) * 1.02, "and longer than plain Arial");
            // MTEXT wraps where AutoCAD would: at SHX widths.
            var box = FdDraft.View.ShxMetrics.Width("THE INTENDED PLOT SIZE", 2) + 0.5;
            doc.Entities.Add(new ACadSharp.Entities.MText { Value = "THE INTENDED PLOT SIZE OF THIS", Height = 2, RectangleWidth = box, Style = shx, InsertPoint = new CSMath.XYZ(0, 50, 0) });
            var lines = new FdDraft.View.SceneBuilder(doc).Model().AllPrims().Where(p => p.Kind == FdDraft.View.PrimKind.Text && p.Center.Y > 40).OrderByDescending(p => p.Center.Y).Select(p => p.Text).ToList();
            Assert.Equal("THE INTENDED PLOT SIZE", lines[0], "first line breaks at the SHX width");
        }
}
}
