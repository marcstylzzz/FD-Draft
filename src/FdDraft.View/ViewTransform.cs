using System;
using FdDraft.Core.Geometry;
using FdDraft.Core.Standards;

namespace FdDraft.View
{
    /// <summary>
    /// Scene (world or paper) coordinates to screen pixels and back. Screen y grows
    /// downward; scene y (northing, or paper up) grows upward.
    /// </summary>
    public sealed class ViewTransform
    {
        /// <summary>Scene point at the centre of the screen.</summary>
        public Vec2 Center { get; set; }
        /// <summary>Pixels per scene unit.</summary>
        public double Zoom { get; set; } = 1;
        public double ScreenWidth { get; set; } = 800;
        public double ScreenHeight { get; set; } = 600;

        public Vec2 ToScreen(Vec2 p) => new Vec2((p.X - Center.X) * Zoom + ScreenWidth / 2, ScreenHeight / 2 - (p.Y - Center.Y) * Zoom);
        public Vec2 ToScene(double sx, double sy) => new Vec2((sx - ScreenWidth / 2) / Zoom + Center.X, (ScreenHeight / 2 - sy) / Zoom + Center.Y);

        /// <summary>The scene rectangle visible on screen.</summary>
        public Rect Visible
        {
            get
            {
                var a = ToScene(0, ScreenHeight); var b = ToScene(ScreenWidth, 0);
                return new Rect(a.X, a.Y, Math.Max(b.X, a.X + 1e-9), Math.Max(b.Y, a.Y + 1e-9));
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

        public void PanPixels(double dx, double dy) => Center = new Vec2(Center.X - dx / Zoom, Center.Y + dy / Zoom);

        public void Fit(Rect r, double marginFraction = 0.05)
        {
            double w = Math.Max(r.Width, 1e-9), h = Math.Max(r.Height, 1e-9);
            Center = new Vec2((r.X1 + r.X2) / 2, (r.Y1 + r.Y2) / 2);
            Zoom = Math.Min(ScreenWidth / w, ScreenHeight / h) * (1 - 2 * marginFraction);
        }
    }
}
