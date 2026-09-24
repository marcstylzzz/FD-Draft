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
            }
            catch (IOException) { /* defaults */ }
            return s;
        }

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
                });
            }
            catch (IOException) { /* settings are a convenience */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
