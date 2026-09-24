using System;
using System.Collections.Generic;
using System.Linq;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using FdDraft.Core.Geometry;

namespace FdDraft.Cad.Editing
{
    /// <summary>
    /// Entity-level construction behind COPY, MIRROR and OFFSET: each returns brand-new
    /// entities (never touching the source), which the caller adds with
    /// <see cref="EntityOps.AddBesideSources"/> so undo is simply removing them again.
    /// </summary>
    public static class EntityOps
    {
        /// <summary>A detached copy of <paramref name="e"/> on the same layer and linetype.
        /// ACadSharp's Clone detaches table references (it clones the Layer); they are pointed
        /// back at the source's own entries so the copy doesn't carry stray table clones.</summary>
        public static Entity Duplicate(Entity e)
        {
            var c = (Entity)e.Clone();
            c.Layer = e.Layer;
            c.LineType = e.LineType;
            return c;
        }

        /// <summary>Copies of <paramref name="entities"/> moved by (dx, dy) - COPY.</summary>
        public static List<(Entity Source, Entity Copy)> Copies(IEnumerable<Entity> entities, double dx, double dy)
        {
            var list = new List<(Entity, Entity)>();
            foreach (var e in entities)
            {
                if (e.GetType() == typeof(DimensionAligned))
                {
                    var d = (DimensionAligned)e;
                    Vec2 T(XYZ p) => new Vec2(p.X + dx, p.Y + dy);
                    list.Add((e, DimensionBuilder.Rebuilt(d, T(d.FirstPoint), T(d.SecondPoint), T(d.DefinitionPoint))));
                    continue;
                }
                if (e is Dimension) continue; // other dimension kinds: their picture block is per-dimension
                var c = Duplicate(e);
                c.ApplyTransform(Transform.CreateTranslation(new XYZ(dx, dy, 0)));
                list.Add((e, c));
            }
            return list;
        }

        /// <summary>
        /// Adds each copy to the block its source lives in (Model space, or a sheet's own block
        /// for paper-native linework) as one undo step.
        /// </summary>
        public static IEditCommand? AddBesideSources(IEnumerable<(Entity Source, Entity Copy)> pairs, string description)
        {
            var byOwner = pairs
                .Where(p => p.Source.Owner is BlockRecord)
                .GroupBy(p => (BlockRecord)p.Source.Owner!)
                .ToList();
            if (byOwner.Count == 0) return null;
            var cmds = byOwner.Select(g => (IEditCommand)new AddEntitiesCommand(g.Key, g.Select(p => p.Copy), description)).ToList();
            return cmds.Count == 1 ? cmds[0] : new CompositeCommand(cmds, description);
        }

        // ---- MIRROR ----------------------------------------------------------------------------

