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
    /// Writes a DraftDocument as an AutoCAD R12 ASCII DXF: the one CAD format every
    /// program opens. No template is applied - this is the preview/interchange path
    /// for machines without AutoCAD. Splines are written as fitted polylines and
    /// symbols as exploded linework, because R12 has neither SPLINE nor our blocks.
    /// </summary>
    public static class DxfWriter
    {
        public static void Write(DraftDocument doc, string path, double modelPerMm)
        {
            var sb = new StringBuilder();
            void G(int code, string value) { sb.Append(code.ToString(CultureInfo.InvariantCulture)).Append("\r\n").Append(value).Append("\r\n"); }
            void D(int code, double value) => G(code, value.ToString("0.0########", CultureInfo.InvariantCulture));

            var ext = doc.GeometryExtents();
            G(0, "SECTION"); G(2, "HEADER");
            G(9, "$ACADVER"); G(1, "AC1009");
            G(9, "$DWGCODEPAGE"); G(3, "ANSI_1252");
            if (!ext.IsEmpty)
            {
                G(9, "$EXTMIN"); D(10, ext.MinX); D(20, ext.MinY); D(30, 0);
                G(9, "$EXTMAX"); D(10, ext.MaxX); D(20, ext.MaxY); D(30, 0);
            }
            G(0, "ENDSEC");

            G(0, "SECTION"); G(2, "TABLES");
            G(0, "TABLE"); G(2, "LTYPE"); G(70, "1");
            G(0, "LTYPE"); G(2, "CONTINUOUS"); G(70, "0"); G(3, "Solid line"); G(72, "65"); G(73, "0"); D(40, 0);
            G(0, "ENDTAB");
            G(0, "TABLE"); G(2, "LAYER"); G(70, doc.Layers.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var layer in doc.Layers.Values)
            {
                G(0, "LAYER"); G(2, layer.Name); G(70, "0");
                G(62, Math.Max(1, Math.Min(255, layer.Aci)).ToString(CultureInfo.InvariantCulture));
                G(6, "CONTINUOUS");
            }
            G(0, "ENDTAB");
            G(0, "ENDSEC");

            G(0, "SECTION"); G(2, "ENTITIES");
            foreach (var entity in doc.Entities)
            {
                switch (entity)
                {
                    case DraftPolyline pl:
                        Polyline(G, D, pl.Layer, pl.Vertices, pl.Bulges, pl.Closed);
                        break;
                    case DraftSpline sp:
                        var samples = sp.Sample();
                        Polyline(G, D, sp.Layer, samples, null, false);
                        break;
                    case DraftCircle c:
                        G(0, "CIRCLE"); G(8, c.Layer); D(10, c.Center.X); D(20, c.Center.Y); D(30, 0); D(40, c.Radius);
                        break;
                    case DraftSymbol s:
                        G(0, "POINT"); G(8, s.Layer); D(10, s.Position.X); D(20, s.Position.Y); D(30, s.Elevation);
                        double size = s.SizeMm * modelPerMm;
                        foreach (var part in SymbolShapes.Unit(s.Symbol))
                        {
                            if (part.CircleRadius > 0)
                            {
                                var ctr = s.Position + part.Points[0] * size;
                                G(0, "CIRCLE"); G(8, s.Layer); D(10, ctr.X); D(20, ctr.Y); D(30, 0); D(40, part.CircleRadius * size);
                            }
                            else
                            {
                                var pts = new List<Vec2>();
                                foreach (var p in part.Points) pts.Add(s.Position + p * size);
                                Polyline(G, D, s.Layer, pts, null, part.Closed);
                            }
                        }
                        break;
                    case DraftText t:
                        double hgt = t.HeightMm * modelPerMm;
                        G(0, "TEXT"); G(8, t.Layer);
                        D(10, t.Position.X); D(20, t.Position.Y); D(30, 0);
                        D(40, hgt);
                        G(1, DxfText(t.Text));
                        D(50, t.Rotation * 180.0 / Math.PI);
                        int h = t.H == HAlign.Left ? 0 : t.H == HAlign.Center ? 1 : 2;
                        int v = t.V == VAlign.Bottom ? 1 : t.V == VAlign.Middle ? 2 : 3;
                        G(72, h.ToString(CultureInfo.InvariantCulture));
                        D(11, t.Position.X); D(21, t.Position.Y); D(31, 0);
                        G(73, v.ToString(CultureInfo.InvariantCulture));
                        break;
                }
            }
            G(0, "ENDSEC");
            G(0, "EOF");
            // Latin-1 so ² survives; ° goes out as %%d, which every AutoCAD font understands.
            File.WriteAllText(path, sb.ToString(), Encoding.GetEncoding(28591));
        }

        public static string DxfText(string text) => text.Replace("°", "%%d");

        private static void Polyline(Action<int, string> G, Action<int, double> D, string layer, IList<Vec2> vertices, IList<double>? bulges, bool closed)
        {
            G(0, "POLYLINE"); G(8, layer); G(66, "1"); D(10, 0); D(20, 0); D(30, 0);
            G(70, closed ? "1" : "0");
            for (int i = 0; i < vertices.Count; i++)
            {
                G(0, "VERTEX"); G(8, layer); D(10, vertices[i].X); D(20, vertices[i].Y); D(30, 0);
                double b = bulges != null && i < bulges.Count ? bulges[i] : 0;
                if (b != 0) D(42, b);
            }
            G(0, "SEQEND"); G(8, layer);
        }
    }
}
