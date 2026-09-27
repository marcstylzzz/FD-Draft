using System;
using System.Collections.Generic;
using System.Linq;
using FdDraft.Core.Geometry;
using FdDraft.Core.Standards;

namespace FdDraft.View
{
    /// <summary>
    /// Drag-box selection, the CAD way: a <b>window</b> (dragged left to right) takes only
    /// entities entirely inside the box; a <b>crossing</b> (dragged right to left) also takes
    /// anything the box touches. Works on the scene's own prims, so it selects exactly what is
    /// drawn - inside a sheet's viewport only what shows through it.
    /// </summary>
    public static class BoxSelect
    {
        /// <summary>DWG handles of the entities a box selects in <paramref name="scene"/>.</summary>
        public static HashSet<ulong> Handles(Scene scene, Rect box, bool crossing)
        {
            // Per entity: does every prim fall inside (window), does any touch (crossing)?
            var inside = new Dictionary<ulong, bool>();
            var touched = new HashSet<ulong>();
            foreach (var g in scene.Groups)
            {
                var b = box;
                if (g.Clip.HasValue)
                {
                    var c = g.Clip.Value;
                    // Only what shows through the viewport can be picked there.
                    b = new Rect(Math.Max(b.X1, c.X1), Math.Max(b.Y1, c.Y1), Math.Min(b.X2, c.X2), Math.Min(b.Y2, c.Y2));
                    if (b.X1 > b.X2 || b.Y1 > b.Y2) continue;
                }
                foreach (var p in g.Prims)
                {
                    if (p.Handle == 0) continue;
                    bool all = AllInside(p, b);
                    inside[p.Handle] = inside.TryGetValue(p.Handle, out bool prev) ? prev && all : all;
                    if (crossing && (all || Touches(p, b))) touched.Add(p.Handle);
                }
            }
            if (crossing) return touched;
            return new HashSet<ulong>(inside.Where(kv => kv.Value).Select(kv => kv.Key));
        }

        /// <summary>
        /// A box picked on a view turned by <paramref name="twist"/>: <paramref name="a"/> and
        /// <paramref name="b"/> are its opposite corners in the drawing. The scene is looked at in
        /// the view's own frame, where the box is square to the axes again.
        /// </summary>
        public static HashSet<ulong> Handles(Scene scene, double twist, Vec2 a, Vec2 b, bool crossing)
        {
            double c = Math.Cos(twist), s = Math.Sin(twist);
            Vec2 T(Vec2 p) => new Vec2(p.X * c - p.Y * s, p.X * s + p.Y * c);
            var turned = new Scene { Name = scene.Name, IsPaper = scene.IsPaper };
            foreach (var g in scene.Groups)
            {
                var ng = new SceneGroup();
                foreach (var p in g.Prims)
                {
                    if (p.Handle == 0) continue;
                    ng.Prims.Add(new Prim
                    {
                        Kind = p.Kind, Handle = p.Handle, Closed = p.Closed, Radius = p.Radius, Center = T(p.Center),
                        Points = p.Points.Select(T).ToList(), Text = p.Text, Height = p.Height, Rotation = p.Rotation + twist, H = p.H, V = p.V, WidthFactor = p.WidthFactor, Font = p.Font, FitWidth = p.FitWidth,
                    });
                }
                turned.Groups.Add(ng);
            }
            var ta = T(a); var tb = T(b);
            return Handles(turned, new Rect(Math.Min(ta.X, tb.X), Math.Min(ta.Y, tb.Y), Math.Max(ta.X, tb.X), Math.Max(ta.Y, tb.Y)), crossing);
        }

        private static bool In(Vec2 p, Rect b) => p.X >= b.X1 && p.X <= b.X2 && p.Y >= b.Y1 && p.Y <= b.Y2;

        private static bool AllInside(Prim p, Rect b)
        {
            switch (p.Kind)
            {
                case PrimKind.Polyline:
                case PrimKind.Fill:
                    return p.Points.Count > 0 && p.Points.All(q => In(q, b));
                case PrimKind.Circle:
                    return p.Center.X - p.Radius >= b.X1 && p.Center.X + p.Radius <= b.X2 && p.Center.Y - p.Radius >= b.Y1 && p.Center.Y + p.Radius <= b.Y2;
                default: // text and nodes go by their anchor
                    return In(p.Center, b);
            }
        }

        private static bool Touches(Prim p, Rect b)
        {
            switch (p.Kind)
            {
                case PrimKind.Polyline:
                case PrimKind.Fill:
                {
                    if (p.Points.Any(q => In(q, b))) return true;
                    int n = p.Points.Count;
                    int segs = p.Closed || p.Kind == PrimKind.Fill ? n : n - 1;
                    for (int i = 0; i < segs; i++)
                        if (SegmentHitsBox(p.Points[i], p.Points[(i + 1) % n], b)) return true;
                    // A fill that swallows the whole box still touches it.
                    return p.Kind == PrimKind.Fill && Polygon.Contains(p.Points, new Vec2((b.X1 + b.X2) / 2, (b.Y1 + b.Y2) / 2));
                }
                case PrimKind.Circle:
                {
                    // Nearest and farthest box points from the centre bracket the radius.
                    double nx = Math.Max(b.X1, Math.Min(p.Center.X, b.X2)), ny = Math.Max(b.Y1, Math.Min(p.Center.Y, b.Y2));
                    double near = Vec2.Distance(p.Center, new Vec2(nx, ny));
                    double fx = Math.Abs(p.Center.X - b.X1) > Math.Abs(p.Center.X - b.X2) ? b.X1 : b.X2;
                    double fy = Math.Abs(p.Center.Y - b.Y1) > Math.Abs(p.Center.Y - b.Y2) ? b.Y1 : b.Y2;
                    double far = Vec2.Distance(p.Center, new Vec2(fx, fy));
                    return near <= p.Radius && far >= p.Radius;
                }
                default:
                    return In(p.Center, b);
            }
        }

        /// <summary>Whether segment a-b crosses the box (either end inside, or it cuts an edge).</summary>
        public static bool SegmentHitsBox(Vec2 a, Vec2 b, Rect r)
        {
            if (In(a, r) || In(b, r)) return true;
            if (Math.Max(a.X, b.X) < r.X1 || Math.Min(a.X, b.X) > r.X2 || Math.Max(a.Y, b.Y) < r.Y1 || Math.Min(a.Y, b.Y) > r.Y2) return false;
            var c1 = new Vec2(r.X1, r.Y1); var c2 = new Vec2(r.X2, r.Y1); var c3 = new Vec2(r.X2, r.Y2); var c4 = new Vec2(r.X1, r.Y2);
            return Cross(a, b, c1, c2) || Cross(a, b, c2, c3) || Cross(a, b, c3, c4) || Cross(a, b, c4, c1);
        }

        private static bool Cross(Vec2 p1, Vec2 p2, Vec2 q1, Vec2 q2)
        {
            double d1 = Vec2.Cross(p2 - p1, q1 - p1), d2 = Vec2.Cross(p2 - p1, q2 - p1);
            double d3 = Vec2.Cross(q2 - q1, p1 - q1), d4 = Vec2.Cross(q2 - q1, p2 - q1);
            return ((d1 > 0) != (d2 > 0) || d1 == 0 || d2 == 0) && ((d3 > 0) != (d4 > 0) || d3 == 0 || d4 == 0);
        }
    }
}
