using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ACadSharp.Entities;
using CSMath;
using FdDraft.Cad.Editing;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;
using Line = ACadSharp.Entities.Line;

namespace FdDraft.App
{
    /// <summary>
    /// INFO's Line Computations window and MSCAD's "Traverse or Side Shots": bearing / distance legs
    /// from a picked point, the job's scale factor applied to typed distances (ground in, grid drawn).
    /// </summary>
    public sealed partial class MainWindow
    {
        private TraverseDialog? _traverseDlg;
        private int _traverseNextPoint;

        /// <summary>INFO on a line (or a straight polyline span): the Line Computations window.</summary>
        private void ShowLineInfo(Vec2 a, Vec2 b, double za, double zb, string what)
        {
            var std = LabelStandards();
            double g2g = GridToGround(std);
            var c = new LineComputation(a.X, a.Y, za, b.X, b.Y, zb, g2g);
            string bearing = Angles.FormatBearing(c.Azimuth, std.BearingSecondsDecimals);
            string rotated = Math.Abs(std.BearingRotationDeg) > 1e-12
                ? Angles.FormatBearing(c.Azimuth + std.BearingRotationDeg * Math.PI / 180, std.BearingSecondsDecimals) : "";
            ListLine(c, bearing, rotated, what);
            var dlg = new LineInfoDialog(c, what, bearing, rotated, Decimals(), g2g > 0 ? 1 / g2g : 1) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            switch (dlg.Choice)
            {
                case LineInfoDialog.Action.Traverse: EndTool(); StartTraverse(DdMmSsBearing(c.Azimuth)); break;
                case LineInfoDialog.Action.TurnedAngle: EndTool(); Execute("TURNANGLE", ""); break;
                case LineInfoDialog.Action.TangentToArc: EndTool(); Execute("TANLINE", ""); break;
                case LineInfoDialog.Action.CurveCalcs: EndTool(); Execute("CURVECALC", ""); break;
                case LineInfoDialog.Action.ListLine:
                    Log(string.Format(CultureInfo.InvariantCulture, "    from N {0}  E {1}  Z {2}   to N {3}  E {4}  Z {5}", F(c.FromN), F(c.FromE), F(c.FromZ), F(c.ToN), F(c.ToE), F(c.ToZ)));
                    Log("    horizontal " + F(c.Horizontal) + "  scaled " + F(c.ScaledHorizontal) + "   slope " + F(c.Slope) + "  scaled " + F(c.ScaledSlope) + "   grade " + F(c.GradePercent) + " %   dZ " + F(c.DeltaZ));
                    break;
            }
        }

        private void ListLine(LineComputation c, string bearing, string rotated, string what)
        {
            Log("  " + what + ": " + bearing + (rotated.Length > 0 ? " (rotated " + rotated + ")" : "") + "  " + F(c.ScaledHorizontal) +
                (Math.Abs(c.OutputScale - 1) > 1e-12 ? " (grid " + F(c.Horizontal) + ")" : "") + "   from " + NE(new Vec2(c.FromE, c.FromN)) + " to " + NE(new Vec2(c.ToE, c.ToN)));
        }

        /// <summary>A bearing as the Traverse dialog takes it: N73.1010E (DD.MMSS).</summary>
        private static string DdMmSsBearing(double azimuth)
        {
            double deg = Angles.Normalize2Pi(azimuth) * 180 / Math.PI;
            string ns, ew; double q;
            if (deg <= 90) { ns = "N"; ew = "E"; q = deg; }
            else if (deg <= 180) { ns = "S"; ew = "E"; q = 180 - deg; }
            else if (deg <= 270) { ns = "S"; ew = "W"; q = deg - 180; }
            else { ns = "N"; ew = "W"; q = 360 - deg; }
            long secs = (long)Math.Round(q * 3600, MidpointRounding.AwayFromZero);
            return ns + (secs / 3600).ToString(CultureInfo.InvariantCulture) + "." + (secs % 3600 / 60).ToString("00", CultureInfo.InvariantCulture) + (secs % 60).ToString("00", CultureInfo.InvariantCulture) + ew;
        }

        /// <summary>TRAVERSE: pick the point to start from, then the Traverse or Side Shots window.</summary>
        private void StartTraverse(string bearing = "0")
        {
            if (!NeedDrawing()) return;
            _traverseDlg?.Close();
            if (_activeTool.Length > 0) EndTool();
            BeginTool("TRAVERSE");
            _prompt.Text = "Traverse - pick the point to start from (snap to it):";
            Log("TRAVERSE  pick the point to start from (Esc cancels)");
            _awaitingLine = s => { if (s.Length == 0) { EndTool(); Log("  *cancelled*"); } else if (Cogo.TryParseCoordinate(s, out double e, out double n)) Begin(new Vec2(e, n)); else Log("  pick a point, or type E,N"); };
            _awaitingPoint = p =>
            {
                var model = ModelOf(p);
                if (model != null) Begin(model.Value);
            };

            void Begin(Vec2 start)
            {
                EndTool();
                OpenTraverse(start, bearing);
            }
        }

