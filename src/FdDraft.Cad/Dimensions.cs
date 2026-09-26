using System;
using System.Globalization;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using FdDraft.Core.Geometry;

namespace FdDraft.Cad.Editing
{
    /// <summary>
    /// Real DIMENSION entities: aligned (a distance measured along the line between two
    /// points), linear (the same measured horizontally, vertically or along a given angle)
    /// and radius (an arc's or circle's radius). A DWG dimension carries its own "picture" -
    /// an anonymous block (*D…) of the lines, arrowheads and text a CAD program shows - and
    /// ACadSharp's generator for that block has known faults (both arrows drawn at one end,
    /// text never turned to the dimension line), so FD-Draft draws the picture itself.
    /// </summary>
    public static class DimensionBuilder
    {
        public const double DefaultTextHeight = 0.2;

        /// <summary>Text heights for dimensions built but not yet drawn (a COPY/MIRROR result
        /// carries its source's height into its first picture).</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Dimension, object> _pendingHeight =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Dimension, object>();

        /// <summary>The dimension kinds whose picture FD-Draft draws (exact types - a template's
        /// angular, diameter or ordinate dimensions keep the picture they came with).</summary>
        public static bool IsOurs(Entity e)
        {
            var t = e.GetType();
            return t == typeof(DimensionAligned) || t == typeof(DimensionLinear) || t == typeof(DimensionRadius)
                || t == typeof(DimensionDiameter) || t == typeof(DimensionAngular3Pt);
        }

        /// <summary>The text height of a dimension's current picture (or the default).</summary>
        public static double TextHeightOf(Dimension dim) =>
            dim.Block?.Entities.OfType<MText>().Select(m => (double?)m.Height).FirstOrDefault()
            ?? (_pendingHeight.TryGetValue(dim, out var h) ? (double)h : DefaultTextHeight);

        private static string Fmt(double v, int decimals) => v.ToString("F" + Math.Max(0, decimals), CultureInfo.InvariantCulture);
        private static XYZ W(Vec2 v) => new XYZ(v.X, v.Y, 0);
        private static Vec2 V(XYZ p) => new Vec2(p.X, p.Y);

        private static T Prepare<T>(T dim) where T : Dimension
        {
            dim.Style = DimensionStyle.Default;
            // Keeps the text travelling with MOVE/ROTATE (ACadSharp only transforms
            // TextMiddlePoint for a user-placed text); FD-Draft always sets it explicitly anyway.
            dim.IsTextUserDefinedLocation = true;
            dim.TextMiddlePoint = XYZ.Zero;
            return dim;
        }

        /// <summary>
        /// An aligned dimension from <paramref name="p1"/> to <paramref name="p2"/> with its
        /// dimension line through <paramref name="linePoint"/> (the side and distance it
        /// stands off). Its text is the measured distance to <paramref name="decimals"/> places.
        /// Add it with <see cref="AddDimensionCommand"/> (or any <c>AddEntitiesCommand</c>),
        /// which also draws its picture.
        /// </summary>
        public static DimensionAligned Aligned(Vec2 p1, Vec2 p2, Vec2 linePoint, int decimals = 3)
        {
            var n = (p2 - p1).Normalized().Left();
            var def = p2 + n * Vec2.Dot(linePoint - p1, n);
            var dim = Prepare(new DimensionAligned(W(p1), W(p2)) { DefinitionPoint = W(def) });
            dim.Text = Fmt(Vec2.Distance(p1, p2), decimals);
            return dim;
        }

        /// <summary>
        /// A linear dimension measuring p1→p2 along <paramref name="rotation"/> (radians, CCW
        /// from east: 0 horizontal, π/2 vertical), its dimension line through
        /// <paramref name="linePoint"/>. With no rotation given it is horizontal or vertical
        /// the way CAD programs decide: horizontal when the line is placed above or below both
        /// points, vertical otherwise.
        /// </summary>
        public static DimensionLinear Linear(Vec2 p1, Vec2 p2, Vec2 linePoint, double? rotation = null, int decimals = 3)
        {
            double r = rotation ?? (linePoint.Y > Math.Max(p1.Y, p2.Y) || linePoint.Y < Math.Min(p1.Y, p2.Y) ? 0 : Math.PI / 2);
            var u = new Vec2(Math.Cos(r), Math.Sin(r));
            var def = linePoint + u * Vec2.Dot(p2 - linePoint, u);
            var dim = Prepare(new DimensionLinear { FirstPoint = W(p1), SecondPoint = W(p2), DefinitionPoint = W(def), Rotation = r });
            dim.Text = Fmt(Math.Abs(Vec2.Dot(p2 - p1, u)), decimals);
            return dim;
        }

