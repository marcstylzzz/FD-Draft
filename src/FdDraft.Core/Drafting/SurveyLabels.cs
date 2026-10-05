using System;
using System.Collections.Generic;
using System.Globalization;
using FdDraft.Core.Geometry;
using FdDraft.Core.Standards;

namespace FdDraft.Core.Drafting
{
    /// <summary>
    /// The rest of MSCAD's MS Labels 1 toolbar: the angle between two lines, arrows along a line,
    /// curve data on or off an arc, and text that follows an arc. Pure layout, by the firm's
    /// standards; the CAD layer turns the results into DWG entities.
    /// </summary>
    public static class SurveyLabels
    {
        /// <summary>Degrees, minutes and seconds: 89°59'60" never prints (the carry is in whole seconds).</summary>
        public static string Dms(double radians)
        {
            long total = (long)Math.Round(Math.Abs(radians) * 180 / Math.PI * 3600, MidpointRounding.AwayFromZero);
            return (total / 3600).ToString(CultureInfo.InvariantCulture) + "°" + (total % 3600 / 60).ToString("00", CultureInfo.InvariantCulture) + "'" +
                   (total % 60).ToString("00", CultureInfo.InvariantCulture) + "\"";
        }

        /// <summary>
        /// "Add Angle between two lines": the angle at <paramref name="vertex"/> between the lines
        /// running along <paramref name="u1"/> and <paramref name="u2"/> (either way along each), in
        /// the sector <paramref name="pick"/> is in. The text sits at the pick's distance on the
        /// sector's bisector, an arc marks the angle inside it. Null when the pick is on the vertex.
        /// </summary>
        public static (DraftText Text, DraftPolyline Arc, double Angle)? Angle(Vec2 vertex, Vec2 u1, Vec2 u2, Vec2 pick, FirmStandards std, double modelPerMm, string layer)
        {
            var p = pick - vertex;
            double rp = p.Length;
            if (rp < 1e-9 || u1.Length < 1e-12 || u2.Length < 1e-12) return null;
            u1 = u1.Normalized(); u2 = u2.Normalized();
            // Of the four sectors the two lines make, the one the pick is in.
            Vec2 d1 = u1, d2 = u2;
            foreach (var a in new[] { u1, u1 * -1 })
                foreach (var b in new[] { u2, u2 * -1 })
                {
                    double ab = Vec2.Cross(a, b);
                    if (Math.Abs(ab) < 1e-12) continue;
                    // p is between a and b when it's on b's side of a and on a's side of b.
                    if (Math.Sign(Vec2.Cross(a, p)) == Math.Sign(ab) && Math.Sign(Vec2.Cross(p, b)) == Math.Sign(ab)) { d1 = a; d2 = b; }
                }
            double angle = Math.Acos(Math.Max(-1, Math.Min(1, Vec2.Dot(d1, d2))));
            var bis = (d1 + d2);
            bis = bis.Length < 1e-12 ? d1.Left() : bis.Normalized();
            double h = std.BearingTextMm * modelPerMm;
            var at = vertex + bis * rp;
            double rot = Angles.ReadableRotation(at, at + bis.Left());
            var text = new DraftText
            {
                Layer = layer, Style = std.TextStyle("bearing"), Text = Dms(angle), Position = at, HeightMm = std.BearingTextMm,
                Rotation = rot, H = HAlign.Center, V = VAlign.Middle, Kind = TextKind.Other,
            };
            // The marking arc runs inside the text, counter-clockwise from the first ray.
            double ra = Math.Max(rp - 1.2 * h, rp * 0.5);
            var from = Vec2.Cross(d1, d2) > 0 ? d1 : d2;
            var to = Vec2.Cross(d1, d2) > 0 ? d2 : d1;
            var arc = new DraftPolyline { Layer = layer };
            arc.Add(vertex + from * ra, Math.Tan(angle / 4));
            arc.Add(vertex + to * ra);
            return (text, arc, angle);
        }

        /// <summary>
        /// "Add arrows to line offset equal to labels": a line beside the course a→b on the
        /// picked side, clear of a one-row label, with an arrowhead at each end - the extent the
        /// label applies to. Null when the course is too short for two arrowheads.
        /// </summary>
        public static DraftPolyline? ArrowsAlong(Vec2 a, Vec2 b, Vec2 pick, FirmStandards std, double modelPerMm, string layer)
        {
            double len = Vec2.Distance(a, b);
            double h = Math.Max(std.DistanceTextMm, std.BearingTextMm) * modelPerMm;
            double gap = Math.Max(std.DistanceTextMm, std.BearingTextMm) * std.LabelGapFactor * modelPerMm;
            double arrowLen = 1.2 * h, arrowW = 0.45 * h;
            if (len < 2.5 * arrowLen) return null;
            var u = (b - a) * (1 / len);
            var n = u.Left();
            double side = Vec2.Cross(b - a, pick - a) >= 0 ? 1 : -1;
            var off = n * (side * (gap + h + gap));
            var p0 = a + off; var p3 = b + off;
            var p1 = p0 + u * arrowLen; var p2 = p3 - u * arrowLen;
            var pl = new DraftPolyline { Layer = layer };
            // Tapered spans make the heads: zero width at the tip, full width at the back.
            pl.Add(p0); pl.StartWidths.Add(0); pl.EndWidths.Add(arrowW);
            pl.Add(p1); pl.StartWidths.Add(0); pl.EndWidths.Add(0);
            pl.Add(p2); pl.StartWidths.Add(arrowW); pl.EndWidths.Add(0);
            pl.Add(p3); pl.StartWidths.Add(0); pl.EndWidths.Add(0);
            return pl;
        }

