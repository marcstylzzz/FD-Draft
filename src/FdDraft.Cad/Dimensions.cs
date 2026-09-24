using System;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using FdDraft.Core.Geometry;

namespace FdDraft.Cad.Editing
{
    /// <summary>
    /// Real DIMENSION entities (aligned: a distance measured along the line between two
    /// points). A DWG dimension carries its own "picture" - an anonymous block (*D…) of the
    /// lines, arrowheads and text a CAD program shows - and ACadSharp's generator for that
    /// block has known faults (both arrows drawn at one end, text never turned to the
    /// dimension line), so FD-Draft draws the picture itself: extension lines, the dimension
    /// line, two filled arrowheads and the measured distance, turned to read along the line.
    /// </summary>
    public static class DimensionBuilder
    {
        public const double DefaultTextHeight = 0.2;

        /// <summary>Text heights for dimensions built but not yet drawn (a COPY/MIRROR result
        /// carries its source's height into its first picture).</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DimensionAligned, object> _pendingHeight =
            new System.Runtime.CompilerServices.ConditionalWeakTable<DimensionAligned, object>();

        /// <summary>The text height of an aligned dimension's current picture (or the default).</summary>
        public static double TextHeightOf(DimensionAligned dim) =>
            dim.Block?.Entities.OfType<MText>().Select(m => (double?)m.Height).FirstOrDefault()
            ?? (_pendingHeight.TryGetValue(dim, out var h) ? (double)h : DefaultTextHeight);

        /// <summary>A new aligned dimension through the given definition points, text height
        /// carried over - what COPY and MIRROR make of an FD-Draft dimension (a clone would
        /// share its source's picture block).</summary>
        public static DimensionAligned Rebuilt(DimensionAligned source, Vec2 p1, Vec2 p2, Vec2 linePoint)
        {
            var dim = Aligned(p1, p2, linePoint);
            dim.Text = source.Text;
            dim.Layer = source.Layer;
            _pendingHeight.AddOrUpdate(dim, TextHeightOf(source));
            return dim;
        }

        /// <summary>
        /// An aligned dimension from <paramref name="p1"/> to <paramref name="p2"/> with its
        /// dimension line through <paramref name="linePoint"/> (the side and distance it
        /// stands off). Its text is the measured distance to <paramref name="decimals"/> places.
        /// Add it to a block with <see cref="AddDimensionCommand"/>, which also draws its picture.
        /// </summary>
        public static DimensionAligned Aligned(Vec2 p1, Vec2 p2, Vec2 linePoint, int decimals = 3)
        {
            var u = (p2 - p1).Normalized();
            var n = u.Left();
            double off = Vec2.Dot(linePoint - p1, n);
            var def = p2 + n * off;
            var dim = new DimensionAligned(new XYZ(p1.X, p1.Y, 0), new XYZ(p2.X, p2.Y, 0))
            {
                DefinitionPoint = new XYZ(def.X, def.Y, 0),
                Text = Vec2.Distance(p1, p2).ToString("F" + Math.Max(0, decimals), System.Globalization.CultureInfo.InvariantCulture),
                Style = DimensionStyle.Default,
            };
            // Keeps the text travelling with MOVE/ROTATE (ACadSharp only transforms
            // TextMiddlePoint for a user-placed text); FD-Draft always sets it explicitly anyway.
            dim.IsTextUserDefinedLocation = true;
            dim.TextMiddlePoint = new XYZ(0, 0, 0);
            return dim;
        }