        /// <summary>A radius dimension for a circle/arc centred at <paramref name="center"/>,
        /// pointing at the curve in the direction of <paramref name="toward"/>; text "R" and the
        /// radius.</summary>
        public static DimensionRadius Radius(Vec2 center, double radius, Vec2 toward, int decimals = 3)
        {
            var dir = (toward - center).Normalized();
            if (dir.Length < 1e-12) dir = new Vec2(1, 0);
            var dim = Prepare(new DimensionRadius { DefinitionPoint = W(center), AngleVertex = W(center + dir * radius) });
            dim.Text = "R" + Fmt(radius, decimals);
            return dim;
        }

        /// <summary>A diameter dimension across a circle/arc centred at <paramref name="center"/>,
        /// running through it in the direction of <paramref name="toward"/>; text "Ø" and the
        /// diameter.</summary>
        public static DimensionDiameter Diameter(Vec2 center, double radius, Vec2 toward, int decimals = 3)
        {
            var dir = (toward - center).Normalized();
            if (dir.Length < 1e-12) dir = new Vec2(1, 0);
            var dim = Prepare(new DimensionDiameter { DefinitionPoint = W(center - dir * radius), AngleVertex = W(center + dir * radius) });
            dim.Text = "%%c" + Fmt(2 * radius, decimals);
            return dim;
        }

        /// <summary>
        /// An angular dimension at <paramref name="vertex"/> between the rays through
        /// <paramref name="p1"/> and <paramref name="p2"/>, its arc through
        /// <paramref name="arcPoint"/> - which also picks which of the two angles between the
        /// rays is measured (the one the arc point lies in). Text is degrees, minutes and
        /// seconds, as a surveyor writes an angle.
        /// </summary>
        public static DimensionAngular3Pt Angular(Vec2 vertex, Vec2 p1, Vec2 p2, Vec2 arcPoint)
        {
            var dim = Prepare(new DimensionAngular3Pt { AngleVertex = W(vertex), FirstPoint = W(p1), SecondPoint = W(p2), DefinitionPoint = W(arcPoint) });
            var (_, sweep) = AngularSweep(vertex, p1, p2, arcPoint);
            dim.Text = Dms(sweep);
            return dim;
        }

        /// <summary>The start angle and CCW sweep of an angular dimension's arc: from ray 1 to
        /// ray 2 if the arc point lies that way round, otherwise from ray 2 to ray 1.</summary>
        public static (double Start, double Sweep) AngularSweep(Vec2 vertex, Vec2 p1, Vec2 p2, Vec2 arcPoint)
        {
            double a1 = Math.Atan2(p1.Y - vertex.Y, p1.X - vertex.X);
            double a2 = Math.Atan2(p2.Y - vertex.Y, p2.X - vertex.X);
            double ap = Math.Atan2(arcPoint.Y - vertex.Y, arcPoint.X - vertex.X);
            double s12 = Angles.Normalize2Pi(a2 - a1);
            return Angles.Normalize2Pi(ap - a1) <= s12 ? (a1, s12) : (a2, Angles.TwoPi - s12);
        }

        /// <summary>An angle as D°MM'SS" (degrees as %%d for SHX fonts), seconds carried so
        /// 60" never prints.</summary>
        public static string Dms(double radians)
        {
            long total = (long)Math.Round(Math.Abs(radians) * 180 / Math.PI * 3600, MidpointRounding.AwayFromZero);
            return (total / 3600).ToString(CultureInfo.InvariantCulture) + "%%d" + (total % 3600 / 60).ToString("00", CultureInfo.InvariantCulture) + "'" + (total % 60).ToString("00", CultureInfo.InvariantCulture) + "\"";
        }

