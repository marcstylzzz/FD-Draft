using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ACadSharp;
using ACadSharp.Entities;
using FdDraft.Core.Layout;

namespace FdDraft.Cad.Editing
{
    /// <summary>
    /// Changes a sheet's plot scale in place (VPSCALE) - the scale-only sheet setup that used
    /// to mean re-running the whole Draft FD-Pro job flow. The sheet's plan viewport is
    /// re-zoomed about its own centre, the title block's "1:n" text and the scale-bar tick
    /// labels are rewritten, and (optionally) the model-space annotation - text heights,
    /// symbol blocks, dimension text - is resized by the same ratio so it keeps its size on
    /// paper. All of it is one undo step.
    /// </summary>
    public static class SheetScale
    {
        public sealed class Result
        {
            public IEditCommand? Command { get; set; }
            public double OldDenominator { get; set; }
            public List<string> Notes { get; } = new List<string>();
        }

        /// <summary>The viewport a sheet's plan is shown through: its largest working one.</summary>
        public static Viewport? PlanViewport(ACadSharp.Objects.Layout layout) =>
            layout.AssociatedBlock.Entities.OfType<Viewport>()
                .Where(v => !v.RepresentsPaper && v.ViewHeight > 0 && v.Height > 0 && v.Width > 0)
                .OrderByDescending(v => v.Width * v.Height)
                .FirstOrDefault();

        /// <summary>The scale the sheet says it is at: the denominator in its "SCALE 1:n"
        /// title-block text (<paramref name="anchorPattern"/>), or null.</summary>
        public static double? StatedDenominator(ACadSharp.Objects.Layout layout, string anchorPattern)
        {
            var anchor = SimplePattern.ToRegex(anchorPattern);
            foreach (var t in SheetTexts(layout))
                if (anchor.IsMatch(Value(t)) && TitleBlockFiller.DenominatorIn(Value(t)) is double d) return d;
            return null;
        }

