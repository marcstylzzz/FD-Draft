using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;

namespace FdDraft.Core.Export
{
    /// <summary>
    /// Renders the plan as it will sit in the chosen viewport, in paper millimetres.
    /// A quick visual check of the sheet/scale choice and the label placement
    /// without opening AutoCAD. Not a deliverable - the title block is the .dwt's.
    /// </summary>
    public static class SvgPreview
    {
        public static void Write(DraftDocument doc, string path, double viewportWidthMm, double viewportHeightMm, double modelPerMm, string caption)
        {
            var ext = doc.GeometryExtents();
            var c = ext.Center;
            double W = viewportWidthMm, H = viewportHeightMm;
            string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            Vec2 T(Vec2 p) => new Vec2((p.X - c.X) / modelPerMm + W / 2, H / 2 - (p.Y - c.Y) / modelPerMm);

            var sb = new StringBuilder();
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"-10 -10 ").Append(F(W + 20)).Append(' ').Append(F(H + 24))
              .Append("\" width=\"").Append(F((W + 20) * 2)).Append("\" height=\"").Append(F((H + 24) * 2)).Append("\" font-family=\"Arial, sans-serif\">\n");
            sb.Append("<rect x=\"-10\" y=\"-10\" width=\"").Append(F(W + 20)).Append("\" height=\"").Append(F(H + 24)).Append("\" fill=\"#fff\"/>\n");
            sb.Append("<rect x=\"0\" y=\"0\" width=\"").Append(F(W)).Append("\" height=\"").Append(F(H)).Append("\" fill=\"none\" stroke=\"#999\" stroke-width=\"0.3\" stroke-dasharray=\"2 1\"/>\n");
            sb.Append("<text x=\"0\" y=\"").Append(F(H + 8)).Append("\" font-size=\"4\" fill=\"#555\">").Append(Esc(caption)).Append("</text>\n");

            foreach (var e in doc.Entities)
            {
                string color = Color(doc, e.Layer);
                switch (e)
                {
                    case DraftPolyline pl:
                        sb.Append("<path d=\"").Append(PathData(pl, T, modelPerMm)).Append("\" fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"0.25\"/>\n");
                        break;
                    case DraftSpline sp:
                        sb.Append("<polyline points=\"");
                        foreach (var p in sp.Sample()) { var q = T(p); sb.Append(F(q.X)).Append(',').Append(F(q.Y)).Append(' '); }
                        sb.Append("\" fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"0.25\"/>\n");
                        break;
                    case DraftCircle ci:
                        var cc = T(ci.Center);
                        sb.Append("<circle cx=\"").Append(F(cc.X)).Append("\" cy=\"").Append(F(cc.Y)).Append("\" r=\"").Append(F(ci.Radius / modelPerMm))
                          .Append("\" fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"0.25\"/>\n");
                        break;
                    case DraftSymbol s:
                        var sp0 = T(s.Position);
                        foreach (var part in SymbolShapes.Unit(s.Symbol))
                        {
                            string fill = part.Filled ? color : "none";
                            if (part.CircleRadius > 0)
                            {
                                sb.Append("<circle cx=\"").Append(F(sp0.X + part.Points[0].X * s.SizeMm)).Append("\" cy=\"").Append(F(sp0.Y - part.Points[0].Y * s.SizeMm))
                                  .Append("\" r=\"").Append(F(part.CircleRadius * s.SizeMm)).Append("\" fill=\"").Append(fill).Append("\" stroke=\"").Append(color).Append("\" stroke-width=\"0.15\"/>\n");
                            }
                            else
                            {
                                sb.Append(part.Closed ? "<polygon" : "<polyline").Append(" points=\"");
                                foreach (var p in part.Points) sb.Append(F(sp0.X + p.X * s.SizeMm)).Append(',').Append(F(sp0.Y - p.Y * s.SizeMm)).Append(' ');
                                sb.Append("\" fill=\"").Append(fill).Append("\" stroke=\"").Append(color).Append("\" stroke-width=\"0.15\"/>\n");
                            }
                        }
                        break;
                    case DraftText t:
                        var tp = T(t.Position);
                        string anchor = t.H == HAlign.Left ? "start" : t.H == HAlign.Center ? "middle" : "end";
                        string baseline = t.V == VAlign.Bottom ? "auto" : t.V == VAlign.Middle ? "central" : "hanging";
                        double deg = -t.Rotation * 180 / Math.PI;
                        sb.Append("<text x=\"").Append(F(tp.X)).Append("\" y=\"").Append(F(tp.Y)).Append("\" font-size=\"").Append(F(t.HeightMm * 1.35))
                          .Append("\" text-anchor=\"").Append(anchor).Append("\" dominant-baseline=\"").Append(baseline)
                          .Append("\" transform=\"rotate(").Append(F(deg)).Append(' ').Append(F(tp.X)).Append(' ').Append(F(tp.Y)).Append(")\" fill=\"").Append(color).Append("\">")
                          .Append(Esc(t.Text)).Append("</text>\n");
                        break;
                }
            }
            sb.Append("</svg>\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string PathData(DraftPolyline pl, Func<Vec2, Vec2> T, double modelPerMm)
        {
            string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            int n = pl.Vertices.Count;
            var first = T(pl.Vertices[0]);
            sb.Append('M').Append(F(first.X)).Append(' ').Append(F(first.Y));
            int segs = pl.Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                var b = T(pl.Vertices[(i + 1) % n]);
                double bulge = pl.Bulges[i];
                if (bulge == 0) { sb.Append(" L").Append(F(b.X)).Append(' ').Append(F(b.Y)); continue; }
                double theta = 4 * Math.Atan(bulge);
                double chord = Vec2.Distance(pl.Vertices[i], pl.Vertices[(i + 1) % n]) / modelPerMm;
                double r = chord / (2 * Math.Sin(Math.Abs(theta) / 2));
                int large = Math.Abs(theta) > Math.PI ? 1 : 0;
                // Y is flipped on screen, so a CCW (positive) bulge becomes SVG sweep-flag 0.
                int sweep = theta > 0 ? 0 : 1;
                sb.Append(" A").Append(F(r)).Append(' ').Append(F(r)).Append(" 0 ").Append(large).Append(' ').Append(sweep).Append(' ').Append(F(b.X)).Append(' ').Append(F(b.Y));
            }
            if (pl.Closed) sb.Append(" Z");
            return sb.ToString();
        }

        private static readonly Dictionary<int, string> Aci = new Dictionary<int, string>
        {
            { 1, "#d62728" }, { 2, "#b8a000" }, { 3, "#2ca02c" }, { 4, "#17a2b8" }, { 5, "#1f4fd6" }, { 6, "#c2185b" }, { 7, "#222" }, { 8, "#777" }, { 9, "#aaa" },
        };

        private static string Color(DraftDocument doc, string layer) =>
            doc.Layers.TryGetValue(layer, out var l) && Aci.TryGetValue(l.Aci, out var c) ? c : "#222";

        private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
