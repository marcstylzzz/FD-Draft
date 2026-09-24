using System;
using System.Collections.Generic;
using FdDraft.Core.Geometry;

namespace FdDraft.Core.Drafting
{
    /// <summary>One primitive of a point symbol, in a unit box (symbol size 1 = 1 paper mm across once scaled).</summary>
    public sealed class ShapePart
    {
        public List<Vec2> Points { get; } = new List<Vec2>();
        public bool Closed { get; set; }
        public bool Filled { get; set; }
        /// <summary>When > 0 this part is a circle of this radius at Points[0].</summary>
        public double CircleRadius { get; set; }
    }

    /// <summary>
    /// Fallback drawings of FD-Pro's point symbols, for codes the firm's template
    /// has no block for. Names match FD-Pro's PointSymbol enum.
    /// </summary>
    public static class SymbolShapes
    {
        public static List<ShapePart> Unit(string symbol)
        {
            var parts = new List<ShapePart>();
            string s = (symbol ?? "DOT").ToUpperInvariant();
            bool filled = s.EndsWith("_FILLED", StringComparison.Ordinal);
            bool dot = s.EndsWith("_DOT", StringComparison.Ordinal);
            bool plus = s.EndsWith("_PLUS", StringComparison.Ordinal);
            bool x = s.EndsWith("_X", StringComparison.Ordinal);
            const double h = 0.5;

            if (s.StartsWith("CIRCLE", StringComparison.Ordinal)) parts.Add(Circle(h, filled));
            else if (s.StartsWith("SQUARE", StringComparison.Ordinal)) parts.Add(Poly(filled, P(-h, -h), P(h, -h), P(h, h), P(-h, h)));
            else if (s.StartsWith("DIAMOND", StringComparison.Ordinal)) parts.Add(Poly(filled, P(0, -h), P(h, 0), P(0, h), P(-h, 0)));
            else if (s.StartsWith("TRIANGLE_RIGHT", StringComparison.Ordinal)) parts.Add(Poly(filled, P(-h, -h), P(h, 0), P(-h, h)));
            else if (s.StartsWith("TRIANGLE_LEFT", StringComparison.Ordinal)) parts.Add(Poly(filled, P(h, -h), P(h, h), P(-h, 0)));
            else if (s.StartsWith("TRIANGLE_DOWN", StringComparison.Ordinal)) parts.Add(Poly(filled, P(-h, h), P(h, h), P(0, -h)));
            else if (s.StartsWith("TRIANGLE", StringComparison.Ordinal)) parts.Add(Poly(filled, P(-h, -h), P(h, -h), P(0, h)));
            else if (s == "X") { parts.Add(Line(P(-h, -h), P(h, h))); parts.Add(Line(P(-h, h), P(h, -h))); }
            else if (s == "PLUS") { parts.Add(Line(P(-h, 0), P(h, 0))); parts.Add(Line(P(0, -h), P(0, h))); }
            else if (s == "ASTERISK")
            {
                parts.Add(Line(P(-h, 0), P(h, 0))); parts.Add(Line(P(0, -h), P(0, h)));
                double d = h * 0.7071;
                parts.Add(Line(P(-d, -d), P(d, d))); parts.Add(Line(P(-d, d), P(d, -d)));
            }
            else parts.Add(Circle(h * 0.35, true)); // DOT and anything unknown

            if (dot) parts.Add(Circle(h * 0.2, true));
            if (plus) { parts.Add(Line(P(-h, 0), P(h, 0))); parts.Add(Line(P(0, -h), P(0, h))); }
            if (x && s != "X") { double d = h * 0.7071; parts.Add(Line(P(-d, -d), P(d, d))); parts.Add(Line(P(-d, d), P(d, -d))); }
            if (s == "CIRCLE_TARGET") parts.Add(Circle(h * 0.5, false));
            return parts;
        }

        private static Vec2 P(double x, double y) => new Vec2(x, y);

        private static ShapePart Circle(double r, bool filled)
        {
            var part = new ShapePart { CircleRadius = r, Filled = filled, Closed = true };
            part.Points.Add(P(0, 0));
            return part;
        }

        private static ShapePart Poly(bool filled, params Vec2[] pts)
        {
            var part = new ShapePart { Closed = true, Filled = filled };
            part.Points.AddRange(pts);
            return part;
        }

        private static ShapePart Line(Vec2 a, Vec2 b)
        {
            var part = new ShapePart();
            part.Points.Add(a);
            part.Points.Add(b);
            return part;
        }
    }
}
