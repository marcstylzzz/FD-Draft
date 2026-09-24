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

            /// <summary>The point on this span nearest <paramref name="p"/> (an arc's own
            /// endpoint when p is off the ends of its sweep).</summary>
            public Vec2 Project(Vec2 p)
            {
                if (!IsArc) { DistanceToSegment(p, A, B, out var c); return c; }
                if (!OnSweep(p)) return Vec2.Distance(p, A) <= Vec2.Distance(p, B) ? A : B;
                var dir = (p - Center).Normalized();
                return Center + dir * Radius;
            }

            /// <summary>Halfway along the span (the arc's midpoint, not the chord's).</summary>
            public Vec2 Midpoint
            {
                get
                {
                    if (!IsArc) return (A + B) * 0.5;
                    double a0 = Math.Atan2(A.Y - Center.Y, A.X - Center.X) + Sweep / 2;
                    return new Vec2(Center.X + Radius * Math.Cos(a0), Center.Y + Radius * Math.Sin(a0));
                }
            }

            /// <summary>
            /// The bulges of the two pieces when this span is split at <paramref name="p"/>: an
            /// arc split at a point on it keeps its curve exactly; a straight span, or a point off
            /// the arc, gives two straight pieces.
            /// </summary>
            public (double First, double Second) SplitBulges(Vec2 p)
            {
                if (!IsArc || Math.Abs(Vec2.Distance(p, Center) - Radius) > 1e-6 * Math.Max(1, Radius) || !OnSweep(p)) return (0, 0);
                double a0 = Math.Atan2(A.Y - Center.Y, A.X - Center.X);
                double ap = Math.Atan2(p.Y - Center.Y, p.X - Center.X);
                double s1 = Sweep > 0 ? Angles.Normalize2Pi(ap - a0) : -Angles.Normalize2Pi(a0 - ap);
                return (Math.Tan(s1 / 4), Math.Tan((Sweep - s1) / 4));
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

        // ---- TRIM / EXTEND / FILLET ------------------------------------------------------------

        /// <summary>Where the infinite line p + t·d crosses a span, as parameters t (a Line
        /// entity from p to p + d is t in 0..1).</summary>
        public static List<double> LineParamsOn(Vec2 p, Vec2 d, Span s)
        {
            var result = new List<double>();
            double dd = Vec2.Dot(d, d);
            if (dd < 1e-24) return result;
            if (!s.IsArc)
            {
                var x = LineLine(p, d, s.A, s.B - s.A);
                if (x == null) return result;
                DistanceToSegment(x.Value, s.A, s.B, out var onSeg);
                if (Vec2.Distance(onSeg, x.Value) > 1e-9 * Math.Max(1, (s.B - s.A).Length)) return result;
                result.Add(Vec2.Dot(x.Value - p, d) / dd);
                return result;
            }
            foreach (var x in LineCircle(p, d, s.Center, s.Radius))
            {
                if (Vec2.Distance(s.Project(x), x) > 1e-9 * Math.Max(1, s.Radius)) continue; // off the arc's sweep
                result.Add(Vec2.Dot(x - p, d) / dd);
            }
            return result;
        }

        /// <summary>
        /// TRIM for a straight segment a-b cut by <paramref name="edges"/>: the part around the
        /// picked parameter <paramref name="pick"/> (0..1 along a→b) between the nearest cuts
        /// either side is removed. Returns the pieces that remain (zero, one or two), or null
        /// when no edge crosses the segment on either side of the pick.
        /// </summary>
        public static List<(Vec2 A, Vec2 B)>? TrimSegment(Vec2 a, Vec2 b, double pick, IEnumerable<Span> edges)
        {
            var d = b - a;
            double below = double.NegativeInfinity, above = double.PositiveInfinity;
            const double eps = 1e-9;
            foreach (var s in edges)
                foreach (double t in LineParamsOn(a, d, s))
                {
                    if (t <= eps || t >= 1 - eps) continue;   // a cut at an existing end changes nothing
                    if (t < pick && t > below) below = t;
                    if (t > pick && t < above) above = t;
                }
            if (double.IsNegativeInfinity(below) && double.IsPositiveInfinity(above)) return null;
            var keep = new List<(Vec2, Vec2)>();
            if (!double.IsNegativeInfinity(below)) keep.Add((a, a + d * below));
            if (!double.IsPositiveInfinity(above)) keep.Add((a + d * above, b));
            return keep;
        }

        /// <summary>
        /// EXTEND for a straight segment a-b: the end nearer <paramref name="pick"/> (0..1) is
        /// run out along the segment to the first boundary it meets. Returns the new end
        /// point and which end moved (true = b), or null when nothing lies ahead.
        /// </summary>
        public static (Vec2 Point, bool AtB)? ExtendSegment(Vec2 a, Vec2 b, double pick, IEnumerable<Span> boundaries)
        {
            var d = b - a;
            bool atB = pick >= 0.5;
            double best = atB ? double.PositiveInfinity : double.NegativeInfinity;
            const double eps = 1e-9;
            foreach (var s in boundaries)
                foreach (double t in LineParamsOn(a, d, s))
                {
                    if (atB && t > 1 + eps && t < best) best = t;
                    if (!atB && t < -eps && t > best) best = t;
                }
            if (double.IsInfinity(best)) return null;
            return (a + d * best, atB);
        }

        /// <summary>
        /// A fillet (corner rounding) of radius <paramref name="r"/> between two straight
        /// courses, each given as (far end, end near the corner). Returns the two tangent
        /// points, the arc's centre and its counter-clockwise start/end angles - or, for r = 0,
        /// the corner itself (tangent points both at the intersection). Null when the courses
        /// are parallel or too short to hold the curve.
        /// </summary>
        public static (Vec2 T1, Vec2 T2, Vec2 Center, double StartAngle, double EndAngle)? Fillet(Vec2 far1, Vec2 near1, Vec2 far2, Vec2 near2, double r)
        {
            var x = LineLine(far1, near1 - far1, far2, near2 - far2);
            if (x == null) return null;
            var X = x.Value;
            var u1 = (far1 - X).Normalized();
            var u2 = (far2 - X).Normalized();
            if (u1.Length < 1e-12 || u2.Length < 1e-12) return null;
            if (r <= 0) return (X, X, X, 0, 0);
            double theta = Math.Acos(Math.Max(-1, Math.Min(1, Vec2.Dot(u1, u2))));
            if (theta < 1e-9 || Math.PI - theta < 1e-9) return null;
            double back = r / Math.Tan(theta / 2);
            if (back > Vec2.Distance(X, far1) + 1e-9 || back > Vec2.Distance(X, far2) + 1e-9) return null;
            var t1 = X + u1 * back;
            var t2 = X + u2 * back;
            var bis = (u1 + u2).Normalized();
            var c = X + bis * (r / Math.Sin(theta / 2));
            double a1 = Math.Atan2(t1.Y - c.Y, t1.X - c.X);
            double a2 = Math.Atan2(t2.Y - c.Y, t2.X - c.X);
            // The fillet is the short way round (< 180°) between the tangent points.
            double ccw = Angles.Normalize2Pi(a2 - a1);
            return ccw <= Math.PI ? (t1, t2, c, Angles.Normalize2Pi(a1), Angles.Normalize2Pi(a2)) : (t1, t2, c, Angles.Normalize2Pi(a2), Angles.Normalize2Pi(a1));
        }

        // ---- JOIN ----------------------------------------------------------------------------

        /// <summary>One piece of linework to join: a straight (bulge 0) or arc span a→b.</summary>
        public readonly struct Piece
        {
            public Vec2 A { get; }
            public Vec2 B { get; }
            public double Bulge { get; }
            public Piece(Vec2 a, Vec2 b, double bulge) { A = a; B = b; Bulge = bulge; }
            /// <summary>The same curve walked the other way.</summary>
            public Piece Reversed => new Piece(B, A, -Bulge);
        }

        /// <summary>A joined run of pieces as polyline vertices and bulges.</summary>
        public sealed class Chain
        {
            public List<Vec2> Points { get; } = new List<Vec2>();
            public List<double> Bulges { get; } = new List<double>();
            public bool Closed { get; set; }
            /// <summary>Indexes of the input pieces this chain was built from.</summary>
            public List<int> Sources { get; } = new List<int>();
        }

        /// <summary>
        /// Links pieces that share endpoints (within <paramref name="tol"/>) into chains, each
        /// piece used once and turned round where needed so every chain runs one way. A chain
        /// whose ends meet is closed. Where three or more pieces meet at a point the chain
        /// takes the first match and the rest start chains of their own.
        /// </summary>
        public static List<Chain> JoinPieces(IList<Piece> pieces, double tol)
        {
            var used = new bool[pieces.Count];
            var chains = new List<Chain>();
            bool Same(Vec2 p, Vec2 q) => Vec2.Distance(p, q) <= tol;
            for (int seed = 0; seed < pieces.Count; seed++)
            {
                if (used[seed]) continue;
                used[seed] = true;
                var run = new LinkedList<(Piece P, int Src)>();
                run.AddLast((pieces[seed], seed));
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    var tail = run.Last!.Value.P.B;
                    var head = run.First!.Value.P.A;
                    if (Same(tail, head) && run.Count > 1) break; // closed up
                    for (int i = 0; i < pieces.Count; i++)
                    {
                        if (used[i]) continue;
                        var pc = pieces[i];
                        if (Same(pc.A, tail)) { run.AddLast((pc, i)); used[i] = true; grew = true; break; }
                        if (Same(pc.B, tail)) { run.AddLast((pc.Reversed, i)); used[i] = true; grew = true; break; }
                        if (Same(pc.B, head)) { run.AddFirst((pc, i)); used[i] = true; grew = true; break; }
                        if (Same(pc.A, head)) { run.AddFirst((pc.Reversed, i)); used[i] = true; grew = true; break; }
                    }
                }
                var chain = new Chain();
                foreach (var (pc, src) in run)
                {
                    chain.Points.Add(pc.A);
                    chain.Bulges.Add(pc.Bulge);
                    chain.Sources.Add(src);
                }
                var end = run.Last!.Value.P.B;
                if (run.Count > 1 && Same(end, chain.Points[0])) chain.Closed = true;
                else { chain.Points.Add(end); chain.Bulges.Add(0); }
                chains.Add(chain);
            }
            return chains;
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
