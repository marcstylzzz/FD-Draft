using System;
using System.Collections.Generic;
using FdDraft.Core.Drafting;
using FdDraft.Core.Geometry;
using FdDraft.Core.Standards;

namespace FdDraft.View
{
    /// <summary>A 2D affine transform: x' = A x + C y + E, y' = B x + D y + F.</summary>
    public readonly struct Affine
    {
        public readonly double A, B, C, D, E, F;
        public Affine(double a, double b, double c, double d, double e, double f) { A = a; B = b; C = c; D = d; E = e; F = f; }

        public static readonly Affine Identity = new Affine(1, 0, 0, 1, 0, 0);
        public static Affine Translate(double x, double y) => new Affine(1, 0, 0, 1, x, y);
        public static Affine Scale(double sx, double sy) => new Affine(sx, 0, 0, sy, 0, 0);
        public static Affine Rotate(double r) => new Affine(Math.Cos(r), Math.Sin(r), -Math.Sin(r), Math.Cos(r), 0, 0);

        /// <summary>This transform applied after <paramref name="first"/>.</summary>
        public Affine After(Affine first) => new Affine(
            A * first.A + C * first.B, B * first.A + D * first.B,
            A * first.C + C * first.D, B * first.C + D * first.D,
            A * first.E + C * first.F + E, B * first.E + D * first.F + F);

        public Vec2 Apply(Vec2 p) => new Vec2(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F);
        public Vec2 Apply(double x, double y) => new Vec2(A * x + C * y + E, B * x + D * y + F);

        public Affine Inverse()
        {
            double det = A * D - B * C;
            if (Math.Abs(det) < 1e-300) return Identity;
            double ia = D / det, ib = -B / det, ic = -C / det, id = A / det;
            return new Affine(ia, ib, ic, id, -(ia * E + ic * F), -(ib * E + id * F));
        }

        /// <summary>How much lengths grow (the mean of the two axis scales).</summary>
        public double ScaleFactor => (Math.Sqrt(A * A + B * B) + Math.Sqrt(C * C + D * D)) / 2;
        /// <summary>The angle the x axis is turned to.</summary>
        public double Rotation => Math.Atan2(B, A);
        /// <summary>True when the transform mirrors (negative determinant).</summary>
        public bool Mirrors => A * D - B * C < 0;
    }

    public enum PrimKind { Polyline, Fill, Circle, Text, Node }

    /// <summary>One thing to paint, already in the scene's coordinates.</summary>
    public sealed class Prim
    {
        public PrimKind Kind;
        /// <summary>Polyline/Fill vertices (arcs already tessellated).</summary>
        public List<Vec2> Points = new List<Vec2>();
        public bool Closed;
        public Vec2 Center;
        public double Radius;
        public string Text = "";
        /// <summary>Cap height of text, in scene units.</summary>
        public double Height;
        public double Rotation;
        public double WidthFactor = 1;
        public HAlign H = HAlign.Left;
        public VAlign V = VAlign.Bottom;
        /// <summary>0xRRGGBB.</summary>
        public uint Rgb;
        public string Layer = "0";
        /// <summary>DWG handle of the top-level entity this came from (for selection).</summary>
        public ulong Handle;
        /// <summary>The entity's colour number (1-255, ByLayer/ByBlock resolved) - what a .ctb
        /// plot style table maps by. -1 for a true (RGB) colour; -2 until set.</summary>
        public short Aci = -2;
        /// <summary>The entity's own colour for plotting: <see cref="Rgb"/> without the
        /// on-screen darkening of pale colours (colour 7 still plots black on white paper).</summary>
        public uint PlotRgb;
        /// <summary>Lineweight in mm (ByLayer/ByBlock resolved); -1 = the default lineweight.</summary>
        public double LineWeightMm = -1;
        /// <summary>On a composed plot page: the pen width in paper mm (0 = the device's
        /// thinnest line). -1 on an ordinary scene.</summary>
        public double PenMm = -1;
        public Rect Bounds;

        public void ComputeBounds()
        {
            switch (Kind)
            {
                case PrimKind.Circle:
                    Bounds = new Rect(Center.X - Radius, Center.Y - Radius, Center.X + Radius, Center.Y + Radius);
                    break;
                case PrimKind.Text:
                case PrimKind.Node:
                {
                    double w = Math.Max(Height, 1e-9) * Math.Max(1, Text.Length) * 0.8 * WidthFactor;
                    Bounds = new Rect(Center.X - w, Center.Y - w, Center.X + w, Center.Y + w);
                    break;
                }
                default:
                {
                    double x1 = double.MaxValue, y1 = double.MaxValue, x2 = double.MinValue, y2 = double.MinValue;
                    foreach (var p in Points)
                    {
                        if (p.X < x1) x1 = p.X; if (p.Y < y1) y1 = p.Y;
                        if (p.X > x2) x2 = p.X; if (p.Y > y2) y2 = p.Y;
                    }
                    Bounds = Points.Count == 0 ? default : new Rect(x1, y1, Math.Max(x2, x1 + 1e-9), Math.Max(y2, y1 + 1e-9));
                    break;
                }
            }
        }
    }

