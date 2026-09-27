using System;
using System.Globalization;
using FdDraft.Core.Geometry;
using FdDraft.Core.Job;
using FdDraft.Core.Standards;

namespace FdDraft.Core.Drafting
{
    /// <summary>
    /// Adds the scale-dependent text: bearings and distances, arc data, point
    /// numbers and elevations, monument abbreviations and parcel areas.
    /// Re-runnable: calling it again with another scale replaces the old labels.
    /// </summary>
    public static class Annotator
    {
        public static void Annotate(DraftDocument doc, FdJob job, FirmStandards std, ScaleOption scale)
        {
            doc.Entities.RemoveAll(e => e is DraftText);
            doc.AnnotatedModelPerPaper = scale.ModelPerPaper;
            double modelPerMm = ModelPerMm(scale, std);
            double gridToGround = std.GridToGround && job.Settings.ScaleFactor > 0 ? 1.0 / job.Settings.ScaleFactor : 1.0;
            int skippedShort = 0;
            // Where the surveyed points are, so a course label can slide off a point that sits on it.
            var occupied = new System.Collections.Generic.List<Vec2>();
            foreach (var e in doc.Entities) if (e is DraftSymbol sym) occupied.Add(sym.Position);
            // Half the along-course length of a bearing label, and how far either side of the line the labels reach.
            int bChars = 12 + (std.BearingSecondsDecimals > 0 ? std.BearingSecondsDecimals + 1 : 0);
            double halfLen = (bChars * 0.8 * std.BearingTextMm / 2 + 1.0) * modelPerMm;
            double halfHt = (Math.Max(std.BearingTextMm, std.DistanceTextMm) * (1 + std.LabelGapFactor) + 0.5) * modelPerMm;
            double arcHalfLen = (22 * 0.8 * std.ArcTextMm / 2 + 1.0) * modelPerMm;
            double arcHalfHt = (std.ArcTextMm * (1 + std.LabelGapFactor) + 0.5) * modelPerMm;

            foreach (var course in doc.Courses)
            {
                if (!course.Labelled) continue;
                if (course.Length / modelPerMm < std.MinLabelledCourseMm) { skippedShort++; continue; }

                if (course.Arc == null)
                {
                    var a = course.A;
                    var b = course.B;
                    double t = ClearSpot(u => a + (b - a) * u, u => b - a, occupied, halfLen, halfHt);
                    doc.Entities.AddRange(StraightCourseLabels(a, b, t, std, modelPerMm, gridToGround, doc.Layer(std.BearingLayer).Name, doc.Layer(std.DistanceLayer).Name));
                }
                else
                {
                    var arc = course.Arc;
                    double t = ClearSpot(arc.PointAt, u => (arc.PointAt(u) - arc.Center).Left(), occupied, arcHalfLen, arcHalfHt);
                    doc.Entities.AddRange(ArcCourseLabels(arc, t, std, modelPerMm, gridToGround, doc.Layer(std.ArcLayer).Name));
                }
            }
            if (skippedShort > 0)
                doc.Warnings.Add(skippedShort + " labelled course(s) are shorter than " + std.MinLabelledCourseMm.ToString(CultureInfo.InvariantCulture) +
                                 " mm at " + scale.Label + " and were left unlabelled - show them in a table or a detail.");

            // Points: elevation at its angle on the plan (45° up-right by default), number at its
            // own (down-right), monument text left - each reading level on the plan.
            var symbols = doc.Entities.FindAll(e => e is DraftSymbol);
            foreach (DraftSymbol s in symbols)
            {
                double off = Math.Max(s.SizeMm, 1.0) * 0.7 * modelPerMm;
                if (std.PointNumbers)
                {
                    var (at, h, v) = PointLabelPlace(s.Position, PointLabelDistance(off, std.PointNumberTextMm * modelPerMm), std.PointNumberAngleDeg, Angles.ViewTwist);
                    doc.Entities.Add(new DraftText
                    {
                        Layer = s.NumberLayer, Style = std.TextStyle("point_number"), Text = s.PointId.ToString(CultureInfo.InvariantCulture),
                        Position = at, HeightMm = std.PointNumberTextMm, Rotation = -Angles.ViewTwist,
                        H = h, V = v, Kind = TextKind.PointNumber, PointId = s.PointId,
                    });
                }
                if (std.PointElevations && s.ShowElevation)
                {
                    var (at, h, v) = PointLabelPlace(s.Position, PointLabelDistance(off, std.ElevationTextMm * modelPerMm), std.ElevationAngleDeg, Angles.ViewTwist);
                    doc.Entities.Add(new DraftText
                    {
                        Layer = s.ElevationLayer, Style = std.TextStyle("elevation"), Text = s.Elevation.ToString(std.ElevationFormat, CultureInfo.InvariantCulture),
                        Position = at, HeightMm = std.ElevationTextMm, Rotation = -Angles.ViewTwist,
                        H = h, V = v, Kind = TextKind.PointElevation, PointId = s.PointId,
                    });
                }
                if (s.MonumentText.Length > 0)
                    doc.Entities.Add(new DraftText
                    {
                        Layer = doc.Layer(std.MonumentLabelLayer).Name, Style = std.TextStyle("monument"), Text = s.MonumentText,
                        Position = s.Position + new Vec2(-off, off * 0.3), HeightMm = std.MonumentTextMm,
                        H = HAlign.Right, V = VAlign.Bottom, Kind = TextKind.Monument, PointId = s.PointId,
                    });
            }

            if (std.AreaLabels)
            {
                foreach (var parcel in doc.Parcels)
                {
                    // Parcels only: a building footprint is an AREA figure too, but gets no area text.
                    if (!std.IsLabelledCode(parcel.Code) && !std.LabelAreaFigures) continue;
                    doc.Entities.Add(new DraftText
                    {
                        Layer = doc.Layer(std.AreaLayer).Name, Style = std.TextStyle("area"),
                        Text = FormatArea(std.AreaFormat, parcel.Area * gridToGround * gridToGround, job.Settings.Units),
                        Position = Polygon.LabelPoint(parcel.Vertices), HeightMm = std.AreaTextMm,
                        H = HAlign.Center, V = VAlign.Middle, Kind = TextKind.Area,
                    });
                }
            }
        }

