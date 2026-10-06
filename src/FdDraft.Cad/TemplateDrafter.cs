using System;
using System.Collections.Generic;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using FdDraft.Core;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;
using FdDraft.Core.Layout;
using FdDraft.Core.Standards;

namespace FdDraft.Cad
{
    /// <summary>
    /// Drafts a finished plan into a copy of the firm's .dwt and saves it as a DWG.
    ///
    /// The template wins every tie: an existing layer keeps its colour, linetype and
    /// plot setting, existing text styles and blocks are used as they are, and only
    /// what the template lacks is created - each creation is reported. The .dwt on
    /// disk is never modified; it is read into memory and the result saved elsewhere.
    /// </summary>
    public sealed class TemplateDrafter
    {
        private readonly CadDocument _doc;
        private readonly FirmStandards _std;
        private readonly List<string> _report = new List<string>();
        private readonly HashSet<string> _missingBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _createdLayers = new List<string>();

        public IReadOnlyList<string> Report => _report;
        public CadDocument Document => _doc;

        private TemplateDrafter(CadDocument doc, FirmStandards std) { _doc = doc; _std = std; }

        /// <summary>Reads the template. Notices about unsupported objects are collected, not fatal.</summary>
        public static TemplateDrafter Open(string templatePath, FirmStandards std)
        {
            int unknown = 0;
            var doc = DwgReader.Read(templatePath, (s, e) => { if (e.Message.StartsWith("Unlisted object", StringComparison.Ordinal)) unknown++; });
            var d = new TemplateDrafter(doc, std);
            if (unknown > 0) d._report.Add(unknown + " template objects of types FD-Draft does not edit (display settings, AEC data) are carried through untouched.");
            return d;
        }

        public IEnumerable<string> LayoutNames() => _doc.Layouts.Where(l => l.IsPaperSpace).OrderBy(l => l.TabOrder).Select(l => l.Name);

        /// <summary>Draft the plan: model space, then the chosen layout. Returns the report.</summary>
        public IReadOnlyList<string> Draft(DraftResult result, TitleBlockFiller filler)
        {
            var chosen = result.Chosen ?? throw new InvalidOperationException("No sheet was chosen for this plan.");
            var layout = _doc.Layouts.FirstOrDefault(l => l.Name.Equals(chosen.Sheet.Layout, StringComparison.OrdinalIgnoreCase));
            if (layout == null)
                throw new InvalidOperationException("The template has no layout named " + chosen.Sheet.Layout + " - check the standards' [sheet.*] names.");

            if (_std.ClearTemplateModelSpace) ClearModelSpace();
            DrawModel(result.Document, result.ModelPerMm);
            AddViewport(layout, chosen.Sheet.Area, result.Document.GeometryExtents(), chosen.Scale);
            AddNorthArrow(layout, chosen.Sheet.Area);
            PlaceTitleBlocks(layout);
            FillTitleBlock(layout, filler, chosen.Scale);
            Freeze(result.Family?.FreezeLayers ?? new List<string>());
            if (_std.DeleteOtherLayouts) RemoveOtherLayouts(layout.Name);

            if (_createdLayers.Count > 0) _report.Add("Layers not in the template were created: " + string.Join(", ", _createdLayers));
            foreach (var b in _missingBlocks) _report.Add("Block '" + b + "' is not in the template; a plain symbol was drawn instead.");
            return _report;
        }

        public void Save(string dwgPath)
        {
            // Keep the template's own DWG version so every CAD program that opened the .dwt opens the plan.
            DwgWriter.Write(dwgPath, _doc);
        }

        // ---- model space --------------------------------------------------------------------

        private void ClearModelSpace()
        {
            var ents = _doc.ModelSpace.Entities.ToList();
            foreach (var e in ents) _doc.ModelSpace.Entities.Remove(e);
            if (ents.Count > 0) _report.Add("Cleared " + ents.Count + " sample entities from the template's model space.");
        }

