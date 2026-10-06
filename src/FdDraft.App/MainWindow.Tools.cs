using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using FdDraft.Cad;
using FdDraft.Cad.Editing;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;
using FdDraft.Core.Job;
using FdDraft.Core.Standards;
using Microsoft.Win32;
using Arc = ACadSharp.Entities.Arc;
using CoreArc = FdDraft.Core.Geometry.Arc;
using Line = ACadSharp.Entities.Line;

namespace FdDraft.App
{
    /// <summary>
    /// The survey drafting tools behind the FD Labels, FD Ties, FD Text Edit, FD Layer, Layer Tools,
    /// Dimensioning, Text, FD Main Control, FD Calcs and FD Coordinate toolbars (laid out after
    /// MSCAD's). Each is a typed command too; <see cref="ExecuteMsTool"/> dispatches them.
    /// </summary>
    public sealed partial class MainWindow
    {
        /// <summary>The commands in this file; false when the verb isn't one of them.</summary>
        private bool ExecuteMsTool(string verb, string arg)
        {
            switch (verb)
            {
                // ---- FD Labels
                case "SPLITBRG": StartAnnotate(CourseLabelStyle.SplitBearing); return true;
                case "BRGON": StartAnnotate(CourseLabelStyle.BearingOnLine); return true;
                case "BRGOFF": StartAnnotate(CourseLabelStyle.BearingOffLine); return true;
                case "DISTON": StartAnnotate(CourseLabelStyle.DistanceOnLine); return true;
                case "DISTOFF": StartAnnotate(CourseLabelStyle.DistanceOffLine); return true;
                case "BRGDIST": StartAnnotate(CourseLabelStyle.BearingDistance); return true;
                case "BRGDASH": StartAnnotate(CourseLabelStyle.BearingDashDistance); return true;
                case "DISTDASH": StartAnnotate(CourseLabelStyle.DistanceDashBearing); return true;
                case "BRGDISTL": StartAnnotate(CourseLabelStyle.BearingOverDistance); return true;
                case "DISTBRGL": StartAnnotate(CourseLabelStyle.DistanceOverBearing); return true;
                case "CURVEON": StartAnnotate(CourseLabelStyle.BearingOnLine, curvesOnly: true); return true;
                case "ADDANGLE": StartAddAngle(); return true;
                case "ARROWLINE": StartArrowLine(); return true;
                case "CURVEOFF": StartCurveOff(); return true;
                case "TEXTARC": StartTextOnArc(); return true;
                // ---- FD Ties
                case "HOUSETIEA": StartAutoHouseTie(true); return true;
                case "HOUSETIE": StartAutoHouseTie(false); return true;
                case "MHOUSETIEA": StartManualHouseTie(true); return true;
                case "MHOUSETIE": StartManualHouseTie(false); return true;
                case "LEADERSCALE": LeaderScale(arg); return true;
                case "CURVYLEADER": StartCurvyLeader(); return true;
                case "QPOST": case "INSERT": StartInsertBlock(arg); return true;
                case "BLOCKLINE": StartBlockLine(); return true;
                case "LINETABLE": StartTable(TableKind.Lines); return true;
                case "CURVETABLE": StartTable(TableKind.Curves); return true;
                case "TIETABLE": StartTable(TableKind.Ties); return true;
                // ---- FD Text Edit / Text
                case "LEROY": Leroy(arg); return true;
                case "STYLE": TextStyleCommand(arg); return true;
                case "ARROWS": StartArrows(); return true;
                case "SCALEONE": StartScaleOne(); return true;
                case "SCALETXT": StartScaleText(); return true;
                case "ROTEXT": RotateTexts180(); return true;
                case "ROTOLINE": StartRotateToLine(); return true;
                case "SLIDETEXT": StartSlideText(); return true;
                case "TEXTEDIT": case "DDEDIT": StartTextEdit(); return true;
                case "MTEXT": case "MT": StartMText(); return true;
                case "TXT2MTXT": TextToMText(); return true;
                // ---- layers
                case "LAYCOPY": StartLayerCopy(); return true;
                case "LAYDEL": case "LAYERASE": StartLayerDelete(); return true;
                case "LAYFRZ": StartLayerPick("LAYFRZ", l => SetLayerFlag(l, LayerFlags.Frozen, true), "frozen"); return true;
                case "LAYTHW": AllLayers(l => SetLayerFlag(l, LayerFlags.Frozen, false), "thawed"); return true;
                case "LAYOFF": StartLayerPick("LAYOFF", l => l.IsOn = false, "off"); return true;
                case "LAYON": AllLayers(l => l.IsOn = true, "on"); return true;
                case "LAYLCK": StartLayerPick("LAYLCK", l => SetLayerFlag(l, LayerFlags.Locked, true), "locked"); return true;
                case "LAYULK": StartLayerPick("LAYULK", l => SetLayerFlag(l, LayerFlags.Locked, false), "unlocked"); return true;
                case "LAYMCH": StartLayerMatch(); return true;
                case "LAYMCUR": StartLayerSetCurrent(); return true;
                case "LAYCUR": SetSelectionLayer(); return true;
                case "LAYWHAT": StartLayerWhat(); return true;
                case "LAYISO": LayerIsolate(); return true;
                case "LAYUNISO": LayerUnisolate(); return true;
                case "LAYERSTATE": case "LAYERSTATESAVE": LayerStateCommand(verb == "LAYERSTATESAVE" ? "SAVE " + arg : arg); return true;
                case "LAYERP": LayerPrevious(); return true;
                case "SETBYLAYER": SetByLayer(); return true;
                case "LAYERS": if (_layersTab != null) _leftTabs.SelectedItem = _layersTab; return true;
                case "CODES": if (_codesTab != null) _leftTabs.SelectedItem = _codesTab; if (_job == null) Log("  draft an FD-Pro job to fill the code library"); return true;
                case "POINTS": if (_pointsTab != null) _leftTabs.SelectedItem = _pointsTab; if (_job == null) Log("  draft an FD-Pro job to list its points"); return true;
                // ---- dimensioning
                case "QDIM": StartQuickDim(); return true;
                case "DIMBASELINE": case "DBA": StartDimChain(baseline: true); return true;
                case "DIMCONTINUE": case "DCO": StartDimChain(baseline: false); return true;
                case "CENTERMARK": case "DCE": StartCenterMark(); return true;
                case "CENTERLINE": StartCenterLine(); return true;
                case "DIMTEXT": StartDimText(); return true;
                case "DIMROTATE": StartDimRotate(); return true;
                case "DIMTEDIT": StartDimTextMove(); return true;
                case "DIMHOME": StartDimHome(); return true;
                case "DIMSTYLE": DimStyleCommand(arg); return true;
                case "DIMSTATUS": DimStatus(); return true;
                case "DIMUPDATE": DimUpdate(); return true;
                // ---- main control
                case "CONFIG": OpenStandardsFile(); return true;
                case "INFO": case "LIST": StartInfo(); return true;
                case "TRAVERSE": case "TRAV": case "SIDESHOT": StartTraverse(); return true;
                case "TITLEBLOCKS": case "TBLOCKS": PlaceTitleBlocksCommand(); return true;
                case "SELECTSIMILAR": SelectSimilar(); return true;
                case "COPYCLIP": ClipCopy(false); return true;
                case "CUTCLIP": ClipCopy(false, cut: true); return true;
                case "COPYBASE": ClipCopy(true); return true;
                case "PASTECLIP": ClipPaste(PasteMode.AtPoint); return true;
                case "PASTEBLOCK": ClipPaste(PasteMode.AsBlock); return true;
                case "PASTEORIG": ClipPaste(PasteMode.Original); return true;
                case "DRAWORDER": case "DR":
                    var where = arg.Trim().ToUpperInvariant();
                    DrawOrder(where.StartsWith("B") ? DrawOrderCommand.Place.Back : where.StartsWith("A") ? DrawOrderCommand.Place.Above : where.StartsWith("U") ? DrawOrderCommand.Place.Under : DrawOrderCommand.Place.Front);
                    return true;
                case "ADDPOINTS": AddPointsToObjects(); return true;
                case "LOGFILE": OpenLogFile(); return true;
                case "CALC": Launch("calc.exe", "the Windows calculator"); return true;
                // ---- calcs
                case "PTSONOBJ": StartPointsOnObject(); return true;
                case "TURNANGLE": StartTurnedAngle(); return true;
                case "STAOFF": StartStationOffset(); return true;
                case "TANLINE": StartTangentLine(); return true;
                case "JOINDESC": JoinByDescription(arg); return true;
                case "BESTLINE": BestFit(false); return true;
                case "BESTCURVE": BestFit(true); return true;
                case "CURVECALC": StartCurveCalc(arg); return true;
                case "CURVETAN": StartCurveOffTangent(); return true;
                // ---- coordinate
                case "PTEXPORT": ExportPoints(); return true;
                case "PTIMPORT": ImportPoints(); return true;
                case "DELPOINTS": DeletePoints(); return true;
                case "LISTP": ListPoints(arg); return true;
                case "SCALEP": case "SC": StartScaleSelection(); return true;
                case "ZOOMP": ZoomPointCommand(arg); return true;
                case "SNAPMODE": SnapModeCommand(arg); return true;
                case "SV": case "SURVEYORVIEW": StartSurveyorView(arg); return true;
                case "WV": case "WORLDVIEW": WorldView(); return true;
                case "ELEV45": case "ELEVPLACE": PlaceElevations(); return true;
                case "NORTHARROW": case "NARROW": RepairNorthArrows(); return true;
                case "RSV": case "RETURNSV": case "RETURN_SURVEYORVIEW": ReturnToSurveyorView(); return true;
            }
            return false;
        }