    /// <summary>A set of prims sharing a clip rectangle (a layout viewport) or none (model / paper).</summary>
    public sealed class SceneGroup
    {
        public Rect? Clip;
        /// <summary>For a viewport group: paper point back to model coordinates.</summary>
        public Affine? ToModel;
        /// <summary>For a viewport group: model units per paper unit.</summary>
        public double ModelPerPaper = 1;
        public List<Prim> Prims { get; } = new List<Prim>();
    }

    public enum SnapKind { Endpoint, Midpoint, Center, Node, Intersection, Perpendicular, Nearest, Quadrant, Insertion, Tangent }

    /// <summary>Which object snaps are on - the Object Snap toolbar's toggles.</summary>
    [Flags]
    public enum SnapModes
    {
        None = 0,
        Endpoint = 1, Midpoint = 2, Center = 4, Node = 8,
        Intersection = 16, Perpendicular = 32, Nearest = 64, Quadrant = 128,
        /// <summary>Insertion point of a block, text or multiline text.</summary>
        Insertion = 256,
        /// <summary>Tangent to a circle or arc, from the tool's last point.</summary>
        Tangent = 512,
        /// <summary>What FD-Draft snapped to before the modes could be chosen.</summary>
        Default = Endpoint | Midpoint | Center | Node | Intersection,
    }

    public readonly struct SnapPoint
    {
        public readonly Vec2 Point;
        public readonly SnapKind Kind;
        public SnapPoint(Vec2 p, SnapKind k) { Point = p; Kind = k; }
    }

    /// <summary>A painted view of model space or of one layout (paper, in paper units).</summary>
    public sealed class Scene
    {
        public string Name = "Model";
        public bool IsPaper;
        /// <summary>Drawn for a black background (model space on screen).</summary>
        public bool DarkBackground;
        /// <summary>For a layout: the paper sheet, in paper units.</summary>
        public Rect? Paper;
        public List<SceneGroup> Groups { get; } = new List<SceneGroup>();
        public Rect Bounds;
        public List<string> Notes { get; } = new List<string>();

        public IEnumerable<Prim> AllPrims()
        {
            foreach (var g in Groups) foreach (var p in g.Prims) yield return p;
        }

        public void ComputeBounds()
        {
            double x1 = double.MaxValue, y1 = double.MaxValue, x2 = double.MinValue, y2 = double.MinValue;
            bool any = false;
            foreach (var g in Groups)
            {
                foreach (var p in g.Prims)
                {
                    p.ComputeBounds();
                    var b = g.Clip.HasValue ? Intersect(p.Bounds, g.Clip.Value) : p.Bounds;
                    if (b.Width <= 0 && b.Height <= 0 && p.Kind != PrimKind.Node) continue;
                    any = true;
                    x1 = Math.Min(x1, b.X1); y1 = Math.Min(y1, b.Y1); x2 = Math.Max(x2, b.X2); y2 = Math.Max(y2, b.Y2);
                }
            }
            if (Paper.HasValue)
            {
                var pp = Paper.Value; any = true;
                x1 = Math.Min(x1, pp.X1); y1 = Math.Min(y1, pp.Y1); x2 = Math.Max(x2, pp.X2); y2 = Math.Max(y2, pp.Y2);
            }
            Bounds = any ? new Rect(x1, y1, Math.Max(x2, x1 + 1), Math.Max(y2, y1 + 1)) : new Rect(0, 0, 100, 100);
        }

        private static Rect Intersect(Rect a, Rect b)
        {
            double x1 = Math.Max(a.X1, b.X1), y1 = Math.Max(a.Y1, b.Y1), x2 = Math.Min(a.X2, b.X2), y2 = Math.Min(a.Y2, b.Y2);
            if (x2 < x1 || y2 < y1) return default;
            return new Rect(x1, y1, Math.Max(x2, x1 + 1e-9), Math.Max(y2, y1 + 1e-9));
        }

