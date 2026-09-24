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
                if (e is Dimension) continue; // its picture block is per-dimension; see Mirrored
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
