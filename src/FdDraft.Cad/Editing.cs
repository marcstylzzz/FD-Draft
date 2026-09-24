using System;
using System.Collections.Generic;
using System.Linq;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using FdDraft.Core.Geometry;

namespace FdDraft.Cad.Editing
{
    /// <summary>One undoable change to the drawing. A command's constructor performs the
    /// change; <see cref="Undo"/> reverses it and <see cref="Redo"/> re-applies it.</summary>
    public interface IEditCommand
    {
        void Undo();
        void Redo();
        string Description { get; }
    }

    /// <summary>Adds already-built entities to a block (model space, or a layout's own block).</summary>
    public sealed class AddEntitiesCommand : IEditCommand
    {
        private readonly BlockRecord _owner;
        private readonly List<Entity> _entities;
        public string Description { get; }

        public AddEntitiesCommand(BlockRecord owner, IEnumerable<Entity> entities, string description)
        {
            _owner = owner;
            _entities = entities.ToList();
            Description = description;
            Redo();
        }

        public void Undo() { foreach (var e in _entities) _owner.Entities.Remove(e); }
        public void Redo()
        {
            foreach (var e in _entities)
            {
                _owner.Entities.Add(e);
                // FD-Draft's aligned dimensions (re)draw their own picture once they're in the document.
                if (DimensionBuilder.IsOurs(e)) DimensionBuilder.DrawPicture((Dimension)e);
            }
        }
    }

    /// <summary>Removes entities (erase). Each is put back in the block it came from on undo.</summary>
    public sealed class RemoveEntitiesCommand : IEditCommand
    {
        private readonly List<(BlockRecord Owner, Entity Entity)> _items;
        public string Description { get; }
        public int Count => _items.Count;

        public RemoveEntitiesCommand(IEnumerable<Entity> entities, string description)
        {
            Description = description;
            _items = entities
                .Select(e => (Owner: e.Owner as BlockRecord, Entity: e))
                .Where(t => t.Owner != null)
                .Select(t => (t.Owner!, t.Entity))
                .ToList();
            foreach (var (owner, e) in _items) owner.Entities.Remove(e);
        }

        public void Undo() { foreach (var (owner, e) in _items) owner.Entities.Add(e); }
        public void Redo() { foreach (var (owner, e) in _items) owner.Entities.Remove(e); }
    }

    /// <summary>Moves, rotates, or otherwise transforms entities in place (MOVE, ROTATE).</summary>
    public sealed class TransformEntitiesCommand : IEditCommand
    {
        private readonly List<Entity> _entities;
        private readonly Transform _forward;
        private readonly Transform _inverse;
        public string Description { get; }

        public TransformEntitiesCommand(IEnumerable<Entity> entities, Transform forward, Transform inverse, string description)
        {
            _entities = entities.ToList();
            _forward = forward;
            _inverse = inverse;
            Description = description;
            Apply(_forward);
        }

        public void Undo() => Apply(_inverse);
        public void Redo() => Apply(_forward);

        private void Apply(Transform t)
        {
            foreach (var e in _entities)
            {
                // A dimension's picture block is in world coordinates and doesn't follow the
                // transform on its own; FD-Draft's dimensions redraw theirs.
                if (DimensionBuilder.IsOurs(e)) DimensionBuilder.Transform((Dimension)e, t);
                else e.ApplyTransform(t);
            }
        }

        /// <summary>A pure translation by (dx, dy).</summary>
        public static TransformEntitiesCommand Move(IEnumerable<Entity> entities, double dx, double dy, string description) =>
            new TransformEntitiesCommand(entities,
                Transform.CreateTranslation(new XYZ(dx, dy, 0)),
                Transform.CreateTranslation(new XYZ(-dx, -dy, 0)),
                description);

        /// <summary>A rotation by <paramref name="angleRadians"/> about <paramref name="pivot"/>.</summary>
        public static TransformEntitiesCommand Rotate(IEnumerable<Entity> entities, XYZ pivot, double angleRadians, string description)
        {
            var toOrigin = Matrix4.CreateTranslation(new XYZ(-pivot.X, -pivot.Y, -pivot.Z));
            var back = Matrix4.CreateTranslation(pivot);
            var fwd = back * Matrix4.CreateRotationMatrix(new XYZ(0, 0, angleRadians)) * toOrigin;
            var inv = back * Matrix4.CreateRotationMatrix(new XYZ(0, 0, -angleRadians)) * toOrigin;
            return new TransformEntitiesCommand(entities, new Transform(fwd), new Transform(inv), description);
        }
    }

