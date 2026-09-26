using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;
using FdDraft.Core.Standards;
using CoreArc = FdDraft.Core.Geometry.Arc;

namespace FdDraft.Cad.Editing
{
    /// <summary>
    /// Entity builders behind the FD Labels / FD Ties / FD Text Edit tools: arrowheads, arrow lines,
    /// house ties, line/curve tables, text along an arc, curve data blocks. Plain DWG entities
    /// (SOLID arrowheads, LINE, ARC, TEXT) so they read back the same in any CAD program.
    /// Heights and sizes are in drawing units; the caller converts from paper mm.
    /// </summary>
    public static class SurveyDrafting
    {
        private static XYZ W(Vec2 v) => new XYZ(v.X, v.Y, 0);

        /// <summary>A filled arrowhead with its tip at <paramref name="tip"/>, pointing along
        /// <paramref name="direction"/>; <paramref name="size"/> long, a third as wide.</summary>
        public static Solid Arrowhead(Vec2 tip, Vec2 direction, double size, Layer layer)
        {
            var u = direction.Normalized();
            var back = tip - u * size;
            var side = u.Left() * (size / 6);
            // SOLID corners zig-zag (1,2,3,4 = 1,2,4,3 around); a triangle repeats its last corner.
            return new Solid
            {
                FirstCorner = W(tip), SecondCorner = W(back + side), ThirdCorner = W(back - side), FourthCorner = W(back - side),
                Layer = layer,
            };
        }

        /// <summary>A line from a to b with an arrowhead at b (and at a too when <paramref name="both"/>).</summary>
        public static List<Entity> ArrowLine(Vec2 a, Vec2 b, double size, bool both, Layer layer)
        {
            var list = new List<Entity> { new Line(W(a), W(b)) { Layer = layer } };
            if (Vec2.Distance(a, b) < 1e-9) return list;
            list.Add(Arrowhead(b, b - a, size, layer));
            if (both) list.Add(Arrowhead(a, a - b, size, layer));
            return list;
        }

        /// <summary>
        /// A square tie from a building corner to the lot line: the tie line, arrowheads at both
        /// ends if wanted, and its length in text beside the middle, reading left to right.
        /// </summary>
        public static List<Entity> HouseTie(Vec2 corner, Vec2 foot, bool arrows, double arrowSize, double textHeightMm, double modelPerMm, int decimals,
            Layer tieLayer, string textLayer, string textStyle, CadDocument doc, Func<string, Layer> layer)
        {
            var list = new List<Entity>();
            double len = Vec2.Distance(corner, foot);
            if (len < 1e-9) return list;
            if (arrows) list.AddRange(ArrowLine(corner, foot, Math.Min(arrowSize, len / 3), true, tieLayer));
            else list.Add(new Line(W(corner), W(foot)) { Layer = tieLayer });
            double r = Angles.ReadableRotation(corner, foot);
            var up = new Vec2(-Math.Sin(r), Math.Cos(r));
            var mid = (corner + foot) * 0.5;
            var t = new DraftText
            {
                Text = len.ToString("F" + Math.Max(0, decimals), CultureInfo.InvariantCulture), Position = mid + up * (textHeightMm * 0.4 * modelPerMm),
                HeightMm = textHeightMm, Rotation = r, H = HAlign.Center, V = VAlign.Bottom, Layer = textLayer, Style = textStyle, Kind = TextKind.Distance,
            };
            list.Add(CourseLabelling.ToEntity(t, doc, modelPerMm, layer));
            return list;
        }

