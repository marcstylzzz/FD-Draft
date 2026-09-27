using System;
using System.Globalization;
using System.Linq;

namespace FdDraft.Core.Geometry
{
    /// <summary>Parses the bearing/distance text a drafter types at the command line for the
    /// COGO drafting tools (LINE, and the leg between two picked points).</summary>
    public static class Cogo
    {
        /// <summary>
        /// A quadrant bearing such as "N45-30-00E", "N45.5E", "N45d30'00"E", "NE30.0030" (DD.MMSS), or a plain
        /// azimuth in decimal degrees ("125.5"). Returns the azimuth in radians, 0 = north,
        /// clockwise - the same convention as <see cref="FdDraft.Core.Geometry.Angles"/>.
        /// </summary>
        public static double ParseBearing(string text)
        {
            string s = text.Trim();
            if (s.Length == 0) throw new FormatException("empty bearing");
            char first = char.ToUpperInvariant(s[0]);
            char last = char.ToUpperInvariant(s[^1]);
            // Quadrant first, then the angle: "NE30.0030" / "ne 30-00-30". A dotted number here
            // is DD.MMSS (30.0030 = 30°00'30"), the way surveyors key bearings in MSCAD.
            if (s.Length > 2 && (first == 'N' || first == 'S') && "EW".IndexOf(char.ToUpperInvariant(s[1])) >= 0
                && (char.IsDigit(s[^1]) || s[^1] == '"' || s[^1] == '\''))
            {
                char ew = char.ToUpperInvariant(s[1]);
                string rest = s.Substring(2).Trim();
                double angle;
                if (double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out double dms))
                    angle = FromDdMmSs(dms);
                else
                {
                    var parts = rest.Split(new[] { '-', ':', 'd', 'D', 'm', 'M', 's', 'S', '\'', '"', '°', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                    if (parts.Length == 0) throw new FormatException("bearing has no numbers: " + text);
                    angle = parts[0] + (parts.Length > 1 ? parts[1] / 60.0 : 0) + (parts.Length > 2 ? parts[2] / 3600.0 : 0);
                }
                if (angle < 0 || angle > 90) throw new FormatException("a quadrant bearing is 0 to 90 degrees: " + text);
                return Normalize(QuadrantToAzimuth(first, ew, angle) * Math.PI / 180.0);
            }
            if ((first == 'N' || first == 'S') && (last == 'E' || last == 'W') && s.Length > 2)
            {
                string mid = s.Substring(1, s.Length - 2);
                var nums = mid.Split(new[] { '-', ':', 'd', 'D', 'm', 'M', 's', 'S', '\'', '"', '°', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                if (nums.Length == 0) throw new FormatException("bearing has no numbers: " + text);
                double deg = nums[0];
                double min = nums.Length > 1 ? nums[1] : 0;
                double sec = nums.Length > 2 ? nums[2] : 0;
                double angle = deg + min / 60.0 + sec / 3600.0;
                return Normalize(QuadrantToAzimuth(first, last, angle) * Math.PI / 180.0);
            }
            // A plain azimuth in decimal degrees (0 = north, clockwise).
            return Normalize(double.Parse(s, CultureInfo.InvariantCulture) * Math.PI / 180.0);
        }

        private static double QuadrantToAzimuth(char ns, char ew, double angle) => (ns, ew) switch
        {
            ('N', 'E') => angle,
            ('S', 'E') => 180 - angle,
            ('S', 'W') => 180 + angle,
            _ => 360 - angle, // N.. W
        };

        /// <summary>DD.MMSS (30.0030 = 30°00'30", 45.3015 = 45°30'15") to decimal degrees.</summary>
        public static double FromDdMmSs(double v)
        {
            double a = Math.Abs(v);
            double deg = Math.Floor(a);
            double mmss = Math.Round((a - deg) * 10000, 6);
            double min = Math.Floor(mmss / 100);
            double sec = mmss - min * 100;
            if (min >= 60 || sec >= 60) throw new FormatException("minutes and seconds must be under 60: " + v.ToString(CultureInfo.InvariantCulture));
            return Math.Sign(v == 0 ? 1 : v) * (deg + min / 60.0 + sec / 3600.0);
        }

        private static double Normalize(double radians)
        {
            const double twoPi = 2 * Math.PI;
            radians %= twoPi;
            if (radians < 0) radians += twoPi;
            return radians;
        }

        /// <summary>
        /// Splits a command-line "BEARING DISTANCE" leg, e.g. "N45-30-00E 125.50" or
        /// "125.5 30" (azimuth then distance). Distance is in the drawing's own units.
        /// </summary>
        public static bool TryParseLeg(string text, out double azimuthRadians, out double distance)
        {
            azimuthRadians = 0; distance = 0;
            var parts = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // "NE 30.0030 125.5": the quadrant typed apart from its angle.
            if (parts.Length == 3 && parts[0].Length == 2 && "NS".IndexOf(char.ToUpperInvariant(parts[0][0])) >= 0
                && "EW".IndexOf(char.ToUpperInvariant(parts[0][1])) >= 0)
                parts = new[] { parts[0] + parts[1], parts[2] };
            if (parts.Length != 2) return false;
            try
            {
                azimuthRadians = ParseBearing(parts[0]);
                distance = double.Parse(parts[1], CultureInfo.InvariantCulture);
                return distance > 0;
            }
            catch (FormatException) { return false; }
        }

        /// <summary>"E,N" or "N,E label:E" style coordinate text ("500.00,1200.00"). Returns
        /// (Easting, Northing) to match FD-Draft's XY = (E, N) convention.</summary>
        public static bool TryParseCoordinate(string text, out double easting, out double northing)
        {
            easting = 0; northing = 0;
            var parts = text.Split(',');
            if (parts.Length != 2) return false;
            return double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out easting)
                && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out northing);
        }
    }
}
