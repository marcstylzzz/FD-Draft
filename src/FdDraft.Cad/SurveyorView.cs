using System;
using System.Collections.Generic;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using CSMath;
using FdDraft.Core.Geometry;

namespace FdDraft.Cad.Editing
{
    /// <summary>
    /// MSCAD's Surveyor View: the plan turned so north isn't up - on the sheets as well as on
    /// screen. Each sheet's plan viewport gets the twist (the same model point stays in the
    /// middle of it) and the north arrow on that sheet turns with it, so the plot shows the
    /// turned plan with a true north arrow. Coordinates, bearings and labels are untouched.
    /// World View is the same with no twist.
    /// </summary>
    public static class SurveyorView
    {
        /// <summary>The twist on the drawing now: the first sheet's plan viewport's, else 0.</summary>
        public static double CurrentTwist(CadDocument doc)
        {
            foreach (var l in doc.Layouts.Where(l => l.IsPaperSpace).OrderBy(l => l.TabOrder))
                if (SheetScale.PlanViewport(l) is Viewport vp) return vp.TwistAngle;
            return 0;
        }

        /// <summary>The model point at the middle of a viewport (its view centre is in the twisted frame).</summary>
        public static Vec2 ModelCenter(Viewport vp)
        {
            double c = Math.Cos(-vp.TwistAngle), s = Math.Sin(-vp.TwistAngle);
            double x = vp.ViewCenter.X, y = vp.ViewCenter.Y;
            return new Vec2(vp.ViewTarget.X + x * c - y * s, vp.ViewTarget.Y + x * s + y * c);
        }

        /// <summary>North arrows on a sheet: inserts of the standards' north-arrow block, or of any block named like one.</summary>
        public static List<Insert> NorthArrows(ACadSharp.Objects.Layout layout, string northArrowBlock)
        {
            bool IsArrow(string n) =>
                (northArrowBlock.Length > 0 && n.Equals(northArrowBlock, StringComparison.OrdinalIgnoreCase))
                || n.IndexOf("NORTH", StringComparison.OrdinalIgnoreCase) >= 0 || n.Equals("NARROW", StringComparison.OrdinalIgnoreCase);
            return layout.AssociatedBlock.Entities.OfType<Insert>().Where(i => i.Block != null && IsArrow(i.Block.Name)).ToList();
        }

        /// <summary>
        /// Turns every sheet's plan viewport to <paramref name="twist"/> (radians, counter-clockwise;
        /// 0 = north up) and each sheet's north arrow by the same change. One undo step; null when
        /// there's no sheet with a plan viewport (the screen view can still turn).
        /// </summary>
        public static IEditCommand? Apply(CadDocument doc, double twist, string northArrowBlock, out int sheets, out int arrows)
        {
            sheets = 0; arrows = 0;
            var cmds = new List<IEditCommand>();
            foreach (var layout in doc.Layouts.Where(l => l.IsPaperSpace))
            {
                var vp = SheetScale.PlanViewport(layout);
                if (vp == null) continue;
                double delta = twist - vp.TwistAngle;
                if (Math.Abs(Angles.Normalize2Pi(delta + Math.PI) - Math.PI) < 1e-12) continue;
                var mid = ModelCenter(vp);
                // Keep the same model point in the middle: its place in the new twisted frame.
                double c = Math.Cos(twist), s = Math.Sin(twist);
                double dx = mid.X - vp.ViewTarget.X, dy = mid.Y - vp.ViewTarget.Y;
                var newCenter = new XY(dx * c - dy * s, dx * s + dy * c);
                var v = vp;
                var oldState = (v.TwistAngle, v.ViewCenter);
                cmds.Add(new SetPropertyCommand<(double, XY)>(oldState, (twist, newCenter), st => { v.TwistAngle = st.Item1; v.ViewCenter = st.Item2; }, "Surveyor view"));
                sheets++;
                foreach (var arrow in NorthArrows(layout, northArrowBlock))
                {
                    var a = arrow;
                    cmds.Add(new SetPropertyCommand<double>(a.Rotation, Angles.Normalize2Pi(a.Rotation + delta), r => a.Rotation = r, "Surveyor view"));
                    arrows++;
                }
            }
            return cmds.Count == 0 ? null : new CompositeCommand(cmds, twist == 0 ? "World view" : "Surveyor view");
        }

        /// <summary>The twist that lays a line level, reading left to right.</summary>
        public static double TwistToLevel(Vec2 a, Vec2 b) => -Angles.ReadableRotation(a, b);

        /// <summary>The twist that points a grid azimuth (radians, clockwise from north) straight up.</summary>
        public static double TwistToPointUp(double azimuth)
        {
            double t = Angles.Normalize2Pi(azimuth);
            return t > Math.PI ? t - 2 * Math.PI : t;
        }
    }
}
