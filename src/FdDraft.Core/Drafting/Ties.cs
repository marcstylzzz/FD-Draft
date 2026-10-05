using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FdDraft.Core.Geometry;
using FdDraft.Core.Standards;

namespace FdDraft.Core.Drafting
{
    /// <summary>One house tie: from a building corner square to a lot line.</summary>
    public readonly struct HouseTie
    {
        public Vec2 From { get; }
        public Vec2 To { get; }
        public double Length => Vec2.Distance(From, To);
        public HouseTie(Vec2 from, Vec2 to) { From = from; To = to; }
    }

    /// <summary>
    /// MSCAD's MS Ties toolbar, as layout: house ties (automatic and manual, with or without
    /// arrows), straight and curvy leaders, a line of blocks along a path, and line/curve tables.
    /// </summary>
    public static class Ties
    {
        /// <summary>
        /// "Draw House ties to lot boundaries": from the building's corners, square to each
        /// straight lot line whose foot lands on the line, keeping for each lot line the
        /// <paramref name="perLine"/> nearest corners whose tie runs clear of the building.
        /// </summary>
        public static List<HouseTie> Automatic(IList<Vec2> building, IEnumerable<Construct.Span> lotLines, int perLine = 2)
        {
            var result = new List<HouseTie>();
            if (building.Count < 3) return result;
            foreach (var s in lotLines)
            {
                if (s.IsArc) continue;
                var d = s.B - s.A;
                double dd = Vec2.Dot(d, d);
                if (dd < 1e-18) continue;
                var cands = new List<HouseTie>();
                foreach (var v in building)
                {
                    double t = Vec2.Dot(v - s.A, d) / dd;
                    if (t < -1e-9 || t > 1 + 1e-9) continue;
                    var foot = s.A + d * t;
                    if (Vec2.Distance(v, foot) < 1e-6) continue;
                    if (!ClearOfBuilding(building, v, foot)) continue;
                    cands.Add(new HouseTie(v, foot));
                }
                result.AddRange(cands.OrderBy(c => c.Length).Take(perLine));
            }
            return result;
        }

        /// <summary>"Manual House Tie": from a picked corner square to the (infinite) line of a picked lot line.</summary>
        public static HouseTie? Manual(Vec2 corner, Construct.Span lotLine)
        {
            if (lotLine.IsArc)
            {
                var dir = corner - lotLine.Center;
                if (dir.Length < 1e-12) return null;
                return new HouseTie(corner, lotLine.Center + dir.Normalized() * lotLine.Radius);
            }
            var d = lotLine.B - lotLine.A;
            double dd = Vec2.Dot(d, d);
            if (dd < 1e-18) return null;
            var foot = lotLine.A + d * (Vec2.Dot(corner - lotLine.A, d) / dd);
            return Vec2.Distance(corner, foot) < 1e-9 ? (HouseTie?)null : new HouseTie(corner, foot);
        }

        /// <summary>A tie leaves its corner outward: it mustn't pass through the building or cross its walls.</summary>
        private static bool ClearOfBuilding(IList<Vec2> b, Vec2 v, Vec2 foot)
        {
            double len = Vec2.Distance(v, foot);
            var probe = v + (foot - v) * (Math.Min(len, 0.05) / len);
            if (Polygon.Contains(b, probe)) return false;
            int n = b.Count;
            for (int i = 0; i < n; i++)
            {
                var a = b[i]; var c = b[(i + 1) % n];
                if (Vec2.Distance(a, v) < 1e-9 || Vec2.Distance(c, v) < 1e-9) continue;
                if (SegmentsCross(v, foot, a, c)) return false;
            }
            return true;
        }

        private static bool SegmentsCross(Vec2 p1, Vec2 p2, Vec2 q1, Vec2 q2)
        {
            double d1 = Vec2.Cross(p2 - p1, q1 - p1), d2 = Vec2.Cross(p2 - p1, q2 - p1);
            double d3 = Vec2.Cross(q2 - q1, p1 - q1), d4 = Vec2.Cross(q2 - q1, p2 - q1);
            return (d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0) && Math.Abs(d1) > 1e-12 && Math.Abs(d2) > 1e-12;
        }

        /// <summary>
        /// A tie as drawn: its line (with an arrowhead at each end when <paramref name="arrows"/>,
        /// sized <paramref name="arrowMm"/> on paper) and its distance, along the tie, on the side
        /// away from the building where it's read left to right.
        /// </summary>
        public static (DraftPolyline Line, DraftText Text) Draw(HouseTie tie, bool arrows, double arrowMm, FirmStandards std, double modelPerMm, double gridToGround, string layer)
        {
            var line = arrows ? ArrowLine(tie.From, tie.To, arrowMm * modelPerMm, true) : Plain(tie.From, tie.To);
            line.Layer = layer;
            double r = Angles.ReadableRotation(tie.From, tie.To);
            var up = new Vec2(-Math.Sin(r), Math.Cos(r));
            double gap = std.DistanceTextMm * std.LabelGapFactor * modelPerMm;
            var text = new DraftText
            {
                Layer = std.DistanceLayer, Style = std.TextStyle("distance"),
                Text = (tie.Length * gridToGround).ToString("F" + Math.Max(0, std.DistanceDecimals), CultureInfo.InvariantCulture),
                Position = (tie.From + tie.To) * 0.5 + up * gap, HeightMm = std.DistanceTextMm, Rotation = r,
                H = HAlign.Center, V = VAlign.Bottom, Kind = TextKind.Distance,
            };
            return (line, text);
        }

