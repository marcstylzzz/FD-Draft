using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FdDraft.View.Toolbars;

namespace FdDraft.App
{
    /// <summary>
    /// Turns a toolbar icon's primitives (<see cref="ToolIcons"/>, a 24x24 grid) into a frozen
    /// vector <see cref="DrawingImage"/> in the dark-toolbar colours - crisp at any display scale.
    /// </summary>
    public static class ToolIconImage
    {
        private static readonly Dictionary<string, DrawingImage> _cache = new Dictionary<string, DrawingImage>();
        private static readonly Typeface Face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        public static DrawingImage Get(string key)
        {
            if (_cache.TryGetValue(key, out var img)) return img;
            var group = new DrawingGroup();
            // A clear 24x24 frame, so every icon lays out the same size whatever it draws.
            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
            foreach (var p in ToolIcons.Get(key))
            {
                try { group.Children.Add(Draw(p)); }
                catch (FormatException) { /* a bad path in one stroke shouldn't lose the whole icon */ }
            }
            group.ClipGeometry = new RectangleGeometry(new Rect(-0.5, -0.5, 25, 25));
            group.Freeze();
            img = new DrawingImage(group);
            img.Freeze();
            _cache[key] = img;
            return img;
        }

        private static SolidColorBrush BrushOf(IconRole role)
        {
            uint c = ToolIcons.DarkColor(role);
            var b = new SolidColorBrush(Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c));
            b.Freeze();
            return b;
        }

        private static Pen PenOf(IconPrim p)
        {
            var pen = new Pen(BrushOf(p.Role), p.Width)
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
            };
            if (p.Dashed)
            {
                // Dash lengths in WPF are multiples of the pen width; the icons use 2 on, 1.6 off.
                double w = Math.Max(p.Width, 0.1);
                pen.DashStyle = new DashStyle(new[] { 2 / w, 1.6 / w }, 0);
                pen.DashCap = PenLineCap.Flat;
            }
            pen.Freeze();
            return pen;
        }

        private static Drawing Draw(IconPrim p)
        {
            Brush? fill = p.Fill.HasValue ? BrushOf(p.Fill.Value) : null;
            switch (p.Kind)
            {
                case IconPrimKind.Line:
                    return new GeometryDrawing(null, PenOf(p), new LineGeometry(new Point(p.N[0], p.N[1]), new Point(p.N[2], p.N[3])));
                case IconPrimKind.Circle:
                    return new GeometryDrawing(fill, p.Width > 0 ? PenOf(p) : null, new EllipseGeometry(new Point(p.N[0], p.N[1]), p.N[2], p.N[2]));
                case IconPrimKind.Rect:
                    return new GeometryDrawing(fill, p.Width > 0 ? PenOf(p) : null, new RectangleGeometry(new Rect(p.N[0], p.N[1], p.N[2], p.N[3])));
                case IconPrimKind.Path:
                    return new GeometryDrawing(fill, p.Width > 0 ? PenOf(p) : null, Geometry.Parse(p.Data));
                default:
                {
                    // Text: x, y is the baseline point, anchored start/middle/end, optionally rotated about (ax, ay).
                    var ft = new FormattedText(p.Data, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, p.N[2], Brushes.Black, 1.0);
                    double x = p.N[0], y = p.N[1];
                    double w = ft.WidthIncludingTrailingWhitespace;
                    double left = p.Anchor == "middle" ? x - w / 2 : p.Anchor == "end" ? x - w : x;
                    var geo = ft.BuildGeometry(new Point(left, y - ft.Baseline));
                    if (Math.Abs(p.N[3]) > 1e-9) geo.Transform = new RotateTransform(p.N[3], p.N[4], p.N[5]);
                    return new GeometryDrawing(BrushOf(p.Role), null, geo);
                }
            }
        }
    }
}
