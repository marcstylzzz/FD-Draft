using System;
using System.Collections.Generic;
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
    /// LABEL: bearing/distance (or curve-data) labels for linework drawn or edited by hand,
    /// built by exactly the same rules the drafting pipeline's Annotator uses
    /// (<see cref="Annotator.StraightCourseLabels"/>, <see cref="Annotator.ArcCourseLabels"/>):
    /// the firm's text heights, layers, styles, gap, bearing format and rotation.
    /// </summary>
    public static class CourseLabelling
    {
        /// <summary>
        /// Labels for one Line, Arc, or every span of an LwPolyline/Polyline2D. Heights are the
        /// standards' paper millimetres times <paramref name="modelPerMm"/>. Layers are created
        /// on the fly through <paramref name="layer"/> when the drawing lacks them.
        /// </summary>
        public static List<Entity> For(Entity e, CadDocument doc, FirmStandards std, double modelPerMm, Func<string, Layer> layer, double gridToGround = 1.0)
        {
            var texts = new List<DraftText>();
            switch (e)
            {
                case Line l:
                    texts.AddRange(Annotator.StraightCourseLabels(new Vec2(l.StartPoint.X, l.StartPoint.Y), new Vec2(l.EndPoint.X, l.EndPoint.Y), 0.5, std, modelPerMm, gridToGround, std.BearingLayer, std.DistanceLayer));
                    break;
                case ACadSharp.Entities.Arc a:
                {
                    double sweep = a.EndAngle - a.StartAngle;
                    while (sweep <= 0) sweep += Angles.TwoPi;
                    texts.AddRange(Annotator.ArcCourseLabels(ToCore(new Vec2(a.Center.X, a.Center.Y), a.Radius, a.StartAngle, sweep), 0.5, std, modelPerMm, gridToGround, std.ArcLayer));
                    break;
                }
                case LwPolyline lp when lp.Vertices.Count >= 2:
                    texts.AddRange(ForSpans(Construct.Spans(lp.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList(), lp.Vertices.Select(v => v.Bulge).ToList(), lp.IsClosed), std, modelPerMm, gridToGround));
                    break;
                case Polyline2D p2 when p2.Vertices.Count >= 2:
                    texts.AddRange(ForSpans(Construct.Spans(p2.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList(), p2.Vertices.Select(v => v.Bulge).ToList(), p2.IsClosed), std, modelPerMm, gridToGround));
                    break;
            }
            return texts.Select(t => (Entity)ToEntity(t, doc, modelPerMm, layer)).ToList();
        }

        private static IEnumerable<DraftText> ForSpans(IEnumerable<Construct.Span> spans, FirmStandards std, double modelPerMm, double gridToGround)
        {
            foreach (var s in spans)
            {
                if (Vec2.Distance(s.A, s.B) < 1e-9) continue;
                if (!s.IsArc)
                    foreach (var t in Annotator.StraightCourseLabels(s.A, s.B, 0.5, std, modelPerMm, gridToGround, std.BearingLayer, std.DistanceLayer)) yield return t;
                else
                {
                    // Core arcs carry a signed sweep, so the chord bearing runs the polyline's way.
                    double start = Math.Atan2(s.A.Y - s.Center.Y, s.A.X - s.Center.X);
                    foreach (var t in Annotator.ArcCourseLabels(ToCore(s.Center, s.Radius, start, s.Sweep), 0.5, std, modelPerMm, gridToGround, std.ArcLayer)) yield return t;
                }
            }
        }

        private static CoreArc ToCore(Vec2 c, double r, double start, double sweep) => new CoreArc
        {
            Center = c, Radius = r, StartAngle = start, Sweep = sweep,
            Start = new Vec2(c.X + r * Math.Cos(start), c.Y + r * Math.Sin(start)),
            End = new Vec2(c.X + r * Math.Cos(start + sweep), c.Y + r * Math.Sin(start + sweep)),
        };

        /// <summary>A pipeline label as a DWG TEXT, aligned the way TemplateDrafter writes them.</summary>
        public static TextEntity ToEntity(DraftText t, CadDocument doc, double modelPerMm, Func<string, Layer> layer)
        {
            var at = new XYZ(t.Position.X, t.Position.Y, 0);
            var text = new TextEntity
            {
                Value = TemplateDrafter.AcadText(t.Text),
                InsertPoint = at,
                AlignmentPoint = at,
                Height = t.HeightMm * modelPerMm,
                Rotation = t.Rotation,
                HorizontalAlignment = t.H == HAlign.Left ? TextHorizontalAlignment.Left : t.H == HAlign.Center ? TextHorizontalAlignment.Center : TextHorizontalAlignment.Right,
                VerticalAlignment = t.V == VAlign.Bottom ? TextVerticalAlignmentType.Bottom : t.V == VAlign.Middle ? TextVerticalAlignmentType.Middle : TextVerticalAlignmentType.Top,
                Layer = layer(t.Layer),
            };
            if (!string.IsNullOrEmpty(t.Style) && doc.TextStyles.TryGetValue(t.Style, out var style)) text.Style = style;
            return text;
        }
    }
}
