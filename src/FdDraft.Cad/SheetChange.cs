using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using FdDraft.Cad.Editing;
using FdDraft.Core.Geometry;
using FdDraft.Core.Layout;
using FdDraft.Core.Standards;

namespace FdDraft.Cad
{
    /// <summary>
    /// CHANGESHEET: puts the plan on another of the firm template's sheets without drafting it
    /// again. The sheet (frame, title block, notes, plot settings) is copied from the template
    /// into the drawing as a new layout tab; a plan viewport is fitted to the free area at the
    /// largest standard scale that holds the plan (or a scale asked for); the north arrow and
    /// title block are filled the way Draft does. Model space - every edit made since drafting,
    /// every label link - is untouched, and the old sheet stays until it's deleted.
    /// </summary>
    public static class SheetChange
    {
        public sealed class Result
        {
            public Layout Layout { get; set; } = null!;
            public ScaleOption Scale { get; set; } = new ScaleOption();
            public Rect Area { get; set; }
            public List<string> Report { get; } = new List<string>();
            public IEditCommand Command { get; set; } = null!;
        }

        private static readonly FieldInfo? XDictionaryField = typeof(CadObject).GetField("_xdictionary", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// A clone of <paramref name="e"/> ready to go into another drawing. ACadSharp's clone
        /// keeps the source's extended dictionaries (MTEXT fields), which can't join a second
        /// document - they're left behind, as AutoCAD does when fields are pasted as text.
        /// </summary>
        public static Entity CloneForTransfer(Entity e)
        {
            var c = (Entity)e.Clone();
            Strip(c);
            return c;
        }

        private static void Strip(Entity e)
        {
            XDictionaryField?.SetValue(e, null);
            if (e is Insert i)
            {
                foreach (var a in i.Attributes) XDictionaryField?.SetValue(a, null);
                if (i.Block != null && i.Block.Document == null)
                    foreach (var be in i.Block.Entities) Strip(be);
            }
        }

        /// <summary>
        /// A copy of <paramref name="source"/> (a layout of another drawing) added to
        /// <paramref name="target"/> as <paramref name="name"/>: its plot settings and everything
        /// drawn on it except viewports. Layers, line types, text styles and blocks it uses come
        /// along by name (the drawing's own win where both have one).
        /// </summary>
        public static Layout CopyLayout(Layout source, CadDocument target, string name, out int copied, out int skipped)
        {
            var layout = new Layout(name);
            foreach (var p in typeof(PlotSettings).GetProperties().Where(p => p.CanRead && p.CanWrite && p.DeclaringType == typeof(PlotSettings)))
            {
                try { p.SetValue(layout, p.GetValue(source)); }
                catch (TargetInvocationException) { }
                catch (ArgumentException) { }
            }
            target.Layouts.Add(layout);
            copied = 0; skipped = 0;
            foreach (var e in source.AssociatedBlock.Entities.ToList())
            {
                if (e is Viewport) continue;
                try { layout.AssociatedBlock.Entities.Add(CloneForTransfer(e)); copied++; }
                catch (ArgumentException) { skipped++; }
                catch (InvalidOperationException) { skipped++; }
            }
            return layout;
        }

        /// <summary>
        /// The free area of sheet <paramref name="layoutName"/> and the scale to draw
        /// <paramref name="plan"/> (model extents) at: <paramref name="forced"/>, or the largest of
        /// the standards' scales that fits with Draft's margin and label room. Null when the
        /// standards don't describe that sheet or nothing fits.
        /// </summary>
        public static (Rect Area, ScaleOption Scale)? Fit(FirmStandards std, string layoutName, Extents plan, ScaleOption? forced)
        {
            if (plan.IsEmpty || !std.Sheets.ContainsKey(layoutName)) return null;
            double margin = 1 - Math.Max(0, Math.Min(std.MarginPct, 45)) / 100.0;
            double pad = 2 * (std.BearingTextMm + std.DistanceTextMm) * std.PaperUnitsPerMm;
            (Rect, ScaleOption)? best = null;
            foreach (var c in SheetPicker.Candidates(std, null).Where(c => c.Layout.Equals(layoutName, StringComparison.OrdinalIgnoreCase)))
            {
                double w = c.Area.Width * margin, h = c.Area.Height * margin;
                foreach (var s in forced != null ? new[] { forced } : (IEnumerable<ScaleOption>)std.Scales)
                {
                    bool fits = plan.Width / s.ModelPerPaper + 2 * pad <= w && plan.Height / s.ModelPerPaper + 2 * pad <= h;
                    if (!fits && forced == null) continue;
                    // The larger scale first, then the bigger area; a forced scale takes the biggest area.
                    if (best == null || s.ModelPerPaper < best.Value.Item2.ModelPerPaper - 1e-12
                        || Math.Abs(s.ModelPerPaper - best.Value.Item2.ModelPerPaper) < 1e-12 && c.Area.Area > best.Value.Item1.Area)
                        best = (c.Area, s);
                    break;
                }
            }
            return best;
        }

        /// <summary>
        /// Builds the new sheet in <paramref name="doc"/> from <paramref name="template"/>'s layout
        /// <paramref name="layoutName"/>. <paramref name="filler"/> (may be null: the title block
        /// then keeps the template's text) fills the title block for the chosen scale.
        /// </summary>
        public static Result? Apply(CadDocument doc, CadDocument template, FirmStandards std, string layoutName, Extents plan, ScaleOption? forced,
            Func<ScaleOption, string, TitleBlockFiller?>? filler, out string why)
        {
            why = "";
            var source = template.Layouts.FirstOrDefault(l => l.IsPaperSpace && l.Name.Equals(layoutName, StringComparison.OrdinalIgnoreCase));
            if (source == null) { why = "the template has no sheet named " + layoutName; return null; }
            var fit = Fit(std, source.Name, plan, forced);
            if (fit == null)
            {
                why = std.Sheets.ContainsKey(source.Name)
                    ? "the plan doesn't fit on " + source.Name + " at any of the firm's scales - try a bigger sheet"
                    : "the firm standards have no [sheet." + source.Name + "] frame for " + source.Name;
                return null;
            }
            string name = source.Name;
            for (int k = 2; doc.Layouts.Any(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); k++) name = source.Name + " (" + k + ")";

            var result = new Result { Scale = fit.Value.Scale, Area = fit.Value.Area };
            var layout = CopyLayout(source, doc, name, out int copied, out int skipped);
            result.Layout = layout;
            result.Report.Add("Sheet " + name + " copied from the template: " + copied + " entities" + (skipped > 0 ? " (" + skipped + " couldn't be copied)" : "") + ".");

            // The plan viewport, as Draft places it: 1 mm inside the free area, north up, locked.
            const double inset = 1.0;
            var a = fit.Value.Area;
            var paper = new Rect(a.X1 + inset, a.Y1 + inset, a.X2 - inset, a.Y2 - inset);
            if (!doc.Layers.TryGetValue(std.ViewportLayer, out var vpLayer))
            {
                vpLayer = new Layer(std.ViewportLayer) { PlotFlag = false };
                doc.Layers.Add(vpLayer);
            }
            var vp = SheetViewports.Create(paper, plan.Center, fit.Value.Scale.ModelPerPaper, vpLayer);
            layout.AddViewport(vp);
            result.Report.Add("Plan viewport " + paper.Width.ToString("0") + " x " + paper.Height.ToString("0") + " mm at " + fit.Value.Scale.Label + ", locked.");

            // North arrow, from the drawing's own block or the template's.
            if (!string.IsNullOrEmpty(std.NorthArrowBlock))
            {
                BlockRecord? arrow = doc.BlockRecords.TryGetValue(std.NorthArrowBlock, out var own) ? own : null;
                if (arrow == null && template.BlockRecords.TryGetValue(std.NorthArrowBlock, out var tb))
                {
                    var probe = CloneForTransfer(new Insert(tb));
                    arrow = ((Insert)probe).Block;
                }
                if (arrow != null)
                {
                    double k = std.NorthArrowScale;
                    layout.AssociatedBlock.Entities.Add(new Insert(arrow)
                    {
                        InsertPoint = new XYZ(a.X1 + std.NorthArrowOffsetX, a.Y2 - std.NorthArrowOffsetY, 0), XScale = k, YScale = k, ZScale = k,
                    });
                }
                else result.Report.Add("North arrow block '" + std.NorthArrowBlock + "' is in neither the drawing nor the template.");
            }

            var f = filler?.Invoke(fit.Value.Scale, name);
            if (f != null)
            {
                int changed = TemplateDrafter.FillTitleBlockText(doc, layout, std, f, fit.Value.Scale, out int ticks);
                result.Report.Add("Title block: " + changed + " text(s) filled in" + (ticks > 0 ? ", scale bar relabelled (" + ticks + " ticks)." : "."));
            }
            else result.Report.Add("Title block left as the template has it (no job details to fill it from).");

            result.Command = new AddLayoutCommand(doc, layout, "Change sheet to " + name);
            return result;
        }
    }

    /// <summary>A layout added to the drawing (CHANGESHEET); undo takes it out again with its block.</summary>
    public sealed class AddLayoutCommand : IEditCommand
    {
        private readonly CadDocument _doc;
        private readonly Layout _layout;
        public string Description { get; }

        /// <summary>For a layout already added (its constructor doesn't add it again).</summary>
        public AddLayoutCommand(CadDocument doc, Layout layout, string description)
        {
            _doc = doc; _layout = layout; Description = description;
        }

        public void Undo()
        {
            var block = _layout.AssociatedBlock;
            _doc.Layouts.Remove(_layout.Name, out _);
            if (block != null && _doc.BlockRecords.Contains(block.Name)) _doc.BlockRecords.Remove(block.Name);
        }

        public void Redo()
        {
            if (!_doc.Layouts.Any(l => ReferenceEquals(l, _layout))) _doc.Layouts.Add(_layout);
        }
    }
}
