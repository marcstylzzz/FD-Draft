using System;
using System.Globalization;
using FdDraft.Core.Drafting;

namespace FdDraft.Core.Geometry
{
    /// <summary>MSCAD's Main Control calculator, FD-Draft's CAL command.</summary>
    public static class Calculator
    {
        /// <summary>
        /// The calculator: + - * / ^ and brackets, sqrt sin cos tan (degrees), pi, and d.mmss
        /// angles via dms(45.3015). Reports what's wrong rather than throwing.
        /// </summary>
        public static string Evaluate(string expr)
        {
            try
            {
                var p = new Parser(expr);
                double v = p.Parse();
                return double.IsNaN(v) || double.IsInfinity(v) ? "undefined" : v.ToString("0.##########", CultureInfo.InvariantCulture) + (Math.Abs(v) < 360 && expr.Contains("dms", StringComparison.OrdinalIgnoreCase) ? "   (" + SurveyLabels.Dms(v * Math.PI / 180) + ")" : "");
            }
            catch (FormatException e) { return "? " + e.Message; }
        }

        /// <summary>A small recursive-descent calculator (no eval, no surprises).</summary>
        private sealed class Parser
        {
            private readonly string _s; private int _i;
            public Parser(string s) { _s = s; }
            public double Parse() { double v = Sum(); Skip(); if (_i < _s.Length) throw new FormatException("unexpected '" + _s[_i] + "'"); return v; }
            private void Skip() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
            private bool Eat(char c) { Skip(); if (_i < _s.Length && _s[_i] == c) { _i++; return true; } return false; }
            private double Sum() { double v = Product(); while (true) { if (Eat('+')) v += Product(); else if (Eat('-')) v -= Product(); else return v; } }
            private double Product() { double v = Power(); while (true) { if (Eat('*')) v *= Power(); else if (Eat('/')) v /= Power(); else return v; } }
            private double Power() { double v = Unary(); return Eat('^') ? Math.Pow(v, Power()) : v; }
            private double Unary() { if (Eat('-')) return -Unary(); if (Eat('+')) return Unary(); return Atom(); }
            private double Atom()
            {
                Skip();
                if (Eat('(')) { double v = Sum(); if (!Eat(')')) throw new FormatException("missing )"); return v; }
                int start = _i;
                if (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.'))
                {
                    while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E')) _i++;
                    if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) throw new FormatException("bad number");
                    return n;
                }
                while (_i < _s.Length && char.IsLetter(_s[_i])) _i++;
                string name = _s.Substring(start, _i - start).ToLowerInvariant();
                if (name == "pi") return Math.PI;
                if (name.Length == 0) throw new FormatException(_i < _s.Length ? "unexpected '" + _s[_i] + "'" : "expression ends too soon");
                if (!Eat('(')) throw new FormatException(name + " needs ( )");
                double a = Sum();
                if (!Eat(')')) throw new FormatException("missing )");
                const double D = Math.PI / 180;
                switch (name)
                {
                    case "sqrt": return Math.Sqrt(a);
                    case "sin": return Math.Sin(a * D);
                    case "cos": return Math.Cos(a * D);
                    case "tan": return Math.Tan(a * D);
                    case "asin": return Math.Asin(a) / D;
                    case "acos": return Math.Acos(a) / D;
                    case "atan": return Math.Atan(a) / D;
                    case "abs": return Math.Abs(a);
                    case "dms":
                    {
                        // d.mmss -> decimal degrees, the way a survey calculator keys angles.
                        double sign = Math.Sign(a); a = Math.Abs(a);
                        double deg = Math.Floor(a), mm = Math.Floor((a - deg) * 100 + 1e-9), ss = ((a - deg) * 100 - mm) * 100;
                        return sign * (deg + mm / 60 + ss / 3600);
                    }
                    default: throw new FormatException("unknown function " + name);
                }
            }
        }
    }
}
