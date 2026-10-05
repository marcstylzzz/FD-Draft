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
    /// MSCAD's MS Ties toolbar on a drawing: house ties, leaders, a line of blocks and the
    /// line/curve tables, each one undoable edit.
    /// </summary>
    public static class TieEditing
    {
        /// <summary>A closed polyline's corners (a building footprint); null for anything else.</summary>
        public static List<Vec2>? Footprint(Entity e) => e switch
        {
            LwPolyline lp when lp.Vertices.Count >= 3 && (lp.IsClosed || Closes(lp.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList()))
                => Distinct(lp.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList()),
            Polyline2D p2 when p2.Vertices.Count >= 3 && (p2.IsClosed || Closes(p2.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList()))
                => Distinct(p2.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList()),
            _ => null,
        };

        private static bool Closes(List<Vec2> v) => Vec2.Distance(v[0], v[v.Count - 1]) < 1e-6;
        private static List<Vec2> Distinct(List<Vec2> v) { if (v.Count > 3 && Closes(v)) v.RemoveAt(v.Count - 1); return v; }

        private static IEnumerable<Entity> Drawn(IEnumerable<(DraftPolyline Line, DraftText Text)> ties, CadDocument doc, double mpm, Func<string, Layer> layer)
        {
            foreach (var (line, text) in ties)
            {
                yield return CourseLabelling.ToPolyline(line, layer);
                yield return CourseLabelling.ToEntity(text, doc, mpm, layer);
            }
        }

        /// <summary>
        /// "Automatic House Ties": ties from the building's corners square to each lot line among
        /// <paramref name="lots"/> (the two nearest corners per line), each with its distance.
        /// </summary>
        public static IEditCommand? Automatic(Entity building, IEnumerable<Entity> lots, bool arrows, double arrowMm, CadDocument doc, FirmStandards std, double mpm, double g2g, Func<string, Layer> layer, out int count, out string why)
        {
            count = 0; why = "";
            if (!(building.Owner is BlockRecord owner)) { why = "not in a drawing"; return null; }
            var corners = Footprint(building);
            if (corners == null) { why = "the building must be a closed polyline"; return null; }
            var spans = lots.Where(l => l != building).SelectMany(EntityOps.SpansOf).ToList();
            if (spans.Count == 0) { why = "no lot lines"; return null; }
            var ties = Ties.Automatic(corners, spans);
            count = ties.Count;
            if (count == 0) { why = "no corner is square to a lot line"; return null; }
            var made = Drawn(ties.Select(t => Ties.Draw(t, arrows, arrowMm, std, mpm, g2g, std.DistanceLayer)), doc, mpm, layer).ToList();
            return new AddEntitiesCommand(owner, made, "House ties");
        }

        /// <summary>"Manual House Tie": from <paramref name="corner"/> square to the lot line of <paramref name="lot"/> picked at <paramref name="lotPick"/>.</summary>
        public static IEditCommand? Manual(BlockRecord owner, Vec2 corner, Entity lot, Vec2 lotPick, bool arrows, double arrowMm, CadDocument doc, FirmStandards std, double mpm, double g2g, Func<string, Layer> layer, out double length, out string why)
        {
            length = 0; why = "";
            var span = CourseLabelling.NearestSpan(lot, lotPick);
            if (span == null) { why = "pick a lot line"; return null; }
            var tie = Ties.Manual(corner, span.Value);
            if (tie == null) { why = "the corner is on that line"; return null; }
            length = tie.Value.Length * g2g;
            var made = Drawn(new[] { Ties.Draw(tie.Value, arrows, arrowMm, std, mpm, g2g, std.DistanceLayer) }, doc, mpm, layer).ToList();
            return new AddEntitiesCommand(owner, made, "House tie");
        }

        /// <summary>A straight (through every pick) or curvy (tip, via, end) leader with its arrowhead at the first pick.</summary>
        public static IEditCommand? Leader(BlockRecord owner, IList<Vec2> picks, bool curvy, double arrowMm, double mpm, string layerName, Func<string, Layer> layer)
        {
            if (picks.Count < 2) return null;
            var pl = curvy && picks.Count >= 3
                ? Ties.CurvyLeader(picks[0], picks[1], picks[picks.Count - 1], arrowMm * mpm, layerName)
                : Ties.StraightLeader(picks, arrowMm * mpm, layerName);
            return new AddEntitiesCommand(owner, new Entity[] { CourseLabelling.ToPolyline(pl, layer) }, curvy ? "Curvy leader" : "Leader");
        }

        /// <summary>"Draw Line of Blocks": copies of <paramref name="block"/> every <paramref name="spacing"/> along <paramref name="path"/>, turned to follow it.</summary>
        public static IEditCommand? LineOfBlocks(Insert block, Entity path, double spacing, bool rotate, out int count, out string why)
        {
            count = 0; why = "";
            if (!(path.Owner is BlockRecord owner)) { why = "not in a drawing"; return null; }
            var spans = EntityOps.SpansOf(path).ToList();
            if (path is Circle) spans = spans.Take(2).ToList();
            var spots = Ties.AlongPath(spans, spacing);
            if (spots.Count == 0) { why = "nothing fits at that spacing"; return null; }
            var made = new List<Entity>();
            var src = block.InsertPoint;
            foreach (var (at, rot) in spots)
            {
                var ins = (Insert)EntityOps.Duplicate(block);
                // Through a transform, not by setting the fields, so attributes travel with it.
                var m = Matrix4.CreateTranslation(new XYZ(at.X - src.X, at.Y - src.Y, 0));
                if (rotate)
                    m = m * Matrix4.CreateTranslation(src) * Matrix4.CreateRotationMatrix(new XYZ(0, 0, rot - block.Rotation)) * Matrix4.CreateTranslation(new XYZ(-src.X, -src.Y, -src.Z));
                EntityTransform.Apply(ins, new Transform(m));
                made.Add(ins);
            }
            count = made.Count;
            return new AddEntitiesCommand(owner, made, "Line of blocks");
        }

        /// <summary>
        /// "Create a table of multities or radial lines": a line from <paramref name="origin"/> to
        /// each target, tagged T1, T2... (numbered on from the drawing's tags) at its far end, and
        /// a table of their bearings and distances with its top-left corner at <paramref name="at"/>.
        /// </summary>
        public static IEditCommand? Multities(BlockRecord owner, Vec2 origin, IList<Vec2> targets, Vec2 at, CadDocument doc, FirmStandards std, double mpm, double g2g, Func<string, Layer> layer, out string why)
        {
            why = "";
            var real = targets.Where(t => Vec2.Distance(t, origin) > 1e-9).ToList();
            if (real.Count == 0) { why = "no points to tie to"; return null; }
            int n = Ties.NextTag("T", owner.Entities.OfType<TextEntity>().Select(t => t.Value.Trim()));
            var rows = new List<string[]>();
            var made = new List<Entity>();
            double h = std.DistanceTextMm, gap = h * std.LabelGapFactor * mpm;
            foreach (var t in real)
            {
                string tag = "T" + n++;
                rows.Add(Ties.LineRow(tag, origin, t, std, g2g));
                var line = new DraftPolyline { Layer = std.DistanceLayer };
                line.Add(origin); line.Add(t);
                made.Add(CourseLabelling.ToPolyline(line, layer));
                // The tag just past the far end, along the line.
                var u = (t - origin).Normalized();
                double r = Angles.ReadableRotation(origin, t);
                bool forward = Math.Abs(Math.Atan2(u.Y, u.X) - r) < 1e-6;
                made.Add(CourseLabelling.ToEntity(new DraftText
                {
                    Layer = std.DistanceLayer, Style = std.TextStyle("distance"), Text = tag, Position = t + u * gap, HeightMm = h, Rotation = r,
                    H = forward ? HAlign.Left : HAlign.Right, V = VAlign.Middle, Kind = TextKind.Other,
                }, doc, mpm, layer));
            }
            var (texts, rules) = Ties.Table("TIES FROM " + FormatNE(origin), Ties.TieHeadings, rows, at, h, mpm, std.AreaLayer, std.TextStyle("distance"));
            made.AddRange(rules.Select(r => (Entity)CourseLabelling.ToPolyline(r, layer)));
            made.AddRange(texts.Select(t => (Entity)CourseLabelling.ToEntity(t, doc, mpm, layer)));
            return new AddEntitiesCommand(owner, made, "Multities");
        }

        private static string FormatNE(Vec2 p) =>
            "N " + p.Y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " E " + p.X.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// Survey points from a coordinate file into the drawing: a POINT for each and its number
        /// beside it, tagged with the number and code (so clicking finds the point, as for drafted
        /// jobs), all on <paramref name="layerName"/>.
        /// </summary>
        public static IEditCommand? ImportPoints(BlockRecord owner, IEnumerable<FdDraft.Core.Job.SurveyPoint> points, string layerName, CadDocument doc, FirmStandards std, double mpm, Func<string, Layer> layer)
        {
            var made = new List<Entity>();
            var tags = new List<(Entity, int, string)>();
            double off = std.SymbolSizeMm * 0.7 * mpm;
            foreach (var p in points)
            {
                var pt = new Point(new XYZ(p.Easting, p.Northing, p.Elevation)) { Layer = layer(layerName) };
                var label = CourseLabelling.ToEntity(new DraftText
                {
                    Layer = layerName, Style = std.TextStyle("point_number"), Text = p.Name.Length > 0 ? p.Name : p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Position = new Vec2(p.Easting + off, p.Northing + off * 0.3), HeightMm = std.PointNumberTextMm, H = HAlign.Left, V = VAlign.Bottom, Kind = TextKind.PointNumber,
                }, doc, mpm, layer);
                made.Add(pt); made.Add(label);
                tags.Add((pt, p.Id, p.Code)); tags.Add((label, p.Id, ""));
            }
            if (made.Count == 0) return null;
            var cmd = new AddEntitiesCommand(owner, made, "Import points");
            // Tagged once they're in the drawing (the tags' application name is registered there).
            foreach (var (e, id, code) in tags) { PointLinks.Tag(e, id); if (code.Length > 0) PointLinks.TagCode(e, code); }
            return cmd;
        }

        /// <summary>
        /// "Generate or Add to a Line Table" / "a Curve Table": tags (L1, L2... / C1, C2...,
        /// numbered on from the drawing's existing tags) on each straight course / arc of the
        /// selection, and a table of them with its top-left corner at <paramref name="at"/>.
        /// </summary>
        public static IEditCommand? Table(bool curves, IEnumerable<Entity> selection, BlockRecord owner, Vec2 at, CadDocument doc, FirmStandards std, double mpm, double g2g, Func<string, Layer> layer, out int count, out string why)
        {
            count = 0; why = "";
            string prefix = curves ? "C" : "L";
            var existing = owner.Entities.OfType<TextEntity>().Select(t => t.Value.Trim());
            int n = Ties.NextTag(prefix, existing);
            var rows = new List<string[]>();
            var made = new List<Entity>();
            string tagLayer = curves ? std.ArcLayer : std.DistanceLayer;
            double h = (curves ? std.ArcTextMm : std.DistanceTextMm);
            double gap = h * std.LabelGapFactor * mpm;
            foreach (var e in selection)
                foreach (var s in EntityOps.SpansOf(e).Take(e is Circle && !(e is ACadSharp.Entities.Arc) ? 0 : int.MaxValue))
                {
                    if (s.IsArc != curves || Vec2.Distance(s.A, s.B) < 1e-9) continue;
                    string tag = prefix + n++;
                    DraftText t;
                    if (!curves)
                    {
                        rows.Add(Ties.LineRow(tag, s.A, s.B, std, g2g));
                        double r = Angles.ReadableRotation(s.A, s.B);
                        var up = new Vec2(-Math.Sin(r), Math.Cos(r));
                        t = new DraftText { Layer = tagLayer, Style = std.TextStyle("distance"), Text = tag, Position = (s.A + s.B) * 0.5 + up * gap, HeightMm = h, Rotation = r, H = HAlign.Center, V = VAlign.Bottom, Kind = TextKind.Other };
                    }
                    else
                    {
                        var arc = new CoreArc { Center = s.Center, Radius = s.Radius, StartAngle = Math.Atan2(s.A.Y - s.Center.Y, s.A.X - s.Center.X), Sweep = s.Sweep, Start = s.A, End = s.B };
                        rows.Add(Ties.CurveRow(tag, arc, std, g2g));
                        var m = s.Midpoint; var outward = (m - s.Center).Normalized();
                        double r = Angles.ReadableRotation(m, m + outward.Left());
                        var up = new Vec2(-Math.Sin(r), Math.Cos(r));
                        bool upOut = Vec2.Dot(up, outward) > 0;
                        t = new DraftText { Layer = tagLayer, Style = std.TextStyle("arc"), Text = tag, Position = m + outward * gap, HeightMm = h, Rotation = r, H = HAlign.Center, V = upOut ? VAlign.Bottom : VAlign.Top, Kind = TextKind.Other };
                    }
                    made.Add(CourseLabelling.ToEntity(t, doc, mpm, layer));
                }
            count = rows.Count;
            if (count == 0) { why = curves ? "no arcs in the selection" : "no straight lines in the selection"; return null; }
            var (texts, rules) = Ties.Table(curves ? "CURVE TABLE" : "LINE TABLE", curves ? Ties.CurveHeadings : Ties.LineHeadings, rows, at, h, mpm, std.AreaLayer, std.TextStyle("distance"));
            made.AddRange(rules.Select(r => (Entity)CourseLabelling.ToPolyline(r, layer)));
            made.AddRange(texts.Select(t => (Entity)CourseLabelling.ToEntity(t, doc, mpm, layer)));
            return new AddEntitiesCommand(owner, made, curves ? "Curve table" : "Line table");
        }
    }
}
