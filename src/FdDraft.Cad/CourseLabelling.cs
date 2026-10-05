using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;
using FdDraft.Core.Standards;
using CoreArc = FdDraft.Core.Geometry.Arc;

namespace FdDraft.Cad.Editing
{
    /// <summary>
    /// LABEL: bearing/distance (or curve-data) labels for linework drawn or edited by hand,
    /// built by exactly the same rules the drafting pipeline's Annotator uses
    /// (<see cref="Annotator.StraightCourseLabels"/>, <see cref="Annotator.ArcCourseLabels"/>):
    /// the firm's text heights, layers, styles, gap, bearing format and rotation.
    /// </summary>
    public static class CourseLabelling
    {
        /// <summary>
        /// Labels for one Line, Arc, or every span of an LwPolyline/Polyline2D. Heights are the
        /// standards' paper millimetres times <paramref name="modelPerMm"/>. Layers are created
        /// on the fly through <paramref name="layer"/> when the drawing lacks them.
        /// </summary>
        public static List<Entity> For(Entity e, CadDocument doc, FirmStandards std, double modelPerMm, Func<string, Layer> layer, double gridToGround = 1.0) =>
            ForLinked(e, doc, std, modelPerMm, layer, gridToGround).Select(x => x.Label).ToList();

        /// <summary>
        /// <see cref="For"/>, with each label's span index and what it says, for
        /// <see cref="CourseLinks.Tag"/> once the labels are in the drawing.
        /// </summary>
        public static List<(Entity Label, int Span, string Kind)> ForLinked(Entity e, CadDocument doc, FirmStandards std, double modelPerMm, Func<string, Layer> layer, double gridToGround = 1.0)
        {
            var result = new List<(Entity, int, string)>();
            if (!(e is Line || e is ACadSharp.Entities.Arc || e is LwPolyline || e is Polyline2D)) return result;
            int i = 0;
            foreach (var s in EntityOps.SpansOf(e))
            {
                int span = i++;
                if (Vec2.Distance(s.A, s.B) < 1e-9) continue;
                if (!s.IsArc)
                {
                    var t = Annotator.StraightCourseLabels(s.A, s.B, 0.5, std, modelPerMm, gridToGround, std.BearingLayer, std.DistanceLayer);
                    result.Add((ToEntity(t[0], doc, modelPerMm, layer), span, CourseLinks.Kinds.Bearing));
                    result.Add((ToEntity(t[1], doc, modelPerMm, layer), span, CourseLinks.Kinds.Distance));
                }
                else
                {
                    // Core arcs carry a signed sweep, so the chord bearing runs the polyline's way.
                    var t = Annotator.ArcCourseLabels(ToCore(s), 0.5, std, modelPerMm, gridToGround, std.ArcLayer);
                    result.Add((ToEntity(t[0], doc, modelPerMm, layer), span, CourseLinks.Kinds.ArcOuter));
                    result.Add((ToEntity(t[1], doc, modelPerMm, layer), span, CourseLinks.Kinds.ArcInner));
                }
            }
            return result;
        }

        private static CoreArc ToCore(Vec2 c, double r, double start, double sweep) => new CoreArc
        {
            Center = c, Radius = r, StartAngle = start, Sweep = sweep,
            Start = new Vec2(c.X + r * Math.Cos(start), c.Y + r * Math.Sin(start)),
            End = new Vec2(c.X + r * Math.Cos(start + sweep), c.Y + r * Math.Sin(start + sweep)),
        };