        /// <summary>
        /// A mirrored copy of <paramref name="e"/> across the axis a→b, or null for an entity
        /// type MIRROR doesn't handle. Geometry is reflected exactly (arcs keep their centre
        /// and swap ends, polyline bulges change sign, blocks get a negative Y scale). Text is
        /// mirrored the way a plan needs it (AutoCAD's MIRRTEXT 0): its position and extent
        /// are reflected but it still reads forwards and never upside down.
        /// </summary>
        public static Entity? Mirrored(Entity e, Vec2 a, Vec2 b)
        {
            if (Vec2.Distance(a, b) < 1e-12) return null;
            double axis = Math.Atan2(b.Y - a.Y, b.X - a.X);
            XYZ R(XYZ p) { var q = Construct.Reflect(new Vec2(p.X, p.Y), a, b); return new XYZ(q.X, q.Y, p.Z); }
            XY R2(XY p) { var q = Construct.Reflect(new Vec2(p.X, p.Y), a, b); return new XY(q.X, q.Y); }

            switch (e)
            {
                case Line l:
                {
                    var c = (Line)Duplicate(l);
                    c.StartPoint = R(l.StartPoint); c.EndPoint = R(l.EndPoint);
                    return c;
                }
                case LwPolyline p:
                {
                    var c = (LwPolyline)Duplicate(p);
                    foreach (var v in c.Vertices) { v.Location = R2(v.Location); v.Bulge = -v.Bulge; }
                    return c;
                }
                case Polyline2D p2:
                {
                    var c = (Polyline2D)Duplicate(p2);
                    foreach (var v in c.Vertices) { v.Location = R(v.Location); v.Bulge = -v.Bulge; }
                    return c;
                }
                // Arc must come before Circle: Arc derives from Circle in ACadSharp.
                case ACadSharp.Entities.Arc arc:
                {
                    var c = (ACadSharp.Entities.Arc)Duplicate(arc);
                    c.Center = R(arc.Center);
                    // A counter-clockwise arc reflects into a clockwise one; walk it the other way.
                    double s = Construct.ReflectAngle(arc.EndAngle, axis);
                    double en = Construct.ReflectAngle(arc.StartAngle, axis);
                    c.StartAngle = s; c.EndAngle = en;
                    return c;
                }
                case Circle ci:
                {
                    var c = (Circle)Duplicate(ci);
                    c.Center = R(ci.Center);
                    return c;
                }
                case Point pt:
                {
                    var c = (Point)Duplicate(pt);
                    c.Location = R(pt.Location);
                    return c;
                }
                case TextEntity te:
                {
                    var c = (TextEntity)Duplicate(te);
                    MirrorText(c, a, b, axis);
                    return c;
                }
                case MText mt:
                {
                    var c = (MText)Duplicate(mt);
                    c.InsertPoint = R(mt.InsertPoint);
                    double rot = Construct.ReflectAngle(mt.Rotation, axis);
                    int ap = (int)mt.AttachmentPoint; // 1..9: row*3 + col, rows top/middle/bottom
                    int row = (ap - 1) / 3, col = (ap - 1) % 3;
                    row = 2 - row; // reflection turns the text's "up" into "down"
                    if (Construct.ReadsUpsideDown(rot)) { rot = Angles.Normalize2Pi(rot + Math.PI); row = 2 - row; col = 2 - col; }
                    c.AttachmentPoint = (AttachmentPointType)(row * 3 + col + 1);
                    c.AlignmentPoint = new XYZ(Math.Cos(rot), Math.Sin(rot), 0);
                    return c;
                }
                case Insert ins:
                {
                    var c = (Insert)Duplicate(ins);
                    c.InsertPoint = R(ins.InsertPoint);
                    // Reflect(axis)·Rot(r)·Scale(sx, sy) = Rot(2·axis − r)·Scale(sx, −sy).
                    c.Rotation = Construct.ReflectAngle(ins.Rotation, axis);
                    c.YScale = -ins.YScale;
                    foreach (var att in c.Attributes) MirrorText(att, a, b, axis);
                    return c;
                }
                case DimensionAligned da when da.GetType() == typeof(DimensionAligned):
                {
                    Vec2 V(XYZ p) => Construct.Reflect(new Vec2(p.X, p.Y), a, b);
                    return DimensionBuilder.Rebuilt(da, V(da.FirstPoint), V(da.SecondPoint), V(da.DefinitionPoint));
                }
                case Leader ld:
                {
                    var c = (Leader)Duplicate(ld);
                    for (int i = 0; i < c.Vertices.Count; i++) c.Vertices[i] = R(c.Vertices[i]);
                    return c;
                }
                default:
                    return null;
            }
        }

        /// <summary>Reflects a TEXT's anchor across the axis and turns it to run along the
        /// mirrored direction, re-anchoring (top/bottom, and left/right when it had to be
        /// turned 180° to stay readable) so it still covers the mirrored area.</summary>
        private static void MirrorText(TextEntity t, Vec2 a, Vec2 b, double axis)
        {
            bool aligned = t.HorizontalAlignment != TextHorizontalAlignment.Left || t.VerticalAlignment != TextVerticalAlignmentType.Baseline;
            var anchor = aligned ? t.AlignmentPoint : t.InsertPoint;
            var q = Construct.Reflect(new Vec2(anchor.X, anchor.Y), a, b);
            var at = new XYZ(q.X, q.Y, anchor.Z);
            double rot = Construct.ReflectAngle(t.Rotation, axis);
            var v = t.VerticalAlignment;
            var h = t.HorizontalAlignment;
            // Reflection turns "up" into "down": text that sat above its anchor now hangs below it.
            v = FlipVertical(v);
            if (Construct.ReadsUpsideDown(rot))
            {
                rot = Angles.Normalize2Pi(rot + Math.PI);
                v = FlipVertical(v);
                h = h == TextHorizontalAlignment.Left ? TextHorizontalAlignment.Right : h == TextHorizontalAlignment.Right ? TextHorizontalAlignment.Left : h;
            }
            t.Rotation = rot;
            t.HorizontalAlignment = h;
            t.VerticalAlignment = v;
            t.InsertPoint = at;
            t.AlignmentPoint = at;
        }

