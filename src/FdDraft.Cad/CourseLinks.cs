using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using ACadSharp.XData;
using CSMath;
using FdDraft.Cad.Editing;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;
using FdDraft.Core.Standards;
using CoreArc = FdDraft.Core.Geometry.Arc;

namespace FdDraft.Cad
{
    /// <summary>
    /// The link from a bearing/distance/curve label to the course it describes: the course
    /// entity's handle, which span of it, what the label says (bearing, distance, both, a curve
    /// datum...) and where the span's ends were when it was labelled - saved with the label as
    /// FD-Draft extended data ("COURSE"). When the course is reshaped (STRETCH, a vertex edit,
    /// MOVE/ROTATE of the line alone), RELABEL rewrites the text from the new geometry and
    /// carries the label to the same place relative to the course - a label dragged or flipped
    /// to one side stays on that side.
    /// </summary>
    public static class CourseLinks
    {
        private const string Key = "COURSE";

        /// <summary>What a linked label says.</summary>
        public static class Kinds
        {
            public const string Bearing = "B", Distance = "D", BearingDistance = "BD", DistanceBearing = "DB";
            public const string SplitTop = "SB1", SplitBottom = "SB2";
            /// <summary>The two curve-data lines on an arc (radius/arc, chord/bearing).</summary>
            public const string ArcOuter = "A0", ArcInner = "A1";
            /// <summary>A line of a placed curve-data block: C0 radius ... C4 delta.</summary>
            public const string CurveLine = "C";
        }

        public readonly struct Link
        {
            public ulong Course { get; }
            public int Span { get; }
            public string Kind { get; }
            public Vec2 A { get; }
            public Vec2 B { get; }
            /// <summary>The label reads the course the other way round from the span (a drafted
            /// course running against its polyline): its bearing is worked out B to A.</summary>
            public bool Reversed { get; }
            public Link(ulong course, int span, string kind, Vec2 a, Vec2 b, bool reversed = false) { Course = course; Span = span; Kind = kind; A = a; B = b; Reversed = reversed; }

            public override string ToString() => string.Join("|",
                Course.ToString("X", CultureInfo.InvariantCulture), Span.ToString(CultureInfo.InvariantCulture), Kind,
                R(A.X), R(A.Y), R(B.X), R(B.Y)) + (Reversed ? "|R" : "");

            private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

            public static Link? Parse(string s)
            {
                var f = s.Split('|');
                if (f.Length != 7 && f.Length != 8) return null;
                if (!ulong.TryParse(f[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong h)) return null;
                if (!int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int span)) return null;
                var d = new double[4];
                for (int i = 0; i < 4; i++) if (!double.TryParse(f[3 + i], NumberStyles.Float, CultureInfo.InvariantCulture, out d[i])) return null;
                return new Link(h, span, f[2], new Vec2(d[0], d[1]), new Vec2(d[2], d[3]), f.Length == 8 && f[7] == "R");
            }
        }

        /// <summary>Links a label (already in the drawing) to span <paramref name="span"/> of <paramref name="course"/>.</summary>
        public static void Tag(Entity label, Entity course, int span, string kind, bool reversed = false)
        {
            var s = EntityOps.SpansOf(course).ElementAtOrDefault(span);
            Write(label, new Link(course.Handle, span, kind, s.A, s.B, reversed));
        }

        public static void Write(Entity label, Link link) => PointLinks.SetValue(label, Key, new ExtendedDataString(link.ToString()));

        public static Link? Read(Entity label) => PointLinks.GetValue(label, Key) is ExtendedDataString s ? Link.Parse(s.Value) : null;

        /// <summary>The span index of <paramref name="e"/> whose ends are a→b (either way round); -1 if none.</summary>
        public static int SpanIndex(Entity e, Vec2 a, Vec2 b)
        {
            int i = 0;
            foreach (var s in EntityOps.SpansOf(e))
            {
                if (Vec2.Distance(s.A, a) < 1e-6 && Vec2.Distance(s.B, b) < 1e-6 || Vec2.Distance(s.A, b) < 1e-6 && Vec2.Distance(s.B, a) < 1e-6) return i;
                i++;
            }
            return -1;
        }

