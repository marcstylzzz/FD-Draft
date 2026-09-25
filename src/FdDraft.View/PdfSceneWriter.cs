using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;
using FdDraft.Core.Standards;

namespace FdDraft.View
{
    /// <summary>
    /// Writes a layout scene as a vector PDF at true scale: one page the size of the
    /// sheet, 1 paper mm = 1 mm on the PDF, so it prints at the plan's scale on any
    /// plotter. Linework stays vector; text uses the PDF base font Helvetica (no font
    /// files to embed or license). A plain writer on purpose: no PDF library needed.
    /// </summary>
    public static class PdfSceneWriter
    {
        private const double PtPerMm = 72.0 / 25.4;

        // Helvetica advance widths (1/1000 em) for WinAnsi 32..126, from the Adobe AFM.
        internal static readonly int[] Widths =
        {
            278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556,
            556, 556, 278, 278, 584, 584, 584, 556, 1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556, 333, 556, 556, 500, 556, 556, 278, 556,
            556, 222, 222, 500, 222, 833, 556, 556, 556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
        };

        internal const double CapHeight = 0.718; // Helvetica cap height, em

        /// <summary>How wide <paramref name="text"/> runs at a cap height of
        /// <paramref name="capHeight"/>, in the same units - the Helvetica metrics the PDF plots
        /// with, so text measured here (MTEXT word wrap) lines up with what plots.</summary>
        public static double MeasureText(string text, double capHeight)
        {
            double em = TextWidth(WinAnsi(text));
            return em * capHeight / CapHeight;
        }

