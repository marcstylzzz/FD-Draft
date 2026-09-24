using System;
using System.Collections.Generic;
using System.IO;
using FdDraft.Core.Standards;

namespace FdDraft.AutoCAD
{
    /// <summary>
    /// Per-user settings, remembered between sessions in
    /// %APPDATA%\FD-Draft\fd-draft.ini: which template and standards file to use,
    /// the last sheet family and the last job folder.
    /// </summary>
    internal sealed class PluginSettings
    {
        public string TemplatePath { get; set; } = "";
        public string StandardsPath { get; set; } = "";
        public string Family { get; set; } = "";
        public string LastJobFolder { get; set; } = "";
        public bool PlotPdf { get; set; } = true;
        public bool DeleteOtherLayouts { get; set; } = true;

        public static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FD-Draft", "fd-draft.ini");

        public static PluginSettings Load()
        {
            var s = new PluginSettings();
            if (!File.Exists(FilePath)) return s;
            var ini = IniFile.Parse(File.ReadAllLines(FilePath));
            s.TemplatePath = ini.GetString("", "template", "");
            s.StandardsPath = ini.GetString("", "standards", "");
            s.Family = ini.GetString("", "family", "");
            s.LastJobFolder = ini.GetString("", "last_job_folder", "");
            s.PlotPdf = ini.GetBool("", "plot_pdf", true);
            s.DeleteOtherLayouts = ini.GetBool("", "delete_other_layouts", true);
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllLines(FilePath, new List<string>
            {
                "; FD-Draft user settings - FDDRAFTSETUP changes these",
                "template=" + TemplatePath,
                "standards=" + StandardsPath,
                "family=" + Family,
                "last_job_folder=" + LastJobFolder,
                "plot_pdf=" + (PlotPdf ? "true" : "false"),
                "delete_other_layouts=" + (DeleteOtherLayouts ? "true" : "false"),
            });
        }
    }
}
