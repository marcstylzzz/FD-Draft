using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FdDraft.View
{
    /// <summary>
    /// MTEXT with its inline formatting honoured: {groups}, \H height (absolute or 0.75x),
    /// \f / \F font (TrueType or SHX - which sets how wide the words run), \C colour, \W width,
    /// \pxq paragraph alignment, \P paragraph breaks, \S stacks, \~ and the escapes. Before, the
    /// codes were stripped and every run drawn at the entity's height: a title block's
    /// "{\H0.3333x;TOPOGRAPHIC SURVEYS/...}" came out three times too big over its neighbours.
    /// </summary>
    public static class MTextLayout
    {
        public sealed class Run
        {
            public string Text = "";
            public double Height;
            public bool Shx;
            /// <summary>The TrueType face (\f or the style's), null for Arial / SHX.</summary>
            public string? Font;
            public double Width = 1;
            /// <summary>An inline colour: index (1-255) or -1 for <see cref="TrueColor"/>; 0 = the entity's.</summary>
            public int Aci;
            public uint TrueColor;
        }

        public sealed class Paragraph
        {
            public List<Run> Runs = new List<Run>();
            /// <summary>'l', 'c', 'r' from \pxq, or '\0' for the attachment's.</summary>
            public char Align;
        }

        private struct State
        {
            public double Height; public bool Shx; public string? Font; public double Width; public int Aci; public uint TrueColor;
        }

        public static List<Paragraph> Parse(string value, double height, bool shx, string? font = null)
        {
            var paras = new List<Paragraph> { new Paragraph() };
            var stack = new Stack<State>();
            var st = new State { Height = height, Shx = shx, Font = shx ? null : font, Width = 1 };
            var sb = new StringBuilder();

            void Flush()
            {
                if (sb.Length == 0) return;
                paras[paras.Count - 1].Runs.Add(new Run { Text = SceneBuilder.Decode(sb.ToString()), Height = st.Height, Shx = st.Shx, Font = st.Font, Width = st.Width, Aci = st.Aci, TrueColor = st.TrueColor });
                sb.Clear();
            }
            string Arg(ref int i)
            {
                int end = value.IndexOf(';', i);
                if (end < 0) end = value.Length;
                string a = value.Substring(i, end - i);
                i = end + 1;
                return a;
            }

            int k = 0;
            while (k < value.Length)
            {
                char ch = value[k];
                if (ch == '{') { Flush(); stack.Push(st); k++; continue; }
                if (ch == '}') { Flush(); if (stack.Count > 0) st = stack.Pop(); k++; continue; }
                if (ch != '\\' || k + 1 >= value.Length) { sb.Append(ch); k++; continue; }
                char code = value[k + 1];
                k += 2;
                switch (code)
                {
                    case 'P': Flush(); paras.Add(new Paragraph()); break;
                    case '~': sb.Append(' '); break;
                    case '\\': case '{': case '}': sb.Append(code); break;
                    case 'H':
                    {
                        Flush();
                        string a = Arg(ref k).Trim();
                        bool rel = a.EndsWith("x", StringComparison.OrdinalIgnoreCase);
                        if (double.TryParse(rel ? a.Substring(0, a.Length - 1) : a, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0)
                            st.Height = rel ? st.Height * v : v;
                        break;
                    }
                    case 'W':
                    {
                        Flush();
                        string a = Arg(ref k).Trim().TrimEnd('x', 'X');
                        if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0) st.Width = v;
                        break;
                    }
                    case 'f': case 'F':
                    {
                        Flush();
                        string a = Arg(ref k);
                        st.Shx = a.IndexOf(".shx", StringComparison.OrdinalIgnoreCase) >= 0;
                        int bar = a.IndexOf('|');
                        string face = (bar >= 0 ? a.Substring(0, bar) : a).Trim();
                        st.Font = st.Shx || face.Length == 0 ? null : TextFonts.Family(face);
                        break;
                    }
                    case 'C':
                    {
                        Flush();
                        if (int.TryParse(Arg(ref k), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c)) st.Aci = c >= 1 && c <= 255 ? c : 0;
                        break;
                    }
                    case 'c':
                    {
                        Flush();
                        if (uint.TryParse(Arg(ref k), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint c)) { st.Aci = -1; st.TrueColor = c & 0xFFFFFF; }
                        break;
                    }
                    case 'p':
                    {
                        Flush();
                        string a = Arg(ref k);
                        int q = a.IndexOf('q');
                        if (q >= 0 && q + 1 < a.Length && "lcrjd".IndexOf(a[q + 1]) >= 0)
                            paras[paras.Count - 1].Align = a[q + 1] == 'c' ? 'c' : a[q + 1] == 'r' ? 'r' : 'l';
                        break;
                    }
                    case 'S':
                    {
                        string a = Arg(ref k);
                        sb.Append(a.Replace('^', '/').Replace('#', '/'));
                        break;
                    }
                    case 'U':
                        if (k < value.Length && value[k] == '+' && k + 5 <= value.Length
                            && int.TryParse(value.Substring(k + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int u))
                        { sb.Append((char)u); k += 5; }
                        break;
                    case 'A': case 'T': case 'Q':
                        Arg(ref k); break;
                    case 'L': case 'l': case 'O': case 'o': case 'K': case 'k': case 'N': case 'n':
                        break;
                    default:
                        sb.Append(code); break;
                }
            }
            Flush();
            return paras;
        }

        /// <summary>A word (or a run of spaces) with the run it belongs to.</summary>
        public readonly struct Piece
        {
            public readonly string Text; public readonly Run Run; public readonly bool Space;
            public Piece(string text, Run run, bool space) { Text = text; Run = run; Space = space; }
            public bool Tab => Text == "\t";
            public double Width => Tab ? 0 : Measure(Text, Run);
        }

        public static double Measure(string text, Run run) =>
            (run.Shx ? ShxMetrics.Width(text, run.Height) : TextFonts.Width(run.Font, text, run.Height)) * run.Width;

        /// <summary>Tab stops, when a paragraph sets none: measured off MSCAD's legend (two tabs
        /// indent a continuation line under the column the first line's words start at).</summary>
        public static double TabWidth(double height) => 4.78 * height;

        /// <summary>Where a tab at <paramref name="x"/> (from the line's start) takes the text.</summary>
        public static double AfterTab(double x, double height)
        {
            double tab = TabWidth(height);
            return (Math.Floor(x / tab + 1e-9) + 1) * tab;
        }

        /// <summary>A paragraph's pieces, words and spaces apart, in order.</summary>
        public static List<Piece> Pieces(Paragraph p)
        {
            var list = new List<Piece>();
            foreach (var r in p.Runs)
            {
                int i = 0;
                while (i < r.Text.Length)
                {
                    if (r.Text[i] == '\t') { list.Add(new Piece("\t", r, true)); i++; continue; }
                    bool sp = r.Text[i] == ' ';
                    int j = i;
                    while (j < r.Text.Length && r.Text[j] != '\t' && (r.Text[j] == ' ') == sp) j++;
                    list.Add(new Piece(r.Text.Substring(i, j - i), r, sp));
                    i = j;
                }
            }
            return list;
        }

        /// <summary>Breaks a paragraph into lines no wider than <paramref name="width"/> (0 = no
        /// wrapping). Leading spaces - hanging indents - stay; a word too long for a line keeps one.</summary>
        public static List<List<Piece>> Wrap(Paragraph p, double width)
        {
            var lines = new List<List<Piece>>();
            var line = new List<Piece>();
            double w = 0;
            bool hasWord = false;
            foreach (var piece in Pieces(p))
            {
                double pw = piece.Width;
                // A line may run a letter's side bearing past the box: AutoCAD measures to the ink.
                if (!piece.Space && hasWord && width > 0 && w + pw > width + 0.2 * piece.Run.Height)
                {
                    while (line.Count > 0 && line[line.Count - 1].Space) line.RemoveAt(line.Count - 1);
                    lines.Add(line);
                    line = new List<Piece>(); w = 0; hasWord = false;
                }
                if (piece.Space && line.Count == 0 && lines.Count > 0) continue; // a wrapped line doesn't start with the space it broke at
                line.Add(piece); w = piece.Tab ? AfterTab(w, piece.Run.Height) : w + pw;
                if (!piece.Space) hasWord = true;
            }
            while (line.Count > 0 && line[line.Count - 1].Space && line.Any(x => !x.Space)) line.RemoveAt(line.Count - 1);
            lines.Add(line);
            return lines;
        }
    }
}