    /// <summary>Reassigns entities to another layer (the toolbar's current-layer picker,
    /// applied to the selection).</summary>
    public sealed class ChangeLayerCommand : IEditCommand
    {
        private readonly List<(Entity Entity, Layer? Old)> _items;
        private readonly Layer _new;
        public string Description { get; }

        public ChangeLayerCommand(IEnumerable<Entity> entities, Layer newLayer, string description)
        {
            _items = entities.Select(e => (Entity: e, Old: (Layer?)e.Layer)).ToList();
            _new = newLayer;
            Description = description;
            foreach (var (e, _) in _items) e.Layer = _new;
        }

        public void Undo() { foreach (var (e, old) in _items) if (old != null) e.Layer = old; }
        public void Redo() { foreach (var (e, _) in _items) e.Layer = _new; }
    }

    /// <summary>Retypes a single-line text entity's content (TEXT) or a multiline text
    /// entity's content (MTEXT) in place.</summary>
    public sealed class EditTextCommand : IEditCommand
    {
        private readonly TextEntity? _text;
        private readonly MText? _mtext;
        private readonly string _oldValue;
        private readonly string _newValue;
        public string Description { get; }

        public EditTextCommand(TextEntity text, string newValue, string description)
        {
            _text = text;
            _oldValue = text.Value;
            _newValue = newValue;
            Description = description;
            _text.Value = newValue;
        }

        public EditTextCommand(MText mtext, string newValue, string description)
        {
            _mtext = mtext;
            _oldValue = mtext.Value;
            _newValue = newValue;
            Description = description;
            _mtext.Value = newValue;
        }

        public void Undo() { if (_text != null) _text.Value = _oldValue; if (_mtext != null) _mtext.Value = _oldValue; }
        public void Redo() { if (_text != null) _text.Value = _newValue; if (_mtext != null) _mtext.Value = _newValue; }
    }

    /// <summary>Sets a single scalar property (a Circle/Arc's radius, a text's height or
    /// rotation, ...) via a setter delegate, undoable. One generic command instead of a
    /// bespoke class per property, since there is nothing property-specific about undo here.</summary>
    public sealed class SetPropertyCommand<T> : IEditCommand
    {
        private readonly Action<T> _set;
        private readonly T _old;
        private readonly T _new;
        public string Description { get; }

        public SetPropertyCommand(T oldValue, T newValue, Action<T> set, string description)
        {
            _old = oldValue;
            _new = newValue;
            _set = set;
            Description = description;
            _set(_new);
        }

        public void Undo() => _set(_old);
        public void Redo() => _set(_new);
    }

    /// <summary>Groups several already-applied commands (e.g. a Properties panel edit that
    /// touches more than one field, like an arc's start and end angle together) into a single
    /// undo step. Sub-commands undo in reverse order and redo in the order they were given.</summary>
    public sealed class CompositeCommand : IEditCommand
    {
        private readonly List<IEditCommand> _commands;
        public string Description { get; }

        public CompositeCommand(IEnumerable<IEditCommand> commands, string description)
        {
            _commands = commands.ToList();
            Description = description;
        }

        public void Undo() { for (int i = _commands.Count - 1; i >= 0; i--) _commands[i].Undo(); }
        public void Redo() { foreach (var c in _commands) c.Redo(); }
    }

    /// <summary>One endpoint of a Line, or one vertex of an LwPolyline/Polyline2D, that
    /// <see cref="StretchVertexCommand"/> can move independently of the rest of the entity.
    /// Found by <see cref="VertexEditing.FindCoincident"/>.</summary>
    public readonly struct VertexRef
    {
        public Entity Entity { get; }
        /// <summary>-1 = a Line's StartPoint, -2 = a Line's EndPoint, otherwise an
        /// LwPolyline/Polyline2D vertex index.</summary>
        public int Index { get; }
        public VertexRef(Entity entity, int index) { Entity = entity; Index = index; }

        public XYZ Get() => Entity switch
        {
            Line l when Index == -1 => l.StartPoint,
            Line l when Index == -2 => l.EndPoint,
            LwPolyline p => new XYZ(p.Vertices[Index].Location.X, p.Vertices[Index].Location.Y, 0),
            Polyline2D p => p.Vertices[Index].Location,
            _ => XYZ.Zero,
        };

        public void Set(XYZ p)
        {
            switch (Entity)
            {
                case Line l when Index == -1: l.StartPoint = p; break;
                case Line l when Index == -2: l.EndPoint = p; break;
                case LwPolyline lp: lp.Vertices[Index].Location = new XY(p.X, p.Y); break;
                case Polyline2D p2: p2.Vertices[Index].Location = p; break;
            }
        }
    }

