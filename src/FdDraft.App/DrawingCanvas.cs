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
        /// <summary>Where a left-button drag started (screen), while no tool is active - a
        /// drag past a few pixels becomes a selection box instead of a click.</summary>
        private WPoint? _boxFrom;
        private bool _boxing;
        /// <summary>The entity under the cursor when the button went down - dragging from it
        /// moves it (and the rest of the selection, if it was selected) instead of boxing.</summary>
        private ulong? _pressedOn;
        private bool _dragging;

        public ViewTransform View { get; } = new ViewTransform();
        public bool SnapEnabled { get; set; } = true;
        /// <summary>Which object snaps are on (the Object Snap toolbar); used while <see cref="SnapEnabled"/>.</summary>
        public SnapModes SnapModes { get; set; } = SnapModes.Default;
        /// <summary>Pan mode (the Pan button): a left-drag pans the view instead of selecting.</summary>
        public bool PanMode { get; set; }
        /// <summary>Right-click: the entity under the cursor (if any) and the screen point - for the
        /// context menu, or to finish the running command.</summary>
        public event Action<ulong?, Point>? RightClicked;
        /// <summary>First point of a two-point tool (inverse): a rubber band is drawn from it.</summary>
        public Vec2? RubberFrom { get; set; }
        /// <summary>True while a command is waiting for a point (Inverse, Line, Move's pick steps,
        /// and so on): a click is a point pick, not a selection click.</summary>
        public bool ToolActive { get; set; }
        /// <summary>DWG handles of the selected entities, highlighted in the canvas.</summary>
        public HashSet<ulong> Selected { get; } = new HashSet<ulong>();

        /// <summary>Scene point under the cursor (snapped if a snap is active) and the snap, if any.</summary>
        public event Action<Vec2, SnapPoint?>? CursorMoved;
        /// <summary>A point picked while <see cref="ToolActive"/> is true.</summary>
        public event Action<Vec2>? Picked;
        /// <summary>The entity handle clicked while not tool-active (null on an empty click), and
        /// whether Ctrl was held (add/remove from the existing selection rather than replace it).</summary>
        public event Action<ulong?, bool>? EntityClicked;
        /// <summary>The entity handles a drag-box selected (window left-to-right, crossing
        /// right-to-left), and whether Ctrl was held (add to the selection rather than replace it).</summary>
        public event Action<HashSet<ulong>, bool>? BoxSelected;
        /// <summary>Entities dragged with the mouse: which ones, and the scene points the drag
        /// went from and to (no snapping - a label goes exactly where it's dropped).</summary>
        public event Action<HashSet<ulong>, Vec2, Vec2>? Dragged;

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

        // ---- zoom history (Zoom Previous) ----
        private readonly List<(Vec2 Center, double Zoom)> _views = new List<(Vec2, double)>();
        private DateTime _lastViewPush = DateTime.MinValue;

        /// <summary>Remembers the current view before a change, for Zoom Previous. Wheel steps within
        /// a second of each other count as one change.</summary>
        private void PushView(bool coalesce = false)
        {
            if (_scene == null || View.Zoom <= 0) return;
            if (coalesce && (DateTime.Now - _lastViewPush).TotalSeconds < 1) { _lastViewPush = DateTime.Now; return; }
            _lastViewPush = coalesce ? DateTime.Now : DateTime.MinValue;
            _views.Add((View.Center, View.Zoom));
            if (_views.Count > 50) _views.RemoveAt(0);
        }

        /// <summary>Zoom Previous: back to the view before the last zoom or pan. False when there's none.</summary>
        public bool ZoomPrevious()
        {
            if (_views.Count == 0) return false;
            var (c, z) = _views[_views.Count - 1];
            _views.RemoveAt(_views.Count - 1);
            View.Center = c; View.Zoom = z;
            _lastViewPush = DateTime.MinValue;
            InvalidateVisual();
            return true;
        }

        /// <summary>Zooms so a scene rectangle fills the view.</summary>
        public void ZoomWindow(FdDraft.Core.Standards.Rect r)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            PushView();
            UpdateSize();
            View.Fit(r, 0.02);
            InvalidateVisual();
        }

        /// <summary>Zooms in or out about the middle of the view.</summary>
        public void ZoomBy(double factor)
        {
            PushView();
            UpdateSize();
            View.ZoomAt(ActualWidth / 2, ActualHeight / 2, factor);
            InvalidateVisual();
        }

        /// <summary>Forgets the zoom history (a new drawing or sheet).</summary>
        public void ClearViewHistory() => _views.Clear();

        public void ZoomExtents()
        {
            if (_fitted) PushView();
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
            else dc.DrawRectangle(_scene.DarkBackground ? Brushes.Black : Brushes.White, null, screen);

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
                    DrawPrim(dc, prim, dip, visible, highlight: false);
                }
                if (g.Clip.HasValue) dc.Pop();
            }

            // Selected entities are redrawn on top in a highlight colour so they always show,
            // even under other linework.
            if (Selected.Count > 0)
            {
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
                        if (prim.Handle == 0 || !Selected.Contains(prim.Handle)) continue;
                        DrawPrim(dc, prim, dip, visible, highlight: true);
                    }
                    if (g.Clip.HasValue) dc.Pop();
                }
            }

            if (RubberFrom.HasValue)
            {
                var from = S(RubberFrom.Value);
                var pen = new Pen(Brushes.OrangeRed, 1) { DashStyle = DashStyles.Dash };
                dc.DrawLine(pen, from, _snap.HasValue ? S(_snap.Value.Point) : _mouse);
            }
            if (_snap.HasValue) DrawSnapMarker(dc, _snap.Value);
            if (_dragging && _boxFrom.HasValue && _pressedOn.HasValue)
            {
                // The dragged entities, drawn highlighted at where they'd land.
                var moving = Selected.Contains(_pressedOn.Value) ? Selected : new HashSet<ulong> { _pressedOn.Value };
                dc.PushTransform(new TranslateTransform(_mouse.X - _boxFrom.Value.X, _mouse.Y - _boxFrom.Value.Y));
                foreach (var g in _scene.Groups)
                    foreach (var prim in g.Prims)
                        if (prim.Handle != 0 && moving.Contains(prim.Handle)) DrawPrim(dc, prim, dip, visible, highlight: true);
                dc.Pop();
                dc.DrawLine(new Pen(Brushes.OrangeRed, 1) { DashStyle = DashStyles.Dash }, _boxFrom.Value, _mouse);
            }
            if (_boxing && _boxFrom.HasValue)
            {
                // Window (left to right) solid blue; crossing (right to left) dashed green - the
                // same cue every CAD program gives for which rule applies.
                bool crossing = _mouse.X < _boxFrom.Value.X;
                var rect = new WRect(_boxFrom.Value, _mouse);
                var fill = new SolidColorBrush(crossing ? Color.FromArgb(40, 0x20, 0xA0, 0x40) : Color.FromArgb(40, 0x20, 0x60, 0xE0));
                var pen = new Pen(new SolidColorBrush(crossing ? Color.FromRgb(0x20, 0xA0, 0x40) : Color.FromRgb(0x20, 0x60, 0xE0)), 1);
                if (crossing) pen.DashStyle = DashStyles.Dash;
                dc.DrawRectangle(fill, pen, rect);
            }
        }

        private static readonly Color HighlightColor = Color.FromRgb(0xFF, 0x00, 0xC8);

        private void DrawPrim(DrawingContext dc, Prim p, double dip, FdDraft.Core.Standards.Rect visible, bool highlight)
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
                        ctx.BeginFigure(S(p.Points[0]), p.Kind == PrimKind.Fill && !highlight, p.Closed || p.Kind == PrimKind.Fill);
                        var pts = new List<WPoint>(p.Points.Count - 1);
                        for (int i = 1; i < p.Points.Count; i++) pts.Add(S(p.Points[i]));
                        ctx.PolyLineTo(pts, true, false);
                    }
                    geo.Freeze();
                    if (highlight) dc.DrawGeometry(null, HighlightPen(), geo);
                    else if (p.Kind == PrimKind.Fill) dc.DrawGeometry(BrushFor(p.Rgb), null, geo);
                    else dc.DrawGeometry(null, PenFor(p.Rgb), geo);
                    break;
                }
                case PrimKind.Circle:
                {
                    double r = p.Radius * View.Zoom;
                    if (r < 0.3) return;
                    dc.DrawEllipse(null, highlight ? HighlightPen() : PenFor(p.Rgb), S(p.Center), r, r);
                    break;
                }
                case PrimKind.Node:
                {
                    var c = S(p.Center);
                    if (highlight) dc.DrawEllipse(null, HighlightPen(), c, 5, 5);
                    else dc.DrawRectangle(BrushFor(p.Rgb), null, new WRect(c.X - 1, c.Y - 1, 2, 2));
                    break;
                }
                case PrimKind.Text:
                {
                    double capPx = p.Height * View.Zoom;
                    if (capPx < 1.5) return; // unreadable at this zoom; skip rather than smear
                    var at = S(p.Center);
                    if (at.X < -2000 || at.Y < -2000 || at.X > ActualWidth + 2000 || at.Y > ActualHeight + 2000) return;
                    if (highlight)
                    {
                        var box = new StreamGeometry();
                        using (var ctx = box.Open())
                        {
                            var corners = TextHit.Corners(p);
                            ctx.BeginFigure(S(corners[0]), false, true);
                            ctx.PolyLineTo(new[] { S(corners[1]), S(corners[2]), S(corners[3]) }, true, false);
                        }
                        box.Freeze();
                        dc.DrawGeometry(null, HighlightPen(), box);
                    }
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
                case SnapKind.Quadrant:
                {
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new WPoint(c.X, c.Y - r), false, true);
                        ctx.PolyLineTo(new[] { new WPoint(c.X + r, c.Y), new WPoint(c.X, c.Y + r), new WPoint(c.X - r, c.Y) }, true, false);
                    }
                    dc.DrawGeometry(null, pen, g);
                    break;
                }
                case SnapKind.Perpendicular:
                    dc.DrawLine(pen, new WPoint(c.X - r, c.Y + r), new WPoint(c.X + r, c.Y + r));
                    dc.DrawLine(pen, new WPoint(c.X - r, c.Y + r), new WPoint(c.X - r, c.Y - r));
                    dc.DrawLine(pen, new WPoint(c.X - r, c.Y), new WPoint(c.X, c.Y));
                    dc.DrawLine(pen, new WPoint(c.X, c.Y), new WPoint(c.X, c.Y + r));
                    break;
                case SnapKind.Nearest:
                {
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new WPoint(c.X - r, c.Y - r), false, true);
                        ctx.PolyLineTo(new[] { new WPoint(c.X + r, c.Y - r), new WPoint(c.X - r, c.Y + r), new WPoint(c.X + r, c.Y + r) }, true, false);
                    }
                    dc.DrawGeometry(null, pen, g);
                    break;
                }
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

        private Pen? _highlightPen;
        private Pen HighlightPen()
        {
            if (_highlightPen == null)
            {
                _highlightPen = new Pen(new SolidColorBrush(HighlightColor), 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                _highlightPen.Freeze();
            }
            return _highlightPen;
        }

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
            PushView(coalesce: true);
            View.ZoomAt(p.X, p.Y, e.Delta > 0 ? 1.25 : 1 / 1.25);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Focus();
            if (e.ChangedButton == MouseButton.Right && _scene != null)
            {
                var rp = e.GetPosition(this);
                var hit = HitTest(rp);
                RightClicked?.Invoke(hit != null && hit.Handle != 0 ? hit.Handle : (ulong?)null, rp);
                e.Handled = true;
                return;
            }
            if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || (PanMode && !ToolActive))))
            {
                if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Middle) { ZoomExtents(); return; }
                PushView();
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
                if (ToolActive)
                {
                    var world = _snap.HasValue ? _snap.Value.Point : View.ToScene(p.X, p.Y);
                    Picked?.Invoke(world);
                }
                else
                {
                    // Decide on release: a click selects what's under the cursor; a drag from an
                    // entity moves it, a drag from empty space boxes.
                    _boxFrom = p;
                    _boxing = false;
                    _dragging = false;
                    var hit = HitTest(p);
                    _pressedOn = hit != null && hit.Handle != 0 ? hit.Handle : (ulong?)null;
                    CaptureMouse();
                }
                e.Handled = true;
            }
        }

        /// <summary>The prim nearest the screen point, within a small pixel tolerance - used to
        /// pick an entity to select when no tool is waiting for a point.</summary>
        private Prim? HitTest(WPoint screenPt, double tolerancePx = 6)
        {
            if (_scene == null) return null;
            Prim? best = null;
            double bestD = tolerancePx;
            var scenePt = View.ToScene(screenPt.X, screenPt.Y);
            foreach (var g in _scene.Groups)
            {
                if (g.Clip.HasValue)
                {
                    // Only what shows through a viewport can be picked there.
                    var c = g.Clip.Value;
                    if (scenePt.X < c.X1 || scenePt.X > c.X2 || scenePt.Y < c.Y1 || scenePt.Y > c.Y2) continue;
                }
                foreach (var prim in g.Prims)
                {
                    if (prim.Handle == 0) continue;
                    double d;
                    if (prim.Kind == PrimKind.Text)
                    {
                        // Anywhere on the characters counts; a click inside a label's box picks the
                        // label ahead of linework or a point marker it sits on.
                        double inScene = TextHit.Distance(prim, scenePt);
                        d = inScene <= 0 ? -1 : inScene * View.Zoom;
                    }
                    else d = DistanceToPrim(prim, screenPt);
                    if (d < bestD) { bestD = d; best = prim; }
                }
            }
            return best;
        }

        private double DistanceToPrim(Prim p, WPoint pt)
        {
            switch (p.Kind)
            {
                case PrimKind.Polyline:
                case PrimKind.Fill:
                {
                    if (p.Points.Count == 0) return double.MaxValue;
                    if (p.Points.Count == 1) { var c0 = S(p.Points[0]); return Distance(pt, c0); }
                    double best = double.MaxValue;
                    int segs = p.Points.Count - (p.Closed || p.Kind == PrimKind.Fill ? 0 : 1);
                    for (int i = 0; i < segs; i++)
                    {
                        var a = S(p.Points[i]);
                        var b = S(p.Points[(i + 1) % p.Points.Count]);
                        best = Math.Min(best, DistanceToSegment(pt, a, b));
                    }
                    return best;
                }
                case PrimKind.Circle:
                {
                    var c = S(p.Center);
                    double r = p.Radius * View.Zoom;
                    return Math.Abs(Distance(pt, c) - r);
                }
                case PrimKind.Text:
                case PrimKind.Node:
                    return Distance(pt, S(p.Center));
                default:
                    return double.MaxValue;
            }
        }

        private static double Distance(WPoint a, WPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static double DistanceToSegment(WPoint p, WPoint a, WPoint b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-9) return Distance(p, a);
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            t = Math.Max(0, Math.Min(1, t));
            return Distance(p, new WPoint(a.X + t * dx, a.Y + t * dy));
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (_boxFrom.HasValue && e.ChangedButton == MouseButton.Left)
            {
                var from = _boxFrom.Value;
                var to = e.GetPosition(this);
                bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                _boxFrom = null;
                ReleaseMouseCapture();
                if (_dragging && _pressedOn.HasValue && _scene != null)
                {
                    _dragging = false; _boxing = false;
                    var moving = Selected.Contains(_pressedOn.Value) ? new HashSet<ulong>(Selected) : new HashSet<ulong> { _pressedOn.Value };
                    _pressedOn = null;
                    Dragged?.Invoke(moving, View.ToScene(from.X, from.Y), View.ToScene(to.X, to.Y));
                }
                else if (_boxing && _scene != null)
                {
                    _boxing = false;
                    var a = View.ToScene(from.X, from.Y);
                    var b = View.ToScene(to.X, to.Y);
                    var box = new FdDraft.Core.Standards.Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                    BoxSelected?.Invoke(BoxSelect.Handles(_scene, box, crossing: to.X < from.X), ctrl);
                }
                else
                {
                    _boxing = false;
                    var hit = HitTest(from);
                    EntityClicked?.Invoke(hit?.Handle is ulong h && h != 0 ? h : null, ctrl);
                }
                InvalidateVisual();
                e.Handled = true;
                return;
            }
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
            if (_boxFrom.HasValue && !_boxing && !_dragging && Math.Abs(_mouse.X - _boxFrom.Value.X) + Math.Abs(_mouse.Y - _boxFrom.Value.Y) > 5)
            {
                if (_pressedOn.HasValue) _dragging = true; else _boxing = true;
            }
            if (_boxing || _dragging) { InvalidateVisual(); return; }
            if (_scene == null) return;
            var world = View.ToScene(_mouse.X, _mouse.Y);
            _snap = SnapEnabled && ToolActive || SnapEnabled && RubberFrom.HasValue
                ? _scene.Snap(world, 10 / View.Zoom, SnapModes, RubberFrom)
                : SnapEnabled ? _scene.Snap(world, 10 / View.Zoom, SnapModes & ~(SnapModes.Nearest | SnapModes.Perpendicular), null) : null;
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
