using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FdDraft.Core.Drafting;
using FdDraft.Core.Standards;

namespace FdDraft.AutoCAD
{
    /// <summary>
    /// Draws a DraftDocument into an AutoCAD database's model space. The template
    /// wins every tie: an existing layer keeps its colour, linetype and plot flag, an
    /// existing text style and block are used as they are. Only what the template
    /// lacks is created, and every such creation is reported.
    /// </summary>
    internal sealed class AcadRenderer
    {
        private readonly Database _db;
        private readonly Transaction _tr;
        private readonly FirmStandards _std;
        private readonly double _modelPerMm;
        private readonly List<string> _report;
        private readonly Dictionary<string, ObjectId> _layers = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ObjectId> _styles = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _missingBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public List<string> CreatedLayers { get; } = new List<string>();

        public AcadRenderer(Database db, Transaction tr, FirmStandards std, double modelPerMm, List<string> report)
        {
            _db = db; _tr = tr; _std = std; _modelPerMm = modelPerMm; _report = report;
        }

        public void ClearModelSpace()
        {
            var ms = ModelSpace(OpenMode.ForRead);
            int n = 0;
            foreach (ObjectId id in ms)
            {
                var ent = (Entity)_tr.GetObject(id, OpenMode.ForWrite);
                ent.Erase();
                n++;
            }
            if (n > 0) _report.Add("Cleared " + n + " sample entities from the template's model space.");
        }

        public void Draw(DraftDocument doc)
        {
            foreach (var layer in doc.Layers.Values) Layer(layer.Name, layer.Aci, layer.LineType);
            var ms = ModelSpace(OpenMode.ForWrite);

            foreach (var e in doc.Entities)
            {
                switch (e)
                {
                    case DraftPolyline pl: DrawPolyline(ms, pl); break;
                    case DraftSpline sp: DrawSpline(ms, sp); break;
                    case DraftCircle c:
                        Add(ms, new Circle(new Point3d(c.Center.X, c.Center.Y, 0), Vector3d.ZAxis, c.Radius), c.Layer);
                        break;
                    case DraftSymbol s: DrawSymbol(ms, s); break;
                    case DraftText t: DrawText(ms, t); break;
                }
            }
            foreach (var b in _missingBlocks)
                _report.Add("Block '" + b + "' is not in the template; a plain symbol was drawn instead.");
        }

        // ---- entities --------------------------------------------------------------------

        private void DrawPolyline(BlockTableRecord ms, DraftPolyline d)
        {
            var pl = new Polyline();
            for (int i = 0; i < d.Vertices.Count; i++)
                pl.AddVertexAt(i, new Point2d(d.Vertices[i].X, d.Vertices[i].Y), d.Bulges[i], 0, 0);
            pl.Closed = d.Closed;
            Add(ms, pl, d.Layer);
        }

        private void DrawSpline(BlockTableRecord ms, DraftSpline d)
        {
            var fit = new Point3dCollection();
            foreach (var p in d.FitPoints) fit.Add(new Point3d(p.X, p.Y, 0));
            // A true fit-point spline, like AutoCAD's own SPLINE command: degree 3, zero tolerance.
            Add(ms, new Spline(fit, 4, 0.0), d.Layer);
        }

        private void DrawSymbol(BlockTableRecord ms, DraftSymbol s)
        {
            // A node at the true elevation, for snapping and for a surface later.
            Add(ms, new DBPoint(new Point3d(s.Position.X, s.Position.Y, s.Elevation)), s.Layer);

            ObjectId block = ObjectId.Null;
            double scale;
            if (s.BlockName.Length > 0)
            {
                block = BlockId(s.BlockName);
                if (block.IsNull) _missingBlocks.Add(s.BlockName);
            }
            if (!block.IsNull)
            {
                // Template blocks are drawn so that 1 block unit = BlockUnitMm on paper.
                scale = _std.BlockUnitMm * _modelPerMm;
            }
            else
            {
                block = FallbackSymbol(s.Symbol);
                scale = s.SizeMm * _modelPerMm;
            }
            var br = new BlockReference(new Point3d(s.Position.X, s.Position.Y, 0), block) { ScaleFactors = new Scale3d(scale) };
            Add(ms, br, s.Layer);
        }

        private void DrawText(BlockTableRecord ms, DraftText d)
        {
            var t = new DBText();
            t.SetDatabaseDefaults(_db);
            t.TextStyleId = Style(d.Style);
            t.Height = d.HeightMm * _modelPerMm;
            t.TextString = AcadText(d.Text);
            t.Rotation = d.Rotation;
            t.HorizontalMode = d.H == HAlign.Left ? TextHorizontalMode.TextLeft : d.H == HAlign.Center ? TextHorizontalMode.TextCenter : TextHorizontalMode.TextRight;
            t.VerticalMode = d.V == VAlign.Bottom ? TextVerticalMode.TextBottom : d.V == VAlign.Middle ? TextVerticalMode.TextVerticalMid : TextVerticalMode.TextTop;
            var p = new Point3d(d.Position.X, d.Position.Y, 0);
            t.Position = p;
            t.AlignmentPoint = p; // every mode used here is aligned, not left-baseline
            Add(ms, t, d.Layer);
            t.AdjustAlignment(_db);
        }