        private static TextVerticalAlignmentType FlipVertical(TextVerticalAlignmentType v) => v switch
        {
            TextVerticalAlignmentType.Top => TextVerticalAlignmentType.Bottom,
            TextVerticalAlignmentType.Bottom or TextVerticalAlignmentType.Baseline => TextVerticalAlignmentType.Top,
            _ => v,
        };

        // ---- OFFSET ----------------------------------------------------------------------------

        /// <summary>
        /// A parallel copy of a Line, Arc, Circle or LwPolyline, <paramref name="distance"/>
        /// away on the side of <paramref name="toward"/> - OFFSET. Polyline corners are mitred
        /// and arcs stay concentric (<see cref="Construct.OffsetPolyline"/>). Null when the
        /// entity type isn't supported or the offset would collapse an arc/circle.
        /// </summary>
        public static Entity? Offset(Entity e, double distance, Vec2 toward)
        {
            if (distance <= 0) return null;
            switch (e)
            {
                case Line l:
                {
                    var pa = new Vec2(l.StartPoint.X, l.StartPoint.Y);
                    var pb = new Vec2(l.EndPoint.X, l.EndPoint.Y);
                    if (Vec2.Distance(pa, pb) < 1e-12) return null;
                    double side = Construct.SideDistance(toward, pa, pb) >= 0 ? 1 : -1;
                    var n = (pb - pa).Normalized().Left() * (distance * side);
                    var c = (Line)Duplicate(l);
                    c.StartPoint = new XYZ(pa.X + n.X, pa.Y + n.Y, l.StartPoint.Z);
                    c.EndPoint = new XYZ(pb.X + n.X, pb.Y + n.Y, l.EndPoint.Z);
                    return c;
                }
                case ACadSharp.Entities.Arc arc:
                {
                    double r = OffsetRadius(arc.Center, arc.Radius, distance, toward);
                    if (r <= 1e-9) return null;
                    var c = (ACadSharp.Entities.Arc)Duplicate(arc);
                    c.Radius = r;
                    return c;
                }
                case Circle ci:
                {
                    double r = OffsetRadius(ci.Center, ci.Radius, distance, toward);
                    if (r <= 1e-9) return null;
                    var c = (Circle)Duplicate(ci);
                    c.Radius = r;
                    return c;
                }
                case LwPolyline p when p.Vertices.Count >= 2:
                {
                    var pts = p.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList();
                    var bulges = p.Vertices.Select(v => v.Bulge).ToList();
                    int side = Construct.SideOfPolyline(pts, bulges, p.IsClosed, toward);
                    var result = Construct.OffsetPolyline(pts, bulges, p.IsClosed, distance * side);
                    if (result == null) return null;
                    var c = (LwPolyline)Duplicate(p);
                    for (int i = 0; i < c.Vertices.Count; i++)
                    {
                        c.Vertices[i].Location = new XY(result.Value.Points[i].X, result.Value.Points[i].Y);
                        c.Vertices[i].Bulge = result.Value.Bulges[i];
                    }
                    return c;
                }
                default:
                    return null;
            }
        }

        // ---- spans of linework (TRIM/EXTEND edges, FLIP courses) ----------------------------

