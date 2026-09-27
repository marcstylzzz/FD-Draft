using System;
using ACadSharp.Tables;

namespace FdDraft.View
{
    /// <summary>
    /// How wide text in an AutoCAD SHX font (romans, simplex, txt, a Leroy font...) comes out, so
    /// FD-Draft - which draws every font as Arial - puts words where AutoCAD does. SHX fonts are
    /// wider than Arial, spaces most of all (about three quarters of the cap height against
    /// Arial's two fifths): drawn as Arial, an indented note ran into the heading word before it,
    /// and MTEXT wrapped a word or two later than AutoCAD. Widths are the Hershey simplex font's,
    /// which romans/simplex are built from, in units where the cap height is 21.
    /// </summary>
    public static class ShxMetrics
    {
        /// <summary>The space drawn for an SHX space: Arial's en space (half an em).</summary>
        public const char WideSpace = ' ';

        private static readonly int[] Advance =
        {
            // 32 space ! " # $ % & ' ( ) * + , - . /
            16, 10, 16, 21, 20, 24, 26, 10, 14, 14, 16, 26, 10, 26, 10, 22,
            // 0-9
            20, 20, 20, 20, 20, 20, 20, 20, 20, 20,
            // : ; < = > ? @
            10, 10, 24, 26, 24, 18, 27,
            // A-Z
            18, 21, 21, 21, 19, 18, 21, 22, 8, 16, 21, 17, 24, 22, 22, 21, 22, 21, 20, 16, 22, 18, 24, 20, 18, 20,
            // [ \ ] ^ _ `
            14, 14, 14, 16, 16, 10,
            // a-z
            19, 19, 18, 19, 18, 12, 19, 19, 8, 10, 17, 8, 30, 19, 19, 19, 19, 13, 17, 12, 19, 16, 22, 17, 16, 17,
            // { | } ~
            14, 8, 14, 24,
        };

        /// <summary>True when the style draws with an SHX (shape) font rather than a TrueType one.
        /// A style with no font file is txt.shx.</summary>
        public static bool IsShx(TextStyle? style)
        {
            if (style == null) return true;
            string f = (style.Filename ?? "").Trim();
            if (f.Length == 0) return true;
            return !(f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Width of <paramref name="text"/> in an SHX font at cap height <paramref name="capHeight"/>.</summary>
        public static double Width(string text, double capHeight)
        {
            double units = 0;
            foreach (char ch in text)
            {
                if (ch == WideSpace) units += Advance[0];
                else if (ch >= 32 && ch <= 126) units += Advance[ch - 32];
                else if (ch == '°') units += 14;
                else if (ch == '±') units += 26;
                else if (ch == 'Ø') units += 22;
                else units += 20;
            }
            return units / 21.0 * capHeight;
        }

        /// <summary>The text as drawn for a prim with <see cref="Prim.WideSpaces"/>: each space an en space.</summary>
        public static string Spaced(string text) => text.Replace(' ', WideSpace);

        /// <summary>The horizontal stretch that makes Arial with wide spaces as long as the SHX
        /// text, kept within reason.</summary>
        public static double Stretch(string text)
        {
            double arial = PdfSceneWriter.MeasureText(text, 1, wideSpaces: true);
            if (arial < 1e-9) return 1;
            return Math.Max(0.6, Math.Min(1.8, Width(text, 1) / arial));
        }
    }
}
