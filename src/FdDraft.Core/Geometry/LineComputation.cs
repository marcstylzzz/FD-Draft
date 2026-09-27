using System;

namespace FdDraft.Core.Geometry
{
    /// <summary>
    /// The numbers MSCAD's "CAD Line Computations" window shows for a picked line: bearing,
    /// from/to N E Z, horizontal and slope distance, the same scaled for output (grid to ground),
    /// % grade and delta Z.
    /// </summary>
    public sealed class LineComputation
    {
        public double FromN { get; }
        public double FromE { get; }
        public double FromZ { get; }
        public double ToN { get; }
        public double ToE { get; }
        public double ToZ { get; }
        /// <summary>Grid azimuth, radians clockwise from north.</summary>
        public double Azimuth { get; }
        public double Horizontal { get; }
        public double Slope { get; }
        public double DeltaZ => ToZ - FromZ;
        /// <summary>Rise over run in percent (0 for a zero-length line).</summary>
        public double GradePercent => Horizontal > 1e-12 ? DeltaZ / Horizontal * 100 : 0;
        /// <summary>The factor distances are multiplied by for output (1 / the job's scale factor).</summary>
        public double OutputScale { get; }
        public double ScaledHorizontal => Horizontal * OutputScale;
        public double ScaledSlope => Slope * OutputScale;

        public LineComputation(double fromE, double fromN, double fromZ, double toE, double toN, double toZ, double outputScale = 1.0)
        {
            FromE = fromE; FromN = fromN; FromZ = fromZ; ToE = toE; ToN = toN; ToZ = toZ;
            OutputScale = outputScale > 0 ? outputScale : 1.0;
            double de = toE - fromE, dn = toN - fromN, dz = toZ - fromZ;
            Horizontal = Math.Sqrt(de * de + dn * dn);
            Slope = Math.Sqrt(de * de + dn * dn + dz * dz);
            Azimuth = Angles.Azimuth(new Vec2(fromE, fromN), new Vec2(toE, toN));
        }
    }
}
