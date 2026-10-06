using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using ACadSharp.Entities;
using CSMath;
using FdDraft.Cad.Editing;
using FdDraft.Core.Geometry;
using FdDraft.View;
using Arc = ACadSharp.Entities.Arc;
using Line = ACadSharp.Entities.Line;
using Point = ACadSharp.Entities.Point;

namespace FdDraft.App
{
    /// <summary>
    /// MSCAD's Draw toolbar (row 3 of Marc's workspace): polyline, circles, arcs, ellipse, point,
    /// polygons and rectangle, revision cloud, donut, filled plane, block insert, text and
    /// multiline text. Every tool draws on the toolbar's current layer and is one undo step.
    /// </summary>
    public sealed partial class MainWindow
    {
        /// <summary>The Draw toolbar's commands that live here; false when the verb isn't one of them.</summary>
        private bool ExecuteDrawTool(string verb, string arg)
        {
            switch (verb)
            {
                case "PLINE": case "PL": case "POLYLINE": StartPolyline(); return true;
                case "SPLINE": case "SPL": StartSpline(); return true;
                case "CIRCLE": case "C": StartCircle(arg); return true;
                case "ARCC": StartArcCenter(); return true;
                case "ELLIPSE": case "EL": StartEllipse(); return true;
                case "POINT": case "PO": StartPoint(); return true;
                case "RECTANGLE": case "RECTANG": case "REC": StartRectangle(); return true;
                case "POLYGON": case "POL":
                {
                    var m = arg.Trim().ToUpperInvariant();
                    StartPolygon(m == "C" || m == "E" ? m : "I");
                    return true;
                }
                case "REVCLOUD": StartRevCloud(); return true;
                case "DONUT": case "DO": StartDonut(); return true;
                case "SOLID": case "SO": case "PLANE": StartSolid(); return true;
                case "HATCH": case "H": case "BHATCH": StartHatch(arg); return true;
            }
            return false;
        }

        // ---- shared plumbing ----------------------------------------------------------------------

        /// <summary>Adds entities built on the current layer as one undo step and keeps the view.</summary>
        private void AddDrawn(string what, params Entity[] entities)
        {
            Commit(new AddEntitiesCommand(CurrentEntityOwner(), entities, what), "");
        }

        private static XYZ W(Vec2 v) => new XYZ(v.X, v.Y, 0);

