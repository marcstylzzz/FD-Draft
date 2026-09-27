using System;
using System.Collections.Generic;
using ACadSharp.Tables;

namespace FdDraft.View
{
    /// <summary>
    /// How wide text in an AutoCAD SHX font (msurvey, romans, simplex, a Leroy font...) comes out,
    /// so FD-Draft - which draws every font as Arial - puts words where AutoCAD and MSCAD do. SHX
    /// letters are a little wider than Arial's and their spaces over twice as wide: drawn as plain
    /// Arial, a note indented with spaces ran into the heading word before it, and MTEXT wrapped
    /// words later than MSCAD. Letter advances are the Hershey simplex font's (the family these
    /// fonts come from), scaled and with the space width measured off Marc's MSCAD screen of the
    /// firm's msurvey.shx notes (DISTANCES / HEREON ... at 2.5 and 2.0 mm).
    /// SceneBuilder draws SHX text a word at a time, each word stretched to its SHX width and
    /// placed where these metrics put it.
    /// </summary>
    public static class ShxMetrics
    {
        /// <summary>Letter widths relative to Hershey simplex.</summary>
        public const double LetterScale = 1.02;
        /// <summary>A space, in cap heights.</summary>
        public const double Space = 0.935;

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

        /// <summary>One character's advance, in cap heights.</summary>
        public static double CharWidth(char ch)
        {
            if (ch == ' ' || ch == '\u00A0') return Space;
            int units = ch >= 32 && ch <= 126 ? Advance[ch - 32] : ch == '°' ? 14 : ch == '±' ? 26 : ch == 'Ø' ? 22 : 20;
            return units / 21.0 * LetterScale;
        }

        /// <summary>Width of <paramref name="text"/> in an SHX font at cap height <paramref name="capHeight"/>.</summary>
        public static double Width(string text, double capHeight)
        {
            double w = 0;
            foreach (char ch in text) w += CharWidth(ch);
            return w * capHeight;
        }

        /// <summary>The words of a line with where each starts and how wide it is, in cap heights.</summary>
        public static List<(string Word, double X, double W)> Words(string text)
        {
            var result = new List<(string, double, double)>();
            double x = 0; int i = 0;
            while (i < text.Length)
            {
                if (text[i] == ' ' || text[i] == '\u00A0') { x += Space; i++; continue; }
                int j = i; double w = 0;
                while (j < text.Length && text[j] != ' ' && text[j] != '\u00A0') { w += CharWidth(text[j]); j++; }
                result.Add((text.Substring(i, j - i), x, w));
                x += w; i = j;
            }
            return result;
        }
    }
}
