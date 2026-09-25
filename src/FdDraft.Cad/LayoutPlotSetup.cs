using System;
using System.IO;
using ACadSharp.Objects;
using FdDraft.Core.Standards;
using FdDraft.View;

namespace FdDraft.Cad
{
    /// <summary>
    /// A layout's page setup as the DWG stores it (printer, paper, plot style table, what to
    /// plot, scale, offset, flags) to and from FD-Draft's <see cref="PlotSetup"/> - so the Print
    /// dialog opens with the sheet's own settings and "Apply to Layout" saves them in the DWG,
    /// where AutoCAD and MSCAD read them too.
    /// </summary>
    public static class LayoutPlotSetup
    {
        /// <summary>The printer name FD-Draft's own PDF output is saved under.</summary>
        public const string PdfPrinter = "FD-Draft PDF";

        public static PlotSetup Read(Layout l, out string? styleSheet, out string? printer)
        {
            styleSheet = string.IsNullOrWhiteSpace(l.StyleSheet) ? null : l.StyleSheet;
            printer = string.IsNullOrWhiteSpace(l.SystemPrinterName) || l.SystemPrinterName == "none_device" ? null : l.SystemPrinterName;
            double unit = l.PaperUnits == PlotPaperUnits.Inches ? 25.4 : 1;
            double pw = l.PaperWidth, ph = l.PaperHeight; // the paper as fed, in mm
            bool turned = l.PaperRotation == PlotRotation.Degrees90 || l.PaperRotation == PlotRotation.Degrees270;
            double across = turned ? ph : pw, up = turned ? pw : ph;
            var s = new PlotSetup
            {
                PaperWidthMm = pw > 0 ? pw : 215.9, PaperHeightMm = ph > 0 ? ph : 279.4,
                PaperName = PaperSize.Match(pw, ph)?.Name ?? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.#} x {1:0.#} mm", pw, ph),
                Landscape = across > up,
                UpsideDown = l.PaperRotation == PlotRotation.Degrees180 || l.PaperRotation == PlotRotation.Degrees270,
                Center = l.Flags.HasFlag(PlotFlags.PlotCentered),
                PlotLineweights = l.Flags.HasFlag(PlotFlags.PrintLineweights),
                PlotWithStyles = l.Flags.HasFlag(PlotFlags.PlotPlotStyles),
                ScaleLineweights = l.Flags.HasFlag(PlotFlags.ScaleLineweights),
                OffsetXMm = l.PlotOriginX * unit, OffsetYMm = l.PlotOriginY * unit,
                // "Fit" is the standard-scale flag with the scaled-to-fit entry; any other scale (a
                // standard one or custom) is kept as the paper : drawing ratio.
                FitToPaper = l.Flags.HasFlag(PlotFlags.UseStandardScale) && l.ScaledFit == ScaledType.ScaledToFit,
                MmPerUnit = l.DenominatorScale > 0 ? l.NumeratorScale / l.DenominatorScale * unit : 1,
                Area = l.PlotType switch
                {
                    PlotType.DrawingExtents => PlotArea.Extents,
                    PlotType.Window => PlotArea.Window,
                    PlotType.LastScreenDisplay => PlotArea.Display,
                    _ => PlotArea.Layout,
                },
            };
            if (s.Area == PlotArea.Window)
                s.Region = new Rect(l.WindowLowerLeftX, l.WindowLowerLeftY, l.WindowUpperLeftX, l.WindowUpperLeftY);
            return s;
        }

        /// <summary>Saves a setup into the layout (AutoCAD's "Apply to Layout"). Changing the paper
        /// changes the sheet's size, exactly as it does in AutoCAD.</summary>
        public static void Write(Layout l, PlotSetup s, string? styleSheetPath, string printer)
        {
            // Just the file name, as AutoCAD stores it (it looks the table up in its own folders).
            l.StyleSheet = styleSheetPath == null ? "" : styleSheetPath.Substring(styleSheetPath.LastIndexOfAny(new[] { '\\', '/' }) + 1);
            l.SystemPrinterName = printer;
            l.PaperUnits = PlotPaperUnits.Millimeters;
            // Stored as fed (portrait, narrow side first) and turned for landscape.
            l.PaperWidth = Math.Min(s.PaperWidthMm, s.PaperHeightMm);
            l.PaperHeight = Math.Max(s.PaperWidthMm, s.PaperHeightMm);
            l.PaperSize = s.PaperName;
            l.PaperRotation = s.Landscape ? (s.UpsideDown ? PlotRotation.Degrees270 : PlotRotation.Degrees90) : (s.UpsideDown ? PlotRotation.Degrees180 : PlotRotation.NoRotation);
            var f = l.Flags & ~(PlotFlags.PlotCentered | PlotFlags.PrintLineweights | PlotFlags.PlotPlotStyles | PlotFlags.ScaleLineweights | PlotFlags.UseStandardScale);
            if (s.Center) f |= PlotFlags.PlotCentered;
            if (s.PlotLineweights) f |= PlotFlags.PrintLineweights;
            if (s.PlotWithStyles) f |= PlotFlags.PlotPlotStyles;
            if (s.ScaleLineweights) f |= PlotFlags.ScaleLineweights;
            l.Flags = f;
            l.PlotOriginX = s.OffsetXMm; l.PlotOriginY = s.OffsetYMm;
            l.PlotType = s.Area switch
            {
                PlotArea.Extents => PlotType.DrawingExtents,
                PlotArea.Window => PlotType.Window,
                PlotArea.Display => PlotType.LastScreenDisplay,
                _ => PlotType.LayoutInformation,
            };
            if (s.Area == PlotArea.Window && s.Region.HasValue)
            {
                var r = s.Region.Value;
                l.WindowLowerLeftX = r.X1; l.WindowLowerLeftY = r.Y1; l.WindowUpperLeftX = r.X2; l.WindowUpperLeftY = r.Y2;
            }
            if (s.FitToPaper) { l.Flags |= PlotFlags.UseStandardScale; l.ScaledFit = ScaledType.ScaledToFit; }
            else if (s.MmPerUnit > 0)
            {
                // mm on paper : drawing units, e.g. 1 : 0.5 for metres at 1:500.
                l.NumeratorScale = 1;
                l.DenominatorScale = 1 / s.MmPerUnit;
            }
        }
    }
}
