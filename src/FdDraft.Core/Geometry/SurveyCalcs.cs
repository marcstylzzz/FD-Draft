using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FdDraft.Core.Geometry
{
    /// <summary>A circular curve's elements, all from any two of them (MSCAD's COGO curve calculator).</summary>
    public sealed class CurveElements
    {
        /// <summary>Radius.</summary>
        public double R;
        /// <summary>Central angle (delta), radians.</summary>
        public double Delta;
        /// <summary>Arc length.</summary>
        public double L;
        /// <summary>Chord.</summary>
        public double C;
        /// <summary>Tangent length.</summary>
        public double T;
        /// <summary>External distance.</summary>
        public double E;
        /// <summary>Middle ordinate.</summary>
        public double M;

        public static CurveElements FromRadiusDelta(double r, double delta) => new CurveElements
        {
            R = r, Delta = delta, L = r * delta, C = 2 * r * Math.Sin(delta / 2), T = r * Math.Tan(delta / 2),
            E = r * (1 / Math.Cos(delta / 2) - 1), M = r * (1 - Math.Cos(delta / 2)),
        };

        public IEnumerable<string> Report(int decimals = 3)
        {
            string f = "F" + decimals;
            string N(double v) => double.IsInfinity(v) || double.IsNaN(v) ? "-" : v.ToString(f, CultureInfo.InvariantCulture);
            yield return "R  radius          " + N(R);
            yield return "Δ  delta           " + SurveyCalcs.Dms(Delta);
            yield return "L  arc length      " + N(L);
            yield return "C  chord           " + N(C);
            yield return "T  tangent         " + N(T);
            yield return "E  external        " + N(E);
            yield return "M  middle ordinate " + N(M);
        }
    }

    /// <summary>Survey calculations behind MSCAD's Calcs and Labels tools - pure math, unit-tested.</summary>
    public static class SurveyCalcs
    {
        // ---- curve solver --------------------------------------------------------------------------

        /// <summary>Each element as a multiple of the radius, for a delta.</summary>
        private static double Factor(char k, double d) => k switch
        {
            'L' => d,
            'C' => 2 * Math.Sin(d / 2),
            'T' => Math.Tan(d / 2),
            'E' => 1 / Math.Cos(d / 2) - 1,
            'M' => 1 - Math.Cos(d / 2),
            _ => double.NaN,
        };

        /// <summary>
        /// Solves a curve from any two known elements: keys R, D (delta, radians), L, C, T, E, M.
        /// Null when the pair can't define a curve (two of the same, impossible values).
        /// </summary>
        public static CurveElements? SolveCurve(IDictionary<char, double> known)
        {
            if (known.Count < 2) return null;
            var keys = known.Keys.Select(char.ToUpperInvariant).ToList();
            if (keys.Any(k => "RDLCTEM".IndexOf(k) < 0) || keys.Distinct().Count() != keys.Count) return null;
            double Get(char k) => known.First(kv => char.ToUpperInvariant(kv.Key) == k).Value;
            if (known.Values.Any(v => !(v > 0))) return null;
            if (keys.Contains('R') && keys.Contains('D')) return CurveElements.FromRadiusDelta(Get('R'), Get('D'));
            if (keys.Contains('D'))
            {
                char k = keys.First(x => x != 'D');
                double d = Get('D');
                double f = Factor(k, d);
                if (!(f > 0) || d >= 2 * Math.PI) return null;
                return CurveElements.FromRadiusDelta(Get(k) / f, d);
            }
            if (keys.Contains('R'))
            {
                char k = keys.First(x => x != 'R');
                double r = Get('R'), q = Get(k) / r, d;
                switch (k)
                {
                    case 'L': d = q; break;
                    case 'C': if (q > 2) return null; d = 2 * Math.Asin(q / 2); break;
                    case 'T': d = 2 * Math.Atan(q); break;
                    case 'E': d = 2 * Math.Acos(1 / (1 + q)); break;
                    case 'M': if (q > 1) return null; d = 2 * Math.Acos(1 - q); break;
                    default: return null;
                }
                return d > 0 && d < 2 * Math.PI ? CurveElements.FromRadiusDelta(r, d) : null;
            }
            // Two lengths: their ratio fixes delta (found by bisection), then either gives R.
            char a = keys[0], b = keys[1];
            double target = Get(a) / Get(b);
            bool tangentLike = "TE".IndexOf(a) >= 0 || "TE".IndexOf(b) >= 0;
            double hi = tangentLike ? Math.PI - 1e-9 : 2 * Math.PI - 1e-9;
            double G(double d) => Factor(a, d) / Factor(b, d) - target;
            // Scan for a sign change, then bisect it.
            const int steps = 720;
            double prevD = 1e-7, prevG = G(prevD);
            for (int i = 1; i <= steps; i++)
            {
                double d = 1e-7 + (hi - 1e-7) * i / steps, g = G(d);
                if (double.IsNaN(g) || double.IsNaN(prevG)) { prevD = d; prevG = g; continue; }
                if (Math.Sign(g) != Math.Sign(prevG) || g == 0)
                {
                    double lo = prevD, up = d, glo = prevG;
                    for (int it = 0; it < 200; it++)
                    {
                        double mid = (lo + up) / 2, gm = G(mid);
                        if (Math.Sign(gm) == Math.Sign(glo)) { lo = mid; glo = gm; } else up = mid;
                    }
                    double delta = (lo + up) / 2;
                    return CurveElements.FromRadiusDelta(Get(a) / Factor(a, delta), delta);
                }
                prevD = d; prevG = g;
            }
            return null;
        }

        /// <summary>Parses "R=100 L=25.5" or "R 100 D 45-30-00" (D in DMS or decimal degrees).</summary>
        public static Dictionary<char, double>? ParseCurveInput(string s)
        {
            var tokens = s.Replace("=", " ").Replace(",", " ").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length % 2 != 0) return null;
            var d = new Dictionary<char, double>();
            for (int i = 0; i < tokens.Length; i += 2)
            {
                if (tokens[i].Length != 1) return null;
                char k = char.ToUpperInvariant(tokens[i][0]);
                if (k == 'A') k = 'L'; // "A=" is how FD-Draft's labels write arc length
                if (k == 'D' || k == 'Δ')
                {
                    if (!TryParseAngle(tokens[i + 1], out double rad)) return null;
                    d['D'] = rad;
                }
                else if (double.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) d[k] = v;
                else return null;
            }
            return d;
        }

        /// <summary>An angle typed as decimal degrees (45.5) or D-M-S (45-30-00, 45°30'00", 45.3000 is decimal).</summary>
        public static bool TryParseAngle(string s, out double radians)
        {
            radians = 0;
            s = s.Trim().Replace("°", "-").Replace("'", "-").Replace("\"", "").TrimEnd('-');
            bool neg = s.StartsWith("-");
            if (neg) s = s.Substring(1);
            var parts = s.Split('-');
            if (parts.Length == 0 || parts.Length > 3) return false;
            double deg = 0;
            double[] div = { 1, 60, 3600 };
            for (int i = 0; i < parts.Length; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0) return false;
                deg += v / div[i];
            }
            radians = (neg ? -deg : deg) * Math.PI / 180;
            return true;
        }

        /// <summary>An angle as 45°30'15" (whole seconds, carried properly).</summary>
        public static string Dms(double radians, int secondsDecimals = 0)
        {
            bool neg = radians < 0;
            double f = Math.Pow(10, secondsDecimals);
            long units = (long)Math.Round(Math.Abs(radians) * 180 / Math.PI * 3600 * f, MidpointRounding.AwayFromZero);
            long secs = units / (long)f, frac = units % (long)f;
            string s = (secs % 60).ToString("00", CultureInfo.InvariantCulture) + (secondsDecimals > 0 ? "." + frac.ToString(new string('0', secondsDecimals), CultureInfo.InvariantCulture) : "");
            return (neg ? "-" : "") + (secs / 3600).ToString(CultureInfo.InvariantCulture) + "°" + (secs % 3600 / 60).ToString("00", CultureInfo.InvariantCulture) + "'" + s + "\"";
        }

        // ---- best fit ---------------------------------------------------------------------------------

        /// <summary>The total-least-squares line through the points, as the segment spanning them.
        /// Null for fewer than two distinct points.</summary>
        public static (Vec2 A, Vec2 B, double RmsOffset)? BestFitLine(IList<Vec2> pts)
        {
            if (pts.Count < 2) return null;
            double mx = pts.Average(p => p.X), my = pts.Average(p => p.Y);
            double sxx = 0, syy = 0, sxy = 0;
            foreach (var p in pts) { double dx = p.X - mx, dy = p.Y - my; sxx += dx * dx; syy += dy * dy; sxy += dx * dy; }
            if (sxx + syy < 1e-18) return null;
            double theta = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
            var u = new Vec2(Math.Cos(theta), Math.Sin(theta));
            var c = new Vec2(mx, my);
            double tmin = double.MaxValue, tmax = double.MinValue, ss = 0;
            foreach (var p in pts)
            {
                double t = Vec2.Dot(p - c, u);
                tmin = Math.Min(tmin, t); tmax = Math.Max(tmax, t);
                double off = Vec2.Cross(u, p - c);
                ss += off * off;
            }
            return (c + u * tmin, c + u * tmax, Math.Sqrt(ss / pts.Count));
        }

        /// <summary>The least-squares circle through the points (algebraic fit, then refined), and the
        /// arc from the first point to the last going through the rest. Null for fewer than three
        /// points or collinear ones.</summary>
        public static (Arc Arc, double RmsOffset)? BestFitArc(IList<Vec2> pts)
        {
            if (pts.Count < 3) return null;
            // Work relative to the centroid for conditioning.
            double mx = pts.Average(p => p.X), my = pts.Average(p => p.Y);
            double suu = 0, svv = 0, suv = 0, suuu = 0, svvv = 0, suvv = 0, svuu = 0;
            foreach (var p in pts)
            {
                double u = p.X - mx, v = p.Y - my;
                suu += u * u; svv += v * v; suv += u * v;
                suuu += u * u * u; svvv += v * v * v; suvv += u * v * v; svuu += v * u * u;
            }
            double det = suu * svv - suv * suv;
            if (Math.Abs(det) < 1e-12 * Math.Max(1, suu * svv)) return null;
            double b1 = 0.5 * (suuu + suvv), b2 = 0.5 * (svvv + svuu);
            double uc = (b1 * svv - b2 * suv) / det, vc = (suu * b2 - suv * b1) / det;
            var center = new Vec2(uc + mx, vc + my);
            double r = Math.Sqrt(uc * uc + vc * vc + (suu + svv) / pts.Count);
            // A few Gauss-Newton steps on the geometric distance.
            for (int it = 0; it < 20; it++)
            {
                double a11 = 0, a12 = 0, a13 = 0, a22 = 0, a23 = 0, a33 = 0, g1 = 0, g2 = 0, g3 = 0;
                foreach (var p in pts)
                {
                    var d = p - center; double di = d.Length;
                    if (di < 1e-12) continue;
                    double j1 = -d.X / di, j2 = -d.Y / di, j3 = -1, res = di - r;
                    a11 += j1 * j1; a12 += j1 * j2; a13 += j1 * j3; a22 += j2 * j2; a23 += j2 * j3; a33 += j3 * j3;
                    g1 += j1 * res; g2 += j2 * res; g3 += j3 * res;
                }
                var step = Solve3(a11, a12, a13, a22, a23, a33, -g1, -g2, -g3);
                if (step == null) break;
                center = new Vec2(center.X + step.Value.x, center.Y + step.Value.y);
                r += step.Value.z;
                if (Math.Abs(step.Value.x) + Math.Abs(step.Value.y) + Math.Abs(step.Value.z) < 1e-12) break;
            }
            double ss = pts.Sum(p => { double e = Vec2.Distance(p, center) - r; return e * e; });
            var arc = Arc.ThroughThreePoints(pts[0], pts[pts.Count / 2], pts[pts.Count - 1]);
            if (arc == null) return null;
            // Same sense as the three-point arc through first/middle/last, on the fitted circle.
            double a0 = Math.Atan2(pts[0].Y - center.Y, pts[0].X - center.X);
            double a2 = Math.Atan2(pts[pts.Count - 1].Y - center.Y, pts[pts.Count - 1].X - center.X);
            double sweep = arc.Sweep > 0 ? Angles.Normalize2Pi(a2 - a0) : -Angles.Normalize2Pi(a0 - a2);
            var fitted = new Arc
            {
                Center = center, Radius = r, StartAngle = a0, Sweep = sweep,
                Start = center + new Vec2(Math.Cos(a0), Math.Sin(a0)) * r, End = center + new Vec2(Math.Cos(a2), Math.Sin(a2)) * r,
            };
            return (fitted, Math.Sqrt(ss / pts.Count));
        }

        private static (double x, double y, double z)? Solve3(double a11, double a12, double a13, double a22, double a23, double a33, double b1, double b2, double b3)
        {
            double det = a11 * (a22 * a33 - a23 * a23) - a12 * (a12 * a33 - a23 * a13) + a13 * (a12 * a23 - a22 * a13);
            if (Math.Abs(det) < 1e-18) return null;
            double x = (b1 * (a22 * a33 - a23 * a23) - a12 * (b2 * a33 - a23 * b3) + a13 * (b2 * a23 - a22 * b3)) / det;
            double y = (a11 * (b2 * a33 - a23 * b3) - b1 * (a12 * a33 - a23 * a13) + a13 * (a12 * b3 - b2 * a13)) / det;
            double z = (a11 * (a22 * b3 - b2 * a23) - a12 * (a12 * b3 - b2 * a13) + b1 * (a12 * a23 - a22 * a13)) / det;
            return (x, y, z);
        }

        // ---- COGO --------------------------------------------------------------------------------------

        /// <summary>From <paramref name="occupied"/>, backsighting <paramref name="backsight"/>: turn
        /// <paramref name="angleRight"/> (clockwise, radians) and measure <paramref name="distance"/>.</summary>
        public static Vec2 TurnedAngle(Vec2 occupied, Vec2 backsight, double angleRight, double distance)
        {
            double az = Angles.Azimuth(occupied, backsight) + angleRight;
            return new Vec2(occupied.X + distance * Math.Sin(az), occupied.Y + distance * Math.Cos(az));
        }

        /// <summary>The point <paramref name="station"/> along a→b (0 at a) and <paramref name="offset"/>
        /// square off it - positive to the right looking from a to b.</summary>
        public static Vec2 StationOffset(Vec2 a, Vec2 b, double station, double offset)
        {
            var u = (b - a).Normalized();
            var right = new Vec2(u.Y, -u.X);
            return a + u * station + right * offset;
        }

        /// <summary>Station and offset (right positive) of a point relative to a→b.</summary>
        public static (double Station, double Offset) ToStationOffset(Vec2 a, Vec2 b, Vec2 p)
        {
            var u = (b - a).Normalized();
            return (Vec2.Dot(p - a, u), Vec2.Cross(p - a, u));
        }

        /// <summary>The points where lines from <paramref name="from"/> touch the circle; empty when it's inside.</summary>
        public static List<Vec2> TangentPoints(Vec2 from, Vec2 center, double radius)
        {
            var d = from - center;
            double dist = d.Length;
            var list = new List<Vec2>();
            if (dist <= radius * (1 + 1e-12)) return list;
            double a = Math.Atan2(d.Y, d.X), b = Math.Acos(radius / dist);
            list.Add(center + new Vec2(Math.Cos(a - b), Math.Sin(a - b)) * radius);
            list.Add(center + new Vec2(Math.Cos(a + b), Math.Sin(a + b)) * radius);
            return list;
        }

        /// <summary>
        /// The arc that leaves <paramref name="start"/> heading <paramref name="direction"/> (tangent to
        /// the line it continues), turning left or right, with the given radius and arc length.
        /// </summary>
        public static Arc CurveOffTangent(Vec2 start, Vec2 direction, double radius, double length, bool turnLeft)
        {
            var u = direction.Normalized();
            var n = turnLeft ? u.Left() : new Vec2(u.Y, -u.X);
            var c = start + n * radius;
            double a0 = Math.Atan2(start.Y - c.Y, start.X - c.X);
            double sweep = (turnLeft ? 1 : -1) * length / radius;
            return new Arc
            {
                Center = c, Radius = radius, StartAngle = a0, Sweep = sweep, Start = start,
                End = c + new Vec2(Math.Cos(a0 + sweep), Math.Sin(a0 + sweep)) * radius,
            };
        }

        /// <summary>The foot of the perpendicular from p on the infinite line through a and b.</summary>
        public static Vec2 Foot(Vec2 p, Vec2 a, Vec2 b)
        {
            var u = (b - a).Normalized();
            return a + u * Vec2.Dot(p - a, u);
        }

        /// <summary>
        /// Where two picked lines meet and the angle between the parts that were picked:
        /// each ray runs from the intersection toward its pick. Null when they're parallel.
        /// </summary>
        public static (Vec2 Vertex, double Start, double Sweep)? AngleBetween(Vec2 a1, Vec2 b1, Vec2 pick1, Vec2 a2, Vec2 b2, Vec2 pick2)
        {
            var v = Construct.LineLine(a1, b1 - a1, a2, b2 - a2);
            if (v == null) return null;
            var r1 = Foot(pick1, a1, b1) - v.Value;
            var r2 = Foot(pick2, a2, b2) - v.Value;
            // A pick right on the vertex: use the far end of that line.
            if (r1.Length < 1e-9) r1 = (Vec2.Distance(a1, v.Value) > Vec2.Distance(b1, v.Value) ? a1 : b1) - v.Value;
            if (r2.Length < 1e-9) r2 = (Vec2.Distance(a2, v.Value) > Vec2.Distance(b2, v.Value) ? a2 : b2) - v.Value;
            double t1 = Math.Atan2(r1.Y, r1.X), t2 = Math.Atan2(r2.Y, r2.X);
            double ccw = Angles.Normalize2Pi(t2 - t1);
            // The interior angle (under 180°), running counter-clockwise from Start.
            return ccw <= Math.PI ? (v.Value, t1, ccw) : (v.Value, t2, Angles.TwoPi - ccw);
        }

        // ---- text on an arc ----------------------------------------------------------------------------

        /// <summary>
        /// Characters of <paramref name="text"/> laid along an arc, centred on the angle
        /// <paramref name="midAngle"/> (radians from the centre), reading left to right and upright:
        /// over the top of the curve they run clockwise, under it counter-clockwise.
        /// <paramref name="radius"/> is the baseline radius. Returns each character's centre and rotation.
        /// </summary>
        public static List<(string Ch, Vec2 At, double Rotation)> TextOnArc(string text, Vec2 center, double radius, double midAngle, double height)
        {
            var list = new List<(string, Vec2, double)>();
            if (text.Length == 0 || radius <= 0) return list;
            // Advance per character: roughly a Helvetica average at this cap height, spaces narrower.
            double Adv(char c) => height * (c == ' ' ? 0.45 : c == 'I' || c == 'i' || c == 'l' || c == '.' || c == '\'' ? 0.4 : char.IsUpper(c) || c == 'm' || c == 'w' ? 0.9 : 0.72);
            double total = text.Sum(Adv);
            bool over = Math.Sin(midAngle) >= 0;
            double dir = over ? -1 : 1; // clockwise over the top
            double a = midAngle - dir * (total / 2) / radius;
            foreach (char c in text)
            {
                double w = Adv(c);
                double ac = a + dir * (w / 2) / radius;
                var p = center + new Vec2(Math.Cos(ac), Math.Sin(ac)) * radius;
                double rot = over ? ac - Math.PI / 2 : ac + Math.PI / 2;
                if (c != ' ') list.Add((c.ToString(), p, rot));
                a += dir * w / radius;
            }
            return list;
        }
    }
}
