using System;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;

namespace FdDraft.View
{
    /// <summary>
    /// Picking text by the area it covers, not just its anchor point: a point number is anchored
    /// at its bottom-left corner and a bearing at its bottom-centre, so a click on the middle of
    /// the characters has to count as a hit.
    /// </summary>
    public static class TextHit
    {
        /// <summary>The four corners of a text prim's box, in scene units, in order round it -
        /// what the canvas outlines when the text is selected.</summary>
        public static Vec2[] Corners(Prim t)
        {
            Box(t, out double x0, out double y0, out double w, out double h);
            double c = Math.Cos(t.Rotation), s = Math.Sin(t.Rotation);
            Vec2 P(double lx, double ly) => new Vec2(t.Center.X + lx * c - ly * s, t.Center.Y + lx * s + ly * c);
            double pad = h * 0.2;
            return new[] { P(x0 - pad, y0 - pad), P(x0 + w + pad, y0 - pad), P(x0 + w + pad, y0 + h + pad), P(x0 - pad, y0 + h + pad) };
        }

        private static void Box(Prim t, out double x0, out double y0, out double w, out double h)
        {
            h = Math.Max(t.Height, 1e-9);
            w = Math.Max(PdfSceneWriter.MeasureText(t.Text, h, t.WideSpaces) * (t.WidthFactor <= 0 ? 1 : t.WidthFactor), h * 0.5);
            x0 = t.H == HAlign.Left ? 0 : t.H == HAlign.Center ? -w / 2 : -w;
            y0 = t.V == VAlign.Bottom ? 0 : t.V == VAlign.Middle ? -h / 2 : -h;
        }

        /// <summary>Distance (scene units) from <paramref name="p"/> to a text prim's box - its
        /// rotated extent, measured with the Helvetica metrics the PDF plots with. 0 inside.</summary>
        public static double Distance(Prim t, Vec2 p)
        {
            Box(t, out double x0, out double y0, out double w, out double h);
            // Into the text's own axes: along the baseline and up.
            var d = p - t.Center;
            double c = Math.Cos(t.Rotation), s = Math.Sin(t.Rotation);
            double lx = d.X * c + d.Y * s, ly = -d.X * s + d.Y * c;
            // A little air round the capitals (descenders, the gap to the next line).
            double pad = h * 0.2;
            double dx = Math.Max(Math.Max(x0 - pad - lx, 0), lx - (x0 + w + pad));
            double dy = Math.Max(Math.Max(y0 - pad - ly, 0), ly - (y0 + h + pad));
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
