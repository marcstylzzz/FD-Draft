using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ACadSharp.Tables;

namespace FdDraft.View
{
    /// <summary>
    /// TrueType faces for text: which face a style or an MTEXT \f code names, and how wide text
    /// in it runs. The app plugs in a measurer that asks Windows for the real face
    /// (<see cref="Measurer"/>); without one (tests, a face that isn't installed) Arial's metrics stand in.
    /// A company name set in Broadway stays Broadway - on screen and on the plotter.
    /// </summary>
    public static class TextFonts
    {
        /// <summary>(face, text, cap height) -> width, or null when the face isn't available.</summary>
        public static Func<string, string, double, double?>? Measurer;

        public static double Width(string? font, string text, double capHeight)
        {
            if (font != null && Measurer != null && Measurer(font, text, capHeight) is double w) return w;
            return PdfSceneWriter.MeasureText(text, capHeight);
        }

        private static readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["arial"] = "Arial", ["arialn"] = "Arial Narrow", ["arialbd"] = "Arial", ["broadw"] = "Broadway", ["times"] = "Times New Roman",
            ["calibri"] = "Calibri", ["verdana"] = "Verdana", ["tahoma"] = "Tahoma", ["cour"] = "Courier New", ["segoeui"] = "Segoe UI",
            ["gothic"] = "Century Gothic", ["romantic"] = "Romantic", ["swissek"] = "Swis721 Ex BT", ["swiss"] = "Swis721 BT",
            ["isocpeur"] = "ISOCPEUR", ["isocp"] = "ISOCP", ["simplex"] = "Simplex", ["romans"] = "RomanS", ["txt"] = "Txt",
        };

        /// <summary>A face name from an \f code or a font file name ("BROADW.TTF" -> "Broadway").</summary>
        public static string Family(string nameOrFile)
        {
            string stem = Path.GetFileNameWithoutExtension(nameOrFile.Trim());
            return Files.TryGetValue(stem, out var fam) ? fam : nameOrFile.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ? stem : nameOrFile.Trim();
        }

        /// <summary>The TrueType face a style draws with, or null for an SHX style (or plain Arial).</summary>
        public static string? Of(TextStyle? style)
        {
            if (style == null || ShxMetrics.IsShx(style)) return null;
            // AutoCAD keeps the face name in the style's ACAD extended data.
            foreach (var kv in style.ExtendedData)
                foreach (var r in kv.Value.Records)
                    if (r is ACadSharp.XData.ExtendedDataString s && !string.IsNullOrWhiteSpace(s.Value)) return s.Value.Trim();
            string fam = Family(style.Filename);
            return fam.Equals("Arial", StringComparison.OrdinalIgnoreCase) ? null : fam;
        }
    }
}