        /// <summary>An entity's linework as spans: a Line, an Arc, a Circle (as two half
        /// arcs), or every span of an LwPolyline/Polyline2D. Empty for anything else.</summary>
        public static IEnumerable<Construct.Span> SpansOf(Entity e)
        {
            switch (e)
            {
                case Line l:
                    return new[] { Construct.Span.Straight(new Vec2(l.StartPoint.X, l.StartPoint.Y), new Vec2(l.EndPoint.X, l.EndPoint.Y)) };
                case ACadSharp.Entities.Arc a:
                {
                    double sweep = a.EndAngle - a.StartAngle;
                    while (sweep <= 0) sweep += Angles.TwoPi;
                    var c = new Vec2(a.Center.X, a.Center.Y);
                    return new[] { Construct.Span.Arc(Polar(c, a.Radius, a.StartAngle), Polar(c, a.Radius, a.EndAngle), c, a.Radius, sweep) };
                }
                case Circle ci:
                {
                    var c = new Vec2(ci.Center.X, ci.Center.Y);
                    var e0 = Polar(c, ci.Radius, 0); var e1 = Polar(c, ci.Radius, Math.PI);
                    return new[] { Construct.Span.Arc(e0, e1, c, ci.Radius, Math.PI), Construct.Span.Arc(e1, e0, c, ci.Radius, Math.PI) };
                }
                case LwPolyline lp when lp.Vertices.Count >= 2:
                    return Construct.Spans(lp.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList(), lp.Vertices.Select(v => v.Bulge).ToList(), lp.IsClosed);
                case Polyline2D p2 when p2.Vertices.Count >= 2:
                    return Construct.Spans(p2.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList(), p2.Vertices.Select(v => v.Bulge).ToList(), p2.IsClosed);
                default:
                    return Array.Empty<Construct.Span>();
            }
        }

        private static Vec2 Polar(Vec2 c, double r, double a) => new Vec2(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));

        /// <summary>How far along a line (0..1) the point nearest <paramref name="p"/> is.</summary>
        public static double ParamAlong(Line l, Vec2 p)
        {
            var a = new Vec2(l.StartPoint.X, l.StartPoint.Y);
            var d = new Vec2(l.EndPoint.X, l.EndPoint.Y) - a;
            double dd = Vec2.Dot(d, d);
            return dd < 1e-24 ? 0 : Math.Max(0, Math.Min(1, Vec2.Dot(p - a, d) / dd));
        }

        private static IEditCommand SetEnds(Line l, XYZ start, XYZ end, string description) =>
            new SetPropertyCommand<(XYZ S, XYZ E)>((l.StartPoint, l.EndPoint), (start, end), v => { l.StartPoint = v.S; l.EndPoint = v.E; }, description);

        private static XYZ W(Vec2 v, double z = 0) => new XYZ(v.X, v.Y, z);

        /// <summary>
        /// TRIM: removes the part of <paramref name="line"/> around <paramref name="pick"/>
        /// between the nearest cutting edges either side (the line is shortened, split in two,
        /// or - if every part is cut away - erased). Null when no edge crosses it.
        /// </summary>
        public static IEditCommand? Trim(Line line, Vec2 pick, IEnumerable<Entity> edges)
        {
            var a = new Vec2(line.StartPoint.X, line.StartPoint.Y);
            var b = new Vec2(line.EndPoint.X, line.EndPoint.Y);
            var pieces = Construct.TrimSegment(a, b, ParamAlong(line, pick), edges.Where(e => e != line).SelectMany(SpansOf));
            if (pieces == null) return null;
            if (pieces.Count == 0) return new RemoveEntitiesCommand(new Entity[] { line }, "Trim");
            var first = SetEnds(line, W(pieces[0].A, line.StartPoint.Z), W(pieces[0].B, line.EndPoint.Z), "Trim");
            if (pieces.Count == 1 || !(line.Owner is ACadSharp.Tables.BlockRecord owner)) return first;
            var rest = (Line)Duplicate(line);
            rest.StartPoint = W(pieces[1].A, line.StartPoint.Z); rest.EndPoint = W(pieces[1].B, line.EndPoint.Z);
            return new CompositeCommand(new[] { first, new AddEntitiesCommand(owner, new Entity[] { rest }, "Trim") }, "Trim");
        }

        /// <summary>EXTEND: runs the end of <paramref name="line"/> nearer the pick out to the
        /// first boundary ahead of it. Null when nothing lies ahead.</summary>
        public static IEditCommand? Extend(Line line, Vec2 pick, IEnumerable<Entity> boundaries)
        {
            var a = new Vec2(line.StartPoint.X, line.StartPoint.Y);
            var b = new Vec2(line.EndPoint.X, line.EndPoint.Y);
            var hit = Construct.ExtendSegment(a, b, ParamAlong(line, pick), boundaries.Where(e => e != line).SelectMany(SpansOf));
            if (hit == null) return null;
            return hit.Value.AtB
                ? SetEnds(line, line.StartPoint, W(hit.Value.Point, line.EndPoint.Z), "Extend")
                : SetEnds(line, W(hit.Value.Point, line.StartPoint.Z), line.EndPoint, "Extend");
        }

