using System.Collections.Generic;
using System.Linq;
using ACadSharp.Entities;
using CSMath;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;

namespace FdDraft.Cad.Editing
{
    /// <summary>SPLINE: a real DWG spline through picked points.</summary>
    public static class SplineEditing
    {
        /// <summary>
        /// A degree-3 fit-point spline through <paramref name="points"/> (closed: on round to the first
        /// point - the curve meets itself there at a joint rather than a smooth seam, as ACadSharp
        /// has no periodic fit). Control points are solved from the fit points so every reader draws it the
        /// same; if that can't be done, the control polygon falls back to a dense run of points
        /// on FD-Draft's own Catmull-Rom curve through them.
        /// </summary>
        public static Spline Through(IList<Vec2> points, bool closed)
        {
            var fit = points.Select(p => new XYZ(p.X, p.Y, 0)).ToList();
            if (closed) fit.Add(fit[0]);
            var sp = new Spline { Degree = 3 };
            sp.FitPoints.AddRange(fit);
            if (fit.Count >= 3 && sp.UpdateFromFitPoints() && sp.TryPolygonalVertexes(64, out var v) && v.Count > 1) return sp;
            // Fallback: a degree-1 spline along the sampled curve - still a SPLINE, exact at the picks.
            var ds = new DraftSpline { Closed = closed };
            ds.FitPoints.AddRange(points);
            var dense = ds.Sample(12);
            var lin = new Spline { Degree = 1 };
            lin.ControlPoints.AddRange(dense.Select(p => new XYZ(p.X, p.Y, 0)));
            int n = dense.Count;
            lin.Knots.Add(0); lin.Knots.Add(0);
            for (int i = 1; i < n - 1; i++) lin.Knots.Add(i);
            lin.Knots.Add(n - 1); lin.Knots.Add(n - 1);
            lin.Weights.AddRange(Enumerable.Repeat(1.0, n));
            return lin;
        }
    }
}