        private void DrawModel(DraftDocument d, double modelPerMm)
        {
            foreach (var layer in d.Layers.Values) Layer(layer.Name, layer.Aci, layer.LineType);
            var ms = _doc.ModelSpace;
            var courseLabels = new List<(Entity Text, DraftText Source)>();

            foreach (var e in d.Entities)
            {
                switch (e)
                {
                    case DraftPolyline pl:
                    {
                        var poly = Poly(pl.Layer, pl.Vertices, pl.Bulges, pl.Closed);
                        ms.Entities.Add(poly);
                        PointLinks.TagCode(poly, pl.Code);
                        break;
                    }
                    case DraftSpline sp:
                        // A dense polyline through the fitted curve: exact at every shot point.
                        ms.Entities.Add(Poly(sp.Layer, sp.Sample(12), null, false));
                        break;
                    case DraftCircle c:
                        ms.Entities.Add(new Circle { Center = new XYZ(c.Center.X, c.Center.Y, 0), Radius = c.Radius, Layer = Layer(c.Layer) });
                        break;
                    case DraftSymbol s:
                        DrawSymbol(ms, s, modelPerMm);
                        break;
                    case DraftText t:
                    {
                        var te = Text(t, modelPerMm);
                        ms.Entities.Add(te);
                        if (t.PointId.HasValue) PointLinks.Tag(te, t.PointId.Value);
                        if (t.CourseKind.Length > 0) courseLabels.Add((te, t));
                        break;
                    }
                }
            }

            // Course labels are linked to the linework they describe, so they follow it when it's edited.
            if (courseLabels.Count > 0)
            {
                var spans = new List<(Entity Course, int Span, Vec2 A, Vec2 B)>();
                foreach (var e in ms.Entities)
                {
                    if (!(e is Line || e is ACadSharp.Entities.Arc || e is LwPolyline || e is Polyline2D)) continue;
                    int i = 0;
                    foreach (var sp in FdDraft.Cad.Editing.EntityOps.SpansOf(e)) spans.Add((e, i++, sp.A, sp.B));
                }
                foreach (var (te, t) in courseLabels)
                    foreach (var (course, span, a, b) in spans)
                    {
                        bool same = Vec2.Distance(a, t.CourseA) < 1e-6 && Vec2.Distance(b, t.CourseB) < 1e-6;
                        bool reversed = Vec2.Distance(a, t.CourseB) < 1e-6 && Vec2.Distance(b, t.CourseA) < 1e-6;
                        if (!same && !reversed) continue;
                        CourseLinks.Tag(te, course, span, t.CourseKind, reversed);
                        break;
                    }
            }
        }

        private void DrawSymbol(BlockRecord owner, DraftSymbol s, double modelPerMm)
        {
            // Every part is tagged with the point number, so clicking it finds the point.
            void Add(Entity e) { owner.Entities.Add(e); PointLinks.Tag(e, s.PointId); }

            // A node at the true elevation, for snapping and for a surface later.
            Add(new Point { Location = new XYZ(s.Position.X, s.Position.Y, s.Elevation), Layer = Layer(s.Layer) });

            if (s.BlockName.Length > 0)
            {
                if (_doc.BlockRecords.TryGetValue(s.BlockName, out var block))
                {
                    // Template blocks are drawn so that 1 block unit = BlockUnitMm on paper.
                    double k = _std.BlockUnitMm * modelPerMm;
                    Add(new Insert(block)
                    {
                        InsertPoint = new XYZ(s.Position.X, s.Position.Y, 0),
                        XScale = k, YScale = k, ZScale = k,
                        Layer = Layer(s.Layer),
                    });
                    return;
                }
                _missingBlocks.Add(s.BlockName);
            }

            double size = s.SizeMm * modelPerMm;
            foreach (var part in SymbolShapes.Unit(s.Symbol))
            {
                if (part.CircleRadius > 0)
                {
                    var c = s.Position + part.Points[0] * size;
                    double r = part.CircleRadius * size;
                    if (part.Filled)
                    {
                        // A filled disc: a closed two-arc polyline as wide as its radius.
                        var donut = new LwPolyline { Layer = Layer(s.Layer), ConstantWidth = r, IsClosed = true };
                        donut.Vertices.Add(new LwPolyline.Vertex { Location = new XY(c.X - r / 2, c.Y), Bulge = 1 });
                        donut.Vertices.Add(new LwPolyline.Vertex { Location = new XY(c.X + r / 2, c.Y), Bulge = 1 });
                        Add(donut);
                    }
                    else Add(new Circle { Center = new XYZ(c.X, c.Y, 0), Radius = r, Layer = Layer(s.Layer) });
                }
                else
                {
                    var pts = part.Points.Select(p => s.Position + p * size).ToList();
                    Add(Poly(s.Layer, pts, null, part.Closed));
                }
            }
        }

