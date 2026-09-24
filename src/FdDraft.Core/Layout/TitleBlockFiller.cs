using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using FdDraft.Core.Job;
using FdDraft.Core.Standards;

namespace FdDraft.Core.Layout
{
    /// <summary>
    /// Fills in a title block that is made of plain text rather than attributes
    /// (the ProVision template's case): each [titleblock-replace] rule is a regex
    /// run over every paper-space text of the chosen layout, with tokens in the
    /// replacement. Also relabels the scale bar for the chosen scale.
    /// </summary>
    public sealed class TitleBlockFiller
    {
        private readonly List<KeyValuePair<Regex, string>> _rules = new List<KeyValuePair<Regex, string>>();
        private readonly Dictionary<string, string> _tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public TitleBlockFiller(FirmStandards std, JobSettings job, ScaleOption scale, string layout, string drawingName, DateTime date, double paperWidthMm = 0, double paperHeightMm = 0)
        {
            foreach (var kv in std.TitleBlockReplacements)
            {
                try { _rules.Add(new KeyValuePair<Regex, string>(new Regex(kv.Key, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), kv.Value)); }
                catch (ArgumentException) { /* a bad pattern is reported by Validate(), not fatal here */ }
            }
            _tokens["job.name"] = job.Name;
            _tokens["job.id"] = job.Id;
            _tokens["job.surveyor"] = job.Surveyor;
            _tokens["job.description"] = job.Description;
            _tokens["scale"] = scale.Label;
            _tokens["scale.denominator"] = scale.Denominator.ToString("0.###", CultureInfo.InvariantCulture);
            _tokens["sheet"] = layout;
            _tokens["dwg.name"] = drawingName;
            _tokens["date"] = date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
            _tokens["date.iso"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _tokens["year"] = date.Year.ToString(CultureInfo.InvariantCulture);
            _tokens["paper.width"] = Math.Round(paperWidthMm).ToString("0", CultureInfo.InvariantCulture);
            _tokens["paper.height"] = Math.Round(paperHeightMm).ToString("0", CultureInfo.InvariantCulture);
        }

        public static List<string> Validate(FirmStandards std)
        {
            var errors = new List<string>();
            foreach (var kv in std.TitleBlockReplacements)
            {
                try { new Regex(kv.Key); }
                catch (ArgumentException e) { errors.Add("[titleblock-replace] '" + kv.Key + "': " + e.Message); }
            }
            return errors;
        }

        /// <summary>The text with every matching rule applied, or null when nothing matched.</summary>
        public string? Apply(string text)
        {
            string result = text;
            bool changed = false;
            foreach (var rule in _rules)
            {
                if (!rule.Key.IsMatch(result)) continue;
                result = rule.Key.Replace(result, Expand(rule.Value));
                changed = true;
            }
            return changed && result != text ? result : null;
        }

        private string Expand(string replacement)
        {
            foreach (var kv in _tokens) replacement = replacement.Replace("{" + kv.Key + "}", kv.Value);
            return replacement;
        }

        /// <summary>
        /// A scale-bar tick label ("0", "7.5", "24m") redrawn for a new scale: the tick
        /// sits at a fixed paper distance, so its ground value scales with the denominator.
        /// Returns null for text that is not a tick label.
        /// </summary>
        public static string? RelabelTick(string label, double oldDenominator, double newDenominator)
        {
            var m = Regex.Match(label.Trim(), @"^([0-9]+(?:\.[0-9]+)?)\s*(m|ft|')?$");
            if (!m.Success || oldDenominator <= 0) return null;
            double v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * newDenominator / oldDenominator;
            return Nice(v) + m.Groups[2].Value;
        }

        /// <summary>Scale denominator in a "SCALE 1:300" style anchor text.</summary>
        public static double? DenominatorIn(string text)
        {
            var m = Regex.Match(text, @"1\s*:\s*([0-9]+(?:\.[0-9]+)?)");
            return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : (double?)null;
        }

        private static string Nice(double v)
        {
            if (Math.Abs(v - Math.Round(v)) < 1e-9) return Math.Round(v).ToString("0", CultureInfo.InvariantCulture);
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