        /// <summary>
        /// The model coordinate under a scene point: itself in model space; through the
        /// viewport it falls in on a layout; the paper point itself on a layout that has no
        /// working viewport at all (the plan is drawn directly in paper space - a real MSCAD
        /// job commonly does this on the sheet it actually used, leaving the rest of the
        /// paper-space viewport machinery as the DWG-mandated background only); otherwise null,
        /// since a pick outside every real viewport on a sheet that does have one is ambiguous.
        /// </summary>
        public Vec2? ModelAt(Vec2 p)
        {
            if (!IsPaper) return p;
            bool anyViewport = false;
            foreach (var g in Groups)
            {
                if (!g.Clip.HasValue || !g.ToModel.HasValue) continue;
                anyViewport = true;
                var c = g.Clip.Value;
                if (p.X >= c.X1 && p.X <= c.X2 && p.Y >= c.Y1 && p.Y <= c.Y2) return g.ToModel.Value.Apply(p);
            }
            return anyViewport ? null : p;
        }

        /// <summary>Snap candidates collected by the builder: vertices, midpoints of straight
        /// segments, arc and circle centres, surveyed points and block insertion points.</summary>
        public List<SnapPoint> Snaps { get; } = new List<SnapPoint>();

        /// <summary>The snap nearest to <paramref name="at"/> within <paramref name="tolerance"/>, nodes first.</summary>
        public SnapPoint? Snap(Vec2 at, double tolerance) => Snap(at, tolerance, SnapModes.Default, null);

