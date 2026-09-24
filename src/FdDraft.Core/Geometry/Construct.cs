using System;
using System.Collections.Generic;

namespace FdDraft.Core.Geometry
{
    /// <summary>
    /// Constructive geometry behind the editing tools (OFFSET, MIRROR, FLIP): parallel
    /// offsets of bulged polylines, reflection across an axis, and the line/circle
    /// intersections those need. Plain math on <see cref="Vec2"/>, no CAD library, so it is
    /// unit-tested on the Linux build box like <see cref="Cogo"/>.
    /// </summary>
    public static class Construct
    {
        // ---- reflection --------------------------------------------------------------------

        /// <summary>The mirror image of <paramref name="p"/> across the infinite line through
        /// <paramref name="a"/> and <paramref name="b"/>.</summary>
        public static Vec2 Reflect(Vec2 p, Vec2 a, Vec2 b)
        {
            var u = (b - a).Normalized();
            var d = p - a;
            double along = Vec2.Dot(d, u);
            return a + u * (2 * along) - d;
        }

        /// <summary>The mirror image of a direction angle (radians, CCW from +X) across an axis
        /// at <paramref name="axisAngle"/>.</summary>
        public static double ReflectAngle(double angle, double axisAngle) => Angles.Normalize2Pi(2 * axisAngle - angle);

        /// <summary>True when text at this rotation (radians, CCW from +X) would read upside
        /// down - pointing leftwards, past vertical.</summary>
        public static bool ReadsUpsideDown(double rotation)
        {
            double r = Angles.Normalize2Pi(rotation);
            return r > Math.PI / 2 + 1e-9 && r <= 3 * Math.PI / 2 + 1e-9;
        }

        /// <summary>Signed perpendicular distance of <paramref name="p"/> from the line a→b:
        /// positive on its left.</summary>
        public static double SideDistance(Vec2 p, Vec2 a, Vec2 b)
        {
            var u = (b - a).Normalized();
            return Vec2.Cross(u, p - a);
        }

        /// <summary>Closest point to <paramref name="p"/> on the segment a-b, and its distance.</summary>
        public static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b, out Vec2 closest)
        {
            var d = b - a;
            double len2 = Vec2.Dot(d, d);
            double t = len2 < 1e-24 ? 0 : Math.Max(0, Math.Min(1, Vec2.Dot(p - a, d) / len2));
            closest = a + d * t;
            return Vec2.Distance(p, closest);
        }

        // ---- intersections -------------------------------------------------------------------

        /// <summary>Intersection of the infinite lines p + s·u and q + t·v; null when parallel.</summary>
        public static Vec2? LineLine(Vec2 p, Vec2 u, Vec2 q, Vec2 v)
        {
            double den = Vec2.Cross(u, v);
            if (Math.Abs(den) < 1e-12 * Math.Max(1, u.Length * v.Length)) return null;
            double s = Vec2.Cross(q - p, v) / den;
            return p + u * s;
        }

        /// <summary>Intersections (0, 1 or 2) of the infinite line p + s·u with a circle.</summary>
        public static List<Vec2> LineCircle(Vec2 p, Vec2 u, Vec2 c, double r)
        {
            var result = new List<Vec2>();
            var un = u.Normalized();
            double along = Vec2.Dot(c - p, un);
            var foot = p + un * along;
            double h2 = r * r - (c - foot).Length * (c - foot).Length;
            if (h2 < -1e-9 * r * r) return result;
            double h = Math.Sqrt(Math.Max(0, h2));
            result.Add(foot - un * h);
            if (h > 1e-12) result.Add(foot + un * h);
            return result;
        }

        /// <summary>Intersections (0, 1 or 2) of two circles.</summary>
        public static List<Vec2> CircleCircle(Vec2 c1, double r1, Vec2 c2, double r2)
        {
            var result = new List<Vec2>();
            double d = Vec2.Distance(c1, c2);
            if (d < 1e-12 || d > r1 + r2 + 1e-9 || d < Math.Abs(r1 - r2) - 1e-9) return result;
            double a = (r1 * r1 - r2 * r2 + d * d) / (2 * d);
            double h = Math.Sqrt(Math.Max(0, r1 * r1 - a * a));
            var u = (c2 - c1) * (1 / d);
            var m = c1 + u * a;
            result.Add(m + u.Left() * h);
            if (h > 1e-12) result.Add(m - u.Left() * h);
            return result;
        }

