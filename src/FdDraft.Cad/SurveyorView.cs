using System;
using System.Collections.Generic;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using CSMath;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;

namespace FdDraft.Cad.Editing
{
    /// <summary>
    /// MSCAD's Surveyor View: the plan turned so north isn't up - on the sheets as well as on
    /// screen. Each sheet's plan viewport gets the twist (the same model point stays in the
    /// middle of it) and the north arrow on that sheet turns with it, so the plot shows the
    /// turned plan with a true north arrow. Coordinates, bearings and labels are untouched.
    /// World View is the same with no twist.
    /// </summary>
    public static class SurveyorView
    {
        /// <summary>The twist on the drawing now: the first sheet's plan viewport's, else 0.</summary>
        public static double CurrentTwist(CadDocument doc)
        {
            foreach (var l in doc.Layouts.Where(l => l.IsPaperSpace).OrderBy(l => l.TabOrder))
                if (SheetScale.PlanViewport(l) is Viewport vp) return vp.TwistAngle;
            return 0;
        }

        /// <summary>The model point at the middle of a viewport (its view centre is in the twisted frame).</summary>
        public static Vec2 ModelCenter(Viewport vp)
        {
            double c = Math.Cos(-vp.TwistAngle), s = Math.Sin(-vp.TwistAngle);
            double x = vp.ViewCenter.X, y = vp.ViewCenter.Y;
            return new Vec2(vp.ViewTarget.X + x * c - y * s, vp.ViewTarget.Y + x * s + y * c);
        }

        /// <summary>North arrows on a sheet: inserts of the standards' north-arrow block, or of any block named like one.</summary>
        public static List<Insert> NorthArrows(ACadSharp.Objects.Layout layout, string northArrowBlock)
        {
            bool IsArrow(string n) =>
                (northArrowBlock.Length > 0 && n.Equals(northArrowBlock, StringComparison.OrdinalIgnoreCase))
                || n.IndexOf("NORTH", StringComparison.OrdinalIgnoreCase) >= 0 || n.Equals("NARROW", StringComparison.OrdinalIgnoreCase);
            return layout.AssociatedBlock.Entities.OfType<Insert>().Where(i => i.Block != null && IsArrow(i.Block.Name)).ToList();
        }

        /// <summary>
        /// Turns every sheet's plan viewport to <paramref name="twist"/> (radians, counter-clockwise;
        /// 0 = north up) and each sheet's north arrow by the same change. One undo step; null when
        /// there's no sheet with a plan viewport (the screen view can still turn).
        /// </summary>
        public static IEditCommand? Apply(CadDocument doc, double twist, string northArrowBlock, out int sheets, out int arrows) =>
            Apply(doc, twist, northArrowBlock, null, out sheets, out arrows, out _);

        /// <summary>
        /// As above, and - with <paramref name="labels"/> - the model's labels follow the view too
        /// (see <see cref="Relabel"/>): the turn counted from the twist the drawing had.
        /// </summary>
        public static IEditCommand? Apply(CadDocument doc, double twist, string northArrowBlock, LabelRules? labels, out int sheets, out int arrows, out int relabelled)
        {
            sheets = 0; arrows = 0; relabelled = 0;
            var cmds = new List<IEditCommand>();
            if (labels != null)
            {
                var r = Relabel(doc, labels.FromTwist, twist, labels.ElevationAngleDeg, out relabelled);
                if (r != null) cmds.Add(r);
            }
            foreach (var layout in doc.Layouts.Where(l => l.IsPaperSpace))
            {
                var vp = SheetScale.PlanViewport(layout);
                if (vp == null) continue;
                double delta = twist - vp.TwistAngle;
                if (Math.Abs(Angles.Normalize2Pi(delta + Math.PI) - Math.PI) < 1e-12) continue;
                var mid = ModelCenter(vp);
                // Keep the same model point in the middle: its place in the new twisted frame.
                double c = Math.Cos(twist), s = Math.Sin(twist);
                double dx = mid.X - vp.ViewTarget.X, dy = mid.Y - vp.ViewTarget.Y;
                var newCenter = new XY(dx * c - dy * s, dx * s + dy * c);
                var v = vp;
                var oldState = (v.TwistAngle, v.ViewCenter);
                cmds.Add(new SetPropertyCommand<(double, XY)>(oldState, (twist, newCenter), st => { v.TwistAngle = st.Item1; v.ViewCenter = st.Item2; }, "Surveyor view"));
                sheets++;
                foreach (var arrow in NorthArrows(layout, northArrowBlock))
                {
                    cmds.Add(TrueNorth(arrow, twist));
                    arrows++;
                }
            }
            return cmds.Count == 0 ? null : new CompositeCommand(cmds, twist == 0 ? "World view" : "Surveyor view");
        }