        /// <summary>
        /// The building corner to tie to a lot line a-b: the one nearest the line whose square foot
        /// falls on the line between a and b (else simply the nearest). Returns the corner and foot.
        /// </summary>
        public static (Vec2 Corner, Vec2 Foot)? AutoTie(IList<Vec2> building, Vec2 a, Vec2 b)
        {
            if (building.Count == 0 || Vec2.Distance(a, b) < 1e-9) return null;
            (Vec2, Vec2)? best = null, bestAny = null;
            double bd = double.MaxValue, bdAny = double.MaxValue;
            var u = (b - a).Normalized();
            double len = Vec2.Distance(a, b);
            foreach (var c in building)
            {
                var foot = SurveyCalcs.Foot(c, a, b);
                double d = Vec2.Distance(c, foot);
                double t = Vec2.Dot(foot - a, u);
                if (d < bdAny) { bdAny = d; bestAny = (c, foot); }
                if (t >= -1e-9 && t <= len + 1e-9 && d < bd) { bd = d; best = (c, foot); }
            }
            return best ?? bestAny;
        }

        /// <summary>The vertices of a closed or open polyline, or a single line's ends.</summary>
        public static List<Vec2> Vertices(Entity e) => e switch
        {
            LwPolyline lp => lp.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList(),
            Polyline2D p2 => p2.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList(),
            Line l => new List<Vec2> { new Vec2(l.StartPoint.X, l.StartPoint.Y), new Vec2(l.EndPoint.X, l.EndPoint.Y) },
            _ => new List<Vec2>(),
        };

        /// <summary>A core arc as a DWG ARC (counter-clockwise from start to end, as DWG arcs run).</summary>
        public static ACadSharp.Entities.Arc ToEntity(CoreArc a, Layer layer)
        {
            double s = a.StartAngle, e = a.StartAngle + a.Sweep;
            if (a.Sweep < 0) (s, e) = (e, s);
            return new ACadSharp.Entities.Arc
            {
                Center = new XYZ(a.Center.X, a.Center.Y, 0), Radius = a.Radius,
                StartAngle = Angles.Normalize2Pi(s), EndAngle = Angles.Normalize2Pi(e), Layer = layer,
            };
        }

        /// <summary>A DWG ARC as a core arc (counter-clockwise).</summary>
        public static CoreArc ToCore(ACadSharp.Entities.Arc a)
        {
            double sweep = a.EndAngle - a.StartAngle;
            while (sweep <= 0) sweep += Angles.TwoPi;
            var c = new Vec2(a.Center.X, a.Center.Y);
            return new CoreArc
            {
                Center = c, Radius = a.Radius, StartAngle = a.StartAngle, Sweep = sweep,
                Start = c + new Vec2(Math.Cos(a.StartAngle), Math.Sin(a.StartAngle)) * a.Radius,
                End = c + new Vec2(Math.Cos(a.StartAngle + sweep), Math.Sin(a.StartAngle + sweep)) * a.Radius,
            };
        }

        /// <summary>A curve's data the way a plan's curve label or table states it.</summary>
        public static List<string> CurveData(CoreArc arc, FirmStandards std, double gridToGround)
        {
            string f = "F" + Math.Max(0, std.DistanceDecimals).ToString(CultureInfo.InvariantCulture);
            double rotation = std.BearingRotationDeg * Math.PI / 180.0;
            return new List<string>
            {
                "R=" + (arc.Radius * gridToGround).ToString(f, CultureInfo.InvariantCulture),
                "A=" + (arc.Length * gridToGround).ToString(f, CultureInfo.InvariantCulture),
                "Δ=" + SurveyCalcs.Dms(Math.Abs(arc.Sweep)),
                "C=" + (arc.ChordLength * gridToGround).ToString(f, CultureInfo.InvariantCulture),
                "CB=" + Angles.FormatBearing(Angles.Azimuth(arc.Start, arc.End) + rotation, std.BearingSecondsDecimals),
            };
        }

