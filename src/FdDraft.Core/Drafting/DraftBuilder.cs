using System;
using System.Collections.Generic;
using System.Text;
using FdDraft.Core.Geometry;
using FdDraft.Core.Job;
using FdDraft.Core.Standards;

namespace FdDraft.Core.Drafting
{
    /// <summary>
    /// Turns an FD-Pro job into survey geometry on the right layers. Scale-free:
    /// labels are added afterwards by <see cref="Annotator"/>, once a sheet and scale
    /// have been chosen from this geometry.
    /// </summary>
    public static class DraftBuilder
    {
        public static DraftDocument Build(FdJob job, FirmStandards std)
        {
            var doc = new DraftDocument { JobName = job.Settings.Name };
            doc.Warnings.AddRange(job.Warnings);
            var courseKeys = new HashSet<string>();

            foreach (var figure in job.Figures)
            {
                if (figure.Mode == "MEASURED" && !std.DrawMeasuredLines) continue;
                BuildFigure(job, std, doc, figure, courseKeys);
            }

            foreach (var p in job.Points)
            {
                var code = job.Code(p.Code);
                string lineLayer = CodeLayer(p.Code, code, std);
                string feature = Feature(lineLayer, std);
                string symbolLayer = std.PointSymbolLayers.TryGetValue(p.Code, out var forced)
                    ? Sanitize(forced)
                    : Sanitize(Expand(std.PointSymbolPattern, feature, lineLayer));
                int aci = code?.Aci ?? 7;
                doc.Layer(symbolLayer, aci);
                string monument = "";
                if (std.Monuments.TryGetValue(p.Code, out var mon))
                    monument = std.MonumentFormat.Replace("{mon}", mon).Replace("{note}", p.Note).Trim();
                doc.Entities.Add(new DraftSymbol
                {
                    Layer = symbolLayer,
                    Position = Vec2.FromNE(p.Northing, p.Easting),
                    PointId = p.Id,
                    Code = p.Code,
                    Symbol = code?.Symbol ?? "DOT",
                    BlockName = std.SymbolBlocks.Get(p.Code, ""),
                    SizeMm = std.SymbolSizeMm,
                    Elevation = p.Elevation,
                    Note = p.Note,
                    Feature = feature,
                    NumberLayer = doc.Layer(Sanitize(Expand(std.PointNumberPattern, feature, lineLayer)), aci).Name,
                    ElevationLayer = doc.Layer(Sanitize(Expand(std.PointElevationPattern, feature, lineLayer)), aci).Name,
                    MonumentText = monument,
                    ShowElevation = !job.Settings.Is2D && !FirmStandards.MatchesAny(std.NoElevationCodes, p.Code),
                });
            }
            return doc;
        }

        /// <summary>
        /// The layer a code's linework goes on: the firm's [line-layers] first, then the
        /// job's codes.csv LINE LAYER, then the code itself; no code at all falls to the
        /// default line layer.
        /// </summary>
        public static string LineLayer(string codeKey, FeatureCode? code, FirmStandards std)
        {
            string key = codeKey.Trim();
            if (key.Length > 0 && std.LineLayers.TryGetValue(key, out var mapped) && mapped.Trim().Length > 0) return Sanitize(mapped);
            if (code != null && code.LayerName.Length > 0) return Sanitize(code.LayerName);
            if (key.Length > 0) return Sanitize(key.ToUpperInvariant());
            return Sanitize(std.DefaultLineLayer);
        }

        /// <summary>The code's own layer from codes.csv (no [line-layers] override).</summary>
        public static string CodeLayer(string codeKey, FeatureCode? code, FirmStandards std)
        {
            if (code != null && code.LayerName.Length > 0) return Sanitize(code.LayerName);
            string key = codeKey.Trim();
            return key.Length > 0 ? Sanitize(key.ToUpperInvariant()) : Sanitize(std.DefaultLineLayer);
        }

        /// <summary>
        /// The feature name behind the per-feature point layers: the line layer with its
        /// dashes removed (FENCE-CHAINLINK -> FENCECHAINLINK), unless [feature-map] says otherwise.
        /// Uses the code's own layer, not a [line-layers] override, so a boundary line
        /// remapped to PLAN-SubjectBoundary keeps its monument points on MONUMENT layers.
        /// </summary>
        public static string Feature(string lineLayer, FirmStandards std)
        {
            if (std.FeatureMap.TryGetValue(lineLayer, out var f)) return f;
            return lineLayer.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        }

        private static string Expand(string pattern, string feature, string layer) =>
            pattern.Replace("{feature}", feature).Replace("{layer}", layer);