        private void ShowToolsHelp()
        {
            Log("  FD Labels: SPLITBRG BRGON BRGOFF DISTON DISTOFF BRGDIST BRGDASH DISTDASH BRGDISTL DISTBRGL ADDANGLE ARROWLINE CURVEON CURVEOFF TEXTARC");
            Log("  FD Ties: HOUSETIEA HOUSETIE MHOUSETIEA MHOUSETIE LEADERSCALE CURVYLEADER QPOST BLOCKLINE LINETABLE CURVETABLE TIETABLE");
            Log("  FD Text Edit / Text: LEROY 080 STYLE ARROWS SCALEONE SCALETXT ROTEXT ROTOLINE SLIDETEXT TEXTEDIT MTEXT TXT2MTXT");
            Log("  Layers: LAYISO LAYUNISO LAYOFF LAYON LAYFRZ LAYTHW LAYLCK LAYULK LAYMCH LAYMCUR LAYCUR LAYCOPY LAYDEL LAYWHAT LAYERSTATE LAYERP SETBYLAYER");
            Log("  Dimensioning: QDIM DIMLIN DIM DIMANG DIMBASELINE DIMCONTINUE CENTERMARK CENTERLINE DIMTEXT DIMROTATE DIMTEDIT DIMHOME DIMSTYLE DIMSTATUS DIMUPDATE");
            Log("  FD Calcs: PTSONOBJ TURNANGLE STAOFF TANLINE JOINDESC BESTLINE BESTCURVE CURVECALC CURVETAN TRAVERSE (or INFO on a line > Traverse)   FD Coordinate: PTEXPORT PTIMPORT DELPOINTS LISTP SCALEP ZOOMP");
            Log("  At any point prompt: FROM (base point, then dx,dy or bearing distance) · M2P (midpoint of two picks) · snaps also INS TAN EXT");
            Log("  Draw: PLINE (A arc span, C close) SPLINE CIRCLE [D|2P|3P|A] ARC ARCC ELLIPSE POINT RECTANGLE POLYGON [C|E] REVCLOUD DONUT SOLID HATCH [S|L|X]");
            Log("  Main: INFO ADDPOINTS CONFIG LOGFILE CALC   (every toolbar button is one of these - hover a button to see its command)");
        }

