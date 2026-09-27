using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using FdDraft.Core.Layout;
using FdDraft.Core.Standards;

namespace FdDraft.Cad
{
    /// <summary>
    /// Reads a firm's .dwt and describes what FD-Draft needs from it: each paper-space
    /// layout's paper size, its drawing frame (the largest rectangle), the areas taken
    /// by the title block and notes, and the free rectangles left for the plan. Prints
    /// a starter [sheet.*] block for the firm's standards file, which a drafter then
    /// checks once - this is how a new firm's template gets set up.
    /// </summary>
    public static class TemplateInspector
    {
        public sealed class SheetGuess
        {
            public string Layout = "";
            public double PaperWidth, PaperHeight;
            public Rect Frame;
            public List<Rect> Keepouts = new List<Rect>();
            public bool FrameFound;
        }

        public static List<SheetGuess> Guess(CadDocument doc)
        {
            var list = new List<SheetGuess>();
            foreach (var layout in doc.Layouts.Where(l => l.IsPaperSpace).OrderBy(l => l.TabOrder))
            {
                var g = new SheetGuess { Layout = layout.Name };
                bool rotated = layout.PaperRotation == PlotRotation.Degrees90 || layout.PaperRotation == PlotRotation.Degrees270;
                g.PaperWidth = rotated ? layout.PaperHeight : layout.PaperWidth;
                g.PaperHeight = rotated ? layout.PaperWidth : layout.PaperHeight;

                var ents = layout.AssociatedBlock.Entities.Where(e => !(e is Viewport)).ToList();
                var boxes = new List<(Entity e, Rect r)>();
                foreach (var e in ents)
                {
                    try
                    {
                        var b = e is Spline sp && FdDraft.View.SplinePoints.Of(sp) is var sps && sps.Count >= 2
                            ? CSMath.BoundingBox.FromPoints(sps) : e.GetBoundingBox();
                        if (double.IsInfinity(b.Min.X) || double.IsNaN(b.Min.X) || double.IsInfinity(b.Max.X)) continue;
                        // A box with no size (an empty text, a stray point) says nothing about space taken.
                        if (b.Max.X - b.Min.X < 0.1 && b.Max.Y - b.Min.Y < 0.1) continue;
                        boxes.Add((e, new Rect(b.Min.X, b.Min.Y, Math.Max(b.Max.X, b.Min.X + 0.01), Math.Max(b.Max.Y, b.Min.Y + 0.01))));
                    }
                    catch (Exception) { /* entity types without a usable box are ignored */ }
                }

                // Frame: the largest polyline rectangle on the sheet.
                var frame = boxes.Where(x => x.e is LwPolyline pl && pl.Vertices.Count >= 4).OrderByDescending(x => x.r.Area).FirstOrDefault();
                if (frame.e == null) { list.Add(g); continue; }
                g.Frame = frame.r;
                g.FrameFound = true;

                // Everything inside the frame, grouped into clusters (5 mm apart or closer).
                var inside = boxes.Where(x => x.e != frame.e && Inside(x.r, frame.r)).Select(x => x.r).ToList();
                var clusters = Cluster(inside, 5.0);
                // A cluster touching the frame's right edge is the title column: it takes the full height.
                foreach (var c in clusters)
                {
                    var k = c;
                    if (frame.r.X2 - c.X2 < 3) k = new Rect(c.X1, frame.r.Y1, frame.r.X2, frame.r.Y2);
                    g.Keepouts.Add(k);
                }
                g.Keepouts = Cluster(g.Keepouts, 0.0);
                list.Add(g);
            }
            return list;
        }

        public static string Describe(string templatePath)
        {
            var doc = DwgReader.Read(templatePath, (s, e) => { });
            var sb = new StringBuilder();
            string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
            sb.AppendLine("Template:  " + templatePath);
            sb.AppendLine("Version:   " + doc.Header.Version + ", units " + doc.Header.InsUnits);
            sb.AppendLine("Tables:    " + doc.Layers.Count + " layers, " + doc.BlockRecords.Count(b => !b.Name.StartsWith("*")) + " blocks, " + doc.TextStyles.Count + " text styles, " + doc.LineTypes.Count + " linetypes");
            sb.AppendLine("Model:     " + doc.ModelSpace.Entities.Count() + " entities in model space (sample content? see clear_template_model_space)");
            sb.AppendLine();
            sb.AppendLine("Blocks:    " + string.Join(", ", doc.BlockRecords.Where(b => !b.Name.StartsWith("*")).Select(b => b.Name)));
            sb.AppendLine();

            var guesses = Guess(doc);
            foreach (var g in guesses)
            {
                sb.Append("Layout ").Append(g.Layout).Append(": paper ").Append(F(g.PaperWidth)).Append(" x ").Append(F(g.PaperHeight)).Append(" mm");
                if (!g.FrameFound) { sb.AppendLine(" - no frame found; measure it by hand"); continue; }
                sb.AppendLine(", frame " + g.Frame);
                foreach (var k in g.Keepouts) sb.AppendLine("    taken    " + k);
                foreach (var r in SheetPicker.FreeRects(g.Frame, g.Keepouts).OrderByDescending(r => r.Area).Take(3))
                    sb.AppendLine("    free     " + r);
            }

            sb.AppendLine();
            sb.AppendLine("; ---- starter sheet definitions - check each against the template before use");
            foreach (var g in guesses.Where(x => x.FrameFound))
            {
                sb.AppendLine("[sheet." + g.Layout + "]");
                sb.AppendLine("paper = " + F(g.PaperWidth) + " x " + F(g.PaperHeight));
                sb.AppendLine("frame = " + R(g.Frame));
                for (int i = 0; i < g.Keepouts.Count; i++)
                    sb.AppendLine((i == 0 ? "keepout" : "keepout" + (i + 1)) + " = " + R(g.Keepouts[i]));
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string R(Rect r) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.#},{1:0.#},{2:0.#},{3:0.#}", r.X1, r.Y1, r.X2, r.Y2);

        private static bool Inside(Rect r, Rect f) => r.X1 >= f.X1 - 0.5 && r.Y1 >= f.Y1 - 0.5 && r.X2 <= f.X2 + 0.5 && r.Y2 <= f.Y2 + 0.5;

        /// <summary>Merges rectangles closer than <paramref name="gap"/> until none are.</summary>
        public static List<Rect> Cluster(List<Rect> rects, double gap)
        {
            var list = new List<Rect>(rects);
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < list.Count && !merged; i++)
                    for (int j = i + 1; j < list.Count && !merged; j++)
                    {
                        var a = list[i]; var b = list[j];
                        if (a.X1 - gap <= b.X2 && b.X1 - gap <= a.X2 && a.Y1 - gap <= b.Y2 && b.Y1 - gap <= a.Y2)
                        {
                            list[i] = new Rect(Math.Min(a.X1, b.X1), Math.Min(a.Y1, b.Y1), Math.Max(a.X2, b.X2), Math.Max(a.Y2, b.Y2));
                            list.RemoveAt(j);
                            merged = true;
                        }
                    }
            }
            return list;
        }
    }
}
