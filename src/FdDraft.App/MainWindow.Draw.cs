using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        private ToolBar BuildDrawBar()
        {
            var bar = new ToolBar { Band = 3 };
            Button B(string text, string tip, Action a) { var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(6, 2, 6, 2) }; b.Click += (s, e) => a(); return b; }
            Button Fly(string text, string tip, params (string Header, string Tip, Action Run)[] items)
            {
                var b = new Button { Content = text + " ▾", ToolTip = tip, Padding = new Thickness(6, 2, 6, 2) };
                var menu = new ContextMenu();
                foreach (var it in items)
                {
                    var m = new MenuItem { Header = it.Header, ToolTip = it.Tip };
                    var run = it.Run;
                    m.Click += (s, e) => run();
                    menu.Items.Add(m);
                }
                b.ContextMenu = menu;
                b.Click += (s, e) => { menu.PlacementTarget = b; menu.IsOpen = true; };
                return b;
            }
            bar.Items.Add(B("Line", "Draws a line (LINE) - picks, or bearing and distance", StartLine));
            bar.Items.Add(B("PLine", "Draws a polyline, including straight and arc segments (PLINE)", StartPolyline));
            bar.Items.Add(new Separator());
            bar.Items.Add(Fly("Circle", "Circles (CIRCLE)",
                ("Center-Radius", "Draws a circle given a center point and radius (CIRCLE)", () => StartCircle("")),
                ("Center-Diameter", "Draws a circle given a center point and diameter (CIRCLE D)", () => StartCircle("D")),
                ("2-Point", "Draws a circle given 2 end points of the diameter (CIRCLE 2P)", () => StartCircle("2P")),
                ("3-Point", "Draws a circle given 3 points on the circle (CIRCLE 3P)", () => StartCircle("3P")),
                ("Convert Arc to Circle", "Turns an existing arc into a complete circle (CIRCLE A)", () => StartCircle("A"))));
            bar.Items.Add(Fly("Arc", "Arcs (ARC)",
                ("3-Point", "Draws an arc through 3 points (ARC)", StartArc),
                ("Center-Start-End", "Draws an arc given center, start, and end - counter-clockwise (ARC C)", StartArcCenter)));
            bar.Items.Add(B("Ellipse", "Draws an ellipse given center and axes (ELLIPSE)", StartEllipse));
            bar.Items.Add(B("Point", "Draws a point (POINT)", StartPoint));
            bar.Items.Add(Fly("Polygon", "Rectangles and polygons",
                ("Rectangle", "Draws a rectangle (RECTANGLE)", StartRectangle),
                ("Polygon Center-Vertex", "Draws a polygon using the center point and a vertex (POLYGON)", () => StartPolygon("I")),
                ("Polygon Center-Side", "Draws a polygon using center point and distance to side (POLYGON C)", () => StartPolygon("C")),
                ("Polygon Edge", "Draws a polygon using the length and orientation of an edge (POLYGON E)", () => StartPolygon("E"))));
            bar.Items.Add(B("Cloud", "Draws a revision cloud (REVCLOUD) - pick its outline", StartRevCloud));
            bar.Items.Add(B("Donut", "Draws a donut (DONUT)", StartDonut));
            bar.Items.Add(B("Plane", "Draws a filled plane (SOLID) - 3 or 4 corners", StartSolid));
            bar.Items.Add(new Separator());
            bar.Items.Add(B("Insert", "Inserts a block already in the drawing (INSERT)", () => StartInsert("")));
            bar.Items.Add(B("Text", "Creates a single line of text (TEXT)", () => StartText()));
            bar.Items.Add(B("MText", "Creates a multiple-line block of text (MTEXT)", StartMText));
            return bar;
        }

        // ---- shared plumbing ----------------------------------------------------------------------

        /// <summary>Adds entities built on the current layer as one undo step and keeps the view.</summary>
        private void AddDrawn(string what, params Entity[] entities)
        {
            AfterPickEdit(new AddEntitiesCommand(CurrentEntityOwner(), entities, what));
        }

        private static XYZ W(Vec2 v) => new XYZ(v.X, v.Y, 0);

        private static bool Num(string s, out double v) => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

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
            _awaitingPoint = p => { var m = ModelPick(p); if (m != null) pick(m.Value, p); };
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
                        if (r > 1e-9) { AddDrawn("Circle", Make(c, r)); Log("  circle R " + F(r)); }
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
                        AddDrawn("Circle", Make(c.Value.Center, c.Value.Radius)); Log("  circle R " + F(c.Value.Radius));
                        EndTool();
                    }, s => EndTool());
                    return;
                case "A":
                    StartDrawTool("CIRCLE A", "pick an arc to close into a full circle", "Circle - pick the arc:", (m, p) =>
                    {
                        var hit = LineworkAt(RawModelPick() ?? m);
                        if (!(hit is Arc a)) { Log("  pick an arc"); return; }
                        var c = new Circle { Center = a.Center, Radius = a.Radius, Layer = a.Layer, Color = a.Color, LineType = a.LineType };
                        AfterPickEdit(new CompositeCommand(new IEditCommand[] { new RemoveEntitiesCommand(new Entity[] { a }, "Circle"), new AddEntitiesCommand(CurrentEntityOwner(), new Entity[] { c }, "Circle") }, "Arc to circle"));
                        Log("  arc closed into a circle R " + F(a.Radius) + "  (Ctrl+Z undoes it)");
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
                        if (r > 1e-9) { AddDrawn("Circle", Make(center.Value, r)); Log("  circle R " + F(r)); }
                        EndTool();
                    }, s =>
                    {
                        if (center == null || s.Length == 0) { EndTool(); return; }
                        if (!Num(s, out double v) || v <= 0) { Log("  type a " + (diameter ? "diameter" : "radius")); return; }
                        double r = diameter ? v / 2 : v;
                        AddDrawn("Circle", Make(center.Value, r)); Log("  circle R " + F(r));
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
                Log("  arc R " + F(r) + "  length " + F(r * sweep));
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
                Log("  ellipse " + F(2 * a) + " x " + F(2 * other));
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
                Log("  rectangle " + F(c[1].X - c[0].X) + " x " + F(c[2].Y - c[1].Y));
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
                    if (v.Count >= 3) { AddDrawn("Polygon", Poly(v, null, true, layer)); Log("  " + sides + "-sided polygon, side " + F(Vec2.Distance(v[0], v[1]))); }
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
                if (s.Length > 0 && Num(s, out double v) && v > 0) { arcMm = v; Log("  arcs about " + F(v) + " mm"); return; }
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

        // ---- INSERT and MTEXT ---------------------------------------------------------------------

        /// <summary>INSERT [name]: a block already defined in the drawing - type its name (? lists them), pick the point, then "scale rotation" (blank = 1 0).</summary>
        private void StartInsert(string name)
        {
            if (!NeedDrawing()) return;
            var names = _doc!.BlockRecords.Where(b => !b.Name.StartsWith("*") && !b.IsAnonymous).Select(b => b.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count == 0) { Log("  this drawing has no blocks to insert"); return; }
            string layer = CurrentLayer();
            ACadSharp.Tables.BlockRecord? block = null;
            Vec2? at = null;
            StartDrawTool("INSERT", "type the block name (? lists " + names.Count + "), pick where it goes, then \"scale rotation\" (blank = 1 0)", "Insert - block name:", (m, p) =>
            {
                if (block == null) { Log("  type the block name first (? lists them)"); return; }
                at = m; _prompt.Text = "Insert - scale and rotation in degrees <1 0>:";
                _canvas.ToolActive = false;
            }, s =>
            {
                if (block == null)
                {
                    if (s.Length == 0) { EndTool(); return; }
                    if (s.Trim() == "?") { Log("  blocks: " + string.Join(", ", names)); return; }
                    var hit = names.FirstOrDefault(n => n.Equals(s.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (hit == null) { Log("  no block \"" + s.Trim() + "\" - ? lists them"); return; }
                    block = _doc!.BlockRecords[hit];
                    _prompt.Text = "Insert " + hit + " - insertion point:";
                    return;
                }
                if (at == null) { EndTool(); return; }
                double scale = 1, rot = 0;
                var parts = s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && Num(parts[0], out double sc) && sc != 0) scale = sc;
                if (parts.Length > 1 && Num(parts[1], out double r)) rot = r * Math.PI / 180;
                // Built at the origin then transformed into place, so any attributes come along.
                var ins = new Insert(block) { Layer = GetOrCreateLayer(layer) };
                var m = Matrix4.CreateTranslation(W(at.Value)) * Matrix4.CreateRotationMatrix(new XYZ(0, 0, rot)) * Matrix4.CreateScale(scale);
                EntityTransform.Apply(ins, new Transform(m));
                AddDrawn("Insert", ins);
                Log("  " + block.Name + " inserted at " + NE(at.Value));
                EndTool();
            });
            if (name.Length > 0) _awaitingLine?.Invoke(name);
        }

        /// <summary>MTEXT: pick the top-left corner and the width (second pick), then type lines - blank finishes ("height text" on the first line sets the height).</summary>
        private void StartMText()
        {
            string layer = CurrentLayer();
            Vec2? corner = null; double width = 0;
            var lines = new List<string>();
            double height = 0.2;
            StartDrawTool("MTEXT", "pick the top-left corner, then the right edge (sets the width), then type the lines - blank finishes", "MText - top-left corner:", (m, p) =>
            {
                if (corner == null) { corner = m; _canvas.RubberFrom = p; _prompt.Text = "MText - right edge (sets the width):"; return; }
                width = Math.Abs(m.X - corner.Value.X);
                _canvas.ToolActive = false; _canvas.RubberFrom = null;
                _prompt.Text = "MText - first line (or \"height text\"):";
            }, s =>
            {
                if (corner == null) { EndTool(); return; }
                if (s.Length > 0)
                {
                    if (lines.Count == 0) { ParseHeightAndText(s, out height, out string first); lines.Add(first); }
                    else lines.Add(s);
                    _prompt.Text = "MText - next line (blank finishes):";
                    return;
                }
                if (lines.Count > 0)
                {
                    var mt = new MText { Value = string.Join("\\P", lines), InsertPoint = W(corner.Value), Height = height, RectangleWidth = width, AttachmentPoint = AttachmentPointType.TopLeft, Layer = GetOrCreateLayer(layer) };
                    AddDrawn("MText", mt);
                    Log("  " + Plural(lines.Count, "line", "lines") + " of text");
                }
                EndTool();
            });
        }
    }
}