        private static DraftPolyline Plain(Vec2 a, Vec2 b)
        {
            var p = new DraftPolyline();
            p.Add(a); p.Add(b);
            return p;
        }

        /// <summary>
        /// A straight line with a tapered arrowhead at <paramref name="a"/> (and at b too when
        /// <paramref name="both"/>); a line too short for its heads gets them shrunk to fit.
        /// </summary>
        public static DraftPolyline ArrowLine(Vec2 a, Vec2 b, double arrowLen, bool both)
        {
            double len = Vec2.Distance(a, b);
            var p = new DraftPolyline();
            if (len < 1e-12) { p.Add(a); p.Add(b); return p; }
            double al = Math.Min(arrowLen, len / (both ? 2.5 : 1.5));
            double w = al / 3;
            var u = (b - a) * (1 / len);
            void V(Vec2 at, double sw, double ew) { p.Add(at); p.StartWidths.Add(sw); p.EndWidths.Add(ew); }
            V(a, 0, w);
            if (both) { V(a + u * al, 0, 0); V(b - u * al, w, 0); }
            else V(a + u * al, 0, 0);
            V(b, 0, 0);
            return p;
        }

        /// <summary>"Draw a straight leader with arrowhead": arrow at the first pick, through the rest.</summary>
        public static DraftPolyline StraightLeader(IList<Vec2> picks, double arrowLen, string layer)
        {
            var p = ArrowLine(picks[0], picks[1], arrowLen, false);
            for (int i = 2; i < picks.Count; i++) { p.Add(picks[i]); p.StartWidths.Add(0); p.EndWidths.Add(0); }
            p.Layer = layer;
            return p;
        }

        /// <summary>
        /// "Draw curvey leader with arrowhead": from the arrow at <paramref name="tip"/> a curve
        /// through <paramref name="via"/> to <paramref name="end"/> - a short straight head along
        /// the curve's start, then one arc. Straight when the three picks are in line.
        /// </summary>
        public static DraftPolyline CurvyLeader(Vec2 tip, Vec2 via, Vec2 end, double arrowLen, string layer)
        {
            var arc = Arc.ThroughThreePoints(tip, via, end);
            if (arc == null) return StraightLeader(new[] { tip, end }, arrowLen, layer);
            // Head: along the arc's tangent at the tip, a little way in.
            double total = arc.Length;
            double al = Math.Min(arrowLen, total / 3);
            double t = al / total;
            var back = arc.PointAt(t);
            var rest = new Arc { Center = arc.Center, Radius = arc.Radius, StartAngle = arc.StartAngle + arc.Sweep * t, Sweep = arc.Sweep * (1 - t), Start = back, End = end };
            var p = new DraftPolyline { Layer = layer };
            p.Add(tip); p.StartWidths.Add(0); p.EndWidths.Add(al / 3);
            p.Add(back, rest.Bulge); p.StartWidths.Add(0); p.EndWidths.Add(0);
            p.Add(end); p.StartWidths.Add(0); p.EndWidths.Add(0);
            // The head is a chord of the arc: its bulge keeps it on the curve.
            p.Bulges[0] = Math.Tan(arc.Sweep * t / 4);
            return p;
        }

        /// <summary>
        /// "Draw Line of Blocks": insertion points every <paramref name="spacing"/> along a path of
        /// spans, starting at its start, each with the path's direction there (radians, CCW from east).
        /// </summary>
        public static List<(Vec2 At, double Rotation)> AlongPath(IList<Construct.Span> path, double spacing)
        {
            var result = new List<(Vec2, double)>();
            if (spacing <= 0 || path.Count == 0) return result;
            double next = 0; // how far into the current span the next block goes
            foreach (var s in path)
            {
                double len = s.IsArc ? Math.Abs(s.Sweep) * s.Radius : Vec2.Distance(s.A, s.B);
                double at = next;
                for (; at <= len + 1e-9; at += spacing)
                {
                    if (!s.IsArc)
                    {
                        var u = (s.B - s.A) * (1 / Math.Max(len, 1e-12));
                        result.Add((s.A + u * at, Math.Atan2(u.Y, u.X)));
                    }
                    else
                    {
                        double a0 = Math.Atan2(s.A.Y - s.Center.Y, s.A.X - s.Center.X) + Math.Sign(s.Sweep) * at / s.Radius;
                        var pt = new Vec2(s.Center.X + s.Radius * Math.Cos(a0), s.Center.Y + s.Radius * Math.Sin(a0));
                        result.Add((pt, a0 + Math.Sign(s.Sweep) * Math.PI / 2));
                    }
                }
                next = at - len;
                // A block right on the joint isn't placed twice.
                if (next < 1e-9 && result.Count > 0) next = 0;
                if (Math.Abs(next - spacing) < 1e-9) next = spacing;
            }
            // A closed path ends where it started: no doubled block.
            if (result.Count > 1 && Vec2.Distance(result[0].Item1, result[result.Count - 1].Item1) < 1e-6) result.RemoveAt(result.Count - 1);
            return result;
        }