        /// <summary>
        /// FILLET: rounds the corner between two lines with an arc of radius
        /// <paramref name="radius"/> (0 = just run both lines to their corner). Each line keeps
        /// the part on its picked side of the corner and is cut back to its tangent point; the
        /// arc goes on the first line's layer. Null when the lines are parallel or too short.
        /// </summary>
        public static IEditCommand? Fillet(Line l1, Vec2 pick1, Line l2, Vec2 pick2, double radius, out ACadSharp.Entities.Arc? arc)
        {
            arc = null;
            if (l1 == l2 || !(l1.Owner is ACadSharp.Tables.BlockRecord owner)) return null;
            var (far1, near1, atEnd1) = KeptSide(l1, l2, pick1);
            var (far2, near2, atEnd2) = KeptSide(l2, l1, pick2);
            var f = Construct.Fillet(far1, near1, far2, near2, radius);
            if (f == null) return null;
            var edits = new List<IEditCommand>
            {
                atEnd1 ? SetEnds(l1, W(far1, l1.StartPoint.Z), W(f.Value.T1, l1.EndPoint.Z), "Fillet") : SetEnds(l1, W(f.Value.T1, l1.StartPoint.Z), W(far1, l1.EndPoint.Z), "Fillet"),
                atEnd2 ? SetEnds(l2, W(far2, l2.StartPoint.Z), W(f.Value.T2, l2.EndPoint.Z), "Fillet") : SetEnds(l2, W(f.Value.T2, l2.StartPoint.Z), W(far2, l2.EndPoint.Z), "Fillet"),
            };
            if (radius > 0)
            {
                arc = new ACadSharp.Entities.Arc
                {
                    Center = W(f.Value.Center), Radius = radius, StartAngle = f.Value.StartAngle, EndAngle = f.Value.EndAngle,
                    Layer = l1.Layer, LineType = l1.LineType,
                };
                edits.Add(new AddEntitiesCommand(owner, new Entity[] { arc }, "Fillet"));
            }
            return new CompositeCommand(edits, "Fillet");
        }

        /// <summary>For a line meeting another at their (extended) intersection: the end it keeps
        /// (on the picked side of the corner), the end that moves to the corner, and whether that
        /// moving end is the line's EndPoint.</summary>
        private static (Vec2 Far, Vec2 Near, bool NearIsEnd) KeptSide(Line l, Line other, Vec2 pick)
        {
            var a = new Vec2(l.StartPoint.X, l.StartPoint.Y);
            var b = new Vec2(l.EndPoint.X, l.EndPoint.Y);
            var oa = new Vec2(other.StartPoint.X, other.StartPoint.Y);
            var ob = new Vec2(other.EndPoint.X, other.EndPoint.Y);
            var x = Construct.LineLine(a, b - a, oa, ob - oa);
            if (x == null) return Vec2.Distance(pick, a) > Vec2.Distance(pick, b) ? (a, b, true) : (b, a, false);
            // Keep the end on the same side of the corner as the pick.
            double side = Vec2.Dot(pick - x.Value, b - a);
            return side >= 0 ? (b, a, false) : (a, b, true);
        }

        private static double OffsetRadius(XYZ center, double radius, double distance, Vec2 toward)
        {
            double dc = Vec2.Distance(new Vec2(center.X, center.Y), toward);
            return dc < radius ? radius - distance : radius + distance;
        }
    }
}

namespace FdDraft.Cad.Editing
{
    /// <summary>
    /// FLIP: moves a bearing/distance (or curve-data) label to the other side of the course it
    /// labels. The label's anchor is mirrored across the course - the course's own line for a
    /// straight course, radially through the curve for an arc - and its top/bottom anchoring is
    /// swapped, so text that sat just above the line now hangs just below it at the same gap,
    /// still reading the same way. Rotation is untouched.
    /// </summary>
    public static class LabelFlip
    {
        /// <summary>The course span nearest <paramref name="p"/> among the linework
        /// candidates (Lines, Arcs, and every span of LwPolylines/Polyline2Ds).</summary>
        public static bool NearestCourse(IEnumerable<Entity> candidates, Vec2 p, out Construct.Span course, out double distance)
        {
            course = default; distance = double.MaxValue; bool found = false;
            foreach (var e in candidates)
            {
                if (e is Circle && !(e is ACadSharp.Entities.Arc)) continue; // a circle isn't a course
                foreach (var s in EntityOps.SpansOf(e))
                {
                    if (Vec2.Distance(s.A, s.B) < 1e-9) continue;
                    double d = s.DistanceAndSide(p, out _);
                    if (d < distance) { distance = d; course = s; found = true; }
                }
            }
            return found;
        }

