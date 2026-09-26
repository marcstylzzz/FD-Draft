using System;
using System.Collections.Generic;
using System.IO;
using FdDraft.Core.Standards;

namespace FdDraft.App
{
    /// <summary>Per-user settings in %APPDATA%\FD-Draft\app.ini.</summary>
    public sealed class AppSettings
    {
        public string TemplatePath = "";
        public string StandardsPath = "";
        public string Family = "";
        public string LastJobFolder = "";
        public string LastDrawingFolder = "";
        public bool Snap = true;
        /// <summary>Object snap modes on (FdDraft.View.SnapModes flags); -1 = the default set.</summary>
        public int SnapModes = -1;
        /// <summary>The folder a .ctb was last browsed from - searched for plot style tables.</summary>
        public string PlotStyleFolder = "";
        /// <summary>The main window's last normal (not maximized) bounds; NaN = never saved.</summary>
        public double WindowLeft = double.NaN, WindowTop = double.NaN, WindowWidth = double.NaN, WindowHeight = double.NaN;
        /// <summary>Opens maximized the first time, then however it was left.</summary>
        public bool WindowMaximized = true;
        /// <summary>Toolbar rows, places and visibility: "key:band:index:visible;..." (empty = defaults).</summary>
        public string ToolbarLayout = "";
        /// <summary>Arrowhead size on paper (mm) for leaders, ties and arrows (MSCAD's Leader Scale).</summary>
        public double ArrowMm = 2.5;
        /// <summary>Toolbar icon size in device-independent pixels (12 small, 16 medium, 24 large).</summary>
        public int ToolbarIconSize = 16;
        /// <summary>The icon size <see cref="ToolbarLayout"/> was saved at; a layout saved at another size is re-packed.</summary>
        public int ToolbarLayoutSize = 0;
        /// <summary>Paper height (mm) for new TEXT set by a Leroy button; 0 = none chosen yet.</summary>
        public double TextMm = 0;
        /// <summary>Saved layer states: name -> "layer=flags|layer=flags" (flags: o on, f frozen, l locked).</summary>
        public Dictionary<string, string> LayerStates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FD-Draft", "app.ini");

        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var ini = IniFile.Parse(File.ReadAllLines(FilePath));
                s.TemplatePath = ini.GetString("", "template", "");
                s.StandardsPath = ini.GetString("", "standards", "");
                s.Family = ini.GetString("", "family", "");
                s.LastJobFolder = ini.GetString("", "last_job_folder", "");
                s.LastDrawingFolder = ini.GetString("", "last_drawing_folder", "");
                s.Snap = ini.GetBool("", "snap", true);
                s.SnapModes = ini.GetInt("", "snap_modes", -1);
                s.PlotStyleFolder = ini.GetString("", "plot_style_folder", "");
                s.WindowLeft = ini.GetDouble("", "window_left", double.NaN);
                s.WindowTop = ini.GetDouble("", "window_top", double.NaN);
                s.WindowWidth = ini.GetDouble("", "window_width", double.NaN);
                s.WindowHeight = ini.GetDouble("", "window_height", double.NaN);
                s.WindowMaximized = ini.GetBool("", "window_maximized", true);
                s.ToolbarLayout = ini.GetString("", "toolbar_layout", "");
                s.ArrowMm = ini.GetDouble("", "arrow_mm", 2.5);
                s.ToolbarIconSize = ini.GetInt("", "toolbar_icon_size", 16);
                if (s.ToolbarIconSize != 12 && s.ToolbarIconSize != 16 && s.ToolbarIconSize != 24) s.ToolbarIconSize = 16;
                s.ToolbarLayoutSize = ini.GetInt("", "toolbar_layout_size", 0);
                s.TextMm = ini.GetDouble("", "text_mm", 0);
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    const string prefix = "layer_state.";
                    if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    int eq = line.IndexOf('=');
                    if (eq > prefix.Length) s.LayerStates[line.Substring(prefix.Length, eq - prefix.Length)] = line.Substring(eq + 1);
                }
            }
            catch (IOException) { /* defaults */ }
            return s;
        }

        private static string Num(double v) => double.IsNaN(v) ? "" : v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The saved normal window bounds, if there are any.</summary>
        public Rect? WindowBounds =>
            double.IsNaN(WindowLeft) || double.IsNaN(WindowTop) || double.IsNaN(WindowWidth) || double.IsNaN(WindowHeight)
                ? (Rect?)null : new Rect(WindowLeft, WindowTop, WindowLeft + WindowWidth, WindowTop + WindowHeight);

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var lines = new List<string>
                {
                    "; FD-Draft user settings",
                    "template=" + TemplatePath,
                    "standards=" + StandardsPath,
                    "family=" + Family,
                    "last_job_folder=" + LastJobFolder,
                    "last_drawing_folder=" + LastDrawingFolder,
                    "snap=" + (Snap ? "true" : "false"),
                    "snap_modes=" + SnapModes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "plot_style_folder=" + PlotStyleFolder,
                    "window_left=" + Num(WindowLeft),
                    "window_top=" + Num(WindowTop),
                    "window_width=" + Num(WindowWidth),
                    "window_height=" + Num(WindowHeight),
                    "window_maximized=" + (WindowMaximized ? "true" : "false"),
                    "toolbar_layout=" + ToolbarLayout,
                    "toolbar_icon_size=" + ToolbarIconSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "toolbar_layout_size=" + ToolbarLayoutSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "arrow_mm=" + ArrowMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                    "text_mm=" + TextMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                };
                foreach (var kv in LayerStates) lines.Add("layer_state." + kv.Key + "=" + kv.Value);
                File.WriteAllLines(FilePath, lines);
            }
            catch (IOException) { /* settings are a convenience */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