        private LwPolyline Poly(string layer, IList<Vec2> pts, IList<double>? bulges, bool closed)
        {
            var pl = new LwPolyline { Layer = Layer(layer), IsClosed = closed };
            for (int i = 0; i < pts.Count; i++)
                pl.Vertices.Add(new LwPolyline.Vertex { Location = new XY(pts[i].X, pts[i].Y), Bulge = bulges != null && i < bulges.Count ? bulges[i] : 0 });
            return pl;
        }

        private Entity Text(DraftText t, double modelPerMm)
        {
            var at = new XYZ(t.Position.X, t.Position.Y, 0);
            double h = t.HeightMm * modelPerMm;
            var style = Style(t.Style);
            if (t.Text.IndexOf('²') >= 0)
            {
                // The plan's SHX font has no ² glyph; draw it in Segoe UI the way the
                // template's own "AREA (m²)" heading does.
                return new MText
                {
                    Value = AcadText(t.Text).Replace("²", "{\\fSegoe UI|b0|i0|c0|p34;²}"),
                    InsertPoint = at, Height = h, Layer = Layer(t.Layer), // area labels are horizontal
                    AttachmentPoint = Attachment(t), Style = style ?? _doc.TextStyles.First(),
                };
            }
            var text = new TextEntity
            {
                Value = AcadText(t.Text),
                InsertPoint = at,
                AlignmentPoint = at,
                Height = h,
                Rotation = t.Rotation,
                HorizontalAlignment = t.H == HAlign.Left ? TextHorizontalAlignment.Left : t.H == HAlign.Center ? TextHorizontalAlignment.Center : TextHorizontalAlignment.Right,
                VerticalAlignment = t.V == VAlign.Bottom ? TextVerticalAlignmentType.Bottom : t.V == VAlign.Middle ? TextVerticalAlignmentType.Middle : TextVerticalAlignmentType.Top,
                Layer = Layer(t.Layer),
            };
            if (style != null) text.Style = style;
            return text;
        }

        private static AttachmentPointType Attachment(DraftText t)
        {
            int row = t.V == VAlign.Top ? 0 : t.V == VAlign.Middle ? 1 : 2;
            int col = t.H == HAlign.Left ? 1 : t.H == HAlign.Center ? 2 : 3;
            return (AttachmentPointType)(row * 3 + col);
        }

        /// <summary>SHX plan fonts have no ° glyph; %%d draws it in every CAD program.</summary>
        public static string AcadText(string s) => s.Replace("°", "%%d");

        // ---- tables -------------------------------------------------------------------------

        private Layer Layer(string name, int aci = 7, string lineType = "")
        {
            if (_doc.Layers.TryGetValue(name, out var layer)) return layer;
            layer = new Layer(name) { Color = new Color((short)Math.Max(1, Math.Min(255, aci))) };
            if (!string.IsNullOrWhiteSpace(lineType) && !lineType.Equals("ByLayer", StringComparison.OrdinalIgnoreCase)
                && !lineType.Equals("Continuous", StringComparison.OrdinalIgnoreCase))
            {
                if (_doc.LineTypes.TryGetValue(lineType, out var lt)) layer.LineType = lt;
                else _report.Add("Linetype '" + lineType + "' is not in the template; layer " + name + " uses Continuous.");
            }
            _doc.Layers.Add(layer);
            _createdLayers.Add(name);
            return layer;
        }

