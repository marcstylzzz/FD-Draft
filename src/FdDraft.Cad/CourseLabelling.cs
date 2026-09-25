using System;
using System.Collections.Generic;
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
        public static List<Entity> For(Entity e, CadDocument doc, FirmStandards std, double modelPerMm, Func<string, Layer> layer, double gridToGround = 1.0)
        {
            var texts = new List<DraftText>();
            switch (e)
            {
                case Line l:
                    texts.AddRange(Annotator.StraightCourseLabels(new Vec2(l.StartPoint.X, l.StartPoint.Y), new Vec2(l.EndPoint.X, l.EndPoint.Y), 0.5, std, modelPerMm, gridToGround, std.BearingLayer, std.DistanceLayer));
                    break;
                case ACadSharp.Entities.Arc a:
                {
                    double sweep = a.EndAngle - a.StartAngle;
                    while (sweep <= 0) sweep += Angles.TwoPi;
                    texts.AddRange(Annotator.ArcCourseLabels(ToCore(new Vec2(a.Center.X, a.Center.Y), a.Radius, a.StartAngle, sweep), 0.5, std, modelPerMm, gridToGround, std.ArcLayer));
                    break;
                }
                case LwPolyline lp when lp.Vertices.Count >= 2:
                    texts.AddRange(ForSpans(Construct.Spans(lp.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList(), lp.Vertices.Select(v => v.Bulge).ToList(), lp.IsClosed), std, modelPerMm, gridToGround));
                    break;
                case Polyline2D p2 when p2.Vertices.Count >= 2:
                    texts.AddRange(ForSpans(Construct.Spans(p2.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList(), p2.Vertices.Select(v => v.Bulge).ToList(), p2.IsClosed), std, modelPerMm, gridToGround));
                    break;
            }
            return texts.Select(t => (Entity)ToEntity(t, doc, modelPerMm, layer)).ToList();
        }

        private static IEnumerable<DraftText> ForSpans(IEnumerable<Construct.Span> spans, FirmStandards std, double modelPerMm, double gridToGround)
        {
            foreach (var s in spans)
            {
                if (Vec2.Distance(s.A, s.B) < 1e-9) continue;
                if (!s.IsArc)
                    foreach (var t in Annotator.StraightCourseLabels(s.A, s.B, 0.5, std, modelPerMm, gridToGround, std.BearingLayer, std.DistanceLayer)) yield return t;
                else
                {
                    // Core arcs carry a signed sweep, so the chord bearing runs the polyline's way.
                    double start = Math.Atan2(s.A.Y - s.Center.Y, s.A.X - s.Center.X);
                    foreach (var t in Annotator.ArcCourseLabels(ToCore(s.Center, s.Radius, start, s.Sweep), 0.5, std, modelPerMm, gridToGround, std.ArcLayer)) yield return t;
                }
            }
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
