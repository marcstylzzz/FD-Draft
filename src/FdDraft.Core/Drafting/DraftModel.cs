using System.Collections.Generic;
using FdDraft.Core.Geometry;

namespace FdDraft.Core.Drafting
{
    /// <summary>
    /// A CAD-neutral drawing. The AutoCAD plugin, the DXF writer and the SVG preview
    /// all render this one model, so what the preview shows is what AutoCAD gets.
    /// Coordinates are model units (the job's units), X = easting, Y = northing.
    /// Text heights and symbol sizes are PAPER millimetres; a writer multiplies by
    /// the chosen scale, which is what keeps text the same size on every sheet.
    /// </summary>
    public sealed class DraftDocument
    {
        public string JobName { get; set; } = "";
        public Dictionary<string, DraftLayer> Layers { get; } = new Dictionary<string, DraftLayer>(System.StringComparer.OrdinalIgnoreCase);
        public List<DraftEntity> Entities { get; } = new List<DraftEntity>();
        /// <summary>Every course that could carry a bearing and distance, deduplicated across figures.</summary>
        public List<Course> Courses { get; } = new List<Course>();
        /// <summary>Closed parcels, for area labels and closure reporting.</summary>
        public List<Parcel> Parcels { get; } = new List<Parcel>();
        public List<string> Warnings { get; } = new List<string>();
        /// <summary>Model units per paper unit of the scale the annotation was built for; 0 before annotation.</summary>
        public double AnnotatedModelPerPaper { get; set; }

        public DraftLayer Layer(string name, int aci = 7, string lineType = "")
        {
            if (!Layers.TryGetValue(name, out var layer))
            {
                layer = new DraftLayer { Name = name, Aci = aci, LineType = lineType };
                Layers[name] = layer;
            }
            return layer;
        }

        /// <summary>Extents of the survey geometry only - labels are placed after the scale is chosen from this.</summary>
        public Extents GeometryExtents()
        {
            var e = new Extents();
            foreach (var entity in Entities)
            {
                if (entity is DraftText) continue;
                entity.AddTo(e);
            }
            return e;
        }
    }

    public sealed class DraftLayer
    {
        public string Name { get; set; } = "";
        /// <summary>AutoCAD Colour Index. Used only when the template does not already have the layer.</summary>
        public int Aci { get; set; } = 7;
        public string LineType { get; set; } = "";
    }

    public abstract class DraftEntity
    {
        public string Layer { get; set; } = "0";
        public abstract void AddTo(Extents e);
    }

    /// <summary>Straight and arc segments. Bulges[i] belongs to the segment from vertex i to i+1.</summary>
    public sealed class DraftPolyline : DraftEntity
    {
        public List<Vec2> Vertices { get; } = new List<Vec2>();
        public List<double> Bulges { get; } = new List<double>();
        public bool Closed { get; set; }
        public string Code { get; set; } = "";

        public void Add(Vec2 p, double bulgeToNext = 0)
        {
            Vertices.Add(p);
            Bulges.Add(bulgeToNext);
        }

