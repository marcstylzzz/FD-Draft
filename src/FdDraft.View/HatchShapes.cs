using System;
using System.Collections.Generic;
using System.Linq;
using ACadSharp.Entities;
using CSMath;
using FdDraft.Core.Geometry;

namespace FdDraft.View
{
    /// <summary>
    /// A HATCH as FD-Draft draws it: its boundary loops (lines, arcs, ellipse arcs, splines and
    /// bulged polylines, tessellated in the hatch's own plane), and for a pattern hatch the pattern's
    /// line segments clipped to them. Loops fill even-odd, so the holes in letters stay open.
    /// </summary>
    public static class HatchShapes
    {
        /// <summary>Most pattern segments drawn for one hatch - a dense pattern over a large area
        /// would otherwise be millions of lines.</summary>
        public const int MaxPatternSegments = 20000;

        public static List<List<Vec2>> Loops(Hatch h)
        {
            var loops = new List<List<Vec2>>();
            foreach (var path in h.Paths)
            {
                var pieces = path.Edges.Select(EdgePoints).Where(p => p.Count > 0).ToList();
                if (pieces.Count == 0) continue;
                // Edges are not always stored head to tail (or in order): chain them, each time taking
                // the piece whose nearer end meets the loop's end, turned round if need be.
                var loop = new List<Vec2>(pieces[0]);
                pieces.RemoveAt(0);
                while (pieces.Count > 0)
                {
                    var end = loop[loop.Count - 1];
                    int best = 0; bool flip = false; double bd = double.MaxValue;
                    for (int k = 0; k < pieces.Count; k++)
                    {
                        double ds = Vec2.Distance(end, pieces[k][0]), de = Vec2.Distance(end, pieces[k][pieces[k].Count - 1]);
                        if (ds < bd) { bd = ds; best = k; flip = false; }
                        if (de < bd) { bd = de; best = k; flip = true; }
                    }
                    var next = pieces[best];
                    pieces.RemoveAt(best);
                    if (flip) next.Reverse();
                    foreach (var q in next)
                        if (Vec2.Distance(loop[loop.Count - 1], q) > 1e-12) loop.Add(q);
                }
                if (loop.Count > 1 && Vec2.Distance(loop[0], loop[loop.Count - 1]) < 1e-12) loop.RemoveAt(loop.Count - 1);
                if (loop.Count >= 3) loops.Add(loop);
            }
            return loops;
        }

        private static List<Vec2> EdgePoints(Hatch.BoundaryPath.Edge edge)
        {
            switch (edge)
            {
                case Hatch.BoundaryPath.Line l:
                    return new List<Vec2> { new Vec2(l.Start.X, l.Start.Y), new Vec2(l.End.X, l.End.Y) };
                case Hatch.BoundaryPath.Arc a:
                    return ArcLike(a.StartAngle, a.EndAngle, a.CounterClockWise,
                        th => new Vec2(a.Center.X + a.Radius * Math.Cos(th), a.Center.Y + a.Radius * Math.Sin(th)));
                case Hatch.BoundaryPath.Ellipse e:
                {
                    var major = new Vec2(e.MajorAxisEndPoint.X, e.MajorAxisEndPoint.Y);
                    var minor = major.Left() * e.RadiusRatio;
                    var c = new Vec2(e.Center.X, e.Center.Y);
                    return ArcLike(e.StartAngle, e.EndAngle, e.CounterClockWise, th => c + major * Math.Cos(th) + minor * Math.Sin(th));
                }
                case Hatch.BoundaryPath.Polyline pl:
                {
                    var v = pl.Vertices;
                    var pts = new List<Vec2>();
                    int n = v.Count;
                    int segs = pl.IsClosed ? n : n - 1;
                    for (int i = 0; i < segs; i++)
                    {
                        var a = new Vec2(v[i].X, v[i].Y); var b = new Vec2(v[(i + 1) % n].X, v[(i + 1) % n].Y);
                        pts.Add(a);
                        double bulge = v[i].Z;
                        if (Math.Abs(bulge) > 1e-9) pts.AddRange(BulgePoints(a, b, bulge));
                    }
                    if (n > 0) pts.Add(pl.IsClosed ? new Vec2(v[0].X, v[0].Y) : new Vec2(v[n - 1].X, v[n - 1].Y));
                    return pts;
                }
                case Hatch.BoundaryPath.Spline sp:
                {
                    var ctrl = sp.ControlPoints.Select(q => new XYZ(q.X, q.Y, 0)).ToArray();
                    var w = sp.ControlPoints.Select(q => sp.IsRational && q.Z > 0 ? q.Z : 1.0).ToArray();
                    var pts = SplinePoints.Curve(ctrl, w, sp.Knots.ToArray(), sp.Degree, sp.IsPeriodic,
                        sp.FitPoints.Select(f => new XYZ(f.X, f.Y, 0)).ToList(), 48);
                    return pts.Select(q => new Vec2(q.X, q.Y)).ToList();
                }
                default:
                    return new List<Vec2>();
            }
        }

