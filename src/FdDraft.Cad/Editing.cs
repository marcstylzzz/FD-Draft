using System;
using System.Collections.Generic;
using System.Linq;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;

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
            foreach (var e in _entities) _owner.Entities.Add(e);
        }

        public void Undo() { foreach (var e in _entities) _owner.Entities.Remove(e); }
        public void Redo() { foreach (var e in _entities) _owner.Entities.Add(e); }
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
            foreach (var e in _entities) e.ApplyTransform(_forward);
        }

        public void Undo() { foreach (var e in _entities) e.ApplyTransform(_inverse); }
        public void Redo() { foreach (var e in _entities) e.ApplyTransform(_forward); }

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
