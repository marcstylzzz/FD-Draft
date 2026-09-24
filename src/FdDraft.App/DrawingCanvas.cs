using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;
using FdDraft.View;
using WPoint = System.Windows.Point;
using WRect = System.Windows.Rect;

namespace FdDraft.App
{
    /// <summary>
    /// The drawing area. Paints a <see cref="Scene"/> (model space, or a sheet with
    /// model space through its viewports), zooms at the cursor with the wheel, pans
    /// with the middle button (or left button with Shift), snaps to survey points and
    /// line ends, and reports picks to the window.
    /// </summary>
    public sealed class DrawingCanvas : FrameworkElement
    {
        private Scene? _scene;
        private readonly Dictionary<uint, Pen> _pens = new Dictionary<uint, Pen>();
        private readonly Dictionary<uint, Brush> _brushes = new Dictionary<uint, Brush>();
        private readonly Dictionary<Prim, FormattedText> _texts = new Dictionary<Prim, FormattedText>();
        private readonly Typeface _typeface = new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private const double EmSize = 100;
        private const double CapRatio = 0.716; // Arial cap height / em
        private bool _panning;
        private WPoint _panFrom;
        private SnapPoint? _snap;
        private WPoint _mouse;
        private bool _fitted;

        public ViewTransform View { get; } = new ViewTransform();
        public bool SnapEnabled { get; set; } = true;
        /// <summary>First point of a two-point tool (inverse): a rubber band is drawn from it.</summary>
        public Vec2? RubberFrom { get; set; }

        /// <summary>Scene point under the cursor (snapped if a snap is active) and the snap, if any.</summary>
        public event Action<Vec2, SnapPoint?>? CursorMoved;
        public event Action<Vec2>? Picked;

        public DrawingCanvas()
        {
            Focusable = true;
            ClipToBounds = true;
            Cursor = Cursors.Cross;
        }

        public Scene? Scene
        {
            get => _scene;
            set
            {
                _scene = value;
                _texts.Clear();
                InvalidateVisual();
            }
        }

        public void ZoomExtents()
        {
            if (_scene == null) return;
            UpdateSize();
            if (ActualWidth > 1) _fitted = true;
            View.Fit(_scene.Bounds, 0.03);
            InvalidateVisual();
        }

        public void ZoomTo(Vec2 center, double pixelsPerUnit)
        {
            View.Center = center;
            View.Zoom = pixelsPerUnit;
            InvalidateVisual();
        }

        private void UpdateSize()
        {
            View.ScreenWidth = Math.Max(1, ActualWidth);
            View.ScreenHeight = Math.Max(1, ActualHeight);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            // Keep the same scene point in the middle when the window is resized.
            UpdateSize();
            if (!_fitted && _scene != null) { ZoomExtents(); _fitted = true; }
            InvalidateVisual();
        }

        // ---- painting -------------------------------------------------------------------------

