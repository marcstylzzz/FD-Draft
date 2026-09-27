using System;
using FdDraft.Core.Geometry;
using FdDraft.Core.Standards;

namespace FdDraft.View
{
    /// <summary>
    /// Scene (world or paper) coordinates to screen pixels and back. Screen y grows
    /// downward; scene y (northing, or paper up) grows upward. <see cref="Twist"/> turns the
    /// whole view (MSCAD's Surveyor View): the drawing is shown rotated on screen, its
    /// coordinates untouched.
    /// </summary>
    public sealed class ViewTransform
    {
        /// <summary>Scene point at the centre of the screen.</summary>
        public Vec2 Center { get; set; }
        /// <summary>Pixels per scene unit.</summary>
        public double Zoom { get; set; } = 1;
        public double ScreenWidth { get; set; } = 800;
        public double ScreenHeight { get; set; } = 600;
        /// <summary>How far the drawing is turned on screen, radians counter-clockwise (0 = north up).</summary>
        public double Twist { get; set; }

        public Vec2 ToScreen(Vec2 p)
        {
            double dx = p.X - Center.X, dy = p.Y - Center.Y;
            if (Twist != 0)
            {
                double c = Math.Cos(Twist), s = Math.Sin(Twist);
                (dx, dy) = (dx * c - dy * s, dx * s + dy * c);
            }
            return new Vec2(dx * Zoom + ScreenWidth / 2, ScreenHeight / 2 - dy * Zoom);
        }

        public Vec2 ToScene(double sx, double sy)
        {
            double dx = (sx - ScreenWidth / 2) / Zoom, dy = (ScreenHeight / 2 - sy) / Zoom;
            if (Twist != 0)
            {
                double c = Math.Cos(Twist), s = Math.Sin(Twist);
                (dx, dy) = (dx * c + dy * s, -dx * s + dy * c);
            }
            return new Vec2(dx + Center.X, dy + Center.Y);
        }

        /// <summary>The scene rectangle visible on screen (its bounding box when the view is turned).</summary>
        public Rect Visible
        {
            get
            {
                var a = ToScene(0, 0); var b = ToScene(ScreenWidth, 0); var c = ToScene(ScreenWidth, ScreenHeight); var d = ToScene(0, ScreenHeight);
                double x1 = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)), x2 = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
                double y1 = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)), y2 = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
                return new Rect(x1, y1, Math.Max(x2, x1 + 1e-9), Math.Max(y2, y1 + 1e-9));
            }
        }

        /// <summary>Zoom by <paramref name="factor"/> keeping the scene point under the cursor fixed.</summary>
        public void ZoomAt(double sx, double sy, double factor)
        {
            var before = ToScene(sx, sy);
            Zoom = Math.Max(1e-9, Math.Min(1e9, Zoom * factor));
            var after = ToScene(sx, sy);
            Center = Center + (before - after);
        }

        public void PanPixels(double dx, double dy)
        {
            var a = ToScene(ScreenWidth / 2, ScreenHeight / 2);
            var b = ToScene(ScreenWidth / 2 + dx, ScreenHeight / 2 + dy);
            Center = Center - (b - a);
        }

        /// <summary>Fits a scene rectangle on screen - as the view is turned, its turned extent.</summary>
        public void Fit(Rect r, double marginFraction = 0.05) =>
            FitCorners(new[] { new Vec2(r.X1, r.Y1), new Vec2(r.X2, r.Y1), new Vec2(r.X2, r.Y2), new Vec2(r.X1, r.Y2) }, marginFraction);

        /// <summary>Fits scene points on screen, measuring them the way the view is turned - so a
        /// zoom window picked on a turned view shows what was boxed on screen.</summary>
        public void FitCorners(Vec2[] pts, double marginFraction = 0.05)
        {
            double c = Math.Cos(Twist), s = Math.Sin(Twist);
            double u1 = double.MaxValue, u2 = double.MinValue, v1 = double.MaxValue, v2 = double.MinValue;
            foreach (var p in pts)
            {
                double u = p.X * c - p.Y * s, v = p.X * s + p.Y * c;
                u1 = Math.Min(u1, u); u2 = Math.Max(u2, u); v1 = Math.Min(v1, v); v2 = Math.Max(v2, v);
            }
            double w = Math.Max(u2 - u1, 1e-9), h = Math.Max(v2 - v1, 1e-9);
            double cu = (u1 + u2) / 2, cv = (v1 + v2) / 2;
            // Back from the turned frame to the scene.
            Center = new Vec2(cu * c + cv * s, -cu * s + cv * c);
            Zoom = Math.Min(ScreenWidth / w, ScreenHeight / h) * (1 - 2 * marginFraction);
        }
    }
}
