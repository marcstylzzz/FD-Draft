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

        /// <summary>The same, sized from the sheet's own layout.</summary>
        public static bool ShowsModel(Viewport vp, ACadSharp.Objects.Layout layout) => ShowsModel(vp, layout.PaperWidth, layout.PaperHeight);
    }
}