    /// <summary>Finds every entity endpoint/vertex that sits at (or very near) a picked point,
    /// so a STRETCH can drag a shared survey vertex and keep every line meeting there connected -
    /// the CAD-standard "grip edit", as opposed to MOVE/ROTATE which transform whole entities.</summary>
    public static class VertexEditing
    {
        public static List<VertexRef> FindCoincident(IEnumerable<Entity> entities, XYZ point, double tolerance)
        {
            var found = new List<VertexRef>();
            bool Near(XYZ p) => Math.Abs(p.X - point.X) <= tolerance && Math.Abs(p.Y - point.Y) <= tolerance;
            foreach (var e in entities)
            {
                switch (e)
                {
                    case Line l:
                        if (Near(l.StartPoint)) found.Add(new VertexRef(l, -1));
                        if (Near(l.EndPoint)) found.Add(new VertexRef(l, -2));
                        break;
                    case LwPolyline p:
                        for (int i = 0; i < p.Vertices.Count; i++)
                            if (Near(new XYZ(p.Vertices[i].Location.X, p.Vertices[i].Location.Y, 0))) found.Add(new VertexRef(p, i));
                        break;
                    case Polyline2D p2:
                        for (int i = 0; i < p2.Vertices.Count; i++)
                            if (Near(new XYZ(p2.Vertices[i].Location.X, p2.Vertices[i].Location.Y, 0))) found.Add(new VertexRef(p2, i));
                        break;
                }
            }
            return found;
        }

        /// <summary>The LwPolyline vertex nearest <paramref name="pick"/> (for VXDEL), and how
        /// far away it is.</summary>
        public static int NearestVertex(LwPolyline poly, Vec2 pick, out double distance)
        {
            int best = -1; distance = double.MaxValue;
            for (int i = 0; i < poly.Vertices.Count; i++)
            {
                var v = poly.Vertices[i].Location;
                double d = Vec2.Distance(pick, new Vec2(v.X, v.Y));
                if (d < distance) { distance = d; best = i; }
            }
            return best;
        }

        /// <summary>The LwPolyline span nearest <paramref name="pick"/> (for VXADD): the index
        /// of the vertex it starts at, and the point on it nearest the pick - on the curve
        /// itself for an arc span, so an inserted vertex there splits the arc exactly.</summary>
        public static int NearestSpan(LwPolyline poly, Vec2 pick, out Vec2 onSpan, out double distance)
        {
            var pts = poly.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList();
            var spans = Construct.Spans(pts, poly.Vertices.Select(v => v.Bulge).ToList(), poly.IsClosed);
            int best = -1; distance = double.MaxValue; onSpan = pick;
            for (int i = 0; i < spans.Count; i++)
            {
                var q = spans[i].Project(pick);
                double d = Vec2.Distance(q, pick);
                if (d < distance) { distance = d; best = i; onSpan = q; }
            }
            return best;
        }
    }

    /// <summary>Moves one shared vertex - every Line endpoint and polyline vertex that sits at
    /// it - to a new position, keeping the entities that meet there connected (STRETCH).
    /// Anything else about the entities (their other endpoint, bulge, layer) is untouched.</summary>
    public sealed class StretchVertexCommand : IEditCommand
    {
        private readonly List<(VertexRef Ref, XYZ Old)> _items;
        private readonly XYZ _new;
        public string Description { get; }

        public StretchVertexCommand(IEnumerable<VertexRef> vertices, XYZ newPosition, string description)
        {
            _items = vertices.Select(v => (Ref: v, Old: v.Get())).ToList();
            _new = newPosition;
            Description = description;
            foreach (var (r, _) in _items) r.Set(_new);
        }

        public void Undo() { foreach (var (r, old) in _items) r.Set(old); }
        public void Redo() { foreach (var (r, _) in _items) r.Set(_new); }
    }

    /// <summary>
    /// Removes one vertex from an LwPolyline (the single-vertex partial erase, as opposed to
    /// Del erasing the whole entity). The two spans meeting there become one straight span
    /// from the previous vertex to the next; undo puts the vertex, and the previous span's
    /// bulge, back exactly.
    /// </summary>
    public sealed class DeleteVertexCommand : IEditCommand
    {
        private readonly LwPolyline _poly;
        private readonly int _index;
        private readonly LwPolyline.Vertex _removed;
        private readonly int _prev;
        private readonly double _prevBulge;
        public string Description { get; }

