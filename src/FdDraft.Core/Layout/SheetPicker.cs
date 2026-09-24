using System;
using System.Collections.Generic;
using System.Globalization;
using FdDraft.Core.Drafting;
using FdDraft.Core.Standards;

namespace FdDraft.Core.Layout
{
    /// <summary>
    /// One place the plan could go: a paper-space layout of the template and a free
    /// rectangle inside its frame that avoids the title column and schedule boxes.
    /// </summary>
    public sealed class SheetCandidate
    {
        /// <summary>The layout name in the template (17X22, RPLAN-22X34 ...).</summary>
        public string Layout { get; set; } = "";
        /// <summary>Where the viewport goes, in paper units.</summary>
        public Rect Area { get; set; }
        public double PaperArea { get; set; }
        public string Name => Layout;
    }

    public sealed class SheetChoice
    {
        public SheetCandidate Sheet { get; set; } = new SheetCandidate();
        public ScaleOption Scale { get; set; } = new ScaleOption();
        /// <summary>Share of labelled courses long enough on paper to carry their bearing and distance.</summary>
        public double Legibility { get; set; }
        /// <summary>How much of the viewport the survey fills on its tighter axis, 0..1.</summary>
        public double Fill { get; set; }
        public bool MeetsLegibility { get; set; }
        public string Reason { get; set; } = "";
        public override string ToString() => Sheet.Layout + " @ " + Scale.Label + " - " + Reason;
    }

    /// <summary>
    /// Chooses the sheet and scale:
    ///  1. For every layout and every free area on it, the largest standard scale at
    ///     which the whole survey plus a margin fits.
    ///  2. Keep options where at least MinLegibleFraction of the labelled courses are
    ///     long enough on paper for their bearing and distance.
    ///  3. Of those, the smallest paper; ties go to the larger scale.
    ///  4. If nothing reaches the legibility bar, the most legible option.
    /// Everything is returned ranked with a reason, so the drafter - or the assistant -
    /// can see why and override.
    /// </summary>
    public static class SheetPicker
    {
        public static List<SheetChoice> Rank(DraftDocument doc, IEnumerable<SheetCandidate> sheets, FirmStandards std)
        {
            var ext = doc.GeometryExtents();
            var best = new Dictionary<string, SheetChoice>(StringComparer.OrdinalIgnoreCase);
            if (ext.IsEmpty) return new List<SheetChoice>();
            double margin = 1 - Math.Max(0, Math.Min(std.MarginPct, 45)) / 100.0;
            double pad = 2 * (std.BearingTextMm + std.DistanceTextMm) * std.PaperUnitsPerMm;

            foreach (var sheet in sheets)
            {
                double usableW = sheet.Area.Width * margin, usableH = sheet.Area.Height * margin;
                if (usableW <= 0 || usableH <= 0) continue;
                ScaleOption? fit = null;
                foreach (var scale in std.Scales) // largest scale first
                {
                    if (ext.Width / scale.ModelPerPaper + 2 * pad <= usableW && ext.Height / scale.ModelPerPaper + 2 * pad <= usableH) { fit = scale; break; }
                }
                if (fit == null) continue;
                var choice = new SheetChoice
                {
                    Sheet = sheet,
                    Scale = fit,
                    Legibility = Legibility(doc, fit, std),
                    Fill = Math.Max(ext.Width / fit.ModelPerPaper / sheet.Area.Width, ext.Height / fit.ModelPerPaper / sheet.Area.Height),
                };
                choice.MeetsLegibility = choice.Legibility >= std.MinLegibleFraction;
                // One entry per layout: the free area that allows the larger scale, then the bigger area.
                if (!best.TryGetValue(sheet.Layout, out var prev) ||
                    fit.ModelPerPaper < prev.Scale.ModelPerPaper ||
                    (fit.ModelPerPaper == prev.Scale.ModelPerPaper && sheet.Area.Area > prev.Sheet.Area.Area))
                    best[sheet.Layout] = choice;
            }

            var options = new List<SheetChoice>(best.Values);
            options.Sort((a, b) =>
            {
                if (a.MeetsLegibility != b.MeetsLegibility) return a.MeetsLegibility ? -1 : 1;
                if (!a.MeetsLegibility)
                {
                    int l = b.Legibility.CompareTo(a.Legibility);
                    if (l != 0) return l;
                }
                int area = a.Sheet.PaperArea.CompareTo(b.Sheet.PaperArea);
                if (area != 0) return area;
                return a.Scale.ModelPerPaper.CompareTo(b.Scale.ModelPerPaper);
            });

            for (int i = 0; i < options.Count; i++)
            {
                var o = options[i];
                string leg = (o.Legibility * 100).ToString("F0", CultureInfo.InvariantCulture) + "% of labelled courses legible";
                string fill = (o.Fill * 100).ToString("F0", CultureInfo.InvariantCulture) + "% of plan area used";
                o.Reason = (i == 0
                    ? (o.MeetsLegibility ? "smallest sheet that fits at a legible scale; " : "nothing reaches the legibility target - most legible option; ")
                    : "") + leg + ", " + fill;
            }
            return options;
        }

