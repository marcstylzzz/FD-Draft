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
        /// <summary>The main window's last normal (not maximized) bounds; NaN = never saved.</summary>
        public double WindowLeft = double.NaN, WindowTop = double.NaN, WindowWidth = double.NaN, WindowHeight = double.NaN;
        /// <summary>Opens maximized the first time, then however it was left.</summary>
        public bool WindowMaximized = true;

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
                s.WindowLeft = ini.GetDouble("", "window_left", double.NaN);
                s.WindowTop = ini.GetDouble("", "window_top", double.NaN);
                s.WindowWidth = ini.GetDouble("", "window_width", double.NaN);
                s.WindowHeight = ini.GetDouble("", "window_height", double.NaN);
                s.WindowMaximized = ini.GetBool("", "window_maximized", true);
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
                File.WriteAllLines(FilePath, new List<string>
                {
                    "; FD-Draft user settings",
                    "template=" + TemplatePath,
                    "standards=" + StandardsPath,
                    "family=" + Family,
                    "last_job_folder=" + LastJobFolder,
                    "last_drawing_folder=" + LastDrawingFolder,
                    "snap=" + (Snap ? "true" : "false"),
                    "window_left=" + Num(WindowLeft),
                    "window_top=" + Num(WindowTop),
                    "window_width=" + Num(WindowWidth),
                    "window_height=" + Num(WindowHeight),
                    "window_maximized=" + (WindowMaximized ? "true" : "false"),
                });
            }
            catch (IOException) { /* settings are a convenience */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
