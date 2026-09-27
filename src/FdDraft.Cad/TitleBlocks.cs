using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using FdDraft.Core.Standards;

namespace FdDraft.Cad
{
    /// <summary>
    /// The firm's alternative title-block boxes (M&amp;M, YZ, Grad...) kept ready beside a sheet:
    /// each is a drawing whose model space holds the box at paper millimetres with the frame's
    /// lower-right corner at 0,0. <see cref="PlaceBeside"/> brings them into a layout as blocks
    /// just right of the paper, bottoms level with the sheet's own box, so swapping one in is a
    /// move (not plotted - they sit outside the sheet). <see cref="Extract"/> makes such a file
    /// from a box drawn in any sheet or drawing.
    /// </summary>
    public static class TitleBlocks
    {
        public const string BlockPrefix = "FD-TITLEBLOCK-";

        /// <summary>A title block's frame: the extent of its linework (lines and polylines, nested
        /// blocks included; text left out) - one outer rectangle, or side-by-side panels like
        /// Grad's AOLS / address / logo boxes.</summary>
        public static Rect? FindFrame(IEnumerable<Entity> ents, Rect? within = null)
        {
            Rect? best = null; double bestArea = 0;
            void Walk(IEnumerable<Entity> list, Transform t, int depth)
            {
                foreach (var e in list)
                {
                    List<XYZ>? pts = e switch
                    {
                        LwPolyline pl => pl.Vertices.Select(v => new XYZ(v.Location.X, v.Location.Y, 0)).ToList(),
                        Line ln => new List<XYZ> { ln.StartPoint, ln.EndPoint },
                        _ => null,
                    };
                    if (pts != null && pts.Count >= 2)
                    {
                        pts = pts.Select(p => t.ApplyTransform(p)).ToList();
                        var r = new Rect(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));
                        if (within.HasValue && !Inside(r, within.Value, 0.6)) continue;
                        best = best == null ? r : new Rect(Math.Min(best.Value.X1, r.X1), Math.Min(best.Value.Y1, r.Y1), Math.Max(best.Value.X2, r.X2), Math.Max(best.Value.Y2, r.Y2));
                        bestArea = best.Value.Area;
                    }
                    else if (e is Insert ins && ins.Block != null && depth < 6)
                        Walk(ins.Block.Entities, new Transform(t.Matrix * ins.GetTransform().Matrix), depth + 1);
                }
            }
            Walk(ents, new Transform(Matrix4.Identity), 0);
            return best;
        }

        private static bool Inside(Rect r, Rect box, double tol) =>
            r.X1 >= box.X1 - tol && r.Y1 >= box.Y1 - tol && r.X2 <= box.X2 + tol && r.Y2 <= box.Y2 + tol;

        /// <summary>
        /// A new drawing holding the box: from the named layout (or model space when null), the
        /// entities inside <paramref name="region"/> (all when null), moved so the frame's
        /// lower-right corner is 0,0. Null when no frame is found.
        /// </summary>
        public static CadDocument? Extract(CadDocument src, string? layout, Rect? region)
        {
            var owner = layout == null ? src.ModelSpace
                : src.Layouts.FirstOrDefault(l => l.Name.Equals(layout, StringComparison.OrdinalIgnoreCase))?.AssociatedBlock;
            if (owner == null) return null;
            var all = owner.Entities.Where(e => !(e is Viewport)).ToList();
            // The frame from the linework in the region; then what belongs to the box: linework and
            // blocks inside it, text whose insertion point is inside (an MTEXT that opens with an
            // empty paragraph starts a line or two above its frame, so it gets that much slack).
            var frame = FindFrame(all, region);
            if (frame == null) return null;
            var f = frame.Value;
            bool In(double x, double y, double slackTop = 0) => x >= f.X1 - 0.6 && x <= f.X2 + 0.6 && y >= f.Y1 - 0.6 && y <= f.Y2 + slackTop;
            var ents = region == null ? all : all.Where(e =>
            {
                switch (e)
                {
                    case TextEntity t: return In(t.InsertPoint.X, t.InsertPoint.Y);
                    case MText m: return In(m.InsertPoint.X, m.InsertPoint.Y, 3 * m.Height);
                    default:
                        try { var b = e.GetBoundingBox(); return Inside(new Rect(b.Min.X, b.Min.Y, b.Max.X, b.Max.Y), f, 0.6); }
                        catch (Exception) { return false; }
                }
            }).ToList();
            var doc = new CadDocument();
            var move = Transform.CreateTranslation(new XYZ(-frame.Value.X2, -frame.Value.Y1, 0));
            foreach (var e in ents)
            {
                var c = Copy(e, doc);
                if (c == null) continue;
                doc.ModelSpace.Entities.Add(c);
                Editing.EntityTransform.Apply(c, move);
            }
            return doc;
        }

