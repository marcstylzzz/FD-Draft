using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ACadSharp.Entities;
using CSMath;
using FdDraft.Cad;
using FdDraft.Cad.Editing;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;
using FdDraft.Core.Standards;
using FdDraft.View;
using Microsoft.Win32;
using Arc = ACadSharp.Entities.Arc;
using Line = ACadSharp.Entities.Line;
using Point = ACadSharp.Entities.Point;

namespace FdDraft.App
{
    /// <summary>
    /// MSCAD's MS Main Control, MS Defaults and MS FieldGenius toolbars - the parts that make
    /// sense in FD-Draft: entity information, re-scale, auto points on objects, a calculator,
    /// azimuth / quadrant bearings, the firm standards, and a FieldGenius-style coordinate export.
    /// Plus the Zoom toolbar's Zoom All / Center / Object.
    /// </summary>
    public sealed partial class MainWindow
    {
        private readonly ToggleButton _azimuthButton = new ToggleButton { Content = "Azimuth", ToolTip = "Set azimuth: directions read as azimuths (0-360° from north) in INV, INFO and ID (AZ). Plan labels stay quadrant bearings.", Padding = new Thickness(6, 2, 6, 2) };
        private readonly ToggleButton _quadrantButton = new ToggleButton { Content = "Quadrant", ToolTip = "Set quadrants: directions read as quadrant bearings, N45°30'00\"E (QUAD)", Padding = new Thickness(6, 2, 6, 2) };

        private ToolBar BuildMainControlBar()
        {
            var bar = new ToolBar { Band = 3 };
            Button B(string text, string tip, Action a) { var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(6, 2, 6, 2) }; b.Click += (s, e) => a(); return b; }
            bar.Items.Add(B("Standards", "Job defaults / labeling defaults: open the firm standards file this drawing is drafted and labelled by (EDITSTD)", EditStandards));
            bar.Items.Add(B("Info", "Line / Curve / Text information (INFO) - pick entities", StartInfo));
            bar.Items.Add(B("COGO", "Command line COGO: lines by bearing and distance (LINE)", StartLine));
            bar.Items.Add(B("Points", "The coordinate database: the Points list (POINTS)", ShowPointsTab));
            bar.Items.Add(B("Re-scale", "Re-scale complete drawing, or the selection (RESCALE) - pick the base point, type the factor", StartRescale));
            bar.Items.Add(B("Auto pts", "Auto add points to objects (AUTOP) - a point at every vertex of the selected linework", AutoPointsOnObjects));
            bar.Items.Add(B("Calc", "Calculator (CAL) - type an expression, e.g. CAL 125.5*0.3048", () => { if (_activeTool.Length > 0) EndTool(); BeginTool("CAL"); AskCalc(); }));
            bar.Items.Add(new Separator());
            _azimuthButton.Checked += (s, e) => SetAzimuths(true);
            _quadrantButton.Checked += (s, e) => SetAzimuths(false);
            _azimuthButton.Unchecked += (s, e) => { if (_quadrantButton.IsChecked != true && _settings.Azimuths) _azimuthButton.IsChecked = true; };
            _quadrantButton.Unchecked += (s, e) => { if (_azimuthButton.IsChecked != true && !_settings.Azimuths) _quadrantButton.IsChecked = true; };
            (_settings.Azimuths ? _azimuthButton : _quadrantButton).IsChecked = true;
            bar.Items.Add(_azimuthButton);
            bar.Items.Add(_quadrantButton);
            return bar;
        }

        private ToolBar BuildFieldGeniusBar()
        {
            var bar = new ToolBar { Band = 1 };
            var b = new Button { Content = "Export pts", ToolTip = "Export coordinates for FieldGenius / FD-Pro (EXPORTPTS): P,N,E,Z,D comma-delimited", Padding = new Thickness(6, 2, 6, 2) };
            b.Click += (s, e) => ExportPoints();
            bar.Items.Add(b);
            var imp = new Button { Content = "Import pts", ToolTip = "Import coordinates from FieldGenius / FD-Pro / any P,N,E,Z,D file as numbered points (IMPORTPTS; IMPORTPTS ENZ for P,E,N,Z,D)", Padding = new Thickness(6, 2, 6, 2) };
            imp.Click += (s, e) => ImportPoints(false);
            bar.Items.Add(imp);
            return bar;
        }