        /// <summary>Whether <paramref name="poly"/> can lose a vertex and still be a polyline
        /// (an open one needs 2 left, a closed one 3).</summary>
        public static bool CanDelete(LwPolyline poly) => poly.Vertices.Count > (poly.IsClosed ? 3 : 2);

        public DeleteVertexCommand(LwPolyline poly, int index, string description)
        {
            if (index < 0 || index >= poly.Vertices.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (!CanDelete(poly)) throw new InvalidOperationException("too few vertices left to delete one");
            _poly = poly;
            _index = index;
            _removed = poly.Vertices[index];
            int n = poly.Vertices.Count;
            // The span arriving at the removed vertex starts at the previous vertex; for vertex 0
            // of an open polyline there is none (-1).
            _prev = index > 0 ? index - 1 : poly.IsClosed ? n - 1 : -1;
            _prevBulge = _prev >= 0 ? poly.Vertices[_prev].Bulge : 0;
            Description = description;
            Redo();
        }

        public void Redo()
        {
            if (_prev >= 0) _poly.Vertices[_prev].Bulge = 0;
            _poly.Vertices.RemoveAt(_index);
        }

        public void Undo()
        {
            _poly.Vertices.Insert(_index, _removed);
            if (_prev >= 0) _poly.Vertices[_prev].Bulge = _prevBulge;
        }
    }

    /// <summary>
    /// Adds a vertex to an LwPolyline on the span that starts at vertex
    /// <c>afterIndex</c>. A point on an arc span splits the arc exactly (both pieces keep
    /// the curve); otherwise both pieces are straight. Undo removes it and restores the
    /// span's original bulge.
    /// </summary>
    public sealed class InsertVertexCommand : IEditCommand
    {
        private readonly LwPolyline _poly;
        private readonly int _after;
        private readonly double _oldBulge;
        private readonly double _firstBulge;
        private readonly LwPolyline.Vertex _added;
        public string Description { get; }
        /// <summary>The new vertex's index.</summary>
        public int Index => _after + 1;

        public InsertVertexCommand(LwPolyline poly, int afterIndex, XY location, string description)
        {
            int n = poly.Vertices.Count;
            int spans = poly.IsClosed ? n : n - 1;
            if (afterIndex < 0 || afterIndex >= spans) throw new ArgumentOutOfRangeException(nameof(afterIndex));
            _poly = poly;
            _after = afterIndex;
            var a = poly.Vertices[afterIndex];
            var b = poly.Vertices[(afterIndex + 1) % n];
            _oldBulge = a.Bulge;
            var span = Construct.Span.FromBulge(new Vec2(a.Location.X, a.Location.Y), new Vec2(b.Location.X, b.Location.Y), a.Bulge);
            var (first, second) = span.SplitBulges(new Vec2(location.X, location.Y));
            _firstBulge = first;
            _added = new LwPolyline.Vertex(location) { Bulge = second, StartWidth = a.StartWidth, EndWidth = a.EndWidth };
            Description = description;
            Redo();
        }

        public void Redo()
        {
            _poly.Vertices[_after].Bulge = _firstBulge;
            _poly.Vertices.Insert(_after + 1, _added);
        }

        public void Undo()
        {
            _poly.Vertices.RemoveAt(_after + 1);
            _poly.Vertices[_after].Bulge = _oldBulge;
        }
    }

    /// <summary>Linear undo/redo stack. A new command truncates any redo history past it,
    /// like every other editor.</summary>
    public sealed class UndoStack
    {
        private readonly List<IEditCommand> _done = new List<IEditCommand>();
        private int _index;

        public bool CanUndo => _index > 0;
        public bool CanRedo => _index < _done.Count;

        /// <summary>Record a command that has already been applied (its constructor performed the work).</summary>
        public void Push(IEditCommand cmd)
        {
            if (_index < _done.Count) _done.RemoveRange(_index, _done.Count - _index);
            _done.Add(cmd);
            _index++;
        }

        public string? Undo()
        {
            if (!CanUndo) return null;
            _index--;
            _done[_index].Undo();
            return _done[_index].Description;
        }

        public string? Redo()
        {
            if (!CanRedo) return null;
            var cmd = _done[_index];
            cmd.Redo();
            _index++;
            return cmd.Description;
        }

        public void Clear() { _done.Clear(); _index = 0; }
    }
}