        // =============================================================================================
        // shared helpers
        // =============================================================================================

        private bool NeedDrawing()
        {
            if (_doc != null && _canvas.Scene != null) return true;
            Log("  open or draft a drawing first");
            return false;
        }

        /// <summary>The model point under a scene pick, or null (logged) outside any viewport.</summary>
        private Vec2? ModelOf(Vec2 scenePoint)
        {
            var m = _canvas.Scene?.ModelAt(scenePoint);
            if (m == null) Log("  pick inside a viewport (or on Model)");
            return m;
        }

        /// <summary>The nearest entity drawn at a scene pick that <paramref name="accept"/> takes.</summary>
        private Entity? PickEntity(Vec2 scenePoint, Func<Entity, bool> accept, double pixels = 10)
        {
            if (_doc == null) return null;
            foreach (var h in _canvas.HandlesAt(scenePoint, pixels))
                if (_doc.GetCadObject(h) is Entity e && accept(e)) return e;
            return null;
        }

        private static bool IsText(Entity e) => e is TextEntity || e is MText;
        private static bool IsCourse(Entity e) => e is Line || e is Arc || e is LwPolyline || e is Polyline2D;

        /// <summary>Pushes an undo step, marks the drawing changed and redraws in place.</summary>
        private void Commit(IEditCommand cmd, string message)
        {
            _undo.Push(cmd);
            _dirty = true; UpdateTitle();
            var c = _canvas.View.Center; var z = _canvas.View.Zoom;
            Rebuild(fit: false);
            _canvas.ZoomTo(c, z);
            UpdateProperties();
            if (message.Length > 0) Log(message);
        }

        private void AddEntities(IList<Entity> entities, string description, string message)
        {
            if (entities.Count == 0) { Log("  nothing to add"); return; }
            Commit(new AddEntitiesCommand(CurrentEntityOwner(), entities.ToList(), description), message);
        }

        private double GridToGround(FirmStandards std) =>
            std.GridToGround && _job != null && _job.Settings.ScaleFactor > 0 ? 1.0 / _job.Settings.ScaleFactor : 1.0;

        private double ModelPerMm() => LabelModelPerMm(LabelStandards(), out _);

        private static string F(double v, int d = 3) => v.ToString("F" + d, CultureInfo.InvariantCulture);

        private int Decimals() => _std?.DistanceDecimals ?? 3;

        private string BearingText(Vec2 a, Vec2 b)
        {
            var std = LabelStandards();
            return Angles.FormatBearing(Angles.Azimuth(a, b) + std.BearingRotationDeg * Math.PI / 180, std.BearingSecondsDecimals);
        }

        /// <summary>The straight span of a line/polyline nearest a model point, as (a, b).</summary>
        private static (Vec2 A, Vec2 B)? StraightSpanNear(Entity e, Vec2 model)
        {
            (Vec2, Vec2)? best = null; double bd = double.MaxValue;
            foreach (var s in EntityOps.SpansOf(e))
            {
                if (s.IsArc) continue;
                double d = s.DistanceAndSide(model, out _);
                if (d < bd) { bd = d; best = (s.A, s.B); }
            }
            return best;
        }

        /// <summary>The arc (an ARC, or an arc span of a polyline) nearest a model point.</summary>
        private static CoreArc? ArcNear(Entity e, Vec2 model)
        {
            if (e is Arc a) return SurveyDrafting.ToCore(a);
            if (e is Circle c) return new CoreArc { Center = new Vec2(c.Center.X, c.Center.Y), Radius = c.Radius, StartAngle = 0, Sweep = Angles.TwoPi };
            CoreArc? best = null; double bd = double.MaxValue;
            foreach (var s in EntityOps.SpansOf(e))
            {
                if (!s.IsArc) continue;
                double d = s.DistanceAndSide(model, out _);
                if (d >= bd) continue;
                bd = d;
                double start = Math.Atan2(s.A.Y - s.Center.Y, s.A.X - s.Center.X);
                best = new CoreArc { Center = s.Center, Radius = s.Radius, StartAngle = start, Sweep = s.Sweep, Start = s.A, End = s.B };
            }
            return best;
        }

        /// <summary>Starts a tool that takes repeated picks until Esc/right-click/blank.</summary>
        private void PickLoop(string name, string prompt, string intro, Action<Vec2> onPick, Action? onEnd = null)
        {
            if (_activeTool.Length > 0) EndTool();
            BeginTool(name);
            _prompt.Text = prompt;
            Log(name + "  " + intro);
            _awaitingPoint = onPick;
            _awaitingLine = s => { if (s.Length == 0) { EndTool(); onEnd?.Invoke(); } else Log("  pick, or blank / Esc to end"); };
        }

