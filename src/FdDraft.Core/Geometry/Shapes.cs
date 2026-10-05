using System;
using System.Collections.Generic;

namespace FdDraft.Core.Geometry
{
    /// <summary>
    /// The shapes on MSCAD's Draw toolbar that come down to polylines: rectangle, regular
    /// polygon, revision cloud, donut, and circles by two or three points.
    /// </summary>
    public static class Shapes
    {
        /// <summary>The four corners of the rectangle with opposite corners a and b, counter-clockwise.</summary>
        public static List<Vec2> Rectangle(Vec2 a, Vec2 b)
        {
            double x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X), y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
            return new List<Vec2> { new Vec2(x0, y0), new Vec2(x1, y0), new Vec2(x1, y1), new Vec2(x0, y1) };
        }

        /// <summary>
        /// A regular polygon of <paramref name="sides"/> about <paramref name="center"/>: through
        /// <paramref name="at"/> as a vertex (inscribed), or with <paramref name="at"/> as the
        /// midpoint of a side (circumscribed).
        /// </summary>
        public static List<Vec2> RegularPolygon(int sides, Vec2 center, Vec2 at, bool inscribed)
        {
            var result = new List<Vec2>();
            if (sides < 3) return result;
            double r = Vec2.Distance(center, at);
            double a0 = Math.Atan2(at.Y - center.Y, at.X - center.X);
            double step = 2 * Math.PI / sides;
            if (!inscribed) { r /= Math.Cos(step / 2); a0 += step / 2; }
            for (int i = 0; i < sides; i++) result.Add(new Vec2(center.X + r * Math.Cos(a0 + i * step), center.Y + r * Math.Sin(a0 + i * step)));
            return result;
        }

        /// <summary>A regular polygon with one side from a to b, the rest to its left.</summary>
        public static List<Vec2> PolygonOnEdge(int sides, Vec2 a, Vec2 b)
        {
            var result = new List<Vec2>();
            if (sides < 3 || Vec2.Distance(a, b) < 1e-12) return result;
            double ext = 2 * Math.PI / sides;
            var p = a; double dir = Math.Atan2(b.Y - a.Y, b.X - a.X), len = Vec2.Distance(a, b);
            for (int i = 0; i < sides; i++)
            {
                result.Add(p);
                p = new Vec2(p.X + len * Math.Cos(dir), p.Y + len * Math.Sin(dir));
                dir += ext;
            }
            return result;
        }

        /// <summary>
        /// A revision cloud round a closed outline: each side cut into arcs about
        /// <paramref name="arcChord"/> long, bulging outward. Returns the vertices and their bulges.
        /// </summary>
        public static (List<Vec2> Points, List<double> Bulges) RevisionCloud(IList<Vec2> outline, double arcChord)
        {
            var pts = new List<Vec2>(); var bulges = new List<double>();
            int n = outline.Count;
            if (n < 2 || arcChord <= 0) return (pts, bulges);
            // Outward is to the right of travel on a counter-clockwise outline.
            double area = 0;
            for (int i = 0; i < n; i++) area += Vec2.Cross(outline[i], outline[(i + 1) % n]);
            double bulge = area >= 0 ? -0.6 : 0.6;
            for (int i = 0; i < n; i++)
            {
                var a = outline[i]; var b = outline[(i + 1) % n];
                double len = Vec2.Distance(a, b);
                int k = Math.Max(1, (int)Math.Round(len / arcChord));
                for (int j = 0; j < k; j++) { pts.Add(a + (b - a) * ((double)j / k)); bulges.Add(len < 1e-12 ? 0 : bulge); }
            }
            return (pts, bulges);
        }

        /// <summary>The circle with a and b at the ends of a diameter.</summary>
        public static (Vec2 Center, double Radius) CircleTwoPoints(Vec2 a, Vec2 b) => ((a + b) * 0.5, Vec2.Distance(a, b) / 2);

        /// <summary>The circle through three points; null when they're in line.</summary>
        public static (Vec2 Center, double Radius)? CircleThreePoints(Vec2 a, Vec2 b, Vec2 c)
        {
            var arc = Arc.ThroughThreePoints(a, b, c);
            return arc == null ? ((Vec2, double)?)null : (arc.Center, arc.Radius);
        }
    }
}
