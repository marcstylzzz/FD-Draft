using System;
using System.Collections.Generic;
using System.Linq;
using FdDraft.Core.Geometry;
using FdDraft.Core.Standards;

namespace FdDraft.View
{
    public enum PlotArea { Layout, Extents, Display, Window }

    /// <summary>Everything the Print dialog decides - the same choices as AutoCAD's plot dialog.</summary>
    public sealed class PlotSetup
    {
        /// <summary>Paper size as it comes off the roll/tray, in mm (width &lt;= height or not - orientation decides).</summary>
        public double PaperWidthMm { get; set; } = 215.9;
        public double PaperHeightMm { get; set; } = 279.4;
        public string PaperName { get; set; } = "Letter";
        public bool Landscape { get; set; } = true;
        public bool UpsideDown { get; set; }
        /// <summary>The printable part of the page, as margins in mm (left, bottom, right, top of the
        /// page as plotted). A PDF has none; a printer reports its own.</summary>
        public double MarginLeft { get; set; }
        public double MarginBottom { get; set; }
        public double MarginRight { get; set; }
        public double MarginTop { get; set; }

        public PlotArea Area { get; set; } = PlotArea.Layout;
        /// <summary>For <see cref="PlotArea.Window"/> and <see cref="PlotArea.Display"/>: the region, in scene units.</summary>
        public Rect? Region { get; set; }

        public bool FitToPaper { get; set; }
        /// <summary>Paper mm per scene unit: 1 for a sheet at 1:1; 1000/n for model space in metres at 1:n.</summary>
        public double MmPerUnit { get; set; } = 1;
        public bool Center { get; set; }
        public double OffsetXMm { get; set; }
        public double OffsetYMm { get; set; }

        /// <summary>Plot style table; null = none (object colours).</summary>
        public PlotStyleTable? Styles { get; set; }
        public bool PlotWithStyles { get; set; } = true;
        public bool PlotLineweights { get; set; } = true;
        public bool ScaleLineweights { get; set; }
        public int Copies { get; set; } = 1;

        /// <summary>The page as it lies when plotted: width across, height up, in mm.</summary>
        public double PageWidth => Landscape ? Math.Max(PaperWidthMm, PaperHeightMm) : Math.Min(PaperWidthMm, PaperHeightMm);
        public double PageHeight => Landscape ? Math.Min(PaperWidthMm, PaperHeightMm) : Math.Max(PaperWidthMm, PaperHeightMm);
    }

    /// <summary>The result of composing a plot: the page, ready to write to PDF or send to a printer.</summary>
    public sealed class PlotPage
    {
        /// <summary>Everything on the page in paper mm (origin bottom-left), pens resolved
        /// (<see cref="Prim.PlotRgb"/> and <see cref="Prim.PenMm"/>).</summary>
        public Scene Page { get; set; } = new Scene();
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        /// <summary>The scale actually used, paper mm per scene unit (what "fit" worked out).</summary>
        public double MmPerUnit { get; set; }
        /// <summary>Where the plotted region lands on the page (mm) - for the preview and for checking it fits.</summary>
        public Rect Placed { get; set; }
        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>
    /// Lays a scene (a sheet or model space) out on paper per a <see cref="PlotSetup"/>: the area
    /// to plot, the scale (or fit), centring/offset, orientation and upside-down, and each item's
    /// pen from the plot style table. PDF and printer output both draw the result.
    /// </summary>
    public static class PlotComposer
    {
        /// <summary>The lineweight AutoCAD plots "Default" with.</summary>
        public const double DefaultLineweightMm = 0.25;

        public static PlotPage Compose(Scene scene, PlotSetup s)
        {
            var result = new PlotPage { WidthMm = s.PageWidth, HeightMm = s.PageHeight };
            // What to plot, in scene units.
            Rect region;
            switch (s.Area)
            {
                case PlotArea.Layout when scene.IsPaper && scene.Paper.HasValue: region = scene.Paper.Value; break;
                case PlotArea.Window when s.Region.HasValue:
                case PlotArea.Display when s.Region.HasValue: region = s.Region!.Value; break;
                default: region = scene.Bounds; break;
            }
            if (region.Width <= 0 || region.Height <= 0) region = new Rect(region.X1, region.Y1, region.X1 + 1, region.Y1 + 1);

            // The printable part of the page.
            var printable = new Rect(s.MarginLeft, s.MarginBottom, s.PageWidth - s.MarginRight, s.PageHeight - s.MarginTop);
            double k = s.FitToPaper ? Math.Min(printable.Width / region.Width, printable.Height / region.Height) : s.MmPerUnit;
            if (!(k > 0)) k = 1;
            result.MmPerUnit = k;
            double w = region.Width * k, h = region.Height * k;
            double left = s.Center ? printable.X1 + (printable.Width - w) / 2 : printable.X1 + s.OffsetXMm;
            double bottom = s.Center ? printable.Y1 + (printable.Height - h) / 2 : printable.Y1 + s.OffsetYMm;
            result.Placed = new Rect(left, bottom, left + w, bottom + h);
            if (w > printable.Width + 0.5 || h > printable.Height + 0.5)
                result.Notes.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "the plot ({0:0} x {1:0} mm) is bigger than the printable area ({2:0} x {3:0} mm) - it will be cut off; pick a bigger paper, Fit to paper, or a smaller scale",
                    w, h, printable.Width, printable.Height));

