using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Printing;
using System.Windows.Documents;
using System.Windows.Markup;
using FdDraft.View;

namespace FdDraft.App
{
    /// <summary>
    /// The Windows printers FD-Draft can plot to (anything installed: plotters, office printers,
    /// "Microsoft Print to PDF"), their paper sizes and printable margins, and the actual
    /// printing - each page is a <see cref="PlotPageElement"/> at true size.
    /// </summary>
    public static class PlotPrinters
    {
        private const double MmPerDip = 25.4 / 96;

        /// <summary>Installed and connected printers' names; empty if the print spooler can't be reached.</summary>
        public static List<string> Names()
        {
            try
            {
                using var server = new LocalPrintServer();
                return server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections })
                    .Select(q => q.FullName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception e) when (e is PrintSystemException || e is InvalidOperationException || e is UnauthorizedAccessException)
            {
                return new List<string>();
            }
        }

        private static PrintQueue? Queue(string name)
        {
            try
            {
                var server = new LocalPrintServer();
                return server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections })
                    .FirstOrDefault(q => q.FullName == name);
            }
            catch (Exception e) when (e is PrintSystemException || e is InvalidOperationException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>The paper sizes a printer offers, named as the driver names them.</summary>
        public static List<PaperSize> PaperSizes(string printer)
        {
            var result = new List<PaperSize>();
            var q = Queue(printer);
            if (q == null) return result;
            try
            {
                foreach (var m in q.GetPrintCapabilities().PageMediaSizeCapability)
                {
                    if (!(m.Width > 0) || !(m.Height > 0)) continue;
                    double w = m.Width!.Value * MmPerDip, h = m.Height!.Value * MmPerDip;
                    string name = m.PageMediaSizeName?.ToString() ?? "Custom";
                    result.Add(new PaperSize(string.Format(CultureInfo.InvariantCulture, "{0} ({1:0} x {2:0} mm)", name, Math.Min(w, h), Math.Max(w, h)), w, h));
                }
            }
            catch (Exception e) when (e is PrintQueueException || e is PrintSystemException || e is InvalidOperationException) { }
            return result;
        }

        /// <summary>
        /// The printer's unprintable margin for a paper size, as one figure all round (mm) - the
        /// largest of its four edges, so nothing FD-Draft places inside it gets clipped whichever
        /// way the driver turns the page.
        /// </summary>
        public static double Margin(string printer, PaperSize paper)
        {
            var q = Queue(printer);
            if (q == null) return 0;
            try
            {
                var ticket = Ticket(q, paper, landscape: false, copies: 1);
                var area = q.GetPrintCapabilities(ticket).PageImageableArea;
                if (area == null) return 0;
                double w = paper.WidthMm, h = paper.HeightMm;
                double left = area.OriginWidth * MmPerDip, top = area.OriginHeight * MmPerDip;
                double right = w - left - area.ExtentWidth * MmPerDip, bottom = h - top - area.ExtentHeight * MmPerDip;
                return Math.Max(0, Math.Max(Math.Max(left, top), Math.Max(right, bottom)));
            }
            catch (Exception e) when (e is PrintQueueException || e is PrintSystemException || e is InvalidOperationException) { return 0; }
        }

        private static PrintTicket Ticket(PrintQueue q, PaperSize paper, bool landscape, int copies)
        {
            var t = q.DefaultPrintTicket.Clone();
            t.PageMediaSize = new PageMediaSize(paper.WidthMm / MmPerDip, paper.HeightMm / MmPerDip);
            t.PageOrientation = landscape ? PageOrientation.Landscape : PageOrientation.Portrait;
            t.CopyCount = Math.Max(1, copies);
            return t;
        }

        /// <summary>Sends a composed page to a printer. Returns null when sent, or why it wasn't.</summary>
        public static string? Print(string printer, PlotPage page, PlotSetup setup, string title)
        {
            var q = Queue(printer);
            if (q == null) return "the printer \"" + printer + "\" can't be reached";
            try
            {
                var paper = new PaperSize(setup.PaperName, setup.PaperWidthMm, setup.PaperHeightMm);
                var ticket = Ticket(q, paper, setup.Landscape, setup.Copies);
                // One fixed page the size of the paper as it's plotted (landscape = wide), holding
                // the page drawn at true size; the ticket tells the driver the paper and turn.
                var element = new PlotPageElement(page, preview: false);
                var fixedPage = new FixedPage { Width = element.Width, Height = element.Height };
                fixedPage.Children.Add(element);
                var content = new PageContent();
                ((IAddChild)content).AddChild(fixedPage);
                var doc = new FixedDocument();
                doc.DocumentPaginator.PageSize = new System.Windows.Size(element.Width, element.Height);
                doc.Pages.Add(content);
                q.CurrentJobSettings.Description = title;
                var writer = PrintQueue.CreateXpsDocumentWriter(q);
                writer.Write(doc, ticket);
                return null;
            }
            catch (Exception e) when (e is PrintQueueException || e is PrintSystemException || e is InvalidOperationException || e is System.IO.IOException)
            {
                return e.Message;
            }
        }
    }
}
