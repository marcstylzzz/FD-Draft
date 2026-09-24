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
        /// A quadrant bearing such as "N45-30-00E", "N45.5E", "N45d30'00"E", or a plain
        /// azimuth in decimal degrees ("125.5"). Returns the azimuth in radians, 0 = north,
        /// clockwise - the same convention as <see cref="FdDraft.Core.Geometry.Angles"/>.
        /// </summary>
        public static double ParseBearing(string text)
        {
            string s = text.Trim();
            if (s.Length == 0) throw new FormatException("empty bearing");
            char first = char.ToUpperInvariant(s[0]);
            char last = char.ToUpperInvariant(s[^1]);
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
                double az = (first, last) switch
                {
                    ('N', 'E') => angle,
                    ('S', 'E') => 180 - angle,
                    ('S', 'W') => 180 + angle,
                    _ => 360 - angle, // N.. W
                };
                return Normalize(az * Math.PI / 180.0);
            }
            // A plain azimuth in decimal degrees (0 = north, clockwise).
            return Normalize(double.Parse(s, CultureInfo.InvariantCulture) * Math.PI / 180.0);
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
            if (parts.Length < 2) return false;
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
