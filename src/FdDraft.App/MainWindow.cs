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
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using FdDraft.Cad;
using FdDraft.Cad.Editing;
using FdDraft.Core;
using FdDraft.Core.Geometry;
using FdDraft.Core.Job;
using FdDraft.Core.Layout;
using FdDraft.Core.Standards;
using FdDraft.View;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;
using Arc = ACadSharp.Entities.Arc;
using Line = ACadSharp.Entities.Line;

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

        private readonly ComboBox _layerCombo = new ComboBox { MinWidth = 160 };
        private readonly TextBox _properties = new TextBox { IsReadOnly = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), AcceptsReturn = true };
        private readonly ListView _codes = new ListView();
        private readonly UndoStack _undo = new UndoStack();

        private CadDocument? _doc;
        private string? _path;
        private bool _dirty;
        private FdJob? _job;
        private FirmStandards? _std;
        private string _sheet = "Model";
        private readonly HashSet<string> _hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Vec2? _inverseFrom;
        private bool _suppressLayerEvents;

        // ---- the active command's state: set while a tool is waiting for a click or a typed line ----
        private string _activeTool = "";
        private Action<Vec2>? _awaitingPoint;
        private Action<string>? _awaitingLine;
        private (List<Entity> Added, Vec2 End, string Layer)? _leaderPending;

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
            left.Items.Add(new TabItem { Header = "Codes", Content = _codes });
            left.Items.Add(new TabItem { Header = "Properties", Content = _properties });
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

            var codeView = new GridView();
            codeView.Columns.Add(new GridViewColumn { Header = "Code", DisplayMemberBinding = new Binding("Key"), Width = 90 });
            codeView.Columns.Add(new GridViewColumn { Header = "Description", DisplayMemberBinding = new Binding("Description"), Width = 150 });
            codeView.Columns.Add(new GridViewColumn { Header = "Layer", DisplayMemberBinding = new Binding("LayerName"), Width = 140 });
            _codes.View = codeView;

            _points.MouseDoubleClick += (s, e) => { if (_points.SelectedItem is PointRow r) ZoomToPoint(r); };
            _canvas.CursorMoved += OnCursor;
            _canvas.Picked += OnPick;
            _canvas.EntityClicked += OnEntityClicked;
            Closing += OnClosing;
            PreviewKeyDown += OnKey;

            Bind(Key.O, ModifierKeys.Control, () => OpenDrawing(null));
            Bind(Key.N, ModifierKeys.Control, NewFromTemplate);
            Bind(Key.S, ModifierKeys.Control, () => Save(false));
            Bind(Key.D, ModifierKeys.Control, DraftJob);
            Bind(Key.P, ModifierKeys.Control, PlotPdf);
            Bind(Key.F3, ModifierKeys.None, ToggleSnap);
            Bind(Key.Z, ModifierKeys.Control, DoUndo);
            Bind(Key.Y, ModifierKeys.Control, DoRedo);

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
            var edit = new MenuItem { Header = "_Edit" };
            edit.Items.Add(Item("_Undo", "Ctrl+Z", DoUndo));
            edit.Items.Add(Item("_Redo", "Ctrl+Y", DoRedo));
            edit.Items.Add(new Separator());
            edit.Items.Add(Item("Erase selection", "Del", EraseSelected));
            var draw = new MenuItem { Header = "_Draw" };
            draw.Items.Add(Item("_Line", "LINE", StartLine));
            draw.Items.Add(Item("_Arc (3 points)", "ARC", StartArc));
            draw.Items.Add(Item("_Text", "TEXT", StartText));
            draw.Items.Add(Item("_Leader", "LEADER", StartLeader));
            var modify = new MenuItem { Header = "_Modify" };
            modify.Items.Add(Item("_Move", "MOVE", StartMove));
            modify.Items.Add(Item("_Rotate", "ROTATE", StartRotate));
            var view = new MenuItem { Header = "_View" };
            view.Items.Add(Item("Zoom _extents", "ZE", () => _canvas.ZoomExtents()));
            view.Items.Add(Item("_Snap on/off", "F3", ToggleSnap));
            var help = new MenuItem { Header = "_Help" };
            help.Items.Add(Item("_Commands", "HELP", ShowHelp));
            menu.Items.Add(file); menu.Items.Add(edit); menu.Items.Add(survey); menu.Items.Add(draw); menu.Items.Add(modify); menu.Items.Add(view); menu.Items.Add(help);
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
            bar.Items.Add(new Separator());
            bar.Items.Add(B("Undo", "Undo the last change (Ctrl+Z)", DoUndo));
            bar.Items.Add(B("Redo", "Redo (Ctrl+Y)", DoRedo));
            bar.Items.Add(B("Erase", "Erase the selected entities (Del)", EraseSelected));
            bar.Items.Add(new Separator());
            bar.Items.Add(B("Line", "Draw lines by bearing and distance (LINE)", StartLine));
            bar.Items.Add(B("Arc", "Draw an arc through three points (ARC)", StartArc));
            bar.Items.Add(B("Text", "Place text (TEXT)", StartText));
            bar.Items.Add(B("Leader", "Draw a leader with text (LEADER)", StartLeader));
            bar.Items.Add(new Separator());
            bar.Items.Add(B("Move", "Move the selected entities (MOVE)", StartMove));
            bar.Items.Add(B("Rotate", "Rotate the selected entities (ROTATE)", StartRotate));
            bar.Items.Add(new Separator());
            bar.Items.Add(new TextBlock { Text = "Layer:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
            bar.Items.Add(_layerCombo);
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
            // A tool waiting for a typed line (a COGO leg, a rotation angle, text content)
            // gets first refusal, even on a blank line (blank usually ends its chain).
            if (_awaitingLine != null)
            {
                Log("Command: " + t);
                _awaitingLine(t);
                return;
            }
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
                case "LINE": case "L": StartLine(); break;
                case "ARC": StartArc(); break;
                case "TEXT": case "T": StartText(); break;
                case "LEADER": case "LE": StartLeader(); break;
                case "MOVE": case "M": StartMove(); break;
                case "ROTATE": case "RO": StartRotate(); break;
                case "ERASE": EraseSelected(); break;
                case "UNDO": case "U": DoUndo(); break;
                case "REDO": DoRedo(); break;
                case "CLAYER":
                    if (arg.Length > 0 && _layerCombo.Items.Contains(arg)) _layerCombo.SelectedItem = arg;
                    else Log("  current layer: " + CurrentLayer());
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
            Log("  Click an entity to select it (Ctrl+click adds); Del erases; Ctrl+Z/Ctrl+Y undo/redo");
            Log("  MOVE    select entities, MOVE, pick base point then destination");
            Log("  ROTATE  select entities, ROTATE, pick the pivot, type the angle in degrees (clockwise)");
            Log("  LINE    pick or type E,N for the start, then BEARING DISTANCE for each leg, e.g. N45-30-00E 125.50 (blank ends)");
            Log("  ARC     pick three points on the arc: start, a point on it, end");
            Log("  TEXT    pick a point, then type the text (or \"height text\", e.g. \"0.25 LOT 5\")");
            Log("  LEADER  pick the feature point then the text position, then type the text");
            Log("  CLAYER <name>   set the layer new drawing picks up   · type the name into the toolbar's Layer box");
            Log("  ZE      zoom extents · wheel zooms · middle-drag or Shift-drag pans");
            Log("  MODEL / LAYOUT <name>   switch sheet · SNAP (F3) toggles snapping · Esc cancels the active tool");
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_activeTool.Length > 0) { EndTool(); Log("  *cancelled*"); }
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Delete && !_input.IsKeyboardFocusWithin)
            {
                EraseSelected();
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
                _job = null; _points.ItemsSource = null; FillCodes();
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
            _undo.Clear();
            _canvas.Selected.Clear();
            EndTool();
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
            string? keepCurrent = _layerCombo.SelectedItem as string;
            _layerCombo.Items.Clear();
            if (_doc != null)
            {
                foreach (var layer in _doc.Layers.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
                {
                    _layerCombo.Items.Add(layer.Name);
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
            _layerCombo.SelectedItem = keepCurrent != null && _layerCombo.Items.Contains(keepCurrent) ? keepCurrent
                : _layerCombo.Items.Contains("0") ? "0"
                : _layerCombo.Items.Count > 0 ? _layerCombo.Items[0] : null;
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
                FillCodes();
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

        // ---- command dispatch: the active tool waits for a click (_awaitingPoint) or a typed
        // line (_awaitingLine); Esc cancels either way (see OnKey). --------------------------

        private void BeginTool(string name)
        {
            _activeTool = name;
            _canvas.ToolActive = true;
            _canvas.InvalidateVisual();
        }

        private void EndTool()
        {
            _activeTool = "";
            _canvas.ToolActive = false;
            _awaitingPoint = null;
            _awaitingLine = null;
            _leaderPending = null;
            _inverseFrom = null;
            _canvas.RubberFrom = null;
            _prompt.Text = "Command:";
            _canvas.InvalidateVisual();
        }

        private void OnPick(Vec2 scenePoint) => _awaitingPoint?.Invoke(scenePoint);

        private void StartInverse()
        {
            if (_canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            BeginTool("INVERSE");
            _inverseFrom = null;
            _prompt.Text = "Inverse - from point:";
            Log("INVERSE  pick the first point (Esc to finish)");
            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                if (_inverseFrom == null)
                {
                    _inverseFrom = model;
                    _canvas.RubberFrom = p;
                    _prompt.Text = "Inverse - to point:";
                    Log("  from " + NE(model.Value));
                    return;
                }
                var inv = InverseResult.Between(_inverseFrom.Value, model.Value, _std?.BearingRotationDeg ?? 0);
                Log("  to   " + NE(model.Value) + "   " + inv);
                // Chain like a data collector: the "to" point becomes the next "from".
                _inverseFrom = model;
                _canvas.RubberFrom = p;
            };
            _canvas.Focus();
        }

        // ---- selection, erase, undo -------------------------------------------------------------

        private void OnEntityClicked(ulong? handle, bool ctrl)
        {
            if (!ctrl) _canvas.Selected.Clear();
            if (handle.HasValue)
            {
                if (!_canvas.Selected.Add(handle.Value) && ctrl) _canvas.Selected.Remove(handle.Value);
            }
            UpdateProperties();
            _canvas.InvalidateVisual();
        }

        private List<Entity> SelectedEntities() =>
            _doc == null ? new List<Entity>() : _canvas.Selected.Select(h => _doc.GetCadObject(h) as Entity).Where(e => e != null).Cast<Entity>().ToList();

        private void EraseSelected()
        {
            if (_doc == null) return;
            var entities = SelectedEntities();
            if (entities.Count == 0) { Log("  nothing selected - click an entity first"); return; }
            _undo.Push(new RemoveEntitiesCommand(entities, entities.Count == 1 ? "Erase" : "Erase " + entities.Count));
            _canvas.Selected.Clear();
            _dirty = true; UpdateTitle();
            Rebuild(fit: false);
            UpdateProperties();
            Log("  erased " + entities.Count + " entit" + (entities.Count == 1 ? "y" : "ies") + "  (Ctrl+Z to undo)");
        }

        private void DoUndo()
        {
            var d = _undo.Undo();
            if (d == null) { Log("  nothing to undo"); return; }
            _canvas.Selected.Clear();
            _dirty = true; UpdateTitle();
            Rebuild(fit: false);
            UpdateProperties();
            Log("  undid: " + d);
        }

        private void DoRedo()
        {
            var d = _undo.Redo();
            if (d == null) { Log("  nothing to redo"); return; }
            _canvas.Selected.Clear();
            _dirty = true; UpdateTitle();
            Rebuild(fit: false);
            UpdateProperties();
            Log("  redid: " + d);
        }

        private void UpdateProperties()
        {
            var entities = SelectedEntities();
            if (entities.Count == 0) { _properties.Text = "(nothing selected)"; return; }
            var sb = new System.Text.StringBuilder();
            foreach (var e in entities.Take(30))
            {
                sb.Append(e.GetType().Name).Append("   layer ").Append(e.Layer?.Name ?? "0").Append("   handle ").Append(e.Handle).AppendLine();
                switch (e)
                {
                    case Line ln:
                    {
                        var from = new Vec2(ln.StartPoint.X, ln.StartPoint.Y);
                        var to = new Vec2(ln.EndPoint.X, ln.EndPoint.Y);
                        sb.AppendLine("  " + InverseResult.Between(from, to, _std?.BearingRotationDeg ?? 0));
                        break;
                    }
                    case Arc a:
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  radius {0:F3}   length {1:F3}", a.Radius, a.Radius * Math.Abs(a.EndAngle - a.StartAngle)));
                        break;
                    case Circle c:
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  radius {0:F3}   centre {1}", c.Radius, NE(new Vec2(c.Center.X, c.Center.Y))));
                        break;
                    case TextEntity t:
                        sb.AppendLine("  \"" + t.Value + "\"   height " + t.Height.ToString("F3", CultureInfo.InvariantCulture));
                        break;
                    case MText mt:
                        sb.AppendLine("  \"" + mt.Value + "\"   height " + mt.Height.ToString("F3", CultureInfo.InvariantCulture));
                        break;
                    case Insert ins:
                        sb.AppendLine("  block " + (ins.Block?.Name ?? "?") + "   at " + NE(new Vec2(ins.InsertPoint.X, ins.InsertPoint.Y)));
                        break;
                }
                sb.AppendLine();
            }
            if (entities.Count > 30) sb.AppendLine("… and " + (entities.Count - 30) + " more");
            _properties.Text = sb.ToString().TrimEnd();
        }

        // ---- layers, codes ------------------------------------------------------------------------

        private string CurrentLayer() => (_layerCombo.SelectedItem as string) ?? "0";

        private Layer GetOrCreateLayer(string name)
        {
            if (_doc!.Layers.TryGetValue(name, out var layer)) return layer;
            layer = new Layer(name);
            _doc.Layers.Add(layer);
            FillLayers();
            return layer;
        }

        private void FillCodes()
        {
            _codes.ItemsSource = _job?.Codes.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ---- drafting tools (COGO) ----------------------------------------------------------------

        private void StartLine()
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            string layer = CurrentLayer();
            BeginTool("LINE");
            Log("LINE  on layer " + layer + " - pick the start point, or type E,N (blank/Esc ends)");
            _prompt.Text = "Line - start point:";
            Vec2? cur = null;

            void Leg(Vec2 to)
            {
                var from = cur!.Value;
                var line = new Line(new XYZ(from.X, from.Y, 0), new XYZ(to.X, to.Y, 0)) { Layer = GetOrCreateLayer(layer) };
                _undo.Push(new AddEntitiesCommand(_doc!.ModelSpace, new Entity[] { line }, "Line"));
                cur = to;
                _canvas.RubberFrom = to;
                _dirty = true; UpdateTitle();
                Rebuild(fit: false);
                Log("  " + InverseResult.Between(from, to, _std?.BearingRotationDeg ?? 0));
                _prompt.Text = "Line - next bearing distance (blank ends):";
            }

            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                if (cur == null)
                {
                    cur = model.Value; _canvas.RubberFrom = p; _canvas.ToolActive = false;
                    Log("  start " + NE(model.Value));
                    _prompt.Text = "Line - next bearing distance (blank ends):";
                }
                else Leg(model.Value);
            };
            _awaitingLine = s =>
            {
                if (cur == null)
                {
                    if (Cogo.TryParseCoordinate(s, out double e, out double n))
                    {
                        cur = new Vec2(e, n);
                        Log("  start " + NE(cur.Value));
                        _prompt.Text = "Line - next bearing distance (blank ends):";
                    }
                    else Log("  type E,N or click a start point");
                    return;
                }
                if (s.Length == 0) { EndTool(); Log("  *line complete*"); return; }
                if (!Cogo.TryParseLeg(s, out double az, out double dist))
                {
                    Log("  type BEARING DISTANCE, e.g. N45-30-00E 125.50 (blank ends)");
                    return;
                }
                var to = new Vec2(cur.Value.X + dist * Math.Sin(az), cur.Value.Y + dist * Math.Cos(az));
                Leg(to);
            };
        }

        private void StartArc()
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            string layer = CurrentLayer();
            BeginTool("ARC");
            Log("ARC  on layer " + layer + " - pick three points on the arc: start, a point on it, end (Esc to cancel)");
            _prompt.Text = "Arc - start point:";
            var pts = new List<Vec2>();
            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                pts.Add(model.Value);
                if (pts.Count == 1) { _canvas.RubberFrom = p; _prompt.Text = "Arc - a point on the arc:"; Log("  start " + NE(model.Value)); return; }
                if (pts.Count == 2) { _prompt.Text = "Arc - end point:"; Log("  through " + NE(model.Value)); return; }
                var a = FdDraft.Core.Geometry.Arc.ThroughThreePoints(pts[0], pts[1], pts[2]);
                if (a == null)
                {
                    Log("  those three points are collinear - pick again");
                    pts.Clear(); _canvas.RubberFrom = null; _prompt.Text = "Arc - start point:";
                    return;
                }
                double a0 = a.StartAngle, a1 = a.StartAngle + a.Sweep;
                var entity = new Arc { Center = new XYZ(a.Center.X, a.Center.Y, 0), Radius = a.Radius, StartAngle = Math.Min(a0, a1), EndAngle = Math.Max(a0, a1), Layer = GetOrCreateLayer(layer) };
                _undo.Push(new AddEntitiesCommand(_doc!.ModelSpace, new Entity[] { entity }, "Arc"));
                _dirty = true; UpdateTitle();
                EndTool();
                Rebuild(fit: false);
                Log(string.Format(CultureInfo.InvariantCulture, "  arc  R {0:F3}  length {1:F3}", a.Radius, a.Length));
            };
        }

        private void StartText()
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            string layer = CurrentLayer();
            BeginTool("TEXT");
            Log("TEXT  on layer " + layer + " - pick the insertion point, then type the text (or \"height text\"; Esc to cancel)");
            _prompt.Text = "Text - insertion point:";
            Vec2? at = null;
            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                at = model.Value;
                _canvas.ToolActive = false;
                _prompt.Text = "Text - content (or \"height text\", default height 0.2):";
                Log("  at " + NE(model.Value) + " - type the text");
            };
            _awaitingLine = s =>
            {
                if (s.Length == 0) { EndTool(); Log("  *cancelled - no text*"); return; }
                ParseHeightAndText(s, out double h, out string content);
                var entity = new TextEntity { Value = content, InsertPoint = new XYZ(at!.Value.X, at.Value.Y, 0), Height = h, Layer = GetOrCreateLayer(layer) };
                _undo.Push(new AddEntitiesCommand(_doc!.ModelSpace, new Entity[] { entity }, "Text"));
                _dirty = true; UpdateTitle();
                EndTool();
                Rebuild(fit: false);
                Log("  text placed");
            };
        }

        private void StartLeader()
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            string layer = CurrentLayer();
            BeginTool("LEADER");
            Log("LEADER  on layer " + layer + " - pick the point on the feature, then where the text goes (Esc to cancel)");
            _prompt.Text = "Leader - points to (the feature):";
            Vec2? tip = null;
            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                if (tip == null)
                {
                    tip = model.Value; _canvas.RubberFrom = p; _prompt.Text = "Leader - text position:";
                    Log("  points to " + NE(model.Value));
                    return;
                }
                var end = model.Value;
                var added = new List<Entity>();
                var shaft = new Line(new XYZ(tip.Value.X, tip.Value.Y, 0), new XYZ(end.X, end.Y, 0)) { Layer = GetOrCreateLayer(layer) };
                added.Add(shaft);
                double dx = end.X - tip.Value.X, dy = end.Y - tip.Value.Y;
                double len = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-9);
                double ux = dx / len, uy = dy / len;
                double headLen = Math.Min(0.15, len * 0.2);
                double px = -uy, py = ux;
                var wing1 = new Vec2(tip.Value.X + ux * headLen - px * headLen * 0.35, tip.Value.Y + uy * headLen - py * headLen * 0.35);
                var wing2 = new Vec2(tip.Value.X + ux * headLen + px * headLen * 0.35, tip.Value.Y + uy * headLen + py * headLen * 0.35);
                added.Add(new Line(new XYZ(tip.Value.X, tip.Value.Y, 0), new XYZ(wing1.X, wing1.Y, 0)) { Layer = GetOrCreateLayer(layer) });
                added.Add(new Line(new XYZ(tip.Value.X, tip.Value.Y, 0), new XYZ(wing2.X, wing2.Y, 0)) { Layer = GetOrCreateLayer(layer) });
                _canvas.ToolActive = false;
                _prompt.Text = "Leader - text (or \"height text\", default height 0.2):";
                _leaderPending = (added, end, layer);
                Log("  text position " + NE(end) + " - type the text");
            };
            _awaitingLine = s =>
            {
                if (_leaderPending == null) return;
                var (added, end, lyr) = _leaderPending.Value;
                _leaderPending = null;
                if (s.Length == 0) { EndTool(); Log("  *cancelled - no text*"); return; }
                ParseHeightAndText(s, out double h, out string content);
                added.Add(new TextEntity { Value = content, InsertPoint = new XYZ(end.X, end.Y, 0), Height = h, Layer = GetOrCreateLayer(lyr) });
                _undo.Push(new AddEntitiesCommand(_doc!.ModelSpace, added, "Leader"));
                _dirty = true; UpdateTitle();
                EndTool();
                Rebuild(fit: false);
                Log("  leader placed");
            };
        }

        private static void ParseHeightAndText(string s, out double height, out string content)
        {
            height = 0.2; content = s;
            var parts = s.Split(new[] { ' ' }, 2);
            if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double h) && h > 0)
            {
                height = h; content = parts[1];
            }
        }

        // ---- editing tools ------------------------------------------------------------------------

        private void StartMove()
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            var entities = SelectedEntities();
            if (entities.Count == 0) { Log("  select entities to move first, then type MOVE"); return; }
            BeginTool("MOVE");
            _prompt.Text = "Move - base point:";
            Log("MOVE  " + entities.Count + " entit" + (entities.Count == 1 ? "y" : "ies") + " - pick the base point, then the destination (Esc to cancel)");
            Vec2? basePt = null;
            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                if (basePt == null)
                {
                    basePt = model.Value; _canvas.RubberFrom = p; _prompt.Text = "Move - destination:";
                    Log("  base " + NE(model.Value));
                    return;
                }
                double dx = model.Value.X - basePt.Value.X, dy = model.Value.Y - basePt.Value.Y;
                _undo.Push(TransformEntitiesCommand.Move(entities, dx, dy, "Move " + entities.Count));
                _dirty = true; UpdateTitle();
                EndTool();
                _canvas.Selected.Clear();
                Rebuild(fit: false);
                Log(string.Format(CultureInfo.InvariantCulture, "  moved  dN {0:F3}  dE {1:F3}", dy, dx));
            };
        }

        private void StartRotate()
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            var entities = SelectedEntities();
            if (entities.Count == 0) { Log("  select entities to rotate first, then type ROTATE"); return; }
            BeginTool("ROTATE");
            _prompt.Text = "Rotate - pivot point:";
            Log("ROTATE  " + entities.Count + " entit" + (entities.Count == 1 ? "y" : "ies") + " - pick the pivot point, then type the angle in degrees, clockwise (Esc to cancel)");
            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                var pivot = new XYZ(model.Value.X, model.Value.Y, 0);
                _canvas.ToolActive = false;
                _prompt.Text = "Rotate - angle, degrees clockwise:";
                Log("  pivot " + NE(model.Value) + " - type the angle, e.g. 90 or -15.5");
                _awaitingLine = s =>
                {
                    if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double deg))
                    {
                        Log("  not a number - type the angle in degrees");
                        return;
                    }
                    // Survey angles turn clockwise from north; the drawing's X/Y (East/North) plane
                    // rotates counter-clockwise for a positive angle, so clockwise input is negated.
                    _undo.Push(TransformEntitiesCommand.Rotate(entities, pivot, -deg * Math.PI / 180.0, "Rotate " + entities.Count));
                    _dirty = true; UpdateTitle();
                    EndTool();
                    _canvas.Selected.Clear();
                    Rebuild(fit: false);
                    Log("  rotated " + deg.ToString("F2", CultureInfo.InvariantCulture) + "°");
                };
            };
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
