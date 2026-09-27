using System;
using System.Collections.Generic;
using System.Linq;
using ACadSharp.Entities;
using CSMath;

namespace FdDraft.View
{
    /// <summary>
    /// Points along a SPLINE, worked out here rather than by ACadSharp's
    /// <c>Spline.PolygonalVertexes</c>: that samples from the very first knot to the last, and on an
    /// unclamped (closed / periodic) spline - text and logos exploded to outlines are made of these -
    /// the curve isn't defined at the first knot, so it returns (0,0) there. Drawn, every glyph
    /// then grew a line back to its block's base point: a fan of rays out of a title-block logo.
    /// Here the curve is sampled only over its valid span [knot[degree], knot[n]], by de Boor.
    /// </summary>
    public static class SplinePoints
    {
        /// <summary>The spline as a polyline of about <paramref name="segments"/> pieces (block
        /// coordinates). Falls back to the fit points; empty when there's nothing usable.</summary>
        public static List<XYZ> Of(Spline sp, int segments = 96) =>
            Curve(sp.ControlPoints.ToArray(), sp.Weights.ToArray(), sp.Knots.ToArray(), sp.Degree, sp.IsClosed, sp.FitPoints.ToList(), segments);

        /// <summary>The same for raw spline data (a hatch boundary's spline edge).</summary>
        public static List<XYZ> Curve(XYZ[] ctrl, double[] weights, double[] knots, int p, bool closed, List<XYZ> fitPoints, int segments = 96)
        {
            int n = ctrl.Length;
            var w = weights.Length == n ? weights : Enumerable.Repeat(1.0, n).ToArray();
            // A closed spline may keep its control points unwrapped, with p more knots than an open
            // one: wrap the first p points round to the end so it is an ordinary B-spline.
            if (p >= 1 && n > p && knots.Length == n + 2 * p + 1)
            {
                ctrl = ctrl.Concat(ctrl.Take(p)).ToArray();
                w = w.Concat(w.Take(p)).ToArray();
                n = ctrl.Length;
            }
            // No (or an odd) knot vector: uniform, clamped when open, unclamped when closed.
            if (p >= 1 && n > p && knots.Length != n + p + 1)
            {
                if (closed)
                {
                    ctrl = ctrl.Concat(ctrl.Take(p)).ToArray(); w = w.Concat(w.Take(p)).ToArray(); n = ctrl.Length;
                    knots = Enumerable.Range(0, n + p + 1).Select(i => (double)i).ToArray();
                }
                else knots = Enumerable.Range(0, n + p + 1).Select(i => (double)Math.Min(Math.Max(i - p, 0), n - p)).ToArray();
            }
            if (n >= 2 && p >= 1 && knots.Length == n + p + 1)
            {
                double u0 = knots[p], u1 = knots[n];
                if (u1 > u0 && !double.IsNaN(u0) && !double.IsNaN(u1))
                {
                    var pts = new List<XYZ>(segments + 2);
                    for (int i = 0; i <= segments; i++)
                    {
                        double u = i == segments ? u1 : u0 + (u1 - u0) * i / segments;
                        if (Evaluate(ctrl, w, knots, p, u) is XYZ pt) pts.Add(pt);
                    }
                    if (closed && pts.Count > 2 && !Same(pts[0], pts[pts.Count - 1])) pts.Add(pts[0]);
                    if (pts.Count >= 2) return pts;
                }
            }
            // No usable control frame: join the fit points.
            var fit = fitPoints.ToList();
            if (fit.Count >= 2 && closed && !Same(fit[0], fit[fit.Count - 1])) fit.Add(fit[0]);
            return fit.Count >= 2 ? fit : new List<XYZ>();
        }

        private static bool Same(XYZ a, XYZ b) => Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9 && Math.Abs(a.Z - b.Z) < 1e-9;

        /// <summary>De Boor on the rational (homogeneous) points; null if the weights vanish.</summary>
        private static XYZ? Evaluate(XYZ[] c, double[] w, double[] U, int p, double u)
        {
            int n = c.Length;
            // The knot span k with U[k] <= u < U[k+1], kept inside [p, n-1].
            int k = p;
            while (k < n - 1 && u >= U[k + 1]) k++;
            var dx = new double[p + 1]; var dy = new double[p + 1]; var dz = new double[p + 1]; var dw = new double[p + 1];
            for (int j = 0; j <= p; j++)
            {
                int i = j + k - p;
                double wi = w[i];
                dx[j] = c[i].X * wi; dy[j] = c[i].Y * wi; dz[j] = c[i].Z * wi; dw[j] = wi;
            }
            for (int r = 1; r <= p; r++)
            {
                for (int j = p; j >= r; j--)
                {
                    int i = j + k - p;
                    double den = U[i + p - r + 1] - U[i];
                    double a = den == 0 ? 0 : (u - U[i]) / den;
                    dx[j] = (1 - a) * dx[j - 1] + a * dx[j];
                    dy[j] = (1 - a) * dy[j - 1] + a * dy[j];
                    dz[j] = (1 - a) * dz[j - 1] + a * dz[j];
                    dw[j] = (1 - a) * dw[j - 1] + a * dw[j];
                }
            }
            if (Math.Abs(dw[p]) < 1e-15 || double.IsNaN(dw[p])) return null;
            return new XYZ(dx[p] / dw[p], dy[p] / dw[p], dz[p] / dw[p]);
        }
    }
}
