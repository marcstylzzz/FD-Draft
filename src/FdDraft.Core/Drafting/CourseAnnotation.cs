using System;
using System.Collections.Generic;
using System.Globalization;
using FdDraft.Core.Geometry;
using FdDraft.Core.Standards;

namespace FdDraft.Core.Drafting
{
    /// <summary>The eight course-labelling styles of MSCAD's annotate toolbar.</summary>
    public enum CourseLabelStyle
    {
        /// <summary>Auto split bearing: the bearing centred on the line, the line broken around it.</summary>
        BearingOnLine,
        /// <summary>Auto bearing off line: the bearing beside the line, on the picked side.</summary>
        BearingOffLine,
        /// <summary>Auto split bearing: the bearing split across the line - direction and degrees
        /// on one side, minutes, seconds and quadrant letter on the other (the picked side gets the degrees).</summary>
        SplitBearing,
        /// <summary>Auto distance - bearing: "distance  bearing" as one line of text, on the picked side.</summary>
        DistanceDashBearing,
        /// <summary>Auto distance: the distance centred on the line, the line broken around it.</summary>
        DistanceOnLine,
        /// <summary>Auto distance off line: the distance beside the line, on the picked side.</summary>
        DistanceOffLine,
        /// <summary>Auto bearing/distance: bearing on one side, distance on the other.</summary>
        BearingDistance,
        /// <summary>Auto bearing-distance: "bearing  distance" as one line of text, on the picked side.</summary>
        BearingDashDistance,
        /// <summary>Auto bearing/distance // line: bearing over distance, both on the picked side.</summary>
        BearingOverDistance,
        /// <summary>Auto distance/bearing // line: distance over bearing, both on the picked side.</summary>
        DistanceOverBearing,
    }

    /// <summary>What a course label comes to: its texts, and for the "on line" styles the part of
    /// the line (0..1 along it) to break out so the text sits in a gap.</summary>
    public sealed class CourseLabelResult
    {
        public List<DraftText> Texts { get; } = new List<DraftText>();
        public double? GapFrom { get; set; }
        public double? GapTo { get; set; }
    }

