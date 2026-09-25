using System.Linq;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace FdDraft.View
{
    /// <summary>
    /// Which of a sheet's viewports is the "paper background" one (it shows the sheet itself,
    /// never model space) and which show the plan.
    /// </summary>
    /// <remarks>
    /// ACadSharp doesn't read a viewport's number from the DWG: <see cref="Viewport.Id"/> is its
    /// position among the viewports in the sheet's block, and <see cref="Viewport.RepresentsPaper"/>
    /// is simply "Id == 1". That is right when the background viewport is there and comes first,
    /// but a sheet tab that was never opened in AutoCAD can hold only its plan viewport - which
    /// then comes out as #1 and would be thrown away as "paper". So a lone #1 viewport only counts
    /// as the background when it actually looks at paper: its view centre lies around the sheet
    /// rather than out at the survey's coordinates.
    /// </remarks>
    public static class ViewportRules
    {
        public static bool IsPaperBackground(Viewport vp, double paperWidth, double paperHeight)
        {
            if (!vp.RepresentsPaper) return false;
            if (vp.Owner is BlockRecord block && block.Entities.OfType<Viewport>().Count() > 1) return true;
            // The paper view's centre is in sheet units: somewhere on or near the sheet.
            double w = paperWidth > 0 ? paperWidth : vp.Width, h = paperHeight > 0 ? paperHeight : vp.Height;
            bool nearSheet = vp.ViewCenter.X >= -w && vp.ViewCenter.X <= 2 * w && vp.ViewCenter.Y >= -h && vp.ViewCenter.Y <= 2 * h;
            return nearSheet;
        }

        /// <summary>A viewport that shows model space on its sheet.</summary>
        public static bool ShowsModel(Viewport vp, double paperWidth, double paperHeight) =>
            !IsPaperBackground(vp, paperWidth, paperHeight) && !vp.Status.HasFlag(ViewportStatusFlags.ViewportOff) && vp.ViewHeight > 0 && vp.Height > 0 && vp.Width > 0;

        /// <summary>
        /// One line per viewport on a sheet, with everything that decides whether it shows model
        /// space - for VPINFO, so a sheet that comes up blank can be diagnosed from the log.
        /// </summary>
        public static System.Collections.Generic.List<string> Describe(ACadSharp.Objects.Layout layout)
        {
            var lines = new System.Collections.Generic.List<string>();
            var ents = layout.AssociatedBlock.Entities.ToList();
            var vps = ents.OfType<Viewport>().ToList();
            lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}: paper {1:0.#} x {2:0.#}, {3} viewport(s), {4} other paper entities", layout.Name, layout.PaperWidth, layout.PaperHeight, vps.Count, ents.Count - vps.Count));
            foreach (var vp in vps)
            {
                string verdict = IsPaperBackground(vp, layout.PaperWidth, layout.PaperHeight) ? "paper background"
                    : ShowsModel(vp, layout) ? "SHOWS MODEL"
                    : vp.Status.HasFlag(ViewportStatusFlags.ViewportOff) ? "switched off"
                    : "empty (zero size or view height)";
                lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "  #{0} {1}: centre {2:0.##},{3:0.##} size {4:0.##} x {5:0.##}; looks at {6:0.###},{7:0.###}, view height {8:0.###}{9}; status {10}; layer {11}",
                    vp.Id, verdict, vp.Center.X, vp.Center.Y, vp.Width, vp.Height, vp.ViewCenter.X, vp.ViewCenter.Y, vp.ViewHeight,
                    vp.ViewHeight > 0 && vp.Height > 0 ? " (1:" + (vp.ViewHeight / vp.Height * 1000).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " if m on mm)" : "",
                    (int)vp.Status, vp.Layer?.Name ?? "?"));
            }
            return lines;
        }

        /// <summary>The same, sized from the sheet's own layout.</summary>
        public static bool ShowsModel(Viewport vp, ACadSharp.Objects.Layout layout) => ShowsModel(vp, layout.PaperWidth, layout.PaperHeight);
    }
}
