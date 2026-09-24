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

    public enum SnapKind { Endpoint, Midpoint, Center, Node }

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
        /// viewport it falls in on a layout; null on bare paper.
        /// </summary>
        public Vec2? ModelAt(Vec2 p)
        {
            if (!IsPaper) return p;
            foreach (var g in Groups)
            {
                if (!g.Clip.HasValue || !g.ToModel.HasValue) continue;
                var c = g.Clip.Value;
                if (p.X >= c.X1 && p.X <= c.X2 && p.Y >= c.Y1 && p.Y <= c.Y2) return g.ToModel.Value.Apply(p);
            }
            return null;
        }

        /// <summary>Snap candidates collected by the builder: vertices, midpoints of straight
        /// segments, arc and circle centres, surveyed points and block insertion points.</summary>
        public List<SnapPoint> Snaps { get; } = new List<SnapPoint>();

        /// <summary>The snap nearest to <paramref name="at"/> within <paramref name="tolerance"/>, nodes first.</summary>
        public SnapPoint? Snap(Vec2 at, double tolerance)
        {
            SnapPoint? best = null;
            double bestD = double.MaxValue;
            foreach (var s in Snaps)
            {
                double d = Vec2.Distance(at, s.Point);
                if (d > tolerance) continue;
                // A surveyed point beats a line end at the same spot.
                double score = d - (s.Kind == SnapKind.Node ? tolerance * 0.25 : 0);
                if (score < bestD) { bestD = score; best = s; }
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