        /// <summary>
        /// A straight course's bearing (above, bottom-anchored) and distance (below,
        /// top-anchored) labels, centred at <paramref name="t"/> (0..1) along a→b and turned to
        /// read left-to-right. Shared by the drafting pipeline and the app's LABEL command.
        /// </summary>
        public static DraftText[] StraightCourseLabels(Vec2 a, Vec2 b, double t, FirmStandards std, double modelPerMm, double gridToGround, string bearingLayer, string distanceLayer)
        {
            string dist = "F" + Math.Max(0, std.DistanceDecimals).ToString(CultureInfo.InvariantCulture);
            double rotation = std.BearingRotationDeg * Math.PI / 180.0;
            double r = Angles.ReadableRotation(a, b);
            double az = Angles.Azimuth(a, b);
            if (std.BearingDirection == "reading")
            {
                var readDir = new Vec2(Math.Cos(r), Math.Sin(r));
                if (Vec2.Dot(readDir, b - a) < 0) az += Math.PI;
            }
            var mid = a + (b - a) * t;
            var up = new Vec2(-Math.Sin(r), Math.Cos(r));
            return new[]
            {
                new DraftText
                {
                    Layer = bearingLayer, Style = std.TextStyle("bearing"),
                    Text = Angles.FormatBearing(az + rotation, std.BearingSecondsDecimals),
                    Position = mid + up * (std.BearingTextMm * std.LabelGapFactor * modelPerMm),
                    HeightMm = std.BearingTextMm, Rotation = r, H = HAlign.Center, V = VAlign.Bottom, Kind = TextKind.Bearing,
                },
                new DraftText
                {
                    Layer = distanceLayer, Style = std.TextStyle("distance"),
                    Text = (Vec2.Distance(a, b) * gridToGround).ToString(dist, CultureInfo.InvariantCulture),
                    Position = mid - up * (std.DistanceTextMm * std.LabelGapFactor * modelPerMm),
                    HeightMm = std.DistanceTextMm, Rotation = r, H = HAlign.Center, V = VAlign.Top, Kind = TextKind.Distance,
                },
            };
        }

        /// <summary>
        /// A curve's data labels at <paramref name="t"/> (0..1) along it: radius and arc length
        /// on the convex side, chord and chord bearing inside.
        /// </summary>
        public static DraftText[] ArcCourseLabels(Arc arc, double t, FirmStandards std, double modelPerMm, double gridToGround, string layer)
        {
            string dist = "F" + Math.Max(0, std.DistanceDecimals).ToString(CultureInfo.InvariantCulture);
            double rotation = std.BearingRotationDeg * Math.PI / 180.0;
            var m = arc.PointAt(t);
            var outward = (m - arc.Center).Normalized();
            var tangent = outward.Left();
            double r = Angles.ReadableRotation(m, m + tangent);
            var up = new Vec2(-Math.Sin(r), Math.Cos(r));
            bool upIsOut = Vec2.Dot(up, outward) > 0;
            double gap = std.ArcTextMm * std.LabelGapFactor * modelPerMm;
            double chordAz = Angles.Azimuth(arc.Start, arc.End);
            string outer = "R=" + (arc.Radius * gridToGround).ToString(dist, CultureInfo.InvariantCulture) +
                           "  A=" + (arc.Length * gridToGround).ToString(dist, CultureInfo.InvariantCulture);
            string inner = "C=" + (arc.ChordLength * gridToGround).ToString(dist, CultureInfo.InvariantCulture) +
                           "  " + Angles.FormatBearing(chordAz + rotation, std.BearingSecondsDecimals);
            return new[]
            {
                new DraftText
                {
                    Layer = layer, Style = std.TextStyle("arc"), Text = outer, Position = m + outward * gap, HeightMm = std.ArcTextMm,
                    Rotation = r, H = HAlign.Center, V = upIsOut ? VAlign.Bottom : VAlign.Top, Kind = TextKind.ArcData,
                },
                new DraftText
                {
                    Layer = layer, Style = std.TextStyle("arc"), Text = inner, Position = m - outward * gap, HeightMm = std.ArcTextMm,
                    Rotation = r, H = HAlign.Center, V = upIsOut ? VAlign.Top : VAlign.Bottom, Kind = TextKind.ArcData,
                },
            };
        }