        private static bool Num(string s, out double v) => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        private static string Fm(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private LwPolyline Poly(IList<Vec2> pts, IList<double>? bulges, bool closed, string layer)
        {
            var pl = new LwPolyline { IsClosed = closed, Layer = GetOrCreateLayer(layer) };
            for (int i = 0; i < pts.Count; i++)
                pl.Vertices.Add(new LwPolyline.Vertex(new XY(pts[i].X, pts[i].Y)) { Bulge = bulges != null && i < bulges.Count ? bulges[i] : 0 });
            return pl;
        }

        /// <summary>Starts a pick tool: a model point per pick, typed lines to <paramref name="typed"/>.</summary>
        private bool StartDrawTool(string name, string intro, string prompt, Action<Vec2, Vec2> pick, Action<string> typed)
        {
            if (!NeedDrawing()) return false;
            if (_activeTool.Length > 0) EndTool();
            BeginTool(name);
            Log(name + "  on layer " + CurrentLayer() + " - " + intro);
            _prompt.Text = prompt;
            _awaitingPoint = p => { var m = ModelOf(p); if (m != null) pick(m.Value, p); };
            _awaitingLine = typed;
            return true;
        }

        // ---- PLINE --------------------------------------------------------------------------------

        /// <summary>PLINE: pick vertices; A = the next span is an arc (pick a point on it, then its end), L = back to straight, C closes, U undoes, blank ends.</summary>
        private void StartPolyline()
        {
            var pts = new List<Vec2>(); var bulges = new List<double>();
            bool arcMode = false; Vec2? through = null;
            string layer = CurrentLayer();
            void Prompt() => _prompt.Text = pts.Count == 0 ? "Polyline - start point:" : arcMode ? (through == null ? "Polyline arc - a point on the arc:" : "Polyline arc - its end:") : "Polyline - next point (A arc, C close, U undo, blank ends):";
            void Finish(bool close)
            {
                if (pts.Count >= 2)
                {
                    if (!close) bulges[bulges.Count - 1] = 0;
                    AddDrawn("Polyline", Poly(pts, bulges, close, layer));
                    Log("  polyline, " + Plural(pts.Count, "vertex", "vertices") + (close ? ", closed" : ""));
                }
                EndTool();
            }
            StartDrawTool("PLINE", "pick vertices; type A for an arc span, L for straight again, C to close, U to undo the last, blank to end", "Polyline - start point:",
                (m, p) =>
                {
                    if (pts.Count == 0) { pts.Add(m); bulges.Add(0); _canvas.RubberFrom = p; Prompt(); return; }
                    if (arcMode && through == null) { through = m; Prompt(); return; }
                    if (arcMode)
                    {
                        var arc = FdDraft.Core.Geometry.Arc.ThroughThreePoints(pts[pts.Count - 1], through!.Value, m);
                        bulges[bulges.Count - 1] = arc?.Bulge ?? 0;
                        through = null;
                    }
                    pts.Add(m); bulges.Add(0); _canvas.RubberFrom = p; Prompt();
                },
                s =>
                {
                    switch (s.Trim().ToUpperInvariant())
                    {
                        case "": Finish(false); return;
                        case "C": if (pts.Count >= 3) Finish(true); else Log("  needs three points to close"); return;
                        case "A": arcMode = true; through = null; Prompt(); return;
                        case "L": arcMode = false; through = null; Prompt(); return;
                        case "U":
                            if (pts.Count > 0) { pts.RemoveAt(pts.Count - 1); bulges.RemoveAt(bulges.Count - 1); if (bulges.Count > 0) bulges[bulges.Count - 1] = 0; }
                            through = null; _canvas.RubberFrom = null; Prompt(); return;
                        default: Log("  A arc, L line, C close, U undo, or blank to end"); return;
                    }
                });
        }

        /// <summary>SPLINE: a smooth curve through the picks (a fit-point SPLINE, as AutoCAD/MSCAD draw it); C closes, U undoes, blank ends.</summary>
        private void StartSpline()
        {
            string layer = CurrentLayer();
            var pts = new List<Vec2>();
            void Finish(bool closed)
            {
                if (pts.Count >= 2)
                {
                    var sp = SplineEditing.Through(pts, closed);
                    sp.Layer = GetOrCreateLayer(layer);
                    AddDrawn("Spline", sp);
                    Log("  spline through " + Plural(pts.Count, "point", "points") + (closed ? ", closed" : ""));
                }
                EndTool();
            }
            StartDrawTool("SPLINE", "pick the points it passes through; C closes, U undoes the last, blank ends", "Spline - first point:", (m, p) =>
            {
                pts.Add(m); _canvas.RubberFrom = p;
                _prompt.Text = "Spline - next point (C close, U undo, blank ends):";
            }, s =>
            {
                switch (s.Trim().ToUpperInvariant())
                {
                    case "": Finish(false); return;
                    case "C": if (pts.Count >= 3) Finish(true); else Log("  needs three points to close"); return;
                    case "U": if (pts.Count > 0) pts.RemoveAt(pts.Count - 1); _canvas.RubberFrom = null; return;
                    default: Log("  C close, U undo, or blank to end"); return;
                }
            });
        }

        // ---- circles, arcs, ellipse, point --------------------------------------------------------

        private void StartCircle(string mode)
        {
            string layer = CurrentLayer();
            var pts = new List<Vec2>();
            Circle Make(Vec2 c, double r) => new Circle { Center = W(c), Radius = r, Layer = GetOrCreateLayer(layer) };
            switch (mode.ToUpperInvariant())
            {
                case "2P":
                    StartDrawTool("CIRCLE 2P", "pick both ends of a diameter", "Circle - first end of the diameter:", (m, p) =>
                    {
                        pts.Add(m);
                        if (pts.Count == 1) { _canvas.RubberFrom = p; _prompt.Text = "Circle - other end:"; return; }
                        var (c, r) = Shapes.CircleTwoPoints(pts[0], pts[1]);
                        if (r > 1e-9) { AddDrawn("Circle", Make(c, r)); Log("  circle R " + Fm(r)); }
                        EndTool();
                    }, s => EndTool());
                    return;
                case "3P":
                    StartDrawTool("CIRCLE 3P", "pick three points on the circle", "Circle - first point:", (m, p) =>
                    {
                        pts.Add(m);
                        if (pts.Count < 3) { _prompt.Text = pts.Count == 1 ? "Circle - second point:" : "Circle - third point:"; return; }
                        var c = Shapes.CircleThreePoints(pts[0], pts[1], pts[2]);
                        if (c == null) { Log("  those points are in line - pick again"); pts.Clear(); _prompt.Text = "Circle - first point:"; return; }
                        AddDrawn("Circle", Make(c.Value.Center, c.Value.Radius)); Log("  circle R " + Fm(c.Value.Radius));
                        EndTool();
                    }, s => EndTool());
                    return;
                case "A":
                    StartDrawTool("CIRCLE A", "pick an arc to close into a full circle", "Circle - pick the arc:", (m, p) =>
                    {
                        var hit = PickEntity(p, x => x is Arc);
                        if (!(hit is Arc a)) { Log("  pick an arc"); return; }
                        var c = new Circle { Center = a.Center, Radius = a.Radius, Layer = a.Layer, Color = a.Color, LineType = a.LineType };
                        Commit(new CompositeCommand(new IEditCommand[] { new RemoveEntitiesCommand(new Entity[] { a }, "Circle"), new AddEntitiesCommand(CurrentEntityOwner(), new Entity[] { c }, "Circle") }, "Arc to circle"), "  arc closed into a circle R " + Fm(a.Radius) + "  (Ctrl+Z undoes it)");
                    }, s => EndTool());
                    return;
                default:
                {
                    bool diameter = mode.Equals("D", StringComparison.OrdinalIgnoreCase);
                    Vec2? center = null;
                    StartDrawTool(diameter ? "CIRCLE D" : "CIRCLE", "pick the center, then " + (diameter ? "type the diameter" : "pick a point on the circle or type the radius"), "Circle - center:", (m, p) =>
                    {
                        if (center == null) { center = m; _canvas.RubberFrom = p; _prompt.Text = diameter ? "Circle - diameter:" : "Circle - radius (pick or type):"; return; }
                        double r = Vec2.Distance(center.Value, m) / (diameter ? 2 : 1);
                        if (r > 1e-9) { AddDrawn("Circle", Make(center.Value, r)); Log("  circle R " + Fm(r)); }
                        EndTool();
                    }, s =>
                    {
                        if (center == null || s.Length == 0) { EndTool(); return; }
                        if (!Num(s, out double v) || v <= 0) { Log("  type a " + (diameter ? "diameter" : "radius")); return; }
                        double r = diameter ? v / 2 : v;
                        AddDrawn("Circle", Make(center.Value, r)); Log("  circle R " + Fm(r));
                        EndTool();
                    });
                    return;
                }
            }
        }

        /// <summary>ARC C: center, start, end - counter-clockwise from start to end, as MSCAD/AutoCAD draw it.</summary>
        private void StartArcCenter()
        {
            string layer = CurrentLayer();
            var pts = new List<Vec2>();
            StartDrawTool("ARC C", "pick the center, the start, then the end (counter-clockwise)", "Arc - center:", (m, p) =>
            {
                pts.Add(m);
                if (pts.Count == 1) { _canvas.RubberFrom = p; _prompt.Text = "Arc - start point:"; return; }
                if (pts.Count == 2) { _prompt.Text = "Arc - end point (counter-clockwise):"; return; }
                var c = pts[0]; double r = Vec2.Distance(c, pts[1]);
                if (r < 1e-9) { EndTool(); return; }
                double a0 = Math.Atan2(pts[1].Y - c.Y, pts[1].X - c.X), a1 = Math.Atan2(pts[2].Y - c.Y, pts[2].X - c.X);
                if (a0 < 0) a0 += 2 * Math.PI;
                if (a1 < 0) a1 += 2 * Math.PI;
                AddDrawn("Arc", new Arc { Center = W(c), Radius = r, StartAngle = a0, EndAngle = a1, Layer = GetOrCreateLayer(layer) });
                double sweep = a1 - a0; if (sweep <= 0) sweep += 2 * Math.PI;
                Log("  arc R " + Fm(r) + "  length " + Fm(r * sweep));
                EndTool();
            }, s => EndTool());
        }

        /// <summary>ELLIPSE: center, the end of one axis, then the other axis's half-length (pick or type).</summary>
        private void StartEllipse()
        {
            string layer = CurrentLayer();
            Vec2? center = null, axis = null;
            void Make(double other)
            {
                var major = axis!.Value - center!.Value;
                double a = major.Length;
                if (a < 1e-9 || other <= 1e-9) { EndTool(); return; }
                // ACadSharp wants the major axis the longer one: swap if the other is longer.
                Vec2 majorVec = major; double ratio = other / a;
                if (ratio > 1) { majorVec = major.Normalized().Left() * other; ratio = a / other; }
                AddDrawn("Ellipse", new Ellipse { Center = W(center.Value), MajorAxisEndPoint = new XYZ(majorVec.X, majorVec.Y, 0), RadiusRatio = ratio, StartParameter = 0, EndParameter = 2 * Math.PI, Layer = GetOrCreateLayer(layer) });
                Log("  ellipse " + Fm(2 * a) + " x " + Fm(2 * other));
                EndTool();
            }
            StartDrawTool("ELLIPSE", "pick the center, the end of one axis, then the other half-axis (pick or type)", "Ellipse - center:", (m, p) =>
            {
                if (center == null) { center = m; _canvas.RubberFrom = p; _prompt.Text = "Ellipse - end of the first axis:"; return; }
                if (axis == null) { axis = m; _prompt.Text = "Ellipse - other half-axis (pick or type):"; return; }
                var u = (axis.Value - center.Value).Normalized();
                Make(Math.Abs(Vec2.Cross(u, m - center.Value)));
            }, s =>
            {
                if (axis == null || s.Length == 0) { EndTool(); return; }
                if (Num(s, out double v) && v > 0) Make(v); else Log("  type the other half-axis length");
            });
        }

        /// <summary>POINT: a point entity at each pick; blank ends.</summary>
        private void StartPoint()
        {
            string layer = CurrentLayer();
            int n = 0;
            StartDrawTool("POINT", "pick each point (blank or Esc ends)", "Point - location:", (m, p) =>
            {
                AddDrawn("Point", new Point(W(m)) { Layer = GetOrCreateLayer(layer) });
                n++;
                Log("  point " + NE(m));
            }, s => { EndTool(); Log("  *" + Plural(n, "point", "points") + "*"); });
        }

        // ---- rectangles, polygons, cloud, donut, plane --------------------------------------------

        private void StartRectangle()
        {
            string layer = CurrentLayer();
            Vec2? first = null;
            StartDrawTool("RECTANGLE", "pick two opposite corners", "Rectangle - first corner:", (m, p) =>
            {
                if (first == null) { first = m; _canvas.RubberFrom = p; _prompt.Text = "Rectangle - opposite corner:"; return; }
                var c = Shapes.Rectangle(first.Value, m);
                AddDrawn("Rectangle", Poly(c, null, true, layer));
                Log("  rectangle " + Fm(c[1].X - c[0].X) + " x " + Fm(c[2].Y - c[1].Y));
                EndTool();
            }, s => EndTool());
        }

        /// <summary>POLYGON: type the number of sides, then center + vertex (I), center + side midpoint (C), or the two ends of an edge (E).</summary>
        private void StartPolygon(string mode)
        {
            string layer = CurrentLayer();
            int sides = 0;
            Vec2? first = null;
            string second = mode == "E" ? "Polygon - other end of the edge:" : mode == "C" ? "Polygon - midpoint of a side:" : "Polygon - a vertex:";
            StartDrawTool(mode == "E" ? "POLYGON E" : mode == "C" ? "POLYGON C" : "POLYGON", "type the number of sides, then " + (mode == "E" ? "pick both ends of one edge" : "pick the center and " + (mode == "C" ? "the midpoint of a side" : "a vertex")),
                "Polygon - number of sides <4>:", (m, p) =>
                {
                    if (sides == 0) sides = 4;
                    if (first == null) { first = m; _canvas.RubberFrom = p; _prompt.Text = second; return; }
                    var v = mode == "E" ? Shapes.PolygonOnEdge(sides, first.Value, m) : Shapes.RegularPolygon(sides, first.Value, m, mode != "C");
                    if (v.Count >= 3) { AddDrawn("Polygon", Poly(v, null, true, layer)); Log("  " + sides + "-sided polygon, side " + Fm(Vec2.Distance(v[0], v[1]))); }
                    EndTool();
                }, s =>
                {
                    if (sides == 0)
                    {
                        if (s.Length == 0) sides = 4;
                        else if (int.TryParse(s.Trim(), out int n) && n >= 3 && n <= 1024) sides = n;
                        else { Log("  type 3 or more sides"); return; }
                        _prompt.Text = mode == "E" ? "Polygon - first end of an edge:" : "Polygon - center:";
                        return;
                    }
                    EndTool();
                });
        }

        /// <summary>REVCLOUD: pick the outline's corners, blank closes it; arcs about 10 mm on paper (type a number first to change).</summary>
        private void StartRevCloud()
        {
            string layer = CurrentLayer();
            var std = LabelStandards();
            double mpm = LabelModelPerMm(std, out _);
            double arcMm = 10;
            var pts = new List<Vec2>();
            StartDrawTool("REVCLOUD", "pick the outline's corners (blank closes it); type a number for the arc length in paper mm (now 10)", "Cloud - first corner:", (m, p) =>
            {
                pts.Add(m); _canvas.RubberFrom = p;
                _prompt.Text = "Cloud - next corner (blank closes):";
            }, s =>
            {
                if (s.Length > 0 && Num(s, out double v) && v > 0) { arcMm = v; Log("  arcs about " + Fm(v) + " mm"); return; }
                if (pts.Count >= 2)
                {
                    var outline = pts.Count == 2 ? Shapes.Rectangle(pts[0], pts[1]) : pts;
                    var (v2, b) = Shapes.RevisionCloud(outline, arcMm * mpm);
                    AddDrawn("Revision cloud", Poly(v2, b, true, layer));
                    Log("  revision cloud, " + v2.Count + " arcs");
                }
                EndTool();
            });
        }

        /// <summary>DONUT: inside and outside diameters, then a donut at each pick (a wide closed polyline of two half-circles).</summary>
        private void StartDonut()
        {
            string layer = CurrentLayer();
            double inside = -1, outside = -1;
            StartDrawTool("DONUT", "type the inside diameter, the outside diameter, then pick each center (blank ends)", "Donut - inside diameter <0.5>:", (m, p) =>
            {
                if (inside < 0) inside = 0.5;
                if (outside < 0) outside = 1.0;
                double r = (inside + outside) / 4, w = (outside - inside) / 2;
                var pl = new LwPolyline { IsClosed = true, ConstantWidth = w, Layer = GetOrCreateLayer(layer) };
                pl.Vertices.Add(new LwPolyline.Vertex(new XY(m.X - r, m.Y)) { Bulge = 1 });
                pl.Vertices.Add(new LwPolyline.Vertex(new XY(m.X + r, m.Y)) { Bulge = 1 });
                AddDrawn("Donut", pl);
            }, s =>
            {
                if (inside < 0) { inside = s.Length == 0 ? 0.5 : Num(s, out double a) && a >= 0 ? a : 0.5; _prompt.Text = "Donut - outside diameter <1>:"; return; }
                if (outside < 0)
                {
                    outside = s.Length == 0 ? 1.0 : Num(s, out double b) && b > inside ? b : Math.Max(1.0, inside * 2);
                    _prompt.Text = "Donut - center (blank ends):";
                    return;
                }
                EndTool();
            });
        }

        /// <summary>SOLID: a filled plane through 3 or 4 picked corners (blank after 3 makes a triangle).</summary>
        private void StartSolid()
        {
            string layer = CurrentLayer();
            var pts = new List<Vec2>();
            void Make()
            {
                // SOLID corners zig-zag: picks 1-2-3-4 round the outline are stored 1, 2, 4, 3.
                var c4 = pts.Count == 4 ? pts[3] : pts[2];
                AddDrawn("Plane", new Solid { FirstCorner = W(pts[0]), SecondCorner = W(pts[1]), ThirdCorner = W(c4), FourthCorner = W(pts[2]), Layer = GetOrCreateLayer(layer) });
                EndTool();
            }
            StartDrawTool("SOLID", "pick the corners round the outline: 3 (then blank) or 4", "Plane - first corner:", (m, p) =>
            {
                pts.Add(m);
                if (pts.Count == 4) { Make(); return; }
                _prompt.Text = pts.Count < 3 ? "Plane - next corner:" : "Plane - fourth corner (blank for a triangle):";
            }, s => { if (pts.Count >= 3) Make(); else EndTool(); });
        }

        /// <summary>
        /// HATCH S|L|X: pick inside closed areas (polylines, circles) - each becomes a hatch of
        /// the smallest area around the pick, its islands left clear. Lines are spaced in paper mm
        /// at the sheet's scale; type "spacing angle" first to change them (2 mm, 45°).
        /// </summary>
        private void StartHatch(string mode)
        {
            mode = mode.Length == 0 ? "S" : mode.Substring(0, 1).ToUpperInvariant();
            if (mode != "S" && mode != "L" && mode != "X") mode = "S";
            string layer = CurrentLayer();
            var std = LabelStandards();
            double mpm = LabelModelPerMm(std, out string basis);
            double spacingMm = 2, angleDeg = 45;
            int n = 0;
            string kind = mode == "S" ? "solid" : mode == "L" ? "lines" : "crossed lines";
            StartDrawTool("HATCH", kind + " - pick inside each area (blank ends)" + (mode == "S" ? "" : "; type \"spacing angle\" to change from 2 mm at 45° (scale from " + basis + ")"), "Hatch - pick inside an area:", (m, p) =>
            {
                var raw = _canvas.Scene?.ModelAt(_canvas.LastRawPick) ?? m; // where it was clicked, not the snap
                var loops = HatchEditing.BoundaryAt(raw, CurrentEntityOwner().Entities);
                if (loops == null) { Log("  no closed polyline or circle around that point"); return; }
                var h = HatchEditing.Create(loops, mode == "S" ? 0 : spacingMm * mpm, angleDeg * Math.PI / 180, mode == "X", GetOrCreateLayer(layer));
                AddDrawn("Hatch", h);
                n++;
                Log("  hatched (" + kind + (loops.Count > 1 ? ", " + Plural(loops.Count - 1, "island", "islands") + " left clear" : "") + ")  (Ctrl+Z undoes it)");
            }, s =>
            {
                if (s.Length == 0) { EndTool(); if (n > 0) Log("  *" + Plural(n, "hatch", "hatches") + "*"); return; }
                var parts = s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (mode != "S" && parts.Length >= 1 && Num(parts[0], out double sp) && sp > 0)
                {
                    spacingMm = sp;
                    if (parts.Length > 1 && Num(parts[1], out double an)) angleDeg = an;
                    Log("  lines every " + Fm(spacingMm) + " mm at " + Fm(angleDeg) + "°");
                }
                else Log("  pick inside an area" + (mode == "S" ? "" : ", or type spacing (mm) and angle"));
            });
        }

        // ---- INSERT and MTEXT ---------------------------------------------------------------------

        // ---- FROM / M2P point modifiers -----------------------------------------------------------

        /// <summary>
        /// FROM (pick a base point, then type an offset "dx,dy" or "bearing distance") and M2P /
        /// MTP (the midpoint of two picks), typed while a tool waits for a point: the point they
        /// make goes to the tool as if it had been picked. True when <paramref name="verb"/> was one.
        /// </summary>
        private bool TryPointModifier(string verb)
        {
            if (verb != "FROM" && verb != "'FROM" && verb != "M2P" && verb != "MTP") return false;
            var target = _awaitingPoint!; var line = _awaitingLine; var prompt = _prompt.Text;
            var canvasState = (_canvas.ToolActive, _canvas.RubberFrom);
            void Restore() { _awaitingPoint = target; _awaitingLine = line; _prompt.Text = prompt; (_canvas.ToolActive, _canvas.RubberFrom) = canvasState; }
            void Deliver(Vec2 scene) { Restore(); _canvas.SetRawPick(scene); target(scene); }
            _canvas.ToolActive = true;
            if (verb == "M2P" || verb == "MTP")
            {
                Vec2? first = null;
                _prompt.Text = "Mid between 2 points - first point:";
                _awaitingPoint = p => { if (first == null) { first = p; _prompt.Text = "Mid between 2 points - second point:"; } else Deliver((first.Value + p) * 0.5); };
                _awaitingLine = s => Restore();
                return true;
            }
            Vec2? basePt = null;
            _prompt.Text = "From - base point:";
            _awaitingPoint = p => { basePt = p; _canvas.ToolActive = false; _prompt.Text = "From - offset (dx,dy or bearing distance):"; };
            _awaitingLine = s =>
            {
                if (basePt == null || s.Length == 0) { Restore(); return; }
                double dx, dy;
                var xy = s.TrimStart('@').Split(',');
                if (xy.Length == 2 && Num(xy[0], out dx) && Num(xy[1], out dy)) { }
                else if (Cogo.TryParseLeg(s, out double az, out double dist)) { dx = dist * Math.Sin(az); dy = dist * Math.Cos(az); }
                else { Log("  type dx,dy (east, north) or a bearing and distance, e.g. N45-30-00E 12.5"); return; }
                var at = SceneOffset(basePt.Value, dx, dy);
                if (at == null) { Log("  that offset lands outside the viewport"); return; }
                Deliver(at.Value);
            };
            return true;
        }

        /// <summary>The scene point dx, dy drawing units (east, north) from a scene point - through a viewport's scale and turn on a sheet.</summary>
        private Vec2? SceneOffset(Vec2 baseScene, double dx, double dy)
        {
            var sc = _canvas.Scene!;
            if (!sc.IsPaper) return baseScene + new Vec2(dx, dy);
            var m0 = sc.ModelAt(baseScene);
            if (m0 == null) return null;
            double h = 1e-3;
            Vec2? Step(Vec2 d, out double sign)
            {
                sign = 1;
                var m = sc.ModelAt(baseScene + d);
                if (m == null) { sign = -1; m = sc.ModelAt(baseScene - d); }
                return m;
            }
            var mx = Step(new Vec2(h, 0), out double sx); var my = Step(new Vec2(0, h), out double sy);
            if (mx == null || my == null) return null;
            var a = (mx.Value - m0.Value) * (sx / h); var b = (my.Value - m0.Value) * (sy / h);
            double det = Vec2.Cross(a, b);
            if (Math.Abs(det) < 1e-18) return null;
            var d = new Vec2(dx, dy);
            return baseScene + new Vec2(Vec2.Cross(d, b) / det, Vec2.Cross(a, d) / det);
        }
    }
}
