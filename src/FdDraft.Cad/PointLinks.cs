using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ACadSharp.Entities;
using ACadSharp.XData;
using FdDraft.Core.Geometry;
using FdDraft.Core.Job;

namespace FdDraft.Cad
{
    /// <summary>
    /// Which survey point an entity on the plan belongs to - its node, symbol, point number,
    /// elevation and monument/code label - so clicking any of them can find the point.
    /// Drafted entities carry the point number as extended data (AppId "FDDRAFT", a "POINT"
    /// string then the number), which is saved in the DWG and survives dragging a label away;
    /// anything untagged (an older drawing) falls back to where it sits.
    /// </summary>
    public static class PointLinks
    {
        public const string AppName = "FDDRAFT";
        private const string Key = "POINT";

        private const string CodeKey = "CODE";

        /// <summary>Tags <paramref name="e"/> with its point number. Call once the entity is in
        /// the document, so the application name is registered in the DWG.</summary>
        public static void Tag(Entity e, int pointId) => Set(e, Key, new ExtendedDataInteger32(pointId));

        /// <summary>Tags linework with the FD-Pro figure code it was drafted from.</summary>
        public static void TagCode(Entity e, string code)
        {
            if (!string.IsNullOrWhiteSpace(code)) Set(e, CodeKey, new ExtendedDataString(code.Trim()));
        }

        /// <summary>The figure code linework was tagged with, if any.</summary>
        public static string? TaggedCode(Entity e) => Get(e, CodeKey) is ExtendedDataString s ? s.Value : null;

        internal static void SetValue(Entity e, string key, ExtendedDataRecord value) => Set(e, key, value);
        internal static ExtendedDataRecord? GetValue(Entity e, string key) => Get(e, key);

        /// <summary>Sets one "KEY, value" pair in FD-Draft's extended data, leaving any others.</summary>
        internal static void Set(Entity e, string key, ExtendedDataRecord value)
        {
            if (!e.ExtendedData.TryGet(AppName, out var xd))
            {
                xd = new ExtendedData();
                e.ExtendedData.Add(AppName, xd);
            }
            var r = xd.Records;
            for (int i = 0; i + 1 < r.Count; i++)
                if (r[i] is ExtendedDataString s && s.Value == key) { r[i + 1] = value; return; }
            r.Add(new ExtendedDataString(key));
            r.Add(value);
        }

        internal static ExtendedDataRecord? Get(Entity e, string key)
        {
            if (!e.ExtendedData.TryGet(AppName, out var xd)) return null;
            var r = xd.Records;
            for (int i = 0; i + 1 < r.Count; i++)
                if (r[i] is ExtendedDataString s && s.Value == key) return r[i + 1];
            return null;
        }

        /// <summary>
        /// The FD-Pro code an entity belongs to, as a key in <paramref name="codes"/>: its own tag;
        /// else its survey point's code; else the one code whose layer it is on (null when
        /// several codes share that layer and nothing else says which).
        /// </summary>
        public static FeatureCode? CodeOf(Entity e, IReadOnlyList<SurveyPoint> points, IReadOnlyList<FeatureCode> codes)
        {
            if (codes.Count == 0) return null;
            string? code = TaggedCode(e) ?? Find(e, points)?.Code;
            if (!string.IsNullOrWhiteSpace(code))
            {
                var exact = codes.FirstOrDefault(c => c.Key.Equals(code, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
                // A shot coded with a suffix ("FDS2", "TRA-1") belongs to the longest code it starts with.
                var prefix = codes.Where(c => c.Key.Length > 0 && code.StartsWith(c.Key, StringComparison.OrdinalIgnoreCase)).OrderByDescending(c => c.Key.Length).FirstOrDefault();
                if (prefix != null) return prefix;
            }
            var onLayer = codes.Where(c => c.LayerName.Equals(e.Layer?.Name ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
            return onLayer.Count == 1 ? onLayer[0] : null;
        }

        /// <summary>The point number an entity was tagged with, if any.</summary>
        public static int? Tagged(Entity e)
        {
            return Get(e, Key) switch
            {
                ExtendedDataInteger32 n => n.Value,
                ExtendedDataInteger16 n => n.Value,
                _ => (int?)null,
            };
        }

        /// <summary>
        /// The survey point <paramref name="e"/> belongs to: its tag, or else - for a drawing
        /// without tags - a node or symbol standing on a point, a text reading a point's number
        /// near it, or a label within a few text heights of one point.
        /// </summary>
        public static SurveyPoint? Find(Entity e, IReadOnlyList<SurveyPoint> points)
        {
            if (points.Count == 0) return null;
            var tag = Tagged(e);
            if (tag.HasValue) return points.FirstOrDefault(p => p.Id == tag.Value);

            const double onPoint = 0.005; // 5 mm: drafted nodes/symbols sit exactly on the shot
            switch (e)
            {
                case Point pt: return Nearest(points, new Vec2(pt.Location.X, pt.Location.Y), onPoint);
                case Insert ins: return Nearest(points, new Vec2(ins.InsertPoint.X, ins.InsertPoint.Y), onPoint);
                case Circle c: return Nearest(points, new Vec2(c.Center.X, c.Center.Y), Math.Max(c.Radius * 3, onPoint));
                case TextEntity t:
                {
                    bool aligned = t.HorizontalAlignment != TextHorizontalAlignment.Left || t.VerticalAlignment != TextVerticalAlignmentType.Baseline;
                    var at = aligned ? new Vec2(t.AlignmentPoint.X, t.AlignmentPoint.Y) : new Vec2(t.InsertPoint.X, t.InsertPoint.Y);
                    double reach = Math.Max(t.Height, 1e-6) * 4;
                    // A point number names its point outright, as long as it's anywhere near it.
                    if (int.TryParse(t.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                    {
                        var named = points.FirstOrDefault(p => p.Id == id);
                        if (named != null && Vec2.Distance(at, new Vec2(named.Easting, named.Northing)) <= reach * 3) return named;
                    }
                    return Nearest(points, at, reach);
                }
                default: return null;
            }
        }

        private static SurveyPoint? Nearest(IReadOnlyList<SurveyPoint> points, Vec2 at, double within)
        {
            SurveyPoint? best = null; double bestD = within;
            foreach (var p in points)
            {
                double d = Vec2.Distance(at, new Vec2(p.Easting, p.Northing));
                if (d <= bestD) { bestD = d; best = p; }
            }
            return best;
        }
    }
}
