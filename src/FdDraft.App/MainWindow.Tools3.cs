using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using CSMath;
using FdDraft.Cad;
using FdDraft.Cad.Editing;
using FdDraft.Core.Geometry;
using FdDraft.Core.Job;
using Microsoft.Win32;
using Arc = ACadSharp.Entities.Arc;
using Line = ACadSharp.Entities.Line;
using Point = ACadSharp.Entities.Point;

namespace FdDraft.App
{
    /// <summary>FD Main Control, FD Calcs and FD Coordinate commands.</summary>
    public sealed partial class MainWindow
    {
        // =============================================================================================
        // Main control
        // =============================================================================================

        private void Launch(string target, string what)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); Log("  opened " + what); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException) { Log("  couldn't open " + what + ": " + ex.Message); }
        }

        private void OpenStandardsFile()
        {
            var path = _settings.StandardsPath;
            if (path.Length == 0 || !File.Exists(path)) { Log("  no firm standards file chosen yet - pick one in the Draft dialog (Ctrl+D); FD-Draft is using its built-in defaults"); return; }
            Launch(path, "the firm standards file " + Path.GetFileName(path) + " (re-draft or reopen to pick up changes)");
            _std = null;
        }

        private void OpenLogFile()
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "FD-Draft command log.txt");
                File.WriteAllText(path, _history.Text);
                Launch(path, "the command history");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Log("  couldn't write the log: " + ex.Message); }
        }

        /// <summary>INFO: what a picked line, curve, polyline span, text, dimension, block or point is.</summary>
        private void StartInfo()
        {
            if (!NeedDrawing()) return;
            var std = LabelStandards();
            double g2g = GridToGround(std);
            PickLoop("INFO", "Information - pick a line, curve or text:", "pick entities to report on - a line opens Line Computations (Traverse, Turned Angle, Curve Calcs); Esc ends", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                var e = PickEntity(p, _ => true);
                if (e == null) { Log("  nothing there"); return; }
                string layer = " on " + (e.Layer?.Name ?? "0");
                switch (e)
                {
                    case Line l:
                    {
                        var a = new Vec2(l.StartPoint.X, l.StartPoint.Y); var b = new Vec2(l.EndPoint.X, l.EndPoint.Y);
                        ShowLineInfo(a, b, l.StartPoint.Z, l.EndPoint.Z, "Drawing data. Line" + layer + (Math.Abs(l.StartPoint.Z - l.EndPoint.Z) < 1e-12 && Math.Abs(l.StartPoint.Z) < 1e-12 ? ", 2D." : "."));
                        break;
                    }
                    case Arc arc:
                        Log("  arc" + layer + ": " + string.Join("  ", SurveyDrafting.CurveData(SurveyDrafting.ToCore(arc), std, g2g)) + "   centre " + NE(new Vec2(arc.Center.X, arc.Center.Y)));
                        break;
                    case Circle c:
                        Log("  circle" + layer + ": R " + F(c.Radius) + "   centre " + NE(new Vec2(c.Center.X, c.Center.Y)));
                        break;
                    case LwPolyline _:
                    case Polyline2D _:
                    {
                        var spans = EntityOps.SpansOf(e).ToList();
                        var s = spans.OrderBy(x => x.DistanceAndSide(model.Value, out _)).FirstOrDefault();
                        double total = spans.Sum(x => x.IsArc ? Math.Abs(x.Sweep) * x.Radius : Vec2.Distance(x.A, x.B));
                        string span = s.IsArc
                            ? "curve " + string.Join("  ", SurveyDrafting.CurveData(new CoreArcHelper(s).Arc, std, g2g))
                            : BearingText(s.A, s.B) + "  " + F(Vec2.Distance(s.A, s.B) * g2g);
                        Log("  polyline" + layer + " (" + spans.Count + " spans, length " + F(total * g2g) + "): this span " + span);
                        if (!s.IsArc) ShowLineInfo(s.A, s.B, 0, 0, "Drawing data. Polyline span" + layer + ".");
                        break;
                    }
                    case TextEntity t:
                        Log("  text" + layer + ": \"" + t.Value + "\"  height " + F(t.Height) + "  rotation " + F(t.Rotation * 180 / Math.PI, 2) + "°  style " + (t.Style?.Name ?? "-"));
                        break;
                    case MText m:
                        Log("  multiline text" + layer + ": \"" + m.Value.Replace("\\P", " | ") + "\"  height " + F(m.Height) + "  style " + (m.Style?.Name ?? "-"));
                        break;
                    case Dimension d:
                        Log("  dimension" + layer + ": " + (d.Text ?? "") + "  measured " + F(d.Measurement) + "  style " + (d.Style?.Name ?? "-"));
                        break;
                    case Insert ins:
                        Log("  block " + ins.Block?.Name + layer + " at " + NE(new Vec2(ins.InsertPoint.X, ins.InsertPoint.Y)) + "  scale " + F(ins.XScale, 4) + "  rotation " + F(ins.Rotation * 180 / Math.PI, 2) + "°");
                        break;
                    case Point pt:
                        Log("  point" + layer + ": " + NE(new Vec2(pt.Location.X, pt.Location.Y)) + "  elev " + F(pt.Location.Z));
                        break;
                    default:
                        Log("  " + e.GetType().Name + layer);
                        break;
                }
            });
        }

        /// <summary>A polyline arc span as a core arc.</summary>
        private readonly struct CoreArcHelper
        {
            public readonly FdDraft.Core.Geometry.Arc Arc;
            public CoreArcHelper(Construct.Span s)
            {
                double start = Math.Atan2(s.A.Y - s.Center.Y, s.A.X - s.Center.X);
                Arc = new FdDraft.Core.Geometry.Arc { Center = s.Center, Radius = s.Radius, StartAngle = start, Sweep = s.Sweep, Start = s.A, End = s.B };
            }
        }

        /// <summary>A point object on the current layer.</summary>
        private Point NewPoint(Vec2 at, double z = 0) => new Point { Location = new XYZ(at.X, at.Y, z), Layer = GetOrCreateLayer(CurrentLayer()) };

        private void AddPointsToObjects()
        {
            if (!NeedDrawing()) return;
            var sel = SelectedEntities().Where(IsCourse).ToList();
            if (sel.Count == 0) { Log("  select the linework first, then ADDPOINTS"); return; }
            var pts = new List<Vec2>();
            void Add(Vec2 v) { if (!pts.Any(q => Vec2.Distance(q, v) < 1e-6)) pts.Add(v); }
            foreach (var e in sel)
                foreach (var s in EntityOps.SpansOf(e)) { Add(s.A); Add(s.B); }
            // Leave out spots that already have a point object.
            var existing = CurrentEntityOwner().Entities.OfType<Point>().Select(pt => new Vec2(pt.Location.X, pt.Location.Y)).ToList();
            var fresh = pts.Where(v => !existing.Any(q => Vec2.Distance(q, v) < 1e-6)).ToList();
            AddEntities(fresh.Select(v => (Entity)NewPoint(v)).ToList(), "Add points", "  " + Plural(fresh.Count, "point", "points") + " added at the vertices" + (fresh.Count < pts.Count ? " (" + (pts.Count - fresh.Count) + " already had one)" : "") + "  (Ctrl+Z undoes it)");
        }

        // =============================================================================================
        // Calcs
        // =============================================================================================

        private void StartPointsOnObject()
        {
            if (!NeedDrawing()) return;
            PickLoop("PTSONOBJ", "Points on object - pick a line or arc:", "pick a line, arc or polyline span, then a number of equal parts or @interval", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                var e = PickEntity(p, IsCourse);
                if (e == null) { Log("  no line or arc there"); return; }
                Func<double, Vec2> at; double length;
                // The span actually picked - straight or curved - of a line, arc or polyline.
                var spans = EntityOps.SpansOf(e).ToList();
                if (spans.Count == 0) { Log("  can't use that"); return; }
                var near = spans.OrderBy(x => x.DistanceAndSide(model.Value, out _)).First();
                if (near.IsArc)
                {
                    var a = new CoreArcHelper(near).Arc;
                    length = a.Length; at = t => a.PointAt(t);
                }
                else { var (s0, s1) = (near.A, near.B); length = Vec2.Distance(s0, s1); at = t => s0 + (s1 - s0) * t; }
                Log("  length " + F(length));
                AskText("Points - number of equal parts, or @interval:", s =>
                {
                    var t = s.Trim();
                    var ts = new List<double>();
                    if (t.StartsWith("@") && TryNumber(t.Substring(1), out double iv) && iv > 0)
                        for (double d = 0; d <= length + 1e-9; d += iv) ts.Add(d / length);
                    else if (int.TryParse(t, out int n) && n >= 1)
                        for (int i = 0; i <= n; i++) ts.Add((double)i / n);
                    else { Log("  type a count like 4, or an interval like @20"); return; }
                    var list = ts.Select(x => (Entity)NewPoint(at(x))).ToList();
                    EndTool();
                    AddEntities(list, "Points on object", "  " + Plural(list.Count, "point", "points") + " placed (node snap picks them up)  (Ctrl+Z undoes it)");
                });
            });
        }

        private void StartTurnedAngle()
        {
            if (!NeedDrawing()) return;
            Vec2? occ = null, bs = null;
            PickLoop("TURNANGLE", "Turned angle - pick the occupied point:", "pick the occupied point, the backsight, then type angle-right and distance for each point", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                if (occ == null) { occ = model; _canvas.RubberFrom = p; _prompt.Text = "Turned angle - pick the backsight:"; Log("  occupied " + NE(model.Value)); return; }
                bs = model;
                Log("  backsight " + BearingText(occ.Value, bs.Value));
                Ask();
            });
            void Ask()
            {
                AskText("Turned angle - angle right (d-m-s) and distance, blank ends:", s =>
                {
                    var parts = s.Trim().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2 || !SurveyCalcs.TryParseAngle(parts[0], out double ang) || !TryNumber(parts[1], out double dist)) { Log("  type e.g. 90-15-30 25.000"); return; }
                    var q = SurveyCalcs.TurnedAngle(occ!.Value, bs!.Value, ang, dist);
                    Commit(new AddEntitiesCommand(CurrentEntityOwner(), new Entity[] { NewPoint(q) }, "Turned angle"), "  point " + NE(q) + "  (" + BearingText(occ.Value, q) + ")");
                    Ask();
                });
            }
        }

        private void StartStationOffset()
        {
            if (!NeedDrawing()) return;
            PickLoop("STAOFF", "Station/offset - pick the line near its 0+000 end:", "pick the line near the end stationing starts from, then type station and offset (+ right)", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                var e = PickEntity(p, x => x is Line || x is LwPolyline || x is Polyline2D);
                var span = e == null ? null : StraightSpanNear(e, model.Value);
                if (span == null) { Log("  no line there"); return; }
                var (a, b) = span.Value;
                if (Vec2.Distance(model.Value, b) < Vec2.Distance(model.Value, a)) (a, b) = (b, a);
                Log("  stationing from " + NE(a) + " along " + BearingText(a, b) + ", length " + F(Vec2.Distance(a, b)));
                void Ask()
                {
                    AskText("Station offset (offset + right, - left), blank ends:", s =>
                    {
                        var parts = s.Trim().Replace("+", "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length != 2 || !TryNumber(parts[0], out double sta) || !TryNumber(parts[1], out double off)) { Log("  type e.g. 50.00 -3.25"); return; }
                        var q = SurveyCalcs.StationOffset(a, b, sta, off);
                        Commit(new AddEntitiesCommand(CurrentEntityOwner(), new Entity[] { NewPoint(q) }, "Station/offset"), "  point " + NE(q));
                        Ask();
                    });
                }
                Ask();
            });
        }

        private void StartTangentLine()
        {
            if (!NeedDrawing()) return;
            Vec2? from = null;
            PickLoop("TANLINE", "Tangent line - pick the start point:", "pick the start point, then the curve near the side to touch it", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                if (from == null) { from = model; _canvas.RubberFrom = p; _prompt.Text = "Tangent line - pick the curve:"; return; }
                var e = PickEntity(p, x => x is Circle || x is LwPolyline || x is Polyline2D, 14);
                var arc = e == null ? null : ArcNear(e, model.Value);
                if (arc == null) { Log("  no curve there"); return; }
                var tps = SurveyCalcs.TangentPoints(from.Value, arc.Center, arc.Radius);
                if (tps.Count == 0) { Log("  the start point is inside the circle - no tangent from there"); return; }
                var t = tps.OrderBy(q => Vec2.Distance(q, model.Value)).First();
                var f = from.Value;
                from = null; _canvas.RubberFrom = null; _prompt.Text = "Tangent line - pick the start point:";
                AddEntities(new List<Entity> { new Line(new XYZ(f.X, f.Y, 0), new XYZ(t.X, t.Y, 0)) { Layer = GetOrCreateLayer(CurrentLayer()) } }, "Tangent line",
                    "  tangent " + BearingText(f, t) + "  " + F(Vec2.Distance(f, t)) + " to " + NE(t));
            });
        }

        private void JoinByDescription(string arg)
        {
            if (!NeedDrawing()) return;
            if (_job == null) { Log("  draft an FD-Pro job first - its points are what get joined"); return; }
            void Run(string code)
            {
                EndTool();
                var pts = _job!.Points.Where(q => q.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase)).OrderBy(q => q.Id).ToList();
                if (pts.Count < 2) { Log("  " + pts.Count + " point(s) coded " + code + " - need two or more"); return; }
                var pl = new LwPolyline { Layer = GetOrCreateLayer(CurrentLayer()) };
                foreach (var q in pts) pl.Vertices.Add(new LwPolyline.Vertex(new XY(q.Easting, q.Northing)));
                AddEntities(new List<Entity> { pl }, "Connect " + code, "  joined " + pts.Count + " points coded " + code + " (" + pts.First().Id + " to " + pts.Last().Id + ")  (Ctrl+Z undoes it)");
            }
            if (arg.Length > 0) { Run(arg); return; }
            var codes = _job.Points.GroupBy(q => q.Code, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key + " (" + g.Count() + ")");
            Log("  codes: " + string.Join("  ", codes));
            BeginTool("JOINDESC");
            AskText("Connect by description - code:", Run);
        }

        /// <summary>Where the selected point-like entities sit: point objects, blocks, and point-tagged labels' points.</summary>
        private List<Vec2> SelectedPointLocations()
        {
            var list = new List<Vec2>();
            foreach (var e in SelectedEntities())
            {
                Vec2? v = e switch
                {
                    Point pt => new Vec2(pt.Location.X, pt.Location.Y),
                    Insert ins => new Vec2(ins.InsertPoint.X, ins.InsertPoint.Y),
                    _ => _job != null && PointLinks.Find(e, _job.Points) is SurveyPoint sp ? new Vec2(sp.Easting, sp.Northing) : (Vec2?)null,
                };
                if (v != null && !list.Any(q => Vec2.Distance(q, v.Value) < 1e-6)) list.Add(v.Value);
            }
            return list;
        }

        private void BestFit(bool curve)
        {
            if (!NeedDrawing()) return;
            var pts = SelectedPointLocations();
            if (pts.Count < (curve ? 3 : 2)) { Log("  select " + (curve ? "three" : "two") + " or more points (point objects or symbols) first"); return; }
            var layer = GetOrCreateLayer(CurrentLayer());
            if (!curve)
            {
                var r = SurveyCalcs.BestFitLine(pts);
                if (r == null) { Log("  those points don't define a line"); return; }
                var (a, b, rms) = r.Value;
                AddEntities(new List<Entity> { new Line(new XYZ(a.X, a.Y, 0), new XYZ(b.X, b.Y, 0)) { Layer = layer } }, "Best fit line",
                    "  best fit through " + pts.Count + " points: " + BearingText(a, b) + "  " + F(Vec2.Distance(a, b)) + ", RMS offset " + F(rms));
                return;
            }
            // Order the points along the curve first: by angle about their fitted centre.
            var fit = SurveyCalcs.BestFitArc(pts);
            if (fit == null) { Log("  those points are in a line - use Best Fit a Line"); return; }
            var c = fit.Value.Arc.Center;
            var ordered = pts.OrderBy(q => Math.Atan2(q.Y - c.Y, q.X - c.X)).ToList();
            // Start the ordering at the widest gap so it runs round the actual arc.
            int gapAt = 0; double gap = -1;
            for (int i = 0; i < ordered.Count; i++)
            {
                double a0 = Math.Atan2(ordered[i].Y - c.Y, ordered[i].X - c.X), a1 = Math.Atan2(ordered[(i + 1) % ordered.Count].Y - c.Y, ordered[(i + 1) % ordered.Count].X - c.X);
                double g = Angles.Normalize2Pi(a1 - a0);
                if (g > gap) { gap = g; gapAt = (i + 1) % ordered.Count; }
            }
            ordered = ordered.Skip(gapAt).Concat(ordered.Take(gapAt)).ToList();
            var refit = SurveyCalcs.BestFitArc(ordered)!.Value;
            AddEntities(new List<Entity> { SurveyDrafting.ToEntity(refit.Arc, layer) }, "Best fit curve",
                "  best fit through " + pts.Count + " points: R " + F(refit.Arc.Radius) + ", arc " + F(refit.Arc.Length) + ", RMS offset " + F(refit.RmsOffset));
        }

        private void StartCurveCalc(string arg)
        {
            void Solve(string s)
            {
                var known = SurveyCalcs.ParseCurveInput(s);
                var r = known == null ? null : SurveyCalcs.SolveCurve(known);
                if (r == null) { Log("  give two of R, L, D (delta), C, T, E, M - e.g. R=100 D=45-00-00 or L=78.54 C=76.54"); return; }
                foreach (var line in r.Report(Decimals())) Log("  " + line);
            }
            if (arg.Length > 0) { Solve(arg); return; }
            if (_activeTool.Length > 0) EndTool();
            BeginTool("CURVECALC");
            Log("CURVECALC  type any two of R (radius), L (arc), D (delta, d-m-s), C (chord), T (tangent), E (external), M (middle ordinate); blank ends");
            void Ask() => AskText("Curve - two elements, e.g. R=100 D=45-00-00:", s => { Solve(s); Ask(); });
            Ask();
        }

        private void StartCurveOffTangent()
        {
            if (!NeedDrawing()) return;
            PickLoop("CURVETAN", "Curve off tangent - pick the line near the end the curve leaves from:", "pick a line near its end, type radius and arc length, then pick the side it turns to", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                var e = PickEntity(p, x => x is Line || x is LwPolyline || x is Polyline2D);
                var span = e == null ? null : StraightSpanNear(e, model.Value);
                if (span == null) { Log("  no line there"); return; }
                var (a, b) = span.Value;
                if (Vec2.Distance(model.Value, a) < Vec2.Distance(model.Value, b)) (a, b) = (b, a); // the curve leaves from b, heading a→b
                Log("  leaving " + NE(b) + " on " + BearingText(a, b));
                AskText("Curve - radius and arc length:", s =>
                {
                    var parts = s.Trim().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2 || !TryNumber(parts[0], out double r) || !TryNumber(parts[1], out double len) || r <= 0 || len <= 0) { Log("  type e.g. 150 45.5"); return; }
                    _canvas.ToolActive = true;
                    _prompt.Text = "Curve - pick the side it turns toward:";
                    _awaitingLine = null;
                    _awaitingPoint = q =>
                    {
                        var m = ModelOf(q);
                        if (m == null) return;
                        bool left = Vec2.Cross(b - a, m.Value - b) > 0;
                        var arc = SurveyCalcs.CurveOffTangent(b, b - a, r, len, left);
                        EndTool();
                        AddEntities(new List<Entity> { SurveyDrafting.ToEntity(arc, GetOrCreateLayer(CurrentLayer())) }, "Curve off tangent",
                            "  curve R " + F(r) + " arc " + F(len) + " Δ " + SurveyCalcs.Dms(len / r) + ", ends at " + NE(arc.End));
                    };
                });
            });
        }

        // =============================================================================================
        // Coordinate
        // =============================================================================================

        private void ExportPoints()
        {
            if (!NeedDrawing()) return;
            var rows = new List<string>();
            var sel = SelectedEntities().OfType<Point>().ToList();
            if (sel.Count > 0)
            {
                int n = 1;
                foreach (var p in sel) rows.Add(string.Join(",", (n++).ToString(CultureInfo.InvariantCulture), F(p.Location.Y), F(p.Location.X), F(p.Location.Z), p.Layer?.Name ?? ""));
            }
            else if (_job != null)
                foreach (var p in _job.Points) rows.Add(string.Join(",", p.Id.ToString(CultureInfo.InvariantCulture), F(p.Northing), F(p.Easting), F(p.Elevation), p.Code));
            if (rows.Count == 0) { Log("  nothing to export - draft a job, or select point objects"); return; }
            var dlg = new SaveFileDialog { Filter = "Point file (*.csv)|*.csv|Text (*.txt)|*.txt", FileName = (_job?.Settings.Name ?? "points") + " points.csv" };
            if (dlg.ShowDialog(this) != true) return;
            try { File.WriteAllLines(dlg.FileName, rows); Log("  " + rows.Count + " points written to " + dlg.FileName + " (P,N,E,Z,D)"); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Log("  couldn't write: " + ex.Message); }
        }

        private void ImportPoints()
        {
            if (!NeedDrawing()) return;
            var dlg = new OpenFileDialog { Filter = "Point files (*.csv;*.txt)|*.csv;*.txt|All files|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            List<string> lines;
            try { lines = File.ReadAllLines(dlg.FileName).ToList(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Log("  couldn't read: " + ex.Message); return; }
            var std = LabelStandards();
            double mpm = ModelPerMm(), h = std.PointNumberTextMm * mpm;
            var list = new List<Entity>();
            int count = 0, skipped = 0;
            foreach (var raw in lines)
            {
                var f = raw.Split(new[] { ',', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 3 || !TryNumber(f[1], out double n) || !TryNumber(f[2], out double e)) { if (raw.Trim().Length > 0) skipped++; continue; }
                double z = f.Length > 3 && TryNumber(f[3], out double zz) ? zz : 0;
                string desc = f.Length > 4 ? string.Join(" ", f.Skip(4)) : "";
                var at = new Vec2(e, n);
                list.Add(NewPoint(at, z));
                // Number up-right, description below it - right and up as the plan is seen (Surveyor View).
                var right = new Vec2(Math.Cos(-Angles.ViewTwist), Math.Sin(-Angles.ViewTwist)); var up = right.Left();
                var pn = at + right * (h * 0.6) + up * (h * 0.3); var pd = at + right * (h * 0.6) - up * (h * 1.3);
                list.Add(new TextEntity { Value = f[0], InsertPoint = new XYZ(pn.X, pn.Y, 0), Height = h, Rotation = -Angles.ViewTwist, Layer = GetOrCreateLayer(CurrentLayer()) });
                if (desc.Length > 0) list.Add(new TextEntity { Value = desc, InsertPoint = new XYZ(pd.X, pd.Y, 0), Height = h * 0.8, Rotation = -Angles.ViewTwist, Layer = GetOrCreateLayer(CurrentLayer()) });
                count++;
            }
            if (count == 0) { Log("  no P,N,E[,Z,D] rows found in " + Path.GetFileName(dlg.FileName)); return; }
            AddEntities(list, "Import points", "  " + count + " points imported from " + Path.GetFileName(dlg.FileName) + (skipped > 0 ? " (" + skipped + " lines skipped - a header?)" : "") + " onto " + CurrentLayer() + "  (ZE to see them; Ctrl+Z undoes it)");
        }

        private void DeletePoints()
        {
            if (!NeedDrawing()) return;
            var sel = SelectedEntities();
            if (sel.Count == 0) { Log("  select the points to delete first (their symbol, node or number)"); return; }
            var victims = new HashSet<Entity>(sel.Where(e => e is Point));
            if (_job != null)
            {
                var ids = new HashSet<int>(sel.Select(e => PointLinks.Find(e, _job.Points)?.Id ?? -1).Where(i => i >= 0));
                foreach (var e in sel.Select(x => x.Owner).OfType<ACadSharp.Tables.BlockRecord>().Distinct().SelectMany(b => b.Entities))
                    if (PointLinks.Find(e, _job.Points) is SurveyPoint sp && ids.Contains(sp.Id)) victims.Add(e);
            }
            if (victims.Count == 0) { Log("  no point objects in the selection"); return; }
            _canvas.Selected.Clear();
            Commit(new RemoveEntitiesCommand(victims.ToList(), "Delete points"), "  " + Plural(victims.Count, "entity", "entities") + " of the points erased from the drawing (the FD-Pro job itself is unchanged)  (Ctrl+Z undoes it)");
        }

        private void ListPoints(string arg)
        {
            if (_job == null) { Log("  draft an FD-Pro job first"); return; }
            int lo = int.MinValue, hi = int.MaxValue;
            var r = arg.Trim();
            if (r.Length > 0)
            {
                var parts = r.Split('-');
                if (!int.TryParse(parts[0], out lo)) { Log("  LISTP, or LISTP 100-150"); return; }
                hi = parts.Length > 1 && int.TryParse(parts[1], out int h2) ? h2 : lo;
            }
            var pts = _job.Points.Where(p => p.Id >= lo && p.Id <= hi).OrderBy(p => p.Id).ToList();
            Log("  " + pts.Count + " points" + (r.Length > 0 ? " in " + r : "") + ":   P   N   E   Z   D");
            foreach (var p in pts.Take(500))
                Log(string.Format(CultureInfo.InvariantCulture, "  {0,6}  {1,13:F3}  {2,13:F3}  {3,9:F3}  {4}", p.Id, p.Northing, p.Easting, p.Elevation, p.Code));
            if (pts.Count > 500) Log("  ... (first 500 shown - give a range)");
        }

        private void StartScaleSelection()
        {
            if (!NeedDrawing()) return;
            var sel = SelectedEntities();
            if (sel.Count == 0) { Log("  select what to scale first, then SCALEP"); return; }
            PickLoop("SCALEP", "Scale - pick the base point:", Plural(sel.Count, "entity", "entities") + " - pick the base point, then type the factor", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                var c = new XYZ(model.Value.X, model.Value.Y, 0);
                AskText("Scale - factor:", s =>
                {
                    if (!TryNumber(s, out double k) || k <= 0) { Log("  type a positive factor"); return; }
                    EndTool();
                    var fwd = new Transform(Matrix4.CreateScale(k, c));
                    var inv = new Transform(Matrix4.CreateScale(1 / k, c));
                    _canvas.Selected.Clear();
                    Commit(new TransformEntitiesCommand(sel, fwd, inv, "Scale"), "  scaled by " + s.Trim() + " about " + NE(model.Value) + "  (Ctrl+Z undoes it)");
                });
            });
        }

        private void ZoomPointCommand(string arg)
        {
            if (_job == null) { Log("  draft an FD-Pro job first"); return; }
            void Go(string s)
            {
                EndTool();
                if (!int.TryParse(s.Trim(), out int id)) { Log("  type a point number"); return; }
                var p = _job!.Points.FirstOrDefault(q => q.Id == id);
                if (p == null) { Log("  no point " + id); return; }
                ZoomToPoint(new PointRow(p));
            }
            if (arg.Length > 0) { Go(arg); return; }
            BeginTool("ZOOMP");
            AskText("Zoom to point number:", Go);
        }
    }
}