        /// <summary>
        /// A new dimension of the same kind with every definition point sent through
        /// <paramref name="map"/> (a translation for COPY, a reflection for MIRROR), text and
        /// text height carried over - a clone would share its source's picture block.
        /// </summary>
        public static Dimension Remapped(Dimension source, Func<Vec2, Vec2> map)
        {
            Dimension dim;
            switch (source)
            {
                case DimensionLinear l:
                {
                    var p0 = V(l.FirstPoint);
                    var dir = map(p0 + new Vec2(Math.Cos(l.Rotation), Math.Sin(l.Rotation))) - map(p0);
                    dim = Prepare(new DimensionLinear
                    {
                        FirstPoint = W(map(p0)), SecondPoint = W(map(V(l.SecondPoint))), DefinitionPoint = W(map(V(l.DefinitionPoint))),
                        Rotation = Math.Atan2(dir.Y, dir.X),
                    });
                    break;
                }
                case DimensionAligned a:
                    dim = Prepare(new DimensionAligned(W(map(V(a.FirstPoint))), W(map(V(a.SecondPoint)))) { DefinitionPoint = W(map(V(a.DefinitionPoint))) });
                    break;
                case DimensionRadius r:
                    dim = Prepare(new DimensionRadius { DefinitionPoint = W(map(V(r.DefinitionPoint))), AngleVertex = W(map(V(r.AngleVertex))) });
                    break;
                case DimensionDiameter d:
                    dim = Prepare(new DimensionDiameter { DefinitionPoint = W(map(V(d.DefinitionPoint))), AngleVertex = W(map(V(d.AngleVertex))) });
                    break;
                case DimensionAngular3Pt g:
                    dim = Prepare(new DimensionAngular3Pt
                    {
                        AngleVertex = W(map(V(g.AngleVertex))), FirstPoint = W(map(V(g.FirstPoint))),
                        SecondPoint = W(map(V(g.SecondPoint))), DefinitionPoint = W(map(V(g.DefinitionPoint))),
                    });
                    break;
                default:
                    throw new ArgumentException("not an FD-Draft dimension kind", nameof(source));
            }
            dim.Text = source.Text;
            dim.Layer = source.Layer;
            _pendingHeight.AddOrUpdate(dim, TextHeightOf(source));
            return dim;
        }

        /// <summary>
        /// Applies a MOVE/ROTATE transform to one of FD-Draft's dimensions and redraws its
        /// picture. A linear dimension's measuring angle is turned with it (ACadSharp leaves
        /// <see cref="DimensionLinear.Rotation"/> alone).
        /// </summary>
        public static void Transform(Dimension dim, Transform t)
        {
            if (dim is DimensionLinear l)
            {
                var p0 = l.FirstPoint;
                var tip = t.ApplyTransform(p0 + new XYZ(Math.Cos(l.Rotation), Math.Sin(l.Rotation), 0)) - t.ApplyTransform(p0);
                dim.ApplyTransform(t);
                l.Rotation = Math.Atan2(tip.Y, tip.X);
            }
            else dim.ApplyTransform(t);
            if (dim.Document != null) DrawPicture(dim);
        }

        /// <summary>
        /// (Re)draws a dimension's picture block from its own definition points - after it is
        /// created, and after MOVE/ROTATE (the block is in world coordinates, so it does not
        /// follow a transform on its own). The dimension must already be in a document so its
        /// anonymous block is registered. Text height is kept from the existing picture when
        /// there is one.
        /// </summary>
        public static void DrawPicture(Dimension dim, double? textHeight = null)
        {
            double h = textHeight ?? TextHeightOf(dim);
            if (h <= 0) h = DefaultTextHeight;
            switch (dim)
            {
                case DimensionLinear l:
                {
                    var u = new Vec2(Math.Cos(l.Rotation), Math.Sin(l.Rotation));
                    var p1 = V(l.FirstPoint); var p2 = V(l.SecondPoint); var d2 = V(l.DefinitionPoint);
                    var d1 = d2 + u * Vec2.Dot(p1 - d2, u);
                    DrawDistancePicture(dim, p1, p2, d1, d2, h, Math.Abs(Vec2.Dot(p2 - p1, u)));
                    break;
                }
                case DimensionAligned a:
                {
                    var p1 = V(a.FirstPoint); var p2 = V(a.SecondPoint); var d2 = V(a.DefinitionPoint);
                    DrawDistancePicture(dim, p1, p2, p1 + (d2 - p2), d2, h, Vec2.Distance(p1, p2));
                    break;
                }
                case DimensionRadius r:
                    DrawRadiusPicture(r, h);
                    break;
                case DimensionDiameter d:
                    DrawDiameterPicture(d, h);
                    break;
                case DimensionAngular3Pt g:
                    DrawAngularPicture(g, h);
                    break;
            }
        }