        /// <summary>
        /// The position along a course (0..1) for its label: the midpoint unless a
        /// surveyed point falls inside the label's box there, in which case the first of
        /// a few fallbacks that is clear, or failing that the least crowded one.
        /// </summary>
        private static double ClearSpot(Func<double, Vec2> at, Func<double, Vec2> direction, System.Collections.Generic.List<Vec2> occupied, double halfLen, double halfHt)
        {
            double[] tries = { 0.5, 0.4, 0.6, 0.3, 0.7, 0.2, 0.8 };
            double bestT = 0.5, bestScore = double.MaxValue;
            foreach (double t in tries)
            {
                var p = at(t);
                var u = direction(t).Normalized();
                var n = u.Left();
                int hits = 0;
                foreach (var o in occupied)
                {
                    var d = o - p;
                    if (Math.Abs(Vec2.Dot(d, u)) < halfLen && Math.Abs(Vec2.Dot(d, n)) < halfHt) hits++;
                }
                if (hits == 0) return t;
                double score = hits + Math.Abs(t - 0.5); // fewest collisions, then nearest the middle
                if (score < bestScore) { bestScore = score; bestT = t; }
            }
            return bestT;
        }

        /// <summary>Model units per paper millimetre at a scale.</summary>
        /// <summary>
        /// How far out a point's number or elevation sits: clear of the symbol, plus most of a text
        /// height, so the label's corner is plainly on its diagonal (45° up-right reads as 45°,
        /// not as "just to the right").
        /// </summary>
        public static double PointLabelDistance(double symbolClearance, double textHeight) => symbolClearance + 0.8 * textHeight;

        /// <summary>
        /// Where a label goes round its point: <paramref name="distance"/> out at
        /// <paramref name="angleDeg"/> on the plan (a plan turned by <paramref name="twist"/>), anchored
        /// on the corner nearest the point so the text grows away from it.
        /// </summary>
        public static (Vec2 At, HAlign H, VAlign V) PointLabelPlace(Vec2 point, double distance, double angleDeg, double twist)
        {
            double a = angleDeg * Math.PI / 180;
            double w = a - twist; // the plan direction, in the drawing
            var at = point + new Vec2(Math.Cos(w), Math.Sin(w)) * distance;
            double c = Math.Cos(a), s = Math.Sin(a);
            var h = Math.Abs(c) < 0.2 ? HAlign.Center : c > 0 ? HAlign.Left : HAlign.Right;
            var v = Math.Abs(s) < 0.2 ? VAlign.Middle : s > 0 ? VAlign.Bottom : VAlign.Top;
            return (at, h, v);
        }

        public static double ModelPerMm(ScaleOption scale, FirmStandards std) => scale.ModelPerPaper * std.PaperUnitsPerMm;

        /// <summary>Fills {m2} {ha} {ft2} {ac} from an area in the job's own square units.</summary>
        public static string FormatArea(string format, double area, JobUnits units)
        {
            double ftPerM = units == JobUnits.UsSurveyFeet ? 3937.0 / 1200.0 : units == JobUnits.InternationalFeet ? 1 / 0.3048 : 1.0;
            double m2 = units == JobUnits.Meters ? area : area / (ftPerM * ftPerM);
            double ft2 = units == JobUnits.Meters ? area / (0.3048 * 0.3048) : area;
            return format
                .Replace("{m2}", m2.ToString("F1", CultureInfo.InvariantCulture))
                .Replace("{ha}", (m2 / 10000.0).ToString("F4", CultureInfo.InvariantCulture))
                .Replace("{ft2}", ft2.ToString("F0", CultureInfo.InvariantCulture))
                .Replace("{ac}", (ft2 / 43560.0).ToString("F3", CultureInfo.InvariantCulture));
        }
    }
}
