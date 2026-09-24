using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FdDraft.Core;
using FdDraft.Core.Job;
using FdDraft.Core.Standards;
using Microsoft.Win32;

namespace FdDraft.App
{
    /// <summary>
    /// "Draft FD-Pro job": pick the job, the firm template and standards, the plan
    /// type; see every sheet ranked with the scale it fits at and why; accept the
    /// top choice or pick another sheet and scale.
    /// </summary>
    public sealed class DraftDialog : Window
    {
        private readonly TextBox _job = new TextBox();
        private readonly TextBox _template = new TextBox();
        private readonly TextBox _standards = new TextBox();
        private readonly ComboBox _family = new ComboBox { MinWidth = 160 };
        private readonly ComboBox _scale = new ComboBox { MinWidth = 100 };
        private readonly ListView _ranking = new ListView { Height = 190 };
        private readonly TextBlock _info = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = System.Windows.Media.Brushes.DimGray };
        private FirmStandards? _std;

        public string JobFolder => _job.Text.Trim();
        public string TemplatePath => _template.Text.Trim();
        public string StandardsPath => _standards.Text.Trim();
        public string Family => (_family.SelectedItem as string) ?? "";
        public string? Layout => (_ranking.SelectedItem as RankRow)?.Layout;
        public string? Scale => _scale.SelectedItem as string;

        public sealed class RankRow
        {
            public string Layout { get; set; } = "";
            public string Scale { get; set; } = "";
            public string Legible { get; set; } = "";
            public string Reason { get; set; } = "";
        }

        public DraftDialog(AppSettings settings)
        {
            Title = "Draft FD-Pro job";
            Width = 820; SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            _job.Text = settings.LastJobFolder;
            _template.Text = settings.TemplatePath;
            _standards.Text = settings.StandardsPath;

            var grid = new Grid { Margin = new Thickness(14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int row = 0;
            void Row(string label, FrameworkElement field, Button? browse)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
                Grid.SetRow(l, row); grid.Children.Add(l);
                field.Margin = new Thickness(0, 4, 6, 4);
                Grid.SetRow(field, row); Grid.SetColumn(field, 1); grid.Children.Add(field);
                if (browse != null) { browse.Margin = new Thickness(0, 4, 0, 4); Grid.SetRow(browse, row); Grid.SetColumn(browse, 2); grid.Children.Add(browse); }
                row++;
            }

            Row("FD-Pro job folder", _job, Browse("Browse…", () =>
            {
                var d = new OpenFolderDialog { Title = "FD-Pro job folder (the one with points.csv)", InitialDirectory = Existing(_job.Text) };
                if (d.ShowDialog(this) == true) { _job.Text = d.FolderName; Rank(); }
            }));
            Row("Firm template (.dwt)", _template, Browse("Browse…", () =>
            {
                var d = new OpenFileDialog { Title = "Firm drawing template", Filter = "Drawing template (*.dwt)|*.dwt|Drawing (*.dwg)|*.dwg", InitialDirectory = Existing(Path.GetDirectoryName(_template.Text) ?? "") };
                if (d.ShowDialog(this) == true) _template.Text = d.FileName;
            }));
            Row("Standards (.ini)", _standards, Browse("Browse…", () =>
            {
                var d = new OpenFileDialog { Title = "FD-Draft standards for this template", Filter = "FD-Draft standards (*.ini)|*.ini", InitialDirectory = Existing(Path.GetDirectoryName(_standards.Text) ?? "") };
                if (d.ShowDialog(this) == true) { _standards.Text = d.FileName; LoadStandards(settings.Family); Rank(); }
            }));

            var typeRow = new StackPanel { Orientation = Orientation.Horizontal };
            typeRow.Children.Add(_family);
            var rank = new Button { Content = "Rank sheets", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(10, 0, 0, 0) };
            rank.Click += (s, e) => Rank();
            typeRow.Children.Add(rank);
            Row("Plan type", typeRow, null);
            _family.SelectionChanged += (s, e) => Rank();

            var view = new GridView();
            view.Columns.Add(Col("Sheet", "Layout", 110));
            view.Columns.Add(Col("Scale", "Scale", 70));
            view.Columns.Add(Col("Legible", "Legible", 60));
            view.Columns.Add(Col("Why", "Reason", 480));
            _ranking.View = view;
            _ranking.SelectionChanged += (s, e) =>
            {
                if (_ranking.SelectedItem is RankRow r) _scale.SelectedItem = r.Scale;
            };
            Row("Sheets", _ranking, null);

            var scaleRow = new StackPanel { Orientation = Orientation.Horizontal };
            scaleRow.Children.Add(_scale);
            scaleRow.Children.Add(new TextBlock { Text = "  pick another scale to override the sheet's own", VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.DimGray });
            Row("Scale", scaleRow, null);

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_info, row); Grid.SetColumn(_info, 1); grid.Children.Add(_info);
            row++;

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "Draft", IsDefault = true, Width = 90, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 90 };
            ok.Click += (s, e) =>
            {
                string? problem = !Directory.Exists(JobFolder) ? "Pick the FD-Pro job folder."
                    : !File.Exists(TemplatePath) ? "Pick the firm's .dwt template."
                    : !File.Exists(StandardsPath) ? "Pick the standards file for that template."
                    : Layout == null ? "Rank the sheets and pick one." : null;
                if (problem != null) { _info.Text = problem; return; }
                settings.LastJobFolder = JobFolder; settings.TemplatePath = TemplatePath; settings.StandardsPath = StandardsPath; settings.Family = Family;
                settings.Save();
                DialogResult = true;
            };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(buttons, row); Grid.SetColumnSpan(buttons, 3); grid.Children.Add(buttons);

