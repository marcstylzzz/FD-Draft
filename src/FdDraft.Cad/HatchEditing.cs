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
    /// HATCH: fill the closed area round a picked point - the smallest closed polyline or circle
    /// around it, less any closed figures inside it (islands) - solid, or with parallel or
    /// crossed lines at a spacing.
    /// </summary>
    public static class HatchEditing
    {
        /// <summary>A closed boundary: its vertices and bulges (a circle as two half-circle spans).</summary>
        public sealed class Loop
        {
            public List<Vec2> Points { get; } = new List<Vec2>();
            public List<double> Bulges { get; } = new List<double>();
            public Entity? Source { get; set; }

            public double Area => Math.Abs(Polygon.SignedArea(Points, Bulges));

            public bool Contains(Vec2 p)
            {
                // Arcs matter near the edge: test against the densified outline.
                var dense = Construct.Spans(Points, Bulges, true).SelectMany(s => Sample(s)).ToList();
                return Polygon.Contains(dense, p);
            }

            private static IEnumerable<Vec2> Sample(Construct.Span s)
            {
                if (!s.IsArc) { yield return s.A; yield break; }
                double a0 = Math.Atan2(s.A.Y - s.Center.Y, s.A.X - s.Center.X);
                for (int i = 0; i < 16; i++)
                {
                    double a = a0 + s.Sweep * i / 16;
                    yield return new Vec2(s.Center.X + s.Radius * Math.Cos(a), s.Center.Y + s.Radius * Math.Sin(a));
                }
            }
        }

        /// <summary>The closed loops among <paramref name="entities"/>: closed polylines (or ones ending where they start) and circles.</summary>
        public static List<Loop> Loops(IEnumerable<Entity> entities)
        {
            var result = new List<Loop>();
            foreach (var e in entities)
            {
                switch (e)
                {
                    case ACadSharp.Entities.Arc _:
                        break;
                    case Circle c:
                    {
                        var l = new Loop { Source = e };
                        l.Points.Add(new Vec2(c.Center.X + c.Radius, c.Center.Y)); l.Bulges.Add(1);
                        l.Points.Add(new Vec2(c.Center.X - c.Radius, c.Center.Y)); l.Bulges.Add(1);
                        result.Add(l);
                        break;
                    }
                    case LwPolyline lp when lp.Vertices.Count >= 3:
                        Add(e, lp.Vertices.Select(v => (new Vec2(v.Location.X, v.Location.Y), v.Bulge)).ToList(), lp.IsClosed, result);
                        break;
                    case Polyline2D p2 when p2.Vertices.Count >= 3:
                        Add(e, p2.Vertices.Select(v => (new Vec2(v.Location.X, v.Location.Y), v.Bulge)).ToList(), p2.IsClosed, result);
                        break;
                }
            }
            return result;
        }

        private static void Add(Entity e, List<(Vec2 P, double B)> v, bool closed, List<Loop> result)
        {
            if (!closed)
            {
                if (Vec2.Distance(v[0].P, v[v.Count - 1].P) > 1e-6) return;
                v.RemoveAt(v.Count - 1);
                if (v.Count < 3) return;
            }
            var l = new Loop { Source = e };
            foreach (var (p, b) in v) { l.Points.Add(p); l.Bulges.Add(b); }
            result.Add(l);
        }

        /// <summary>
        /// The boundary for a pick: the smallest loop around <paramref name="pick"/>, with the
        /// loops inside it that don't hold the pick as islands. Null when nothing surrounds it.
        /// </summary>
        public static List<Loop>? BoundaryAt(Vec2 pick, IEnumerable<Entity> entities)
        {
            // Largest first, so an island is always judged after the loop that might contain it.
            var loops = Loops(entities).OrderByDescending(l => l.Area).ToList();
            var outer = loops.Where(l => l.Contains(pick)).OrderBy(l => l.Area).FirstOrDefault();
            if (outer == null) return null;
            var result = new List<Loop> { outer };
            foreach (var l in loops)
            {
                if (l == outer || l.Contains(pick) || l.Area >= outer.Area) continue;
                // An island: every corner inside the outer loop, and not inside another island.
                if (l.Points.All(outer.Contains) && !result.Skip(1).Any(i => i.Contains(l.Points[0]))) result.Add(l);
            }
            return result;
        }

        /// <summary>
        /// A HATCH over <paramref name="loops"/> (first the outside, then islands). Solid when
        /// <paramref name="spacing"/> is 0; otherwise lines at <paramref name="angleRadians"/>
        /// every <paramref name="spacing"/> drawing units, and a second family at right angles
        /// when <paramref name="crossed"/>.
        /// </summary>
        public static Hatch Create(IList<Loop> loops, double spacing, double angleRadians, bool crossed, Layer layer)
        {
            var h = new Hatch { Layer = layer };
            if (spacing <= 0)
            {
                h.IsSolid = true;
                h.Pattern = HatchPattern.Solid;
                h.PatternType = HatchPatternType.SolidFill;
            }
            else
            {
                h.IsSolid = false;
                var pat = new HatchPattern(crossed ? "_USER_X" : "_USER");
                h.Pattern = pat;
                h.PatternType = HatchPatternType.PatternFill;
                h.IsDouble = crossed;
                // The pattern's lines are stored as drawn (world spacing and angle), so they're
                // added after the hatch's own angle/scale fields have been set.
                foreach (double a in crossed ? new[] { angleRadians, angleRadians + Math.PI / 2 } : new[] { angleRadians })
                    pat.Lines.Add(new HatchPattern.Line { Angle = a, BasePoint = new XY(0, 0), Offset = new XY(-Math.Sin(a) * spacing, Math.Cos(a) * spacing) });
            }
            for (int i = 0; i < loops.Count; i++)
            {
                var loop = loops[i];
                var path = new Hatch.BoundaryPath();
                var edge = new Hatch.BoundaryPath.Polyline { IsClosed = true };
                for (int k = 0; k < loop.Points.Count; k++) edge.Vertices.Add(new XYZ(loop.Points[k].X, loop.Points[k].Y, loop.Bulges[k]));
                path.Edges.Add(edge);
                path.Flags = BoundaryPathFlags.Polyline | (i == 0 ? BoundaryPathFlags.External | BoundaryPathFlags.Outermost : BoundaryPathFlags.Default);
                h.Paths.Add(path);
            }
            return h;
        }
    }
}
