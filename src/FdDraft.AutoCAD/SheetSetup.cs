using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FdDraft.Core.Geometry;
using FdDraft.Core.Layout;
using FdDraft.Core.Standards;

namespace FdDraft.AutoCAD
{
    /// <summary>
    /// Everything on the paper side of the chosen layout: the plan viewport (the
    /// template has none, so it is created in the chosen free area), the north
    /// arrow, the title-block text and the scale bar.
    /// </summary>
    internal static class SheetSetup
    {
        public static ObjectId LayoutId(Database db, Transaction tr, string name)
        {
            var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry e in dict)
                if (string.Equals(e.Key, name, StringComparison.OrdinalIgnoreCase)) return e.Value;
            return ObjectId.Null;
        }

        public static List<string> LayoutNames(Database db, Transaction tr)
        {
            var names = new List<string>();
            var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry e in dict)
            {
                var lay = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
                if (!lay.ModelType) names.Add(lay.LayoutName);
            }
            return names;
        }

        /// <summary>
        /// Adds the plan viewport: fills the chosen free area (inset 1 mm off the frame
        /// lines), looks at the centre of the survey, locked at the chosen scale.
        /// Call with the layout already current.
        /// </summary>
        public static ObjectId AddPlanViewport(Database db, Transaction tr, ObjectId layoutId, Rect area, Extents survey,
                                               ScaleOption scale, string layer, AcadRenderer renderer)
        {
            var lay = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
            var ps = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForWrite);
            const double inset = 1.0;
            var vp = new Viewport();
            vp.SetDatabaseDefaults(db);
            vp.CenterPoint = new Point3d((area.X1 + area.X2) / 2, (area.Y1 + area.Y2) / 2, 0);
            vp.Width = area.Width - 2 * inset;
            vp.Height = area.Height - 2 * inset;
            vp.LayerId = renderer.Layer(layer, 7, "");
            ps.AppendEntity(vp);
            tr.AddNewlyCreatedDBObject(vp, true);

            // Target the survey's centre and centre the view on it: north up, no twist.
            vp.ViewDirection = Vector3d.ZAxis;
            vp.ViewTarget = new Point3d(survey.Center.X, survey.Center.Y, 0);
            vp.ViewCenter = Point2d.Origin;
            vp.TwistAngle = 0;
            // CustomScale is paper units per model unit: 1:250 with mm paper and m model = 4.
            vp.CustomScale = 1.0 / scale.ModelPerPaper;
            vp.On = true;
            vp.Locked = true;
            return vp.ObjectId;
        }

        public static void AddNorthArrow(Database db, Transaction tr, ObjectId layoutId, Rect area, FirmStandards std, List<string> report)
        {
            if (string.IsNullOrEmpty(std.NorthArrowBlock)) return;
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(std.NorthArrowBlock)) { report.Add("North arrow block '" + std.NorthArrowBlock + "' is not in the template."); return; }
            var lay = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
            var ps = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForWrite);
            var br = new BlockReference(new Point3d(area.X1 + std.NorthArrowOffsetX, area.Y2 - std.NorthArrowOffsetY, 0), bt[std.NorthArrowBlock])
            {
                ScaleFactors = new Scale3d(std.NorthArrowScale),
            };
            br.SetDatabaseDefaults(db);
            ps.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
        }

        /// <summary>
        /// Rewrites the layout's title-block text: first the scale-bar tick labels
        /// (which need the template's original scale, read from the anchor text), then
        /// every [titleblock-replace] rule over every TEXT and MTEXT.
        /// </summary>
        public static int FillTitleBlock(Transaction tr, ObjectId layoutId, TitleBlockFiller filler, FirmStandards std, ScaleOption scale)
        {
            var lay = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
            var ps = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForRead);
            var texts = new List<DBText>();
            var mtexts = new List<MText>();
            foreach (ObjectId id in ps)
            {
                var obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is DBText t && !(obj is AttributeDefinition)) texts.Add(t);
                else if (obj is MText m) mtexts.Add(m);
            }

            int changed = 0;
            if (std.ScaleBarRelabel)
            {
                var anchor = new Regex(std.ScaleBarAnchor, RegexOptions.IgnoreCase);
                foreach (var a in texts)
                {
                    if (!anchor.IsMatch(a.TextString)) continue;
                    var oldDen = TitleBlockFiller.DenominatorIn(a.TextString);
                    if (oldDen == null) continue;
                    // Tick labels sit a few mm above the anchor and run up to ~100 mm right of it.
                    foreach (var t in texts)
                    {
                        double dx = t.Position.X - a.Position.X, dy = t.Position.Y - a.Position.Y;
                        if (dy < 3 || dy > 10 || dx < -5 || dx > 110) continue;
                        var relabel = TitleBlockFiller.RelabelTick(t.TextString, oldDen.Value, scale.Denominator);
                        if (relabel == null) continue;
                        t.UpgradeOpen();
                        t.TextString = relabel;
                        changed++;
                    }
                }
            }

            foreach (var t in texts)
            {
                var s = filler.Apply(t.TextString);
                if (s == null) continue;
                if (!t.IsWriteEnabled) t.UpgradeOpen();
                t.TextString = s;
                changed++;
            }
            foreach (var m in mtexts)
            {
                var s = filler.Apply(m.Contents);
                if (s == null) continue;
                m.UpgradeOpen();
                m.Contents = s;
                changed++;
            }
            return changed;
        }

        /// <summary>Freezes every layer matching the family's patterns (never the current layer).</summary>
        public static int FreezeLayers(Database db, Transaction tr, IList<string> patterns)
        {
            if (patterns.Count == 0) return 0;
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            db.Clayer = lt["0"];
            int n = 0;
            foreach (ObjectId id in lt)
            {
                var rec = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (rec.Name == "0" || !FirmStandards.MatchesAny(patterns, rec.Name)) continue;
                rec.UpgradeOpen();
                rec.IsFrozen = true;
                n++;
            }
            return n;
        }
    }
}