            Content = grid;
            Loaded += (s, e) => { LoadStandards(settings.Family); Rank(); };
        }

        private static Button Browse(string text, Action onClick)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 2, 10, 2) };
            b.Click += (s, e) => onClick();
            return b;
        }

        private static GridViewColumn Col(string header, string path, double width) =>
            new GridViewColumn { Header = header, DisplayMemberBinding = new Binding(path), Width = width };

        private static string Existing(string dir) => !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : "";

        private void LoadStandards(string preferredFamily)
        {
            _std = null;
            _family.Items.Clear();
            _scale.Items.Clear();
            if (!File.Exists(StandardsPath)) return;
            try { _std = FirmStandards.Load(StandardsPath); }
            catch (IOException e) { _info.Text = e.Message; return; }
            foreach (var f in _std.Families) _family.Items.Add(f.Name);
            foreach (var s in _std.Scales) _scale.Items.Add(s.Label);
            var pick = _std.Family(string.IsNullOrEmpty(preferredFamily) ? null : preferredFamily);
            _family.SelectedItem = pick?.Name ?? (_family.Items.Count > 0 ? _family.Items[0] : null);
        }

        private void Rank()
        {
            _ranking.ItemsSource = null;
            if (_std == null) { _info.Text = "Pick the standards file to see the sheets."; return; }
            if (!Directory.Exists(JobFolder)) { _info.Text = "Pick the FD-Pro job folder to see which sheet and scale fit."; return; }
            try
            {
                var job = FdJobReader.Read(JobFolder);
                var result = DraftPipeline.Run(job, _std, Family);
                var rows = result.Ranked.Select(r => new RankRow
                {
                    Layout = r.Sheet.Layout,
                    Scale = r.Scale.Label,
                    Legible = (r.Legibility * 100).ToString("F0", CultureInfo.InvariantCulture) + "%",
                    Reason = r.Reason,
                }).ToList();
                _ranking.ItemsSource = rows;
                if (rows.Count > 0) _ranking.SelectedIndex = 0;
                var ext = result.Document.GeometryExtents();
                _info.Text = job.Settings.Name + ": " + job.Points.Count + " points, " + job.Figures.Count + " figures, "
                    + ext.Width.ToString("F1", CultureInfo.InvariantCulture) + " m x " + ext.Height.ToString("F1", CultureInfo.InvariantCulture) + " m"
                    + (result.Document.Warnings.Count > 0 ? ".  " + string.Join("  ", result.Document.Warnings.Take(3)) : ".");
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is InvalidOperationException)
            {
                _info.Text = e.Message;
            }
        }
    }
}