        /// <summary>
        /// The snap point nearest <paramref name="at"/> (within <paramref name="tolerance"/>) among
        /// the modes that are on. Endpoints, midpoints, centres and nodes are collected while the
        /// scene is built; intersections, quadrants, perpendiculars (from <paramref name="from"/>,
        /// the tool's last point) and nearest-on-object are worked out here from the linework
        /// under the cursor. A surveyed point beats a line end at the same spot, and "nearest"
        /// only wins when nothing more exact is in reach.
        /// </summary>
        public SnapPoint? Snap(Vec2 at, double tolerance, SnapModes modes, Vec2? from)
        {
            SnapPoint? best = null;
            double bestD = double.MaxValue;
            void Consider(Vec2 p, SnapKind kind)
            {
                double d = Vec2.Distance(at, p);
                if (d > tolerance) return;
                double score = d - (kind == SnapKind.Node ? tolerance * 0.25 : 0) + (kind == SnapKind.Nearest ? tolerance * 2 : 0);
                if (score < bestD) { bestD = score; best = new SnapPoint(p, kind); }
            }
            foreach (var s in Snaps)
            {
                var need = s.Kind switch
                {
                    SnapKind.Endpoint => SnapModes.Endpoint,
                    SnapKind.Midpoint => SnapModes.Midpoint,
                    SnapKind.Center => SnapModes.Center,
                    SnapKind.Insertion => SnapModes.Insertion,
                    _ => SnapModes.Node,
                };
                if ((modes & need) != 0) Consider(s.Point, s.Kind);
            }
            if ((modes & (SnapModes.Intersection | SnapModes.Perpendicular | SnapModes.Nearest | SnapModes.Quadrant | SnapModes.Tangent)) == 0) return best;

            // Linework within reach of the cursor: straight segments and circles.
            var segs = new List<(Vec2 A, Vec2 B)>();
            var circles = new List<(Vec2 C, double R)>();
            var reach = new Rect(at.X - tolerance, at.Y - tolerance, at.X + tolerance, at.Y + tolerance);
            foreach (var g in Groups)
            {
                if (g.Clip.HasValue)
                {
                    var c = g.Clip.Value;
                    if (at.X < c.X1 || at.X > c.X2 || at.Y < c.Y1 || at.Y > c.Y2) continue;
                }
                foreach (var p in g.Prims)
                {
                    if (!(p.Bounds.X1 <= reach.X2 && reach.X1 <= p.Bounds.X2 && p.Bounds.Y1 <= reach.Y2 && reach.Y1 <= p.Bounds.Y2)) continue;
                    if (p.Kind == PrimKind.Circle) { circles.Add((p.Center, p.Radius)); continue; }
                    if (p.Kind != PrimKind.Polyline && p.Kind != PrimKind.Fill) continue;
                    int n = p.Points.Count, count = p.Closed || p.Kind == PrimKind.Fill ? n : n - 1;
                    for (int i = 0; i < count; i++)
                    {
                        var a = p.Points[i]; var b = p.Points[(i + 1) % n];
                        if (Construct.DistanceToSegment(at, a, b, out _) <= tolerance) segs.Add((a, b));
                    }
                }
            }
            if ((modes & SnapModes.Quadrant) != 0)
                foreach (var (c, r) in circles)
                    foreach (var q in new[] { new Vec2(c.X + r, c.Y), new Vec2(c.X, c.Y + r), new Vec2(c.X - r, c.Y), new Vec2(c.X, c.Y - r) })
                        Consider(q, SnapKind.Quadrant);
            if ((modes & SnapModes.Intersection) != 0)
            {
                for (int i = 0; i < segs.Count; i++)
                {
                    for (int j = i + 1; j < segs.Count; j++)
                    {
                        var (a, b) = segs[i]; var (c, d) = segs[j];
                        // Consecutive pieces of one line meet at their shared end - that's an endpoint, not a crossing.
                        if (Vec2.Distance(b, c) < 1e-12 || Vec2.Distance(a, d) < 1e-12 || Vec2.Distance(a, c) < 1e-12 || Vec2.Distance(b, d) < 1e-12) continue;
                        var x = Construct.LineLine(a, b - a, c, d - c);
                        if (x.HasValue && Construct.DistanceToSegment(x.Value, a, b, out _) < 1e-9 * Math.Max(1, (b - a).Length) + 1e-12
                            && Construct.DistanceToSegment(x.Value, c, d, out _) < 1e-9 * Math.Max(1, (d - c).Length) + 1e-12)
                            Consider(x.Value, SnapKind.Intersection);
                    }
                    foreach (var (cc, r) in circles)
                        foreach (var x in Construct.LineCircle(segs[i].A, segs[i].B - segs[i].A, cc, r))
                            if (Construct.DistanceToSegment(x, segs[i].A, segs[i].B, out _) < 1e-9 * Math.Max(1, r)) Consider(x, SnapKind.Intersection);
                }
            }
            if ((modes & SnapModes.Perpendicular) != 0 && from.HasValue)
                foreach (var (a, b) in segs)
                {
                    var d = b - a;
                    double len2 = Vec2.Dot(d, d);
                    if (len2 < 1e-24) continue;
                    double t = Vec2.Dot(from.Value - a, d) / len2;
                    if (t >= -1e-9 && t <= 1 + 1e-9) Consider(a + d * t, SnapKind.Perpendicular);
                }
            if ((modes & SnapModes.Tangent) != 0 && from.HasValue)
            {
                // Exact for circles: the two points where a line from "from" just touches.
                foreach (var (c, r) in circles)
                {
                    double d = Vec2.Distance(from.Value, c);
                    if (d <= r + 1e-12) continue;
                    double ang = Math.Atan2(from.Value.Y - c.Y, from.Value.X - c.X), half = Math.Acos(r / d);
                    Consider(new Vec2(c.X + r * Math.Cos(ang + half), c.Y + r * Math.Sin(ang + half)), SnapKind.Tangent);
                    Consider(new Vec2(c.X + r * Math.Cos(ang - half), c.Y + r * Math.Sin(ang - half)), SnapKind.Tangent);
                }
                // Arcs are drawn as short chords: the tangent point is the vertex where the
                // sight line from "from" stops crossing in to the curve and starts crossing out.
                for (int i = 0; i < segs.Count; i++)
                    for (int j = 0; j < segs.Count; j++)
                    {
                        if (i == j || Vec2.Distance(segs[i].B, segs[j].A) > 1e-12) continue;
                        var v = segs[i].B;
                        var d1 = segs[i].B - segs[i].A; var d2 = segs[j].B - segs[j].A;
                        // A corner of straight lines isn't a curve.
                        double turn = Math.Abs(Vec2.Cross(d1.Normalized(), d2.Normalized()));
                        if (turn < 1e-6 || turn > 0.5) continue;
                        double c1 = Vec2.Cross(d1, v - from.Value), c2 = Vec2.Cross(d2, v - from.Value);
                        if (Math.Sign(c1) != Math.Sign(c2)) Consider(v, SnapKind.Tangent);
                    }
            }
            if ((modes & SnapModes.Nearest) != 0)
            {
                foreach (var (a, b) in segs) { Construct.DistanceToSegment(at, a, b, out var q); Consider(q, SnapKind.Nearest); }
                foreach (var (c, r) in circles)
                {
                    var dir = (at - c).Normalized();
                    if (dir.Length > 0) Consider(c + dir * r, SnapKind.Nearest);
                }
            }
            return best;
        }
    }

    /// <summary>Bearing and distance between two picked points, surveyor's way round (N, E).</summary>
    public sealed class InverseResult
    {
        public Vec2 From, To;
        public double Distance, DeltaN, DeltaE;
        public string Bearing = "";

        public static InverseResult Between(Vec2 from, Vec2 to, double bearingRotationDeg = 0)
        {
            return new InverseResult
            {
                From = from, To = to,
                Distance = Vec2.Distance(from, to),
                DeltaN = to.Y - from.Y, DeltaE = to.X - from.X,
                Bearing = Angles.FormatBearing(Angles.Azimuth(from, to) + bearingRotationDeg * Math.PI / 180),
            };
        }

        public override string ToString() =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}  {1:F3}   dN {2:F3}  dE {3:F3}", Bearing, Distance, DeltaN, DeltaE);
    }
}
