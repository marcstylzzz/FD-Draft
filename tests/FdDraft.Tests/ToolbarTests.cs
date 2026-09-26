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