        /// <summary>The text a label of <paramref name="kind"/> reads for span <paramref name="s"/>; null for a kind that doesn't fit it.</summary>
        public static string? TextFor(string kind, Construct.Span s, FirmStandards std, double mpm, double g2g, bool reversed = false)
        {
            if (reversed) s = s.IsArc ? Construct.Span.Arc(s.B, s.A, s.Center, s.Radius, -s.Sweep) : Construct.Span.Straight(s.B, s.A);
            if (!s.IsArc)
            {
                var mid = (s.A + s.B) * 0.5;
                string Layout(CourseLabelStyle st, int i)
                {
                    var r = CourseAnnotation.Layout(s.A, s.B, mid + new Vec2(0, 1), st, std, mpm, g2g);
                    return i < r.Texts.Count ? r.Texts[i].Text : "";
                }
                switch (kind)
                {
                    case Kinds.Bearing: return Annotator.StraightCourseLabels(s.A, s.B, 0.5, std, mpm, g2g, "", "")[0].Text;
                    case Kinds.Distance: return Annotator.StraightCourseLabels(s.A, s.B, 0.5, std, mpm, g2g, "", "")[1].Text;
                    case Kinds.BearingDistance: return Layout(CourseLabelStyle.BearingDashDistance, 0);
                    case Kinds.DistanceBearing: return Layout(CourseLabelStyle.DistanceBeforeBearing, 0);
                    case Kinds.SplitTop: return Layout(CourseLabelStyle.SplitBearing, 0);
                    case Kinds.SplitBottom: return Layout(CourseLabelStyle.SplitBearing, 1);
                    default: return null;
                }
            }
            var arc = new CoreArc
            {
                Center = s.Center, Radius = s.Radius, StartAngle = Math.Atan2(s.A.Y - s.Center.Y, s.A.X - s.Center.X), Sweep = s.Sweep, Start = s.A, End = s.B,
            };
            if (kind == Kinds.ArcOuter || kind == Kinds.ArcInner)
                return Annotator.ArcCourseLabels(arc, 0.5, std, mpm, g2g, "")[kind == Kinds.ArcOuter ? 0 : 1].Text;
            if (kind.Length == 2 && kind[0] == 'C' && char.IsDigit(kind[1]))
            {
                var d = SurveyLabels.CurveData(arc, std, g2g);
                int k = kind[1] - '0';
                return k < d.Length ? d[k] : null;
            }
            return null;
        }

