using System;
using System.Collections.Generic;
using System.Globalization;
using FdDraft.Core.Geometry;
using FdDraft.Core.Standards;

namespace FdDraft.Core.Drafting
{
    /// <summary>
    /// The course-labelling styles of MSCAD's MS Labels 1 toolbar (its help text quoted;
    /// <c>bear_dist_label n</c> is the MSCAD LISP call behind each).
    /// </summary>
    public enum CourseLabelStyle
    {
        /// <summary>"Place Bearing on center of line" (5): the bearing centred on the line, the line broken around it.</summary>
        BearingOnLine,
        /// <summary>"Place a line bearing anywhere on drawing": the bearing beside the line, on the picked side.</summary>
        BearingOffLine,
        /// <summary>"Place Distance on center of a line" (6): the distance centred on the line, the line broken around it.</summary>
        DistanceOnLine,
        /// <summary>Auto distance off line: the distance beside the line, on the picked side.</summary>
        DistanceOffLine,
        /// <summary>"Place bearing opposite distance on line" (4): bearing on one side, distance on the other.</summary>
        BearingDistance,
        /// <summary>"Place bearing before distance on same side of line" (2): "bearing  distance" as one line, picked side.</summary>
        BearingDashDistance,
        /// <summary>"Place bearing above distance on same side of line" (7).</summary>
        BearingOverDistance,
        /// <summary>"Place distance above bearing on same side of line" (3).</summary>
        DistanceOverBearing,
        /// <summary>"Split a bearing into deg - min - sec across line": the degrees on one side of the
        /// line, the minutes and seconds on the other, both centred on it.</summary>
        SplitBearing,
        /// <summary>"Place distance before bearing on same side of line" (1): "distance  bearing" as one line, picked side.</summary>
        DistanceBeforeBearing,
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
                case CourseLabelStyle.DistanceBeforeBearing:
                {
                    var t = Distance(Beside(gapD), away);
                    t.Text = distance + "  " + bearing;
                    result.Texts.Add(t);
                    break;
                }
                case CourseLabelStyle.SplitBearing:
                {
                    // N45°|10'20"E: the degrees read above the line, the rest below.
                    int deg = bearing.IndexOf('°');
                    string top = deg >= 0 ? bearing.Substring(0, deg + 1) : bearing;
                    string bottom = deg >= 0 ? bearing.Substring(deg + 1) : "";
                    var t1 = Bearing(mid + up * gapB, VAlign.Bottom); t1.Text = top;
                    result.Texts.Add(t1);
                    if (bottom.Length > 0) { var t2 = Bearing(mid - up * gapB, VAlign.Top); t2.Text = bottom; result.Texts.Add(t2); }
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

        /// <summary>
        /// Width of <paramref name="s"/> at a text height (cap height) of <paramref name="capHeight"/>,
        /// by Helvetica's advance widths (1/1000 em, cap height 0.718 em) - the metrics the PDF
        /// plots with. Letters FD-Draft rarely draws fall back to an average width.
        /// </summary>
        public static double TextWidth(string s, double capHeight)
        {
            double em = 0;
            foreach (char c in s) em += CharWidth(c);
            return em / 1000 * capHeight / 0.718;
        }

        /// <summary>One character's Helvetica advance width, 1/1000 em.</summary>
        public static double CharWidth(char c)
        {
            if (c >= '0' && c <= '9') return 556;
            switch (c)
            {
                case '.': case ' ': case ',': case 'I': return 278;
                case 'i': case 'j': case 'l': return 222;
                case '°': return 400;
                case '\'': return 191;
                case '"': return 355;
                case '-': return 333;
                case '=': return 584;
                case 'f': case 't': return 278;
                case 'r': return 333;
                case 'm': return 833;
                case 'w': return 722;
                case 'W': return 944;
                case 'M': return 833;
                case 'N': case 'H': case 'D': case 'U': case 'C': case 'R': return 722;
                case 'O': case 'Q': case 'G': return 778;
                case 'S': case 'E': case 'B': case 'A': case 'K': case 'P': case 'V': case 'X': case 'Y': return 667;
                case 'F': case 'T': case 'Z': return 611;
                case 'L': case 'J': return 556;
            }
            return c >= 'A' && c <= 'Z' ? 667 : 556;
        }
    }
}
