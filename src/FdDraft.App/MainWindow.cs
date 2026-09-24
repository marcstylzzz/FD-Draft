using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ACadSharp;
using ACadSharp.IO;
using FdDraft.Cad;
using FdDraft.Core;
using FdDraft.Core.Geometry;
using FdDraft.Core.Job;
using FdDraft.Core.Layout;
using FdDraft.Core.Standards;
using FdDraft.View;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;

namespace FdDraft.App
{
    /// <summary>
    /// The FD-Draft main window: drawing canvas with Model and sheet tabs, layer and
    /// point panels, a command line, and the survey commands (Draft FD-Pro job,
    /// Inverse), plus open/save DWG and plot to PDF.
    /// </summary>
    public sealed class MainWindow : Window
    {
        private readonly AppSettings _settings = AppSettings.Load();
        private readonly DrawingCanvas _canvas = new DrawingCanvas();
        private readonly ListBox _sheets = new ListBox();
        private readonly ListBox _layers = new ListBox();
        private readonly DataGrid _points = new DataGrid { AutoGenerateColumns = true, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.None };
        private readonly TextBox _history = new TextBox { IsReadOnly = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap, BorderThickness = new Thickness(0) };
        private readonly TextBox _input = new TextBox { BorderThickness = new Thickness(0) };
        private readonly TextBlock _prompt = new TextBlock { Text = "Command:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), FontWeight = FontWeights.SemiBold };
        private readonly TextBlock _coords = new TextBlock { MinWidth = 280 };
        private readonly TextBlock _snapStatus = new TextBlock();
        private readonly TextBlock _sheetStatus = new TextBlock();

        private CadDocument? _doc;
        private string? _path;
        private bool _dirty;
        private FdJob? _job;
        private FirmStandards? _std;
        private string _sheet = "Model";
        private readonly HashSet<string> _hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Vec2? _inverseFrom;
        private bool _inverseActive;
        private bool _suppressLayerEvents;

        public MainWindow()
        {
            Title = "FD-Draft";
            Width = 1440; Height = 900;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI");
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
            _canvas.SnapEnabled = _settings.Snap;

            var root = new DockPanel();
            var menu = BuildMenu();
            DockPanel.SetDock(menu, Dock.Top); root.Children.Add(menu);
            var tools = BuildToolbar();
            DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);

            var status = new StatusBar();
            status.Items.Add(new StatusBarItem { Content = _coords });
            status.Items.Add(new Separator());
            status.Items.Add(new StatusBarItem { Content = _snapStatus });
            status.Items.Add(new Separator());
            status.Items.Add(new StatusBarItem { Content = _sheetStatus });
            DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);

            var cmd = BuildCommandArea();
            DockPanel.SetDock(cmd, Dock.Bottom); root.Children.Add(cmd);

            var main = new Grid();
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new TabControl { Margin = new Thickness(4, 4, 0, 4) };
            left.Items.Add(new TabItem { Header = "Layers", Content = _layers });
            left.Items.Add(new TabItem { Header = "Points", Content = _points });
            Grid.SetColumn(left, 0); main.Children.Add(left);
            var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };
            Grid.SetColumn(splitter, 1); main.Children.Add(splitter);