        /// <summary>Asks for a line of text; blank cancels unless <paramref name="blankOk"/>.</summary>
        private void AskText(string prompt, Action<string> then, bool blankOk = false)
        {
            _canvas.ToolActive = false; _canvas.RubberFrom = null;
            _awaitingPoint = null;
            _prompt.Text = prompt;
            _awaitingLine = s =>
            {
                if (s.Length == 0 && !blankOk) { EndTool(); Log("  *cancelled*"); return; }
                then(s);
            };
        }

        private static bool TryNumber(string s, out double v) => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        // =============================================================================================
        // FD Labels
        // =============================================================================================

        private static readonly Dictionary<CourseLabelStyle, (string Command, string Name)> AnnotateNames = new Dictionary<CourseLabelStyle, (string, string)>
        {
            [CourseLabelStyle.SplitBearing] = ("SPLITBRG", "Auto split bearing"),
            [CourseLabelStyle.BearingOnLine] = ("BRGON", "Bearing on centre of line"),
            [CourseLabelStyle.BearingOffLine] = ("BRGOFF", "Auto bearing off line"),
            [CourseLabelStyle.DistanceOnLine] = ("DISTON", "Auto distance"),
            [CourseLabelStyle.DistanceOffLine] = ("DISTOFF", "Auto distance off line"),
            [CourseLabelStyle.BearingDistance] = ("BRGDIST", "Auto bearing/distance"),
            [CourseLabelStyle.BearingDashDistance] = ("BRGDASH", "Auto bearing - distance"),
            [CourseLabelStyle.DistanceDashBearing] = ("DISTDASH", "Auto distance - bearing"),
            [CourseLabelStyle.BearingOverDistance] = ("BRGDISTL", "Auto bearing/distance // line"),
            [CourseLabelStyle.DistanceOverBearing] = ("DISTBRGL", "Auto distance/bearing // line"),
        };

        /// <summary>
        /// One of the FD Labels course tools: pick a line or polyline span, it's labelled in the
        /// chosen style (on the picked side where that matters, the line broken around the text for
        /// the "on line" styles); a curve gets its curve data. Repeats until Esc / right-click.
        /// </summary>
        private void StartAnnotate(CourseLabelStyle style, bool curvesOnly = false)
        {
            if (!NeedDrawing()) return;
            var std = LabelStandards();
            double mpm = LabelModelPerMm(std, out string basis);
            double g2g = GridToGround(std);
            var (command, name) = curvesOnly ? ("CURVEON", "Label on curve") : AnnotateNames[style];
            bool side = !curvesOnly && style != CourseLabelStyle.BearingOnLine && style != CourseLabelStyle.DistanceOnLine && style != CourseLabelStyle.BearingDistance;
            int count = 0;
            PickLoop(command, name + " - pick " + (curvesOnly ? "an arc" : "a line" + (side ? " on the side the label goes" : "")) + ":",
                "pick " + (curvesOnly ? "arcs (or polyline curves)" : "lines") + " to label (scale from " + basis + "); Esc or right-click ends",
                p =>
                {
                    var model = ModelOf(p);
                    if (model == null) return;
                    double tol = Math.Max(10 / _canvas.View.Zoom, 1e-6);
                    Entity? best = null; double bestD = tol;
                    foreach (var e in CurrentEntityOwner().Entities)
                    {
                        if (!IsCourse(e) || (e is Circle && !(e is Arc))) continue;
                        foreach (var sp in EntityOps.SpansOf(e))
                        {
                            if (curvesOnly && !sp.IsArc) continue;
                            double d = sp.DistanceAndSide(model.Value, out _);
                            if (d < bestD) { bestD = d; best = e; }
                        }
                    }
                    if (best == null) { Log(curvesOnly ? "  no curve there - pick on an arc" : "  no line there - pick on a line or polyline"); return; }
                    var cmd = CourseLabelling.Annotate(best, model.Value, style, _doc!, std, mpm, GetOrCreateLayer, out string note, g2g);
                    if (cmd == null) { Log("  can't label that"); return; }
                    count++;
                    Commit(cmd, "  labelled" + (note.Length > 0 ? " (" + note + ")" : "") + "  (Ctrl+Z undoes it)");
                },
                () => Log("  *" + count + " labelled*"));
        }