        private TextStyle? Style(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_doc.TextStyles.TryGetValue(name, out var s)) return s;
            _report.Add("Text style '" + name + "' is not in the template; the default style was used.");
            return null;
        }

        // ---- paper space --------------------------------------------------------------------

        /// <summary>
        /// The plan viewport: fills the chosen free area (1 mm inside the frame lines),
        /// centred on the survey, north up, locked at the chosen scale, on a layer that
        /// never plots.
        /// </summary>
        private void AddViewport(ACadSharp.Objects.Layout layout, Rect area, Extents survey, ScaleOption scale)
        {
            const double inset = 1.0;
            double w = area.Width - 2 * inset, h = area.Height - 2 * inset;
            var vp = new Viewport
            {
                Center = new XYZ((area.X1 + area.X2) / 2, (area.Y1 + area.Y2) / 2, 0),
                Width = w,
                Height = h,
                ViewTarget = XYZ.Zero,
                ViewDirection = XYZ.AxisZ,
                ViewCenter = new XY(survey.Center.X, survey.Center.Y),
                // Model units seen across the viewport's height at this scale.
                ViewHeight = h * scale.ModelPerPaper,
                TwistAngle = 0,
                Status = ViewportStatusFlags.CurrentlyAlwaysEnabled | ViewportStatusFlags.ViewportZoomLocking | ViewportStatusFlags.UcsIconVisibility,
                Layer = Layer(_std.ViewportLayer),
            };
            layout.AddViewport(vp);
            _report.Add("Plan viewport: " + w.ToString("0") + " x " + h.ToString("0") + " mm on " + layout.Name + " at " + scale.Label + ", locked.");
        }

        private void AddNorthArrow(ACadSharp.Objects.Layout layout, Rect area)
        {
            if (string.IsNullOrEmpty(_std.NorthArrowBlock)) return;
            if (!_doc.BlockRecords.TryGetValue(_std.NorthArrowBlock, out var block))
            {
                _report.Add("North arrow block '" + _std.NorthArrowBlock + "' is not in the template.");
                return;
            }
            double k = _std.NorthArrowScale;
            layout.AssociatedBlock.Entities.Add(new Insert(block)
            {
                InsertPoint = new XYZ(area.X1 + _std.NorthArrowOffsetX, area.Y2 - _std.NorthArrowOffsetY, 0),
                XScale = k, YScale = k, ZScale = k,
            });
        }

        /// <summary>
        /// Rewrites the layout's title-block text: the scale-bar tick labels first (they
        /// need the template's original scale, read from its "SCALE 1:n" text), then every
        /// [titleblock-replace] rule over every TEXT and MTEXT on the sheet.
        /// </summary>
        private void FillTitleBlock(ACadSharp.Objects.Layout layout, TitleBlockFiller filler, ScaleOption scale)
        {
            int changed = FillTitleBlockText(_doc, layout, _std, filler, scale, out int ticks);
            _report.Add("Title block: " + changed + " text(s) filled in" + (ticks > 0 ? ", scale bar relabelled (" + ticks + " ticks)." : "."));
        }

