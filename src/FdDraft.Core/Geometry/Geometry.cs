using System;
using System.Collections.Generic;
using System.Globalization;

namespace FdDraft.Core.Geometry
{
    /// <summary>
    /// A plan-space point. X is EASTING and Y is NORTHING, the way every CAD
    /// program lays them out. The survey convention (N, E) lives at the edges,
    /// in labels and reports, never in the arithmetic.
    /// </summary>
    public readonly struct Vec2
    {
        public readonly double X;
        public readonly double Y;

        public Vec2(double x, double y) { X = x; Y = y; }

        public static Vec2 FromNE(double northing, double easting) => new Vec2(easting, northing);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, double k) => new Vec2(a.X * k, a.Y * k);

        public double Length => Math.Sqrt(X * X + Y * Y);
        public Vec2 Normalized() { double l = Length; return l > 0 ? new Vec2(X / l, Y / l) : new Vec2(0, 0); }
        /// <summary>Rotated 90 degrees counter-clockwise (to the left of the direction of travel).</summary>
        public Vec2 Left() => new Vec2(-Y, X);
        public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
        public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "N {0:F3}, E {1:F3}", Y, X);
    }

    /// <summary>Axis-aligned extents of everything drawn.</summary>
    public sealed class Extents
    {
        public double MinX = double.PositiveInfinity, MinY = double.PositiveInfinity;
        public double MaxX = double.NegativeInfinity, MaxY = double.NegativeInfinity;

        public bool IsEmpty => MinX > MaxX;
        public double Width => IsEmpty ? 0 : MaxX - MinX;
        public double Height => IsEmpty ? 0 : MaxY - MinY;
        public Vec2 Center => new Vec2((MinX + MaxX) / 2, (MinY + MaxY) / 2);

        public void Add(Vec2 p)
        {
            if (p.X < MinX) MinX = p.X;
            if (p.Y < MinY) MinY = p.Y;
            if (p.X > MaxX) MaxX = p.X;
            if (p.Y > MaxY) MaxY = p.Y;
        }

        public void Add(Extents other)
        {
            if (other.IsEmpty) return;
            Add(new Vec2(other.MinX, other.MinY));
            Add(new Vec2(other.MaxX, other.MaxY));
        }
    }

    /// <summary>A circular arc through three points, in plan coordinates.</summary>
    public sealed class Arc
    {
        public Vec2 Center;
        public double Radius;
        /// <summary>Start angle in radians, counter-clockwise from +X (east).</summary>
        public double StartAngle;
        /// <summary>Signed sweep in radians: positive counter-clockwise, negative clockwise.</summary>
        public double Sweep;
        public Vec2 Start;
        public Vec2 End;

        public double Length => Math.Abs(Sweep) * Radius;
        public double ChordLength => Vec2.Distance(Start, End);

        /// <summary>
        /// AutoCAD/DXF polyline bulge for this arc as one segment from Start to End:
        /// tan(sweep/4), positive counter-clockwise.
        /// </summary>
        public double Bulge => Math.Tan(Sweep / 4.0);

        public Vec2 PointAt(double t)
        {
            double a = StartAngle + Sweep * t;
            return new Vec2(Center.X + Radius * Math.Cos(a), Center.Y + Radius * Math.Sin(a));
        }

        public Vec2 MidPoint => PointAt(0.5);

        /// <summary>
        /// The two arcs either side of a point on this arc (same centre and radius),
        /// e.g. split at an intermediate monument so each part gets its own curve data.
        /// </summary>
        public Arc[] SplitAt(Vec2 p)
        {
            double a = Math.Atan2(p.Y - Center.Y, p.X - Center.X);
            double first = Sweep > 0 ? Angles.Normalize2Pi(a - StartAngle) : -Angles.Normalize2Pi(StartAngle - a);
            return new[]
            {
                new Arc { Center = Center, Radius = Radius, StartAngle = StartAngle, Sweep = first, Start = Start, End = p },
                new Arc { Center = Center, Radius = Radius, StartAngle = StartAngle + first, Sweep = Sweep - first, Start = p, End = End },
            };
        }

        /// <summary>
        /// The arc from p1 through p2 to p3, or null when the three are (nearly)
        /// collinear or repeated - FD-Pro draws those as a straight line p1-p3, and
        /// so does FD-Draft.
        /// </summary>
        public static Arc? ThroughThreePoints(Vec2 p1, Vec2 p2, Vec2 p3)
        {
            double ax = p1.X, ay = p1.Y, bx = p2.X, by = p2.Y, cx = p3.X, cy = p3.Y;
            double d = 2.0 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            double scale = Math.Max(Vec2.Distance(p1, p3), 1e-9);
            // Relative test: d has units of length^2, so compare against the chord squared.
            if (Math.Abs(d) < 1e-9 * scale * scale) return null;
            double a2 = ax * ax + ay * ay, b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
            double ux = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / d;
            double uy = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / d;
            var center = new Vec2(ux, uy);
            double r = Vec2.Distance(center, p1);
            double a0 = Math.Atan2(ay - uy, ax - ux);
            double a1 = Math.Atan2(by - uy, bx - ux);
            double a3 = Math.Atan2(cy - uy, cx - ux);
            double ccwTo3 = Angles.Normalize2Pi(a3 - a0);
            double ccwTo2 = Angles.Normalize2Pi(a1 - a0);
            double sweep = ccwTo2 < ccwTo3 ? ccwTo3 : -(2 * Math.PI - ccwTo3);
            return new Arc { Center = center, Radius = r, StartAngle = a0, Sweep = sweep, Start = p1, End = p3 };
        }
    }

    public static class Angles
    {
        public const double TwoPi = 2 * Math.PI;

        public static double Normalize2Pi(double a)
        {
            a %= TwoPi;
            if (a < 0) a += TwoPi;
            return a;
        }

        /// <summary>Grid azimuth (radians, clockwise from north) of the line from a to b.</summary>
        public static double Azimuth(Vec2 a, Vec2 b) => Normalize2Pi(Math.Atan2(b.X - a.X, b.Y - a.Y));

        /// <summary>
        /// A quadrant bearing the way it is written on an Ontario plan: N32°10'45"E.
        /// Seconds are rounded to <paramref name="secondsDecimals"/> places and the
        /// carry is done in whole seconds, so 59.6" never prints as 60".
        /// </summary>
        public static string FormatBearing(double azimuthRadians, int secondsDecimals = 0, string degreeSymbol = "°")
        {
            double azDeg = Normalize2Pi(azimuthRadians) * 180.0 / Math.PI;
            string ns, ew;
            double q;
            if (azDeg <= 90) { ns = "N"; ew = "E"; q = azDeg; }
            else if (azDeg <= 180) { ns = "S"; ew = "E"; q = 180 - azDeg; }
            else if (azDeg <= 270) { ns = "S"; ew = "W"; q = azDeg - 180; }
            else { ns = "N"; ew = "W"; q = 360 - azDeg; }

            double factor = Math.Pow(10, secondsDecimals);
            // Work in integer units of the last printed decimal of a second.
            long units = (long)Math.Round(q * 3600.0 * factor, MidpointRounding.AwayFromZero);
            long unitsPerSecond = (long)factor;
            long totalSeconds = units / unitsPerSecond;
            long frac = units % unitsPerSecond;
            long deg = totalSeconds / 3600;
            long min = (totalSeconds % 3600) / 60;
            long sec = totalSeconds % 60;
            string secText = secondsDecimals > 0
                ? sec.ToString("00", CultureInfo.InvariantCulture) + "." + frac.ToString(new string('0', secondsDecimals), CultureInfo.InvariantCulture)
                : sec.ToString("00", CultureInfo.InvariantCulture);
            return ns + deg.ToString("00", CultureInfo.InvariantCulture) + degreeSymbol +
                   min.ToString("00", CultureInfo.InvariantCulture) + "'" + secText + "\"" + ew;
        }

        /// <summary>
        /// Text rotation (radians, CCW from +X) that runs along a line and still reads
        /// left-to-right: never upside down.
        /// </summary>
        public static double ReadableRotation(Vec2 a, Vec2 b)
        {
            double r = Math.Atan2(b.Y - a.Y, b.X - a.X);
            if (r > Math.PI / 2 + 1e-9) r -= Math.PI;
            else if (r <= -Math.PI / 2 + 1e-9) r += Math.PI;
            return r;
        }
    }

    public static class Polygon
    {
        /// <summary>
        /// Signed area of a closed polyline whose segments may be arcs (bulges),
        /// positive when counter-clockwise. Each arc adds its circular segment.
        /// </summary>
        public static double SignedArea(IList<Vec2> vertices, IList<double> bulges)
        {
            int n = vertices.Count;
            if (n < 2) return 0;
            double area = 0;
            for (int i = 0; i < n; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % n];
                area += Vec2.Cross(a, b) / 2.0;
                double bulge = i < bulges.Count ? bulges[i] : 0;
                if (bulge != 0)
                {
                    double theta = 4 * Math.Atan(bulge);
                    double chord = Vec2.Distance(a, b);
                    double r = chord / (2 * Math.Sin(Math.Abs(theta) / 2));
                    double segment = r * r / 2 * (Math.Abs(theta) - Math.Sin(Math.Abs(theta)));
                    area += Math.Sign(theta) * segment;
                }
            }
            return area;
        }

        /// <summary>
        /// A point well inside a simple polygon for placing its area label: the
        /// centroid when it falls inside, otherwise the midpoint of the widest
        /// horizontal interior span through the centroid's height.
        /// </summary>
        public static Vec2 LabelPoint(IList<Vec2> v)
        {
            int n = v.Count;
            double cx = 0, cy = 0, a2 = 0;
            for (int i = 0; i < n; i++)
            {
                var p = v[i];
                var q = v[(i + 1) % n];
                double c = Vec2.Cross(p, q);
                a2 += c;
                cx += (p.X + q.X) * c;
                cy += (p.Y + q.Y) * c;
            }
            Vec2 centroid;
            if (Math.Abs(a2) < 1e-12)
            {
                double sx = 0, sy = 0;
                foreach (var p in v) { sx += p.X; sy += p.Y; }
                centroid = new Vec2(sx / n, sy / n);
            }
            else centroid = new Vec2(cx / (3 * a2), cy / (3 * a2));
            if (Contains(v, centroid)) return centroid;

            var xs = new List<double>();
            for (int i = 0; i < n; i++)
            {
                var p = v[i];
                var q = v[(i + 1) % n];
                if ((p.Y <= centroid.Y && q.Y > centroid.Y) || (q.Y <= centroid.Y && p.Y > centroid.Y))
                    xs.Add(p.X + (centroid.Y - p.Y) / (q.Y - p.Y) * (q.X - p.X));
            }
            xs.Sort();
            double best = -1, bx = centroid.X;
            for (int i = 0; i + 1 < xs.Count; i += 2)
            {
                if (xs[i + 1] - xs[i] > best) { best = xs[i + 1] - xs[i]; bx = (xs[i] + xs[i + 1]) / 2; }
            }
            return new Vec2(bx, centroid.Y);
        }

        public static bool Contains(IList<Vec2> v, Vec2 p)
        {
            bool inside = false;
            for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
            {
                if ((v[i].Y > p.Y) != (v[j].Y > p.Y) &&
                    p.X < (v[j].X - v[i].X) * (p.Y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X)
                    inside = !inside;
            }
            return inside;
        }
    }
}
