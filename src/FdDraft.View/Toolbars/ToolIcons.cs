using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FdDraft.View.Toolbars
{
    /// <summary>What an icon stroke means - the colour key the icons share (see tools/icons).</summary>
    public enum IconRole
    {
        /// <summary>Existing linework.</summary>
        Ink,
        /// <summary>The result, and distances.</summary>
        New,
        /// <summary>Annotation text, bearings.</summary>
        Lbl,
        /// <summary>Snap markers, picks.</summary>
        Snap,
        /// <summary>Before-state or guide lines (usually dashed).</summary>
        Ghost,
        /// <summary>Create / confirm.</summary>
        Ok,
        /// <summary>A translucent fill of <see cref="New"/>.</summary>
        NewFill,
    }

    public enum IconPrimKind { Line, Path, Circle, Rect, Text }

    /// <summary>One stroke of a toolbar icon, on a 24x24 grid with y pointing down.</summary>
    public sealed class IconPrim
    {
        public IconPrimKind Kind;
        /// <summary>Line: x1 y1 x2 y2. Circle: cx cy r. Rect: x y w h. Text: x y size rotation ax ay.</summary>
        public double[] N = Array.Empty<double>();
        public IconRole Role;
        public double Width = 1.5;
        public bool Dashed;
        /// <summary>Null = no fill; otherwise the role whose colour fills the shape.</summary>
        public IconRole? Fill;
        /// <summary>Path data (SVG / WPF path mini-language) or the text.</summary>
        public string Data = "";
        /// <summary>Text anchor: start, middle or end.</summary>
        public string Anchor = "start";
    }

    /// <summary>
    /// FD-Draft's toolbar icons: small vector drawings on a 24x24 grid, drawn from
    /// tools/icons/icons.py (the generator writes ToolIconData.g.cs). Kept free of WPF so the
    /// set can be checked on any machine; the app turns the primitives into a DrawingImage.
    /// </summary>
    public static partial class ToolIcons
    {
        public static IEnumerable<string> Keys => Source.Keys;

        public static bool Has(string key) => Source.ContainsKey(key);

        private static readonly Dictionary<string, List<IconPrim>> _parsed = new Dictionary<string, List<IconPrim>>();

        /// <summary>The icon's primitives, or an empty list for an unknown key.</summary>
        public static IReadOnlyList<IconPrim> Get(string key)
        {
            lock (_parsed)
            {
                if (_parsed.TryGetValue(key, out var p)) return p;
                p = Source.TryGetValue(key, out var src) ? Parse(src) : new List<IconPrim>();
                _parsed[key] = p;
                return p;
            }
        }

        public static List<IconPrim> Parse(string source)
        {
            var list = new List<IconPrim>();
            foreach (var raw in source.Split('\n'))
            {
                if (raw.Length == 0) continue;
                string head = raw, tail = "";
                int bar = raw.IndexOf('|');
                if (bar >= 0) { head = raw.Substring(0, bar); tail = raw.Substring(bar + 1); }
                var t = head.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                double D(int i) => double.Parse(t[i], NumberStyles.Float, CultureInfo.InvariantCulture);
                var prim = new IconPrim();
                switch (t[0])
                {
                    case "L":
                        prim.Kind = IconPrimKind.Line; prim.N = new[] { D(1), D(2), D(3), D(4) };
                        prim.Role = RoleOf(t[5]); prim.Width = D(6); prim.Dashed = t[7] == "1";
                        break;
                    case "P":
                        prim.Kind = IconPrimKind.Path; prim.Role = RoleOf(t[1]); prim.Width = D(2); prim.Dashed = t[3] == "1";
                        prim.Fill = FillOf(t[4], prim.Role); prim.Data = tail;
                        break;
                    case "C":
                        prim.Kind = IconPrimKind.Circle; prim.N = new[] { D(1), D(2), D(3) };
                        prim.Role = RoleOf(t[4]); prim.Width = D(5); prim.Dashed = t[6] == "1"; prim.Fill = FillOf(t[7], prim.Role);
                        break;
                    case "R":
                        prim.Kind = IconPrimKind.Rect; prim.N = new[] { D(1), D(2), D(3), D(4) };
                        prim.Role = RoleOf(t[5]); prim.Width = D(6); prim.Dashed = t[7] == "1"; prim.Fill = FillOf(t[8], prim.Role);
                        break;
                    case "T":
                        prim.Kind = IconPrimKind.Text; prim.N = new[] { D(1), D(2), D(3), D(6), D(7), D(8) };
                        prim.Anchor = t[4]; prim.Role = RoleOf(t[5]); prim.Data = tail;
                        break;
                    default:
                        throw new FormatException("unknown icon primitive: " + raw);
                }
                list.Add(prim);
            }
            return list;
        }

        private static IconRole RoleOf(string s) => s switch
        {
            "ink" => IconRole.Ink,
            "new" => IconRole.New,
            "lbl" => IconRole.Lbl,
            "snap" => IconRole.Snap,
            "ghost" => IconRole.Ghost,
            "ok" => IconRole.Ok,
            "newfill" => IconRole.NewFill,
            _ => throw new FormatException("unknown icon role " + s),
        };

        private static IconRole? FillOf(string s, IconRole stroke) => s == "-" ? (IconRole?)null : s == "=" ? stroke : RoleOf(s);

        /// <summary>The dark-toolbar colour for a role, as 0xAARRGGBB.</summary>
        public static uint DarkColor(IconRole role) => role switch
        {
            IconRole.Ink => 0xFFE3E5E8,
            IconRole.New => 0xFF5AA2FF,
            IconRole.Lbl => 0xFFFF6B5F,
            IconRole.Snap => 0xFFFFA733,
            IconRole.Ghost => 0xFF8B9098,
            IconRole.Ok => 0xFF4CC37A,
            IconRole.NewFill => 0x335AA2FF,
            _ => 0xFFFFFFFF,
        };
    }
}