        /// <summary>How labels follow the view when it turns.</summary>
        public sealed class LabelRules
        {
            /// <summary>The twist the labels were last laid out for.</summary>
            public double FromTwist;
            /// <summary>Where elevations sit on the plan, degrees (45 = up-right).</summary>
            public double ElevationAngleDeg = 45;
        }

        private sealed class TextState
        {
            public XYZ Insert, Align; public double Rotation;
            public TextHorizontalAlignment H; public TextVerticalAlignmentType V;
        }

        private static TextState Of(TextEntity t) => new TextState { Insert = t.InsertPoint, Align = t.AlignmentPoint, Rotation = t.Rotation, H = t.HorizontalAlignment, V = t.VerticalAlignment };

        private static void Set(TextEntity t, TextState s)
        {
            t.HorizontalAlignment = s.H; t.VerticalAlignment = s.V;
            t.InsertPoint = s.Insert; t.AlignmentPoint = s.Align; t.Rotation = s.Rotation;
        }

        private static XYZ Turn(XYZ p, Vec2 about, double a)
        {
            double c = Math.Cos(a), s = Math.Sin(a), dx = p.X - about.X, dy = p.Y - about.Y;
            return new XYZ(about.X + dx * c - dy * s, about.Y + dx * s + dy * c, p.Z);
        }