        /// <summary>SHX fonts (msurvey.shx) have no ° or ² glyph; AutoCAD's control codes draw them.</summary>
        public static string AcadText(string s) => s.Replace("°", "%%d").Replace("²", "%%178");

        private void Add(BlockTableRecord owner, Entity e, string layer)
        {
            e.SetDatabaseDefaults(_db);
            e.LayerId = Layer(layer, 7, "");
            owner.AppendEntity(e);
            _tr.AddNewlyCreatedDBObject(e, true);
        }

        // ---- tables ----------------------------------------------------------------------

        private BlockTableRecord ModelSpace(OpenMode mode)
        {
            var bt = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
            return (BlockTableRecord)_tr.GetObject(bt[BlockTableRecord.ModelSpace], mode);
        }

        public ObjectId Layer(string name, int aci, string lineType)
        {
            if (_layers.TryGetValue(name, out var cached)) return cached;
            var lt = (LayerTable)_tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
            ObjectId id;
            if (lt.Has(name))
            {
                id = lt[name];
            }
            else
            {
                var rec = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, (short)Math.Max(1, Math.Min(255, aci))) };
                var ltId = LineType(lineType);
                if (!ltId.IsNull) rec.LinetypeObjectId = ltId;
                if (!lt.IsWriteEnabled) lt.UpgradeOpen();
                id = lt.Add(rec);
                _tr.AddNewlyCreatedDBObject(rec, true);
                CreatedLayers.Add(name);
            }
            _layers[name] = id;
            return id;
        }

        private ObjectId LineType(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Equals("ByLayer", StringComparison.OrdinalIgnoreCase) || name.Equals("Continuous", StringComparison.OrdinalIgnoreCase))
                return ObjectId.Null;
            var table = (LinetypeTable)_tr.GetObject(_db.LinetypeTableId, OpenMode.ForRead);
            if (table.Has(name)) return table[name];
            try
            {
                _db.LoadLineTypeFile(name, "acadiso.lin");
                table = (LinetypeTable)_tr.GetObject(_db.LinetypeTableId, OpenMode.ForRead);
                if (table.Has(name)) return table[name];
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { /* not in acadiso.lin */ }
            _report.Add("Linetype '" + name + "' is in neither the template nor acadiso.lin; its layer uses Continuous.");
            return ObjectId.Null;
        }

        private ObjectId Style(string name)
        {
            if (string.IsNullOrEmpty(name)) return _db.Textstyle;
            if (_styles.TryGetValue(name, out var id)) return id;
            var table = (TextStyleTable)_tr.GetObject(_db.TextStyleTableId, OpenMode.ForRead);
            id = table.Has(name) ? table[name] : _db.Textstyle;
            if (!table.Has(name)) _report.Add("Text style '" + name + "' is not in the template; the current style was used.");
            _styles[name] = id;
            return id;
        }

        private ObjectId BlockId(string name)
        {
            var bt = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
            return bt.Has(name) ? bt[name] : ObjectId.Null;
        }

        /// <summary>A block for one of FD-Pro's symbol shapes, created once, unit size = 1 paper mm across.</summary>
        private ObjectId FallbackSymbol(string symbol)
        {
            string name = "FD-SYM-" + (string.IsNullOrEmpty(symbol) ? "DOT" : symbol.ToUpperInvariant());
            var existing = BlockId(name);
            if (!existing.IsNull) return existing;

            var bt = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForWrite);
            var btr = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
            var id = bt.Add(btr);
            _tr.AddNewlyCreatedDBObject(btr, true);
            foreach (var part in SymbolShapes.Unit(symbol))
            {
                Entity ent;
                if (part.CircleRadius > 0)
                {
                    var c = part.Points[0];
                    if (part.Filled)
                    {
                        // A donut: two half-circle arcs whose width fills the disc.
                        double r = part.CircleRadius;
                        var donut = new Polyline();
                        donut.AddVertexAt(0, new Point2d(c.X - r / 2, c.Y), 1, r, r);
                        donut.AddVertexAt(1, new Point2d(c.X + r / 2, c.Y), 1, r, r);
                        donut.Closed = true;
                        ent = donut;
                    }
                    else ent = new Circle(new Point3d(c.X, c.Y, 0), Vector3d.ZAxis, part.CircleRadius);
                }
                else if (part.Filled && (part.Points.Count == 3 || part.Points.Count == 4))
                {
                    var p = part.Points;
                    ent = p.Count == 3
                        ? new Solid(P(p[0]), P(p[1]), P(p[2]))
                        : new Solid(P(p[0]), P(p[1]), P(p[3]), P(p[2])); // SOLID corners go in zig-zag order
                }
                else
                {
                    var pl = new Polyline();
                    for (int i = 0; i < part.Points.Count; i++) pl.AddVertexAt(i, new Point2d(part.Points[i].X, part.Points[i].Y), 0, 0, 0);
                    pl.Closed = part.Closed;
                    ent = pl;
                }
                ent.Layer = "0";
                ent.ColorIndex = 0; // ByBlock: the symbol takes its insert's layer colour
                btr.AppendEntity(ent);
                _tr.AddNewlyCreatedDBObject(ent, true);
            }
            return id;
        }

        private static Point3d P(FdDraft.Core.Geometry.Vec2 v) => new Point3d(v.X, v.Y, 0);
    }
}