        /// <summary>
        /// A plain table: a header row then the rows, top-left corner at <paramref name="topLeft"/>,
        /// columns sized to their longest entry. Lines and TEXT only.
        /// </summary>
        public static List<Entity> Table(string title, IList<string> header, IList<IList<string>> rows, Vec2 topLeft, double textHeightMm, double modelPerMm,
            string lineLayer, string textLayer, string textStyle, CadDocument doc, Func<string, Layer> layer)
        {
            var list = new List<Entity>();
            double h = textHeightMm * modelPerMm, pad = h * 0.6, rowH = h + 2 * pad;
            int cols = header.Count;
            var widths = new double[cols];
            for (int c = 0; c < cols; c++)
            {
                int chars = Math.Max(header[c].Length, rows.Count == 0 ? 0 : rows.Max(r => c < r.Count ? r[c].Length : 0));
                widths[c] = chars * h * 0.72 + 2 * pad;
            }
            double width = widths.Sum();
            var lines = layer(lineLayer);
            double y = topLeft.Y;
            DraftText Cell(string s, double cx, double cy, double hmm) => new DraftText
            {
                Text = s, Position = new Vec2(cx, cy), HeightMm = hmm, H = HAlign.Center, V = VAlign.Middle, Layer = textLayer, Style = textStyle,
            };
            if (title.Length > 0)
            {
                list.Add(CourseLabelling.ToEntity(Cell(title, topLeft.X + width / 2, y + rowH / 2, textHeightMm * 1.2), doc, modelPerMm, layer));
            }
            int total = rows.Count + 1;
            for (int r = 0; r <= total; r++)
                list.Add(new Line(new XYZ(topLeft.X, y - r * rowH, 0), new XYZ(topLeft.X + width, y - r * rowH, 0)) { Layer = lines });
            double x = topLeft.X;
            for (int c = 0; c <= cols; c++)
            {
                list.Add(new Line(new XYZ(x, y, 0), new XYZ(x, y - total * rowH, 0)) { Layer = lines });
                if (c < cols)
                {
                    double cx = x + widths[c] / 2;
                    list.Add(CourseLabelling.ToEntity(Cell(header[c], cx, y - rowH / 2, textHeightMm), doc, modelPerMm, layer));
                    for (int r = 0; r < rows.Count; r++)
                        if (c < rows[r].Count && rows[r][c].Length > 0)
                            list.Add(CourseLabelling.ToEntity(Cell(rows[r][c], cx, y - (r + 1.5) * rowH, textHeightMm), doc, modelPerMm, layer));
                    x += widths[c];
                }
            }
            return list;
        }

        /// <summary>A text tag (L1, C1, T1...) centred on a course, just clear of it.</summary>
        public static TextEntity Tag(string tag, Vec2 a, Vec2 b, double textHeightMm, double modelPerMm, string textLayer, string textStyle, CadDocument doc, Func<string, Layer> layer)
        {
            double r = Angles.ReadableRotation(a, b);
            var up = new Vec2(-Math.Sin(r), Math.Cos(r));
            var t = new DraftText
            {
                Text = tag, Position = (a + b) * 0.5 + up * (textHeightMm * 0.4 * modelPerMm), HeightMm = textHeightMm, Rotation = r,
                H = HAlign.Center, V = VAlign.Bottom, Layer = textLayer, Style = textStyle,
            };
            return CourseLabelling.ToEntity(t, doc, modelPerMm, layer);
        }

        /// <summary>Text laid along an arc, one TEXT per character (see <see cref="SurveyCalcs.TextOnArc"/>).</summary>
        public static List<Entity> TextOnArc(string text, Vec2 center, double radius, double midAngle, double textHeightMm, double modelPerMm,
            string textLayer, string textStyle, CadDocument doc, Func<string, Layer> layer)
        {
            double h = textHeightMm * modelPerMm;
            return SurveyCalcs.TextOnArc(text, center, radius, midAngle, h).Select(c => (Entity)CourseLabelling.ToEntity(new DraftText
            {
                Text = c.Ch, Position = c.At, HeightMm = textHeightMm, Rotation = c.Rotation, H = HAlign.Center, V = VAlign.Bottom, Layer = textLayer, Style = textStyle,
            }, doc, modelPerMm, layer)).ToList();
        }

        /// <summary>A TEXT/MTEXT's height, rotation, insertion and value in one place.</summary>
        public static double HeightOf(Entity e) => e is TextEntity t ? t.Height : e is MText m ? m.Height : 0;

