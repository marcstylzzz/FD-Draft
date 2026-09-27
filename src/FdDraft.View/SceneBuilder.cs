using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;
using FdDraft.Core.Standards;

namespace FdDraft.View
{
    /// <summary>
    /// Turns a DWG into a display list. Model space is drawn in model units; a layout
    /// is drawn in paper units, with model space appearing through each of its
    /// viewports (scaled, clipped, and minus that viewport's frozen layers).
    ///
    /// Follows the usual CAD display rules: layer off/frozen hides, ByLayer and
    /// ByBlock colours resolve, entities on layer 0 inside a block take the insert's
    /// layer, arcs and bulges are tessellated finely enough to look round at any zoom
    /// a survey drawing needs.
    /// </summary>
    public sealed class SceneBuilder
    {
        private readonly CadDocument _doc;
        private readonly ISet<string> _hidden;
        private Scene _scene = new Scene();
        private SceneGroup _group = new SceneGroup();
        private ISet<string> _vpFrozen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _skipped;

        /// <param name="hiddenLayers">Layers the user has switched off in the app (display only).</param>
        public SceneBuilder(CadDocument doc, ISet<string>? hiddenLayers = null)
        {
            _doc = doc;
            _hidden = hiddenLayers ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Draw model space for a black background, the way CAD programs show it: colour 7
        /// ("black on white, white on black") draws white and pale colours keep their full
        /// brightness. Sheets are always drawn for white paper. Plot colours are unaffected.
        /// </summary>
        public bool DarkModel { get; set; }
        private bool _dark;

        public Scene Model()
        {
            _dark = DarkModel;
            _scene = new Scene { Name = "Model", DarkBackground = _dark };
            _group = new SceneGroup();
            _scene.Groups.Add(_group);
            foreach (var e in _doc.ModelSpace.Entities) Emit(e, Affine.Identity, null, null, e.Handle, 0);
            Finish();
            return _scene;
        }

        public Scene Layout(string name)
        {
            _dark = false;
            var layout = _doc.Layouts.First(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            bool rotated = layout.PaperRotation == ACadSharp.Objects.PlotRotation.Degrees90 || layout.PaperRotation == ACadSharp.Objects.PlotRotation.Degrees270;
            double pw = rotated ? layout.PaperHeight : layout.PaperWidth, ph = rotated ? layout.PaperWidth : layout.PaperHeight;
            _scene = new Scene { Name = layout.Name, IsPaper = true, Paper = new Rect(0, 0, pw, ph) };
            var paperEntities = layout.AssociatedBlock.Entities.ToList();

            // Model space through each viewport first, so the sheet's own linework draws on top.
            foreach (var vp in paperEntities.OfType<Viewport>())
            {
                if (!ViewportRules.ShowsModel(vp, pw, ph))
                {
                    if (vp.Status.HasFlag(ViewportStatusFlags.ViewportOff) && !ViewportRules.IsPaperBackground(vp, pw, ph))
                        _scene.Notes.Add("a viewport on " + layout.Name + " is switched off in the drawing, so its view of model space isn't shown");
                    continue;
                }
                if (IsHiddenLayer(vp.Layer)) { /* the viewport frame's layer does not hide its contents */ }
                double s = vp.Height / vp.ViewHeight;
                // AutoCAD's convention (as ezdxf reads it): the view centre is in the twisted
                // display frame, so the model is turned about the target first, then the view
                // centre is taken off. With no twist this is the plain scale-and-shift.
                var toPaper = Affine.Translate(vp.Center.X - s * vp.ViewCenter.X, vp.Center.Y - s * vp.ViewCenter.Y)
                    .After(Affine.Rotate(vp.TwistAngle))
                    .After(Affine.Scale(s, s))
                    .After(Affine.Translate(-vp.ViewTarget.X, -vp.ViewTarget.Y));
                _group = new SceneGroup
                {
                    Clip = new Rect(vp.Center.X - vp.Width / 2, vp.Center.Y - vp.Height / 2, vp.Center.X + vp.Width / 2, vp.Center.Y + vp.Height / 2),
                    ToModel = toPaper.Inverse(),
                    ModelPerPaper = 1 / s,
                };
                _scene.Groups.Add(_group);
                _vpFrozen = new HashSet<string>(vp.FrozenLayers.Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var e in _doc.ModelSpace.Entities) Emit(e, toPaper, null, null, e.Handle, 0);
                _vpFrozen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            _group = new SceneGroup();
            _scene.Groups.Add(_group);
            foreach (var e in paperEntities)
            {
                if (e is Viewport) continue;
                Emit(e, Affine.Identity, null, null, e.Handle, 0);
            }
            Finish();
            return _scene;
        }

        private void Finish()
        {
            _scene.ComputeBounds();
            if (_skipped > 0) _scene.Notes.Add(_skipped + " entities of types the viewer does not draw yet (hatch fills, leaders, images) were skipped.");
        }

        // ---- visibility and colour --------------------------------------------------------

        private bool IsHiddenLayer(Layer? layer)
        {
            if (layer == null) return false;
            return !layer.IsOn || layer.Flags.HasFlag(LayerFlags.Frozen) || _hidden.Contains(layer.Name) || _vpFrozen.Contains(layer.Name);
        }

        /// <summary>Blocks: an entity on layer 0 takes the insert's layer.</summary>
        private static Layer? EffectiveLayer(Entity e, Layer? parentLayer) =>
            parentLayer != null && (e.Layer == null || e.Layer.Name == "0") ? parentLayer : e.Layer;

        // The enclosing block reference's resolved colour number, plot colour and lineweight, for
        // entities inside it that are ByBlock.
        private short _blockAci = 7;
        private uint _blockPlotRgb;
        private double _blockLw = -1;

        /// <summary>An entity's colour number, true plot colour and lineweight (mm, -1 = default),
        /// with ByLayer and ByBlock resolved the way AutoCAD plots them.</summary>
        private (short Aci, uint PlotRgb, double LwMm) PlotStyle(Entity e, Layer? layer)
        {
            short aci; uint rgb;
            var c = e.Color;
            if (c.IsByBlock) { aci = _blockAci; rgb = _blockPlotRgb; }
            else
            {
                if (c.IsByLayer) c = layer?.Color ?? new Color(7);
                if (c.IsTrueColor) { aci = -1; rgb = (uint)(c.R << 16 | c.G << 8 | c.B); }
                else
                {
                    aci = c.Index <= 0 || c.Index >= 256 ? (short)7 : c.Index;
                    var v = Color.GetIndexRGB((byte)aci);
                    rgb = aci == 7 ? 0x000000u : (uint)(v[0] << 16 | v[1] << 8 | v[2]);
                }
            }
            double lw;
            var w = e.LineWeight;
            if (w == LineWeightType.ByBlock) lw = _blockLw;
            else
            {
                if (w == LineWeightType.ByLayer) w = layer?.LineWeight ?? LineWeightType.Default;
                lw = (short)w >= 0 ? (short)w / 100.0 : -1;
            }
            return (aci, rgb, lw);
        }

        private uint Rgb(Entity e, Layer? layer, uint? parentRgb)
        {
            var c = e.Color;
            if (c.IsByBlock) return parentRgb ?? (_dark ? 0xFFFFFFu : 0x000000u);
            if (c.IsByLayer) c = layer?.Color ?? new Color(7);
            if (c.IsTrueColor) return (uint)(c.R << 16 | c.G << 8 | c.B);
            short i = c.Index;
            if (_dark)
            {
                if (i <= 0 || i == 7 || i >= 256) return 0xFFFFFF;
                var d = Color.GetIndexRGB((byte)i);
                return (uint)(d[0] << 16 | d[1] << 8 | d[2]);
            }
            // 7 is "white on black, black on white"; plans are viewed on white paper.
            if (i <= 0 || i == 7 || i >= 256) return 0x000000;
            var rgb = Color.GetIndexRGB((byte)i);
            uint v = (uint)(rgb[0] << 16 | rgb[1] << 8 | rgb[2]);
            // Very light colours (yellow 2, 50-ish) vanish on white; darken them for the screen.
            return Readable(v);
        }

        private static uint Readable(uint rgb)
        {
            double r = (rgb >> 16) & 255, g = (rgb >> 8) & 255, b = rgb & 255;
            double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            if (lum < 170) return rgb;
            double k = 170 / lum;
            return (uint)((int)(r * k) << 16 | (int)(g * k) << 8 | (int)(b * k));
        }

        // ---- entities ---------------------------------------------------------------------

        private void Emit(Entity e, Affine t, Layer? parentLayer, uint? parentRgb, ulong handle, int depth)
        {
            if (e.IsInvisible) return;
            var layer = EffectiveLayer(e, parentLayer);
            if (IsHiddenLayer(layer)) return;
            var style = PlotStyle(e, layer);
            var group = _group;
            int first = group.Prims.Count;
            var (saveAci, saveRgb, saveLw) = (_blockAci, _blockPlotRgb, _blockLw);
            (_blockAci, _blockPlotRgb, _blockLw) = style; // for anything inside this one that is ByBlock
            try { EmitShape(e, t, layer, parentRgb, handle, depth); }
            finally { (_blockAci, _blockPlotRgb, _blockLw) = (saveAci, saveRgb, saveLw); }
            // Stamp what this entity drew; nested block contents already stamped themselves.
            for (int i = first; i < group.Prims.Count; i++)
            {
                var p = group.Prims[i];
                if (p.Aci != -2) continue;
                p.Aci = style.Aci; p.PlotRgb = style.PlotRgb; p.LineWeightMm = style.LwMm;
            }
        }

        private void EmitShape(Entity e, Affine t, Layer? layer, uint? parentRgb, ulong handle, int depth)
        {
            uint rgb = Rgb(e, layer, parentRgb);
            string lname = layer?.Name ?? "0";

            switch (e)
            {
                case Line l:
                    Poly(new[] { t.Apply(l.StartPoint.X, l.StartPoint.Y), t.Apply(l.EndPoint.X, l.EndPoint.Y) }, false, rgb, lname, handle, snapVertices: true);
                    break;
                case LwPolyline pl:
                    Bulged(pl.Vertices.Select(v => (new Vec2(v.Location.X, v.Location.Y), v.Bulge)).ToList(), pl.IsClosed, t, rgb, lname, handle);
                    break;
                case Polyline2D p2:
                    Bulged(p2.Vertices.Select(v => (new Vec2(v.Location.X, v.Location.Y), v.Bulge)).ToList(), p2.IsClosed, t, rgb, lname, handle);
                    break;
                case ACadSharp.Entities.Arc a:
                {
                    double sweep = a.EndAngle - a.StartAngle;
                    while (sweep <= 0) sweep += 2 * Math.PI;
                    var pts = ArcPoints(new Vec2(a.Center.X, a.Center.Y), a.Radius, a.StartAngle, sweep).Select(t.Apply).ToList();
                    Poly(pts, false, rgb, lname, handle, snapVertices: false);
                    AddSnap(pts[0], SnapKind.Endpoint); AddSnap(pts[pts.Count - 1], SnapKind.Endpoint);
                    AddSnap(t.Apply(a.Center.X, a.Center.Y), SnapKind.Center);
                    break;
                }
                case Circle c:
                {
                    var center = t.Apply(c.Center.X, c.Center.Y);
                    _group.Prims.Add(new Prim { Kind = PrimKind.Circle, Center = center, Radius = c.Radius * t.ScaleFactor, Rgb = rgb, Layer = lname, Handle = handle });
                    AddSnap(center, SnapKind.Center);
                    break;
                }
                case Ellipse el:
                    Poly(el.PolygonalVertexes(64).Select(p => t.Apply(p.X, p.Y)).ToList(), false, rgb, lname, handle, snapVertices: false);
                    break;
                case Spline sp:
                    if (sp.TryPolygonalVertexes(96, out var spts))
                        Poly(spts.Select(p => t.Apply(p.X, p.Y)).ToList(), false, rgb, lname, handle, snapVertices: false);
                    else _skipped++;
                    break;
                case Solid so:
                    _group.Prims.Add(new Prim
                    {
                        Kind = PrimKind.Fill, Rgb = rgb, Layer = lname, Handle = handle, Closed = true,
                        // SOLID corners run zig-zag: 1, 2, 4, 3 walks the outline.
                        Points = new List<Vec2> { t.Apply(so.FirstCorner.X, so.FirstCorner.Y), t.Apply(so.SecondCorner.X, so.SecondCorner.Y), t.Apply(so.FourthCorner.X, so.FourthCorner.Y), t.Apply(so.ThirdCorner.X, so.ThirdCorner.Y) },
                    });
                    break;
                case Point pt:
                {
                    var p = t.Apply(pt.Location.X, pt.Location.Y);
                    _group.Prims.Add(new Prim { Kind = PrimKind.Node, Center = p, Rgb = rgb, Layer = lname, Handle = handle });
                    AddSnap(p, SnapKind.Node);
                    break;
                }
                case AttributeEntity att:
                    if (!att.Flags.HasFlag(ACadSharp.Entities.AttributeFlags.Hidden)) Text(att, t, rgb, lname, handle);
                    break;
                case TextEntity te:
                    Text(te, t, rgb, lname, handle);
                    break;
                case MText mt:
                    MTextLines(mt, t, rgb, lname, handle);
                    break;
                case Insert ins:
                    InsertBlock(ins, t, layer, rgb, handle, depth);
                    break;
                case Dimension dim:
                    if (dim.Block != null && depth < 8)
                        foreach (var be in dim.Block.Entities) Emit(be, t, layer, rgb, handle, depth + 1);
                    break;
                case Leader ld:
                    EmitLeader(ld, t, rgb, lname, handle);
                    break;
                case Viewport:
                    break;
                default:
                    _skipped++;
                    break;
            }
        }

        private void InsertBlock(Insert ins, Affine t, Layer? layer, uint rgb, ulong handle, int depth)
        {
            if (ins.Block == null || depth > 8) return;
            var basePoint = ins.Block.BlockEntity?.BasePoint ?? XYZ.Zero;
            for (int r = 0; r < Math.Max((int)ins.RowCount, 1); r++)
            {
                for (int c = 0; c < Math.Max((int)ins.ColumnCount, 1); c++)
                {
                    var local = Affine.Translate(ins.InsertPoint.X, ins.InsertPoint.Y)
                        .After(Affine.Rotate(ins.Rotation))
                        .After(Affine.Translate(c * ins.ColumnSpacing, r * ins.RowSpacing))
                        .After(Affine.Scale(ins.XScale, ins.YScale))
                        .After(Affine.Translate(-basePoint.X, -basePoint.Y));
                    var bt = t.After(local);
                    foreach (var be in ins.Block.Entities)
                    {
                        if (be is AttributeDefinition) continue; // definitions show only through their attributes
                        Emit(be, bt, layer, rgb, handle, depth + 1);
                    }
                }
            }
            AddSnap(t.Apply(ins.InsertPoint.X, ins.InsertPoint.Y), SnapKind.Node);
            // Attributes are stored in the insert's own coordinate space, not the block's.
            foreach (var a in ins.Attributes) Emit(a, t, layer, rgb, handle, depth + 1);
        }

        /// <summary>A LEADER's shaft (its own vertices, no bulge) plus a small arrowhead at the
        /// first vertex when <see cref="Leader.ArrowHeadEnabled"/>, aimed back along the shaft -
        /// the annotation text, if any, is a separate MTEXT/TEXT entity drawn on its own.</summary>
        private void EmitLeader(Leader ld, Affine t, uint rgb, string layer, ulong handle)
        {
            if (ld.Vertices.Count < 2) return;
            var pts = ld.Vertices.Select(v => t.Apply(v.X, v.Y)).ToList();
            Poly(pts, false, rgb, layer, handle, snapVertices: true);
            if (!ld.ArrowHeadEnabled) return;
            var tip = pts[0];
            var toward = (pts[1] - tip);
            if (toward.Length < 1e-9) return;
            var u = toward.Normalized();
            var n = u.Left();
            double headLen = Math.Min(0.15 * t.ScaleFactor, toward.Length * 0.2);
            var wing1 = tip + u * headLen - n * (headLen * 0.35);
            var wing2 = tip + u * headLen + n * (headLen * 0.35);
            Poly(new[] { wing1, tip, wing2 }, false, rgb, layer, handle, snapVertices: false);
        }

        private void Text(TextEntity te, Affine t, uint rgb, string layer, ulong handle)
        {
            if (string.IsNullOrWhiteSpace(te.Value)) return;
            bool aligned = te.HorizontalAlignment != TextHorizontalAlignment.Left || te.VerticalAlignment != TextVerticalAlignmentType.Baseline;
            var at = aligned ? te.AlignmentPoint : te.InsertPoint;
            var h = te.HorizontalAlignment switch
            {
                TextHorizontalAlignment.Center or TextHorizontalAlignment.Middle or TextHorizontalAlignment.Aligned or TextHorizontalAlignment.Fit => HAlign.Center,
                TextHorizontalAlignment.Right => HAlign.Right,
                _ => HAlign.Left,
            };
            var v = te.HorizontalAlignment == TextHorizontalAlignment.Middle ? VAlign.Middle : te.VerticalAlignment switch
            {
                TextVerticalAlignmentType.Middle => VAlign.Middle,
                TextVerticalAlignmentType.Top => VAlign.Top,
                _ => VAlign.Bottom,
            };
            _group.Prims.Add(new Prim
            {
                Kind = PrimKind.Text, Text = Decode(te.Value), Center = t.Apply(at.X, at.Y),
                Height = te.Height * t.ScaleFactor, Rotation = te.Rotation + t.Rotation, WidthFactor = te.WidthFactor <= 0 ? 1 : te.WidthFactor,
                H = h, V = v, Rgb = rgb, Layer = layer, Handle = handle,
            });
        }

        private void MTextLines(MText mt, Affine t, uint rgb, string layer, ulong handle)
        {
            var lines = PlainMText(mt.Value);
            if (lines.Count == 0) return;
            // AutoCAD wraps each paragraph to the MTEXT's box width; without that, a long note
            // runs off the sheet as one line.
            if (mt.RectangleWidth > 0) lines = Wrap(lines, mt.RectangleWidth, mt.Height);
            int ap = (int)mt.AttachmentPoint; // 1..9: TL TC TR ML MC MR BL BC BR
            var h = ap % 3 == 1 ? HAlign.Left : ap % 3 == 2 ? HAlign.Center : HAlign.Right;
            double pitch = mt.Height * 1.667 * (mt.LineSpacing <= 0 ? 1 : mt.LineSpacing);
            double blockH = pitch * (lines.Count - 1);
            // Offset of the first line's top from the attachment point, along the text's "up" direction.
            double firstTop = ap <= 3 ? 0 : ap <= 6 ? blockH / 2 + mt.Height / 2 : blockH + mt.Height;
            double rot = mt.Rotation;
            var up = new Vec2(-Math.Sin(rot), Math.Cos(rot));
            var origin = new Vec2(mt.InsertPoint.X, mt.InsertPoint.Y);
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Length == 0) continue;
                var top = origin + up * (firstTop - i * pitch);
                _group.Prims.Add(new Prim
                {
                    Kind = PrimKind.Text, Text = lines[i], Center = t.Apply(top), Height = mt.Height * t.ScaleFactor,
                    Rotation = rot + t.Rotation, H = h, V = VAlign.Top, Rgb = rgb, Layer = layer, Handle = handle,
                });
            }
        }

        /// <summary>
        /// Word-wraps paragraphs to <paramref name="width"/> at cap height <paramref name="height"/>
        /// (Helvetica metrics, as the PDF plots). A single word wider than the box stays whole on
        /// its own line, as in AutoCAD; leading spaces (used in notes for hanging indents) are kept.
        /// </summary>
        public static List<string> Wrap(List<string> paragraphs, double width, double height)
        {
            var result = new List<string>();
            foreach (var para in paragraphs)
            {
                if (para.Length == 0 || PdfSceneWriter.MeasureText(para, height) <= width) { result.Add(para); continue; }
                int lead = para.Length - para.TrimStart(' ').Length;
                var words = para.Substring(lead).Split(' ');
                var line = new System.Text.StringBuilder(new string(' ', lead));
                bool empty = true;
                foreach (var w in words)
                {
                    string candidate = empty ? line + w : line + " " + w;
                    if (!empty && PdfSceneWriter.MeasureText(candidate, height) > width)
                    {
                        result.Add(line.ToString());
                        line.Clear().Append(w);
                    }
                    else { line.Clear().Append(candidate); }
                    empty = false;
                }
                result.Add(line.ToString());
            }
            return result;
        }

        /// <summary>%%d, %%c, %%p and %%nnn control codes as the characters they draw.</summary>
        public static string Decode(string s)
        {
            if (s.IndexOf("%%", StringComparison.Ordinal) < 0) return s;
            s = Regex.Replace(s, "%%([0-9]{3})", m => ((char)int.Parse(m.Groups[1].Value)).ToString());
            return s.Replace("%%d", "°").Replace("%%D", "°").Replace("%%c", "Ø").Replace("%%C", "Ø")
                    .Replace("%%p", "±").Replace("%%P", "±").Replace("%%u", "").Replace("%%U", "").Replace("%%o", "").Replace("%%O", "").Replace("%%%", "%");
        }

        /// <summary>MTEXT contents as plain lines: formatting codes removed, \P split.</summary>
        public static List<string> PlainMText(string value)
        {
            var s = value.Replace("\\P", "\n").Replace("\\~", " ");
            s = Regex.Replace(s, @"\\U\+([0-9A-Fa-f]{4})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
            s = Regex.Replace(s, @"\\[fFHhCcTtQqWwAp][^;]*;", "");        // font, height, colour, tracking, oblique, width, align, paragraph
            s = Regex.Replace(s, @"\\S([^;]*)\^([^;]*);", "$1/$2");          // stacked fractions
            s = Regex.Replace(s, @"\\[LlOoKkNn]", "");
            s = s.Replace("\\\\", "\\").Replace("{", "").Replace("}", "");
            return Decode(s).Split('\n').Select(x => x.TrimEnd()).ToList();
        }

        // ---- geometry helpers -------------------------------------------------------------

        private void Bulged(List<(Vec2 p, double bulge)> v, bool closed, Affine t, uint rgb, string layer, ulong handle)
        {
            if (v.Count == 0) return;
            var pts = new List<Vec2> { t.Apply(v[0].p) };
            int n = v.Count, segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                var a = v[i].p; var b = v[(i + 1) % n].p; double bl = v[i].bulge;
                if (Math.Abs(bl) > 1e-9)
                {
                    double theta = 4 * Math.Atan(bl);
                    double chord = Vec2.Distance(a, b);
                    if (chord > 1e-12)
                    {
                        double r = chord / (2 * Math.Sin(Math.Abs(theta) / 2));
                        var mid = (a + b) * 0.5;
                        var n1 = (b - a).Normalized().Left();
                        double off = r * Math.Cos(theta / 2) * Math.Sign(bl);
                        var c = mid + n1 * off;
                        double start = Math.Atan2(a.Y - c.Y, a.X - c.X);
                        var arc = ArcPoints(c, r, start, theta);
                        for (int k = 1; k < arc.Count - 1; k++) pts.Add(t.Apply(arc[k]));
                        AddSnap(t.Apply(c), SnapKind.Center);
                    }
                }
                else AddSnap(t.Apply((a + b) * 0.5), SnapKind.Midpoint);
                pts.Add(t.Apply(b));
            }
            _group.Prims.Add(new Prim { Kind = PrimKind.Polyline, Points = pts, Closed = false, Rgb = rgb, Layer = layer, Handle = handle });
            foreach (var x in v) AddSnap(t.Apply(x.p), SnapKind.Endpoint);
        }

        private void Poly(IList<Vec2> pts, bool closed, uint rgb, string layer, ulong handle, bool snapVertices)
        {
            if (pts.Count < 2) return;
            var list = new List<Vec2>(pts);
            if (closed) list.Add(pts[0]);
            _group.Prims.Add(new Prim { Kind = PrimKind.Polyline, Points = list, Rgb = rgb, Layer = layer, Handle = handle });
            if (snapVertices)
            {
                foreach (var p in pts) AddSnap(p, SnapKind.Endpoint);
                if (pts.Count == 2) AddSnap((pts[0] + pts[1]) * 0.5, SnapKind.Midpoint);
            }
        }

        /// <summary>Points along an arc, about every 3 degrees (never fewer than 4 spans).</summary>
        public static List<Vec2> ArcPoints(Vec2 c, double r, double start, double sweep)
        {
            int n = Math.Max(4, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 60)));
            var list = new List<Vec2>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                double a = start + sweep * i / n;
                list.Add(new Vec2(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)));
            }
            return list;
        }

        private void AddSnap(Vec2 p, SnapKind k)
        {
            if (_group.Clip.HasValue)
            {
                var c = _group.Clip.Value;
                if (p.X < c.X1 || p.X > c.X2 || p.Y < c.Y1 || p.Y > c.Y2) return;
            }
            _scene.Snaps.Add(new SnapPoint(p, k));
        }
    }
}