        /// <summary>AutoCAD refuses these characters in a layer name: &lt;&gt;/\":;?*|=`</summary>
        public static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name.Trim())
            {
                sb.Append("<>/\\\":;?*|=`,".IndexOf(c) >= 0 || char.IsControl(c) ? '_' : c);
            }
            var s = sb.ToString();
            if (s.Length > 255) s = s.Substring(0, 255);
            return s.Length == 0 ? "0" : s;
        }

        private sealed class Piece
        {
            public string Shape = "STRAIGHT"; // STRAIGHT, SPLINE, ARC
            public List<int> Ids = new List<int>();
            public bool FullCircle;
        }

        /// <summary>
        /// FD-Pro's SavedLine.pieces() (round 199) ported: a mixed figure is split into
        /// single-shape runs sharing end points; arc spans are taken two at a time,
        /// and an odd arc span left over is a straight line.
        /// </summary>
        private static List<Piece> Pieces(Figure f)
        {
            var output = new List<Piece>();
            var ids = f.PointIds;
            var modes = f.Segments;
            if (modes == null || ids.Count < 2 || modes.Count != ids.Count - 1)
            {
                string shape = f.Mode == "SPLINE" ? "SPLINE" : f.Mode == "ARC" ? "ARC" : "STRAIGHT";
                if (shape == "ARC")
                {
                    // A lone ARC figure is one 3-point arc (FD-Pro draws ordered[0..2]).
                    if (ids.Count >= 3) output.Add(new Piece { Shape = "ARC", Ids = ids.GetRange(0, 3), FullCircle = f.Circle });
                    else output.Add(new Piece { Shape = "STRAIGHT", Ids = new List<int>(ids) });
                }
                else output.Add(new Piece { Shape = shape, Ids = new List<int>(ids) });
                return output;
            }
            int i = 0;
            while (i < modes.Count)
            {
                string m = modes[i];
                if (m == "ARC")
                {
                    int span = i + 1 < modes.Count && modes[i + 1] == "ARC" ? 2 : 1;
                    output.Add(new Piece { Shape = span == 2 ? "ARC" : "STRAIGHT", Ids = ids.GetRange(i, span + 1) });
                    i += span;
                }
                else
                {
                    int j = i;
                    while (j + 1 < modes.Count && modes[j + 1] == m) j++;
                    output.Add(new Piece { Shape = m == "SPLINE" ? "SPLINE" : "STRAIGHT", Ids = ids.GetRange(i, j - i + 2) });
                    i = j + 1;
                }
            }
            return output;
        }

        private static void BuildFigure(FdJob job, FirmStandards std, DraftDocument doc, Figure figure, HashSet<string> courseKeys)
        {
            var code = job.Code(figure.Code);
            var layer = doc.Layer(LineLayer(figure.Code, code, std), code?.Aci ?? 7, code?.LineType ?? "");
            bool isArea = figure.Mode == "AREA";
            bool closedArea = isArea && !figure.Open && figure.PointIds.Count >= 3;
            bool labelled = std.IsLabelledCode(figure.Code) || (isArea && std.LabelAreaFigures);

            var pieces = Pieces(figure);
            DraftPolyline? current = null;
            var figureCourses = new List<Course>();
            int lastId = int.MinValue;

            void Flush()
            {
                if (current != null && current.Vertices.Count >= 2) doc.Entities.Add(current);
                current = null;
            }

            // Continue the current polyline when this piece starts where it ended;
            // otherwise finish it and start a new one.
            void Ensure(int firstId, Vec2 first)
            {
                if (current != null && lastId == firstId) return;
                Flush();
                current = new DraftPolyline { Layer = layer.Name, Code = figure.Code };
                current.Add(first);
            }

            foreach (var piece in pieces)
            {
                var pts = new List<Vec2>();
                var ids = new List<int>();
                foreach (int id in piece.Ids)
                {
                    var p = job.Point(id);
                    if (p == null) { doc.Warnings.Add("Figure " + figure.Code + " refers to point " + id + ", which is not in points.csv; skipped."); continue; }
                    pts.Add(Vec2.FromNE(p.Northing, p.Easting));
                    ids.Add(id);
                }
                if (pts.Count < 2) continue;

                if (piece.Shape == "SPLINE" && pts.Count >= 3)
                {
                    Flush();
                    var spline = new DraftSpline { Layer = layer.Name };
                    spline.FitPoints.AddRange(pts);
                    doc.Entities.Add(spline);
                    lastId = int.MinValue;
                    continue; // Spline courses are not labelled with bearings - they are not boundary courses.
                }

                if (piece.Shape == "ARC" && pts.Count == 3)
                {
                    var arc = Arc.ThroughThreePoints(pts[0], pts[1], pts[2]);
                    if (arc != null && piece.FullCircle)
                    {
                        Flush();
                        doc.Entities.Add(new DraftCircle { Layer = layer.Name, Center = arc.Center, Radius = arc.Radius });
                        lastId = int.MinValue;
                        continue;
                    }
                    Ensure(ids[0], pts[0]);
                    var middle = job.Point(ids[1]);
                    if (arc != null && middle != null && std.Monuments.TryGetValue(middle.Code, out _))
                    {
                        // The middle point is a monument: on a plan that is two curves, each with its own data.
                        var halves = arc.SplitAt(pts[1]);
                        current!.Bulges[current.Bulges.Count - 1] = halves[0].Bulge;
                        current.Add(pts[1], halves[1].Bulge);
                        current.Add(pts[2]);
                        figureCourses.Add(new Course { FromId = ids[0], ToId = ids[1], A = pts[0], B = pts[1], Arc = halves[0], Code = figure.Code, Labelled = labelled });
                        figureCourses.Add(new Course { FromId = ids[1], ToId = ids[2], A = pts[1], B = pts[2], Arc = halves[1], Code = figure.Code, Labelled = labelled });
                    }
                    else if (arc != null)
                    {
                        current!.Bulges[current.Bulges.Count - 1] = arc.Bulge;
                        current.Add(pts[2]);
                        figureCourses.Add(new Course { FromId = ids[0], ToId = ids[2], A = pts[0], B = pts[2], Arc = arc, Code = figure.Code, Labelled = labelled });
                    }
                    else
                    {
                        // Collinear: FD-Pro draws a straight line from the first to the third point.
                        current!.Add(pts[2]);
                        figureCourses.Add(new Course { FromId = ids[0], ToId = ids[2], A = pts[0], B = pts[2], Code = figure.Code, Labelled = labelled });
                    }
                    lastId = ids[2];
                    continue;
                }

                // STRAIGHT (and a 2-point spline or arc, which is a straight line).
                Ensure(ids[0], pts[0]);
                for (int k = 1; k < pts.Count; k++)
                {
                    current!.Add(pts[k]);
                    figureCourses.Add(new Course { FromId = ids[k - 1], ToId = ids[k], A = pts[k - 1], B = pts[k], Code = figure.Code, Labelled = labelled });
                }
                lastId = ids[ids.Count - 1];
            }

            if (current != null && current.Vertices.Count >= 2)
            {
                // A polyline whose last vertex returns to its first is closed, not doubled back.
                bool returns = current.Vertices.Count >= 3 && Vec2.Distance(current.Vertices[0], current.Vertices[current.Vertices.Count - 1]) < 1e-6;
                if (returns)
                {
                    current.Vertices.RemoveAt(current.Vertices.Count - 1);
                    current.Bulges.RemoveAt(current.Bulges.Count - 1);
                    current.Closed = true;
                }
                else if (closedArea)
                {
                    current.Closed = true;
                    var first = job.Point(figure.PointIds[0]);
                    var last = job.Point(figure.PointIds[figure.PointIds.Count - 1]);
                    if (first != null && last != null)
                    {
                        figureCourses.Add(new Course
                        {
                            FromId = last.Id, ToId = first.Id,
                            A = current.Vertices[current.Vertices.Count - 1], B = current.Vertices[0],
                            Code = figure.Code, Labelled = labelled,
                        });
                    }
                }
                doc.Entities.Add(current);

                if (current.Closed && current.Vertices.Count >= 3)
                {
                    var parcel = new Parcel { Code = figure.Code };
                    parcel.Vertices.AddRange(current.Vertices);
                    parcel.Bulges.AddRange(current.Bulges);
                    parcel.PointIds.AddRange(figure.PointIds);
                    double perimeter = 0;
                    foreach (var c in figureCourses) perimeter += c.Length;
                    parcel.Perimeter = perimeter;
                    doc.Parcels.Add(parcel);
                    // Counter-clockwise parcel: inside is on the left of each course.
                    int side = Polygon.SignedArea(parcel.Vertices, parcel.Bulges) > 0 ? 1 : -1;
                    foreach (var c in figureCourses) c.InsideSide = side;
                }
            }
            current = null;

            foreach (var c in figureCourses)
            {
                // Adjacent lots share a course: label it once. The first figure to claim it
                // wins, except a labelled figure always beats an unlabelled one.
                string key = (c.Arc != null ? "A" : "S") + Math.Min(c.FromId, c.ToId) + "-" + Math.Max(c.FromId, c.ToId);
                if (courseKeys.Contains(key))
                {
                    if (c.Labelled)
                    {
                        for (int i = 0; i < doc.Courses.Count; i++)
                        {
                            var existing = doc.Courses[i];
                            string k2 = (existing.Arc != null ? "A" : "S") + Math.Min(existing.FromId, existing.ToId) + "-" + Math.Max(existing.FromId, existing.ToId);
                            if (k2 == key && !existing.Labelled) { doc.Courses[i] = c; break; }
                        }
                    }
                    continue;
                }
                courseKeys.Add(key);
                doc.Courses.Add(c);
            }
        }
    }
}