        /// <summary>
        /// One of MSCAD's annotate styles applied to the course of <paramref name="e"/> nearest
        /// <paramref name="pick"/> (a Line, or a span of an LwPolyline/Polyline2D): its labels
        /// added and - for the "on line" styles - the line broken around the text (a Line becomes
        /// two; an LwPolyline is split at the gap, a closed one opened there). An arc gets its
        /// curve data instead. One undo step; null when there's no course there.
        /// </summary>
        public static IEditCommand? Annotate(Entity e, Vec2 pick, CourseLabelStyle style, CadDocument doc, FirmStandards std, double modelPerMm, Func<string, Layer> layer, out string note, double gridToGround = 1.0)
        {
            note = "";
            if (!(e.Owner is BlockRecord owner)) return null;
            var spans = EntityOps.SpansOf(e).ToList();
            if (spans.Count == 0 || e is Circle && !(e is ACadSharp.Entities.Arc)) return null;
            int si = 0; double best = double.MaxValue;
            for (int i = 0; i < spans.Count; i++)
            {
                double d = spans[i].DistanceAndSide(pick, out _);
                if (d < best) { best = d; si = i; }
            }
            var span = spans[si];
            var edits = new List<IEditCommand>();
            List<DraftText> texts;
            if (span.IsArc)
            {
                double start = Math.Atan2(span.A.Y - span.Center.Y, span.A.X - span.Center.X);
                texts = Annotator.ArcCourseLabels(ToCore(span.Center, span.Radius, start, span.Sweep), 0.5, std, modelPerMm, gridToGround, std.ArcLayer).ToList();
                note = "a curve - labelled with its curve data";
            }
            else
            {
                var r = CourseAnnotation.Layout(span.A, span.B, pick, style, std, modelPerMm, gridToGround);
                texts = r.Texts;
                if (r.GapFrom.HasValue && r.GapTo.HasValue)
                {
                    var g0 = span.A + (span.B - span.A) * r.GapFrom.Value;
                    var g1 = span.A + (span.B - span.A) * r.GapTo.Value;
                    var split = Split(e, owner, si, g0, g1, out note);
                    if (split != null) edits.Add(split);
                }
            }
            var entities = texts.Select(t => (Entity)ToEntity(t, doc, modelPerMm, layer)).ToList();
            edits.Insert(0, new AddEntitiesCommand(owner, entities, "Label"));
            // Labels beside an unbroken course are linked to it, so RELABEL can follow it.
            if (edits.Count == 1)
                for (int k = 0; k < entities.Count; k++)
                {
                    string kind = span.IsArc ? (k == 0 ? CourseLinks.Kinds.ArcOuter : CourseLinks.Kinds.ArcInner)
                        : style == CourseLabelStyle.BearingDashDistance ? CourseLinks.Kinds.BearingDistance
                        : style == CourseLabelStyle.DistanceBeforeBearing ? CourseLinks.Kinds.DistanceBearing
                        : style == CourseLabelStyle.SplitBearing ? (k == 0 ? CourseLinks.Kinds.SplitTop : CourseLinks.Kinds.SplitBottom)
                        : texts[k].Kind == TextKind.Distance ? CourseLinks.Kinds.Distance : CourseLinks.Kinds.Bearing;
                    CourseLinks.Tag(entities[k], e, si, kind);
                }
            return edits.Count == 1 ? edits[0] : new CompositeCommand(edits, "Label");
        }

        /// <summary>Breaks span <paramref name="span"/> of <paramref name="e"/> between g0 and g1.</summary>
        private static IEditCommand? Split(Entity e, BlockRecord owner, int span, Vec2 g0, Vec2 g1, out string note)
        {
            note = "";
            XYZ W(Vec2 v, double z) => new XYZ(v.X, v.Y, z);
            switch (e)
            {
                case Line l:
                {
                    var second = (Line)EntityOps.Duplicate(l);
                    second.StartPoint = W(g1, l.StartPoint.Z); second.EndPoint = l.EndPoint;
                    var cut = new SetPropertyCommand<XYZ>(l.EndPoint, W(g0, l.EndPoint.Z), v => l.EndPoint = v, "Label");
                    return new CompositeCommand(new IEditCommand[] { cut, new AddEntitiesCommand(owner, new Entity[] { second }, "Label") }, "Label");
                }
                case LwPolyline p:
                {
                    int n = p.Vertices.Count;
                    var v = p.Vertices.Select(x => (P: new Vec2(x.Location.X, x.Location.Y), x.Bulge)).ToList();
                    var pieces = new List<List<(Vec2 P, double Bulge)>>();
                    if (p.IsClosed)
                    {
                        // Opened at the gap: from its far side all the way round to its near side.
                        var ring = new List<(Vec2, double)> { (g1, 0) };
                        for (int k = 1; k <= n; k++) ring.Add(v[(span + k) % n]);
                        ring[ring.Count - 1] = (ring[ring.Count - 1].Item1, 0);
                        ring.Add((g0, 0));
                        pieces.Add(ring);
                    }
                    else
                    {
                        var first = v.Take(span + 1).ToList();
                        first[first.Count - 1] = (first[first.Count - 1].P, 0);
                        first.Add((g0, 0));
                        var rest = new List<(Vec2, double)> { (g1, 0) };
                        rest.AddRange(v.Skip(span + 1));
                        pieces.Add(first); pieces.Add(rest);
                    }
                    string? code = PointLinks.TaggedCode(p);
                    var made = pieces.Where(pc => pc.Count >= 2).Select(pc =>
                    {
                        var np = (LwPolyline)EntityOps.Duplicate(p);
                        np.IsClosed = false;
                        np.Vertices.Clear();
                        foreach (var (pt, bulge) in pc) np.Vertices.Add(new LwPolyline.Vertex(new XY(pt.X, pt.Y)) { Bulge = bulge, StartWidth = 0, EndWidth = 0 });
                        return (Entity)np;
                    }).ToList();
                    var add = new AddEntitiesCommand(owner, made, "Label");
                    if (code != null) foreach (var m in made) PointLinks.TagCode(m, code);
                    return new CompositeCommand(new IEditCommand[] { new RemoveEntitiesCommand(new Entity[] { p }, "Label"), add }, "Label");
                }
                default:
                    note = "this polyline type isn't broken around the text - the label sits on the line";
                    return null;
            }
        }

