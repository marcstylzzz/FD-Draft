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
                IEnumerable<Construct.Span> spans = e switch
                {
                    Line l => new[] { Construct.Span.Straight(new Vec2(l.StartPoint.X, l.StartPoint.Y), new Vec2(l.EndPoint.X, l.EndPoint.Y)) },
                    ACadSharp.Entities.Arc a => new[] { ArcSpan(a) },
                    LwPolyline lp when lp.Vertices.Count >= 2 => Construct.Spans(lp.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList(), lp.Vertices.Select(v => v.Bulge).ToList(), lp.IsClosed),
                    Polyline2D p2 when p2.Vertices.Count >= 2 => Construct.Spans(p2.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList(), p2.Vertices.Select(v => v.Bulge).ToList(), p2.IsClosed),
                    _ => Array.Empty<Construct.Span>(),
                };
                foreach (var s in spans)
                {
                    if (Vec2.Distance(s.A, s.B) < 1e-9) continue;
                    double d = s.DistanceAndSide(p, out _);
                    if (d < distance) { distance = d; course = s; found = true; }
                }
            }
            return found;
        }

        private static Construct.Span ArcSpan(ACadSharp.Entities.Arc a)
        {
            double sweep = a.EndAngle - a.StartAngle;
            while (sweep <= 0) sweep += Angles.TwoPi;
            var c = new Vec2(a.Center.X, a.Center.Y);
            var s = new Vec2(c.X + a.Radius * Math.Cos(a.StartAngle), c.Y + a.Radius * Math.Sin(a.StartAngle));
            var e = new Vec2(c.X + a.Radius * Math.Cos(a.EndAngle), c.Y + a.Radius * Math.Sin(a.EndAngle));
            return Construct.Span.Arc(s, e, c, a.Radius, sweep);
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
