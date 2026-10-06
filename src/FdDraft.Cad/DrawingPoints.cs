using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.XData;
using FdDraft.Core.Job;

namespace FdDraft.Cad
{
    /// <summary>
    /// The survey points already in a drawing, for a DWG opened without its FD-Pro job - so the
    /// Points and Codes lists, point clicks and point tools work on it too. Two kinds are read:
    /// <list type="bullet">
    /// <item>MicroSurvey CAD's: a POINT on an MSPOINT-* layer carrying the MSCAD_PRO_MSI
    /// extended data (description, point number, elevation) - how every MSCAD job stores them.</item>
    /// <item>FD-Draft's own: a POINT (or symbol block) tagged with its point number, from a plan
    /// drafted or imported by FD-Draft and saved.</item>
    /// </list>
    /// The drawing is only read, never changed.
    /// </summary>
    public static class DrawingPoints
    {
        public const string MscadApp = "MSCAD_PRO_MSI";

        /// <summary>A job holding the drawing's points and their codes; null when it has none.
        /// <paramref name="source"/> says which kind they were.</summary>
        public static FdJob? Read(CadDocument doc, string drawingPath, out string source)
        {
            source = "";
            var job = new FdJob();
            // Either slash: a Windows path read on any machine.
            string file = (drawingPath ?? "").Substring((drawingPath ?? "").LastIndexOfAny(new[] { '\\', '/' }) + 1);
            job.Settings.Name = Path.GetFileNameWithoutExtension(file);
            var seen = new HashSet<int>();
            int synthetic = 900000;
            var codeLayers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int mscad = 0, ours = 0;

            foreach (var e in doc.ModelSpace.Entities)
            {
                if (e is Point pt && TryMscad(pt, out string desc, out string number, out double? elev))
                {
                    var sp = new SurveyPoint
                    {
                        Northing = pt.Location.Y, Easting = pt.Location.X, Elevation = elev ?? pt.Location.Z, Code = desc.Trim(),
                    };
                    if (int.TryParse(number.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && id > 0) sp.Id = id;
                    else { sp.Id = ++synthetic; sp.Name = number.Trim(); }
                    if (!seen.Add(sp.Id)) continue; // a duplicate number: the first one stands
                    job.Points.Add(sp);
                    // "CT-NOELEV D0.30 R4": the code is the first word, the rest are its parameters.
                    string key = CodeKey(sp.Code);
                    if (key.Length > 0 && !codeLayers.ContainsKey(key)) codeLayers[key] = pt.Layer?.Name ?? "";
                    mscad++;
                }
            }
            if (mscad == 0)
            {
                // FD-Draft's own: tagged nodes first (they carry the shot's Z), then symbol blocks.
                foreach (var e in doc.ModelSpace.Entities.OrderBy(x => x is Point ? 0 : 1))
                {
                    if (!(e is Point || e is Insert)) continue;
                    var id = PointLinks.Tagged(e);
                    if (!id.HasValue || !seen.Add(id.Value)) continue;
                    var at = e is Point p ? p.Location : ((Insert)e).InsertPoint;
                    string code = PointLinks.TaggedCode(e) ?? "";
                    job.Points.Add(new SurveyPoint { Id = id.Value, Northing = at.Y, Easting = at.X, Elevation = at.Z, Code = code });
                    string ck = CodeKey(code);
                    if (ck.Length > 0 && !codeLayers.ContainsKey(ck)) codeLayers[ck] = e.Layer?.Name ?? "";
                    ours++;
                }
            }
            if (job.Points.Count == 0) return null;
            job.Points.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var kv in codeLayers.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                job.Codes.Add(new FeatureCode { Key = kv.Key, Description = kv.Key, LayerName = kv.Value });
            source = mscad > 0 ? "MicroSurvey CAD points" : "FD-Draft points";
            return job;
        }

        private static string CodeKey(string description)
        {
            var d = description.Trim();
            int sp = d.IndexOf(' ');
            return sp < 0 ? d : d.Substring(0, sp);
        }

        /// <summary>MSCAD's point data: description, point number (as text) and elevation.</summary>
        public static bool TryMscad(Entity e, out string description, out string number, out double? elevation)
        {
            description = ""; number = ""; elevation = null;
            ExtendedData? xd = null;
            foreach (var kv in e.ExtendedData)
                if (string.Equals(kv.Key.Name, MscadApp, StringComparison.OrdinalIgnoreCase)) { xd = kv.Value; break; }
            if (xd == null) return false;
            var strings = new List<string>();
            foreach (var r in xd.Records)
            {
                switch (r)
                {
                    case ExtendedDataString s: strings.Add(s.Value); break;
                    case ExtendedDataReal d when elevation == null: elevation = d.Value; break;
                }
            }
            if (strings.Count < 2) return false;
            description = strings[0];
            number = strings[1];
            return number.Trim().Length > 0;
        }
    }
}