        /// <summary>A table: column headings and rows of cells, top-left at <paramref name="at"/>.</summary>
        public static (List<DraftText> Texts, List<DraftPolyline> Rules) Table(string title, string[] headings, IList<string[]> rows, Vec2 at, double textMm, double modelPerMm, string layer, string style)
        {
            var texts = new List<DraftText>();
            var rules = new List<DraftPolyline>();
            double h = textMm * modelPerMm;
            double pad = h * 0.6, rowH = h + 2 * pad;
            int cols = headings.Length;
            var widths = new double[cols];
            for (int c = 0; c < cols; c++)
            {
                widths[c] = CourseAnnotation.TextWidth(headings[c], h);
                foreach (var r in rows) if (c < r.Length) widths[c] = Math.Max(widths[c], CourseAnnotation.TextWidth(r[c], h));
                widths[c] += 2 * pad * 1.5;
            }
            double total = widths.Sum();
            int lines = rows.Count + 1 + (title.Length > 0 ? 1 : 0);
            double height = lines * rowH;
            DraftText T(string s, Vec2 p) => new DraftText { Layer = layer, Style = style, Text = s, Position = p, HeightMm = textMm, H = HAlign.Center, V = VAlign.Middle, Kind = TextKind.Other };
            DraftPolyline Rule(Vec2 a, Vec2 b) { var p = new DraftPolyline { Layer = layer }; p.Add(a); p.Add(b); return p; }
            double y = at.Y;
            var frame = new DraftPolyline { Layer = layer, Closed = true };
            frame.Add(at); frame.Add(at + new Vec2(total, 0)); frame.Add(at + new Vec2(total, -height)); frame.Add(at + new Vec2(0, -height));
            rules.Add(frame);
            if (title.Length > 0)
            {
                texts.Add(T(title, new Vec2(at.X + total / 2, y - rowH / 2)));
                y -= rowH;
                rules.Add(Rule(new Vec2(at.X, y), new Vec2(at.X + total, y)));
            }
            double x = at.X;
            for (int c = 0; c < cols; c++) { texts.Add(T(headings[c], new Vec2(x + widths[c] / 2, y - rowH / 2))); x += widths[c]; }
            y -= rowH;
            rules.Add(Rule(new Vec2(at.X, y), new Vec2(at.X + total, y)));
            foreach (var r in rows)
            {
                x = at.X;
                for (int c = 0; c < cols && c < r.Length; c++) { texts.Add(T(r[c], new Vec2(x + widths[c] / 2, y - rowH / 2))); x += widths[c]; }
                y -= rowH;
            }
            // Column rules under the title.
            x = at.X;
            double top = title.Length > 0 ? at.Y - rowH : at.Y;
            for (int c = 0; c < cols - 1; c++) { x += widths[c]; rules.Add(Rule(new Vec2(x, top), new Vec2(x, at.Y - height))); }
            return (texts, rules);
        }

        /// <summary>The line table's columns and one row per course: tag, bearing, distance.</summary>
        public static string[] LineRow(string tag, Vec2 a, Vec2 b, FirmStandards std, double gridToGround) => new[]
        {
            tag,
            Angles.FormatBearing(Angles.Azimuth(a, b) + std.BearingRotationDeg * Math.PI / 180.0, std.BearingSecondsDecimals),
            (Vec2.Distance(a, b) * gridToGround).ToString("F" + Math.Max(0, std.DistanceDecimals), CultureInfo.InvariantCulture),
        };

        public static readonly string[] LineHeadings = { "LINE", "BEARING", "DISTANCE" };
        public static readonly string[] CurveHeadings = { "CURVE", "RADIUS", "ARC", "CHORD", "CHORD BEARING", "DELTA" };

        /// <summary>The curve table's row for an arc: tag, radius, arc, chord, chord bearing, delta.</summary>
        public static string[] CurveRow(string tag, Arc arc, FirmStandards std, double gridToGround)
        {
            var d = SurveyLabels.CurveData(arc, std, gridToGround);
            return new[] { tag, d[0].Substring(2), d[1].Substring(2), d[2].Substring(2), d[3], d[4].Substring(2) };
        }

        /// <summary>The next free number for tags like L1, L2... among <paramref name="existing"/> texts.</summary>
        public static int NextTag(string prefix, IEnumerable<string> existing)
        {
            int max = 0;
            foreach (var s in existing)
                if (s.Length > prefix.Length && s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(s.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n)) max = Math.Max(max, n);
            return max + 1;
        }
    }
}