        /// <summary>
        /// RELABEL: every linked label among <paramref name="labels"/> whose course (if
        /// <paramref name="courses"/> is given, one of those) has changed shape since it was
        /// labelled - its text rewritten, and it moved and turned with its span. One undo step;
        /// null when nothing needed it.
        /// </summary>
        /// <param name="alreadyMoved">Labels that moved along with their course (a MOVE or ROTATE
        /// of both): their text is rewritten if need be but they aren't carried again.</param>
        public static IEditCommand? Relabel(CadDocument doc, IEnumerable<Entity> labels, ISet<ulong>? courses, FirmStandards std, double mpm, double g2g, out int count, ISet<ulong>? alreadyMoved = null)
        {
            count = 0;
            var changes = new List<TextStateCommand.Change>();
            foreach (var e in labels)
            {
                if (!(e is TextEntity t)) continue;
                var link = Read(t);
                if (link == null) continue;
                var l = link.Value;
                if (courses != null && !courses.Contains(l.Course)) continue;
                if (!(doc.GetCadObject(l.Course) is Entity course) || course.Owner == null) continue;
                var spans = EntityOps.SpansOf(course).ToList();
                // A vertex added or removed elsewhere renumbers the spans: the same span, found by
                // its ends, just gets its new number.
                int exact = SpanIndex(course, l.A, l.B);
                if (exact >= 0)
                {
                    if (exact != l.Span)
                    {
                        var st = TextStateCommand.State.Of(t);
                        changes.Add(new TextStateCommand.Change(t, st, new TextStateCommand.State(st.Value, st.Insert, st.Align, st.Rotation, new Link(l.Course, exact, l.Kind, l.A, l.B, l.Reversed).ToString())));
                    }
                    continue;
                }
                if (l.Span < 0 || l.Span >= spans.Count) continue;
                var s = spans[l.Span];
                double oldLen = Vec2.Distance(l.A, l.B), newLen = Vec2.Distance(s.A, s.B);
                if (oldLen < 1e-12 || newLen < 1e-12) continue;
                string? text = TextFor(l.Kind, s, std, mpm, g2g, l.Reversed);
                if (text == null) continue;

                // Where the label sat in the old span's frame, put it in the new one's.
                var u0 = (l.B - l.A) * (1 / oldLen); var n0 = u0.Left();
                var u1 = (s.B - s.A) * (1 / newLen); var n1 = u1.Left();
                bool aligned = t.HorizontalAlignment != TextHorizontalAlignment.Left || t.VerticalAlignment != TextVerticalAlignmentType.Baseline;
                var anchor = aligned ? new Vec2(t.AlignmentPoint.X, t.AlignmentPoint.Y) : new Vec2(t.InsertPoint.X, t.InsertPoint.Y);
                double along = Vec2.Dot(anchor - l.A, u0) / oldLen, off = Vec2.Dot(anchor - l.A, n0);
                var moved = s.A + u1 * (along * newLen) + n1 * off;
                // A label read along its course keeps reading along it; anything else keeps its angle.
                double oldRead = Angles.ReadableRotation(l.A, l.B), newRead = Angles.ReadableRotation(s.A, s.B);
                double rot = Math.Abs(Math.Sin(t.Rotation - oldRead)) < 1e-6 ? newRead : t.Rotation;

                var before = TextStateCommand.State.Of(t);
                if (alreadyMoved != null && alreadyMoved.Contains(t.Handle)) { moved = anchor; rot = t.Rotation; }
                var shift = moved - anchor;
                var after = new TextStateCommand.State(
                    TemplateDrafter.AcadText(text),
                    new XYZ(t.InsertPoint.X + shift.X, t.InsertPoint.Y + shift.Y, t.InsertPoint.Z),
                    new XYZ(t.AlignmentPoint.X + shift.X, t.AlignmentPoint.Y + shift.Y, t.AlignmentPoint.Z),
                    rot, new Link(l.Course, l.Span, l.Kind, s.A, s.B, l.Reversed).ToString());
                changes.Add(new TextStateCommand.Change(t, before, after));
            }
            count = changes.Count;
            return changes.Count == 0 ? null : new TextStateCommand(changes, "Relabel");
        }
    }

    /// <summary>Sets texts' content, position, rotation and course link together; undoes it exactly.</summary>
    public sealed class TextStateCommand : IEditCommand
    {
        public readonly struct State
        {
            public string Value { get; }
            public XYZ Insert { get; }
            public XYZ Align { get; }
            public double Rotation { get; }
            public string Link { get; }
            public State(string value, XYZ insert, XYZ align, double rotation, string link) { Value = value; Insert = insert; Align = align; Rotation = rotation; Link = link; }

            public static State Of(TextEntity t) =>
                new State(t.Value, t.InsertPoint, t.AlignmentPoint, t.Rotation, CourseLinks.Read(t)?.ToString() ?? "");

            public void ApplyTo(TextEntity t)
            {
                t.Value = Value; t.InsertPoint = Insert; t.AlignmentPoint = Align; t.Rotation = Rotation;
                var l = CourseLinks.Link.Parse(Link);
                if (l.HasValue) CourseLinks.Write(t, l.Value);
            }
        }

        public readonly struct Change
        {
            public TextEntity Text { get; }
            public State Before { get; }
            public State After { get; }
            public Change(TextEntity text, State before, State after) { Text = text; Before = before; After = after; }
        }

        private readonly List<Change> _changes;
        public string Description { get; }

        public TextStateCommand(IEnumerable<Change> changes, string description)
        {
            _changes = changes.ToList();
            Description = description;
            Redo();
        }

        public void Redo() { foreach (var c in _changes) c.After.ApplyTo(c.Text); }
        public void Undo() { foreach (var c in _changes) c.Before.ApplyTo(c.Text); }
    }
}