        /// <summary>
        /// Rewrites a sheet's title-block text: the scale-bar tick labels first (they need the
        /// template's original scale, read from its "SCALE 1:n" text), then every
        /// [titleblock-replace] rule over every TEXT and MTEXT on the sheet and the spare title
        /// blocks. Returns how many texts were filled; <paramref name="ticks"/> the relabelled ticks.
        /// </summary>
        public static int FillTitleBlockText(CadDocument doc, ACadSharp.Objects.Layout layout, FirmStandards std, TitleBlockFiller filler, ScaleOption scale, out int ticks)
        {
            var ents = layout.AssociatedBlock.Entities;
            var texts = ents.OfType<TextEntity>().Where(t => !(t is AttributeDefinition) && !(t is AttributeEntity)).ToList();
            var mtexts = ents.OfType<MText>().ToList();
            // The spare title blocks beside the sheet get the same job details, ready to swap in.
            foreach (var b in doc.BlockRecords.Where(b => b.Name.StartsWith(TitleBlocks.BlockPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                texts.AddRange(b.Entities.OfType<TextEntity>().Where(t => !(t is AttributeDefinition)));
                mtexts.AddRange(b.Entities.OfType<MText>());
            }
            int changed = 0;
            ticks = 0;
            if (std.ScaleBarRelabel)
            {
                var anchor = SimplePattern.ToRegex(std.ScaleBarAnchor);
                foreach (var a in texts.Where(t => anchor.IsMatch(t.Value)).ToList())
                {
                    var oldDen = TitleBlockFiller.DenominatorIn(a.Value);
                    if (oldDen == null) continue;
                    foreach (var t in texts)
                    {
                        double dx = t.InsertPoint.X - a.InsertPoint.X, dy = t.InsertPoint.Y - a.InsertPoint.Y;
                        if (dy < 3 || dy > 10 || dx < -5 || dx > 110) continue;
                        var relabel = TitleBlockFiller.RelabelTick(t.Value, oldDen.Value, scale.Denominator);
                        if (relabel == null) continue;
                        t.Value = relabel;
                        ticks++;
                    }
                }
            }
            foreach (var t in texts)
            {
                var s = filler.Apply(t.Value);
                if (s != null) { t.Value = AcadText(s); changed++; }
            }
            foreach (var m in mtexts)
            {
                var s = filler.Apply(m.Value);
                if (s != null) { m.Value = AcadText(s); changed++; }
            }
            return changed;
        }

        /// <summary>The standards' [title-blocks] beside the sheet, outside the paper.</summary>
        private void PlaceTitleBlocks(ACadSharp.Objects.Layout layout)
        {
            if (_std.TitleBlocks.Count == 0) return;
            var boxes = new List<(string, CadDocument)>();
            foreach (var (name, path) in _std.TitleBlocks)
            {
                try { boxes.Add((name, TitleBlocks.Read(path))); }
                catch (Exception ex)
                { _report.Add("Title block " + name + " not added - couldn't read " + path + " (" + ex.Message + ")."); }
            }
            TitleBlocks.PlaceBeside(_doc, layout, boxes, _report);
        }

        private void Freeze(IList<string> patterns)
        {
            if (patterns.Count == 0) return;
            int n = 0;
            foreach (var layer in _doc.Layers)
            {
                if (layer.Name == "0" || !FirmStandards.MatchesAny(patterns, layer.Name)) continue;
                layer.Flags |= LayerFlags.Frozen;
                n++;
            }
            if (n > 0) _report.Add("Froze " + n + " layer(s): " + string.Join(", ", patterns) + ".");
        }

        private void RemoveOtherLayouts(string keep)
        {
            var removed = new List<string>();
            var kept = new List<string>();
            foreach (var layout in _doc.Layouts.Where(l => l.IsPaperSpace).ToList())
            {
                if (layout.Name.Equals(keep, StringComparison.OrdinalIgnoreCase)) continue;
                var block = layout.AssociatedBlock;
                // The layout that owns *Paper_Space is the file's active layout; DWG readers
                // expect it to exist, so it stays (a drafter can delete it in the app later).
                if (block != null && block.Name.Equals(BlockRecord.PaperSpaceName, StringComparison.OrdinalIgnoreCase))
                {
                    kept.Add(layout.Name);
                    continue;
                }
                try
                {
                    if (_doc.Layouts.Remove(layout.Name, out _))
                    {
                        if (block != null) _doc.BlockRecords.Remove(block.Name);
                        removed.Add(layout.Name);
                    }
                    else kept.Add(layout.Name);
                }
                catch (ArgumentException) { kept.Add(layout.Name); }
            }
            if (removed.Count > 0) _report.Add("Removed unused layouts: " + string.Join(", ", removed) + ".");
            if (kept.Count > 0) _report.Add("Kept layout " + string.Join(", ", kept) + " - it is the template's active sheet, which the DWG format needs.");
        }
    }
}