        /// <summary>ADDANGLE: pick the two lines (on the parts that form the angle), then where the
        /// angle's text goes; an arc marks the angle.</summary>
        private void StartAddAngle()
        {
            if (!NeedDrawing()) return;
            var std = LabelStandards();
            double mpm = ModelPerMm();
            (Vec2 A, Vec2 B, Vec2 Pick)? first = null, second = null;
            PickLoop("ADDANGLE", "Add angle - pick the first line:", "pick the first line, the second line, then where the angle goes (Esc ends)", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                if (first != null && second != null)
                {
                    var r = SurveyCalcs.AngleBetween(first.Value.A, first.Value.B, first.Value.Pick, second.Value.A, second.Value.B, second.Value.Pick);
                    if (r == null) { Log("  those lines are parallel - no angle between them"); first = second = null; _prompt.Text = "Add angle - pick the first line:"; return; }
                    var (v, start, sweep) = r.Value;
                    double rad = Math.Max(Vec2.Distance(v, model.Value) * 0.6, std.BearingTextMm * mpm * 2);
                    var arc = new CoreArc { Center = v, Radius = rad, StartAngle = start, Sweep = sweep };
                    var list = new List<Entity> { SurveyDrafting.ToEntity(arc, GetOrCreateLayer(std.BearingLayer)) };
                    list.Add(CourseLabelling.ToEntity(new DraftText
                    {
                        Text = SurveyCalcs.Dms(sweep, std.BearingSecondsDecimals), Position = model.Value, HeightMm = std.BearingTextMm, H = HAlign.Center, V = VAlign.Middle, Rotation = -Angles.ViewTwist,
                        Layer = std.BearingLayer, Style = std.TextStyle("bearing"),
                    }, _doc!, mpm, GetOrCreateLayer));
                    AddEntities(list, "Add angle", "  angle " + SurveyCalcs.Dms(sweep, std.BearingSecondsDecimals) + "  (Ctrl+Z undoes it)");
                    first = second = null;
                    _prompt.Text = "Add angle - pick the first line:";
                    return;
                }
                var e = PickEntity(p, x => x is Line || x is LwPolyline || x is Polyline2D);
                var span = e == null ? null : StraightSpanNear(e, model.Value);
                if (span == null) { Log("  no line there - pick on a line"); return; }
                if (first == null) { first = (span.Value.A, span.Value.B, model.Value); _prompt.Text = "Add angle - pick the second line:"; return; }
                second = (span.Value.A, span.Value.B, model.Value);
                _prompt.Text = "Add angle - pick where the angle text goes:";
            });
        }