            var drawing = new DockPanel { Margin = new Thickness(0, 4, 4, 4) };
            var panelFactory = new FrameworkElementFactory(typeof(StackPanel));
            panelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            _sheets.ItemsPanel = new ItemsPanelTemplate(panelFactory);
            _sheets.BorderThickness = new Thickness(0);
            _sheets.SelectionChanged += (s, e) => { if (_sheets.SelectedItem is string name && name != _sheet) ShowSheet(name); };
            DockPanel.SetDock(_sheets, Dock.Bottom); drawing.Children.Add(_sheets);
            var frame = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)), BorderThickness = new Thickness(1), Child = _canvas };
            drawing.Children.Add(frame);
            Grid.SetColumn(drawing, 2); main.Children.Add(drawing);
            root.Children.Add(main);

            Content = root;

            _points.MouseDoubleClick += (s, e) => { if (_points.SelectedItem is PointRow r) ZoomToPoint(r); };
            _canvas.CursorMoved += OnCursor;
            _canvas.Picked += OnPick;
            Closing += OnClosing;
            PreviewKeyDown += OnKey;

            Bind(Key.O, ModifierKeys.Control, () => OpenDrawing(null));
            Bind(Key.N, ModifierKeys.Control, NewFromTemplate);
            Bind(Key.S, ModifierKeys.Control, () => Save(false));
            Bind(Key.D, ModifierKeys.Control, DraftJob);
            Bind(Key.P, ModifierKeys.Control, PlotPdf);
            Bind(Key.F3, ModifierKeys.None, ToggleSnap);

            UpdateStatus();
            Log("FD-Draft " + typeof(DraftPipeline).Assembly.GetName().Version?.ToString(3) + ". Ctrl+D drafts an FD-Pro job; type HELP for commands.");
        }

        // ---- layout of the window -------------------------------------------------------------

        private Menu BuildMenu()
        {
            var menu = new Menu { Padding = new Thickness(2) };
            MenuItem Item(string header, string gesture, Action a) { var m = new MenuItem { Header = header, InputGestureText = gesture }; m.Click += (s, e) => a(); return m; }
            var file = new MenuItem { Header = "_File" };
            file.Items.Add(Item("_New from template…", "Ctrl+N", NewFromTemplate));
            file.Items.Add(Item("_Open…", "Ctrl+O", () => OpenDrawing(null)));
            file.Items.Add(Item("_Save", "Ctrl+S", () => Save(false)));
            file.Items.Add(Item("Save _As…", "", () => Save(true)));
            file.Items.Add(new Separator());
            file.Items.Add(Item("_Plot sheet to PDF…", "Ctrl+P", PlotPdf));
            file.Items.Add(new Separator());
            file.Items.Add(Item("E_xit", "Alt+F4", Close));
            var survey = new MenuItem { Header = "_Survey" };
            survey.Items.Add(Item("_Draft FD-Pro job…", "Ctrl+D", DraftJob));
            survey.Items.Add(Item("_Inverse", "INV", StartInverse));
            var view = new MenuItem { Header = "_View" };
            view.Items.Add(Item("Zoom _extents", "ZE", () => _canvas.ZoomExtents()));
            view.Items.Add(Item("_Snap on/off", "F3", ToggleSnap));
            var help = new MenuItem { Header = "_Help" };
            help.Items.Add(Item("_Commands", "HELP", ShowHelp));
            menu.Items.Add(file); menu.Items.Add(survey); menu.Items.Add(view); menu.Items.Add(help);
            return menu;
        }

        private ToolBarTray BuildToolbar()
        {
            var bar = new ToolBar();
            Button B(string text, string tip, Action a) { var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(8, 2, 8, 2) }; b.Click += (s, e) => a(); return b; }
            bar.Items.Add(B("Draft job", "Draft an FD-Pro job onto the firm template (Ctrl+D)", DraftJob));
            bar.Items.Add(new Separator());
            bar.Items.Add(B("Open", "Open a DWG or DWT (Ctrl+O)", () => OpenDrawing(null)));
            bar.Items.Add(B("Save", "Save DWG (Ctrl+S)", () => Save(false)));
            bar.Items.Add(B("PDF", "Plot the current sheet to a true-scale PDF (Ctrl+P)", PlotPdf));
            bar.Items.Add(new Separator());
            bar.Items.Add(B("Extents", "Zoom extents (ZE, or double-click the wheel)", () => _canvas.ZoomExtents()));
            bar.Items.Add(B("Inverse", "Bearing and distance between two points (INV)", StartInverse));
            bar.Items.Add(B("Snap", "Snap to points and line ends on/off (F3)", ToggleSnap));
            var tray = new ToolBarTray();
            tray.ToolBars.Add(bar);
            return tray;
        }

        private FrameworkElement BuildCommandArea()
        {
            var mono = new FontFamily("Consolas");
            _history.FontFamily = mono; _input.FontFamily = mono;
            _history.Height = 96;
            _input.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { var t = _input.Text; _input.Clear(); RunCommand(t); e.Handled = true; }
            };
            var grid = new Grid { Background = Brushes.White, Margin = new Thickness(4, 0, 4, 2) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_history, 0); grid.Children.Add(_history);
            var line = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF6)) };
            DockPanel.SetDock(_prompt, Dock.Left);
            line.Children.Add(_prompt);
            line.Children.Add(_input);
            Grid.SetRow(line, 1); grid.Children.Add(line);
            return new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)), BorderThickness = new Thickness(1), Child = grid, Margin = new Thickness(0, 0, 0, 2) };
        }

        private void Bind(Key key, ModifierKeys mods, Action a) => InputBindings.Add(new KeyBinding(new RelayCommand(a), key, mods));

        // ---- command line ----------------------------------------------------------------------

        private void Log(string text)
        {
            _history.AppendText((_history.Text.Length > 0 ? "\n" : "") + text);
            _history.ScrollToEnd();
        }

        private void RunCommand(string text)
        {
            var t = text.Trim();
            if (t.Length == 0) return;
            Log("Command: " + t);
            var parts = t.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string verb = parts[0].ToUpperInvariant();
            string arg = parts.Length > 1 ? parts[1].Trim() : "";
            switch (verb)
            {
                case "OPEN": OpenDrawing(arg.Length > 0 ? arg : null); break;
                case "NEW": NewFromTemplate(); break;
                case "SAVE": Save(false); break;
                case "SAVEAS": Save(true); break;
                case "DRAFT": DraftJob(); break;
                case "PDF": case "PLOT": PlotPdf(); break;
                case "ZE": case "Z": case "EXTENTS": _canvas.ZoomExtents(); break;
                case "INV": case "INVERSE": case "I": StartInverse(); break;
                case "SNAP": ToggleSnap(); break;
                case "MODEL": ShowSheet("Model"); break;
                case "LAYOUT": case "SHEET":
                    if (arg.Length > 0) ShowSheet(arg); else Log("  sheets: " + string.Join(", ", _sheets.Items.Cast<string>()));
                    break;
                case "HELP": case "?": ShowHelp(); break;
                default: Log("  unknown command - type HELP"); break;
            }
        }

        private void ShowHelp()
        {
            Log("  DRAFT   draft an FD-Pro job onto the firm template   (Ctrl+D)");
            Log("  OPEN / NEW / SAVE / SAVEAS   drawings (DWG, DWT)");
            Log("  PDF     plot the current sheet to a true-scale PDF   (Ctrl+P)");
            Log("  INV     inverse: pick two points for bearing and distance");
            Log("  ZE      zoom extents · wheel zooms · middle-drag or Shift-drag pans");
            Log("  MODEL / LAYOUT <name>   switch sheet · SNAP (F3) toggles snapping · Esc cancels");
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_inverseActive) { EndInverse(); Log("  *cancelled*"); }
                e.Handled = true;
                return;
            }
            // Typing anywhere goes to the command line, like a CAD program.
            if (!_input.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.None && e.Key >= Key.A && e.Key <= Key.Z
                && !(Keyboard.FocusedElement is TextBox))
            {
                _input.Focus();
            }
        }

        // ---- drawings --------------------------------------------------------------------------

        public async void OpenDrawing(string? path)
        {
            if (!ConfirmDiscard()) return;
            if (path == null)
            {
                var d = new OpenFileDialog { Title = "Open drawing", Filter = "Drawings (*.dwg;*.dwt)|*.dwg;*.dwt|All files (*.*)|*.*", InitialDirectory = Existing(_settings.LastDrawingFolder) };
                if (d.ShowDialog(this) != true) return;
                path = d.FileName;
            }
            try
            {
                Log("  reading " + path + " …");
                var doc = await Task.Run(() => DwgReader.Read(path));
                _settings.LastDrawingFolder = System.IO.Path.GetDirectoryName(path) ?? "";
                _settings.Save();
                bool isTemplate = path.EndsWith(".dwt", StringComparison.OrdinalIgnoreCase);
                SetDocument(doc, isTemplate ? null : path, dirty: false);
                _job = null; _points.ItemsSource = null;
                Log("  " + System.IO.Path.GetFileName(path) + ": " + doc.Layers.Count + " layers, " + doc.ModelSpace.Entities.Count() + " model entities, sheets: " + string.Join(", ", SheetNames(doc).Skip(1)));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is NotSupportedException || ex is ArgumentException)
            {
                Log("  could not open: " + ex.Message);
            }
        }

        private void NewFromTemplate()
        {
            var d = new OpenFileDialog { Title = "New drawing from template", Filter = "Drawing template (*.dwt)|*.dwt", InitialDirectory = Existing(System.IO.Path.GetDirectoryName(_settings.TemplatePath) ?? "") };
            if (d.ShowDialog(this) != true) return;
            _settings.TemplatePath = d.FileName; _settings.Save();
            OpenDrawing(d.FileName);
        }

        private void Save(bool saveAs)
        {
            if (_doc == null) { Log("  nothing to save"); return; }
            string? path = _path;
            if (saveAs || path == null)
            {
                string suggested = _job != null ? System.IO.Path.Combine(_job.Folder, "export", "fd-draft") : _settings.LastDrawingFolder;
                var d = new SaveFileDialog
                {
                    Title = "Save drawing", Filter = "Drawing (*.dwg)|*.dwg",
                    FileName = _job != null ? SafeName(_job.Settings.Name) + ".dwg" : "Drawing.dwg",
                    InitialDirectory = EnsureDir(suggested),
                };
                if (d.ShowDialog(this) != true) return;
                path = d.FileName;
            }
            try
            {
                DwgWriter.Write(path, _doc);
                _path = path; _dirty = false;
                Log("  saved " + path);
                UpdateTitle();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                Log("  could not save: " + ex.Message);
            }
        }

        private void PlotPdf()
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            var d = new SaveFileDialog
            {
                Title = "Plot to PDF", Filter = "PDF (*.pdf)|*.pdf",
                FileName = (_job != null ? SafeName(_job.Settings.Name) : System.IO.Path.GetFileNameWithoutExtension(_path ?? "Drawing")) + (_sheet == "Model" ? "" : " " + _sheet) + ".pdf",
                InitialDirectory = EnsureDir(_job != null ? System.IO.Path.Combine(_job.Folder, "export", "fd-draft") : _settings.LastDrawingFolder),
            };
            if (d.ShowDialog(this) != true) return;
            try
            {
                PdfSceneWriter.Write(_canvas.Scene, d.FileName, (_job?.Settings.Name ?? "FD-Draft") + " - " + _sheet);
                Log("  plotted " + d.FileName + (_canvas.Scene.IsPaper ? " (sheet at true scale)" : " (model space fitted to 11x17)"));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log("  could not plot: " + ex.Message);
            }
        }

        private void SetDocument(CadDocument doc, string? path, bool dirty, string? showSheet = null)
        {
            _doc = doc; _path = path; _dirty = dirty;
            _hidden.Clear();
            FillLayers();
            _sheets.Items.Clear();
            foreach (var n in SheetNames(doc)) _sheets.Items.Add(n);
            _sheet = "";
            ShowSheet(showSheet ?? "Model");
            UpdateTitle();
        }

        private static IEnumerable<string> SheetNames(CadDocument doc)
        {
            yield return "Model";
            foreach (var l in doc.Layouts.Where(l => l.IsPaperSpace).OrderBy(l => l.TabOrder)) yield return l.Name;
        }

        private void ShowSheet(string name)
        {
            if (_doc == null) return;
            var match = _sheets.Items.Cast<string>().FirstOrDefault(s => s.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match == null) { Log("  no sheet named " + name); return; }
            _sheet = match;
            _sheets.SelectedItem = match;
            Rebuild(fit: true);
        }

        private void Rebuild(bool fit)
        {
            if (_doc == null) return;
            var builder = new SceneBuilder(_doc, _hidden);
            var scene = _sheet == "Model" ? builder.Model() : builder.Layout(_sheet);
            _canvas.Scene = scene;
            if (fit) _canvas.ZoomExtents();
            foreach (var n in scene.Notes) Log("  " + n);
            UpdateStatus();
        }

        private void FillLayers()
        {
            _suppressLayerEvents = true;
            _layers.Items.Clear();
            if (_doc != null)
            {
                foreach (var layer in _doc.Layers.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
                {
                    bool frozen = layer.Flags.HasFlag(ACadSharp.Tables.LayerFlags.Frozen) || !layer.IsOn;
                    var check = new CheckBox { IsChecked = !frozen, IsEnabled = !frozen, VerticalAlignment = VerticalAlignment.Center, Tag = layer.Name };
                    check.Checked += (s, e) => LayerToggled((string)((CheckBox)s).Tag, true);
                    check.Unchecked += (s, e) => LayerToggled((string)((CheckBox)s).Tag, false);
                    var c = layer.Color;
                    Color swatch;
                    if (c.IsTrueColor) swatch = Color.FromRgb(c.R, c.G, c.B);
                    else if (c.Index <= 0 || c.Index == 7 || c.Index >= 256) swatch = Colors.Black;
                    else { var rgb = ACadSharp.Color.GetIndexRGB((byte)c.Index); swatch = Color.FromRgb(rgb[0], rgb[1], rgb[2]); }
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    row.Children.Add(check);
                    row.Children.Add(new Rectangle { Width = 12, Height = 12, Fill = new SolidColorBrush(swatch), Stroke = Brushes.Gray, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                    row.Children.Add(new TextBlock { Text = layer.Name + (frozen ? "  (frozen)" : ""), Foreground = frozen ? Brushes.Gray : Brushes.Black, VerticalAlignment = VerticalAlignment.Center });
                    _layers.Items.Add(row);
                }
            }
            _suppressLayerEvents = false;
        }

        private void LayerToggled(string name, bool visible)
        {
            if (_suppressLayerEvents) return;
            if (visible) _hidden.Remove(name); else _hidden.Add(name);
            // Keep the view where it is; only what is drawn changes.
            var center = _canvas.View.Center; var zoom = _canvas.View.Zoom;
            Rebuild(fit: false);
            _canvas.ZoomTo(center, zoom);
        }

        // ---- survey commands --------------------------------------------------------------------

        private async void DraftJob()
        {
            if (!ConfirmDiscard()) return;
            var dlg = new DraftDialog(_settings) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            try
            {
                Log("Drafting " + dlg.JobFolder + " …");
                string layout = dlg.Layout!;
                string? scale = dlg.Scale;
                string template = dlg.TemplatePath, standards = dlg.StandardsPath, family = dlg.Family, folder = dlg.JobFolder;
                var (drafter, result, job, std, notes) = await Task.Run(() =>
                {
                    var s = FirmStandards.Load(standards);
                    var j = FdJobReader.Read(folder);
                    var r = DraftPipeline.Run(j, s, family, layout, scale);
                    if (r.Chosen == null) throw new InvalidOperationException(string.Join(" ", r.Document.Warnings));
                    s.Sheets.TryGetValue(r.Chosen.Sheet.Layout, out var def);
                    var filler = new TitleBlockFiller(s, j.Settings, r.Chosen.Scale, r.Chosen.Sheet.Layout,
                        SafeName(j.Settings.Name) + ".dwg", DateTime.Today, def?.PaperWidth ?? 0, def?.PaperHeight ?? 0);
                    var d = TemplateDrafter.Open(template, s);
                    var n = d.Draft(r, filler);
                    return (d, r, j, s, n.ToList());
                });
                _job = job; _std = std;
                SetDocument(drafter.Document, null, dirty: true, showSheet: result.Chosen!.Sheet.Layout);
                _points.ItemsSource = job.Points.Select(p => new PointRow(p)).ToList();
                Log("  " + job.Settings.Name + " on " + result.Chosen.Sheet.Layout + " at " + result.Chosen.Scale.Label + " — " + result.Chosen.Reason);
                foreach (var p in result.Document.Parcels)
                    Log(string.Format(CultureInfo.InvariantCulture, "  parcel {0}: area {1:F1} m², perimeter {2:F3} m", p.Code, p.Area, p.Perimeter));
                foreach (var w in result.Document.Warnings) Log("  ! " + w);
                foreach (var n in notes) Log("  " + n);
                Log("  Save (Ctrl+S) to write the DWG; PDF (Ctrl+P) to plot the sheet.");
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is ArgumentException || ex is UnauthorizedAccessException)
            {
                Log("  could not draft: " + ex.Message);
            }
        }

        private void StartInverse()
        {
            if (_canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            _inverseActive = true;
            _inverseFrom = null;
            _prompt.Text = "Inverse - from point:";
            Log("INVERSE  pick the first point (Esc to finish)");
            _canvas.Focus();
        }

        private void EndInverse()
        {
            _inverseActive = false;
            _inverseFrom = null;
            _canvas.RubberFrom = null;
            _prompt.Text = "Command:";
            _canvas.InvalidateVisual();
        }

        private void OnPick(Vec2 scenePoint)
        {
            if (!_inverseActive || _canvas.Scene == null) return;
            var model = _canvas.Scene.ModelAt(scenePoint);
            if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
            if (_inverseFrom == null)
            {
                _inverseFrom = model;
                _canvas.RubberFrom = scenePoint;
                _prompt.Text = "Inverse - to point:";
                Log("  from " + NE(model.Value));
                return;
            }
            var inv = InverseResult.Between(_inverseFrom.Value, model.Value, _std?.BearingRotationDeg ?? 0);
            Log("  to   " + NE(model.Value) + "   " + inv);
            // Chain like a data collector: the "to" point becomes the next "from".
            _inverseFrom = model;
            _canvas.RubberFrom = scenePoint;
        }

        private void OnCursor(Vec2 p, SnapPoint? snap)
        {
            var m = _canvas.Scene?.ModelAt(p);
            _coords.Text = m.HasValue ? NE(m.Value) : "paper " + p.X.ToString("F1", CultureInfo.InvariantCulture) + ", " + p.Y.ToString("F1", CultureInfo.InvariantCulture) + " mm";
            _snapStatus.Text = !_canvas.SnapEnabled ? "Snap off" : snap.HasValue ? "Snap: " + snap.Value.Kind : "Snap on";
        }

        private void ToggleSnap()
        {
            _canvas.SnapEnabled = !_canvas.SnapEnabled;
            _settings.Snap = _canvas.SnapEnabled; _settings.Save();
            Log("  snap " + (_canvas.SnapEnabled ? "on" : "off"));
            UpdateStatus();
        }

        private void ZoomToPoint(PointRow r)
        {
            ShowSheet("Model");
            _canvas.ZoomTo(new Vec2(r.E, r.N), Math.Max(_canvas.View.Zoom, 40));
            Log("  point " + r.Point + "  " + NE(new Vec2(r.E, r.N)) + "  " + r.Code);
        }

        // ---- bits ------------------------------------------------------------------------------

        private static string NE(Vec2 p) =>
            string.Format(CultureInfo.InvariantCulture, "N {0:F3}  E {1:F3}", p.Y, p.X);

        private void UpdateStatus()
        {
            _snapStatus.Text = _canvas.SnapEnabled ? "Snap on" : "Snap off";
            _sheetStatus.Text = _doc == null ? "No drawing" : (_sheet == "Model" ? "Model space" : "Sheet " + _sheet);
        }

        private void UpdateTitle() =>
            Title = "FD-Draft - " + (_path != null ? System.IO.Path.GetFileName(_path) : _job != null ? _job.Settings.Name + " (unsaved)" : "Untitled") + (_dirty ? " *" : "");

        private bool ConfirmDiscard()
        {
            if (!_dirty) return true;
            var r = MessageBox.Show(this, "The current drawing has unsaved changes. Discard them?", "FD-Draft", MessageBoxButton.YesNo, MessageBoxImage.Question);
            return r == MessageBoxResult.Yes;
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (!ConfirmDiscard()) e.Cancel = true;
        }

        private static string Existing(string dir) => !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : "";

        private static string EnsureDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "";
            try { Directory.CreateDirectory(dir); return dir; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return ""; }
        }

        private static string SafeName(string name)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Length == 0 ? "plan" : name;
        }
    }

    /// <summary>A row of the Points panel (FD-Pro's own column order: N before E).</summary>
    public sealed class PointRow
    {
        public PointRow(SurveyPoint p)
        {
            Point = p.Id; N = p.Northing; E = p.Easting;
            Northing = p.Northing.ToString("F3", CultureInfo.InvariantCulture);
            Easting = p.Easting.ToString("F3", CultureInfo.InvariantCulture);
            Elevation = p.Elevation.ToString("F3", CultureInfo.InvariantCulture);
            Code = p.Code; Note = p.Note;
        }
        public int Point { get; }
        public string Northing { get; }
        public string Easting { get; }
        public string Elevation { get; }
        public string Code { get; }
        public string Note { get; }
        internal double N { get; }
        internal double E { get; }
    }
}