        private void SetAzimuths(bool on)
        {
            _settings.Azimuths = on;
            _settings.Save();
            if (on) _quadrantButton.IsChecked = false; else _azimuthButton.IsChecked = false;
            Log("  directions now read as " + (on ? "azimuths" : "quadrant bearings"));
        }

        /// <summary>A direction the way the Defaults toolbar says: quadrant bearing or azimuth (d°mm'ss").</summary>
        private string Direction(Vec2 from, Vec2 to)
        {
            double rot = (_std?.BearingRotationDeg ?? 0) * Math.PI / 180;
            double az = Angles.Azimuth(from, to) + rot;
            return _settings.Azimuths ? "Az " + SurveyLabels.Dms(Angles.Normalize2Pi(az)) : Angles.FormatBearing(az);
        }

        private void ShowPointsTab()
        {
            DependencyObject? d = _points;
            while (d != null && !(d is TabItem)) d = LogicalTreeHelper.GetParent(d) ?? System.Windows.Media.VisualTreeHelper.GetParent(d);
            if (d is TabItem tab) tab.IsSelected = true;
        }

        private void EditStandards()
        {
            string path = _settings.StandardsPath;
            if (path.Length == 0 || !File.Exists(path)) { Log("  no firm standards file set - Draft (Ctrl+D) picks one; FD-Draft is using its built-in defaults"); return; }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = false }); Log("  opened " + path + " - save it, then Draft again to apply"); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException) { Log("  couldn't open " + path + ": " + e.Message); }
        }

        // ---- INFO -----------------------------------------------------------------------------------

        /// <summary>INFO: "Line / Curve / Text Information" for each entity picked.</summary>
        private void StartInfo()
        {
            if (!NeedDrawing()) return;
            if (_activeTool.Length > 0) EndTool();
            BeginTool("INFO");
            Log("INFO  pick lines, arcs, polylines, circles, text or points (Esc or right-click ends)");
            _prompt.Text = "Info - pick an entity:";
            _awaitingPoint = p =>
            {
                var model = ModelPick(p);
                if (model == null) return;
                var raw = RawModelPick() ?? model.Value;
                var e = LineworkAt(raw, circles: true) ?? NearestOther(raw);
                if (e == null) { Log("  nothing there"); return; }
                foreach (var line in Describe(e, raw)) Log("  " + line);
            };
            _awaitingLine = s => EndTool();
        }

        /// <summary>A text, point or block near a model point (for INFO, which isn't only linework).</summary>
        private Entity? NearestOther(Vec2 at)
        {
            double tol = Math.Max(15 / _canvas.View.Zoom, 1e-6);
            Entity? best = null; double bestD = tol;
            foreach (var e in CurrentEntityOwner().Entities)
            {
                XYZ? p = e switch { TextEntity t => t.InsertPoint, MText m => m.InsertPoint, Point pt => pt.Location, Insert i => i.InsertPoint, _ => null };
                if (p == null) continue;
                double d = Vec2.Distance(at, new Vec2(p.Value.X, p.Value.Y));
                if (e is TextEntity te && te.HorizontalAlignment != TextHorizontalAlignment.Left) d = Math.Min(d, Vec2.Distance(at, new Vec2(te.AlignmentPoint.X, te.AlignmentPoint.Y)));
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        private IEnumerable<string> Describe(Entity e, Vec2 pick)
        {
            string f(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
            string layer = e.Layer?.Name ?? "0";
            switch (e)
            {
                case Line l:
                {
                    var a = new Vec2(l.StartPoint.X, l.StartPoint.Y); var b = new Vec2(l.EndPoint.X, l.EndPoint.Y);
                    yield return "LINE on " + layer + ":  " + Direction(a, b) + "   " + f(Vec2.Distance(a, b));
                    yield return "  from " + NE(a) + "   to " + NE(b);
                    break;
                }
                case Arc ar:
                {
                    var s = EntityOps.SpansOf(ar).First();
                    foreach (var x in Curve("ARC on " + layer, s)) yield return x;
                    break;
                }
                case Circle c:
                    yield return "CIRCLE on " + layer + ":  R " + f(c.Radius) + "   circumference " + f(2 * Math.PI * c.Radius) + "   area " + f(Math.PI * c.Radius * c.Radius);
                    yield return "  centre " + NE(new Vec2(c.Center.X, c.Center.Y));
                    break;
                case LwPolyline _:
                case Polyline2D _:
                {
                    var spans = EntityOps.SpansOf(e).ToList();
                    double len = spans.Sum(s => s.IsArc ? Math.Abs(s.Sweep) * s.Radius : Vec2.Distance(s.A, s.B));
                    bool closed = e is LwPolyline lp ? lp.IsClosed : ((Polyline2D)e).IsClosed;
                    yield return "POLYLINE on " + layer + ":  " + Plural(spans.Count, "span", "spans") + ", length " + f(len) + (closed ? ", closed" : "");
                    if (closed)
                    {
                        var pts = spans.Select(s => s.A).ToList();
                        var bul = spans.Select(s => s.IsArc ? s.Bulge : 0).ToList();
                        yield return "  area " + f(Math.Abs(Polygon.SignedArea(pts, bul)));
                    }
                    var near = CourseLabelling.NearestSpan(e, pick);
                    if (near.HasValue)
                    {
                        var s = near.Value;
                        if (s.IsArc) foreach (var x in Curve("  picked span", s)) yield return x;
                        else yield return "  picked span: " + Direction(s.A, s.B) + "   " + f(Vec2.Distance(s.A, s.B)) + "   from " + NE(s.A) + " to " + NE(s.B);
                    }
                    break;
                }
                case TextEntity t:
                    yield return "TEXT on " + layer + ":  \"" + t.Value + "\"";
                    yield return "  height " + f(t.Height) + "   rotation " + SurveyLabels.Dms(t.Rotation) + "   style " + (t.Style?.Name ?? "Standard") + "   at " + NE(new Vec2(t.InsertPoint.X, t.InsertPoint.Y));
                    break;
                case MText m:
                    yield return "MTEXT on " + layer + ":  \"" + m.Value.Replace("\\P", " / ") + "\"";
                    yield return "  height " + f(m.Height) + "   width " + f(m.RectangleWidth) + "   at " + NE(new Vec2(m.InsertPoint.X, m.InsertPoint.Y));
                    break;
                case Point pt:
                    yield return "POINT on " + layer + ":  " + NE(new Vec2(pt.Location.X, pt.Location.Y)) + "   Z " + f(pt.Location.Z) + (PointLinks.Tagged(pt) is int id ? "   survey point " + id : "");
                    break;
                case Insert i:
                    yield return "BLOCK " + i.Block.Name + " on " + layer + ":  at " + NE(new Vec2(i.InsertPoint.X, i.InsertPoint.Y)) + "   scale " + f(i.XScale) + "   rotation " + SurveyLabels.Dms(i.Rotation);
                    break;
                default:
                    yield return e.GetType().Name.ToUpperInvariant() + " on " + layer;
                    break;
            }
        }

        private IEnumerable<string> Curve(string head, Construct.Span s)
        {
            string f(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
            double len = Math.Abs(s.Sweep) * s.Radius;
            yield return head + ":  R " + f(s.Radius) + "   arc " + f(len) + "   chord " + f(Vec2.Distance(s.A, s.B)) + "   delta " + SurveyLabels.Dms(s.Sweep);
            yield return "  chord " + Direction(s.A, s.B) + "   centre " + NE(s.Center) + "   " + (s.Sweep > 0 ? "counter-clockwise" : "clockwise") + " from " + NE(s.A);
        }

        // ---- RESCALE, AUTOP, CAL ---------------------------------------------------------------------

        /// <summary>RESCALE: the selection (or everything on this sheet / model) scaled about a picked base point by a typed factor.</summary>
        private void StartRescale()
        {
            if (!NeedDrawing()) return;
            if (_activeTool.Length > 0) EndTool();
            var sel = SelectedEntities();
            var targets = sel.Count > 0 ? sel : CurrentEntityOwner().Entities.Where(e => !(e is Viewport)).ToList();
            BeginTool("RESCALE");
            Log("RESCALE  " + (sel.Count > 0 ? "the " + Plural(sel.Count, "selected entity", "selected entities") : "everything on " + _canvas.Scene!.Name + " (" + targets.Count + ")") + " - pick the base point, then type the factor (e.g. 0.3048 feet to metres)");
            _prompt.Text = "Re-scale - base point:";
            Vec2? basePt = null;
            _awaitingPoint = p =>
            {
                var m = ModelPick(p);
                if (m == null) return;
                basePt = m; _canvas.ToolActive = false;
                _prompt.Text = "Re-scale - factor:";
            };
            _awaitingLine = s =>
            {
                if (basePt == null) { EndTool(); return; }
                if (s.Length == 0) { EndTool(); return; }
                if (!Num(s, out double k) || k <= 0) { Log("  type a factor greater than 0"); return; }
                EndTool();
                AfterPickEdit(TransformEntitiesCommand.Scale(targets, new XYZ(basePt.Value.X, basePt.Value.Y, 0), k, "Re-scale " + targets.Count));
                Log("  " + Plural(targets.Count, "entity", "entities") + " scaled by " + k.ToString(CultureInfo.InvariantCulture) + "  (Ctrl+Z undoes it)");
            };
        }

        /// <summary>AUTOP: a POINT at every vertex of the selected linework (once per spot), on the current layer.</summary>
        private void AutoPointsOnObjects()
        {
            if (!NeedDrawing()) return;
            var sel = SelectedEntities();
            if (sel.Count == 0) { Log("AUTOP  select the lines, arcs or polylines first, then AUTOP"); return; }
            var spots = new List<Vec2>();
            void Add(Vec2 v) { if (!spots.Any(q => Vec2.Distance(q, v) < 1e-6)) spots.Add(v); }
            foreach (var e in sel)
                foreach (var s in EntityOps.SpansOf(e).Take(e is Circle && !(e is Arc) ? 0 : int.MaxValue)) { Add(s.A); Add(s.B); }
            if (spots.Count == 0) { Log("  no linework in the selection"); return; }
            string layer = CurrentLayer();
            AddDrawn("Auto points", spots.Select(v => (Entity)new Point(W(v)) { Layer = GetOrCreateLayer(layer) }).ToArray());
            Log("AUTOP  " + Plural(spots.Count, "point", "points") + " added on " + layer + "  (Ctrl+Z undoes them)");
        }

        private void AskCalc()
        {
            _prompt.Text = "Calc - expression (blank ends):";
            _awaitingPoint = null;
            _awaitingLine = s =>
            {
                if (s.Length == 0) { EndTool(); return; }
                Log("  = " + Calculate(s));
            };
        }

        /// <summary>The CAL calculator (FdDraft.Core.Geometry.Calculator).</summary>
        public static string Calculate(string expr) => Calculator.Evaluate(expr);

        // ---- EXPORTPTS --------------------------------------------------------------------------------

        /// <summary>
        /// EXPORTPTS: the job's survey points (or, for a drawing opened on its own, its POINT
        /// entities with their survey numbers) as P,N,E,Z,D - what FieldGenius and FD-Pro import.
        /// </summary>
        private void ExportPoints()
        {
            var rows = new List<string>();
            if (_job != null && _job.Points.Count > 0)
                foreach (var p in _job.Points.OrderBy(p => p.Id))
                    rows.Add(Row(p.Id.ToString(CultureInfo.InvariantCulture), p.Northing, p.Easting, p.Elevation, p.Code));
            else if (_doc != null)
            {
                int auto = 1;
                var pts = _doc.ModelSpace.Entities.OfType<Point>().ToList();
                foreach (var pt in pts)
                {
                    int id = PointLinks.Tagged(pt) ?? 0;
                    rows.Add(Row(id > 0 ? id.ToString(CultureInfo.InvariantCulture) : "A" + auto++, pt.Location.Y, pt.Location.X, pt.Location.Z, PointLinks.TaggedCode(pt) ?? pt.Layer?.Name ?? ""));
                }
            }
            if (rows.Count == 0) { Log("EXPORTPTS  no points - draft an FD-Pro job, or open a drawing with POINT entities"); return; }
            var dlg = new SaveFileDialog { Filter = "Coordinates (*.csv)|*.csv|Text (*.txt)|*.txt", FileName = (_job?.Settings.Id is string id2 && id2.Length > 0 ? id2 : "points") + "-pnezd.csv" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllLines(dlg.FileName, rows, new UTF8Encoding(false));
                Log("EXPORTPTS  " + Plural(rows.Count, "point", "points") + " written to " + dlg.FileName + " (P,N,E,Z,D)");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Log("  couldn't write it: " + e.Message); }

            static string Row(string id, double n, double e, double z, string d) =>
                string.Join(",", id, n.ToString("F4", CultureInfo.InvariantCulture), e.ToString("F4", CultureInfo.InvariantCulture), z.ToString("F4", CultureInfo.InvariantCulture), d.Contains(',') ? "\"" + d.Replace("\"", "\"\"") + "\"" : d);
        }

        // ---- Zoom All / Center / Object -----------------------------------------------------------------

        /// <summary>ZOOM C: pick the new centre; the zoom stays (or type a magnification, e.g. 2).</summary>
        private void StartZoomCenter()
        {
            if (_canvas.Scene == null) return;
            var resume = (_activeTool, _awaitingPoint, _awaitingLine, _prompt.Text);
            var resumeCanvas = (_canvas.ToolActive, _canvas.RubberFrom);
            bool wasTool = _activeTool.Length > 0;
            BeginTool("ZOOM C");
            _prompt.Text = "Zoom center - pick the new centre:";
            Vec2? center = null;
            void Done()
            {
                EndTool();
                if (wasTool) { (_activeTool, _awaitingPoint, _awaitingLine, _prompt.Text) = resume; (_canvas.ToolActive, _canvas.RubberFrom) = resumeCanvas; }
            }
            _awaitingPoint = p => { center = p; _prompt.Text = "Zoom center - magnification <1>:"; _canvas.ToolActive = false; };
            _awaitingLine = s =>
            {
                if (center == null) { Done(); return; }
                double k = s.Length > 0 && Num(s, out double v) && v > 0 ? v : 1;
                _canvas.ZoomCenter(center.Value, k);
                Done();
            };
        }

        /// <summary>ZOOM OB: the view fitted to the selected entities.</summary>
        private void ZoomObject()
        {
            if (_canvas.Scene == null) return;
            var sel = new HashSet<ulong>(_canvas.Selected);
            if (sel.Count == 0) { Log("  select something to zoom to first"); return; }
            var ext = new Extents();
            foreach (var p in _canvas.Scene.AllPrims())
                if (sel.Contains(p.Handle)) { ext.Add(new Vec2(p.Bounds.X1, p.Bounds.Y1)); ext.Add(new Vec2(p.Bounds.X2, p.Bounds.Y2)); }
            if (ext.IsEmpty) { Log("  the selection isn't drawn on this sheet"); return; }
            double pad = Math.Max(ext.MaxX - ext.MinX, ext.MaxY - ext.MinY) * 0.1 + 1e-6;
            _canvas.ZoomWindow(new FdDraft.Core.Standards.Rect(ext.MinX - pad, ext.MinY - pad, ext.MaxX + pad, ext.MaxY + pad));
        }
    }
}