    /// <summary>
    /// Lays out a straight course's labels in any of the MSCAD annotate styles, by the firm's
    /// standards (heights, layers, styles, gap, bearing format and rotation) - the same rules
    /// Draft's own labels follow. Text always reads left to right; "above" means the reading
    /// direction's up.
    /// </summary>
    public static class CourseAnnotation
    {
        /// <param name="a">Course start.</param>
        /// <param name="b">Course end.</param>
        /// <param name="pick">Where the course was picked - which side the "off line" styles go.</param>
        /// <param name="modelPerMm">Drawing units per paper mm at the sheet's scale.</param>
        public static CourseLabelResult Layout(Vec2 a, Vec2 b, Vec2 pick, CourseLabelStyle style, FirmStandards std, double modelPerMm, double gridToGround = 1.0)
        {
            var result = new CourseLabelResult();
            double len = Vec2.Distance(a, b);
            if (len < 1e-9) return result;
            double rotation = std.BearingRotationDeg * Math.PI / 180.0;
            double r = Angles.ReadableRotation(a, b);
            double az = Angles.Azimuth(a, b);
            if (std.BearingDirection == "reading")
            {
                var readDir = new Vec2(Math.Cos(r), Math.Sin(r));
                if (Vec2.Dot(readDir, b - a) < 0) az += Math.PI;
            }
            string bearing = Angles.FormatBearing(az + rotation, std.BearingSecondsDecimals);
            string distance = (len * gridToGround).ToString("F" + Math.Max(0, std.DistanceDecimals), CultureInfo.InvariantCulture);
            var mid = (a + b) * 0.5;
            var up = new Vec2(-Math.Sin(r), Math.Cos(r));
            bool above = Vec2.Dot(pick - mid, up) >= 0;
            double bh = std.BearingTextMm * modelPerMm, dh = std.DistanceTextMm * modelPerMm;
            double gapB = std.BearingTextMm * std.LabelGapFactor * modelPerMm, gapD = std.DistanceTextMm * std.LabelGapFactor * modelPerMm;

            DraftText Bearing(Vec2 at, VAlign v) => new DraftText
            {
                Layer = std.BearingLayer, Style = std.TextStyle("bearing"), Text = bearing, Position = at,
                HeightMm = std.BearingTextMm, Rotation = r, H = HAlign.Center, V = v, Kind = TextKind.Bearing,
            };
            DraftText Distance(Vec2 at, VAlign v) => new DraftText
            {
                Layer = std.DistanceLayer, Style = std.TextStyle("distance"), Text = distance, Position = at,
                HeightMm = std.DistanceTextMm, Rotation = r, H = HAlign.Center, V = v, Kind = TextKind.Distance,
            };
            // On the picked side: just clear of the line, anchored so the text grows away from it.
            Vec2 Beside(double gap) => mid + up * (above ? gap : -gap);
            VAlign away = above ? VAlign.Bottom : VAlign.Top;

            switch (style)
            {
                case CourseLabelStyle.BearingOnLine:
                case CourseLabelStyle.DistanceOnLine:
                {
                    bool isBearing = style == CourseLabelStyle.BearingOnLine;
                    var t = isBearing ? Bearing(mid, VAlign.Middle) : Distance(mid, VAlign.Middle);
                    result.Texts.Add(t);
                    // The gap: the text's width (Helvetica metrics at its height) plus a text gap each side.
                    double h = isBearing ? bh : dh;
                    double w = TextWidth(t.Text, h) + 2 * (isBearing ? gapB : gapD);
                    double half = Math.Min(0.5, w / 2 / len);
                    result.GapFrom = 0.5 - half; result.GapTo = 0.5 + half;
                    break;
                }
                case CourseLabelStyle.BearingOffLine:
                    result.Texts.Add(Bearing(Beside(gapB), away));
                    break;
                case CourseLabelStyle.DistanceOffLine:
                    result.Texts.Add(Distance(Beside(gapD), away));
                    break;
                case CourseLabelStyle.BearingDistance:
                    result.Texts.Add(Bearing(mid + up * gapB, VAlign.Bottom));
                    result.Texts.Add(Distance(mid - up * gapD, VAlign.Top));
                    break;
                case CourseLabelStyle.BearingDashDistance:
                {
                    var t = Bearing(Beside(gapB), away);
                    t.Text = bearing + "  " + distance;
                    result.Texts.Add(t);
                    break;
                }
                case CourseLabelStyle.DistanceDashBearing:
                {
                    var t = Distance(Beside(gapD), away);
                    t.Text = distance + "  " + bearing;
                    result.Texts.Add(t);
                    break;
                }
                case CourseLabelStyle.SplitBearing:
                {
                    // "N45°12'30\"E" -> "N45°" and "12'30\"E", either side of the line at its middle.
                    int deg = bearing.IndexOf('°');
                    string head = deg >= 0 ? bearing.Substring(0, deg + 1) : bearing;
                    string tail = deg >= 0 ? bearing.Substring(deg + 1) : "";
                    var first = Bearing(mid + up * (above ? gapB : -gapB), away);
                    first.Text = head;
                    result.Texts.Add(first);
                    if (tail.Length > 0)
                    {
                        var second = Bearing(mid - up * (above ? gapB : -gapB), above ? VAlign.Top : VAlign.Bottom);
                        second.Text = tail;
                        result.Texts.Add(second);
                    }
                    break;
                }
                case CourseLabelStyle.BearingOverDistance:
                case CourseLabelStyle.DistanceOverBearing:
                {
                    // Two lines on the picked side, read top to bottom in the order named.
                    bool bearingFirst = style == CourseLabelStyle.BearingOverDistance;
                    double gap = Math.Max(gapB, gapD);
                    double nearH = above ? (bearingFirst ? dh : bh) : (bearingFirst ? bh : dh);
                    double line2 = gap + nearH + gap; // from the line to the far text's near edge
                    if (above)
                    {
                        // Nearest the line is the lower one.
                        result.Texts.Add(bearingFirst ? Bearing(mid + up * line2, VAlign.Bottom) : Distance(mid + up * line2, VAlign.Bottom));
                        result.Texts.Add(bearingFirst ? Distance(mid + up * gap, VAlign.Bottom) : Bearing(mid + up * gap, VAlign.Bottom));
                    }
                    else
                    {
                        result.Texts.Add(bearingFirst ? Bearing(mid - up * gap, VAlign.Top) : Distance(mid - up * gap, VAlign.Top));
                        result.Texts.Add(bearingFirst ? Distance(mid - up * line2, VAlign.Top) : Bearing(mid - up * line2, VAlign.Top));
                    }
                    break;
                }
            }
            return result;
        }

        // Helvetica advance widths for the characters a bearing or distance uses (1/1000 em),
        // at a cap height of 0.718 em - the same metrics the PDF plots with.
        private static double TextWidth(string s, double capHeight)
        {
            double em = 0;
            foreach (char c in s)
                em += c >= '0' && c <= '9' ? 556 : c == '.' ? 278 : c == ' ' ? 278 : c == '°' ? 400 : c == '\'' ? 191 : c == '"' ? 355
                    : c == 'N' ? 722 : c == 'S' ? 667 : c == 'E' ? 667 : c == 'W' ? 944 : c == '-' ? 333 : 556;
            return em / 1000 * capHeight / 0.718;
        }
    }
}