        /// <summary>Scene to PDF. For a layout the page is the sheet; for model space, <paramref name="modelPageMm"/> is used.</summary>
        public static void Write(Scene scene, string path, string title, Rect? modelPageMm = null)
        {
            Rect page;
            double k;       // PDF points per scene unit
            double ox, oy;  // scene origin of the page
            if (scene.IsPaper && scene.Paper.HasValue)
            {
                page = scene.Paper.Value; k = PtPerMm; ox = page.X1; oy = page.Y1;
            }
            else
            {
                var pm = modelPageMm ?? new Rect(0, 0, 431.8, 279.4);
                var b = scene.Bounds;
                double fit = Math.Min(pm.Width / b.Width, pm.Height / b.Height) * 0.95;
                k = fit * PtPerMm;
                ox = b.X1 - (pm.Width / fit - b.Width) / 2; oy = b.Y1 - (pm.Height / fit - b.Height) / 2;
                page = new Rect(0, 0, pm.Width / fit, pm.Height / fit);
            }
            double W = page.Width * k, H = page.Height * k;
            string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            double X(double x) => (x - ox) * k;
            double Y(double y) => (y - oy) * k;

            var c = new StringBuilder();
            c.Append("1 J 1 j 0.3 w\n"); // round caps and joins, ~0.1 mm lines
            foreach (var g in scene.Groups)
            {
                c.Append("q\n");
                if (g.Clip.HasValue)
                {
                    var r = g.Clip.Value;
                    c.Append(F(X(r.X1))).Append(' ').Append(F(Y(r.Y1))).Append(' ').Append(F(r.Width * k)).Append(' ').Append(F(r.Height * k)).Append(" re W n\n");
                }
                uint lastStroke = uint.MaxValue, lastFill = uint.MaxValue;
                foreach (var p in g.Prims)
                {
                    switch (p.Kind)
                    {
                        case PrimKind.Polyline:
                            if (p.Points.Count < 2) break;
                            Stroke(c, p.Rgb, ref lastStroke);
                            c.Append(F(X(p.Points[0].X))).Append(' ').Append(F(Y(p.Points[0].Y))).Append(" m\n");
                            for (int i = 1; i < p.Points.Count; i++) c.Append(F(X(p.Points[i].X))).Append(' ').Append(F(Y(p.Points[i].Y))).Append(" l\n");
                            c.Append(p.Closed ? "s\n" : "S\n");
                            break;
                        case PrimKind.Fill:
                            if (p.Points.Count < 3) break;
                            Fill(c, p.Rgb, ref lastFill);
                            c.Append(F(X(p.Points[0].X))).Append(' ').Append(F(Y(p.Points[0].Y))).Append(" m\n");
                            for (int i = 1; i < p.Points.Count; i++) c.Append(F(X(p.Points[i].X))).Append(' ').Append(F(Y(p.Points[i].Y))).Append(" l\n");
                            c.Append("h f\n");
                            break;
                        case PrimKind.Circle:
                        {
                            Stroke(c, p.Rgb, ref lastStroke);
                            double cx = X(p.Center.X), cy = Y(p.Center.Y), r = p.Radius * k, m = r * 0.5523;
                            c.Append(F(cx + r)).Append(' ').Append(F(cy)).Append(" m\n");
                            c.Append(F(cx + r)).Append(' ').Append(F(cy + m)).Append(' ').Append(F(cx + m)).Append(' ').Append(F(cy + r)).Append(' ').Append(F(cx)).Append(' ').Append(F(cy + r)).Append(" c\n");
                            c.Append(F(cx - m)).Append(' ').Append(F(cy + r)).Append(' ').Append(F(cx - r)).Append(' ').Append(F(cy + m)).Append(' ').Append(F(cx - r)).Append(' ').Append(F(cy)).Append(" c\n");
                            c.Append(F(cx - r)).Append(' ').Append(F(cy - m)).Append(' ').Append(F(cx - m)).Append(' ').Append(F(cy - r)).Append(' ').Append(F(cx)).Append(' ').Append(F(cy - r)).Append(" c\n");
                            c.Append(F(cx + m)).Append(' ').Append(F(cy - r)).Append(' ').Append(F(cx + r)).Append(' ').Append(F(cy - m)).Append(' ').Append(F(cx + r)).Append(' ').Append(F(cy)).Append(" c S\n");
                            break;
                        }
                        case PrimKind.Node:
                            break; // nodes are construction points; they do not plot
                        case PrimKind.Text:
                        {
                            if (p.Height * k < 0.5) break; // too small to plot
                            double size = p.Height * k / CapHeight;
                            var bytes = WinAnsi(p.Text);
                            double w = TextWidth(bytes) * size * p.WidthFactor;
                            double dx = p.H == HAlign.Left ? 0 : p.H == HAlign.Center ? -w / 2 : -w;
                            double dy = p.V == VAlign.Bottom ? 0 : p.V == VAlign.Middle ? -p.Height * k / 2 : -p.Height * k;
                            double cos = Math.Cos(p.Rotation), sin = Math.Sin(p.Rotation);
                            double tx = X(p.Center.X) + dx * cos - dy * sin, ty = Y(p.Center.Y) + dx * sin + dy * cos;
                            Fill(c, p.Rgb, ref lastFill);
                            c.Append("BT /F1 ").Append(F(size)).Append(" Tf ");
                            if (Math.Abs(p.WidthFactor - 1) > 1e-6) c.Append(F(p.WidthFactor * 100)).Append(" Tz ");
                            c.Append(F(cos)).Append(' ').Append(F(sin)).Append(' ').Append(F(-sin)).Append(' ').Append(F(cos)).Append(' ').Append(F(tx)).Append(' ').Append(F(ty))
                             .Append(" Tm (").Append(Escape(bytes)).Append(") Tj ET\n");
                            break;
                        }
                    }
                }
                c.Append("Q\n");
            }

            byte[] content;
            using (var ms = new MemoryStream())
            {
                using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                {
                    var raw = Encoding.ASCII.GetBytes(c.ToString());
                    z.Write(raw, 0, raw.Length);
                }
                content = ms.ToArray();
            }

            using var fs = File.Create(path);
            var offsets = new List<long>();
            void Raw(string s) { var b = Encoding.ASCII.GetBytes(s); fs.Write(b, 0, b.Length); }
            void Obj(string body) { offsets.Add(fs.Position); Raw((offsets.Count) + " 0 obj\n" + body + "\nendobj\n"); }

            Raw("%PDF-1.4\n%âãÏÓ\n");
            Obj("<< /Type /Catalog /Pages 2 0 R >>");
            Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
            Obj("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + F(W) + " " + F(H) + "] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>");
            Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            offsets.Add(fs.Position);
            Raw("5 0 obj\n<< /Length " + content.Length + " /Filter /FlateDecode >>\nstream\n");
            fs.Write(content, 0, content.Length);
            Raw("\nendstream\nendobj\n");
            Obj("<< /Title (" + Escape(WinAnsi(title)) + ") /Producer (FD-Draft) >>");
            long xref = fs.Position;
            Raw("xref\n0 " + (offsets.Count + 1) + "\n0000000000 65535 f \n");
            foreach (var o in offsets) Raw(o.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
            Raw("trailer\n<< /Size " + (offsets.Count + 1) + " /Root 1 0 R /Info " + offsets.Count + " 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
        }

        private static void Stroke(StringBuilder c, uint rgb, ref uint last)
        {
            if (rgb == last) return;
            last = rgb;
            c.Append(Rgb(rgb)).Append(" RG\n");
        }

        private static void Fill(StringBuilder c, uint rgb, ref uint last)
        {
            if (rgb == last) return;
            last = rgb;
            c.Append(Rgb(rgb)).Append(" rg\n");
        }

        private static string Rgb(uint rgb) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###}", ((rgb >> 16) & 255) / 255.0, ((rgb >> 8) & 255) / 255.0, (rgb & 255) / 255.0);

        /// <summary>Text in Windows-1252 (the PDF's WinAnsiEncoding): °, ², ±, Ø map directly; anything else outside it becomes '?'.</summary>
        private static byte[] WinAnsi(string s)
        {
            var b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                b[i] = ch < 128 || (ch >= 160 && ch <= 255) ? (byte)ch : (byte)'?';
            }
            return b;
        }

        private static double TextWidth(byte[] b)
        {
            double w = 0;
            foreach (var x in b)
                w += x >= 32 && x <= 126 ? Widths[x - 32] : x == 176 ? 400 : x == 178 ? 333 : x == 177 ? 584 : x == 216 ? 778 : 556;
            return w / 1000.0;
        }

        private static string Escape(byte[] b)
        {
            var sb = new StringBuilder();
            foreach (var x in b)
            {
                if (x == (byte)'(' || x == (byte)')' || x == (byte)'\\') sb.Append('\\').Append((char)x);
                else if (x < 32 || x > 126) sb.Append('\\').Append(Convert.ToString(x, 8).PadLeft(3, '0'));
                else sb.Append((char)x);
            }
            return sb.ToString();
        }
    }
}
