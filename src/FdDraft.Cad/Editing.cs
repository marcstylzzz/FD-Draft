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