        /// <summary>The curve data texts (R, A, C, chord bearing, delta) as separate strings.</summary>
        public static string[] CurveData(Arc arc, FirmStandards std, double gridToGround)
        {
            string f = "F" + Math.Max(0, std.DistanceDecimals).ToString(CultureInfo.InvariantCulture);
            double rotation = std.BearingRotationDeg * Math.PI / 180.0;
            return new[]
            {
                "R=" + (arc.Radius * gridToGround).ToString(f, CultureInfo.InvariantCulture),
                "A=" + (arc.Length * gridToGround).ToString(f, CultureInfo.InvariantCulture),
                "C=" + (arc.ChordLength * gridToGround).ToString(f, CultureInfo.InvariantCulture),
                Angles.FormatBearing(Angles.Azimuth(arc.Start, arc.End) + rotation, std.BearingSecondsDecimals),
                "Δ=" + Dms(arc.Sweep),
            };
        }

        /// <summary>
        /// "Curve information placed anywhere in drawing": the curve data as a block of lines,
        /// upper-left corner at <paramref name="at"/>, reading horizontally.
        /// </summary>
        public static List<DraftText> CurveDataBlock(Arc arc, Vec2 at, FirmStandards std, double modelPerMm, double gridToGround, string layer)
        {
            var result = new List<DraftText>();
            double h = std.ArcTextMm * modelPerMm;
            double step = h * (1 + 2 * Math.Max(0.25, std.LabelGapFactor));
            int i = 0;
            foreach (var line in CurveData(arc, std, gridToGround))
                result.Add(new DraftText
                {
                    Layer = layer, Style = std.TextStyle("arc"), Text = line, Position = at - new Vec2(0, step * i++), HeightMm = std.ArcTextMm,
                    H = HAlign.Left, V = VAlign.Top, Kind = TextKind.ArcData,
                });
            return result;
        }

        /// <summary>Where along an arc (0..1) a point projects, clamped to the arc.</summary>
        public static double ParamOn(Arc arc, Vec2 p)
        {
            double a = Math.Atan2(p.Y - arc.Center.Y, p.X - arc.Center.X);
            double d = arc.Sweep >= 0 ? Angles.Normalize2Pi(a - arc.StartAngle) : Angles.Normalize2Pi(arc.StartAngle - a);
            double sweep = Math.Abs(arc.Sweep);
            if (d > sweep) d = d - sweep < Angles.TwoPi - d ? sweep : 0; // off the arc: the nearer end
            return sweep < 1e-12 ? 0.5 : d / sweep;
        }

        /// <summary>
        /// "Manually place any text to follow the curve": one text per character, each turned to
        /// the arc's tangent, centred on the arc's point nearest <paramref name="pick"/>. On the
        /// arc's upper half the letters stand on the outside of the curve; on the lower half they
        /// hang inside it - so the text always reads left to right.
        /// </summary>
        public static List<DraftText> TextOnArc(Vec2 center, double radius, string text, Vec2 pick, double heightMm, double modelPerMm, string layer, string style)
        {
            var result = new List<DraftText>();
            if (string.IsNullOrEmpty(text) || radius < 1e-9) return result;
            double h = heightMm * modelPerMm;
            double mid = Math.Atan2(pick.Y - center.Y, pick.X - center.X);
            bool top = Math.Sin(mid) >= 0;
            double total = CourseAnnotation.TextWidth(text, h) / radius;
            // Reading left to right is clockwise over the top, counter-clockwise underneath.
            double dir = top ? -1 : 1;
            double a = mid - dir * total / 2;
            foreach (char c in text)
            {
                double w = CharWidth(c, h) / radius;
                double ac = a + dir * w / 2;
                var pos = center + new Vec2(Math.Cos(ac), Math.Sin(ac)) * radius;
                result.Add(new DraftText
                {
                    Layer = layer, Style = style, Text = c.ToString(), Position = pos, HeightMm = heightMm,
                    Rotation = top ? ac - Math.PI / 2 : ac + Math.PI / 2, H = HAlign.Center, V = top ? VAlign.Bottom : VAlign.Top, Kind = TextKind.Other,
                });
                a += dir * w;
            }
            result.RemoveAll(t => t.Text == " ");
            return result;
        }

        private static double CharWidth(char c, double capHeight) => CourseAnnotation.CharWidth(c) / 1000 * capHeight / 0.718;
    }
}
