using System;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Text;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;

namespace FdDraft.View
{
    /// <summary>
    /// Paints a scene as SVG with the same rules the app's canvas uses. Used for
    /// previews and by the tests to check what a drawing actually looks like.
    /// </summary>
    public static class SvgSceneWriter
    {
        public static string Render(Scene scene, double pixelWidth)
        {
            var b = scene.Bounds;
            double k = pixelWidth / b.Width;
            double H = b.Height * k;
            string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
            double X(double x) => (x - b.X1) * k;
            double Y(double y) => (b.Y2 - y) * k;
            var sb = new StringBuilder();
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(F(pixelWidth)).Append("\" height=\"").Append(F(H))
              .Append("\" viewBox=\"0 0 ").Append(F(pixelWidth)).Append(' ').Append(F(H)).Append("\" font-family=\"Arial, sans-serif\">\n");
            sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#fff\"/>\n");
            if (scene.Paper.HasValue)
            {
                var p = scene.Paper.Value;
                sb.Append("<rect x=\"").Append(F(X(p.X1))).Append("\" y=\"").Append(F(Y(p.Y2))).Append("\" width=\"").Append(F(p.Width * k)).Append("\" height=\"").Append(F(p.Height * k))
                  .Append("\" fill=\"#fff\" stroke=\"#bbb\"/>\n");
            }
            int clipId = 0;
            foreach (var g in scene.Groups)
            {
                if (g.Clip.HasValue)
                {
                    var c = g.Clip.Value;
                    sb.Append("<clipPath id=\"c").Append(clipId).Append("\"><rect x=\"").Append(F(X(c.X1))).Append("\" y=\"").Append(F(Y(c.Y2)))
                      .Append("\" width=\"").Append(F(c.Width * k)).Append("\" height=\"").Append(F(c.Height * k)).Append("\"/></clipPath><g clip-path=\"url(#c").Append(clipId++).Append(")\">\n");
                }
                foreach (var p in g.Prims)
                {
                    string col = "#" + p.Rgb.ToString("X6", CultureInfo.InvariantCulture);
                    switch (p.Kind)
                    {
                        case PrimKind.Fill when p.Holes != null:
                            sb.Append("<path fill-rule=\"evenodd\" fill=\"" + col + "\" d=\"");
                            foreach (var loop in new[] { p.Points }.Concat(p.Holes))
                            {
                                for (int i = 0; i < loop.Count; i++) sb.Append(i == 0 ? "M" : "L").Append(F(X(loop[i].X))).Append(',').Append(F(Y(loop[i].Y))).Append(' ');
                                sb.Append("Z ");
                            }
                            sb.Append("\"/>\n");
                            break;
                        case PrimKind.Polyline:
                        case PrimKind.Fill:
                            sb.Append(p.Kind == PrimKind.Fill ? "<polygon points=\"" : "<polyline points=\"");
                            foreach (var v in p.Points) sb.Append(F(X(v.X))).Append(',').Append(F(Y(v.Y))).Append(' ');
                            sb.Append(p.Kind == PrimKind.Fill ? "\" fill=\"" + col + "\"/>\n" : "\" fill=\"none\" stroke=\"" + col + "\" stroke-width=\"0.8\"/>\n");
                            break;
                        case PrimKind.Circle:
                            sb.Append("<circle cx=\"").Append(F(X(p.Center.X))).Append("\" cy=\"").Append(F(Y(p.Center.Y))).Append("\" r=\"").Append(F(p.Radius * k))
                              .Append("\" fill=\"none\" stroke=\"").Append(col).Append("\" stroke-width=\"0.8\"/>\n");
                            break;
                        case PrimKind.Node:
                            sb.Append("<circle cx=\"").Append(F(X(p.Center.X))).Append("\" cy=\"").Append(F(Y(p.Center.Y))).Append("\" r=\"0.8\" fill=\"").Append(col).Append("\"/>\n");
                            break;
                        case PrimKind.Text:
                        {
                            double x = X(p.Center.X), y = Y(p.Center.Y);
                            string anchor = p.H == HAlign.Left ? "start" : p.H == HAlign.Center ? "middle" : "end";
                            string baseline = p.V == VAlign.Bottom ? "auto" : p.V == VAlign.Middle ? "central" : "hanging";
                            sb.Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(y)).Append("\" font-size=\"").Append(F(p.Height * k * 1.35))
                              .Append("\" text-anchor=\"").Append(anchor).Append("\" dominant-baseline=\"").Append(baseline).Append("\" fill=\"").Append(col)
                              .Append("\" transform=\"rotate(").Append(F(-p.Rotation * 180 / Math.PI)).Append(' ').Append(F(x)).Append(' ').Append(F(y)).Append(")\">")
                              .Append(System.Security.SecurityElement.Escape(p.Text)).Append("</text>\n");
                            break;
                        }
                    }
                }
                if (g.Clip.HasValue) sb.Append("</g>\n");
            }
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        public static void Write(Scene scene, string path, double pixelWidth) =>
            File.WriteAllText(path, Render(scene, pixelWidth), new UTF8Encoding(false));
    }
}
