using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FdDraft.App
{
    /// <summary>
    /// Windows' own TrueType faces for drawn text (a company name in Broadway stays Broadway):
    /// the typeface and its cap-height ratio, Arial when a face isn't installed. Also the
    /// measurer <see cref="FdDraft.View.TextFonts"/> lays MTEXT out with.
    /// </summary>
    public static class WpfFonts
    {
        private static readonly Dictionary<string, (Typeface Face, double CapRatio, bool Real)> Cache =
            new Dictionary<string, (Typeface, double, bool)>(StringComparer.OrdinalIgnoreCase);
        public static readonly Typeface Arial = new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        public const double ArialCapRatio = 0.716;

        public static (Typeface Face, double CapRatio) Of(string? family)
        {
            var f = Lookup(family);
            return (f.Face, f.CapRatio);
        }

        private static (Typeface Face, double CapRatio, bool Real) Lookup(string? family)
        {
            if (string.IsNullOrWhiteSpace(family)) return (Arial, ArialCapRatio, false);
            if (Cache.TryGetValue(family, out var hit)) return hit;
            var tf = new Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            (Typeface, double, bool) result = (Arial, ArialCapRatio, false);
            if (tf.TryGetGlyphTypeface(out var g) && g.FamilyNames.Values is var names)
            {
                bool matches = false;
                foreach (var n in names) if (n.Equals(family, StringComparison.OrdinalIgnoreCase)) matches = true;
                // AutoCAD sizes every TrueType face by the same em-to-height ratio (Arial's): a display
                // face like Broadway comes out with shorter capitals, not a bigger em. Scaling by the
                // face's own cap height made "M&M SURVEYING LTD." too wide to fit its box.
                if (matches) result = (tf, ArialCapRatio, true);
            }
            Cache[family] = result;
            return result;
        }

        /// <summary>Width of <paramref name="text"/> in <paramref name="family"/> at cap height
        /// <paramref name="cap"/>; null when that face isn't installed.</summary>
        public static double? Measure(string family, string text, double cap)
        {
            var f = Lookup(family);
            if (!f.Real) return null;
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, f.Face, 100, Brushes.Black, 1.0);
            return ft.WidthIncludingTrailingWhitespace * cap / (100 * f.CapRatio);
        }
    }
}