        /// <summary>A copy of <paramref name="e"/> for <paramref name="target"/>, its block (for an
        /// insert) copied across too. Dimensions are left out (their picture blocks are per-drawing).</summary>
        public static Entity? Copy(Entity e, CadDocument target)
        {
            if (e is Dimension || e is Viewport) return null;
            if (e is Insert ei)
            {
                if (ei.Block == null) return null;
                var blk = EnsureBlock(ei.Block, target, ei.Block.Name);
                var ci = new Insert(blk)
                {
                    InsertPoint = ei.InsertPoint, XScale = ei.XScale, YScale = ei.YScale, ZScale = ei.ZScale, Rotation = ei.Rotation, Normal = ei.Normal,
                    RowCount = ei.RowCount, ColumnCount = ei.ColumnCount, RowSpacing = ei.RowSpacing, ColumnSpacing = ei.ColumnSpacing,
                    Color = ei.Color, LineWeight = ei.LineWeight,
                };
                Detach(ci, ei);
                foreach (var a in ei.Attributes) { var ca = (AttributeEntity)a.Clone(); Detach(ca, a); ci.Attributes.Add(ca); }
                return ci;
            }
            var c = (Entity)e.Clone();
            Detach(c, e);
            return c;
        }

        /// <summary>Points a copy's layer, linetype and text style at fresh copies of the source's
        /// (matched by name when the copy joins the other drawing) - never at the source drawing's own.</summary>
        private static void Detach(Entity c, Entity src)
        {
            // Fields, reactors and app data stay with the source drawing: a clone still carries its
            // extension dictionary (an MTEXT's field objects), which the other drawing refuses.
            typeof(CadObject).GetField("_xdictionary", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(c, null);
            c.ExtendedData.Clear();
            if (src.Layer != null)
            {
                var l = (Layer)src.Layer.Clone();
                if (src.Layer.LineType != null) l.LineType = (LineType)src.Layer.LineType.Clone();
                c.Layer = l;
            }
            if (src.LineType != null) c.LineType = (LineType)src.LineType.Clone();
            if (src is TextEntity st && c is TextEntity ct && st.Style != null) ct.Style = (TextStyle)st.Style.Clone();
            if (src is MText sm && c is MText cm && sm.Style != null) cm.Style = (TextStyle)sm.Style.Clone();
        }

        private static BlockRecord EnsureBlock(BlockRecord src, CadDocument target, string name)
        {
            if (target.BlockRecords.TryGetValue(name, out var existing)) return existing;
            var nb = new BlockRecord(name);
            if (src.BlockEntity != null) nb.BlockEntity.BasePoint = src.BlockEntity.BasePoint;
            target.BlockRecords.Add(nb);
            foreach (var e in src.Entities)
            {
                if (e is AttributeDefinition) continue;
                var c = Copy(e, target);
                if (c != null) nb.Entities.Add(c);
            }
            return nb;
        }

        /// <summary>Reads a title-block file (DWG or DXF).</summary>
        public static CadDocument Read(string path) =>
            Path.GetExtension(path).Equals(".dxf", StringComparison.OrdinalIgnoreCase) ? DxfReader.Read(path) : DwgReader.Read(path);

        /// <summary>
        /// Puts each box into <paramref name="layout"/> as a block, right of the paper in a row,
        /// their frames' bottoms level with the sheet's own title-block frame (or 15 mm up), with a
        /// name above each. Boxes already placed (by block name) are skipped. Returns how many were added.
        /// </summary>
        public static int PlaceBeside(CadDocument doc, ACadSharp.Objects.Layout layout, IList<(string Name, CadDocument Box)> boxes, IList<string>? report = null) =>
            PlaceBeside(doc, layout, boxes, report, null);

        /// <summary>As above, listing the entities it put on the sheet in <paramref name="added"/>.</summary>
        public static int PlaceBeside(CadDocument doc, ACadSharp.Objects.Layout layout, IList<(string Name, CadDocument Box)> boxes, IList<string>? report, List<Entity>? addedEntities)
        {
            var sheet = layout.AssociatedBlock;
            double paperW = layout.PaperRotation == ACadSharp.Objects.PlotRotation.Degrees90 || layout.PaperRotation == ACadSharp.Objects.PlotRotation.Degrees270
                ? layout.PaperHeight : layout.PaperWidth;
            if (paperW <= 0) paperW = sheet.Entities.Select(e => { try { return e.GetBoundingBox().Max.X; } catch (Exception) { return 0; } }).DefaultIfEmpty(0).Max();
            double bottom = 15;
            var box = SmallFrameNear(sheet, paperW);
            if (box.HasValue) bottom = box.Value.Y1;

            // Their own layer, on and not plotting (layer 0 is often off in a firm template).
            const string layerName = "FD-Title-Blocks";
            if (!doc.Layers.TryGetValue(layerName, out var tbLayer))
            {
                tbLayer = new Layer(layerName) { Color = new Color(8), PlotFlag = false };
                doc.Layers.Add(tbLayer);
            }
            double x = paperW + 15;
            int added = 0;
            foreach (var (name, src) in boxes)
            {
                string blockName = BlockPrefix + Safe(name);
                var extent = Extent(src);
                if (extent == null) { report?.Add("Title block " + name + ": nothing in it."); continue; }
                double width = extent.Value.X2 - extent.Value.X1;
                x += width; // this box's lower-right corner
                if (sheet.Entities.OfType<Insert>().Any(i => i.Block?.Name == blockName)) { x += 15; continue; }
                BlockRecord blk;
                if (!doc.BlockRecords.TryGetValue(blockName, out blk!))
                {
                    blk = new BlockRecord(blockName);
                    doc.BlockRecords.Add(blk);
                    foreach (var e in src.ModelSpace.Entities)
                    {
                        var c = Copy(e, doc);
                        if (c != null) blk.Entities.Add(c);
                    }
                }
                var ins = new Insert(blk) { InsertPoint = new XYZ(x, bottom, 0), Layer = tbLayer };
                var label = new TextEntity
                {
                    Value = "TITLE BLOCK: " + name.ToUpperInvariant() + " (move into place to swap)",
                    Height = 3, InsertPoint = new XYZ(x - width, bottom + extent.Value.Y2 + 4, 0), Layer = tbLayer,
                };
                sheet.Entities.Add(ins); sheet.Entities.Add(label);
                addedEntities?.Add(ins); addedEntities?.Add(label);
                added++;
                x += 15;
            }
            if (added > 0) report?.Add("Title blocks ready beside " + layout.Name + ": " + string.Join(", ", boxes.Select(b => b.Name)) + " (outside the paper; move one into place to swap).");
            return added;
        }

        /// <summary>The sheet's own title-block frame: the largest closed rectangle whose
        /// lower-right corner sits in the sheet's bottom-right corner area.</summary>
        private static Rect? SmallFrameNear(BlockRecord sheet, double paperW)
        {
            Rect? best = null; double area = 0;
            foreach (var pl in sheet.Entities.OfType<LwPolyline>())
            {
                if (pl.Vertices.Count < 4) continue;
                double x1 = pl.Vertices.Min(v => v.Location.X), x2 = pl.Vertices.Max(v => v.Location.X);
                double y1 = pl.Vertices.Min(v => v.Location.Y), y2 = pl.Vertices.Max(v => v.Location.Y);
                if (x2 < paperW * 0.8 || x2 > paperW || y1 > 60 || y2 - y1 > 150 || x2 - x1 > 250) continue;
                double a = (x2 - x1) * (y2 - y1);
                if (a > area) { area = a; best = new Rect(x1, y1, x2, y2); }
            }
            return best;
        }

        /// <summary>The box's extent in its own file (lower-right of the frame is 0,0).</summary>
        public static Rect? Extent(CadDocument box)
        {
            var f = FindFrame(box.ModelSpace.Entities, null);
            return f;
        }

        private static string Safe(string name) => new string(name.Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_').ToArray());
    }
}
