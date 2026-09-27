using System;
using System.Collections.Generic;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using FdDraft.Cad;
using FdDraft.Cad.Editing;

namespace FdDraft.App
{
    public sealed partial class MainWindow
    {
        /// <summary>TITLEBLOCKS: the standards' spare title blocks (M&amp;M, YZ, Grad...) beside
        /// the current sheet, outside the paper, ready to move into place.</summary>
        private void PlaceTitleBlocksCommand()
        {
            if (!NeedDrawing()) return;
            var layout = _doc!.Layouts.FirstOrDefault(l => l.IsPaperSpace && l.Name.Equals(_sheet, StringComparison.OrdinalIgnoreCase));
            if (layout == null) { Log("  switch to a sheet tab first - the title blocks go beside its paper"); return; }
            var std = LabelStandards();
            if (std.TitleBlocks.Count == 0) { Log("  the standards file lists no [title-blocks] - add name = drawing file lines to it"); return; }
            var boxes = new List<(string, CadDocument)>();
            foreach (var (name, path) in std.TitleBlocks)
            {
                try { boxes.Add((name, TitleBlocks.Read(path))); }
                catch (Exception ex) { Log("  " + name + ": couldn't read " + path + " (" + ex.Message + ")"); }
            }
            var added = new List<Entity>();
            var report = new List<string>();
            int n = TitleBlocks.PlaceBeside(_doc, layout, boxes, report, added);
            if (n == 0) { Log("  nothing added - they are already beside " + layout.Name); return; }
            // Taken off again and put back as one undo step.
            foreach (var e in added) layout.AssociatedBlock.Entities.Remove(e);
            Commit(new AddEntitiesCommand(layout.AssociatedBlock, added, "Title blocks"), "  " + string.Join(" ", report) + "  (ZE to see them; Ctrl+Z undoes it)");
        }
    }
}