        public static Result Change(CadDocument doc, ACadSharp.Objects.Layout layout, double newDenominator, bool resizeAnnotation, string anchorPattern = "SCALE 1:#")
        {
            var r = new Result();
            if (newDenominator <= 0) { r.Notes.Add("the scale has to be a positive 1:n"); return r; }
            var vp = PlanViewport(layout);
            if (vp == null) { r.Notes.Add("this sheet has no plan viewport to rescale"); return r; }

            double? stated = StatedDenominator(layout, anchorPattern);
            double oldDen;
            if (stated != null) oldDen = stated.Value;
            else
            {
                // No "SCALE 1:n" text: assume metres drawn on a millimetre sheet.
                oldDen = vp.ViewHeight / vp.Height * 1000;
                r.Notes.Add("no \"" + anchorPattern + "\" text on the sheet - took the current scale as 1:" + Fmt(oldDen) + " from the viewport (metres on a mm sheet)");
            }
            r.OldDenominator = oldDen;
            double k = newDenominator / oldDen;
            if (Math.Abs(k - 1) < 1e-12) { r.Notes.Add("the sheet is already at 1:" + Fmt(newDenominator)); return r; }

            var edits = new List<IEditCommand>();
            double oldVh = vp.ViewHeight;
            edits.Add(new SetPropertyCommand<double>(oldVh, oldVh * k, v => vp.ViewHeight = v, "Viewport scale"));
            int others = layout.AssociatedBlock.Entities.OfType<Viewport>().Count(v => v != vp && !v.RepresentsPaper && v.ViewHeight > 0);
            if (others > 0) r.Notes.Add(others + " other viewport(s) on the sheet (details, key plans) keep their own scale");

            // Title block: every "1:old" becomes "1:new"; tick labels just above the anchor are relabelled.
            var anchor = SimplePattern.ToRegex(anchorPattern);
            var oldScale = new Regex(@"1\s*:\s*" + Regex.Escape(Fmt(oldDen)) + @"(?![0-9.])");
            int texts = 0, ticks = 0;
            var sheetTexts = SheetTexts(layout).ToList();
            foreach (var t in sheetTexts)
            {
                string v = Value(t);
                if (!oldScale.IsMatch(v)) continue;
                edits.Add(SetText(t, oldScale.Replace(v, "1:" + Fmt(newDenominator))));
                texts++;
            }
            var relabelled = new HashSet<TextEntity>();
            foreach (var a in sheetTexts.OfType<TextEntity>().Where(t => anchor.IsMatch(t.Value)).ToList())
            {
                foreach (var t in sheetTexts.OfType<TextEntity>())
                {
                    if (relabelled.Contains(t)) continue;
                    // Same placement rule the drafter uses: ticks sit 3-10 mm above the anchor text.
                    double dx = t.InsertPoint.X - a.InsertPoint.X, dy = t.InsertPoint.Y - a.InsertPoint.Y;
                    if (dy < 3 || dy > 10 || dx < -5 || dx > 110) continue;
                    var relabel = TitleBlockFiller.RelabelTick(t.Value, oldDen, newDenominator);
                    if (relabel == null || relabel == t.Value) continue;
                    edits.Add(new EditTextCommand(t, relabel, "Relabel scale bar"));
                    relabelled.Add(t);
                    ticks++;
                }
            }
            r.Notes.Add("viewport now 1:" + Fmt(newDenominator) + (texts > 0 ? ", " + texts + " title-block text(s) updated" : "") + (ticks > 0 ? ", scale bar relabelled (" + ticks + " ticks)" : ""));

            if (resizeAnnotation)
            {
                int n = 0;
                foreach (var e in doc.ModelSpace.Entities.ToList())
                {
                    switch (e)
                    {
                        case TextEntity t:
                            edits.Add(new SetPropertyCommand<double>(t.Height, t.Height * k, v => t.Height = v, "Resize text")); n++;
                            break;
                        case MText m:
                            edits.Add(new SetPropertyCommand<double>(m.Height, m.Height * k, v => m.Height = v, "Resize text")); n++;
                            break;
                        case Insert ins:
                        {
                            var old = (ins.XScale, ins.YScale, ins.ZScale);
                            edits.Add(new SetPropertyCommand<(double X, double Y, double Z)>(old, (old.XScale * k, old.YScale * k, old.ZScale * k),
                                s => { ins.XScale = s.X; ins.YScale = s.Y; ins.ZScale = s.Z; }, "Resize symbol"));
                            n++;
                            break;
                        }
                        case Dimension da when DimensionBuilder.IsOurs(da):
                        {
                            double h = DimensionBuilder.TextHeightOf(da);
                            edits.Add(new SetPropertyCommand<double>(h, h * k, v => DimensionBuilder.DrawPicture(da, v), "Resize dimension")); n++;
                            break;
                        }
                    }
                }
                if (n > 0) r.Notes.Add(n + " model-space label(s)/symbol(s) resized ×" + k.ToString("0.###", CultureInfo.InvariantCulture) + " to keep their size on paper - label positions are unchanged, so re-draft (Ctrl+D) for a full relayout");
            }
            r.Command = new CompositeCommand(edits, "Sheet scale 1:" + Fmt(newDenominator));
            return r;
        }

        private static IEnumerable<Entity> SheetTexts(ACadSharp.Objects.Layout layout) =>
            layout.AssociatedBlock.Entities.Where(e => (e is TextEntity && !(e is AttributeDefinition) && !(e is AttributeEntity)) || e is MText);

        private static string Value(Entity e) => e is TextEntity t ? t.Value : e is MText m ? m.Value : "";

        private static IEditCommand SetText(Entity e, string value) =>
            e is TextEntity t ? new EditTextCommand(t, value, "Title block scale") : new EditTextCommand((MText)e, value, "Title block scale");

        private static string Fmt(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