            // Scene -> page: scale about the region's corner, then place; turned 180 degrees if upside down.
            Affine map = Affine.Translate(left, bottom).After(Affine.Scale(k, k)).After(Affine.Translate(-region.X1, -region.Y1));
            if (s.UpsideDown) map = Affine.Translate(s.PageWidth, s.PageHeight).After(Affine.Rotate(Math.PI)).After(map);

            var page = new Scene { Name = scene.Name, IsPaper = true, Paper = new Rect(0, 0, s.PageWidth, s.PageHeight) };
            // Everything is clipped to what's being plotted (and each viewport to itself).
            var regionOnPage = MapRect(map, region);
            foreach (var g in scene.Groups)
            {
                var clip = g.Clip.HasValue ? Intersect(MapRect(map, g.Clip.Value), regionOnPage) : regionOnPage;
                if (clip == null) continue;
                var pg = new SceneGroup { Clip = clip };
                foreach (var p in g.Prims)
                {
                    if (p.Kind == PrimKind.Node) continue; // construction points don't plot
                    if (!Overlaps(p.Bounds, g.Clip ?? region) && p.Kind != PrimKind.Text) continue;
                    pg.Prims.Add(Place(p, map, k, s));
                }
                page.Groups.Add(pg);
            }
            page.ComputeBounds();
            result.Page = page;
            return result;
        }

        private static Prim Place(Prim p, Affine map, double k, PlotSetup s)
        {
            var pen = s.PlotWithStyles && s.Styles != null ? s.Styles.Pen(p.Aci) : null;
            uint objectRgb = p.Aci == -2 ? p.Rgb : p.PlotRgb;
            uint rgb = pen != null ? PlotStyleTable.PlotColor(pen, objectRgb) : objectRgb;
            double penMm;
            if (!s.PlotLineweights) penMm = 0;
            else
            {
                double lw = pen?.LineWeightMm ?? (p.LineWeightMm >= 0 ? p.LineWeightMm : DefaultLineweightMm);
                penMm = s.ScaleLineweights ? lw * k : lw;
            }
            return new Prim
            {
                Kind = p.Kind,
                Points = p.Points.Select(map.Apply).ToList(),
                Holes = p.Holes?.Select(h => h.Select(map.Apply).ToList()).ToList(),
                Closed = p.Closed,
                Center = map.Apply(p.Center),
                Radius = p.Radius * k,
                Text = p.Text,
                Height = p.Height * k,
                Rotation = p.Rotation + map.Rotation,
                WidthFactor = p.WidthFactor, Font = p.Font, FitWidth = p.FitWidth * k,
                H = p.H, V = p.V,
                Rgb = rgb, PlotRgb = rgb, Aci = p.Aci,
                LineWeightMm = p.LineWeightMm, PenMm = penMm,
                Layer = p.Layer, Handle = p.Handle,
            };
        }

        private static Rect MapRect(Affine m, Rect r)
        {
            var a = m.Apply(r.X1, r.Y1); var b = m.Apply(r.X2, r.Y2);
            return new Rect(a.X, a.Y, b.X, b.Y); // Rect normalises the corners
        }

        private static Rect? Intersect(Rect a, Rect b)
        {
            double x1 = Math.Max(a.X1, b.X1), y1 = Math.Max(a.Y1, b.Y1), x2 = Math.Min(a.X2, b.X2), y2 = Math.Min(a.Y2, b.Y2);
            return x1 < x2 && y1 < y2 ? new Rect(x1, y1, x2, y2) : (Rect?)null;
        }

        private static bool Overlaps(Rect a, Rect b) => a.X1 <= b.X2 && b.X1 <= a.X2 && a.Y1 <= b.Y2 && b.Y1 <= a.Y2;
    }
}