        /// <summary>The picture for a diameter: a line right across the curve with an arrowhead
        /// on it at each end, the "Ø…" text above its middle.</summary>
        private static void DrawDiameterPicture(DimensionDiameter dim, double h)
        {
            var a = V(dim.DefinitionPoint); var b = V(dim.AngleVertex);
            if (Vec2.Distance(a, b) < 1e-12) b = a + new Vec2(1e-9, 0);
            double rot = Angles.ReadableRotation(a, b);
            var up = new Vec2(-Math.Sin(rot), Math.Cos(rot));
            var textAt = (a + b) * 0.5 + up * (h * 0.5 + h * 0.6);
            Placed(dim, ref textAt, ref rot);
            dim.TextMiddlePoint = W(textAt);
            dim.TextRotation = 0;
            var block = FreshBlock(dim);
            block.Entities.Add(new Line(W(a), W(b)));
            var along = (b - a).Normalized();
            block.Entities.Add(Arrow(a, along, h));
            block.Entities.Add(Arrow(b, along * -1, h));
            block.Entities.Add(Label(dim, textAt, rot, h, "%%c" + Fmt(Vec2.Distance(a, b), 3)));
            AddDefpoints(dim, block, a, b);
        }

        /// <summary>The picture for an angle: an arc about the vertex through the placed point,
        /// between the two rays, with an arrowhead at each end and the angle's text just
        /// outside its middle; extension lines where the arc lies beyond a leg's picked point.</summary>
        private static void DrawAngularPicture(DimensionAngular3Pt dim, double h)
        {
            var v = V(dim.AngleVertex); var p1 = V(dim.FirstPoint); var p2 = V(dim.SecondPoint); var ap = V(dim.DefinitionPoint);
            double r = Math.Max(Vec2.Distance(v, ap), 1e-9);
            var (start, sweep) = AngularSweep(v, p1, p2, ap);
            var block = FreshBlock(dim);
            block.Entities.Add(new ACadSharp.Entities.Arc { Center = W(v), Radius = r, StartAngle = Angles.Normalize2Pi(start), EndAngle = Angles.Normalize2Pi(start + sweep) });
            Vec2 On(double a) => new Vec2(v.X + r * Math.Cos(a), v.Y + r * Math.Sin(a));
            var e1 = On(start); var e2 = On(start + sweep);
            // Arrow bodies run back along the arc's tangent, into the arc.
            block.Entities.Add(Arrow(e1, new Vec2(-Math.Sin(start), Math.Cos(start)), h));
            block.Entities.Add(Arrow(e2, new Vec2(Math.Sin(start + sweep), -Math.Cos(start + sweep)), h));
            double gap = h * 0.3, overshoot = h * 0.6;
            foreach (var (leg, end) in new[] { (p1, Vec2.Distance(e1, p1) < Vec2.Distance(e2, p1) ? e1 : e2), (p2, Vec2.Distance(e1, p2) < Vec2.Distance(e2, p2) ? e1 : e2) })
            {
                var dir = (end - v).Normalized();
                double legLen = Vec2.Distance(v, leg);
                if (legLen + gap < r) block.Entities.Add(new Line(W(leg + dir * gap), W(end + dir * overshoot)));
            }
            double mid = start + sweep / 2;
            var outward = new Vec2(Math.Cos(mid), Math.Sin(mid));
            double rot = Angles.ReadableRotation(On(mid), On(mid) + outward.Left());
            var textAt = On(mid) + outward * (h * 0.5 + h * 0.6);
            Placed(dim, ref textAt, ref rot);
            dim.TextMiddlePoint = W(textAt);
            dim.TextRotation = 0;
            block.Entities.Add(Label(dim, textAt, rot, h, Dms(sweep)));
            AddDefpoints(dim, block, v, p1, p2, ap);
        }