        /// <summary>ARROWLINE: pick a line on one side - an arrow along it on that side, just clear of
        /// where its labels sit, arrowheads both ends.</summary>
        private void StartArrowLine()
        {
            if (!NeedDrawing()) return;
            var std = LabelStandards();
            double mpm = ModelPerMm();
            PickLoop("ARROWLINE", "Arrows on line - pick a line on the side the arrow goes:", "pick lines on the side the arrow goes (Esc ends)", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                var e = PickEntity(p, x => x is Line || x is LwPolyline || x is Polyline2D);
                var span = e == null ? null : StraightSpanNear(e, model.Value);
                if (span == null) { Log("  no line there - pick on a line"); return; }
                var (a, b) = span.Value;
                var u = (b - a).Normalized();
                var n = u.Left();
                if (Vec2.Dot(model.Value - a, n) < 0) n = n * -1;
                // Clear of a label on that side: the gap, a text height, and the gap again.
                double off = (std.BearingTextMm * (1 + 2 * std.LabelGapFactor) + std.LabelGapFactor * std.BearingTextMm) * mpm;
                double len = Vec2.Distance(a, b);
                var mid = (a + b) * 0.5 + n * off;
                var s = mid - u * (len * 0.3); var t = mid + u * (len * 0.3);
                AddEntities(SurveyDrafting.ArrowLine(s, t, _settings.ArrowMm * mpm, true, GetOrCreateLayer(CurrentLayer())), "Arrows on line", "  arrow added  (Ctrl+Z undoes it)");
            });
        }

        /// <summary>CURVEOFF: pick a curve, then where its data goes - a stack of R, A, Δ, C and CB.</summary>
        private void StartCurveOff()
        {
            if (!NeedDrawing()) return;
            var std = LabelStandards();
            double mpm = ModelPerMm();
            CoreArc? arc = null;
            PickLoop("CURVEOFF", "Label off curve - pick the curve:", "pick a curve, then where its data goes (Esc ends)", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                if (arc == null)
                {
                    var e = PickEntity(p, x => x is Arc || x is LwPolyline || x is Polyline2D);
                    arc = e == null ? null : ArcNear(e, model.Value);
                    if (arc == null) { Log("  no curve there - pick on an arc"); return; }
                    _prompt.Text = "Label off curve - pick where the curve data goes:";
                    _canvas.RubberFrom = p;
                    return;
                }
                var lines = SurveyDrafting.CurveData(arc, std, GridToGround(std));
                double step = std.ArcTextMm * 1.6 * mpm;
                var list = new List<Entity>();
                for (int i = 0; i < lines.Count; i++)
                    list.Add(CourseLabelling.ToEntity(new DraftText
                    {
                        // Stacked down the plan as it's seen (Surveyor View), each line level.
                        Text = lines[i], Position = model.Value - new Vec2(-Math.Sin(-Angles.ViewTwist), Math.Cos(-Angles.ViewTwist)) * (step * i), HeightMm = std.ArcTextMm, H = HAlign.Left, V = VAlign.Top, Rotation = -Angles.ViewTwist,
                        Layer = std.ArcLayer, Style = std.TextStyle("arc"), Kind = TextKind.ArcData,
                    }, _doc!, mpm, GetOrCreateLayer));
                AddEntities(list, "Curve data", "  curve data placed  (Ctrl+Z undoes it)");
                arc = null; _canvas.RubberFrom = null;
                _prompt.Text = "Label off curve - pick the curve:";
            });
        }

        /// <summary>TEXTARC: pick an arc where the text should sit (outside or inside it), type the text.</summary>
        private void StartTextOnArc()
        {
            if (!NeedDrawing()) return;
            var std = LabelStandards();
            double mpm = ModelPerMm();
            PickLoop("TEXTARC", "Text on arc - pick the arc, outside or inside where the text goes:", "pick an arc on the side and at the place the text goes, then type the text", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                var e = PickEntity(p, x => x is Arc || x is Circle || x is LwPolyline || x is Polyline2D, 14);
                var arc = e == null ? null : ArcNear(e, model.Value);
                if (arc == null) { Log("  no arc there - pick on (or just beside) an arc"); return; }
                double mid = Math.Atan2(model.Value.Y - arc.Center.Y, model.Value.X - arc.Center.X);
                bool outside = Vec2.Distance(model.Value, arc.Center) >= arc.Radius;
                double h = std.DistanceTextMm * mpm, gap = std.DistanceTextMm * std.LabelGapFactor * mpm;
                bool over = Math.Sin(mid) >= 0;
                // Baseline radius: characters stand on it, their tops away from the centre over the top.
                double baseR = over ? (outside ? arc.Radius + gap : arc.Radius - gap - h) : (outside ? arc.Radius + gap + h : arc.Radius - gap);
                AskText("Text on arc - text:", s =>
                {
                    var list = SurveyDrafting.TextOnArc(s, arc.Center, baseR, mid, std.DistanceTextMm, mpm, CurrentLayer(), std.TextStyle("distance"), _doc!, GetOrCreateLayer);
                    AddEntities(list, "Text on arc", "  text placed along the arc  (Ctrl+Z undoes it)");
                    StartTextOnArc();
                });
            });
        }

        // =============================================================================================
        // FD Ties
        // =============================================================================================

        private void StartAutoHouseTie(bool arrows)
        {
            if (!NeedDrawing()) return;
            var std = LabelStandards();
            double mpm = ModelPerMm();
            List<Vec2>? building = null;
            int n = 0;
            string name = arrows ? "HOUSETIEA" : "HOUSETIE";
            PickLoop(name, "House tie - pick the building:", "pick the building (a polyline), then each lot line to tie it to (Esc ends)", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                if (building == null)
                {
                    var e = PickEntity(p, x => x is LwPolyline || x is Polyline2D);
                    if (e == null) { Log("  pick the building outline (a polyline)"); return; }
                    building = SurveyDrafting.Vertices(e);
                    _prompt.Text = "House tie - pick a lot line (Esc ends):";
                    Log("  building of " + building.Count + " corners - now pick the lot lines");
                    return;
                }
                var lot = PickEntity(p, x => x is Line || x is LwPolyline || x is Polyline2D);
                var span = lot == null ? null : StraightSpanNear(lot, model.Value);
                if (span == null) { Log("  no lot line there"); return; }
                var tie = SurveyDrafting.AutoTie(building, span.Value.A, span.Value.B);
                if (tie == null) return;
                var list = SurveyDrafting.HouseTie(tie.Value.Corner, tie.Value.Foot, arrows, _settings.ArrowMm * mpm, std.DistanceTextMm, mpm, Decimals(),
                    GetOrCreateLayer(CurrentLayer()), CurrentLayer(), std.TextStyle("distance"), _doc!, GetOrCreateLayer);
                n++;
                AddEntities(list, "House tie", "  tie " + F(Vec2.Distance(tie.Value.Corner, tie.Value.Foot), Decimals()) + "  (Ctrl+Z undoes it)");
            }, () => Log("  *" + n + " ties*"));
        }

        private void StartManualHouseTie(bool arrows)
        {
            if (!NeedDrawing()) return;
            var std = LabelStandards();
            double mpm = ModelPerMm();
            Vec2? corner = null;
            string name = arrows ? "MHOUSETIEA" : "MHOUSETIE";
            PickLoop(name, "House tie - pick the building corner (snap):", "pick a building corner (snap), then the lot line - a square tie (Esc ends)", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                if (corner == null) { corner = model; _canvas.RubberFrom = p; _prompt.Text = "House tie - pick the lot line:"; return; }
                var lot = PickEntity(p, x => x is Line || x is LwPolyline || x is Polyline2D);
                var span = lot == null ? null : StraightSpanNear(lot, model.Value);
                if (span == null) { Log("  no lot line there"); return; }
                var foot = SurveyCalcs.Foot(corner.Value, span.Value.A, span.Value.B);
                var list = SurveyDrafting.HouseTie(corner.Value, foot, arrows, _settings.ArrowMm * mpm, std.DistanceTextMm, mpm, Decimals(),
                    GetOrCreateLayer(CurrentLayer()), CurrentLayer(), std.TextStyle("distance"), _doc!, GetOrCreateLayer);
                AddEntities(list, "House tie", "  tie " + F(Vec2.Distance(corner.Value, foot), Decimals()) + "  (Ctrl+Z undoes it)");
                corner = null; _canvas.RubberFrom = null;
                _prompt.Text = "House tie - pick the next building corner (Esc ends):";
            });
        }

        private void LeaderScale(string arg)
        {
            void Set(string s)
            {
                if (!TryNumber(s, out double mm) || mm <= 0) { Log("  type the arrow size in paper millimetres, e.g. 2.5"); return; }
                _settings.ArrowMm = mm; _settings.Save();
                EndTool();
                Log("  arrows, ties and leaders now draw " + mm.ToString("0.##", CultureInfo.InvariantCulture) + " mm arrowheads on paper");
            }
            if (arg.Length > 0) { Set(arg); return; }
            BeginTool("LEADERSCALE");
            AskText("Leader scale - arrow size in paper mm <" + _settings.ArrowMm.ToString("0.##", CultureInfo.InvariantCulture) + ">:", s => { if (s.Length == 0) { EndTool(); return; } Set(s); }, blankOk: true);
        }

        private void StartCurvyLeader()
        {
            if (!NeedDrawing()) return;
            var pts = new List<Vec2>();
            string layer = CurrentLayer();
            PickLoop("CURVYLEADER", "Curvy leader - the point it points to:", "pick the point, then points along the curve; blank ends and asks for the text", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                pts.Add(model.Value);
                _canvas.RubberFrom = p;
                _prompt.Text = "Curvy leader - next point (blank ends):";
            });
            _awaitingLine = s =>
            {
                if (s.Length > 0 && pts.Count >= 2) { FinishCurvyLeader(pts, layer, s); return; }
                if (pts.Count < 2) { EndTool(); Log("  *cancelled - a leader needs two points*"); return; }
                AskText("Curvy leader - text (blank for none):", t => FinishCurvyLeader(pts, layer, t), blankOk: true);
            };
        }

        private void FinishCurvyLeader(List<Vec2> pts, string layer, string text)
        {
            var leader = new Leader { ArrowHeadEnabled = true, Layer = GetOrCreateLayer(layer), Style = DimensionStyle.Default, PathType = pts.Count > 2 ? LeaderPathType.Spline : LeaderPathType.StraightLineSegments };
            foreach (var q in pts) leader.Vertices.Add(new XYZ(q.X, q.Y, 0));
            var list = new List<Entity> { leader };
            if (text.Length > 0)
            {
                ParseHeightAndText(text, out double h, out string content);
                h = DefaultTextHeight(h, text);
                list.Add(new TextEntity { Value = content, InsertPoint = new XYZ(pts[pts.Count - 1].X, pts[pts.Count - 1].Y, 0), Height = h, Rotation = -Angles.ViewTwist, Layer = GetOrCreateLayer(layer) });
            }
            EndTool();
            AddEntities(list, "Leader", "  leader placed");
        }

        /// <summary>The drawing's insertable blocks: not layouts, not anonymous.</summary>
        private List<BlockRecord> InsertableBlocks() =>
            _doc == null ? new List<BlockRecord>() : _doc.BlockRecords.Where(b => !b.Name.StartsWith("*") && b.Layout == null && !b.IsAnonymous)
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();

        private void ChooseBlock(string arg, Action<BlockRecord> then)
        {
            var blocks = InsertableBlocks();
            if (blocks.Count == 0) { EndTool(); Log("  this drawing has no blocks to insert - the firm template's monument blocks come with a drafted job"); return; }
            BlockRecord? Find(string s)
            {
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) && i >= 1 && i <= blocks.Count) return blocks[i - 1];
                return blocks.FirstOrDefault(b => b.Name.Equals(s, StringComparison.OrdinalIgnoreCase))
                    ?? blocks.FirstOrDefault(b => b.Name.StartsWith(s, StringComparison.OrdinalIgnoreCase));
            }
            if (arg.Length > 0 && Find(arg) is BlockRecord pre) { then(pre); return; }
            Log("  blocks: " + string.Join("  ", blocks.Select((b, i) => (i + 1) + " " + b.Name)));
            AskText("Block - number or name:", s =>
            {
                var b = Find(s.Trim());
                if (b == null) { Log("  no block " + s + " - type a number or name from the list"); return; }
                then(b);
            });
        }

        private double BlockScale() => LabelStandards().BlockUnitMm * ModelPerMm();

        private void StartInsertBlock(string arg)
        {
            if (!NeedDrawing()) return;
            if (_activeTool.Length > 0) EndTool();
            BeginTool("QPOST");
            ChooseBlock(arg, block =>
            {
                double k = BlockScale();
                int n = 0;
                PickLoop("QPOST", "Post " + block.Name + " - pick where it goes (Esc ends):", "inserting " + block.Name + " at 1:" + F(k, 4) + " - pick each spot (snap to the points); Esc ends", p =>
                {
                    var model = ModelOf(p);
                    if (model == null) return;
                    var ins = new Insert(block) { InsertPoint = new XYZ(model.Value.X, model.Value.Y, 0), XScale = k, YScale = k, ZScale = k, Layer = GetOrCreateLayer(CurrentLayer()) };
                    n++;
                    AddEntities(new List<Entity> { ins }, "Insert " + block.Name, "  " + block.Name + " at " + NE(model.Value));
                }, () => Log("  *" + n + " inserted*"));
            });
        }

        private void StartBlockLine()
        {
            if (!NeedDrawing()) return;
            if (_activeTool.Length > 0) EndTool();
            BeginTool("BLOCKLINE");
            ChooseBlock("", block =>
            {
                Vec2? a = null;
                _canvas.ToolActive = true;
                _prompt.Text = "Line of blocks - start point:";
                _awaitingLine = null;
                _awaitingPoint = p =>
                {
                    var model = ModelOf(p);
                    if (model == null) return;
                    if (a == null) { a = model; _canvas.RubberFrom = p; _prompt.Text = "Line of blocks - end point:"; return; }
                    var b = model.Value;
                    double len = Vec2.Distance(a.Value, b);
                    if (len < 1e-9) { Log("  pick a different end point"); return; }
                    AskText("Line of blocks - spacing (or \"n=5\" for a count):", s =>
                    {
                        int count; double step;
                        var t = s.Trim().ToLowerInvariant();
                        if (t.StartsWith("n=") && int.TryParse(t.Substring(2), out int nn) && nn >= 2) { count = nn; step = len / (nn - 1); }
                        else if (TryNumber(t, out double sp) && sp > 0) { count = (int)Math.Floor(len / sp + 1e-9) + 1; step = sp; }
                        else { Log("  type a spacing, or n=count"); return; }
                        double k = BlockScale(), rot = Math.Atan2(b.Y - a.Value.Y, b.X - a.Value.X);
                        var u = (b - a.Value).Normalized();
                        var list = new List<Entity>();
                        for (int i = 0; i < count; i++)
                        {
                            var q = a.Value + u * (step * i);
                            list.Add(new Insert(block) { InsertPoint = new XYZ(q.X, q.Y, 0), XScale = k, YScale = k, ZScale = k, Rotation = rot, Layer = GetOrCreateLayer(CurrentLayer()) });
                        }
                        EndTool();
                        AddEntities(list, "Line of blocks", "  " + count + " × " + block.Name + " along " + F(len) + "  (Ctrl+Z undoes it)");
                    });
                };
            });
        }

        private enum TableKind { Lines, Curves, Ties }
        private int _nextLineTag = 1, _nextCurveTag = 1, _nextTieTag = 1;

        /// <summary>LINETABLE / CURVETABLE / TIETABLE: the selected courses get tags (L1, C1, T1...)
        /// and a table of their data goes where you pick.</summary>
        private void StartTable(TableKind kind)
        {
            if (!NeedDrawing()) return;
            var std = LabelStandards();
            double mpm = ModelPerMm(), g2g = GridToGround(std);
            var sel = SelectedEntities().Where(IsCourse).ToList();
            var straight = new List<(Vec2 A, Vec2 B)>();
            var curves = new List<CoreArc>();
            foreach (var e in sel)
            {
                if (e is Arc a) { curves.Add(SurveyDrafting.ToCore(a)); continue; }
                foreach (var s in EntityOps.SpansOf(e))
                {
                    if (!s.IsArc) straight.Add((s.A, s.B));
                    else
                    {
                        double start = Math.Atan2(s.A.Y - s.Center.Y, s.A.X - s.Center.X);
                        curves.Add(new CoreArc { Center = s.Center, Radius = s.Radius, StartAngle = start, Sweep = s.Sweep, Start = s.A, End = s.B });
                    }
                }
            }
            bool wantCurves = kind == TableKind.Curves;
            if (wantCurves ? curves.Count == 0 : straight.Count == 0)
            {
                Log("  select the " + (wantCurves ? "arcs" : kind == TableKind.Ties ? "tie lines" : "lines") + " to tabulate first, then run it again");
                return;
            }
            string prefix = kind == TableKind.Lines ? "L" : kind == TableKind.Curves ? "C" : "T";
            string tagLayer = wantCurves ? std.ArcLayer : std.DistanceLayer;
            string style = std.TextStyle("distance");
            string f = "F" + Math.Max(0, std.DistanceDecimals);
            var rows = new List<IList<string>>();
            var tags = new List<Entity>();
            int next = kind == TableKind.Lines ? _nextLineTag : kind == TableKind.Curves ? _nextCurveTag : _nextTieTag;
            if (wantCurves)
                foreach (var c in curves)
                {
                    string tag = prefix + next++;
                    var d = SurveyDrafting.CurveData(c, std, g2g);
                    rows.Add(new List<string> { tag, d[0].Substring(2), d[1].Substring(2), d[2].Substring(2), d[3].Substring(2), d[4].Substring(3) });
                    var m = c.MidPoint;
                    var outward = (m - c.Center).Normalized();
                    tags.Add(SurveyDrafting.Tag(tag, m - outward.Left(), m + outward.Left(), std.ArcTextMm, mpm, tagLayer, style, _doc!, GetOrCreateLayer));
                }
            else
                foreach (var (a, b) in straight)
                {
                    string tag = prefix + next++;
                    rows.Add(new List<string> { tag, BearingText(a, b), (Vec2.Distance(a, b) * g2g).ToString(f, CultureInfo.InvariantCulture) });
                    tags.Add(SurveyDrafting.Tag(tag, a, b, std.DistanceTextMm, mpm, tagLayer, style, _doc!, GetOrCreateLayer));
                }
            string title = kind == TableKind.Lines ? "LINE TABLE" : kind == TableKind.Curves ? "CURVE TABLE" : "TIE TABLE";
            IList<string> header = wantCurves ? new List<string> { "CURVE", "RADIUS", "ARC", "DELTA", "CHORD", "CHORD BEARING" } : new List<string> { kind == TableKind.Ties ? "TIE" : "LINE", "BEARING", "DISTANCE" };
            PickLoop(title.Replace(" ", ""), title + " - pick the table's top-left corner:", rows.Count + " rows tagged " + prefix + (next - rows.Count) + "-" + prefix + (next - 1) + " - pick where the table goes", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                var list = new List<Entity>(tags);
                var table = SurveyDrafting.Table(title, header, rows, model.Value, std.DistanceTextMm, mpm, CurrentLayer(), CurrentLayer(), style, _doc!, GetOrCreateLayer);
                if (Math.Abs(Angles.ViewTwist) > 1e-12)
                {
                    // Square to the plan as it's seen (Surveyor View): turned about its top-left corner.
                    var pivot = Matrix4.CreateTranslation(new XYZ(model.Value.X, model.Value.Y, 0)) * Matrix4.CreateRotationMatrix(new XYZ(0, 0, -Angles.ViewTwist))
                        * Matrix4.CreateTranslation(new XYZ(-model.Value.X, -model.Value.Y, 0));
                    foreach (var te in table) EntityTransform.Apply(te, new Transform(pivot));
                }
                list.AddRange(table);
                if (kind == TableKind.Lines) _nextLineTag = next; else if (kind == TableKind.Curves) _nextCurveTag = next; else _nextTieTag = next;
                EndTool();
                AddEntities(list, title, "  " + title.ToLowerInvariant() + " of " + rows.Count + " placed  (Ctrl+Z undoes it)");
            });
        }
    }
}