        // ---- the rest of MS Labels 1 ------------------------------------------------------------

        /// <summary>The span of <paramref name="e"/> nearest <paramref name="p"/>, if it has any.</summary>
        public static Construct.Span? NearestSpan(Entity e, Vec2 p)
        {
            Construct.Span? best = null; double bestD = double.MaxValue;
            foreach (var s in EntityOps.SpansOf(e))
            {
                double d = s.DistanceAndSide(p, out _);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        private static CoreArc ToCore(Construct.Span s) =>
            ToCore(s.Center, s.Radius, Math.Atan2(s.A.Y - s.Center.Y, s.A.X - s.Center.X), s.Sweep);

        /// <summary>
        /// "Add Angle between two lines": the angle where the straight courses of
        /// <paramref name="e1"/> and <paramref name="e2"/> picked at p1/p2 meet (extended if they
        /// don't reach), in the sector <paramref name="at"/> is in, the text at <paramref name="at"/>'s
        /// distance from the corner. Null when either pick isn't on a straight course or they're parallel.
        /// </summary>
        public static IEditCommand? AngleBetween(Entity e1, Vec2 p1, Entity e2, Vec2 p2, Vec2 at, CadDocument doc, FirmStandards std, double modelPerMm, Func<string, Layer> layer, out string why)
        {
            why = "";
            if (!(e1.Owner is BlockRecord owner)) { why = "not in a drawing"; return null; }
            var s1 = NearestSpan(e1, p1); var s2 = NearestSpan(e2, p2);
            if (s1 == null || s2 == null || s1.Value.IsArc || s2.Value.IsArc) { why = "pick two straight lines"; return null; }
            var u1 = s1.Value.B - s1.Value.A; var u2 = s2.Value.B - s2.Value.A;
            var corner = Construct.LineLine(s1.Value.A, u1, s2.Value.A, u2);
            if (corner == null) { why = "the lines are parallel"; return null; }
            var r = SurveyLabels.Angle(corner.Value, u1, u2, at, std, modelPerMm, std.BearingLayer);
            if (r == null) { why = "pick the label's place away from the corner"; return null; }
            var entities = new List<Entity> { ToEntity(r.Value.Text, doc, modelPerMm, layer), ToPolyline(r.Value.Arc, layer) };
            return new AddEntitiesCommand(owner, entities, "Angle");
        }

        /// <summary>"Add arrows to line offset equal to labels", for the course of <paramref name="e"/> picked at <paramref name="pick"/>.</summary>
        public static IEditCommand? ArrowsOnLine(Entity e, Vec2 pick, FirmStandards std, double modelPerMm, Func<string, Layer> layer, out string why)
        {
            why = "";
            if (!(e.Owner is BlockRecord owner)) { why = "not in a drawing"; return null; }
            var s = NearestSpan(e, pick);
            if (s == null || s.Value.IsArc) { why = "pick a straight line"; return null; }
            var pl = SurveyLabels.ArrowsAlong(s.Value.A, s.Value.B, pick, std, modelPerMm, std.DistanceLayer);
            if (pl == null) { why = "the line is too short for arrows at this scale"; return null; }
            return new AddEntitiesCommand(owner, new Entity[] { ToPolyline(pl, layer) }, "Arrows");
        }

        /// <summary>
        /// "Curve information follows arc only" (<paramref name="at"/> null): the curve data along the
        /// arc where it was picked. "Curve information placed anywhere in drawing": as a block of
        /// lines at <paramref name="at"/>.
        /// </summary>
        public static IEditCommand? CurveLabel(Entity e, Vec2 pick, Vec2? at, CadDocument doc, FirmStandards std, double modelPerMm, Func<string, Layer> layer, out string why, double gridToGround = 1.0)
        {
            why = "";
            if (!(e.Owner is BlockRecord owner)) { why = "not in a drawing"; return null; }
            var s = NearestSpan(e, pick);
            if (s == null || !s.Value.IsArc) { why = "pick an arc (or an arc span of a polyline)"; return null; }
            var arc = ToCore(s.Value);
            var texts = at.HasValue
                ? SurveyLabels.CurveDataBlock(arc, at.Value, std, modelPerMm, gridToGround, std.ArcLayer)
                : Annotator.ArcCourseLabels(arc, SurveyLabels.ParamOn(arc, pick), std, modelPerMm, gridToGround, std.ArcLayer).ToList();
            var made = texts.Select(t => (Entity)ToEntity(t, doc, modelPerMm, layer)).ToList();
            var cmd = new AddEntitiesCommand(owner, made, "Curve label");
            int si = EntityOps.SpansOf(e).TakeWhile(x => !(Vec2.Distance(x.A, s.Value.A) < 1e-9 && Vec2.Distance(x.B, s.Value.B) < 1e-9)).Count();
            for (int k = 0; k < made.Count; k++)
                CourseLinks.Tag(made[k], e, si, at.HasValue ? CourseLinks.Kinds.CurveLine + k.ToString(CultureInfo.InvariantCulture) : k == 0 ? CourseLinks.Kinds.ArcOuter : CourseLinks.Kinds.ArcInner);
            return cmd;
        }

        /// <summary>"Manually place any text to follow the curve": <paramref name="text"/> along the arc or circle of <paramref name="e"/>, centred where it was picked.</summary>
        public static IEditCommand? TextOnArc(Entity e, Vec2 pick, string text, double heightMm, string layerName, CadDocument doc, double modelPerMm, Func<string, Layer> layer, string style, out string why)
        {
            why = "";
            if (!(e.Owner is BlockRecord owner)) { why = "not in a drawing"; return null; }
            var s = NearestSpan(e, pick);
            if (s == null || !s.Value.IsArc) { why = "pick an arc or circle"; return null; }
            var texts = SurveyLabels.TextOnArc(s.Value.Center, s.Value.Radius, text, pick, heightMm, modelPerMm, layerName, style);
            if (texts.Count == 0) { why = "nothing to place"; return null; }
            return new AddEntitiesCommand(owner, texts.Select(t => (Entity)ToEntity(t, doc, modelPerMm, layer)).ToList(), "Text on arc");
        }

        /// <summary>A layout polyline as a DWG LWPOLYLINE, bulges and widths carried over.</summary>
        public static LwPolyline ToPolyline(DraftPolyline p, Func<string, Layer> layer)
        {
            var pl = new LwPolyline { Layer = layer(p.Layer), IsClosed = p.Closed };
            for (int i = 0; i < p.Vertices.Count; i++)
                pl.Vertices.Add(new LwPolyline.Vertex(new XY(p.Vertices[i].X, p.Vertices[i].Y))
                {
                    Bulge = i < p.Bulges.Count ? p.Bulges[i] : 0,
                    StartWidth = i < p.StartWidths.Count ? p.StartWidths[i] : 0,
                    EndWidth = i < p.EndWidths.Count ? p.EndWidths[i] : 0,
                });
            return pl;
        }

        /// <summary>A pipeline label as a DWG TEXT, aligned the way TemplateDrafter writes them.</summary>
        public static TextEntity ToEntity(DraftText t, CadDocument doc, double modelPerMm, Func<string, Layer> layer)
        {
            var at = new XYZ(t.Position.X, t.Position.Y, 0);
            var text = new TextEntity
            {
                Value = TemplateDrafter.AcadText(t.Text),
                InsertPoint = at,
                AlignmentPoint = at,
                Height = t.HeightMm * modelPerMm,
                Rotation = t.Rotation,
                HorizontalAlignment = t.H == HAlign.Left ? TextHorizontalAlignment.Left : t.H == HAlign.Center ? TextHorizontalAlignment.Center : TextHorizontalAlignment.Right,
                VerticalAlignment = t.V == VAlign.Bottom ? TextVerticalAlignmentType.Bottom : t.V == VAlign.Middle ? TextVerticalAlignmentType.Middle : TextVerticalAlignmentType.Top,
                Layer = layer(t.Layer),
            };
            if (!string.IsNullOrEmpty(t.Style) && doc.TextStyles.TryGetValue(t.Style, out var style)) text.Style = style;
            return text;
        }
    }
}