        /// <summary>The picture for a distance: extension lines from the measured points to the
        /// dimension line d1–d2, the line itself with an arrowhead at each end, and the text
        /// above it, turned to read along it.</summary>
        private static void DrawDistancePicture(Dimension dim, Vec2 p1, Vec2 p2, Vec2 d1, Vec2 d2, double h, double measured)
        {
            if (Vec2.Distance(d1, d2) < 1e-12) d2 = d1 + new Vec2(1e-9, 0);
            double rot = Angles.ReadableRotation(d1, d2);
            var up = new Vec2(-Math.Sin(rot), Math.Cos(rot));
            var textAt = (d1 + d2) * 0.5 + up * (h * 0.5 + h * 0.6);
            Placed(dim, ref textAt, ref rot);
            dim.TextMiddlePoint = W(textAt);
            dim.TextRotation = 0;

            var block = FreshBlock(dim);
            double gap = h * 0.3, overshoot = h * 0.6;
            // Extension lines: from just off each measured point to just past the dimension line.
            foreach (var (p, d) in new[] { (p1, d1), (p2, d2) })
            {
                double len = Vec2.Distance(p, d);
                if (len <= gap) continue;
                var outward = (d - p) * (1 / len);
                block.Entities.Add(new Line(W(p + outward * gap), W(d + outward * overshoot)));
            }
            block.Entities.Add(new Line(W(d1), W(d2)));
            var along = (d2 - d1).Normalized();
            block.Entities.Add(Arrow(d1, along, h));        // pointing at the first extension line
            block.Entities.Add(Arrow(d2, along * -1, h));   // and at the second
            block.Entities.Add(Label(dim, textAt, rot, h, Fmt(measured, 3)));
            AddDefpoints(dim, block, p1, p2, d2);
        }

        /// <summary>The picture for a radius: a line from the centre out to the curve with an
        /// arrowhead on the curve, the "R…" text above its middle.</summary>
        private static void DrawRadiusPicture(DimensionRadius dim, double h)
        {
            var c = V(dim.DefinitionPoint); var q = V(dim.AngleVertex);
            if (Vec2.Distance(c, q) < 1e-12) q = c + new Vec2(1e-9, 0);
            double rot = Angles.ReadableRotation(c, q);
            var up = new Vec2(-Math.Sin(rot), Math.Cos(rot));
            var textAt = (c + q) * 0.5 + up * (h * 0.5 + h * 0.6);
            Placed(dim, ref textAt, ref rot);
            dim.TextMiddlePoint = W(textAt);
            dim.TextRotation = 0;

            var block = FreshBlock(dim);
            block.Entities.Add(new Line(W(c), W(q)));
            block.Entities.Add(Arrow(q, (c - q).Normalized(), h));
            block.Entities.Add(Label(dim, textAt, rot, h, "R" + Fmt(Vec2.Distance(c, q), 3)));
            AddDefpoints(dim, block, c, q);
        }

        private sealed class DefaultText { public Vec2 At; public double Rot; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Dimension, DefaultText> _defaultText =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Dimension, DefaultText>();

        /// <summary>Moves the text from where the picture would put it by the dimension's own text
        /// offset (DIMTEDIT / DIMROTATE), kept in FD-Draft's extended data along/across the text's
        /// normal direction so it travels with the dimension through MOVE and ROTATE.</summary>
        private static void Placed(Dimension dim, ref Vec2 at, ref double rot)
        {
            _defaultText.AddOrUpdate(dim, new DefaultText { At = at, Rot = rot });
            var (dx, dy, dr) = TextOffset(dim);
            var u = new Vec2(Math.Cos(rot), Math.Sin(rot));
            at = at + u * dx + u.Left() * dy;
            rot += dr;
        }

        private const string TextDx = "DIMTEXTDX", TextDy = "DIMTEXTDY", TextDr = "DIMTEXTROT";

        /// <summary>The dimension's text offset from its normal place: along and across the text's
        /// normal direction, and extra rotation (radians). All zero for untouched text.</summary>
        public static (double Dx, double Dy, double DRot) TextOffset(Dimension dim)
        {
            double R(string k) => PointLinks.Get(dim, k) is ACadSharp.XData.ExtendedDataReal r ? r.Value : 0;
            return (R(TextDx), R(TextDy), R(TextDr));
        }

        private static void WriteOffset(Dimension dim, (double Dx, double Dy, double DRot) o)
        {
            PointLinks.Set(dim, TextDx, new ACadSharp.XData.ExtendedDataReal(o.Dx));
            PointLinks.Set(dim, TextDy, new ACadSharp.XData.ExtendedDataReal(o.Dy));
            PointLinks.Set(dim, TextDr, new ACadSharp.XData.ExtendedDataReal(o.DRot));
        }