        /// <summary>A point mirrored to the other side of a course: across its line, or
        /// radially through an arc (same gap from the curve, on the other side of it).</summary>
        public static Vec2 Across(Construct.Span course, Vec2 p)
        {
            if (!course.IsArc) return Construct.Reflect(p, course.A, course.B);
            var d = p - course.Center;
            double len = d.Length;
            if (len < 1e-12) return p;
            return course.Center + d * ((2 * course.Radius - len) / len);
        }

        /// <summary>The anchor FLIP mirrors: a TEXT's alignment point when it is aligned (as
        /// every FD-Draft label is), otherwise its insertion point; an MTEXT's insertion point.</summary>
        public static Vec2? Anchor(Entity e) => e switch
        {
            TextEntity t when t.HorizontalAlignment != TextHorizontalAlignment.Left || t.VerticalAlignment != TextVerticalAlignmentType.Baseline
                => new Vec2(t.AlignmentPoint.X, t.AlignmentPoint.Y),
            TextEntity t => new Vec2(t.InsertPoint.X, t.InsertPoint.Y),
            MText m => new Vec2(m.InsertPoint.X, m.InsertPoint.Y),
            _ => null,
        };

        /// <summary>
        /// Flips each TEXT/MTEXT label in <paramref name="labels"/> across the course nearest
        /// it among <paramref name="courses"/> (within <paramref name="maxDistance"/>), as one
        /// undo step. Returns null when nothing could be flipped; <paramref name="flipped"/>
        /// counts the labels moved.
        /// </summary>
        public static IEditCommand? Flip(IEnumerable<Entity> labels, IList<Entity> courses, double maxDistance, out int flipped)
        {
            var edits = new List<IEditCommand>();
            foreach (var e in labels)
            {
                var anchor = Anchor(e);
                if (anchor == null) continue;
                if (!NearestCourse(courses, anchor.Value, out var course, out double d) || d > maxDistance) continue;
                var to = Across(course, anchor.Value);
                switch (e)
                {
                    case TextEntity t:
                    {
                        var oldIns = t.InsertPoint; var oldAl = t.AlignmentPoint; var oldV = t.VerticalAlignment;
                        var at = new XYZ(to.X, to.Y, oldAl.Z);
                        var newV = oldV switch
                        {
                            TextVerticalAlignmentType.Top => TextVerticalAlignmentType.Bottom,
                            TextVerticalAlignmentType.Bottom or TextVerticalAlignmentType.Baseline => TextVerticalAlignmentType.Top,
                            _ => oldV,
                        };
                        // A left/baseline TEXT anchors at its insertion point; once it is re-anchored
                        // at the top it becomes an aligned text anchored at its alignment point.
                        edits.Add(new SetPropertyCommand<(XYZ Ins, XYZ Al, TextVerticalAlignmentType V)>(
                            (oldIns, oldAl, oldV), (at, at, newV),
                            s => { t.InsertPoint = s.Ins; t.AlignmentPoint = s.Al; t.VerticalAlignment = s.V; }, "Flip label"));
                        break;
                    }
                    case MText m:
                    {
                        var oldIns = m.InsertPoint; var oldAp = m.AttachmentPoint;
                        int ap = (int)oldAp; int row = (ap - 1) / 3, col = (ap - 1) % 3;
                        var newAp = (AttachmentPointType)((2 - row) * 3 + col + 1);
                        var at = new XYZ(to.X, to.Y, oldIns.Z);
                        edits.Add(new SetPropertyCommand<(XYZ Ins, AttachmentPointType Ap)>(
                            (oldIns, oldAp), (at, newAp),
                            s => { m.InsertPoint = s.Ins; m.AttachmentPoint = s.Ap; }, "Flip label"));
                        break;
                    }
                }
            }
            flipped = edits.Count;
            if (edits.Count == 0) return null;
            return edits.Count == 1 ? edits[0] : new CompositeCommand(edits, "Flip " + edits.Count + " labels");
        }
    }
}
