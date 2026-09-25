using System;
using FdDraft.Core.Standards;

namespace FdDraft.View
{
    /// <summary>
    /// Where the main window opens. A fixed size is wrong on a laptop with display scaling (a
    /// 1920x1080 screen at 150 % is 1280x720 in window units, and the taskbar takes more), so the
    /// saved size and position are used only if they are still on screen, and are trimmed to fit.
    /// </summary>
    public static class WindowFit
    {
        /// <summary>
        /// The window rectangle to open with: <paramref name="saved"/> (from last time) trimmed to
        /// the <paramref name="workArea"/> (the screen minus the taskbar) when it still shows its
        /// title bar somewhere on the desktop (<paramref name="desktop"/>, every monitor), otherwise
        /// a default <paramref name="width"/> x <paramref name="height"/> fitted and centred in the
        /// work area. Rect is (left, top, right, bottom) in window units.
        /// </summary>
        public static Rect Place(Rect? saved, Rect workArea, Rect desktop, double width = 1440, double height = 900)
        {
            const double margin = 16;
            if (saved.HasValue && saved.Value.Width >= 200 && saved.Value.Height >= 150)
            {
                var s = saved.Value;
                // Enough of the title bar (the top 30 units) must be on the desktop to grab it.
                double visW = Math.Min(s.X2, desktop.X2) - Math.Max(s.X1, desktop.X1);
                bool titleOnScreen = visW >= 120 && s.Y1 >= desktop.Y1 - 5 && s.Y1 + 30 <= desktop.Y2;
                if (titleOnScreen)
                {
                    double w = Math.Min(s.Width, desktop.Width), h = Math.Min(s.Height, desktop.Height);
                    // On the screen with the taskbar, keep the bottom edge (and the command line)
                    // above it; move up first, then shrink if it's still too tall.
                    bool onWorkScreen = Math.Min(s.X1 + w, workArea.X2) - Math.Max(s.X1, workArea.X1) > 0;
                    double sTop = s.Y1;
                    if (onWorkScreen && sTop + h > workArea.Y2)
                    {
                        sTop = Math.Max(workArea.Y1, workArea.Y2 - h);
                        h = Math.Min(h, workArea.Y2 - sTop);
                    }
                    return new Rect(s.X1, sTop, s.X1 + w, sTop + h);
                }
            }
            double fw = Math.Min(width, workArea.Width - 2 * margin), fh = Math.Min(height, workArea.Height - 2 * margin);
            double left = workArea.X1 + (workArea.Width - fw) / 2, top = workArea.Y1 + (workArea.Height - fh) / 2;
            return new Rect(left, top, left + fw, top + fh);
        }
    }
}
