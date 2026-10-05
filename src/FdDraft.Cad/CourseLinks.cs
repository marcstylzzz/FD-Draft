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
            /// <summary>The span's ends (in its own direction) when last labelled.</summary>
            public Vec2 A { get; }
            public Vec2 B { get; }
            /// <summary>The span's bulge then (0 for a straight span) - an arc can change without its ends moving.</summary>
            public double Bulge { get; }
            /// <summary>The label reads the course the other way round from the span (a drafted
            /// course running against its polyline): its bearing is worked out B to A.</summary>
            public bool Reversed { get; }
            public Link(ulong course, int span, string kind, Vec2 a, Vec2 b, double bulge = 0, bool reversed = false)
            { Course = course; Span = span; Kind = kind; A = a; B = b; Bulge = bulge; Reversed = reversed; }

            public Link With(ulong course, int span, Construct.Span s, bool reversed) => new Link(course, span, Kind, s.A, s.B, s.Bulge, reversed);

            /// <summary>Unlinked: the course it described is gone (split around a label, say).</summary>
            public Link Dead() => new Link(0, Span, Kind, A, B, Bulge, Reversed);

            public override string ToString() => string.Join("|",
                Course.ToString("X", CultureInfo.InvariantCulture), Span.ToString(CultureInfo.InvariantCulture), Kind,
                R(A.X), R(A.Y), R(B.X), R(B.Y)) + (Bulge != 0 ? "|b=" + R(Bulge) : "") + (Reversed ? "|R" : "");

            private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

            public static Link? Parse(string s)
            {
                var f = s.Split('|');
                if (f.Length < 7) return null;
                if (!ulong.TryParse(f[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong h)) return null;
                if (!int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int span)) return null;
                var d = new double[4];
                for (int i = 0; i < 4; i++) if (!double.TryParse(f[3 + i], NumberStyles.Float, CultureInfo.InvariantCulture, out d[i])) return null;
                double bulge = 0; bool rev = false;
                for (int i = 7; i < f.Length; i++)
                {
                    if (f[i] == "R") rev = true;
                    else if (f[i].StartsWith("b=", StringComparison.Ordinal)) double.TryParse(f[i].Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out bulge);
                }
                return new Link(h, span, f[2], new Vec2(d[0], d[1]), new Vec2(d[2], d[3]), bulge, rev);
            }
        }

        /// <summary>Links a label (already in the drawing) to span <paramref name="span"/> of <paramref name="course"/>.</summary>
        public static void Tag(Entity label, Entity course, int span, string kind, bool reversed = false)
        {
            var s = EntityOps.SpansOf(course).ElementAtOrDefault(span);
            Write(label, new Link(course.Handle, span, kind, s.A, s.B, s.Bulge, reversed));
        }

        public static void Write(Entity label, Link link) => PointLinks.SetValue(label, Key, new ExtendedDataString(link.ToString()));

        public static Link? Read(Entity label) => PointLinks.GetValue(label, Key) is ExtendedDataString s ? Link.Parse(s.Value) : null;

        /// <summary>
        /// The span of <paramref name="e"/> with ends a and b and the given bulge: its index, and
        /// whether it runs b→a (then its bulge is the opposite). -1 if none.
        /// </summary>
        public static int SpanIndex(Entity e, Vec2 a, Vec2 b, double bulge, out bool flipped)
        {
            int i = 0;
            flipped = false;
            foreach (var s in EntityOps.SpansOf(e).Take(e is Circle && !(e is ACadSharp.Entities.Arc) ? 0 : int.MaxValue))
            {
                if (Vec2.Distance(s.A, a) < 1e-6 && Vec2.Distance(s.B, b) < 1e-6 && Math.Abs(s.Bulge - bulge) < 1e-9) return i;
                if (Vec2.Distance(s.A, b) < 1e-6 && Vec2.Distance(s.B, a) < 1e-6 && Math.Abs(s.Bulge + bulge) < 1e-9) { flipped = true; return i; }
                i++;
            }
            return -1;
        }

        public static int SpanIndex(Entity e, Vec2 a, Vec2 b) => SpanIndex(e, a, b, 0, out _);

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
            if (IsCurveBlock(kind))
            {
                var d = SurveyLabels.CurveData(arc, std, g2g);
                int k = kind[1] - '0';
                return k < d.Length ? d[k] : null;
            }
            return null;
        }

        /// <summary>A line of a curve-data block placed anywhere: rewritten, never moved.</summary>
        private static bool IsCurveBlock(string kind) => kind.Length == 2 && kind[0] == 'C' && char.IsDigit(kind[1]);

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
                if (link == null || link.Value.Course == 0) continue;
                var l = link.Value;
                if (courses != null && !courses.Contains(l.Course)) continue;
                if (!(doc.GetCadObject(l.Course) is Entity course) || course.Owner == null) continue;
                var spans = EntityOps.SpansOf(course).ToList();
                // Unchanged, or renumbered by a vertex added or removed elsewhere: the same span,
                // found by its ends and curve, just gets its new number (and direction).
                int exact = SpanIndex(course, l.A, l.B, l.Bulge, out bool flipped);
                if (exact >= 0)
                {
                    if (exact != l.Span || flipped)
                    {
                        var st = TextStateCommand.State.Of(t);
                        var ns = spans[exact];
                        string? txt = flipped ? st.Value : null; // same course either way; the text stands
                        changes.Add(new TextStateCommand.Change(t, st, new TextStateCommand.State(txt ?? st.Value, st.Insert, st.Align, st.Rotation,
                            l.With(l.Course, exact, ns, flipped ? !l.Reversed : l.Reversed).ToString())));
                    }
                    continue;
                }
                if (l.Span < 0 || l.Span >= spans.Count) continue;
                var s = spans[l.Span];
                var old = Construct.Span.FromBulge(l.A, l.B, l.Bulge);
                if (Vec2.Distance(s.A, s.B) < 1e-12 || Vec2.Distance(l.A, l.B) < 1e-12) continue;
                string? text = TextFor(l.Kind, s, std, mpm, g2g, l.Reversed);
                if (text == null) continue;

                bool aligned = t.HorizontalAlignment != TextHorizontalAlignment.Left || t.VerticalAlignment != TextVerticalAlignmentType.Baseline;
                var anchor = aligned ? new Vec2(t.AlignmentPoint.X, t.AlignmentPoint.Y) : new Vec2(t.InsertPoint.X, t.InsertPoint.Y);
                var (moved, rot) = Carry(old, s, anchor, t.Rotation);
                if (IsCurveBlock(l.Kind) || alreadyMoved != null && alreadyMoved.Contains(t.Handle)) { moved = anchor; rot = t.Rotation; }

                var before = TextStateCommand.State.Of(t);
                var shift = moved - anchor;
                var after = new TextStateCommand.State(
                    TemplateDrafter.AcadText(text),
                    new XYZ(t.InsertPoint.X + shift.X, t.InsertPoint.Y + shift.Y, t.InsertPoint.Z),
                    new XYZ(t.AlignmentPoint.X + shift.X, t.AlignmentPoint.Y + shift.Y, t.AlignmentPoint.Z),
                    rot, l.With(l.Course, l.Span, s, l.Reversed).ToString());
                changes.Add(new TextStateCommand.Change(t, before, after));
            }
            count = changes.Count;
            return changes.Count == 0 ? null : new TextStateCommand(changes, "Relabel");
        }

        /// <summary>
        /// Where a label at <paramref name="anchor"/> beside span <paramref name="from"/> goes
        /// beside <paramref name="to"/>, and its rotation: straight spans by how far along and how
        /// far off the line it sat; arcs by the angle round the centre and the distance off the
        /// curve. A label that read along its course (or round its curve) keeps doing so.
        /// </summary>
        public static (Vec2 At, double Rotation) Carry(Construct.Span from, Construct.Span to, Vec2 anchor, double rotation)
        {
            if (from.IsArc && to.IsArc && Math.Abs(from.Sweep) > 1e-12)
            {
                double a0 = Math.Atan2(from.A.Y - from.Center.Y, from.A.X - from.Center.X);
                double aa = Math.Atan2(anchor.Y - from.Center.Y, anchor.X - from.Center.X);
                double d = from.Sweep > 0 ? Angles.Normalize2Pi(aa - a0) : -Angles.Normalize2Pi(a0 - aa);
                if (Math.Abs(d) > Math.Abs(from.Sweep) + Math.PI) d -= Math.Sign(d) * Angles.TwoPi; // just before the start
                double frac = d / from.Sweep;
                double off = Vec2.Distance(anchor, from.Center) - from.Radius;
                double b0 = Math.Atan2(to.A.Y - to.Center.Y, to.A.X - to.Center.X);
                double ang = b0 + to.Sweep * frac;
                var dir = new Vec2(Math.Cos(ang), Math.Sin(ang));
                var at = to.Center + dir * (to.Radius + off);
                double oldTan = Angles.ReadableRotation(new Vec2(0, 0), new Vec2(-Math.Sin(aa), Math.Cos(aa)));
                double newTan = Angles.ReadableRotation(new Vec2(0, 0), new Vec2(-Math.Sin(ang), Math.Cos(ang)));
                return (at, Math.Abs(Math.Sin(rotation - oldTan)) < 1e-6 ? newTan : rotation + (ang - aa));
            }
            double oldLen = Vec2.Distance(from.A, from.B), newLen = Vec2.Distance(to.A, to.B);
            var u0 = (from.B - from.A) * (1 / oldLen); var n0 = u0.Left();
            var u1 = (to.B - to.A) * (1 / newLen); var n1 = u1.Left();
            double along = Vec2.Dot(anchor - from.A, u0) / oldLen, offset = Vec2.Dot(anchor - from.A, n0);
            var moved = to.A + u1 * (along * newLen) + n1 * offset;
            double oldRead = Angles.ReadableRotation(from.A, from.B), newRead = Angles.ReadableRotation(to.A, to.B);
            return (moved, Math.Abs(Math.Sin(rotation - oldRead)) < 1e-6 ? newRead : rotation);
        }

        /// <summary>
        /// After courses were replaced by new entities (a Polyline2D rebuilt, lines JOINed, a
        /// polyline split round an on-line label): labels linked to a replaced course are linked
        /// to whichever replacement has their span; when the span itself is gone they're linked
        /// to the same-numbered span of a sole replacement, or else unlinked. One undo step.
        /// </summary>
        public static IEditCommand? Rehome(IEnumerable<Entity> labels, IDictionary<ulong, IList<Entity>> replaced)
        {
            var changes = new List<TextStateCommand.Change>();
            foreach (var e in labels)
            {
                if (!(e is TextEntity t)) continue;
                var link = Read(t);
                if (link == null || !replaced.TryGetValue(link.Value.Course, out var candidates)) continue;
                var l = link.Value;
                Link? to = null;
                foreach (var c in candidates)
                {
                    int i = SpanIndex(c, l.A, l.B, l.Bulge, out bool flipped);
                    if (i < 0) continue;
                    var s = EntityOps.SpansOf(c).ElementAt(i);
                    to = l.With(c.Handle, i, s, flipped ? !l.Reversed : l.Reversed);
                    break;
                }
                if (to == null && candidates.Count == 1 && l.Span < EntityOps.SpansOf(candidates[0]).Count())
                    to = new Link(candidates[0].Handle, l.Span, l.Kind, l.A, l.B, l.Bulge, l.Reversed); // relabelled on the next change
                var st = TextStateCommand.State.Of(t);
                changes.Add(new TextStateCommand.Change(t, st, new TextStateCommand.State(st.Value, st.Insert, st.Align, st.Rotation, (to ?? l.Dead()).ToString())));
            }
            return changes.Count == 0 ? null : new TextStateCommand(changes, "Relink labels");
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
