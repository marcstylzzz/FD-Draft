using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ACadSharp;
using FdDraft.Cad;
using FdDraft.View;
using Microsoft.Win32;
using CoreRect = FdDraft.Core.Standards.Rect;

namespace FdDraft.App
{
    public enum PlotDialogAction { Cancel, Print, PickWindow }

    /// <summary>
    /// What the Print dialog remembers between openings in a session - the chosen printer,
    /// paper, plot style table and every setting - so "Select Print Area" can close it to pick a
    /// window and reopen it exactly as it was.
    /// </summary>
    public sealed class PlotDialogState
    {
        public PlotSetup Setup { get; set; } = new PlotSetup();
        public string Printer { get; set; } = LayoutPlotSetup.PdfPrinter;
        /// <summary>Full path of the .ctb, a built-in's name, or null for none.</summary>
        public string? StyleTable { get; set; }
        public bool SaveToLayout { get; set; } = true;
        /// <summary>The sheet these settings were last loaded for.</summary>
        public string Sheet { get; set; } = "";
    }

    /// <summary>
    /// The Print dialog, laid out like AutoCAD's: page setup, printer, paper, copies, plot
    /// style table (.ctb), what to plot, scale, offset, lineweight/style options and
    /// orientation, with Preview and Apply to Layout.
    /// </summary>
    public sealed class PlotDialog : Window
    {
        public const string NoTable = "None";
        public const string BuiltInMono = "monochrome.ctb (built in)";
        public const string BuiltInGray = "grayscale.ctb (built in)";
        private const string Browse = "Browse for a .ctb…";

        private readonly CadDocument _doc;
        private readonly string _sheet;
        private readonly Scene _scene;
        private readonly CoreRect _display;
        private readonly AppSettings _settings;
        public PlotDialogState State { get; }
        public PlotDialogAction Action { get; private set; } = PlotDialogAction.Cancel;
        /// <summary>Set when Apply to Layout changed the drawing (the window marks it unsaved).</summary>
        public bool LayoutChanged { get; private set; }

        private readonly ComboBox _pageSetup = new ComboBox();
        private readonly ComboBox _printer = new ComboBox();
        private readonly TextBlock _pageInfo = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly ComboBox _paper = new ComboBox();
        private readonly TextBox _copies = new TextBox { Width = 50 };
        private readonly ComboBox _styles = new ComboBox();
        private readonly TextBlock _stylesInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11 };
        private readonly ComboBox _area = new ComboBox { Width = 130 };
        private readonly Button _pickWindow = new Button { Content = "Select Print Area <", Padding = new Thickness(8, 2, 8, 2) };
        private readonly TextBlock _windowInfo = new TextBlock { Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        private readonly CheckBox _fit = new CheckBox { Content = "Fit to paper" };
        private readonly ComboBox _scale = new ComboBox { Width = 130 };
        private readonly TextBox _mm = new TextBox { Width = 70 };
        private readonly TextBox _units = new TextBox { Width = 70 };
        private readonly TextBlock _unitName = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        private readonly CheckBox _center = new CheckBox { Content = "Center on page" };
        private readonly TextBox _offX = new TextBox { Width = 90 };
        private readonly TextBox _offY = new TextBox { Width = 90 };
        private readonly CheckBox _lineweights = new CheckBox { Content = "Print object lineweights" };
        private readonly CheckBox _withStyles = new CheckBox { Content = "Print with plot styles" };
        private readonly CheckBox _scaleLw = new CheckBox { Content = "Scale lineweights" };
        private readonly CheckBox _saveToLayout = new CheckBox { Content = "Save changes to layout" };
        private readonly RadioButton _portrait = new RadioButton { Content = "Portrait", GroupName = "orient" };
        private readonly RadioButton _landscape = new RadioButton { Content = "Landscape", GroupName = "orient" };
        private readonly CheckBox _upside = new CheckBox { Content = "Print upside-down" };
        private readonly TextBlock _notes = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

        private List<PaperSize> _papers = new List<PaperSize>();
        private readonly List<string> _tables;
        private double _margin;
        /// <summary>Printer margins already asked for - the spooler is slow to answer.</summary>
        private readonly Dictionary<(string, string), double> _margins = new Dictionary<(string, string), double>();
        private bool _loading;

        /// <summary>Standard plan scales offered in the scale box (1:n).</summary>
        private static readonly int[] Scales = { 1, 2, 5, 10, 20, 25, 50, 100, 150, 200, 250, 300, 400, 500, 600, 750, 1000, 1250, 1500, 2000, 2500, 5000, 10000 };

        private bool IsSheet => _sheet != "Model";

        public PlotDialog(CadDocument doc, string sheet, Scene scene, CoreRect display, AppSettings settings, PlotDialogState state)
        {
            _doc = doc; _sheet = sheet; _scene = scene; _display = display; _settings = settings; State = state;
            Title = "Print - " + sheet;
            Width = 900; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI");
            _tables = PlotStyleLibrary.Find(new[] { settings.PlotStyleFolder, Path.GetDirectoryName(settings.TemplatePath) ?? "" });

            Content = Build();
            FillPrinters();
            FillPageSetups();
            FillStyleTables();
            _area.Items.Clear();
            if (IsSheet) _area.Items.Add("Layout");
            foreach (var a in new[] { "Extents", "Display", "Window" }) _area.Items.Add(a);

            // First opening for this sheet: start from the sheet's own saved page setup.
            if (state.Sheet != sheet) LoadFromLayout(sheet);
            Show(State);
            Wire();
        }

        // ---- layout of the dialog -------------------------------------------------------------

        private static GroupBox Box(string header, UIElement content) =>
            new GroupBox { Header = header, Content = content, Margin = new Thickness(4), Padding = new Thickness(6) };

        private static StackPanel Row(params UIElement[] items)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            foreach (var i in items)
            {
                if (i is FrameworkElement fe && fe.Margin == default) fe.Margin = new Thickness(0, 0, 6, 0);
                p.Children.Add(i);
            }
            return p;
        }

