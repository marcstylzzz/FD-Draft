using System;
using System.Globalization;
using System.Linq;
using ACadSharp.Entities;
using FdDraft.Cad.Editing;
using FdDraft.Core.Geometry;
using Line = ACadSharp.Entities.Line;

namespace FdDraft.App
{
    /// <summary>
    /// Surveyor View (SV), World View (WV) and Return to Surveyor View (RSV), as in MSCAD: the
    /// plan turned so north isn't up - on screen in Model, and on every sheet (the plan viewport's
    /// twist, with the north arrow turned to match). Coordinates and bearings don't change.
    /// </summary>
    public sealed partial class MainWindow
    {
        /// <summary>The last surveyor-view twist, for Return to Surveyor View.</summary>
        private double _lastSurveyorTwist;

        private static string Deg(double radians) => (radians * 180 / Math.PI).ToString("0.####", CultureInfo.InvariantCulture) + "°";

        /// <summary>Model's on-screen twist follows the drawing's sheets (after opening, undo, redo).</summary>
        private void SyncTwistFromDrawing(bool announce)
        {
            if (_doc == null) { _canvas.ModelTwist = 0; Angles.ViewTwist = 0; return; }
            double t = SurveyorView.CurrentTwist(_doc);
            // A drawing with no sheet viewport keeps whatever the screen was turned to.
            if (_doc.Layouts.Any(l => l.IsPaperSpace && SheetScale.PlanViewport(l) != null)) _canvas.ModelTwist = t;
            else if (announce) _canvas.ModelTwist = 0;
            // New labels read left to right as the plan is seen.
            Angles.ViewTwist = _canvas.ModelTwist;
            if (Math.Abs(t) > 1e-12)
            {
                _lastSurveyorTwist = t;
                if (announce) Log("  this drawing is in surveyor view, turned " + Deg(t) + " (WV for north up)");
            }
        }

        /// <summary>Turns the plan to <paramref name="twist"/> everywhere: screen, sheets, north arrows.</summary>
        private void SetPlanTwist(double twist, string what)
        {
            if (!NeedDrawing()) return;
            twist = Math.Atan2(Math.Sin(twist), Math.Cos(twist)); // -180..180
            var std = LabelStandards();
            var rules = new SurveyorView.LabelRules { FromTwist = _canvas.ModelTwist, ElevationAngleDeg = std.ElevationAngleDeg };
            var cmd = SurveyorView.Apply(_doc!, twist, std.NorthArrowBlock, rules, out int sheets, out int arrows, out int relabelled);
            var c = _canvas.View.Center; var z = _canvas.View.Zoom;
            _canvas.ModelTwist = twist;
            Angles.ViewTwist = twist;
            if (Math.Abs(twist) > 1e-12) _lastSurveyorTwist = twist;
            if (cmd != null)
            {
                _undo.Push(cmd);
                _dirty = true; UpdateTitle();
            }
            Rebuild(fit: false);
            _canvas.ZoomTo(c, z);
            Log("  " + what + (Math.Abs(twist) < 1e-12 ? "" : ": plan turned " + Deg(twist) + " counter-clockwise")
                + (sheets > 0 ? " - " + sheets + " sheet" + (sheets == 1 ? "" : "s") + " turned to match" + (arrows > 0 ? ", north arrow turned with " + (sheets == 1 ? "it" : "them") : ", no north arrow found on the sheet") : " (no sheet viewport - on screen only)")
                + (relabelled > 0 ? "; " + relabelled + " labels and symbols turned to read on the plan (elevations at " + std.ElevationAngleDeg.ToString("0", CultureInfo.InvariantCulture) + "° up-right)" : "")
                + (cmd != null ? "  (Ctrl+Z undoes it)" : ""));
        }

        /// <summary>SV [angle | bearing]: pick a line to run level across the plan, or type a bearing
        /// to point straight up (N45-30-00E), or a turn in degrees counter-clockwise.</summary>
        private void StartSurveyorView(string arg)
        {
            if (!NeedDrawing()) return;
            bool Typed(string s)
            {
                var t = s.Trim();
                if (t.Length == 0) return false;
                if (char.IsLetter(t[0]) && Cogo.TryParseLeg(t + " 1", out double az, out _))
                {
                    EndTool();
                    SetPlanTwist(SurveyorView.TwistToPointUp(az), "surveyor view, " + t.ToUpperInvariant() + " up");
                    return true;
                }
                if (double.TryParse(t.TrimEnd('°'), NumberStyles.Float, CultureInfo.InvariantCulture, out double deg))
                {
                    EndTool();
                    SetPlanTwist(deg * Math.PI / 180, "surveyor view");
                    return true;
                }
                Log("  pick a line, or type a bearing to point up (N45-30-00E) or a turn in degrees");
                return true;
            }
            if (arg.Length > 0 && Typed(arg)) return;
            PickLoop("SV", "Surveyor view - pick a line to run level across the plan (or type a bearing to point up, or degrees):",
                "pick the line that should run level (a front lot line), or type a bearing that should point up, or a turn in degrees",
                p =>
                {
                    var model = ModelOf(p);
                    if (model == null) return;
                    var e = PickEntity(p, x => x is Line || x is LwPolyline || x is Polyline2D);
                    var span = e == null ? null : StraightSpanNear(e, model.Value);
                    if (span == null) { Log("  no line there - pick a straight line, or type a bearing"); return; }
                    EndTool();
                    SetPlanTwist(SurveyorView.TwistToLevel(span.Value.A, span.Value.B), "surveyor view, " + BearingText(span.Value.A, span.Value.B) + " level");
                });
            _awaitingLine = s => { if (s.Length == 0) { EndTool(); Log("  *cancelled*"); return; } Typed(s); };
        }

        /// <summary>ELEV45: every elevation put back at its angle round its point on the plan (45° up-right
        /// by default) - for a drawing drafted before that rule, or elevations moved by accident.</summary>
        private void PlaceElevations()
        {
            if (!NeedDrawing()) return;
            var std = LabelStandards();
            double t = _canvas.ModelTwist;
            var cmd = SurveyorView.Relabel(_doc!, t, t, std.ElevationAngleDeg, out int n);
            if (cmd == null || n == 0) { Log("  no elevation labels found (they're the point-tagged numbers on ELEVATION layers)"); return; }
            Commit(cmd, "  " + n + " elevations placed " + std.ElevationAngleDeg.ToString("0", CultureInfo.InvariantCulture) + "° up-right of their points  (Ctrl+Z undoes it)");
        }

        private void WorldView()
        {
            if (!NeedDrawing()) return;
            if (Math.Abs(_canvas.ModelTwist) < 1e-12 && Math.Abs(SurveyorView.CurrentTwist(_doc!)) < 1e-12) { Log("  already north up"); return; }
            SetPlanTwist(0, "world view, north up (RSV goes back to the surveyor view)");
        }

        private void ReturnToSurveyorView()
        {
            if (!NeedDrawing()) return;
            if (Math.Abs(_lastSurveyorTwist) < 1e-12) { Log("  no surveyor view yet - SV sets one"); return; }
            SetPlanTwist(_lastSurveyorTwist, "back to surveyor view");
        }
    }
}