        private void OpenTraverse(Vec2 start, string bearing)
        {
            var std = LabelStandards();
            double g2g = GridToGround(std);
            double sf = g2g > 0 ? 1 / g2g : 1;
            int jobMax = _job?.Points.Count > 0 ? _job.Points.Max(p => p.Id) : 0;
            _traverseNextPoint = Math.Max(_traverseNextPoint, jobMax + 1);
            var cur = start;
            var history = new Stack<(Vec2 From, int NextPoint)>();
            string layer = CurrentLayer();
            string FromText() => "From N " + F(cur.Y) + "  E " + F(cur.X) + "   on layer " + layer;

            var dlg = new TraverseDialog(FromText(), bearing, sf, _traverseNextPoint) { Owner = this };
            Log("  Traverse from " + NE(cur) + (Math.Abs(sf - 1) > 1e-12 ? " - typed distances × scale factor " + sf.ToString("0.########", CultureInfo.InvariantCulture) : ""));
            dlg.Leg = req =>
            {
                if (_doc == null) return "no drawing open";
                var leg = Cogo.TraverseLegFrom(cur, req.Bearing, req.Distance, req.CorrectionRadians, req.InputScale, out string error);
                if (leg == null) return error;
                var l = leg.Value;
                var ly = GetOrCreateLayer(layer);
                var line = new Line(new XYZ(l.From.X, l.From.Y, 0), new XYZ(l.To.X, l.To.Y, 0)) { Layer = ly };
                var entities = new List<Entity> { line };
                if (req.PointNumber is int pn)
                {
                    double h = std.PointNumberTextMm * ModelPerMm();
                    entities.Add(new Point { Location = new XYZ(l.To.X, l.To.Y, 0), Layer = ly });
                    var right = new Vec2(Math.Cos(-Angles.ViewTwist), Math.Sin(-Angles.ViewTwist)); var up = right.Left();
                    var at = l.To + right * (h * 0.6) + up * (h * 0.3);
                    entities.Add(new TextEntity { Value = pn.ToString(CultureInfo.InvariantCulture), InsertPoint = new XYZ(at.X, at.Y, 0), Height = h, Rotation = -Angles.ViewTwist, Layer = ly });
                }
                var commands = new List<IEditCommand> { new AddEntitiesCommand(CurrentEntityOwner(), entities, "Traverse leg") };
                if (req.LabelBearing || req.LabelDistance)
                {
                    var mid = (l.From + l.To) * 0.5;
                    var dir = (l.To - l.From) * (1 / Math.Max(l.GridDistance, 1e-12));
                    var style = req.LabelBearing && req.LabelDistance ? CourseLabelStyle.BearingDistance
                        : req.LabelBearing ? CourseLabelStyle.BearingOffLine : CourseLabelStyle.DistanceOffLine;
                    var pick = mid + dir.Left() * Math.Max(l.GridDistance * 0.01, 1e-6);
                    var label = CourseLabelling.Annotate(line, pick, style, _doc, std, LabelModelPerMm(std, out _), GetOrCreateLayer, out _, g2g);
                    if (label != null) commands.Add(label);
                }
                history.Push((cur, _traverseNextPoint));
                if (req.PointNumber is int used) _traverseNextPoint = used + 1;
                Commit(new CompositeCommand(commands, req.SideShot ? "Side shot" : "Traverse leg"),
                    "  " + (req.SideShot ? "side shot " : "leg ") + BearingText(l.From, l.To) + "  " + F(l.TypedDistance) +
                    (Math.Abs(l.GridDistance - l.TypedDistance) > 1e-9 ? " (grid " + F(l.GridDistance) + ")" : "") + "  to " + NE(l.To) +
                    (req.PointNumber is int n2 ? "  pt " + n2 : ""));
                if (!req.SideShot) cur = l.To;
                dlg.SetFrom(FromText());
                dlg.SetNextPoint(_traverseNextPoint);
                return null;
            };
            dlg.Pad = cmd =>
            {
                double step = _canvas.ActualWidth / 4;
                switch (cmd)
                {
                    case "up": _canvas.PanBy(0, step); break;
                    case "down": _canvas.PanBy(0, -step); break;
                    case "left": _canvas.PanBy(step, 0); break;
                    case "right": _canvas.PanBy(-step, 0); break;
                    case "in": _canvas.ZoomBy(1.6); break;
                    case "out": _canvas.ZoomBy(1 / 1.6); break;
                    case "extents": _canvas.ZoomExtents(); break;
                    case "here":
                        var sc = _canvas.Scene;
                        var vp = sc != null && sc.IsPaper ? sc.Groups.FirstOrDefault(g => g.Clip.HasValue && g.ToModel.HasValue) : null;
                        if (sc != null && sc.IsPaper && vp == null) { Log("  switch to Model to see the traverse"); return; }
                        _canvas.ZoomTo(vp != null ? vp.ToModel!.Value.Inverse().Apply(cur) : cur, _canvas.View.Zoom); break;
                    case "undo":
                        if (history.Count == 0) { Log("  no leg to undo"); return; }
                        var (from, next) = history.Pop();
                        var what = _undo.Undo();
                        _dirty = true; UpdateTitle();
                        Rebuild(fit: false);
                        cur = from; _traverseNextPoint = next;
                        dlg.SetFrom(FromText()); dlg.SetNextPoint(next);
                        Log("  undid " + (what ?? "the last leg") + " - back at " + NE(cur));
                        break;
                }
            };
            dlg.Closed += (s, e) => { if (_traverseDlg == dlg) _traverseDlg = null; Log("  *traverse ended*"); };
            _traverseDlg = dlg;
            dlg.Show();
        }
    }
}
