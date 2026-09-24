using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FdDraft.Core.Geometry
{
    /// <summary>
    /// How well a traverse returns to its start: the misclosure (the course from where the
    /// last leg ends back to the first point), the precision ratio a surveyor quotes
    /// (1 : traverse length ÷ misclosure), and the area of the figure once it is closed.
    /// Used by LINE's Close option and by AREA.
    /// </summary>
    public sealed class ClosureReport
    {
        /// <summary>Easting/northing error: where the traverse ended minus where it started.</summary>
        public double DeltaE { get; }
        public double DeltaN { get; }
        public double Misclosure => Math.Sqrt(DeltaE * DeltaE + DeltaN * DeltaN);
        /// <summary>Total length of the legs as run (not counting the closing course).</summary>
        public double TraverseLength { get; }
        /// <summary>Area of the polygon the legs make once closed back to the start.</summary>
        public double Area { get; }
        /// <summary>Grid azimuth (radians) of the closing course, end back to start.</summary>
        public double ClosingAzimuth { get; }

        public ClosureReport(double dE, double dN, double length, double area, double closingAzimuth)
        {
            DeltaE = dE; DeltaN = dN; TraverseLength = length; Area = area; ClosingAzimuth = closingAzimuth;
        }

        /// <summary>The "1 : n" precision, or null for a perfect closure.</summary>
        public double? Precision => Misclosure < 1e-9 ? (double?)null : TraverseLength / Misclosure;

        public static string FormatPrecision(double? p) =>
            p == null ? "perfect closure" : "1:" + Math.Round(p.Value).ToString("#,0", CultureInfo.InvariantCulture);

        /// <summary>The closure of the traverse that visited <paramref name="points"/> in order,
        /// judged against returning to the first one.</summary>
        public static ClosureReport Of(IList<Vec2> points)
        {
            if (points.Count < 2) return new ClosureReport(0, 0, 0, 0, 0);
            double length = 0;
            for (int i = 1; i < points.Count; i++) length += Vec2.Distance(points[i - 1], points[i]);
            var start = points[0];
            var end = points[points.Count - 1];
            double area = points.Count >= 3 ? Math.Abs(Polygon.SignedArea(points, new double[0])) : 0;
            return new ClosureReport(end.X - start.X, end.Y - start.Y, length, area, Angles.Azimuth(end, start));
        }
    }

    /// <summary>Area and perimeter of a closed figure whose spans may be arcs.</summary>
    public static class FigureMeasure
    {
        public static double Perimeter(IList<Vec2> pts, IList<double>? bulges, bool closed) =>
            Construct.Spans(pts, bulges, closed).Sum(s => s.IsArc ? s.Radius * Math.Abs(s.Sweep) : Vec2.Distance(s.A, s.B));

        /// <summary>Area enclosed (always positive), arcs' circular segments included.</summary>
        public static double Area(IList<Vec2> pts, IList<double>? bulges) =>
            Math.Abs(Polygon.SignedArea(pts, bulges ?? new double[0]));
    }
}
