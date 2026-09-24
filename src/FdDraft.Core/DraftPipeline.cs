using System;
using System.Collections.Generic;
using FdDraft.Core.Drafting;
using FdDraft.Core.Job;
using FdDraft.Core.Layout;
using FdDraft.Core.Standards;

namespace FdDraft.Core
{
    public sealed class DraftResult
    {
        public FdJob Job { get; set; } = new FdJob();
        public FirmStandards Standards { get; set; } = new FirmStandards();
        public SheetFamily? Family { get; set; }
        public DraftDocument Document { get; set; } = new DraftDocument();
        public List<SheetChoice> Ranked { get; set; } = new List<SheetChoice>();
        public SheetChoice? Chosen { get; set; }
        public double ModelPerMm { get; set; }
    }

    /// <summary>
    /// The whole job-to-plan path in one call, shared by every front end:
    /// read the job, build geometry, rank sheets and scales, annotate at the winner.
    /// </summary>
    public static class DraftPipeline
    {
        public static DraftResult Run(FdJob job, FirmStandards std, string? family = null, string? forcedLayout = null, string? forcedScale = null)
        {
            var fam = std.Family(family);
            var doc = DraftBuilder.Build(job, std);
            var ranked = SheetPicker.Rank(doc, SheetPicker.Candidates(std, fam), std);
            SheetChoice? chosen = ranked.Count > 0 ? ranked[0] : null;

            if (forcedLayout != null)
            {
                chosen = null;
                foreach (var r in ranked)
                    if (string.Equals(r.Sheet.Layout, forcedLayout, StringComparison.OrdinalIgnoreCase)) { chosen = r; break; }
                if (chosen == null) doc.Warnings.Add("Layout " + forcedLayout + " cannot hold this survey at any listed scale.");
            }
            if (chosen != null && !string.IsNullOrEmpty(forcedScale))
            {
                string want = forcedScale!.StartsWith("1:", StringComparison.Ordinal) ? forcedScale : "1:" + forcedScale;
                ScaleOption? match = null;
                foreach (var s in std.Scales) if (s.Label == want || s.Label == forcedScale) match = s;
                if (match == null) doc.Warnings.Add("Scale " + forcedScale + " is not in the standards' scale list; kept " + chosen.Scale.Label + ".");
                else chosen = new SheetChoice { Sheet = chosen.Sheet, Scale = match, Reason = "scale set by user", Legibility = SheetPicker.Legibility(doc, match, std) };
            }

            var result = new DraftResult { Job = job, Standards = std, Family = fam, Document = doc, Ranked = ranked, Chosen = chosen };
            if (chosen == null)
            {
                if (forcedLayout == null)
                    doc.Warnings.Add("No sheet in family '" + (fam?.Name ?? "(all)") + "' can hold this survey at any listed scale - add a larger sheet or a smaller scale.");
                return result;
            }
            Annotator.Annotate(doc, job, std, chosen.Scale);
            result.ModelPerMm = Annotator.ModelPerMm(chosen.Scale, std);
            return result;
        }
    }
}