        /// <summary>Setting a TEXT's or MTEXT's height, undoably, for each entity.</summary>
        public static IEditCommand? SetHeights(IEnumerable<Entity> texts, Func<double, double> newHeight, string description)
        {
            var cmds = new List<IEditCommand>();
            foreach (var e in texts)
            {
                switch (e)
                {
                    case TextEntity t: { double h = newHeight(t.Height); if (h > 0 && Math.Abs(h - t.Height) > 1e-12) cmds.Add(new SetPropertyCommand<double>(t.Height, h, v => t.Height = v, description)); break; }
                    case MText m: { double h = newHeight(m.Height); if (h > 0 && Math.Abs(h - m.Height) > 1e-12) cmds.Add(new SetPropertyCommand<double>(m.Height, h, v => m.Height = v, description)); break; }
                }
            }
            return cmds.Count == 0 ? null : cmds.Count == 1 ? cmds[0] : new CompositeCommand(cmds, description);
        }

        /// <summary>The rotation of a TEXT or MTEXT (radians).</summary>
        public static double RotationOf(Entity e) => e is TextEntity t ? t.Rotation : e is MText m ? Math.Atan2(m.AlignmentPoint.Y, m.AlignmentPoint.X) : 0;

        /// <summary>Sets a TEXT's or MTEXT's rotation undoably (MTEXT keeps it as a direction vector).</summary>
        public static IEditCommand? SetRotation(Entity e, double rotation, string description)
        {
            switch (e)
            {
                case TextEntity t: return new SetPropertyCommand<double>(t.Rotation, rotation, v => t.Rotation = v, description);
                case MText m: return new SetPropertyCommand<XYZ>(m.AlignmentPoint, new XYZ(Math.Cos(rotation), Math.Sin(rotation), 0), v => m.AlignmentPoint = v, description);
            }
            return null;
        }

        /// <summary>Where a TEXT/MTEXT sits (its alignment point for aligned TEXT, else the insertion point).</summary>
        public static Vec2 AnchorOf(Entity e)
        {
            switch (e)
            {
                case TextEntity t:
                    bool aligned = t.HorizontalAlignment != TextHorizontalAlignment.Left || t.VerticalAlignment != TextVerticalAlignmentType.Baseline;
                    var p = aligned ? t.AlignmentPoint : t.InsertPoint;
                    return new Vec2(p.X, p.Y);
                case MText m: return new Vec2(m.InsertPoint.X, m.InsertPoint.Y);
            }
            return default;
        }

        /// <summary>
        /// Turns a text end for end about its middle (ROTEXT): rotation + 180°, and the text moved so
        /// it covers the same place - by swapping its alignment to the opposite side.
        /// </summary>
        public static IEditCommand? Rotate180(Entity e, string description)
        {
            if (!(e is TextEntity) && !(e is MText)) return null;
            // Rotating about the anchor and then moving the anchor keeps it simplest: rotate the whole
            // entity 180° about the middle of its (approximate) extent.
            var center = TextMiddle(e);
            return TransformEntitiesCommand.Rotate(new[] { e }, new XYZ(center.X, center.Y, 0), Math.PI, description);
        }

        /// <summary>The approximate middle of a text's box (Helvetica-ish widths), for turning it about.</summary>
        public static Vec2 TextMiddle(Entity e)
        {
            double h = HeightOf(e), r = RotationOf(e);
            string value = e is TextEntity t ? t.Value : e is MText m ? m.Value : "";
            double w = value.Length * h * 0.72;
            var u = new Vec2(Math.Cos(r), Math.Sin(r));
            var up = u.Left();
            var a = AnchorOf(e);
            if (e is TextEntity te)
            {
                double fx = te.HorizontalAlignment == TextHorizontalAlignment.Left ? 0.5 : te.HorizontalAlignment == TextHorizontalAlignment.Right ? -0.5 : 0;
                double fy = te.VerticalAlignment == TextVerticalAlignmentType.Top ? -0.5 : te.VerticalAlignment == TextVerticalAlignmentType.Middle ? 0 : 0.5;
                return a + u * (w * fx) + up * (h * fy);
            }
            return a + u * (w * 0.5) - up * (h * 0.5);
        }
    }
}
