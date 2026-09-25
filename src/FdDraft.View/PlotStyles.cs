using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace FdDraft.View
{
    /// <summary>One colour's pen in a plot style table.</summary>
    public sealed class PlotPen
    {
        /// <summary>The colour it plots in (0xRRGGBB), or null to plot in the object's own colour.</summary>
        public uint? Color { get; set; }
        /// <summary>Plot in grey (the colour's luminance) - the "Grayscale" setting.</summary>
        public bool Grayscale { get; set; }
        /// <summary>Screening, 0-100: 100 is full ink, less fades toward white.</summary>
        public int Screen { get; set; } = 100;
        /// <summary>Pen width in mm, or null to use the object's own lineweight.</summary>
        public double? LineWeightMm { get; set; }
    }

    /// <summary>
    /// A colour-dependent plot style table (.ctb) - how AutoCAD, MSCAD and FD-Draft turn each of
    /// the 255 colour numbers into a pen: its plotted colour (e.g. everything black in
    /// monochrome.ctb), lineweight and screening.
    /// </summary>
    public sealed class PlotStyleTable
    {
        public string Name { get; }
        public string Description { get; private set; } = "";
        /// <summary>Pens for colour numbers 1..255 (index 0 unused).</summary>
        private readonly PlotPen[] _pens = Enumerable.Range(0, 256).Select(_ => new PlotPen()).ToArray();

        private PlotStyleTable(string name) { Name = name; }

        /// <summary>The pen for a colour number (1-255); anything else (a true colour) gets the
        /// object's own colour and lineweight, as AutoCAD plots it.</summary>
        public PlotPen Pen(int aci) => aci >= 1 && aci <= 255 ? _pens[aci] : ObjectPen;

        private static readonly PlotPen ObjectPen = new PlotPen();

        /// <summary>The built-in "monochrome": every colour plots black, object lineweights.</summary>
        public static PlotStyleTable Monochrome()
        {
            var t = new PlotStyleTable("monochrome.ctb (built in)") { Description = "Every colour plots black." };
            for (int i = 1; i <= 255; i++) t._pens[i].Color = 0x000000;
            return t;
        }

        /// <summary>The built-in "grayscale": every colour plots as its own grey.</summary>
        public static PlotStyleTable Grayscale()
        {
            var t = new PlotStyleTable("grayscale.ctb (built in)") { Description = "Every colour plots as a grey of the same lightness." };
            for (int i = 1; i <= 255; i++) t._pens[i].Grayscale = true;
            return t;
        }

        /// <summary>Reads a .ctb file.</summary>
        public static PlotStyleTable Load(string path)
        {
            var t = Parse(Decompress(File.ReadAllBytes(path)), Path.GetFileName(path));
            return t;
        }

        /// <summary>
        /// A .ctb's text. The file is a 48-byte signature ("PIAFILEVERSION_2.0,CTBVER1,compress",
        /// CR LF, "pmzlibcodec"), three 32-bit numbers (checksum, text length, compressed length)
        /// and then the zlib-compressed text.
        /// </summary>
        public static string Decompress(byte[] file)
        {
            if (file.Length < 62 || !Encoding.ASCII.GetString(file, 0, 14).StartsWith("PIAFILEVERSION", StringComparison.Ordinal))
                throw new InvalidDataException("not a plot style table (.ctb/.stb) file");
            using var src = new MemoryStream(file, 60, file.Length - 60);
            using var z = new ZLibStream(src, CompressionMode.Decompress);
            using var outp = new MemoryStream();
            z.CopyTo(outp);
            return Encoding.Latin1.GetString(outp.ToArray()).TrimEnd('\0');
        }

        /// <summary>Writes a .ctb in the same format (used by the tests, and to save edits later).</summary>
        public static byte[] Compress(string text)
        {
            var raw = Encoding.Latin1.GetBytes(text + "\0");
            byte[] body;
            using (var ms = new MemoryStream())
            {
                using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw, 0, raw.Length);
                body = ms.ToArray();
            }
            var head = Encoding.ASCII.GetBytes("PIAFILEVERSION_2.0,CTBVER1,compress\r\npmzlibcodec");
            var result = new byte[60 + body.Length];
            Array.Copy(head, result, head.Length);
            BitConverter.GetBytes(Adler32(body)).CopyTo(result, 48);
            BitConverter.GetBytes((uint)raw.Length).CopyTo(result, 52);
            BitConverter.GetBytes((uint)body.Length).CopyTo(result, 56);
            Array.Copy(body, 0, result, 60, body.Length);
            return result;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (var x in data) { a = (a + x) % 65521; b = (b + a) % 65521; }
            return (b << 16) | a;
        }

        // Colour values meaning "use the object's colour".
        private const int ObjectColor = -1;
        private const int ObjectColor2 = unchecked((int)0xC3FFFFFF);

        /// <summary>The standard lineweight list, used when a table has no lineweight table of its own.</summary>
        public static readonly double[] StandardLineweights =
            { 0.00, 0.05, 0.09, 0.10, 0.13, 0.15, 0.18, 0.20, 0.25, 0.30, 0.35, 0.40, 0.45, 0.50, 0.53, 0.60, 0.65, 0.70, 0.80, 0.90, 1.00, 1.06, 1.20, 1.40, 1.58, 2.00, 2.11 };

        /// <summary>Builds a table from a .ctb's decompressed text.</summary>
        public static PlotStyleTable Parse(string text, string name)
        {
            var root = Node.Parse(text);
            var t = new PlotStyleTable(name) { Description = root.Value("description").Trim('"') };
            var weights = StandardLineweights;
            if (root.Child("custom_lineweight_table") is Node lwt)
            {
                var list = lwt.Values.Select(kv => (Ok: int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i), Index: i, kv.Value))
                    .Where(x => x.Ok).OrderBy(x => x.Index)
                    .Select(x => double.TryParse(x.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0).ToArray();
                if (list.Length > 0) weights = list;
            }
            if (root.Child("plot_style") is Node styles)
            {
                foreach (var (key, style) in styles.Children)
                {
                    // plot_style entry n is colour number n + 1.
                    if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0 || n > 254) continue;
                    var pen = t._pens[n + 1];
                    if (int.TryParse(style.Value("color"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int color)
                        && color != ObjectColor && color != ObjectColor2)
                        pen.Color = (uint)color & 0xFFFFFF;
                    if (int.TryParse(style.Value("color_policy"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int policy))
                        pen.Grayscale = (policy & 2) != 0;
                    if (int.TryParse(style.Value("screen"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int screen))
                        pen.Screen = Math.Max(0, Math.Min(100, screen));
                    // An index into the lineweight table; 0 (or anything past its end) = object lineweight.
                    if (int.TryParse(style.Value("lineweight"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int lw)
                        && lw > 0 && lw < weights.Length)
                        pen.LineWeightMm = weights[lw];
                }
            }
            return t;
        }

        /// <summary>
        /// A drawn item's plotted colour: the pen's colour (or the object's own), in grey when the
        /// pen says so, faded toward white by its screening.
        /// </summary>
        public static uint PlotColor(PlotPen pen, uint objectRgb)
        {
            uint rgb = pen.Color ?? objectRgb;
            int r = (int)(rgb >> 16) & 255, g = (int)(rgb >> 8) & 255, b = (int)rgb & 255;
            if (pen.Grayscale)
            {
                int y = (int)Math.Round(0.299 * r + 0.587 * g + 0.114 * b);
                r = g = b = y;
            }
            if (pen.Screen < 100)
            {
                double s = pen.Screen / 100.0;
                r = (int)Math.Round(255 - (255 - r) * s); g = (int)Math.Round(255 - (255 - g) * s); b = (int)Math.Round(255 - (255 - b) * s);
            }
            return (uint)(r << 16 | g << 8 | b);
        }

        /// <summary>The text of a .ctb: "key=value" lines and "name{ ... }" blocks.</summary>
        private sealed class Node
        {
            public List<KeyValuePair<string, string>> Values { get; } = new List<KeyValuePair<string, string>>();
            public List<(string Key, Node Node)> Children { get; } = new List<(string, Node)>();

            public string Value(string key) => Values.FirstOrDefault(kv => kv.Key == key).Value ?? "";
            public Node? Child(string key) => Children.FirstOrDefault(c => c.Key == key).Node;

            public static Node Parse(string text)
            {
                var root = new Node();
                var stack = new Stack<Node>();
                stack.Push(root);
                foreach (var raw in text.Split('\n'))
                {
                    var line = raw.Trim().TrimEnd('\r');
                    if (line.Length == 0) continue;
                    if (line == "}") { if (stack.Count > 1) stack.Pop(); continue; }
                    if (line.EndsWith("{", StringComparison.Ordinal) && !line.Contains('='))
                    {
                        var child = new Node();
                        stack.Peek().Children.Add((line.Substring(0, line.Length - 1).Trim(), child));
                        stack.Push(child);
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq > 0) stack.Peek().Values.Add(new KeyValuePair<string, string>(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim()));
                }
                return root;
            }
        }
    }
}