        private static TextBlock Label(string s, double width = 0) =>
            new TextBlock { Text = s, VerticalAlignment = VerticalAlignment.Center, Width = width > 0 ? width : double.NaN };

        private UIElement Build()
        {
            _pageSetup.Width = 300; _printer.Width = 300; _paper.Width = 360; _styles.Width = 250;
            var left = new StackPanel();
            left.Children.Add(Box("Page setup", Row(Label("Name:", 60), _pageSetup)));
            var printer = new StackPanel();
            printer.Children.Add(Row(Label("Name:", 60), _printer));
            printer.Children.Add(_pageInfo);
            left.Children.Add(Box("Printer / plotter", printer));
            var paperRow = new Grid();
            paperRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            paperRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var paperBox = Box("Paper size", _paper);
            var copiesBox = Box("Number of copies", _copies);
            Grid.SetColumn(copiesBox, 1);
            paperRow.Children.Add(paperBox); paperRow.Children.Add(copiesBox);
            left.Children.Add(paperRow);

            var lower = new Grid();
            lower.ColumnDefinitions.Add(new ColumnDefinition());
            lower.ColumnDefinitions.Add(new ColumnDefinition());
            var scale = new StackPanel();
            scale.Children.Add(Row(_fit));
            scale.Children.Add(Row(Label("Scale:", 45), _scale));
            scale.Children.Add(Row(_mm, Label("mm  ="), _units, _unitName));
            var scaleBox = Box("Print scale", scale);
            var area = new StackPanel();
            area.Children.Add(Row(Label("What to print:"), _area));
            area.Children.Add(Row(_pickWindow));
            area.Children.Add(_windowInfo);
            var areaBox = Box("Print area", area);
            Grid.SetColumn(areaBox, 1);
            lower.Children.Add(scaleBox); lower.Children.Add(areaBox);
            left.Children.Add(lower);
            var offset = new StackPanel();
            offset.Children.Add(Row(_center));
            offset.Children.Add(Row(Label("X:", 20), _offX, Label("mm"), Label("   Y:", 30), _offY, Label("mm")));
            left.Children.Add(Box("Print offset (from the printable area's corner)", offset));

            var right = new StackPanel();
            var styles = new StackPanel();
            var browse = new Button { Content = "…", Width = 28, ToolTip = "Browse for a .ctb file" };
            browse.Click += (s, e) => BrowseForTable();
            styles.Children.Add(Row(_styles, browse));
            styles.Children.Add(_stylesInfo);
            right.Children.Add(Box("Print style table (pen assignments)", styles));
            var opts = new StackPanel();
            foreach (var c in new UIElement[] { _lineweights, _withStyles, _scaleLw, _saveToLayout }) opts.Children.Add(Row(c));
            right.Children.Add(Box("Print options", opts));
            var orient = new StackPanel();
            foreach (var c in new UIElement[] { _portrait, _landscape, _upside }) orient.Children.Add(Row(c));
            right.Children.Add(Box("Drawing orientation", orient));
            right.Children.Add(_notes);

            var body = new Grid { Margin = new Thickness(8) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(right, 1);
            body.Children.Add(left); body.Children.Add(right);

            Button B(string text, RoutedEventHandler h, bool isDefault = false, bool isCancel = false)
            {
                var b = new Button { Content = text, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(6, 0, 0, 0), IsDefault = isDefault, IsCancel = isCancel };
                b.Click += h;
                return b;
            }
            var apply = B("Apply to Layout", (s, e) => ApplyToLayout(announce: true));
            apply.IsEnabled = IsSheet;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 0, 8, 8) };
            buttons.Children.Add(apply);
            buttons.Children.Add(B("Preview…", (s, e) => PreviewPlot()));
            buttons.Children.Add(B("OK", (s, e) => Finish(PlotDialogAction.Print), isDefault: true));
            buttons.Children.Add(B("Cancel", (s, e) => { Action = PlotDialogAction.Cancel; DialogResult = false; }, isCancel: true));