        protected override void OnRender(DrawingContext dc)
        {
            UpdateSize();
            var screen = new WRect(0, 0, ActualWidth, ActualHeight);
            if (_scene == null)
            {
                dc.DrawRectangle(Brushes.White, null, screen);
                return;
            }

            if (_scene.IsPaper && _scene.Paper.HasValue)
            {
                // Sheets sit on grey, like a plot preview.
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x96)), null, screen);
                var p = _scene.Paper.Value;
                var a = S(new Vec2(p.X1, p.Y2)); var b = S(new Vec2(p.X2, p.Y1));
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), null, new WRect(a.X + 4, a.Y + 4, b.X - a.X, b.Y - a.Y));
                dc.DrawRectangle(Brushes.White, null, new WRect(a, b));
            }
            else dc.DrawRectangle(Brushes.White, null, screen);

            var visible = View.Visible;
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            foreach (var g in _scene.Groups)
            {
                if (g.Clip.HasValue)
                {
                    var c = g.Clip.Value;
                    var a = S(new Vec2(c.X1, c.Y2)); var b = S(new Vec2(c.X2, c.Y1));
                    dc.PushClip(new RectangleGeometry(new WRect(a, b)));
                }
                foreach (var prim in g.Prims)
                {
                    if (!Overlaps(prim.Bounds, visible) && prim.Kind != PrimKind.Text) continue;
                    DrawPrim(dc, prim, dip, visible);
                }
                if (g.Clip.HasValue) dc.Pop();
            }

            if (RubberFrom.HasValue)
            {
                var from = S(RubberFrom.Value);
                var pen = new Pen(Brushes.OrangeRed, 1) { DashStyle = DashStyles.Dash };
                dc.DrawLine(pen, from, _snap.HasValue ? S(_snap.Value.Point) : _mouse);
            }
            if (_snap.HasValue) DrawSnapMarker(dc, _snap.Value);
        }

        private void DrawPrim(DrawingContext dc, Prim p, double dip, FdDraft.Core.Standards.Rect visible)
        {
            switch (p.Kind)
            {
                case PrimKind.Polyline:
                case PrimKind.Fill:
                {
                    if (p.Points.Count < 2) return;
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open())
                    {
                        ctx.BeginFigure(S(p.Points[0]), p.Kind == PrimKind.Fill, p.Closed || p.Kind == PrimKind.Fill);
                        var pts = new List<WPoint>(p.Points.Count - 1);
                        for (int i = 1; i < p.Points.Count; i++) pts.Add(S(p.Points[i]));
                        ctx.PolyLineTo(pts, true, false);
                    }
                    geo.Freeze();
                    if (p.Kind == PrimKind.Fill) dc.DrawGeometry(BrushFor(p.Rgb), null, geo);
                    else dc.DrawGeometry(null, PenFor(p.Rgb), geo);
                    break;
                }
                case PrimKind.Circle:
                {
                    double r = p.Radius * View.Zoom;
                    if (r < 0.3) return;
                    dc.DrawEllipse(null, PenFor(p.Rgb), S(p.Center), r, r);
                    break;
                }
                case PrimKind.Node:
                {
                    var c = S(p.Center);
                    dc.DrawRectangle(BrushFor(p.Rgb), null, new WRect(c.X - 1, c.Y - 1, 2, 2));
                    break;
                }
                case PrimKind.Text:
                {
                    double capPx = p.Height * View.Zoom;
                    if (capPx < 1.5) return; // unreadable at this zoom; skip rather than smear
                    var at = S(p.Center);
                    if (at.X < -2000 || at.Y < -2000 || at.X > ActualWidth + 2000 || at.Y > ActualHeight + 2000) return;
                    if (!_texts.TryGetValue(p, out var ft))
                    {
                        ft = new FormattedText(p.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, EmSize, BrushFor(p.Rgb), dip);
                        _texts[p] = ft;
                    }
                    double k = capPx / (EmSize * CapRatio);
                    double dx = p.H == HAlign.Left ? 0 : p.H == HAlign.Center ? -ft.Width / 2 : -ft.Width;
                    double cap = EmSize * CapRatio;
                    double dy = p.V == VAlign.Bottom ? -ft.Baseline : p.V == VAlign.Middle ? -ft.Baseline + cap / 2 : -ft.Baseline + cap;
                    var m = Matrix.Identity;
                    m.Translate(dx, dy);
                    m.Scale(k * p.WidthFactor, k);
                    m.Rotate(-p.Rotation * 180 / Math.PI);
                    m.Translate(at.X, at.Y);
                    dc.PushTransform(new MatrixTransform(m));
                    dc.DrawText(ft, new WPoint(0, 0));
                    dc.Pop();
                    break;
                }
            }
        }

        private void DrawSnapMarker(DrawingContext dc, SnapPoint s)
        {
            var c = S(s.Point);
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0xA0, 0x40)), 2);
            const double r = 6;
            switch (s.Kind)
            {
                case SnapKind.Endpoint: dc.DrawRectangle(null, pen, new WRect(c.X - r, c.Y - r, 2 * r, 2 * r)); break;
                case SnapKind.Midpoint:
                {
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new WPoint(c.X, c.Y - r), false, true);
                        ctx.PolyLineTo(new[] { new WPoint(c.X + r, c.Y + r), new WPoint(c.X - r, c.Y + r) }, true, false);
                    }
                    dc.DrawGeometry(null, pen, g);
                    break;
                }
                case SnapKind.Center: dc.DrawEllipse(null, pen, c, r, r); break;
                default:
                    dc.DrawLine(pen, new WPoint(c.X - r, c.Y - r), new WPoint(c.X + r, c.Y + r));
                    dc.DrawLine(pen, new WPoint(c.X - r, c.Y + r), new WPoint(c.X + r, c.Y - r));
                    break;
            }
        }

        private WPoint S(Vec2 p)
        {
            var s = View.ToScreen(p);
            return new WPoint(s.X, s.Y);
        }

        private static bool Overlaps(FdDraft.Core.Standards.Rect a, FdDraft.Core.Standards.Rect b) =>
            a.X1 <= b.X2 && b.X1 <= a.X2 && a.Y1 <= b.Y2 && b.Y1 <= a.Y2;

        private Pen PenFor(uint rgb)
        {
            if (_pens.TryGetValue(rgb, out var pen)) return pen;
            pen = new Pen(BrushFor(rgb), 1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            pen.Freeze();
            _pens[rgb] = pen;
            return pen;
        }

        private Brush BrushFor(uint rgb)
        {
            if (_brushes.TryGetValue(rgb, out var b)) return b;
            b = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            b.Freeze();
            _brushes[rgb] = b;
            return b;
        }

        // ---- mouse ----------------------------------------------------------------------------

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            var p = e.GetPosition(this);
            View.ZoomAt(p.X, p.Y, e.Delta > 0 ? 1.25 : 1 / 1.25);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Focus();
            if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
            {
                if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Middle) { ZoomExtents(); return; }
                _panning = true;
                _panFrom = e.GetPosition(this);
                CaptureMouse();
                Cursor = Cursors.SizeAll;
                e.Handled = true;
                return;
            }
            if (e.ChangedButton == MouseButton.Left && _scene != null)
            {
                var p = e.GetPosition(this);
                var world = _snap.HasValue ? _snap.Value.Point : View.ToScene(p.X, p.Y);
                Picked?.Invoke(world);
                e.Handled = true;
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (_panning)
            {
                _panning = false;
                ReleaseMouseCapture();
                Cursor = Cursors.Cross;
                e.Handled = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            _mouse = e.GetPosition(this);
            if (_panning)
            {
                View.PanPixels(_mouse.X - _panFrom.X, _mouse.Y - _panFrom.Y);
                _panFrom = _mouse;
                InvalidateVisual();
                return;
            }
            if (_scene == null) return;
            var world = View.ToScene(_mouse.X, _mouse.Y);
            _snap = SnapEnabled ? _scene.Snap(world, 10 / View.Zoom) : null;
            CursorMoved?.Invoke(_snap.HasValue ? _snap.Value.Point : world, _snap);
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            _snap = null;
            InvalidateVisual();
        }
    }
}