        // ---- bulged polyline segments ----------------------------------------------------------

        /// <summary>One span of a bulged polyline: a straight segment, or an arc with a signed
        /// sweep (positive = counter-clockwise, the DWG bulge convention).</summary>
        public readonly struct Span
        {
            public Vec2 A { get; }
            public Vec2 B { get; }
            public bool IsArc { get; }
            public Vec2 Center { get; }
            public double Radius { get; }
            public double Sweep { get; }

            private Span(Vec2 a, Vec2 b, bool isArc, Vec2 center, double radius, double sweep)
            { A = a; B = b; IsArc = isArc; Center = center; Radius = radius; Sweep = sweep; }

            public static Span FromBulge(Vec2 a, Vec2 b, double bulge)
            {
                double chord = Vec2.Distance(a, b);
                if (Math.Abs(bulge) < 1e-12 || chord < 1e-12) return new Span(a, b, false, default, 0, 0);
                double theta = 4 * Math.Atan(bulge);
                double r = chord / (2 * Math.Sin(Math.Abs(theta) / 2));
                var mid = (a + b) * 0.5;
                var n = (b - a).Normalized().Left();
                double off = r * Math.Cos(theta / 2) * Math.Sign(bulge);
                return new Span(a, b, true, mid + n * off, r, theta);
            }

            public static Span Arc(Vec2 a, Vec2 b, Vec2 center, double radius, double sweep) => new Span(a, b, true, center, radius, sweep);
            public static Span Straight(Vec2 a, Vec2 b) => new Span(a, b, false, default, 0, 0);

            /// <summary>The bulge of this span between its (possibly moved) endpoints, keeping the
            /// arc's direction of travel.</summary>
            public double Bulge
            {
                get
                {
                    if (!IsArc) return 0;
                    double a0 = Math.Atan2(A.Y - Center.Y, A.X - Center.X);
                    double a1 = Math.Atan2(B.Y - Center.Y, B.X - Center.X);
                    double sweep = a1 - a0;
                    if (Sweep > 0) { while (sweep <= 1e-12) sweep += Angles.TwoPi; while (sweep > Angles.TwoPi) sweep -= Angles.TwoPi; }
                    else { while (sweep >= -1e-12) sweep -= Angles.TwoPi; while (sweep < -Angles.TwoPi) sweep += Angles.TwoPi; }
                    return Math.Tan(sweep / 4);
                }
            }

            /// <summary>Distance from p to this span, and which side of its direction of travel p
            /// lies on (+1 left, -1 right).</summary>
            public double DistanceAndSide(Vec2 p, out int side)
            {
                if (!IsArc)
                {
                    side = Vec2.Cross(B - A, p - A) >= 0 ? 1 : -1;
                    return DistanceToSegment(p, A, B, out _);
                }
                double dc = Vec2.Distance(p, Center);
                // Inside the circle is the left of a counter-clockwise arc.
                side = (dc < Radius) == (Sweep > 0) ? 1 : -1;
                if (OnSweep(p)) return Math.Abs(dc - Radius);
                return Math.Min(Vec2.Distance(p, A), Vec2.Distance(p, B));
            }

            private bool OnSweep(Vec2 p)
            {
                double a0 = Math.Atan2(A.Y - Center.Y, A.X - Center.X);
                double ap = Math.Atan2(p.Y - Center.Y, p.X - Center.X);
                double rel = Sweep > 0 ? Angles.Normalize2Pi(ap - a0) : Angles.Normalize2Pi(a0 - ap);
                return rel <= Math.Abs(Sweep) + 1e-12;
            }

            /// <summary>This span moved <paramref name="d"/> to the left of its direction of travel
            /// (negative = right). Null when an arc would shrink to nothing.</summary>
            public Span? Offset(double d)
            {
                if (!IsArc)
                {
                    var n = (B - A).Normalized().Left() * d;
                    return Straight(A + n, B + n);
                }
                double r = Radius - d * Math.Sign(Sweep);
                if (r <= 1e-9) return null;
                double k = r / Radius;
                return Arc(Center + (A - Center) * k, Center + (B - Center) * k, Center, r, Sweep);
            }
        }