            var root = new DockPanel();
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            root.Children.Add(body);
            return root;
        }

        private void Wire()
        {
            _printer.SelectionChanged += (s, e) => { if (!_loading) { FillPapers(keepName: State.Setup.PaperName); Refresh(); } };
            _paper.SelectionChanged += (s, e) => { if (!_loading) Refresh(); };
            _styles.SelectionChanged += (s, e) => { if (!_loading) { if (_styles.SelectedItem as string == Browse) BrowseForTable(); else Refresh(); } };
            _pageSetup.SelectionChanged += (s, e) => { if (!_loading && _pageSetup.SelectedItem is string name) { LoadFromLayout(PageSetupLayout(name)); Show(State); } };
            _area.SelectionChanged += (s, e) => { if (!_loading) Refresh(); };
            _scale.SelectionChanged += (s, e) =>
            {
                if (_loading || !(_scale.SelectedItem is string t)) return;
                var n = ParseRatio(t);
                if (n.HasValue) { _loading = true; _mm.Text = "1"; _units.Text = Num(UnitsPerMm(n.Value)); _loading = false; }
                Refresh();
            };
            foreach (var tb in new[] { _mm, _units, _offX, _offY, _copies }) tb.TextChanged += (s, e) => { if (!_loading) Refresh(); };
            foreach (var cb in new[] { _fit, _center, _lineweights, _withStyles, _scaleLw, _upside }) { cb.Checked += (s, e) => { if (!_loading) Refresh(); }; cb.Unchecked += (s, e) => { if (!_loading) Refresh(); }; }
            _portrait.Checked += (s, e) => { if (!_loading) Refresh(); };
            _landscape.Checked += (s, e) => { if (!_loading) Refresh(); };
            _pickWindow.Click += (s, e) => Finish(PlotDialogAction.PickWindow);
        }

        // ---- lists ------------------------------------------------------------------------------

        private void FillPrinters()
        {
            _printer.Items.Clear();
            _printer.Items.Add(LayoutPlotSetup.PdfPrinter);
            foreach (var n in PlotPrinters.Names()) _printer.Items.Add(n);
        }

        private const string OwnSetup = "<This sheet's own setup>";

        /// <summary>Other sheets' page setups can be borrowed (e.g. plot 11X17 the way 22X34 is set up).</summary>
        private void FillPageSetups()
        {
            _pageSetup.Items.Clear();
            _pageSetup.Items.Add(IsSheet ? OwnSetup : "<Model>");
            foreach (var l in _doc.Layouts.Where(l => l.IsPaperSpace && !l.Name.Equals(_sheet, StringComparison.OrdinalIgnoreCase)).OrderBy(l => l.TabOrder))
                _pageSetup.Items.Add("Same as " + l.Name);
            _pageSetup.SelectedIndex = 0;
        }

        private string PageSetupLayout(string item) => item.StartsWith("Same as ", StringComparison.Ordinal) ? item.Substring(8) : _sheet;

        private void FillStyleTables()
        {
            _styles.Items.Clear();
            _styles.Items.Add(NoTable);
            _styles.Items.Add(BuiltInMono);
            _styles.Items.Add(BuiltInGray);
            foreach (var t in _tables) _styles.Items.Add(Path.GetFileName(t));
            _styles.Items.Add(Browse);
        }

        private void FillPapers(string? keepName)
        {
            bool wasLoading = _loading;
            _loading = true;
            string printer = _printer.SelectedItem as string ?? LayoutPlotSetup.PdfPrinter;
            _papers = printer == LayoutPlotSetup.PdfPrinter ? new List<PaperSize>() : PlotPrinters.PaperSizes(printer);
            if (_papers.Count == 0) _papers = PaperSize.Standard.ToList();
            // A sheet's own paper size first, so "plot this 22x34 at 1:1" is one click.
            var l = IsSheet ? _doc.Layouts.FirstOrDefault(x => x.Name.Equals(_sheet, StringComparison.OrdinalIgnoreCase)) : null;
            if (l != null && l.PaperWidth > 0 && l.PaperHeight > 0 && !_papers.Any(p => Math.Abs(p.WidthMm - Math.Min(l.PaperWidth, l.PaperHeight)) < 1.5 && Math.Abs(p.HeightMm - Math.Max(l.PaperWidth, l.PaperHeight)) < 1.5))
                _papers.Insert(0, new PaperSize(string.Format(CultureInfo.InvariantCulture, "Sheet size ({0:0.#} x {1:0.#} mm)", Math.Min(l.PaperWidth, l.PaperHeight), Math.Max(l.PaperWidth, l.PaperHeight)), l.PaperWidth, l.PaperHeight));
            _paper.Items.Clear();
            foreach (var p in _papers) _paper.Items.Add(p);
            var wanted = State.Setup;
            _paper.SelectedItem = _papers.FirstOrDefault(p => p.Name == keepName)
                ?? _papers.FirstOrDefault(p => Math.Abs(p.WidthMm - Math.Min(wanted.PaperWidthMm, wanted.PaperHeightMm)) < 1.5 && Math.Abs(p.HeightMm - Math.Max(wanted.PaperWidthMm, wanted.PaperHeightMm)) < 1.5)
                ?? _papers[0];
            _loading = wasLoading;
        }

        // ---- state <-> controls -------------------------------------------------------------------

        /// <summary>Loads a sheet's saved page setup (from the DWG) into the state.</summary>
        private void LoadFromLayout(string sheet)
        {
            var l = _doc.Layouts.FirstOrDefault(x => x.Name.Equals(sheet, StringComparison.OrdinalIgnoreCase) && x.IsPaperSpace);
            State.Sheet = _sheet;
            if (l == null)
            {
                // Model space: fit the extents unless a scale was already chosen this session.
                State.Setup.Area = State.Setup.Area == PlotArea.Layout ? PlotArea.Extents : State.Setup.Area;
                return;
            }
            var s = LayoutPlotSetup.Read(l, out var ctb, out var printer);
            // A sheet that was never set up to plot defaults to its own paper at 1:1.
            if (printer == null || s.PaperWidthMm <= 0)
            {
                s.Area = PlotArea.Layout; s.MmPerUnit = 1; s.FitToPaper = false;
            }
            if (!IsSheet && s.Area == PlotArea.Layout) s.Area = PlotArea.Extents;
            State.Setup = s;
            if (printer != null && (printer == LayoutPlotSetup.PdfPrinter || PlotPrinters.Names().Contains(printer))) State.Printer = printer;
            State.StyleTable = ctb == null ? State.StyleTable : ResolveTable(ctb);
        }

        /// <summary>A .ctb name saved in a DWG, found among the tables on this PC (or the built-ins).</summary>
        private string? ResolveTable(string name)
        {
            var found = _tables.FirstOrDefault(t => Path.GetFileName(t).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
            if (name.Equals("monochrome.ctb", StringComparison.OrdinalIgnoreCase)) return BuiltInMono;
            if (name.Equals("grayscale.ctb", StringComparison.OrdinalIgnoreCase)) return BuiltInGray;
            _notes.Text = "This sheet is set up for \"" + name + "\", which isn't on this PC - pick it with the … button.";
            return State.StyleTable;
        }

        private void Show(PlotDialogState st)
        {
            _loading = true;
            var s = st.Setup;
            _printer.SelectedItem = _printer.Items.Contains(st.Printer) ? st.Printer : LayoutPlotSetup.PdfPrinter;
            FillPapers(s.PaperName);
            _copies.Text = Math.Max(1, s.Copies).ToString(CultureInfo.InvariantCulture);
            _styles.SelectedItem = st.StyleTable == null ? NoTable
                : st.StyleTable == BuiltInMono || st.StyleTable == BuiltInGray ? st.StyleTable
                : _styles.Items.Contains(Path.GetFileName(st.StyleTable)) ? Path.GetFileName(st.StyleTable) : NoTable;
            string area = s.Area.ToString();
            _area.SelectedItem = _area.Items.Contains(area) ? area : _area.Items[0];
            _fit.IsChecked = s.FitToPaper;
            _mm.Text = "1";
            _units.Text = Num(s.MmPerUnit > 0 ? 1 / s.MmPerUnit : 1);
            _unitName.Text = IsSheet ? "mm on the sheet" : "m";
            FillScales();
            _center.IsChecked = s.Center;
            _offX.Text = Num(s.OffsetXMm); _offY.Text = Num(s.OffsetYMm);
            _lineweights.IsChecked = s.PlotLineweights;
            _withStyles.IsChecked = s.PlotWithStyles;
            _scaleLw.IsChecked = s.ScaleLineweights;
            _saveToLayout.IsChecked = st.SaveToLayout && IsSheet;
            _saveToLayout.IsEnabled = IsSheet;
            _landscape.IsChecked = s.Landscape; _portrait.IsChecked = !s.Landscape;
            _upside.IsChecked = s.UpsideDown;
            _loading = false;
            Refresh();
        }

        private void FillScales()
        {
            _scale.Items.Clear();
            foreach (var n in Scales) _scale.Items.Add("1:" + n);
            _scale.Items.Add("Custom");
            double upm = ParseD(_units.Text) / Math.Max(ParseD(_mm.Text), 1e-12);
            var match = Scales.FirstOrDefault(n => Math.Abs(UnitsPerMm(n) - upm) < 1e-9 * Math.Max(1, upm));
            _scale.SelectedItem = match > 0 ? "1:" + match : "Custom";
        }

        /// <summary>Drawing units per paper mm at 1:n - n on a sheet (mm), n/1000 in model space (m).</summary>
        private double UnitsPerMm(int n) => IsSheet ? n : n / 1000.0;

        private static int? ParseRatio(string t) =>
            t.StartsWith("1:", StringComparison.Ordinal) && int.TryParse(t.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : (int?)null;

        private static double ParseD(string t) => double.TryParse(t.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
        private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>The controls as a setup (and the state updated to match).</summary>
        private PlotSetup Collect()
        {
            var paper = _paper.SelectedItem as PaperSize ?? PaperSize.Standard[0];
            string printer = _printer.SelectedItem as string ?? LayoutPlotSetup.PdfPrinter;
            var key = (printer, paper.Name);
            if (!_margins.TryGetValue(key, out _margin))
                _margins[key] = _margin = printer == LayoutPlotSetup.PdfPrinter ? 0 : PlotPrinters.Margin(printer, paper);
            double mm = ParseD(_mm.Text), units = ParseD(_units.Text);
            var area = Enum.TryParse<PlotArea>(_area.SelectedItem as string ?? "Extents", out var a) ? a : PlotArea.Extents;
            var s = new PlotSetup
            {
                PaperWidthMm = paper.WidthMm, PaperHeightMm = paper.HeightMm, PaperName = paper.Name,
                Landscape = _landscape.IsChecked == true, UpsideDown = _upside.IsChecked == true,
                MarginLeft = _margin, MarginRight = _margin, MarginTop = _margin, MarginBottom = _margin,
                Area = area,
                Region = area == PlotArea.Window ? State.Setup.Region : area == PlotArea.Display ? _display : (CoreRect?)null,
                FitToPaper = _fit.IsChecked == true,
                MmPerUnit = mm > 0 && units > 0 ? mm / units : State.Setup.MmPerUnit,
                Center = _center.IsChecked == true,
                OffsetXMm = double.IsNaN(ParseD(_offX.Text)) ? 0 : ParseD(_offX.Text),
                OffsetYMm = double.IsNaN(ParseD(_offY.Text)) ? 0 : ParseD(_offY.Text),
                PlotLineweights = _lineweights.IsChecked == true,
                PlotWithStyles = _withStyles.IsChecked == true,
                ScaleLineweights = _scaleLw.IsChecked == true,
                Copies = int.TryParse(_copies.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int c) && c > 0 ? Math.Min(c, 99) : 1,
                Styles = LoadTable(),
            };
            State.Setup = s;
            State.Printer = printer;
            State.SaveToLayout = _saveToLayout.IsChecked == true;
            return s;
        }

        private PlotStyleTable? LoadTable()
        {
            string? item = _styles.SelectedItem as string;
            if (item == null || item == NoTable || item == Browse) { State.StyleTable = null; return null; }
            if (item == BuiltInMono) { State.StyleTable = BuiltInMono; return PlotStyleTable.Monochrome(); }
            if (item == BuiltInGray) { State.StyleTable = BuiltInGray; return PlotStyleTable.Grayscale(); }
            var path = _tables.FirstOrDefault(t => Path.GetFileName(t) == item) ?? (State.StyleTable != null && Path.GetFileName(State.StyleTable) == item ? State.StyleTable : null);
            if (path == null) return null;
            State.StyleTable = path;
            try { return PlotStyleTable.Load(path); }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException)
            {
                _stylesInfo.Text = "Couldn't read " + item + ": " + e.Message;
                return null;
            }
        }

        private void BrowseForTable()
        {
            var d = new OpenFileDialog { Title = "Plot style table", Filter = "Plot style tables (*.ctb)|*.ctb", InitialDirectory = Directory.Exists(_settings.PlotStyleFolder) ? _settings.PlotStyleFolder : "" };
            if (d.ShowDialog(this) == true)
            {
                _settings.PlotStyleFolder = Path.GetDirectoryName(d.FileName) ?? "";
                _settings.Save();
                if (!_tables.Contains(d.FileName)) { _tables.Add(d.FileName); FillStyleTables(); }
                State.StyleTable = d.FileName;
                _loading = true;
                _styles.SelectedItem = Path.GetFileName(d.FileName);
                _loading = false;
            }
            else
            {
                _loading = true;
                _styles.SelectedItem = State.StyleTable == null ? NoTable : _styles.Items.Contains(Path.GetFileName(State.StyleTable)) ? Path.GetFileName(State.StyleTable) : State.StyleTable;
                _loading = false;
            }
            Refresh();
        }

        /// <summary>Keeps the enabled states, the info lines and the fit warning current.</summary>
        private void Refresh()
        {
            var s = Collect();
            bool pdf = State.Printer == LayoutPlotSetup.PdfPrinter;
            _pageInfo.Text = string.Format(CultureInfo.InvariantCulture,
                "{0}\nPage size: {1:0.#} x {2:0.#} mm    Printable area: {3:0.#} x {4:0.#} mm",
                pdf ? "FD-Draft's own vector PDF - saved as a file, true scale, no printer driver." : "Windows printer (" + (s.Copies > 1 ? s.Copies + " copies" : "1 copy") + ").",
                s.PageWidth, s.PageHeight, s.PageWidth - 2 * _margin, s.PageHeight - 2 * _margin);
            _copies.IsEnabled = !pdf;
            _scale.IsEnabled = _mm.IsEnabled = _units.IsEnabled = !s.FitToPaper;
            _offX.IsEnabled = _offY.IsEnabled = !s.Center;
            _pickWindow.IsEnabled = true;
            _windowInfo.Text = s.Area == PlotArea.Window
                ? (State.Setup.Region is CoreRect r ? string.Format(CultureInfo.InvariantCulture, "window {0:0.##},{1:0.##} to {2:0.##},{3:0.##}", r.X1, r.Y1, r.X2, r.Y2) : "no window picked yet - Select Print Area")
                : s.Area == PlotArea.Display ? "what's on screen now" : "";
            _stylesInfo.Text = s.Styles == null ? (_withStyles.IsChecked == true ? "No table: objects plot in their own colours." : "")
                : (s.Styles.Description.Length > 0 ? s.Styles.Description : s.Styles.Name);
            if (s.FitToPaper && _scale.IsEnabled == false)
            {
                var page = PlotComposer.Compose(_scene, s);
                _loading = true;
                _mm.Text = "1"; _units.Text = Num(1 / page.MmPerUnit);
                _scale.SelectedItem = "Custom";
                _loading = false;
            }
            var notes = PlotComposer.Compose(_scene, s).Notes;
            _notes.Text = s.Area == PlotArea.Window && !s.Region.HasValue ? "Pick the window to print first (Select Print Area)." : string.Join("\n", notes);
        }

        // ---- actions --------------------------------------------------------------------------------

        private void PreviewPlot()
        {
            var s = Collect();
            if (s.Area == PlotArea.Window && !s.Region.HasValue) { _notes.Text = "Pick the window to print first (Select Print Area)."; return; }
            var page = PlotComposer.Compose(_scene, s);
            var printable = new CoreRect(_margin, _margin, s.PageWidth - _margin, s.PageHeight - _margin);
            var info = new TextBlock
            {
                Margin = new Thickness(8, 4, 8, 4),
                Text = string.Format(CultureInfo.InvariantCulture, "{0} on {1} {2}, {3}   ·   {4}   ·   Esc closes",
                    _sheet, s.PaperName, s.Landscape ? "landscape" : "portrait",
                    s.FitToPaper ? "fit (1 mm = " + Num(1 / page.MmPerUnit) + " units)" : "1 mm = " + Num(1 / page.MmPerUnit) + " units",
                    s.Styles?.Name ?? "no plot style table") + (page.Notes.Count > 0 ? "\n" + string.Join("\n", page.Notes) : ""),
                TextWrapping = TextWrapping.Wrap,
            };
            var dock = new DockPanel();
            DockPanel.SetDock(info, Dock.Bottom);
            dock.Children.Add(info);
            dock.Children.Add(new PlotPageElement(page, preview: true, printable: printable));
            var w = new Window
            {
                Title = "Print preview - " + _sheet, Owner = this, Width = 1100, Height = 800,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = dock,
            };
            w.KeyDown += (o, e) => { if (e.Key == System.Windows.Input.Key.Escape) w.Close(); };
            w.ShowDialog();
        }

        /// <summary>Writes the settings into this sheet's page setup in the DWG.</summary>
        private void ApplyToLayout(bool announce)
        {
            if (!IsSheet) return;
            var l = _doc.Layouts.FirstOrDefault(x => x.Name.Equals(_sheet, StringComparison.OrdinalIgnoreCase));
            if (l == null) return;
            var s = Collect();
            string? table = State.StyleTable == BuiltInMono ? "monochrome.ctb" : State.StyleTable == BuiltInGray ? "grayscale.ctb" : State.StyleTable;
            LayoutPlotSetup.Write(l, s, table, State.Printer);
            l.PageName = _sheet;
            LayoutChanged = true;
            if (announce) _notes.Text = "Saved to " + _sheet + "'s page setup (save the drawing to keep it).";
        }

        private void Finish(PlotDialogAction action)
        {
            var s = Collect();
            if (action == PlotDialogAction.Print && s.Area == PlotArea.Window && !s.Region.HasValue)
            {
                _notes.Text = "Pick the window to print first (Select Print Area).";
                return;
            }
            if (action == PlotDialogAction.Print && _saveToLayout.IsChecked == true) ApplyToLayout(announce: false);
            Action = action;
            DialogResult = true;
        }
    }
}