        public override void AddTo(Extents e)
        {
            // Vertices plus arc midpoints: close enough for choosing a sheet.
            int n = Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                e.Add(Vertices[i]);
                if (Bulges[i] != 0 && (i + 1 < n || Closed))
                {
                    var a = Vertices[i];
                    var b = Vertices[(i + 1) % n];
                    var chord = b - a;
                    var mid = (a + b) * 0.5;
                    // Sagitta = bulge * half chord, to the right of travel for a positive (CCW) bulge.
                    var sag = chord.Left() * (-Bulges[i] * 0.5);
                    e.Add(mid + sag);
                }
            }
        }
    }

    /// <summary>A smooth curve through fit points (FD-Pro SPLINE figures).</summary>
    public sealed class DraftSpline : DraftEntity
    {
        public List<Vec2> FitPoints { get; } = new List<Vec2>();
        public bool Closed { get; set; }
        public override void AddTo(Extents e) { foreach (var p in FitPoints) e.Add(p); }

        /// <summary>
        /// Centripetal Catmull-Rom samples through the fit points, for writers that
        /// have no spline entity (DXF R12, SVG). AutoCAD gets a real SPLINE.
        /// </summary>
        public List<Vec2> Sample(int perSpan = 16)
        {
            var pts = FitPoints;
            var output = new List<Vec2>();
            if (pts.Count < 3) { output.AddRange(pts); return output; }
            int n = pts.Count;
            int spans = Closed ? n : n - 1;
            for (int i = 0; i < spans; i++)
            {
                Vec2 p0 = Closed ? pts[(i - 1 + n) % n] : pts[System.Math.Max(i - 1, 0)];
                Vec2 p1 = pts[i % n];
                Vec2 p2 = pts[(i + 1) % n];
                Vec2 p3 = Closed ? pts[(i + 2) % n] : pts[System.Math.Min(i + 2, n - 1)];
                for (int s = 0; s < perSpan; s++)
                {
                    double t = (double)s / perSpan;
                    double t2 = t * t, t3 = t2 * t;
                    output.Add(new Vec2(
                        0.5 * ((2 * p1.X) + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3),
                        0.5 * ((2 * p1.Y) + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3)));
                }
            }
            output.Add(Closed ? pts[0] : pts[n - 1]);
            return output;
        }
    }

    public sealed class DraftCircle : DraftEntity
    {
        public Vec2 Center { get; set; }
        public double Radius { get; set; }
        public override void AddTo(Extents e)
        {
            e.Add(new Vec2(Center.X - Radius, Center.Y - Radius));
            e.Add(new Vec2(Center.X + Radius, Center.Y + Radius));
        }
    }

    /// <summary>A surveyed point's symbol. BlockName is the template block to insert, when the firm has one.</summary>
    public sealed class DraftSymbol : DraftEntity
    {
        public Vec2 Position { get; set; }
        public int PointId { get; set; }
        public string Code { get; set; } = "";
        /// <summary>FD-Pro symbol name (DOT, TRIANGLE_FILLED, ...), the fallback shape.</summary>
        public string Symbol { get; set; } = "DOT";
        public string BlockName { get; set; } = "";
        public double SizeMm { get; set; } = 1.5;
        public double Elevation { get; set; }
        public string Note { get; set; } = "";
        /// <summary>Feature name used to build per-feature layers (MSPOINT-{feature} etc.).</summary>
        public string Feature { get; set; } = "";
        public string NumberLayer { get; set; } = "";
        public string ElevationLayer { get; set; } = "";
        /// <summary>Plan abbreviation for a monument (SIB, IB, CP ...); empty for other points.</summary>
        public string MonumentText { get; set; } = "";
        public bool ShowElevation { get; set; } = true;
        public override void AddTo(Extents e) => e.Add(Position);
    }

    public enum HAlign { Left, Center, Right }
    public enum VAlign { Bottom, Middle, Top }

    public enum TextKind { Bearing, Distance, ArcData, PointNumber, PointCode, PointElevation, Monument, Area, Other }

    public sealed class DraftText : DraftEntity
    {
        public Vec2 Position { get; set; }
        public string Text { get; set; } = "";
        public double HeightMm { get; set; } = 2.0;
        /// <summary>Radians, counter-clockwise from +X.</summary>
        public double Rotation { get; set; }
        public HAlign H { get; set; } = HAlign.Left;
        public VAlign V { get; set; } = VAlign.Bottom;
        public TextKind Kind { get; set; } = TextKind.Other;
        /// <summary>The survey point this label belongs to (point number, elevation, monument
        /// text), so the DWG entity can be tagged with it.</summary>
        public int? PointId { get; set; }
        /// <summary>For a course label: what it says ("B", "D", "A0", "A1" - see the CAD layer's
        /// CourseLinks) and the course it was made for, so it can be linked to the drawn linework.</summary>
        public string CourseKind { get; set; } = "";
        public Vec2 CourseA { get; set; }
        public Vec2 CourseB { get; set; }
        /// <summary>Template text style name; empty = the drawing's current style.</summary>
        public string Style { get; set; } = "";
        public override void AddTo(Extents e) => e.Add(Position);
    }

    /// <summary>One course (a straight or arc boundary segment) between two surveyed points.</summary>
    public sealed class Course
    {
        public int FromId { get; set; }
        public int ToId { get; set; }
        public Vec2 A { get; set; }
        public Vec2 B { get; set; }
        /// <summary>Non-null for a curved course.</summary>
        public Arc? Arc { get; set; }
        public string Code { get; set; } = "";
        public bool Labelled { get; set; }
        /// <summary>
        /// Which side of A->B is inside the parcel: +1 left, -1 right, 0 unknown.
        /// Labels go on the outside when this is known.
        /// </summary>
        public int InsideSide { get; set; }
        public double Length => Arc != null ? Arc.Length : Vec2.Distance(A, B);
    }

    public sealed class Parcel
    {
        public string Code { get; set; } = "";
        public List<Vec2> Vertices { get; } = new List<Vec2>();
        public List<double> Bulges { get; } = new List<double>();
        public List<int> PointIds { get; } = new List<int>();
        public double Area => System.Math.Abs(Polygon.SignedArea(Vertices, Bulges));
        public double Perimeter { get; set; }
    }
}