        /// <summary>
        /// (Re)draws an aligned dimension's picture block from its own definition points -
        /// after it is created, and after MOVE/ROTATE (the block is in world coordinates, so it
        /// does not follow a transform on its own). The dimension must already be in a document
        /// so its anonymous block is registered. Text height is kept from the existing picture
        /// when there is one.
        /// </summary>
        public static void DrawPicture(DimensionAligned dim, double? textHeight = null)
        {
            double h = textHeight ?? TextHeightOf(dim);
            if (h <= 0) h = DefaultTextHeight;

            var p1 = new Vec2(dim.FirstPoint.X, dim.FirstPoint.Y);
            var p2 = new Vec2(dim.SecondPoint.X, dim.SecondPoint.Y);
            var d2 = new Vec2(dim.DefinitionPoint.X, dim.DefinitionPoint.Y);
            var shift = d2 - p2;               // from the measured points out to the dimension line
            var d1 = p1 + shift;
            double standOff = shift.Length;
            var outward = standOff > 1e-12 ? shift * (1 / standOff) : (p2 - p1).Normalized().Left();

            double rot = Angles.ReadableRotation(p1, p2);
            var up = new Vec2(-Math.Sin(rot), Math.Cos(rot));
            var mid = (d1 + d2) * 0.5;
            var textAt = mid + up * (h * 0.5 + h * 0.6);
            dim.TextMiddlePoint = new XYZ(textAt.X, textAt.Y, 0);
            dim.TextRotation = 0;

            // Registers (or clears) the dimension's own *D block in the document. ACadSharp's
            // generator fills it; everything it put there is replaced below.
            dim.UpdateBlock();
            var block = dim.Block;
            block.Entities.Clear();

            XYZ W(Vec2 v) => new XYZ(v.X, v.Y, 0);
            double gap = h * 0.3, overshoot = h * 0.6;
            // Extension lines: from just off each measured point to just past the dimension line.
            if (standOff > gap)
            {
                block.Entities.Add(new Line(W(p1 + outward * gap), W(d1 + outward * overshoot)));
                block.Entities.Add(new Line(W(p2 + outward * gap), W(d2 + outward * overshoot)));
            }
            block.Entities.Add(new Line(W(d1), W(d2)));
            var along = (d2 - d1).Normalized();
            block.Entities.Add(Arrow(d1, along, h));        // pointing at the first extension line
            block.Entities.Add(Arrow(d2, along * -1, h));   // and at the second
            block.Entities.Add(new MText
            {
                Value = dim.Text is { Length: > 0 } t ? t : dim.Measurement.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                InsertPoint = W(textAt), Height = h, AttachmentPoint = AttachmentPointType.MiddleCenter,
                AlignmentPoint = new XYZ(Math.Cos(rot), Math.Sin(rot), 0),
            });
            // AutoCAD's definition points, on Defpoints so they never plot.
            var defpoints = dim.Document?.Layers.TryGetValue(Layer.DefpointsName, out var dl) == true ? dl : Layer.Defpoints;
            foreach (var p in new[] { p1, p2, d2 })
                block.Entities.Add(new Point(W(p)) { Layer = defpoints });
        }

        /// <summary>A filled arrowhead with its tip at <paramref name="tip"/>, its body running
        /// back along <paramref name="body"/> (a unit vector).</summary>
        private static Solid Arrow(Vec2 tip, Vec2 body, double size)
        {
            var n = body.Left() * (size / 6);
            var back = tip + body * size;
            return new Solid
            {
                FirstCorner = new XYZ(tip.X, tip.Y, 0),
                SecondCorner = new XYZ(back.X + n.X, back.Y + n.Y, 0),
                ThirdCorner = new XYZ(back.X - n.X, back.Y - n.Y, 0),
                FourthCorner = new XYZ(back.X - n.X, back.Y - n.Y, 0),
            };
        }
    }

    /// <summary>Adds a dimension to a block and draws its picture; redo re-draws it, since
    /// taking a dimension out of the document detaches its picture block.</summary>
    public sealed class AddDimensionCommand : IEditCommand
    {
        private readonly BlockRecord _owner;
        private readonly DimensionAligned _dim;
        private readonly double _height;
        public string Description { get; }

        public AddDimensionCommand(BlockRecord owner, DimensionAligned dim, double textHeight, string description)
        {
            _owner = owner; _dim = dim; _height = textHeight; Description = description;
            Redo();
        }

        public void Redo()
        {
            _owner.Entities.Add(_dim);
            DimensionBuilder.DrawPicture(_dim, _height);
        }

        public void Undo() => _owner.Entities.Remove(_dim);
    }
}