        /// <summary>Is this point label its elevation? (Drafted onto an ELEVATION layer, or a decimal number that isn't the point number.)</summary>
        private static bool IsElevation(TextEntity t, int pointId)
        {
            if ((t.Layer?.Name ?? "").IndexOf("ELEV", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            var v = t.Value.Trim();
            return v.Contains('.') && v != pointId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
        }

        /// <summary>
        /// The model's labels follow a view turned from <paramref name="fromTwist"/> to
        /// <paramref name="toTwist"/>:
        /// <list type="bullet">
        /// <item>a point's labels and symbol (tagged with its point number) turn about the point,
        /// keeping their place round it and reading level on the plan - so a label moved by hand
        /// stays where it was put; the elevation is always put at <paramref name="elevationAngleDeg"/>
        /// on the plan (45° = up-right);</item>
        /// <item>other text that read level stays level on the plan;</item>
        /// <item>text along a line (bearings, distances, curve data) keeps its line and is turned end
        /// for end where it would now read upside down.</item>
        /// </list>
        /// One undo step; null when nothing needs to move.
        /// </summary>
        public static IEditCommand? Relabel(CadDocument doc, double fromTwist, double toTwist, double elevationAngleDeg, out int changed)
        {
            changed = 0;
            double delta = toTwist - fromTwist;
            // The view turns the drawing by +delta, so what stays level on the plan turns back by it.
            double turn = -delta;
            var cmds = new List<IEditCommand>();
            var model = doc.ModelSpace.Entities.ToList();
            // Where each tagged point is: its node (or, failing that, its symbol).
            var at = new Dictionary<int, Vec2>();
            foreach (var e in model)
                if (PointLinks.Tagged(e) is int id)
                {
                    if (e is Point pt) at[id] = new Vec2(pt.Location.X, pt.Location.Y);
                    else if (e is Insert ins && !at.ContainsKey(id)) at[id] = new Vec2(ins.InsertPoint.X, ins.InsertPoint.Y);
                }
            bool Level(double rotation, double twist) => Math.Abs(Math.Atan2(Math.Sin(rotation + twist), Math.Cos(rotation + twist))) < 0.01;
            // The straight courses, to tell a label along a line (a bearing on an east-west line
            // reads level too) from a note that just reads level.
            var courses = model.Where(x => x is Line || x is LwPolyline || x is Polyline2D).SelectMany(EntityOps.SpansOf).Where(x => !x.IsArc).ToList();
            bool AlongALine(Entity text)
            {
                double rot = SurveyDrafting.RotationOf(text), h = Math.Max(SurveyDrafting.HeightOf(text), 1e-9);
                var mid = SurveyDrafting.TextMiddle(text);
                foreach (var c in courses)
                {
                    double ang = Math.Atan2(c.B.Y - c.A.Y, c.B.X - c.A.X) - rot;
                    if (Math.Abs(Math.Sin(ang)) > 0.02) continue; // not parallel (about 1°)
                    if (Construct.DistanceToSegment(mid, c.A, c.B, out _) <= h * 4) return true;
                }
                return false;
            }

            foreach (var e in model)
            {
                int? tag = PointLinks.Tagged(e);
                Vec2? p = tag.HasValue && at.TryGetValue(tag.Value, out var pp) ? pp : (Vec2?)null;
                switch (e)
                {
                    case TextEntity t when p.HasValue && IsElevation(t, tag!.Value):
                    {
                        // Always along its 45° line from the point, as the plan is seen.
                        var old = Of(t);
                        var (spot, rot, h, v) = Annotator.ElevationPlace(p.Value, t.Height * 0.7, elevationAngleDeg, toTwist);
                        var nw = new TextState
                        {
                            Insert = new XYZ(spot.X, spot.Y, t.InsertPoint.Z), Align = new XYZ(spot.X, spot.Y, t.AlignmentPoint.Z), Rotation = rot,
                            H = h == HAlign.Left ? TextHorizontalAlignment.Left : h == HAlign.Center ? TextHorizontalAlignment.Center : TextHorizontalAlignment.Right,
                            V = v == VAlign.Bottom ? TextVerticalAlignmentType.Bottom : v == VAlign.Middle ? TextVerticalAlignmentType.Middle : TextVerticalAlignmentType.Top,
                        };
                        bool same = Math.Abs(old.Rotation - nw.Rotation) < 1e-9 && old.H == nw.H && old.V == nw.V
                            && Math.Abs(old.Align.X - nw.Align.X) < 1e-9 && Math.Abs(old.Align.Y - nw.Align.Y) < 1e-9;
                        if (same) break;
                        var te = t;
                        cmds.Add(new SetPropertyCommand<TextState>(old, nw, st => Set(te, st), "Labels follow the view"));
                        changed++;
                        break;
                    }
                    case TextEntity t when p.HasValue:
                    {
                        if (Math.Abs(delta) < 1e-12) break;
                        var old = Of(t);
                        var nw = new TextState { Insert = Turn(t.InsertPoint, p.Value, turn), Align = Turn(t.AlignmentPoint, p.Value, turn), Rotation = t.Rotation + turn, H = t.HorizontalAlignment, V = t.VerticalAlignment };
                        var te = t;
                        cmds.Add(new SetPropertyCommand<TextState>(old, nw, st => Set(te, st), "Labels follow the view"));
                        changed++;
                        break;
                    }
                    case MText m when p.HasValue:
                    {
                        if (Math.Abs(delta) < 1e-12) break;
                        var old = (m.InsertPoint, m.AlignmentPoint);
                        double r = SurveyDrafting.RotationOf(m) + turn;
                        var nw = (Turn(m.InsertPoint, p.Value, turn), new XYZ(Math.Cos(r), Math.Sin(r), 0));
                        var me = m;
                        cmds.Add(new SetPropertyCommand<(XYZ, XYZ)>(old, nw, st => { me.InsertPoint = st.Item1; me.AlignmentPoint = st.Item2; }, "Labels follow the view"));
                        changed++;
                        break;
                    }
                    case Insert ins when p.HasValue:
                    {
                        if (Math.Abs(delta) < 1e-12) break;
                        var i = ins;
                        cmds.Add(new SetPropertyCommand<double>(i.Rotation, i.Rotation + turn, v => i.Rotation = v, "Labels follow the view"));
                        changed++;
                        break;
                    }
                    case TextEntity _:
                    case MText _:
                    {
                        double rot = SurveyDrafting.RotationOf(e);
                        if (Level(rot, fromTwist) && Math.Abs(delta) > 1e-12 && !AlongALine(e))
                        {
                            var c = SurveyDrafting.SetRotation(e, rot + turn, "Labels follow the view");
                            if (c != null) { cmds.Add(c); changed++; }
                        }
                        else if (!Angles.ReadsLeftToRight(rot, toTwist))
                        {
                            var c = SurveyDrafting.Rotate180(e, "Labels follow the view");
                            if (c != null) { cmds.Add(c); changed++; }
                        }
                        break;
                    }
                }
            }
            // FD-Draft's own dimensions redraw their text to read in the turned view.
            var dims = model.OfType<Dimension>().Where(DimensionBuilder.IsOurs).ToList();
            if (dims.Count > 0) cmds.Add(new RedrawForTwist(dims, fromTwist, toTwist));
            return cmds.Count == 0 ? null : new CompositeCommand(cmds, "Labels follow the view");
        }

        /// <summary>Redraws dimension pictures for a view twist (and back for undo).</summary>
        private sealed class RedrawForTwist : IEditCommand
        {
            private readonly List<Dimension> _dims; private readonly double _from, _to;
            public string Description => "Labels follow the view";
            public RedrawForTwist(List<Dimension> dims, double from, double to) { _dims = dims; _from = from; _to = to; Draw(_to); }
            private void Draw(double twist)
            {
                double keep = Angles.ViewTwist;
                Angles.ViewTwist = twist;
                try { foreach (var d in _dims) if (d.Document != null) DimensionBuilder.DrawPicture(d); }
                finally { Angles.ViewTwist = keep; }
            }
            public void Undo() => Draw(_from);
            public void Redo() => Draw(_to);
        }

        /// <summary>
        /// A north arrow set to point true north on a sheet whose plan is turned by
        /// <paramref name="twist"/>, with an even, unmirrored scale (the size it had) - which also
        /// mends one squashed or flipped by the old block-transform bug.
        /// </summary>
        public static IEditCommand TrueNorth(Insert arrow, double twist)
        {
            double k = Math.Sqrt(Math.Abs(arrow.XScale * arrow.YScale));
            if (k < 1e-12) k = Math.Max(Math.Abs(arrow.XScale), Math.Abs(arrow.YScale));
            if (k < 1e-12) k = 1;
            var a = arrow;
            var old = (a.Rotation, a.XScale, a.YScale, a.ZScale);
            var nw = (Angles.Normalize2Pi(twist), k, k, k);
            return new SetPropertyCommand<(double, double, double, double)>(old, nw, st => { a.Rotation = st.Item1; a.XScale = st.Item2; a.YScale = st.Item3; a.ZScale = st.Item4; }, "North arrow");
        }

        /// <summary>Every sheet's north arrow pointing true north for its plan viewport's twist, evenly scaled. Null when there's none.</summary>
        public static IEditCommand? RepairNorthArrows(CadDocument doc, string northArrowBlock, out int arrows)
        {
            arrows = 0;
            var cmds = new List<IEditCommand>();
            foreach (var layout in doc.Layouts.Where(l => l.IsPaperSpace))
            {
                double twist = SheetScale.PlanViewport(layout)?.TwistAngle ?? 0;
                foreach (var arrow in NorthArrows(layout, northArrowBlock)) { cmds.Add(TrueNorth(arrow, twist)); arrows++; }
            }
            return cmds.Count == 0 ? null : new CompositeCommand(cmds, "North arrow");
        }

        /// <summary>The twist that lays a line level, reading left to right.</summary>
        public static double TwistToLevel(Vec2 a, Vec2 b) => -Angles.ReadableRotation(a, b, 0);

        /// <summary>The twist that points a grid azimuth (radians, clockwise from north) straight up.</summary>
        public static double TwistToPointUp(double azimuth)
        {
            double t = Angles.Normalize2Pi(azimuth);
            return t > Math.PI ? t - 2 * Math.PI : t;
        }
    }
}