        public static List<Span> Spans(IList<Vec2> pts, IList<double>? bulges, bool closed)
        {
            var spans = new List<Span>();
            int n = pts.Count, segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
                spans.Add(Span.FromBulge(pts[i], pts[(i + 1) % n], bulges != null && i < bulges.Count ? bulges[i] : 0));
            return spans;
        }

        /// <summary>
        /// Which side of a (bulged) polyline <paramref name="pick"/> is on: +1 left of its
        /// direction of travel, -1 right - judged against the span nearest the pick, the way a
        /// drafter means "offset toward here".
        /// </summary>
        public static int SideOfPolyline(IList<Vec2> pts, IList<double>? bulges, bool closed, Vec2 pick)
        {
            int side = 1;
            double best = double.MaxValue;
            foreach (var s in Spans(pts, bulges, closed))
            {
                double d = s.DistanceAndSide(pick, out int sd);
                if (d < best) { best = d; side = sd; }
            }
            return side;
        }

        /// <summary>
        /// A parallel copy of a (bulged) polyline, <paramref name="d"/> to the left of its
        /// direction of travel (negative = right). Straight spans are shifted and re-joined at
        /// their intersections (a mitred corner, as a survey offset line needs); arcs stay
        /// concentric. Null when the offset collapses an arc.
        /// </summary>
        public static (List<Vec2> Points, List<double> Bulges)? OffsetPolyline(IList<Vec2> pts, IList<double>? bulges, bool closed, double d)
        {
            if (pts.Count < 2) return null;
            var spans = Spans(pts, bulges, closed);
            if (spans.Count == 0) return null;
            var moved = new List<Span>();
            foreach (var s in spans)
            {
                var o = s.Offset(d);
                if (o == null) return null;
                moved.Add(o.Value);
            }

            int m = moved.Count;
            // Joint i sits between moved[i-1] and moved[i] (at original vertex i).
            var joints = new Vec2[closed ? m : m + 1];
            for (int i = 0; i < joints.Length; i++)
            {
                if (!closed && i == 0) { joints[i] = moved[0].A; continue; }
                if (!closed && i == m) { joints[i] = moved[m - 1].B; continue; }
                var prev = moved[(i - 1 + m) % m];
                var next = moved[i % m];
                joints[i] = Join(prev, next);
            }

            var outPts = new List<Vec2>(joints);
            var outBulges = new List<double>();
            for (int i = 0; i < m; i++)
            {
                var s = moved[i];
                var a = joints[i];
                var b = joints[(i + 1) % joints.Length];
                outBulges.Add(s.IsArc ? Span.Arc(a, b, s.Center, s.Radius, s.Sweep).Bulge : 0);
            }
            if (!closed) outBulges.Add(0);
            return (outPts, outBulges);
        }

        /// <summary>Where two consecutive offset spans meet: their carriers' intersection
        /// nearest the naive joint, or the naive joint itself when they are parallel/tangent
        /// or do not meet.</summary>
        private static Vec2 Join(Span prev, Span next)
        {
            var naive = (prev.B + next.A) * 0.5;
            if (Vec2.Distance(prev.B, next.A) < 1e-9) return prev.B;
            List<Vec2> cands;
            if (!prev.IsArc && !next.IsArc)
            {
                var x = LineLine(prev.A, prev.B - prev.A, next.A, next.B - next.A);
                return x ?? naive;
            }
            if (prev.IsArc && next.IsArc) cands = CircleCircle(prev.Center, prev.Radius, next.Center, next.Radius);
            else if (prev.IsArc) cands = LineCircle(next.A, next.B - next.A, prev.Center, prev.Radius);
            else cands = LineCircle(prev.A, prev.B - prev.A, next.Center, next.Radius);
            if (cands.Count == 0) return naive;
            Vec2 best = cands[0];
            foreach (var c in cands) if (Vec2.Distance(c, naive) < Vec2.Distance(best, naive)) best = c;
            return best;
        }
    }
}