        /// <summary>
        /// Moves a dimension's text to <paramref name="at"/> and/or turns it to
        /// <paramref name="rotation"/> (radians, absolute); both null puts it back home. Undoable;
        /// the picture is redrawn. Only FD-Draft's own dimension kinds (<see cref="IsOurs"/>).
        /// </summary>
        public static IEditCommand? PlaceText(Dimension dim, Vec2? at, double? rotation, bool home, string description)
        {
            if (!IsOurs(dim) || dim.Document == null) return null;
            var old = TextOffset(dim);
            if (!_defaultText.TryGetValue(dim, out var def)) { DrawPicture(dim); if (!_defaultText.TryGetValue(dim, out def)) return null; }
            var u = new Vec2(Math.Cos(def.Rot), Math.Sin(def.Rot));
            var nw = home ? (0.0, 0.0, 0.0) : old;
            if (at.HasValue) { var d = at.Value - def.At; nw.Item1 = Vec2.Dot(d, u); nw.Item2 = Vec2.Dot(d, u.Left()); }
            if (rotation.HasValue) nw.Item3 = rotation.Value - def.Rot;
            return new SetPropertyCommand<(double, double, double)>(old, nw, v => { WriteOffset(dim, v); DrawPicture(dim); }, description);
        }

        /// <summary>
        /// Replaces a dimension's text (DIMEDIT New): "&lt;&gt;" stands for the measurement, empty
        /// goes back to the measurement alone. Undoable; the picture is redrawn.
        /// </summary>
        public static IEditCommand? SetText(Dimension dim, string text, int decimals, string description)
        {
            if (!IsOurs(dim) || dim.Document == null) return null;
            string measured = dim is DimensionAngular3Pt ? Dms(dim.Measurement)
                : (dim is DimensionRadius ? "R" : dim is DimensionDiameter ? "%%c" : "") + Fmt(dim.Measurement, decimals);
            string value = text.Length == 0 ? measured : text.Replace("<>", measured);
            string old = dim.Text ?? "";
            return new SetPropertyCommand<string>(old, value, v => { dim.Text = v; DrawPicture(dim); }, description);
        }

        /// <summary>Registers (or clears) the dimension's own *D block in the document.
        /// ACadSharp's generator fills it; everything it put there is replaced.</summary>
        private static BlockRecord FreshBlock(Dimension dim)
        {
            dim.UpdateBlock();
            dim.Block.Entities.Clear();
            return dim.Block;
        }

        private static MText Label(Dimension dim, Vec2 at, double rot, double h, string measured) => new MText
        {
            Value = dim.Text is { Length: > 0 } t ? t : measured,
            InsertPoint = W(at), Height = h, AttachmentPoint = AttachmentPointType.MiddleCenter,
            AlignmentPoint = new XYZ(Math.Cos(rot), Math.Sin(rot), 0),
        };

        /// <summary>AutoCAD's definition points, on Defpoints so they never plot.</summary>
        private static void AddDefpoints(Dimension dim, BlockRecord block, params Vec2[] pts)
        {
            var defpoints = dim.Document?.Layers.TryGetValue(Layer.DefpointsName, out var dl) == true ? dl : Layer.Defpoints;
            foreach (var p in pts) block.Entities.Add(new Point(W(p)) { Layer = defpoints });
        }

        /// <summary>A filled arrowhead with its tip at <paramref name="tip"/>, its body running
        /// back along <paramref name="body"/> (a unit vector).</summary>
        private static Solid Arrow(Vec2 tip, Vec2 body, double size)
        {
            var n = body.Left() * (size / 6);
            var back = tip + body * size;
            return new Solid
            {
                FirstCorner = W(tip),
                SecondCorner = new XYZ(back.X + n.X, back.Y + n.Y, 0),
                ThirdCorner = new XYZ(back.X - n.X, back.Y - n.Y, 0),
                FourthCorner = new XYZ(back.X - n.X, back.Y - n.Y, 0),
            };
        }
    }

    /// <summary>Adds a dimension to a block and draws its picture at a given text height;
    /// redo re-draws it, since taking a dimension out of the document detaches its block.</summary>
    public sealed class AddDimensionCommand : IEditCommand
    {
        private readonly BlockRecord _owner;
        private readonly Dimension _dim;
        private readonly double _height;
        public string Description { get; }

        public AddDimensionCommand(BlockRecord owner, Dimension dim, double textHeight, string description)
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
