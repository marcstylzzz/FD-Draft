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
        private readonly TextBox _textEdit = new TextBox { IsEnabled = false, Margin = new Thickness(4) };
        private readonly Button _textEditApply = new Button { Content = "Apply text", IsEnabled = false, Margin = new Thickness(4, 0, 4, 4), Padding = new Thickness(6, 2, 6, 2) };
        private readonly StackPanel _numberFields = new StackPanel();
        private readonly Button _numberEditApply = new Button { Content = "Apply", IsEnabled = false, Margin = new Thickness(4, 0, 4, 4), Padding = new Thickness(6, 2, 6, 2), Visibility = Visibility.Collapsed };
        /// <summary>The Properties panel's numeric fields for the current single selection -
        /// rebuilt by <see cref="UpdateProperties"/>, applied together by
        /// <see cref="ApplySelectedNumberEdit"/> as one <c>CompositeCommand</c>.</summary>
        private readonly List<(TextBox Box, double Current, Action<double> Set, string Description)> _numberRows = new List<(TextBox, double, Action<double>, string)>();
        /// <summary>Which vertex of a selected LwPolyline/Polyline2D the Properties panel's E/N
        /// fields edit - typed into a small "Vertex #" box, since a polyline can have many.</summary>
        private int _propertiesVertexIndex;
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
            var propsPanel = new DockPanel();
            var editRow = new StackPanel();
            editRow.Children.Add(new TextBlock { Text = "Text of the selected label:", Margin = new Thickness(4, 4, 4, 0), Foreground = Brushes.Gray, FontSize = 11 });
            editRow.Children.Add(_textEdit);
            editRow.Children.Add(_textEditApply);
            editRow.Children.Add(_numberFields);
            editRow.Children.Add(_numberEditApply);
            DockPanel.SetDock(editRow, Dock.Bottom);
            propsPanel.Children.Add(editRow);
            propsPanel.Children.Add(_properties);
            left.Items.Add(new TabItem { Header = "Properties", Content = propsPanel });
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
            _textEditApply.Click += (s, e) => ApplySelectedTextEdit();
            _textEdit.KeyDown += (s, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift) { ApplySelectedTextEdit(); e.Handled = true; } };
            _numberEditApply.Click += (s, e) => ApplySelectedNumberEdit();
            _canvas.CursorMoved += OnCursor;
            _canvas.Picked += OnPick;
            _canvas.EntityClicked += OnEntityClicked;
            _canvas.BoxSelected += OnBoxSelected;
            _canvas.Dragged += OnDragged;
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
            Bind(Key.A, ModifierKeys.Control, SelectAll);

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
            survey.Items.Add(Item("_Area", "AREA", StartArea));
            survey.Items.Add(Item("I_D point", "ID", StartId));
            var edit = new MenuItem { Header = "_Edit" };
            edit.Items.Add(Item("_Undo", "Ctrl+Z", DoUndo));
            edit.Items.Add(Item("_Redo", "Ctrl+Y", DoRedo));
            edit.Items.Add(new Separator());
            edit.Items.Add(Item("Erase selection", "Del", EraseSelected));
            edit.Items.Add(new Separator());
            edit.Items.Add(Item("Select _all in view", "Ctrl+A", SelectAll));
            edit.Items.Add(Item("Select same _layer", "SELLAYER", () => SelectByLayer("")));
            var draw = new MenuItem { Header = "_Draw" };
            draw.Items.Add(Item("_Line", "LINE", StartLine));
            draw.Items.Add(Item("_Arc (3 points)", "ARC", StartArc));
            draw.Items.Add(Item("_Text", "TEXT", StartText));
            draw.Items.Add(Item("_Leader", "LEADER", StartLeader));
            draw.Items.Add(Item("_Dimension (aligned)", "DIM", () => StartDimension()));
            draw.Items.Add(Item("Dimension (li_near)", "DIMLIN", () => StartDimension(linear: true)));
            draw.Items.Add(Item("Dimension (_radius)", "DIMRAD", () => StartRadiusDimension()));
            draw.Items.Add(Item("Dimension (d_iameter)", "DIMDIA", () => StartRadiusDimension(diameter: true)));
            draw.Items.Add(Item("Dimension (an_gle)", "DIMANG", StartAngularDimension));
            var modify = new MenuItem { Header = "_Modify" };
            modify.Items.Add(Item("_Move", "MOVE", StartMove));
            modify.Items.Add(Item("_Rotate", "ROTATE", StartRotate));
            modify.Items.Add(Item("_Stretch vertex", "STRETCH", StartStretch));
            modify.Items.Add(Item("Delete polyline _vertex", "VXDEL", StartVertexDelete));
            modify.Items.Add(Item("_Add polyline vertex", "VXADD", StartVertexInsert));
            modify.Items.Add(Item("_Copy", "COPY", StartCopy));
            modify.Items.Add(Item("M_irror", "MIRROR", StartMirror));
            modify.Items.Add(Item("_Offset", "OFFSET", StartOffset));
            modify.Items.Add(Item("_Join into polyline", "JOIN", JoinSelection));
            modify.Items.Add(Item("_Trim", "TRIM", () => StartTrimExtend(true)));
            modify.Items.Add(Item("_Extend", "EXTEND", () => StartTrimExtend(false)));
            modify.Items.Add(Item("Fi_llet", "FILLET", StartFillet));
            modify.Items.Add(Item("La_bel bearing/distance", "LABEL", LabelSelection));
            modify.Items.Add(Item("_Flip label to other side", "FLIP", FlipSelectedLabels));
            modify.Items.Add(new Separator());
            modify.Items.Add(Item("Set _layer of selection", "", SetSelectionLayer));
            var view = new MenuItem { Header = "_View" };
            view.Items.Add(Item("Zoom _extents", "ZE", () => _canvas.ZoomExtents()));
            view.Items.Add(Item("_Snap on/off", "F3", ToggleSnap));
            view.Items.Add(new Separator());
            view.Items.Add(Item("Sheet s_cale…", "VPSCALE", () => StartSheetScale("")));
            view.Items.Add(Item("Add _viewport to sheet", "MVIEW", StartMview));
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
            bar.Items.Add(B("Area", "Area and perimeter of the selected closed figure, or of picked corners (AREA)", StartArea));
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
            bar.Items.Add(B("Dim", "Aligned dimension between two points (DIM; DIMLIN for horizontal/vertical, DIMRAD for a radius)", () => StartDimension()));
            bar.Items.Add(new Separator());
            bar.Items.Add(B("Move", "Move the selected entities (MOVE)", StartMove));
            bar.Items.Add(B("Rotate", "Rotate the selected entities (ROTATE)", StartRotate));
            bar.Items.Add(B("Stretch", "Move one shared vertex, keeping connected lines joined (STRETCH)", StartStretch));
            bar.Items.Add(B("Copy", "Copy the selected entities (COPY)", StartCopy));
            bar.Items.Add(B("Mirror", "Mirror the selected entities across a line (MIRROR)", StartMirror));
            bar.Items.Add(B("Label", "Bearing/distance (or curve data) labels for the selected lines, arcs and polylines (LABEL)", LabelSelection));
            bar.Items.Add(B("Join", "Join selected lines/arcs that meet end to end into one polyline (JOIN)", JoinSelection));
            bar.Items.Add(B("Trim", "Cut lines back at the selected edges, or at everything if nothing is selected (TRIM)", () => StartTrimExtend(true)));
            bar.Items.Add(B("Extend", "Run line ends out to the selected boundaries, or to anything if nothing is selected (EXTEND)", () => StartTrimExtend(false)));
            bar.Items.Add(B("Fillet", "Round (or close) the corner between two lines (FILLET)", StartFillet));
            bar.Items.Add(B("Flip", "Move the selected bearing/distance labels to the other side of their course (FLIP)", FlipSelectedLabels));
            bar.Items.Add(B("Offset", "Parallel copy of lines, arcs, circles and polylines at a distance (OFFSET)", StartOffset));
            bar.Items.Add(new Separator());
            bar.Items.Add(new TextBlock { Text = "Layer:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
            bar.Items.Add(_layerCombo);
            bar.Items.Add(B("Set Layer", "Reassign the selected entities to the current layer", SetSelectionLayer));
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
                case "DIM": case "DIMALIGNED": case "DAL": StartDimension(); break;
                case "DIMLIN": case "DLI": StartDimension(linear: true); break;
                case "DIMRAD": case "DRA": StartRadiusDimension(); break;
                case "DIMDIA": case "DDI": StartRadiusDimension(diameter: true); break;
                case "DIMANG": case "DAN": StartAngularDimension(); break;
                case "MOVE": case "M": StartMove(); break;
                case "ROTATE": case "RO": StartRotate(); break;
                case "STRETCH": case "S": StartStretch(); break;
                case "COPY": case "CO": case "CP": StartCopy(); break;
                case "VXDEL": StartVertexDelete(); break;
                case "FLIP": case "FL": FlipSelectedLabels(); break;
                case "LABEL": case "LB": LabelSelection(); break;
                case "SELALL": case "ALL": SelectAll(); break;
                case "SELLAYER": case "SL": SelectByLayer(arg); break;
                case "AREA": case "AA": StartArea(); break;
                case "JOIN": case "J": JoinSelection(); break;
                case "ID": StartId(); break;
                case "VPSCALE": case "SCALE": StartSheetScale(arg); break;
                case "MVIEW": case "MV": case "VIEWPORT": StartMview(); break;
                case "VPINFO": ViewportInfo(arg); break;
                case "VXADD": StartVertexInsert(); break;
                case "MIRROR": case "MI": StartMirror(); break;
                case "OFFSET": case "O": StartOffset(); break;
                case "TRIM": case "TR": StartTrimExtend(true); break;
                case "EXTEND": case "EX": StartTrimExtend(false); break;
                case "FILLET": case "F": StartFillet(); break;
                case "ERASE": EraseSelected(); break;
                case "LAYER": SetSelectionLayer(); break;
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
            Log("  Drag an entity (a label, a line...) to move it - drag one of a selection to move them all; Ctrl+Z undoes");
            Log("  Drag a box: left-to-right takes what's fully inside, right-to-left anything it touches (Ctrl adds)");
            Log("  SELALL (Ctrl+A) everything in view · SELLAYER <layer>, or SELLAYER alone for the selection's own layer(s)");
            Log("  MOVE    select entities, MOVE, pick base point then destination");
            Log("  ROTATE  select entities, ROTATE, pick the pivot, type the angle in degrees (clockwise)");
            Log("  STRETCH select the line(s)/polyline sharing a vertex, STRETCH, pick the vertex then its new position");
            Log("  VXDEL / VXADD   select a polyline, then pick a vertex to remove / a spot on it to add one (also buttons in Properties)");
            Log("  LABEL   select lines/arcs/polylines, LABEL adds bearing & distance (or curve data) the way Draft does");
            Log("  FLIP    select bearing/distance/curve labels, FLIP moves them to the other side of their course");
            Log("  COPY    select entities, COPY, pick the base point then each destination (blank ends)");
            Log("  MIRROR  select entities, MIRROR, pick two points on the mirror line, then Y/N to erase the originals");
            Log("  OFFSET  select lines/arcs/circles/polylines, OFFSET, type the distance, pick the side");
            Log("  TRIM / EXTEND   select the edges (or nothing = everything), then pick lines to cut / lengthen (blank ends)");
            Log("  FILLET  type the radius (0 = sharp corner), then pick two lines on the parts to keep");
            Log("  LAYER   select entities, LAYER, moves them to the toolbar's current layer   · or the Set Layer button");
            Log("  LINE    pick or type E,N for the start, then BEARING DISTANCE for each leg, e.g. N45-30-00E 125.50 (blank ends)");
            Log("          C closes back to the start and reports misclosure, precision and area; U undoes the last leg");
            Log("  ID      pick points to read their N/E (and survey point number, elevation)");
            Log("  JOIN    select lines/arcs/polylines that meet end to end, JOIN makes one polyline (closed if it closes)");
            Log("  AREA    area and perimeter of the selected closed polylines/circles, or pick corners (blank ends)");
            Log("  ARC     pick three points on the arc: start, a point on it, end");
            Log("  TEXT    pick a point, then type the text (or \"height text\", e.g. \"0.25 LOT 5\")");
            Log("  LEADER  pick the feature point then the text position, then type the text");
            Log("  DIM     aligned dimension: pick two points, then the dimension line's position, then the text height");
            Log("  DIMLIN  linear dimension: as DIM, measuring dE or dN by where you place it (or type H, V or an angle)");
            Log("  DIMRAD / DIMDIA   radius / diameter dimension: pick on an arc or circle, then the text height");
            Log("  DIMANG  angle dimension: pick the vertex, a point on each leg, then the arc location (it picks which angle)");
            Log("  CLAYER <name>   set the layer new drawing picks up   · type the name into the toolbar's Layer box");
            Log("  ZE      zoom extents · wheel zooms · middle-drag or Shift-drag pans");
            Log("  VPINFO [sheet|ALL]   list each viewport on the sheet (all sheets from Model) and whether it shows model space");
            Log("  MVIEW   on a sheet tab: pick two corners, then a scale - shows model space on that sheet");
            Log("  VPSCALE [1:n]   change the current sheet's scale: viewport, title-block scale, scale bar, and label sizes");
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
                Log("  " + System.IO.Path.GetFileName(path) + ": " + doc.Layers.Count + " layers, " + doc.ModelSpace.Entities.Count() + " model entities");
                LogSheetContents(doc);
                SuggestJobFolder(path);
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

        /// <summary>
        /// Logs, per sheet, whether it actually has anything drawn on it - a legacy DWG
        /// typically carries several unused blank sheet-size options alongside the one
        /// actually plotted, and they look identical in the tab strip. Counts the display
        /// primitives FD-Draft would draw for each sheet (title block/frame linework only
        /// counts as "blank"; anything past a small handful of prims is a real plan).
        /// </summary>
        private void LogSheetContents(CadDocument doc)
        {
            var builder = new SceneBuilder(doc);
            var counts = new List<(string Name, int Prims)>();
            foreach (var name in SheetNames(doc).Skip(1))
            {
                try
                {
                    var scene = builder.Layout(name);
                    counts.Add((name, scene.Groups.Sum(g => g.Prims.Count)));
                }
                catch { /* a malformed layout just won't get a count */ }
            }
            if (counts.Count == 0) return;
            // A count of drawn items can't tell a plan from a busy title block, so say what's known:
            // whether each sheet has a viewport that actually shows model space.
            var withView = doc.Layouts.Where(l => l.IsPaperSpace && l.AssociatedBlock.Entities.OfType<Viewport>().Any(v => ViewportRules.ShowsModel(v, l)))
                .Select(l => l.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Log("  sheets: " + string.Join(", ", counts.Select(c => c.Name + " (" + c.Prims + " items" + (withView.Contains(c.Name) ? ", shows model space" : "") + ")")));
            if (withView.Count == 0)
                Log("  no sheet has a viewport showing model space - any plan on them is drawn straight onto the paper. MVIEW puts model space onto a sheet; VPINFO lists what each sheet's viewports are.");
        }

        /// <summary>
        /// If the opened file looks like one FD-Draft itself wrote (a job's own
        /// "&lt;job&gt;\export\fd-draft\&lt;job&gt;.dwg"), point the Draft dialog's job folder
        /// at that job so Ctrl+D re-drafts the same job instead of whatever job was drafted
        /// last (which may be unrelated, e.g. the sample job).
        /// </summary>
        private void SuggestJobFolder(string path)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(path);
                if (dir == null || !dir.EndsWith(System.IO.Path.Combine("export", "fd-draft"), StringComparison.OrdinalIgnoreCase)) return;
                var jobFolder = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(dir));
                if (jobFolder == null || !Directory.Exists(jobFolder)) return;
                _settings.LastJobFolder = jobFolder;
                _settings.Save();
                Log("  this looks like an FD-Draft output; Ctrl+D will re-draft " + jobFolder);
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException)
            {
                // best-effort convenience only
            }
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

        /// <summary>
        /// Drag-to-move: the dragged entities move by the drag, as one undo step. On a sheet the
        /// drag is in paper units, so model-space entities move by it as seen through the viewport
        /// the drag started in, and paper entities (title block, notes) by the paper distance.
        /// </summary>
        private void OnDragged(HashSet<ulong> handles, Vec2 from, Vec2 to)
        {
            if (_doc == null || _canvas.Scene == null || _activeTool.Length > 0) return;
            var entities = handles.Select(h => _doc.GetCadObject(h) as Entity).Where(e => e != null).Cast<Entity>().ToList();
            if (entities.Count == 0) return;
            var scene = _canvas.Scene;
            var vp = scene.IsPaper
                ? scene.Groups.FirstOrDefault(g => g.Clip.HasValue && g.ToModel.HasValue && from.X >= g.Clip.Value.X1 && from.X <= g.Clip.Value.X2 && from.Y >= g.Clip.Value.Y1 && from.Y <= g.Clip.Value.Y2)
                : null;
            var paperDelta = to - from;
            var modelDelta = vp != null ? vp.ToModel!.Value.Apply(to) - vp.ToModel.Value.Apply(from) : paperDelta;
            var inModel = entities.Where(e => e.Owner == _doc.ModelSpace).ToList();
            var onPaper = entities.Where(e => e.Owner != _doc.ModelSpace).ToList();
            if (scene.IsPaper && vp == null && inModel.Count > 0) { Log("  drag from inside the viewport to move model-space entities"); return; }
            var cmds = new List<IEditCommand>();
            if (inModel.Count > 0) cmds.Add(TransformEntitiesCommand.Move(inModel, modelDelta.X, modelDelta.Y, "Move"));
            if (onPaper.Count > 0) cmds.Add(TransformEntitiesCommand.Move(onPaper, paperDelta.X, paperDelta.Y, "Move"));
            _undo.Push(cmds.Count == 1 ? cmds[0] : new CompositeCommand(cmds, "Move"));
            // The moved entities stay selected, so they can be nudged again.
            _canvas.Selected.Clear();
            foreach (var h in handles) _canvas.Selected.Add(h);
            _dirty = true; UpdateTitle();
            Rebuild(fit: false);
            UpdateProperties();
            var shown = inModel.Count > 0 ? modelDelta : paperDelta;
            Log(string.Format(CultureInfo.InvariantCulture, "  moved {0}  dN {1:F3}  dE {2:F3}{3}  (Ctrl+Z to undo)",
                Plural(entities.Count, "entity", "entities"), shown.Y, shown.X, inModel.Count > 0 ? "" : " mm"));
        }

        private void OnBoxSelected(HashSet<ulong> handles, bool ctrl)
        {
            if (!ctrl) _canvas.Selected.Clear();
            foreach (var h in handles) _canvas.Selected.Add(h);
            UpdateProperties();
            _canvas.InvalidateVisual();
            if (handles.Count > 0) Log("  selected " + Plural(_canvas.Selected.Count, "entity", "entities"));
        }

        /// <summary>Every entity drawn in the current view (Model, or the current sheet and what
        /// shows through its viewports), as handles.</summary>
        private HashSet<ulong> VisibleHandles() =>
            _canvas.Scene == null ? new HashSet<ulong>() : new HashSet<ulong>(_canvas.Scene.AllPrims().Where(p => p.Handle != 0).Select(p => p.Handle));

        /// <summary>SELALL: select everything drawn in the current view.</summary>
        private void SelectAll()
        {
            _canvas.Selected.Clear();
            foreach (var h in VisibleHandles()) _canvas.Selected.Add(h);
            UpdateProperties();
            _canvas.InvalidateVisual();
            Log("  selected " + Plural(_canvas.Selected.Count, "entity", "entities"));
        }

        /// <summary>SELLAYER [name]: select everything in the current view on that layer - or,
        /// with no name, on the same layer(s) as the current selection ("select similar").</summary>
        private void SelectByLayer(string name)
        {
            if (_doc == null) return;
            var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (name.Length > 0) layers.Add(name);
            else foreach (var e in SelectedEntities()) layers.Add(e.Layer?.Name ?? "0");
            if (layers.Count == 0) { Log("  type SELLAYER <layer>, or select something on the layer first"); return; }
            _canvas.Selected.Clear();
            foreach (var h in VisibleHandles())
                if (_doc.GetCadObject(h) is Entity e && layers.Contains(e.Layer?.Name ?? "0")) _canvas.Selected.Add(h);
            UpdateProperties();
            _canvas.InvalidateVisual();
            Log("  selected " + Plural(_canvas.Selected.Count, "entity", "entities") + " on " + string.Join(", ", layers));
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

        private void SetSelectionLayer()
        {
            var entities = SelectedEntities();
            if (entities.Count == 0) { Log("  nothing selected - click an entity first"); return; }
            var layer = GetOrCreateLayer(CurrentLayer());
            _undo.Push(new ChangeLayerCommand(entities, layer, "Set layer to " + layer.Name));
            _dirty = true; UpdateTitle();
            Rebuild(fit: false);
            UpdateProperties();
            Log("  moved " + entities.Count + " entit" + (entities.Count == 1 ? "y" : "ies") + " to layer " + layer.Name + "  (Ctrl+Z to undo)");
        }

        /// <summary>Commits an edit made in the Properties tab's text box to the single
        /// selected TEXT or MTEXT entity, as an undo step.</summary>
        private void ApplySelectedTextEdit()
        {
            var entities = SelectedEntities();
            if (entities.Count != 1) return;
            string value = _textEdit.Text;
            switch (entities[0])
            {
                case TextEntity te when te.Value != value:
                    _undo.Push(new EditTextCommand(te, value, "Edit text"));
                    break;
                case MText mt when mt.Value != value:
                    _undo.Push(new EditTextCommand(mt, value, "Edit text"));
                    break;
                default:
                    return;
            }
            _dirty = true; UpdateTitle();
            Rebuild(fit: false);
            UpdateProperties();
            Log("  text updated  (Ctrl+Z to undo)");
        }

        /// <summary>Commits an edit made in the Properties tab's number box - a Circle/Arc's
        /// radius, or a TEXT/MTEXT's height - to the single selected entity, as an undo step.</summary>
        /// <summary>Commits every changed field in the Properties tab's numeric rows (built by
        /// <see cref="UpdateProperties"/> for the type of the single selected entity) as one
        /// undo step - so editing an arc's radius and both angles together is a single Ctrl+Z.</summary>
        private void ApplySelectedNumberEdit()
        {
            if (_numberRows.Count == 0) return;
            var edits = new List<IEditCommand>();
            foreach (var row in _numberRows)
            {
                if (!double.TryParse(row.Box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    Log("  \"" + row.Box.Text + "\" is not a number - that field was left as is");
                    continue;
                }
                if (Math.Abs(row.Current - value) <= 1e-12) continue;
                edits.Add(new SetPropertyCommand<double>(row.Current, value, row.Set, row.Description));
            }
            if (edits.Count == 0) return;
            _undo.Push(edits.Count == 1 ? edits[0] : new CompositeCommand(edits, "Edit properties"));
            _dirty = true; UpdateTitle();
            Rebuild(fit: false);
            UpdateProperties();
            Log("  updated " + edits.Count + " field" + (edits.Count == 1 ? "" : "s") + "  (Ctrl+Z to undo)");
        }

        /// <summary>Adds one label+textbox row to the Properties panel's numeric fields for the
        /// current single selection, tracked in <see cref="_numberRows"/> so
        /// <see cref="ApplySelectedNumberEdit"/> can commit every changed one together.</summary>
        private void AddNumberRow(string label, double current, Action<double> set, string description)
        {
            var box = new TextBox { Text = current.ToString("0.####", CultureInfo.InvariantCulture), Margin = new Thickness(4, 0, 4, 4) };
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { ApplySelectedNumberEdit(); e.Handled = true; } };
            _numberFields.Children.Add(new TextBlock { Text = label, Margin = new Thickness(4, 4, 4, 0), Foreground = Brushes.Gray, FontSize = 11 });
            _numberFields.Children.Add(box);
            _numberRows.Add((box, current, set, description));
        }

        /// <summary>Keeps <see cref="_propertiesVertexIndex"/> in range for a polyline with
        /// <paramref name="count"/> vertices (the selection can change to a shorter polyline
        /// while a later index was showing).</summary>
        private int ClampVertexIndex(int count)
        {
            if (_propertiesVertexIndex < 0) _propertiesVertexIndex = 0;
            if (_propertiesVertexIndex >= count) _propertiesVertexIndex = count - 1;
            return _propertiesVertexIndex;
        }

        /// <summary>Adds the "Vertex #" selector (type a 0-based index, Enter to jump to it) plus
        /// E/N fields for that one vertex of a selected LwPolyline/Polyline2D - STRETCH moves a
        /// vertex by picking it, this is the type-in alternative, AutoCAD-Properties-style.</summary>
        private void AddVertexRows(VertexRef vref, int count)
        {
            var indexBox = new TextBox { Text = _propertiesVertexIndex.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(4, 0, 4, 4) };
            indexBox.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                if (int.TryParse(indexBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
                {
                    _propertiesVertexIndex = idx;
                    UpdateProperties();
                }
                e.Handled = true;
            };
            _numberFields.Children.Add(new TextBlock { Text = "Vertex # (0.." + (count - 1) + "), Enter to jump:", Margin = new Thickness(4, 4, 4, 0), Foreground = Brushes.Gray, FontSize = 11 });
            _numberFields.Children.Add(indexBox);
            var p = vref.Get();
            AddNumberRow("Vertex E:", p.X, v => vref.Set(new XYZ(v, vref.Get().Y, 0)), "Set vertex position");
            AddNumberRow("Vertex N:", p.Y, v => vref.Set(new XYZ(vref.Get().X, v, 0)), "Set vertex position");
            if (vref.Entity is LwPolyline || vref.Entity is Polyline2D)
            {
                var lp = vref.Entity;
                int idx = vref.Index;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
                var del = new Button { Content = "Delete vertex", Margin = new Thickness(4, 0, 4, 0), Padding = new Thickness(6, 2, 6, 2), IsEnabled = PolylineVertices.CanDelete(lp), ToolTip = "Remove this vertex; its two spans become one straight span (VXDEL picks one instead)" };
                bool hasSpan = PolylineVertices.IsClosed(lp) || idx < count - 1;
                var ins = new Button { Content = "Insert after", Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(6, 2, 6, 2), IsEnabled = hasSpan, ToolTip = "Add a vertex halfway along the span after this one - an arc is split on its curve (VXADD picks the spot instead)" };
                del.Click += (s, e) => DeletePolylineVertex(lp, idx);
                ins.Click += (s, e) => InsertPolylineVertex(lp, idx, EntityOps.SpansOf(lp).ElementAt(idx).Midpoint);
                row.Children.Add(del); row.Children.Add(ins);
                _numberFields.Children.Add(row);
            }
        }

        private void DeletePolylineVertex(Entity poly, int idx)
        {
            if (!PolylineVertices.CanDelete(poly)) { Log("  that polyline is down to its minimum vertices - erase the whole entity instead (Del)"); return; }
            var at = new VertexRef(poly, idx).Get();
            var cmd = PolylineVertices.Delete(poly, idx, "Delete vertex");
            AfterVertexEdit(poly, cmd, Math.Max(0, idx - 1));
            Log("  deleted vertex " + idx + " at " + NE(new Vec2(at.X, at.Y)) + "  (Ctrl+Z to undo)");
        }

        private void InsertPolylineVertex(Entity poly, int afterIndex, Vec2 at)
        {
            var cmd = PolylineVertices.Insert(poly, afterIndex, at, "Insert vertex", out int newIndex);
            AfterVertexEdit(poly, cmd, newIndex);
            Log("  inserted vertex " + newIndex + " at " + NE(at) + "  (Ctrl+Z to undo)");
        }

        /// <summary>Records a vertex edit and keeps the polyline selected - a Polyline2D edit
        /// swaps in a rebuilt entity, so the selection moves to that.</summary>
        private void AfterVertexEdit(Entity poly, IEditCommand cmd, int showIndex)
        {
            _undo.Push(cmd);
            if (cmd is ReplacePolyline2DCommand r && _canvas.Selected.Remove(poly.Handle)) _canvas.Selected.Add(r.Replacement.Handle);
            _propertiesVertexIndex = showIndex;
            _dirty = true; UpdateTitle();
            Rebuild(fit: false);
            UpdateProperties();
        }

        /// <summary>The selected polylines (either kind), for VXDEL/VXADD.</summary>
        private List<Entity> SelectedPolylines() => SelectedEntities().Where(e => e is LwPolyline || e is Polyline2D).ToList();

        private void StartVertexDelete()
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            var polys = SelectedPolylines();
            if (polys.Count == 0) { Log("  select the polyline first, then type VXDEL"); return; }
            BeginTool("VXDEL");
            _prompt.Text = "Delete vertex - pick the vertex:";
            Log("VXDEL  pick the polyline vertex to remove (snap helps; Esc ends)");
            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                double tol = Math.Max(6 / _canvas.View.Zoom, 1e-6);
                Entity? best = null; int bestIdx = -1; double bestD = double.MaxValue;
                foreach (var lp in polys.ToList())
                {
                    if (lp.Owner == null) continue; // swapped out by an earlier Polyline2D edit
                    int i = PolylineVertices.NearestVertex(lp, model.Value, out double d);
                    if (i >= 0 && d < bestD) { bestD = d; best = lp; bestIdx = i; }
                }
                if (best == null || bestD > tol) { Log("  no vertex of the selected polyline there - pick closer, or snap (F3)"); return; }
                DeletePolylineVertex(best, bestIdx);
                polys = SelectedPolylines(); // a Polyline2D was replaced by a rebuilt one
                // Stays active for the next vertex.
            };
        }

        private void StartVertexInsert()
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            var polys = SelectedPolylines();
            if (polys.Count == 0) { Log("  select the polyline first, then type VXADD"); return; }
            BeginTool("VXADD");
            _prompt.Text = "Add vertex - pick a point on the polyline:";
            Log("VXADD  pick where on the polyline to add a vertex - it lands on the nearest span (on the curve, for an arc; Esc ends)");
            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                double tol = Math.Max(10 / _canvas.View.Zoom, 1e-6);
                Entity? best = null; int bestSpan = -1; double bestD = double.MaxValue; Vec2 at = model.Value;
                foreach (var lp in polys)
                {
                    if (lp.Owner == null) continue;
                    int i = PolylineVertices.NearestSpan(lp, model.Value, out var on, out double d);
                    if (i >= 0 && d < bestD) { bestD = d; best = lp; bestSpan = i; at = on; }
                }
                if (best == null || bestD > tol) { Log("  that's not on the selected polyline - pick on one of its spans"); return; }
                InsertPolylineVertex(best, bestSpan, at);
                polys = SelectedPolylines();
            };
        }

        private void UpdateProperties()
        {
            var entities = SelectedEntities();
            _textEdit.Text = "";
            _textEdit.IsEnabled = false;
            _textEditApply.IsEnabled = false;
            _numberFields.Children.Clear();
            _numberRows.Clear();
            _numberEditApply.IsEnabled = false;
            _numberEditApply.Visibility = Visibility.Collapsed;
            if (entities.Count == 1)
            {
                string? content = entities[0] switch { TextEntity te => te.Value, MText mt => mt.Value, _ => null };
                if (content != null)
                {
                    _textEdit.Text = content;
                    _textEdit.IsEnabled = true;
                    _textEditApply.IsEnabled = true;
                }
                const double toDeg = 180.0 / Math.PI, toRad = Math.PI / 180.0;
                switch (entities[0])
                {
                    // Arc must come before Circle: Arc derives from Circle in ACadSharp.
                    case Arc a:
                        AddNumberRow("Radius:", a.Radius, v => a.Radius = v, "Set radius");
                        AddNumberRow("Start angle (deg):", a.StartAngle * toDeg, v => a.StartAngle = v * toRad, "Set start angle");
                        AddNumberRow("End angle (deg):", a.EndAngle * toDeg, v => a.EndAngle = v * toRad, "Set end angle");
                        break;
                    case Circle c:
                        AddNumberRow("Radius:", c.Radius, v => c.Radius = v, "Set radius");
                        break;
                    case TextEntity te:
                        AddNumberRow("Text height:", te.Height, v => te.Height = v, "Set text height");
                        AddNumberRow("Rotation (deg):", te.Rotation * toDeg, v => te.Rotation = v * toRad, "Set text rotation");
                        break;
                    case MText mt:
                        AddNumberRow("Text height:", mt.Height, v => mt.Height = v, "Set text height");
                        // MText.Rotation is read-only, derived from AlignmentPoint as a direction
                        // vector (not a position) - set that instead to change it.
                        AddNumberRow("Rotation (deg):", mt.Rotation * toDeg, v => mt.AlignmentPoint = new XYZ(Math.Cos(v * toRad), Math.Sin(v * toRad), 0), "Set text rotation");
                        break;
                    case Line ln:
                        // Editing an endpoint here does not drag anything connected to it, unlike
                        // STRETCH - this is for typing an exact coordinate, AutoCAD-Properties-style.
                        AddNumberRow("Start E:", ln.StartPoint.X, v => ln.StartPoint = new XYZ(v, ln.StartPoint.Y, ln.StartPoint.Z), "Set line start");
                        AddNumberRow("Start N:", ln.StartPoint.Y, v => ln.StartPoint = new XYZ(ln.StartPoint.X, v, ln.StartPoint.Z), "Set line start");
                        AddNumberRow("End E:", ln.EndPoint.X, v => ln.EndPoint = new XYZ(v, ln.EndPoint.Y, ln.EndPoint.Z), "Set line end");
                        AddNumberRow("End N:", ln.EndPoint.Y, v => ln.EndPoint = new XYZ(ln.EndPoint.X, v, ln.EndPoint.Z), "Set line end");
                        break;
                    case LwPolyline lp when lp.Vertices.Count > 0:
                        AddVertexRows(new VertexRef(lp, ClampVertexIndex(lp.Vertices.Count)), lp.Vertices.Count);
                        break;
                    case Polyline2D p2 when p2.Vertices.Count > 0:
                        AddVertexRows(new VertexRef(p2, ClampVertexIndex(p2.Vertices.Count)), p2.Vertices.Count);
                        break;
                }
                if (_numberRows.Count > 0)
                {
                    _numberEditApply.Visibility = Visibility.Visible;
                    _numberEditApply.IsEnabled = true;
                }
            }
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

        /// <summary>
        /// Where new linework/text drawn on the canvas belongs: Model space, unless the current
        /// sheet is a layout with no working viewport at all - a real MSCAD job commonly draws
        /// its plan straight onto paper on the sheet it actually used (see the empty-layout note
        /// in docs/ARCHITECTURE.md), so new drafting there has to land in that layout's own block
        /// too, in the same paper coordinates <see cref="Scene.ModelAt"/> already hands back.
        /// </summary>
        private BlockRecord CurrentEntityOwner()
        {
            if (_sheet == "Model" || _sheet.Length == 0 || _canvas.Scene == null) return _doc!.ModelSpace;
            if (_canvas.Scene.Groups.Any(g => g.Clip.HasValue && g.ToModel.HasValue)) return _doc!.ModelSpace;
            return _doc!.Layouts.First(l => l.Name.Equals(_sheet, StringComparison.OrdinalIgnoreCase)).AssociatedBlock;
        }

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
            Log("LINE  on layer " + layer + " - pick the start point, or type E,N (blank/Esc ends; C closes back to the start with a closure report; U undoes the last leg)");
            _prompt.Text = "Line - start point:";
            Vec2? cur = null;
            // Every point the traverse has visited, start first - for C (close) and U (undo leg).
            var visited = new List<Vec2>();
            const string nextPrompt = "Line - next bearing distance (C close, U undo, blank ends):";

            void Leg(Vec2 to)
            {
                var from = cur!.Value;
                var line = new Line(new XYZ(from.X, from.Y, 0), new XYZ(to.X, to.Y, 0)) { Layer = GetOrCreateLayer(layer) };
                _undo.Push(new AddEntitiesCommand(CurrentEntityOwner(), new Entity[] { line }, "Line"));
                cur = to;
                visited.Add(to);
                _canvas.RubberFrom = to;
                _dirty = true; UpdateTitle();
                Rebuild(fit: false);
                Log("  " + InverseResult.Between(from, to, _std?.BearingRotationDeg ?? 0));
                _prompt.Text = nextPrompt;
            }

            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                if (cur == null)
                {
                    cur = model.Value; visited.Add(model.Value); _canvas.RubberFrom = p; _canvas.ToolActive = false;
                    Log("  start " + NE(model.Value));
                    _prompt.Text = nextPrompt;
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
                        visited.Add(cur.Value);
                        Log("  start " + NE(cur.Value));
                        _prompt.Text = nextPrompt;
                    }
                    else Log("  type E,N or click a start point");
                    return;
                }
                if (s.Length == 0) { EndTool(); Log("  *line complete*"); return; }
                if (s.Equals("U", StringComparison.OrdinalIgnoreCase) || s.Equals("UNDO", StringComparison.OrdinalIgnoreCase))
                {
                    if (visited.Count < 2) { Log("  no leg to undo yet"); return; }
                    _undo.Undo();
                    visited.RemoveAt(visited.Count - 1);
                    cur = visited[visited.Count - 1];
                    _dirty = true; UpdateTitle();
                    Rebuild(fit: false);
                    Log("  last leg removed - back at " + NE(cur.Value));
                    return;
                }
                if (s.Equals("C", StringComparison.OrdinalIgnoreCase) || s.Equals("CLOSE", StringComparison.OrdinalIgnoreCase))
                {
                    if (visited.Count < 3) { Log("  a closure needs at least two legs"); return; }
                    var report = ClosureReport.Of(visited);
                    var start = visited[0];
                    Log(string.Format(CultureInfo.InvariantCulture, "  misclosure {0:F3}  (dN {1:F3}  dE {2:F3})  over {3:F3} of traverse - precision {4}",
                        report.Misclosure, report.DeltaN, report.DeltaE, report.TraverseLength, ClosureReport.FormatPrecision(report.Precision)));
                    if (report.Misclosure > 1e-9) Leg(start); // the closing course itself, logged as an inverse
                    Log("  closed figure: area " + AreaText(report.Area) + ", perimeter " + (report.TraverseLength + report.Misclosure).ToString("F3", CultureInfo.InvariantCulture));
                    EndTool();
                    return;
                }
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
                _undo.Push(new AddEntitiesCommand(CurrentEntityOwner(), new Entity[] { entity }, "Arc"));
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
                _undo.Push(new AddEntitiesCommand(CurrentEntityOwner(), new Entity[] { entity }, "Text"));
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
                // A real ACadSharp Leader, not hand-drawn lines: it reads back as an actual
                // leader in AutoCAD/MSCAD, with its own arrowhead driven by the dimension style
                // (Style resolves against the document's DimensionStyles - registering "Standard"
                // if it isn't there yet - once this is added to a block; see TestLeaderEntityAddsAndRenders).
                var leader = new Leader { ArrowHeadEnabled = true, Layer = GetOrCreateLayer(layer), Style = DimensionStyle.Default };
                leader.Vertices.Add(new XYZ(tip.Value.X, tip.Value.Y, 0));
                leader.Vertices.Add(new XYZ(end.X, end.Y, 0));
                added.Add(leader);
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
                _undo.Push(new AddEntitiesCommand(CurrentEntityOwner(), added, "Leader"));
                _dirty = true; UpdateTitle();
                EndTool();
                Rebuild(fit: false);
                Log("  leader placed");
            };
        }

        private double _lastDimHeight = DimensionBuilder.DefaultTextHeight;

        /// <summary>The last step every dimension tool shares: type the text height (Enter
        /// keeps the last one), then build and add the dimension.</summary>
        private void AskDimensionHeightThen(Func<Dimension> build, string layer)
        {
            _canvas.ToolActive = false; _canvas.RubberFrom = null; _awaitingPoint = null;
            string def = _lastDimHeight.ToString("0.###", CultureInfo.InvariantCulture);
            _prompt.Text = "Dimension - text height <" + def + ">:";
            Log("  type the text height (Enter = " + def + ")");
            _awaitingLine = s =>
            {
                double h = _lastDimHeight;
                if (s.Length > 0 && (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out h) || h <= 0)) { Log("  type a positive height, or Enter for " + def); return; }
                _lastDimHeight = h;
                var dim = build();
                dim.Layer = GetOrCreateLayer(layer);
                _undo.Push(new AddDimensionCommand(CurrentEntityOwner(), dim, h, "Dimension"));
                _dirty = true; UpdateTitle();
                EndTool();
                Rebuild(fit: false);
                Log("  dimension " + dim.Text + " placed");
            };
        }

        /// <summary>DIM (aligned) and DIMLIN (linear): two points, then where the dimension
        /// line goes - for DIMLIN, typing H, V or an angle first fixes the direction measured.</summary>
        private void StartDimension(bool linear = false)
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            string layer = CurrentLayer();
            string name = linear ? "DIMLIN" : "DIM";
            BeginTool(name);
            Log(name + "  " + (linear ? "linear" : "aligned") + " dimension on layer " + layer + " - pick the two points to measure between (snap helps), then where the dimension line goes (Esc to cancel)");
            _prompt.Text = "Dimension - first point:";
            var pts = new List<Vec2>();
            double? rotation = null;
            int decimals = _std?.DistanceDecimals ?? 3;
            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                if (pts.Count == 1 && Vec2.Distance(pts[0], model.Value) < 1e-9) { Log("  that's the same point - pick the second point"); return; }
                pts.Add(model.Value);
                if (pts.Count == 1) { _canvas.RubberFrom = p; _prompt.Text = "Dimension - second point:"; Log("  from " + NE(model.Value)); return; }
                if (pts.Count == 2)
                {
                    _prompt.Text = linear ? "Dimension - line location (or type H, V, or an angle in degrees):" : "Dimension - dimension line location:";
                    Log("  to   " + NE(model.Value) + "  " + InverseResult.Between(pts[0], pts[1], _std?.BearingRotationDeg ?? 0)
                        + (linear ? " - pick where the line goes; above/below the points measures dE, beside them dN (or type H / V / an angle first)" : ""));
                    return;
                }
                var at = model.Value;
                AskDimensionHeightThen(() => linear
                    ? DimensionBuilder.Linear(pts[0], pts[1], at, rotation, decimals)
                    : DimensionBuilder.Aligned(pts[0], pts[1], at, decimals), layer);
            };
            if (linear)
            {
                _awaitingLine = s =>
                {
                    if (pts.Count < 2) { Log("  pick the two points first"); return; }
                    var t = s.Trim().ToUpperInvariant();
                    if (t == "H") rotation = 0;
                    else if (t == "V") rotation = Math.PI / 2;
                    else if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double deg)) rotation = deg * Math.PI / 180;
                    else { Log("  type H, V or an angle in degrees (CCW from east), or pick the line location"); return; }
                    Log("  measuring along " + (rotation.Value * 180 / Math.PI).ToString("0.####", CultureInfo.InvariantCulture) + "° - now pick where the line goes");
                };
            }
        }

        /// <summary>DIMRAD: pick an arc or circle where the radius line should point, then the
        /// text height.</summary>
        private void StartRadiusDimension(bool diameter = false)
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            string layer = CurrentLayer();
            string name = diameter ? "DIMDIA" : "DIMRAD";
            BeginTool(name);
            _prompt.Text = (diameter ? "Diameter" : "Radius") + " dimension - pick the arc or circle:";
            Log(name + "  pick on an arc or circle, where the " + (diameter ? "diameter" : "radius") + " line should point (Esc to cancel)");
            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                double tol = Math.Max(10 / _canvas.View.Zoom, 1e-6);
                Circle? best = null; double bestD = tol;
                foreach (var c in CurrentEntityOwner().Entities.OfType<Circle>())
                {
                    double d = Math.Abs(Vec2.Distance(model.Value, new Vec2(c.Center.X, c.Center.Y)) - c.Radius);
                    if (d < bestD) { bestD = d; best = c; }
                }
                if (best == null) { Log("  no arc or circle there - pick on the curve"); return; }
                var center = new Vec2(best.Center.X, best.Center.Y);
                double radius = best.Radius;
                var toward = model.Value;
                Log("  " + (best is Arc ? "arc" : "circle") + " R " + radius.ToString("F3", CultureInfo.InvariantCulture) + " centred " + NE(center));
                int dec = _std?.DistanceDecimals ?? 3;
                AskDimensionHeightThen(() => diameter ? DimensionBuilder.Diameter(center, radius, toward, dec) : DimensionBuilder.Radius(center, radius, toward, dec), layer);
            };
        }

        /// <summary>DIMANG: pick the vertex, a point on each leg, then where the arc goes (which
        /// also chooses which of the two angles is measured), then the text height.</summary>
        private void StartAngularDimension()
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            string layer = CurrentLayer();
            BeginTool("DIMANG");
            _prompt.Text = "Angle dimension - vertex:";
            Log("DIMANG  pick the vertex, a point on each leg (snap helps), then where the arc goes (Esc to cancel)");
            var pts = new List<Vec2>();
            string[] prompts = { "Angle dimension - point on the first leg:", "Angle dimension - point on the second leg:", "Angle dimension - arc location (inside the angle you want):" };
            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                if (pts.Count >= 1 && pts.Count <= 2 && Vec2.Distance(pts[0], model.Value) < 1e-9) { Log("  that's the vertex - pick a point along the leg"); return; }
                pts.Add(model.Value);
                if (pts.Count == 1) { _canvas.RubberFrom = p; Log("  vertex " + NE(model.Value)); }
                if (pts.Count < 4) { _prompt.Text = prompts[pts.Count - 1]; return; }
                var (_, sweep) = DimensionBuilder.AngularSweep(pts[0], pts[1], pts[2], pts[3]);
                Log("  angle " + DimensionBuilder.Dms(sweep).Replace("%%d", "°"));
                AskDimensionHeightThen(() => DimensionBuilder.Angular(pts[0], pts[1], pts[2], pts[3]), layer);
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

        private void StartStretch()
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            var entities = SelectedEntities();
            if (entities.Count == 0) { Log("  select the line(s)/polyline sharing the vertex first, then type STRETCH"); return; }
            BeginTool("STRETCH");
            _prompt.Text = "Stretch - pick the vertex to move:";
            Log("STRETCH  " + entities.Count + " entit" + (entities.Count == 1 ? "y" : "ies") + " selected - pick the shared vertex (snap on helps), then its new position (Esc to cancel)");
            List<VertexRef>? verts = null;
            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                var pt = new XYZ(model.Value.X, model.Value.Y, 0);
                if (verts == null)
                {
                    // 6 screen pixels of slack for an unsnapped click; an exact snap already lands on it.
                    double tol = Math.Max(6 / _canvas.View.Zoom, 1e-6);
                    verts = VertexEditing.FindCoincident(entities, pt, tol);
                    if (verts.Count == 0) { Log("  no endpoint of the selection is there - pick closer, or snap (F3)"); verts = null; return; }
                    _canvas.RubberFrom = p;
                    _prompt.Text = "Stretch - new position:";
                    Log("  vertex " + NE(model.Value) + " (" + verts.Count + " endpoint" + (verts.Count == 1 ? "" : "s") + ") - pick its new position");
                    return;
                }
                _undo.Push(new StretchVertexCommand(verts, pt, "Stretch"));
                _dirty = true; UpdateTitle();
                EndTool();
                _canvas.Selected.Clear();
                Rebuild(fit: false);
                Log("  stretched to " + NE(model.Value));
            };
        }

        private void StartCopy()
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            var entities = SelectedEntities();
            if (entities.Count == 0) { Log("  select entities to copy first, then type COPY"); return; }
            BeginTool("COPY");
            _prompt.Text = "Copy - base point:";
            Log("COPY  " + Plural(entities.Count, "entity", "entities") + " - pick the base point, then each destination (blank/Esc ends)");
            Vec2? basePt = null;
            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                if (basePt == null)
                {
                    basePt = model.Value; _canvas.RubberFrom = p; _prompt.Text = "Copy - destination (blank ends):";
                    Log("  base " + NE(model.Value));
                    return;
                }
                double dx = model.Value.X - basePt.Value.X, dy = model.Value.Y - basePt.Value.Y;
                var pairs = EntityOps.Copies(entities, dx, dy);
                var cmd = EntityOps.AddBesideSources(pairs, "Copy " + pairs.Count);
                if (cmd == null) { Log("  nothing copyable in the selection"); return; }
                _undo.Push(cmd);
                _dirty = true; UpdateTitle();
                Rebuild(fit: false);
                Log(string.Format(CultureInfo.InvariantCulture, "  copied {0}  dN {1:F3}  dE {2:F3}", pairs.Count, dy, dx)
                    + (pairs.Count < entities.Count ? "  (" + (entities.Count - pairs.Count) + " dimension(s) skipped)" : ""));
                // Stays active for more copies from the same base point, like AutoCAD's COPY.
            };
            _awaitingLine = s => { if (s.Length == 0) { EndTool(); Log("  *copy complete*"); } else Log("  pick a destination, or blank to end"); };
        }

        private void StartMirror()
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            var entities = SelectedEntities();
            if (entities.Count == 0) { Log("  select entities to mirror first, then type MIRROR"); return; }
            BeginTool("MIRROR");
            _prompt.Text = "Mirror - first point of the mirror line:";
            Log("MIRROR  " + Plural(entities.Count, "entity", "entities") + " - pick two points on the mirror line (Esc to cancel)");
            Vec2? first = null;
            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                if (first == null)
                {
                    first = model.Value; _canvas.RubberFrom = p; _prompt.Text = "Mirror - second point of the mirror line:";
                    Log("  from " + NE(model.Value));
                    return;
                }
                var a = first.Value; var b = model.Value;
                if (Vec2.Distance(a, b) < 1e-9) { Log("  the two points coincide - pick a different second point"); return; }
                var pairs = new List<(Entity Source, Entity Copy)>();
                foreach (var e in entities)
                {
                    var m = EntityOps.Mirrored(e, a, b);
                    if (m != null) pairs.Add((e, m));
                }
                if (pairs.Count == 0) { EndTool(); Log("  nothing in the selection can be mirrored"); return; }
                _canvas.ToolActive = false; _canvas.RubberFrom = null;
                _prompt.Text = "Mirror - erase the source objects? [Y/N] <N>:";
                Log("  mirror line " + InverseResult.Between(a, b, _std?.BearingRotationDeg ?? 0) + " - erase the originals? Y/N (Enter = N)");
                _awaitingPoint = null;
                _awaitingLine = s =>
                {
                    bool erase = s.Trim().StartsWith("Y", StringComparison.OrdinalIgnoreCase);
                    var add = EntityOps.AddBesideSources(pairs, "Mirror " + pairs.Count)!;
                    IEditCommand cmd = add;
                    if (erase) cmd = new CompositeCommand(new[] { add, new RemoveEntitiesCommand(pairs.Select(x => x.Source).ToList(), "Erase") }, "Mirror " + pairs.Count);
                    _undo.Push(cmd);
                    _dirty = true; UpdateTitle();
                    EndTool();
                    _canvas.Selected.Clear();
                    Rebuild(fit: false);
                    UpdateProperties();
                    Log("  mirrored " + Plural(pairs.Count, "entity", "entities") + (erase ? ", originals erased" : "")
                        + (pairs.Count < entities.Count ? "  (" + (entities.Count - pairs.Count) + " not a mirrorable type, left out)" : ""));
                };
            };
        }

        private double _lastOffset = 1.0;

        private void StartOffset()
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            var entities = SelectedEntities();
            if (entities.Count == 0) { Log("  select the line(s), arc(s), circle(s) or polyline(s) to offset first, then type OFFSET"); return; }
            BeginTool("OFFSET");
            _canvas.ToolActive = false;
            string def = _lastOffset.ToString("0.###", CultureInfo.InvariantCulture);
            _prompt.Text = "Offset - distance <" + def + ">:";
            Log("OFFSET  " + Plural(entities.Count, "entity", "entities") + " - type the distance (Enter = " + def + "), then pick the side to offset toward");
            double dist = 0;
            _awaitingLine = s =>
            {
                if (s.Length == 0) dist = _lastOffset;
                else if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out dist) || dist <= 0)
                {
                    Log("  type a positive distance");
                    return;
                }
                _lastOffset = dist;
                _awaitingLine = null;
                _canvas.ToolActive = true;
                _prompt.Text = "Offset - pick the side to offset toward:";
                Log("  distance " + dist.ToString("0.###", CultureInfo.InvariantCulture) + " - pick a point on the side to offset toward");
            };
            _awaitingPoint = p =>
            {
                if (dist <= 0) { Log("  type the distance first"); return; }
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                var pairs = new List<(Entity Source, Entity Copy)>();
                foreach (var e in entities)
                {
                    var o = EntityOps.Offset(e, dist, model.Value);
                    if (o != null) pairs.Add((e, o));
                }
                EndTool();
                if (pairs.Count == 0) { Log("  nothing offset - OFFSET handles lines, arcs, circles and lightweight polylines (and won't collapse a curve past its centre)"); return; }
                _undo.Push(EntityOps.AddBesideSources(pairs, "Offset " + pairs.Count)!);
                _dirty = true; UpdateTitle();
                Rebuild(fit: false);
                Log("  offset " + Plural(pairs.Count, "entity", "entities") + " by " + dist.ToString("0.###", CultureInfo.InvariantCulture)
                    + (pairs.Count < entities.Count ? "  (" + (entities.Count - pairs.Count) + " skipped: unsupported type or collapsed curve)" : ""));
            };
        }

        /// <summary>An area in the drawing's units as a plan would state it: m² and ha for a metric
        /// job, ft² and acres for a feet job (the drawing is assumed metric with no job open).</summary>
        private string AreaText(double area)
        {
            var units = _job?.Settings.Units ?? JobUnits.Meters;
            string format = units == JobUnits.Meters ? "{m2} m² ({ha} ha)" : "{ft2} ft² ({ac} ac)";
            return FdDraft.Core.Drafting.Annotator.FormatArea(format, area, units);
        }

        /// <summary>AREA: reports the area and perimeter of each selected closed polyline or
        /// circle; with nothing closed selected, measures a figure picked point by point.</summary>
        private void StartArea()
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            var figures = new List<(string What, double Area, double Perimeter)>();
            foreach (var e in SelectedEntities())
            {
                switch (e)
                {
                    case LwPolyline lp when lp.IsClosed && lp.Vertices.Count >= 3:
                    {
                        var pts = lp.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList();
                        var bl = lp.Vertices.Select(v => v.Bulge).ToList();
                        figures.Add(("polyline on " + (lp.Layer?.Name ?? "0"), FigureMeasure.Area(pts, bl), FigureMeasure.Perimeter(pts, bl, true)));
                        break;
                    }
                    case Polyline2D p2 when p2.IsClosed && p2.Vertices.Count >= 3:
                    {
                        var pts = p2.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList();
                        var bl = p2.Vertices.Select(v => v.Bulge).ToList();
                        figures.Add(("polyline on " + (p2.Layer?.Name ?? "0"), FigureMeasure.Area(pts, bl), FigureMeasure.Perimeter(pts, bl, true)));
                        break;
                    }
                    case Arc:
                        break;
                    case Circle c:
                        figures.Add(("circle on " + (c.Layer?.Name ?? "0"), Math.PI * c.Radius * c.Radius, 2 * Math.PI * c.Radius));
                        break;
                }
            }
            if (figures.Count > 0)
            {
                foreach (var f in figures)
                    Log("  " + f.What + ": area " + AreaText(f.Area) + ", perimeter " + f.Perimeter.ToString("F3", CultureInfo.InvariantCulture));
                if (figures.Count > 1) Log("  total area " + AreaText(figures.Sum(f => f.Area)));
                return;
            }
            BeginTool("AREA");
            _prompt.Text = "Area - first corner:";
            Log("AREA  no closed polyline selected - pick the corners in order (snap helps); blank ends and reports");
            var corners = new List<Vec2>();
            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                corners.Add(model.Value);
                _canvas.RubberFrom = p;
                _prompt.Text = "Area - next corner (blank ends):";
                if (corners.Count >= 3)
                    Log("  corner " + corners.Count + " " + NE(model.Value) + " - running area " + AreaText(FigureMeasure.Area(corners, null)));
                else Log("  corner " + corners.Count + " " + NE(model.Value));
            };
            _awaitingLine = s =>
            {
                if (s.Length > 0) { Log("  pick the next corner, or blank to finish"); return; }
                EndTool();
                if (corners.Count < 3) { Log("  *cancelled - an area needs at least three corners*"); return; }
                Log("  area " + AreaText(FigureMeasure.Area(corners, null)) + ", perimeter " + FigureMeasure.Perimeter(corners, null, true).ToString("F3", CultureInfo.InvariantCulture) + " (" + corners.Count + " corners)");
            };
        }

        /// <summary>MVIEW: put model space onto the current sheet - pick two corners on the paper,
        /// then a scale (Enter fits the whole survey in at the next standard scale).</summary>
        private void StartMview()
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            var layout = _sheet == "Model" ? null : _doc.Layouts.FirstOrDefault(l => l.Name.Equals(_sheet, StringComparison.OrdinalIgnoreCase));
            if (layout == null) { Log("  switch to the sheet tab you want the plan on first - MVIEW makes a viewport on a sheet, not in Model"); return; }
            var model = new SceneBuilder(_doc, _hidden).Model();
            if (!model.AllPrims().Any()) { Log("  model space is empty - there's nothing to show through a viewport"); return; }
            var std = LabelStandards();
            BeginTool("MVIEW");
            _prompt.Text = "Viewport - first corner on the sheet:";
            Log("MVIEW  pick two opposite corners of the viewport on " + _sheet + " (inside the frame, clear of the title block)");
            Vec2? first = null;
            _awaitingPoint = p =>
            {
                // Corners are paper points, even where an existing viewport would map them to model.
                if (first == null) { first = p; _canvas.RubberFrom = p; _prompt.Text = "Viewport - opposite corner:"; return; }
                var area = new FdDraft.Core.Standards.Rect(first.Value.X, first.Value.Y, p.X, p.Y);
                if (area.Width < 5 || area.Height < 5) { Log("  that box is too small - pick the opposite corner further away"); return; }
                var b = model.Bounds;
                double fitMpp = SheetViewports.FitScale(b.Width, b.Height, area, std.PaperUnitsPerMm, out double fitDen);
                var center = new Vec2((b.X1 + b.X2) / 2, (b.Y1 + b.Y2) / 2);
                _canvas.ToolActive = false; _canvas.RubberFrom = null; _awaitingPoint = null;
                string fit = fitDen.ToString("0.###", CultureInfo.InvariantCulture);
                _prompt.Text = "Viewport - scale 1:n <fit 1:" + fit + ">:";
                Log(string.Format(CultureInfo.InvariantCulture, "  {0:0} x {1:0} on paper - type the scale (e.g. 1:250), or Enter for 1:{2}, which fits the whole survey", area.Width, area.Height, fit));
                _awaitingLine = s =>
                {
                    double mpp = fitMpp, den = fitDen;
                    var t = s.Trim();
                    if (t.Length > 0)
                    {
                        int colon = t.IndexOf(':');
                        if (colon >= 0) t = t.Substring(colon + 1);
                        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out den) || den <= 0) { Log("  type the scale as 1:n or n, or Enter to fit"); return; }
                        mpp = den / (1000 * std.PaperUnitsPerMm);
                    }
                    var vp = SheetViewports.Create(area, center, mpp, GetOrCreateLayer(std.ViewportLayer));
                    _undo.Push(new AddEntitiesCommand(layout.AssociatedBlock, new Entity[] { vp }, "Viewport"));
                    _dirty = true; UpdateTitle();
                    EndTool();
                    var c = _canvas.View.Center; var z = _canvas.View.Zoom;
                    Rebuild(fit: false);
                    _canvas.ZoomTo(c, z);
                    Log("  viewport at 1:" + den.ToString("0.###", CultureInfo.InvariantCulture) + " on " + _sheet + ", centred on the survey  (VPSCALE changes it later; Ctrl+Z to undo)");
                };
            };
        }

        /// <summary>VPINFO [sheet]: every viewport on the current sheet (or all sheets), and why each
        /// does or doesn't show model space - the evidence for a sheet that comes up blank.</summary>
        private void ViewportInfo(string arg)
        {
            if (_doc == null) { Log("  open a drawing first"); return; }
            var layouts = _doc.Layouts.Where(l => l.IsPaperSpace).OrderBy(l => l.TabOrder)
                .Where(l => arg.Equals("ALL", StringComparison.OrdinalIgnoreCase) || (arg.Length > 0 ? l.Name.Equals(arg, StringComparison.OrdinalIgnoreCase) : _sheet == "Model" || l.Name.Equals(_sheet, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (layouts.Count == 0) { Log("  no sheet named " + arg); return; }
            foreach (var l in layouts) foreach (var line in ViewportRules.Describe(l)) Log("  " + line);
        }

        /// <summary>VPSCALE [1:n]: change the current sheet's plot scale in place - viewport,
        /// title-block scale text and scale bar, and optionally the model labels' size.</summary>
        private void StartSheetScale(string arg)
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            var layout = _sheet == "Model" ? null : _doc.Layouts.FirstOrDefault(l => l.Name.Equals(_sheet, StringComparison.OrdinalIgnoreCase));
            if (layout == null) { Log("  switch to the sheet whose scale you want to change first (VPSCALE works on a layout, not Model)"); return; }
            if (SheetScale.PlanViewport(layout) == null) { Log("  " + _sheet + " has no plan viewport to rescale"); return; }
            string anchor = _std?.ScaleBarAnchor ?? "SCALE 1:#";
            var stated = SheetScale.StatedDenominator(layout, anchor);
            BeginTool("VPSCALE");
            _canvas.ToolActive = false;
            double den = 0;

            bool TryDen(string t, out double d)
            {
                t = t.Trim();
                int colon = t.IndexOf(':');
                if (colon >= 0) t = t.Substring(colon + 1);
                return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0;
            }

            void AskResize()
            {
                _prompt.Text = "Sheet scale - resize the model labels and symbols to suit? [Y/N] <Y>:";
                Log("  resize the model-space labels, symbols and dimension text so they keep their size on paper? Y/N (Enter = Y)");
                _awaitingLine = s =>
                {
                    bool resize = !s.Trim().StartsWith("N", StringComparison.OrdinalIgnoreCase);
                    var r = SheetScale.Change(_doc!, layout, den, resize, anchor);
                    EndTool();
                    foreach (var n in r.Notes) Log("  " + n);
                    if (r.Command == null) return;
                    _undo.Push(r.Command);
                    _dirty = true; UpdateTitle();
                    var center = _canvas.View.Center; var zoom = _canvas.View.Zoom;
                    Rebuild(fit: false);
                    _canvas.ZoomTo(center, zoom);
                    Log("  (Ctrl+Z undoes the whole scale change)");
                };
            }

            if (arg.Length > 0 && TryDen(arg, out den)) { AskResize(); return; }
            _prompt.Text = "Sheet scale - new scale 1:n" + (stated != null ? " <now 1:" + stated.Value.ToString("0.###", CultureInfo.InvariantCulture) + ">" : "") + ":";
            Log("VPSCALE  " + _sheet + (stated != null ? " is at 1:" + stated.Value.ToString("0.###", CultureInfo.InvariantCulture) : "") + " - type the new scale, e.g. 1:250 or 250 (Esc cancels)");
            _awaitingLine = s =>
            {
                if (s.Length == 0) { EndTool(); Log("  *cancelled*"); return; }
                if (!TryDen(s, out den)) { Log("  type the scale as 1:n or just n, e.g. 1:250"); return; }
                AskResize();
            };
        }

        /// <summary>The firm standards for labelling: the drafted job's, else the standards file
        /// last used in the Draft dialog, else FD-Draft's built-in defaults.</summary>
        private FirmStandards LabelStandards()
        {
            if (_std != null) return _std;
            try
            {
                if (_settings.StandardsPath.Length > 0 && File.Exists(_settings.StandardsPath))
                    return _std = FirmStandards.Load(_settings.StandardsPath);
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is UnauthorizedAccessException) { }
            return FirmStandards.Default();
        }

        /// <summary>Model units per paper millimetre for labels drawn now: the current sheet's
        /// plan viewport, else the first sheet with one (when on Model), else 1:1 for a
        /// paper-native sheet; 1:500 metric as a last resort.</summary>
        private double LabelModelPerMm(FirmStandards std, out string basis)
        {
            if (_doc != null)
            {
                bool onSheet = _sheet != "Model";
                var layouts = onSheet
                    ? _doc.Layouts.Where(l => l.Name.Equals(_sheet, StringComparison.OrdinalIgnoreCase))
                    : _doc.Layouts.Where(l => l.IsPaperSpace).OrderBy(l => l.TabOrder);
                foreach (var l in layouts)
                {
                    var vp = SheetScale.PlanViewport(l);
                    if (vp == null) continue;
                    double stated = SheetScale.StatedDenominator(l, std.ScaleBarAnchor) ?? 0;
                    basis = l.Name + (stated > 0 ? " at 1:" + stated.ToString("0.###", CultureInfo.InvariantCulture) : "'s viewport");
                    return vp.ViewHeight / vp.Height * std.PaperUnitsPerMm;
                }
                if (onSheet && CurrentEntityOwner() != _doc.ModelSpace) { basis = "drawn straight onto " + _sheet + " (paper units)"; return std.PaperUnitsPerMm; }
            }
            basis = "1:500 (no sheet viewport to take the scale from)";
            return 0.5;
        }

        /// <summary>LABEL: bearing/distance (or curve data) for each selected line, arc or
        /// polyline span, by the firm's own label rules at the sheet's scale.</summary>
        private void LabelSelection()
        {
            if (_doc == null) return;
            var courses = SelectedEntities().Where(e => e is Line || e is Arc || e is LwPolyline || e is Polyline2D).ToList();
            if (courses.Count == 0) { Log("  select the line(s), arc(s) or polyline(s) to label first, then type LABEL"); return; }
            var std = LabelStandards();
            double mpm = LabelModelPerMm(std, out string basis);
            double g2g = std.GridToGround && _job != null && _job.Settings.ScaleFactor > 0 ? 1.0 / _job.Settings.ScaleFactor : 1.0;
            var pairs = new List<(Entity Source, Entity Copy)>();
            foreach (var c in courses)
                foreach (var t in CourseLabelling.For(c, _doc, std, mpm, GetOrCreateLayer, g2g)) pairs.Add((c, t));
            var cmd = EntityOps.AddBesideSources(pairs, "Label " + courses.Count);
            if (cmd == null) { Log("  nothing to label"); return; }
            _undo.Push(cmd);
            _dirty = true; UpdateTitle();
            Rebuild(fit: false);
            Log("  labelled " + Plural(courses.Count, "course", "courses") + " (" + pairs.Count + " labels, scale from " + basis + ")  - FLIP moves one to the other side; Ctrl+Z to undo");
        }

        /// <summary>FLIP: moves each selected bearing/distance/curve label to the other side of
        /// the course nearest it (in the same block - Model, or a paper-native sheet).</summary>
        private void FlipSelectedLabels()
        {
            if (_doc == null) return;
            var labels = SelectedEntities().Where(e => e is TextEntity || e is MText).ToList();
            if (labels.Count == 0) { Log("  select the label(s) to flip first (bearing, distance or curve text), then type FLIP"); return; }
            // Courses are looked for in whatever block(s) the labels live in.
            var courses = labels.Select(e => e.Owner).OfType<BlockRecord>().Distinct()
                .SelectMany(b => b.Entities).Where(e => e is Line || e is Arc || e is LwPolyline || e is Polyline2D).ToList();
            double h = labels.Max(e => e is TextEntity t ? t.Height : e is MText m ? m.Height : 0);
            var cmd = LabelFlip.Flip(labels, courses, Math.Max(h, 1e-6) * 10, out int flipped);
            if (cmd == null) { Log("  no course close enough to flip across - FLIP is for labels sitting beside their line or curve"); return; }
            _undo.Push(cmd);
            _dirty = true; UpdateTitle();
            Rebuild(fit: false);
            UpdateProperties();
            Log("  flipped " + Plural(flipped, "label", "labels") + (flipped < labels.Count ? " (" + (labels.Count - flipped) + " had no course beside them)" : "") + "  (Ctrl+Z to undo)");
        }

        /// <summary>The Line in the current block (Model, or a paper-native sheet) nearest a
        /// picked point, within a few screen pixels - how TRIM/EXTEND/FILLET pick their target.</summary>
        private Line? LineNear(Vec2 model, double pixels = 8)
        {
            double tol = Math.Max(pixels / _canvas.View.Zoom, 1e-6);
            Line? best = null; double bestD = tol;
            foreach (var l in CurrentEntityOwner().Entities.OfType<Line>())
            {
                double d = Construct.DistanceToSegment(model, new Vec2(l.StartPoint.X, l.StartPoint.Y), new Vec2(l.EndPoint.X, l.EndPoint.Y), out _);
                if (d < bestD) { bestD = d; best = l; }
            }
            return best;
        }

        /// <summary>TRIM (cut=true) / EXTEND: the selection is the cutting edges / boundaries -
        /// or, with nothing selected, all linework in the block - then each picked line is
        /// trimmed at, or extended to, them. Stays active until blank/Esc.</summary>
        private void StartTrimExtend(bool cut)
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            string name = cut ? "TRIM" : "EXTEND";
            var chosen = SelectedEntities();
            bool all = chosen.Count == 0;
            BeginTool(name);
            _prompt.Text = cut ? "Trim - pick the part of a line to cut away (blank ends):" : "Extend - pick a line near the end to extend (blank ends):";
            Log(name + "  " + (all ? "every line, arc, circle and polyline here is a " + (cut ? "cutting edge" : "boundary") : Plural(chosen.Count, "selected entity", "selected entities") + " as " + (cut ? "cutting edges" : "boundaries"))
                + " - pick lines to " + (cut ? "trim" : "extend") + " (Esc/blank ends)");
            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                var line = LineNear(model.Value);
                if (line == null) { Log("  no line there - " + name + " works on lines; pick closer"); return; }
                var edges = all ? CurrentEntityOwner().Entities.ToList() : chosen;
                var cmd = cut ? EntityOps.Trim(line, model.Value, edges) : EntityOps.Extend(line, model.Value, edges);
                if (cmd == null) { Log(cut ? "  nothing crosses that line to trim it at" : "  nothing lies ahead of that end to extend to"); return; }
                _undo.Push(cmd);
                _dirty = true; UpdateTitle();
                Rebuild(fit: false);
                Log("  " + (cut ? "trimmed" : "extended") + " - " + (line.Owner != null
                    ? InverseResult.Between(new Vec2(line.StartPoint.X, line.StartPoint.Y), new Vec2(line.EndPoint.X, line.EndPoint.Y), _std?.BearingRotationDeg ?? 0).ToString()
                    : "line erased (every part was cut away)"));
            };
            _awaitingLine = s => { if (s.Length == 0) { EndTool(); Log("  *" + name.ToLowerInvariant() + " complete*"); } else Log("  pick a line, or blank to end"); };
        }

        private double _lastFillet = 5.0;

        /// <summary>FILLET: type a radius (0 = sharp corner), then pick the two lines on the
        /// sides to keep; they're cut back to their tangent points and joined by the curve.</summary>
        private void StartFillet()
        {
            if (_doc == null || _canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            BeginTool("FILLET");
            _canvas.ToolActive = false;
            string def = _lastFillet.ToString("0.###", CultureInfo.InvariantCulture);
            _prompt.Text = "Fillet - radius <" + def + ">:";
            Log("FILLET  type the radius (Enter = " + def + ", 0 = sharp corner), then pick the two lines on the parts to keep");
            double radius = -1;
            Line? first = null; Vec2 firstPick = default;
            _awaitingLine = s =>
            {
                if (s.Length == 0) radius = _lastFillet;
                else if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out radius) || radius < 0) { Log("  type a radius of 0 or more"); return; }
                _lastFillet = radius;
                _awaitingLine = null;
                _canvas.ToolActive = true;
                _prompt.Text = "Fillet - first line:";
                Log("  radius " + radius.ToString("0.###", CultureInfo.InvariantCulture) + " - pick the first line");
            };
            _awaitingPoint = p =>
            {
                if (radius < 0) { Log("  type the radius first"); return; }
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                var line = LineNear(model.Value);
                if (line == null) { Log("  no line there - pick on a line"); return; }
                if (first == null) { first = line; firstPick = model.Value; _prompt.Text = "Fillet - second line:"; Log("  first line picked - now the second"); return; }
                if (line == first) { Log("  that's the same line - pick the other one"); return; }
                var cmd = EntityOps.Fillet(first, firstPick, line, model.Value, radius, out var arc);
                EndTool();
                if (cmd == null) { Log("  can't fillet those - they're parallel, or too short for that radius"); return; }
                _undo.Push(cmd);
                _dirty = true; UpdateTitle();
                Rebuild(fit: false);
                Log(arc != null
                    ? string.Format(CultureInfo.InvariantCulture, "  fillet R {0:F3}, arc length {1:F3}  (Ctrl+Z to undo)", arc.Radius, arc.Radius * Angles.Normalize2Pi(arc.EndAngle - arc.StartAngle))
                    : "  corner closed  (Ctrl+Z to undo)");
            };
        }

        /// <summary>JOIN: the selected lines, arcs and open polylines that meet end to end become
        /// polylines (closed where they close), so AREA, OFFSET and LABEL can treat a
        /// hand-drawn boundary as one figure.</summary>
        private void JoinSelection()
        {
            if (_doc == null) return;
            var sel = SelectedEntities();
            if (sel.Count == 0) { Log("  select the lines/arcs/polylines to join first (a crossing box is quickest), then type JOIN"); return; }
            // Ends within a millimetre count as meeting - tighter than any plan shows, looser than rounding noise.
            var cmd = EntityOps.Join(sel, 0.001, out var made);
            if (cmd == null) { Log("  nothing to join - the selected pieces don't meet end to end"); return; }
            _undo.Push(cmd);
            _canvas.Selected.Clear();
            foreach (var pl in made) _canvas.Selected.Add(pl.Handle);
            _dirty = true; UpdateTitle();
            Rebuild(fit: false);
            UpdateProperties();
            foreach (var pl in made)
            {
                var pts = pl.Vertices.Select(v => new Vec2(v.Location.X, v.Location.Y)).ToList();
                var bl = pl.Vertices.Select(v => v.Bulge).ToList();
                Log("  joined into a" + (pl.IsClosed ? " closed" : "n open") + " polyline of " + pl.Vertices.Count + " vertices"
                    + (pl.IsClosed ? ", area " + AreaText(FigureMeasure.Area(pts, bl)) : "") + ", length " + FigureMeasure.Perimeter(pts, bl, pl.IsClosed).ToString("F3", CultureInfo.InvariantCulture));
            }
            Log("  (the new polylines are selected; Ctrl+Z to undo)");
        }

        /// <summary>ID: report the coordinates of each picked point (snap for exact ones).</summary>
        private void StartId()
        {
            if (_canvas.Scene == null) { Log("  open or draft a drawing first"); return; }
            BeginTool("ID");
            _prompt.Text = "ID - pick a point (Esc ends):";
            Log("ID  pick points to read their coordinates (snap on for exact ones; Esc ends)");
            _awaitingPoint = p =>
            {
                var model = _canvas.Scene!.ModelAt(p);
                if (model == null) { Log("  pick inside a viewport (or on Model)"); return; }
                var pt = _job?.Points.FirstOrDefault(q => Math.Abs(q.Easting - model.Value.X) < 1e-4 && Math.Abs(q.Northing - model.Value.Y) < 1e-4);
                Log("  " + NE(model.Value) + (pt != null ? "   point " + pt.Id + " " + pt.Code + "  elev " + pt.Elevation.ToString("F3", CultureInfo.InvariantCulture) : ""));
            };
        }

        private static string Plural(int n, string one, string many) => n + " " + (n == 1 ? one : many);

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
