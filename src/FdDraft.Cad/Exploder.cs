using System;
using System.Collections.Generic;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using FdDraft.Core.Geometry;

namespace FdDraft.Cad.Editing
{
    /// <summary>
    /// EXPLODE: breaks a compound entity into its parts, as AutoCAD/MSCAD do - a polyline into
    /// its lines and arcs, a block reference into its own entities (placed where the block
    /// showed them, attributes as plain text), a dimension into the lines, arrowheads and text
    /// of its picture.
    /// </summary>
    public static class Exploder
    {
        /// <summary>Can <paramref name="e"/> be exploded?</summary>
        public static bool CanExplode(Entity e) =>
            (e is LwPolyline lp && lp.Vertices.Count >= 2) || (e is Polyline2D p2 && p2.Vertices.Count >= 2)
            || (e is Insert ins && ins.Block != null) || (e is Dimension d && d.Block != null);

        /// <summary>The parts <paramref name="e"/> explodes into (detached, ready to add beside it),
        /// or null when it isn't something that explodes.</summary>
        public static List<Entity>? Parts(Entity e)
        {
            switch (e)
            {
                case LwPolyline _:
                case Polyline2D _:
                {
                    if (!CanExplode(e)) return null;
                    var parts = new List<Entity>();
                    foreach (var s in EntityOps.SpansOf(e))
                    {
                        if (Vec2.Distance(s.A, s.B) < 1e-12) continue;
                        Entity part;
                        if (!s.IsArc) part = new Line(new XYZ(s.A.X, s.A.Y, 0), new XYZ(s.B.X, s.B.Y, 0));
                        else
                        {
                            double start = Math.Atan2(s.A.Y - s.Center.Y, s.A.X - s.Center.X);
                            part = SurveyDrafting.ToEntity(new FdDraft.Core.Geometry.Arc { Center = s.Center, Radius = s.Radius, StartAngle = start, Sweep = s.Sweep, Start = s.A, End = s.B }, e.Layer);
                        }
                        Inherit(part, e, e);
                        parts.Add(part);
                    }
                    return parts;
                }
                case Insert ins when ins.Block != null:
                {
                    var parts = new List<Entity>();
                    var t = ins.GetTransform();
                    foreach (var be in ins.Block.Entities)
                    {
                        if (be is AttributeDefinition) continue; // its value comes from the insert's attribute
                        var c = EntityOps.Duplicate(be);
                        EntityTransform.Apply(c, t);
                        Inherit(c, be, ins);
                        parts.Add(c);
                    }
                    foreach (var a in ins.Attributes)
                    {
                        if (a.Flags.HasFlag(AttributeFlags.Hidden) || string.IsNullOrEmpty(a.Value)) continue;
                        var text = new TextEntity
                        {
                            Value = a.Value, InsertPoint = a.InsertPoint, AlignmentPoint = a.AlignmentPoint, Height = a.Height, Rotation = a.Rotation,
                            HorizontalAlignment = a.HorizontalAlignment, VerticalAlignment = a.VerticalAlignment, WidthFactor = a.WidthFactor,
                            Style = a.Style,
                        };
                        Inherit(text, a, ins);
                        parts.Add(text);
                    }
                    return parts;
                }
                case Dimension d when d.Block != null:
                {
                    var parts = new List<Entity>();
                    foreach (var be in d.Block.Entities)
                    {
                        if (be is Point) continue; // the definition points on Defpoints
                        var c = EntityOps.Duplicate(be);
                        Inherit(c, be, d);
                        parts.Add(c);
                    }
                    return parts;
                }
            }
            return null;
        }

        /// <summary>
        /// A part takes its own layer, colour, linetype and lineweight - except where the source
        /// part left them to its parent (layer 0, ByBlock), which then come from the parent.
        /// </summary>
        private static void Inherit(Entity part, Entity source, Entity parent)
        {
            bool onZero = source.Layer == null || source.Layer.Name == "0";
            part.Layer = onZero || source == parent ? parent.Layer : source.Layer;
            part.Color = source.Color.IsByBlock || source == parent ? parent.Color : source.Color;
            part.LineType = source.LineType != null && !source.LineType.Name.Equals("ByBlock", StringComparison.OrdinalIgnoreCase) && source != parent ? source.LineType : parent.LineType;
            part.LineWeight = source.LineWeight == LineWeightType.ByBlock || source == parent ? parent.LineWeight : source.LineWeight;
            if (source == parent || parent is LwPolyline || parent is Polyline2D)
            {
                string? code = PointLinks.TaggedCode(parent);
                if (code != null) _pendingCodes.AddOrUpdate(part, code);
            }
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Entity, string> _pendingCodes =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Entity, string>();

        /// <summary>
        /// Explodes each entity that can be, in place: its parts go into the same block and the
        /// original is removed. One undo step; null when nothing in the list explodes.
        /// </summary>
        public static IEditCommand? Explode(IEnumerable<Entity> entities, out int exploded, out List<Entity> made)
        {
            exploded = 0;
            made = new List<Entity>();
            var cmds = new List<IEditCommand>();
            var removed = new List<Entity>();
            foreach (var e in entities)
            {
                if (!(e.Owner is BlockRecord owner)) continue;
                var parts = Parts(e);
                if (parts == null || parts.Count == 0) continue;
                cmds.Add(new AddEntitiesCommand(owner, parts, "Explode"));
                // Linework keeps the FD-Pro code its polyline was tagged with (needs the document).
                foreach (var p in parts) if (_pendingCodes.TryGetValue(p, out var code)) PointLinks.TagCode(p, code);
                removed.Add(e);
                made.AddRange(parts);
                exploded++;
            }
            if (exploded == 0) return null;
            cmds.Add(new RemoveEntitiesCommand(removed, "Explode"));
            return new CompositeCommand(cmds, "Explode " + exploded);
        }
    }
}
