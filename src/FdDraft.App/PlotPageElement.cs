using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FdDraft.Core.Drafting;
using FdDraft.View;
using WPoint = System.Windows.Point;
using WRect = System.Windows.Rect;

namespace FdDraft.App
{
    /// <summary>
    /// Draws a composed plot page (<see cref="PlotComposer"/>): each line in its plot pen's
    /// colour and width, text in its pen colour, clipped per viewport. The same element is the
    /// Print dialog's preview (scaled to fit, on grey, with the printable area dashed) and the
    /// page sent to a Windows printer (at 96 per inch, page-sized).
    /// </summary>
    public sealed class PlotPageElement : FrameworkElement
    {
        private readonly PlotPage _page;
        private readonly bool _preview;
        private readonly FdDraft.Core.Standards.Rect? _printable;
        private readonly Typeface _typeface = new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private readonly Dictionary<(uint, double), Pen> _pens = new Dictionary<(uint, double), Pen>();
        private const double EmSize = 100, CapRatio = 0.716;
        public const double DipPerMm = 96 / 25.4;

        /// <param name="preview">true: fit the page into the element on a grey backdrop;
        /// false: draw at true size (for printing), the element being page-sized.</param>
        /// <param name="printable">The printable area to outline in the preview (page mm).</param>
        public PlotPageElement(PlotPage page, bool preview, FdDraft.Core.Standards.Rect? printable = null)
        {
            _page = page; _preview = preview; _printable = printable;
            if (!preview) { Width = page.WidthMm * DipPerMm; Height = page.HeightMm * DipPerMm; }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double k, ox, oy;
            if (_preview)
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x96)), null, new WRect(0, 0, ActualWidth, ActualHeight));
                k = Math.Min((ActualWidth - 24) / _page.WidthMm, (ActualHeight - 24) / _page.HeightMm);
                if (!(k > 0)) return;
                ox = (ActualWidth - _page.WidthMm * k) / 2; oy = (ActualHeight - _page.HeightMm * k) / 2;
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, new WRect(ox + 4, oy + 4, _page.WidthMm * k, _page.HeightMm * k));
            }
            else { k = DipPerMm; ox = 0; oy = 0; }
            // Page mm (origin bottom-left, y up) -> element (origin top-left, y down).
            WPoint P(double x, double y) => new WPoint(ox + x * k, oy + (_page.HeightMm - y) * k);

            dc.DrawRectangle(Brushes.White, null, new WRect(P(0, _page.HeightMm), P(_page.WidthMm, 0)));
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            foreach (var g in _page.Page.Groups)
            {
                if (g.Clip.HasValue)
                {
                    var c = g.Clip.Value;
                    dc.PushClip(new RectangleGeometry(new WRect(P(c.X1, c.Y2), P(c.X2, c.Y1))));
                }
                foreach (var p in g.Prims) DrawPrim(dc, p, k, P, dip);
                if (g.Clip.HasValue) dc.Pop();
            }
            if (_preview && _printable.HasValue)
            {
                var r = _printable.Value;
                var dash = new Pen(new SolidColorBrush(Color.FromRgb(0x20, 0x60, 0xE0)), 1) { DashStyle = DashStyles.Dash };
                dc.DrawRectangle(null, dash, new WRect(P(r.X1, r.Y2), P(r.X2, r.Y1)));
            }
        }

        private void DrawPrim(DrawingContext dc, Prim p, double k, Func<double, double, WPoint> P, double dip)
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
                        bool fill = p.Kind == PrimKind.Fill;
                        ctx.BeginFigure(P(p.Points[0].X, p.Points[0].Y), fill, p.Closed || fill);
                        var pts = new List<WPoint>(p.Points.Count - 1);
                        for (int i = 1; i < p.Points.Count; i++) pts.Add(P(p.Points[i].X, p.Points[i].Y));
                        ctx.PolyLineTo(pts, true, true);
                    }
                    geo.Freeze();
                    if (p.Kind == PrimKind.Fill) dc.DrawGeometry(Brush(p.Rgb), null, geo);
                    else dc.DrawGeometry(null, PenFor(p, k), geo);
                    break;
                }
                case PrimKind.Circle:
                    dc.DrawEllipse(null, PenFor(p, k), P(p.Center.X, p.Center.Y), p.Radius * k, p.Radius * k);
                    break;
                case PrimKind.Text:
                {
                    double capPx = p.Height * k;
                    if (capPx < 0.4) return;
                    var ft = new FormattedText(p.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, EmSize, Brush(p.Rgb), dip);
                    double s = capPx / (EmSize * CapRatio);
                    double dx = p.H == HAlign.Left ? 0 : p.H == HAlign.Center ? -ft.Width / 2 : -ft.Width;
                    double cap = EmSize * CapRatio;
                    double dy = p.V == VAlign.Bottom ? -ft.Baseline : p.V == VAlign.Middle ? -ft.Baseline + cap / 2 : -ft.Baseline + cap;
                    var m = Matrix.Identity;
                    m.Translate(dx, dy);
                    m.Scale(s * p.WidthFactor, s);
                    m.Rotate(-p.Rotation * 180 / Math.PI);
                    var at = P(p.Center.X, p.Center.Y);
                    m.Translate(at.X, at.Y);
                    dc.PushTransform(new MatrixTransform(m));
                    dc.DrawText(ft, new WPoint(0, 0));
                    dc.Pop();
                    break;
                }
            }
        }

        /// <summary>The line's plot pen: its width in mm on paper (0 = the thinnest line the
        /// device draws - a tenth of a millimetre here), round ends like a plotter pen.</summary>
        private Pen PenFor(Prim p, double k)
        {
            double mm = p.PenMm > 0 ? p.PenMm : 0.1;
            double w = Math.Max(mm * k, _preview ? 0.6 : 0.25);
            var key = (p.Rgb, Math.Round(w, 3));
            if (_pens.TryGetValue(key, out var pen)) return pen;
            pen = new Pen(Brush(p.Rgb), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            pen.Freeze();
            _pens[key] = pen;
            return pen;
        }

        private static Brush Brush(uint rgb)
        {
            var b = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            b.Freeze();
            return b;
        }
    }
}