        /// <summary>A circular or elliptical edge. A clockwise edge keeps its angles mirrored
        /// (as AutoCAD stores them): the curve runs clockwise through -start .. -end.</summary>
        private static List<Vec2> ArcLike(double start, double end, bool ccw, Func<double, Vec2> at)
        {
            double sweep = end - start;
            while (sweep <= 1e-12) sweep += 2 * Math.PI;
            if (sweep > 2 * Math.PI + 1e-9) sweep = 2 * Math.PI;
            int n = Math.Max(4, (int)Math.Ceiling(sweep / (Math.PI / 32)));
            var pts = new List<Vec2>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                double th = start + sweep * i / n;
                pts.Add(at(ccw ? th : -th));
            }
            return pts;
        }

        private static IEnumerable<Vec2> BulgePoints(Vec2 a, Vec2 b, double bulge)
        {
            double chord = Vec2.Distance(a, b);
            if (chord < 1e-12) yield break;
            double sweep = 4 * Math.Atan(bulge);
            var dir = (b - a) * (1 / chord);
            var centre = (a + b) * 0.5 + dir.Left() * (chord * (1 - bulge * bulge) / (4 * bulge));
            double r = chord * (1 + bulge * bulge) / (4 * Math.Abs(bulge));
            double s0 = Math.Atan2(a.Y - centre.Y, a.X - centre.X);
            int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 32)));
            for (int i = 1; i < n; i++)
            {
                double th = s0 + sweep * i / n;
                yield return new Vec2(centre.X + r * Math.Cos(th), centre.Y + r * Math.Sin(th));
            }
        }

        /// <summary>The pattern's dashes clipped to the loops (even-odd), in the hatch's plane.
        /// Pattern lines as stored in the drawing are already scaled and turned.</summary>
        public static List<(Vec2 A, Vec2 B)> PatternSegments(Hatch h, List<List<Vec2>> loops)
        {
            var result = new List<(Vec2, Vec2)>();
            if (loops.Count == 0 || h.Pattern?.Lines == null) return result;
            double minX = loops.SelectMany(l => l).Min(p => p.X), maxX = loops.SelectMany(l => l).Max(p => p.X);
            double minY = loops.SelectMany(l => l).Min(p => p.Y), maxY = loops.SelectMany(l => l).Max(p => p.Y);
            var corners = new[] { new Vec2(minX, minY), new Vec2(maxX, minY), new Vec2(maxX, maxY), new Vec2(minX, maxY) };
            foreach (var pl in h.Pattern.Lines)
            {
                var d = new Vec2(Math.Cos(pl.Angle), Math.Sin(pl.Angle));
                var nrm = d.Left();
                var basePt = new Vec2(pl.BasePoint.X, pl.BasePoint.Y);
                var off = new Vec2(pl.Offset.X, pl.Offset.Y);
                double spacing = off.X * nrm.X + off.Y * nrm.Y;
                if (Math.Abs(spacing) < 1e-9) continue;
                double b0 = basePt.X * nrm.X + basePt.Y * nrm.Y;
                var proj = corners.Select(c => (c.X * nrm.X + c.Y * nrm.Y - b0) / spacing).ToList();
                long k0 = (long)Math.Floor(proj.Min()), k1 = (long)Math.Ceiling(proj.Max());
                if (k1 - k0 > MaxPatternSegments) return result;
                var dashes = pl.DashLengths ?? new List<double>();
                double period = dashes.Sum(x => Math.Abs(x));
                for (long k = k0; k <= k1; k++)
                {
                    var origin = basePt + off * k;
                    // Where this line crosses the boundary, as distances along d from origin.
                    var ts = new List<double>();
                    foreach (var loop in loops)
                        for (int i = 0; i < loop.Count; i++)
                        {
                            var p = loop[i]; var q = loop[(i + 1) % loop.Count];
                            double sp = (p.X - origin.X) * nrm.X + (p.Y - origin.Y) * nrm.Y;
                            double sq = (q.X - origin.X) * nrm.X + (q.Y - origin.Y) * nrm.Y;
                            if ((sp > 0) == (sq > 0)) continue;
                            double f = sp / (sp - sq);
                            var x = p + (q - p) * f;
                            ts.Add((x.X - origin.X) * d.X + (x.Y - origin.Y) * d.Y);
                        }
                    if (ts.Count < 2) continue;
                    ts.Sort();
                    for (int i = 0; i + 1 < ts.Count; i += 2)
                    {
                        double a = ts[i], b = ts[i + 1];
                        if (dashes.Count == 0 || period < 1e-9) { Add(origin + d * a, origin + d * b); continue; }
                        // Walk the dash pattern (positive = dash, negative = gap, 0 = a dot) from the line's origin.
                        double t = Math.Floor(a / period) * period;
                        while (t < b)
                        {
                            foreach (var len in dashes)
                            {
                                double s = t, e = t + Math.Abs(len);
                                if (len >= 0)
                                {
                                    double ca = Math.Max(s, a), cb = Math.Min(len == 0 ? s + period * 1e-3 : e, b);
                                    if (cb > ca) Add(origin + d * ca, origin + d * cb);
                                }
                                t = e;
                                if (t >= b) break;
                            }
                            if (result.Count > MaxPatternSegments) return result;
                        }
                    }
                    if (result.Count > MaxPatternSegments) return result;
                }
            }
            return result;

            void Add(Vec2 a, Vec2 b) => result.Add((a, b));
        }
    }
}