        /// <summary>
        /// Candidates for a sheet family: every maximal free rectangle of every layout
        /// the standards describe. Layouts without a [sheet.X] section are skipped.
        /// </summary>
        public static List<SheetCandidate> Candidates(FirmStandards std, SheetFamily? family)
        {
            var list = new List<SheetCandidate>();
            IEnumerable<string> layouts = family != null ? family.Layouts : (IEnumerable<string>)std.Sheets.Keys;
            foreach (var name in layouts)
            {
                if (!std.Sheets.TryGetValue(name, out var def)) continue;
                double paper = def.PaperWidth > 0 ? def.PaperWidth * def.PaperHeight : def.Frame.Area;
                foreach (var r in FreeRects(def.Frame, def.Keepouts))
                    list.Add(new SheetCandidate { Layout = def.Layout, Area = r, PaperArea = paper });
            }
            return list;
        }

        /// <summary>The maximal rectangles inside the frame that touch no keepout.</summary>
        public static List<Rect> FreeRects(Rect frame, IList<Rect> keepouts)
        {
            var xs = new SortedSet<double> { frame.X1, frame.X2 };
            var ys = new SortedSet<double> { frame.Y1, frame.Y2 };
            foreach (var k in keepouts)
            {
                foreach (var x in new[] { k.X1, k.X2 }) if (x > frame.X1 && x < frame.X2) xs.Add(x);
                foreach (var y in new[] { k.Y1, k.Y2 }) if (y > frame.Y1 && y < frame.Y2) ys.Add(y);
            }
            var xl = new List<double>(xs);
            var yl = new List<double>(ys);
            var valid = new List<Rect>();
            for (int a = 0; a < xl.Count; a++)
                for (int b = a + 1; b < xl.Count; b++)
                    for (int c = 0; c < yl.Count; c++)
                        for (int d = c + 1; d < yl.Count; d++)
                        {
                            var r = new Rect(xl[a], yl[c], xl[b], yl[d]);
                            bool clear = true;
                            foreach (var k in keepouts) if (r.Overlaps(k)) { clear = false; break; }
                            if (clear) valid.Add(r);
                        }
            var maximal = new List<Rect>();
            foreach (var r in valid)
            {
                bool contained = false;
                foreach (var o in valid)
                {
                    if (o.Area > r.Area && o.X1 <= r.X1 && o.Y1 <= r.Y1 && o.X2 >= r.X2 && o.Y2 >= r.Y2) { contained = true; break; }
                }
                if (!contained) maximal.Add(r);
            }
            return maximal;
        }

        /// <summary>
        /// Share of labelled courses whose paper length can hold the longer of their two
        /// labels. Text width is estimated at 0.8 x height per character, conservative
        /// for msurvey.shx / romans and most plan fonts.
        /// </summary>
        public static double Legibility(DraftDocument doc, ScaleOption scale, FirmStandards std)
        {
            int total = 0, ok = 0;
            int bearingChars = 12 + (std.BearingSecondsDecimals > 0 ? std.BearingSecondsDecimals + 1 : 0);
            double needMm = Math.Max(bearingChars * std.BearingTextMm, 8 * std.DistanceTextMm) * 0.8 + 2 * std.BearingTextMm;
            double modelPerMm = scale.ModelPerPaper * std.PaperUnitsPerMm;
            foreach (var c in doc.Courses)
            {
                if (!c.Labelled) continue;
                total++;
                if (c.Length / modelPerMm >= needMm) ok++;
            }
            return total == 0 ? 1.0 : (double)ok / total;
        }
    }
}
