using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FdDraft.View;
using FdDraft.View.Toolbars;
using Color = System.Windows.Media.Color;

namespace FdDraft.App
{
    /// <summary>
    /// The toolbars: built from <see cref="ToolbarCatalog"/> (FD-Draft's own bars plus MSCAD's,
    /// button for button from its icad.cui), drawn dark with vector icons. Each button runs a
    /// typed command, so everything on a toolbar can also be typed. View > Toolbars (or a
    /// right-click on the toolbar area) shows and hides them; the layout is remembered.
    /// </summary>
    public sealed partial class MainWindow
    {
        private readonly ToggleButton _panButton = new ToggleButton();
        private readonly Dictionary<SnapModes, ToggleButton> _snapButtons = new Dictionary<SnapModes, ToggleButton>();
        private readonly Dictionary<string, ToolBar> _toolbars = new Dictionary<string, ToolBar>();
        private readonly MenuItem _toolbarsMenu = new MenuItem { Header = "_Toolbars" };
        private ToolBarTray? _tray;

        private static SolidColorBrush Frozen(byte r, byte g, byte b, byte a = 255)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        private static readonly SolidColorBrush TrayBrush = Frozen(0x23, 0x24, 0x27);
        private static readonly SolidColorBrush BarBrush = Frozen(0x2B, 0x2D, 0x31);
        private static readonly SolidColorBrush BarText = Frozen(0xE3, 0xE5, 0xE8);
        private static readonly SolidColorBrush HoverBrush = Frozen(0x3D, 0x41, 0x48);
        private static readonly SolidColorBrush PressBrush = Frozen(0x1E, 0x4E, 0x8C);
        private static readonly SolidColorBrush OnBrush = Frozen(0x27, 0x4D, 0x7A);
        private static readonly SolidColorBrush OnBorder = Frozen(0x5A, 0xA2, 0xFF);

        private static readonly HashSet<string> SnapModeNames = new HashSet<string> { "END", "MID", "INT", "CEN", "QUA", "PER", "NEA", "NOD" };

        private static SnapModes SnapModeOf(string name) => name switch
        {
            "END" => SnapModes.Endpoint,
            "MID" => SnapModes.Midpoint,
            "INT" => SnapModes.Intersection,
            "CEN" => SnapModes.Center,
            "QUA" => SnapModes.Quadrant,
            "PER" => SnapModes.Perpendicular,
            "NEA" => SnapModes.Nearest,
            "NOD" => SnapModes.Node,
            _ => SnapModes.None,
        };

        private ToolBarTray BuildToolbar()
        {
            var tray = new ToolBarTray { Background = TrayBrush, IsLocked = false };
            _tray = tray;
            var buttonStyle = ToolButtonStyle(typeof(Button));
            var toggleStyle = ToolButtonStyle(typeof(ToggleButton));
            var saved = ParseToolbarLayout(_settings.ToolbarLayout);
            var nextIndex = new Dictionary<int, int>();
            foreach (var def in ToolbarCatalog.All)
            {
                nextIndex.TryGetValue(def.Band, out int index);
                nextIndex[def.Band] = index + 1;
                var bar = new ToolBar { Band = def.Band, BandIndex = index, Background = BarBrush, Foreground = BarText, Tag = def.Key, ToolTip = null, Margin = new Thickness(1, 1, 1, 0) };
                // When a row is too wide for the window, the buttons that don't fit go into the
                // bar's overflow drop-down - keep that dark too, so the icons stay readable.
                bar.Loaded += (s, e) =>
                {
                    if (s is ToolBar tb && tb.Template?.FindName("PART_ToolBarOverflowPanel", tb) is Panel overflow)
                    {
                        overflow.Background = BarBrush;
                        if (VisualTreeHelper.GetParent(overflow) is Border b) b.Background = BarBrush;
                    }
                };
                bar.Resources[ToolBar.ButtonStyleKey] = buttonStyle;
                bar.Resources[ToolBar.ToggleButtonStyleKey] = toggleStyle;
                ToolBarTray.SetIsLocked(bar, false);
                foreach (var b in def.Buttons) bar.Items.Add(MakeToolItem(b));
                if (saved.TryGetValue(def.Key, out var pos))
                {
                    bar.Band = pos.Band; bar.BandIndex = pos.Index;
                    if (!pos.Visible) bar.Visibility = Visibility.Collapsed;
                }
                _toolbars[def.Key] = bar;
                tray.ToolBars.Add(bar);
            }
            _panButton.Checked += (s, e) => { _canvas.PanMode = true; _canvas.Cursor = Cursors.Hand; };
            _panButton.Unchecked += (s, e) => { _canvas.PanMode = false; _canvas.Cursor = Cursors.Cross; };
            FillToolbarsMenu(_toolbarsMenu);
            tray.ContextMenuOpening += (s, e) =>
            {
                var cm = new ContextMenu();
                foreach (var item in ToolbarMenuItems()) cm.Items.Add(item);
                tray.ContextMenu = cm;
            };
            tray.ContextMenu = new ContextMenu();
            return tray;
        }

        /// <summary>One toolbar entry: an icon button, a toggle, a separator or the layer list.</summary>
        private FrameworkElement MakeToolItem(ToolButton b)
        {
            switch (b.Kind)
            {
                case ToolButtonKind.Separator:
                    return new Separator { Background = HoverBrush, Margin = new Thickness(3, 4, 3, 4) };
                case ToolButtonKind.Custom:
                {
                    var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    panel.Children.Add(new TextBlock { Text = "Layer:", Foreground = BarText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
                    panel.Children.Add(_layerCombo);
                    return panel;
                }
            }
            var content = new Image { Source = ToolIconImage.Get(b.Icon), Width = 24, Height = 24, SnapsToDevicePixels = true };
            RenderOptions.SetEdgeMode(content, EdgeMode.Unspecified);
            ButtonBase button;
            if (b.Kind == ToolButtonKind.Toggle)
            {
                ToggleButton t;
                if (b.Command == "PAN") t = _panButton;
                else
                {
                    t = new ToggleButton();
                    var mode = SnapModeOf(b.Command!.Split(' ').Last());
                    t.IsChecked = (_canvas.SnapModes & mode) != 0;
                    t.Checked += (s, e) => SetSnapMode(mode, true);
                    t.Unchecked += (s, e) => SetSnapMode(mode, false);
                    _snapButtons[mode] = t;
                }
                button = t;
            }
            else
            {
                var btn = new Button();
                string? cmd = b.Command;
                btn.IsEnabled = cmd != null;
                if (cmd != null) btn.Click += (s, e) => RunToolbarCommand(cmd);
                button = btn;
            }
            button.Content = content;
            button.ToolTip = ToolTipFor(b);
            ToolTipService.SetShowOnDisabled(button, true);
            ToolTipService.SetInitialShowDelay(button, 350);
            return button;
        }

        private static object ToolTipFor(ToolButton b)
        {
            var panel = new StackPanel { MaxWidth = 360 };
            panel.Children.Add(new TextBlock { Text = b.Name, FontWeight = FontWeights.SemiBold });
            if (b.Available)
            {
                if (b.Help.Length > 0) panel.Children.Add(new TextBlock { Text = b.Help, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                if (b.Kind == ToolButtonKind.Command && b.Command != null)
                    panel.Children.Add(new TextBlock { Text = "Command: " + b.Command, Foreground = Brushes.DimGray, Margin = new Thickness(0, 3, 0, 0), FontSize = 11 });
            }
            else
                panel.Children.Add(new TextBlock { Text = "Not in FD-Draft yet: " + b.Note, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkRed, Margin = new Thickness(0, 2, 0, 0) });
            if (b.Mscad != null)
                panel.Children.Add(new TextBlock { Text = "MSCAD: " + b.Mscad.Replace("^C", ""), Foreground = Brushes.DimGray, FontSize = 11 });
            return panel;
        }

        /// <summary>The dark flat look for toolbar buttons: hover, pressed, on (toggles), dimmed when unavailable.</summary>
        private static Style ToolButtonStyle(Type type)
        {
            var border = new FrameworkElementFactory(typeof(Border), "Bd");
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetValue(Border.PaddingProperty, new Thickness(2));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            var template = new ControlTemplate(type) { VisualTree = border };
            if (type == typeof(ToggleButton))
            {
                var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
                on.Setters.Add(new Setter(Border.BackgroundProperty, OnBrush, "Bd"));
                on.Setters.Add(new Setter(Border.BorderBrushProperty, OnBorder, "Bd"));
                template.Triggers.Add(on);
            }
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, HoverBrush, "Bd"));
            template.Triggers.Add(hover);
            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, PressBrush, "Bd"));
            template.Triggers.Add(pressed);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.3));
            template.Triggers.Add(disabled);
            var style = new Style(type);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(1, 0, 1, 0)));
            style.Setters.Add(new Setter(Control.FocusableProperty, false));
            return style;
        }

        /// <summary>A toolbar click: a view command (zoom, pan, snaps, regen) runs inside whatever is
        /// going on; anything else ends the running command first, as picking a new tool does in CAD.</summary>
        private void RunToolbarCommand(string command)
        {
            var parts = command.Split(new[] { ' ' }, 2);
            string verb = parts[0], arg = parts.Length > 1 ? parts[1] : "";
            bool transparent = verb == "ZW" || verb == "ZP" || verb == "ZI" || verb == "ZO" || verb == "ZE" || verb == "REGEN" || verb == "SNAPMODE"
                || verb == "LAYERS" || verb == "POINTS" || verb == "CODES" || verb == "CALC";
            if (!transparent && _activeTool.Length > 0) { EndTool(); Log("  *cancelled*"); }
            if (!transparent) Log("Command: " + command);
            Execute(verb, arg);
            if (!_input.IsKeyboardFocusWithin) _canvas.Focus();
        }

        /// <summary>SNAPMODE END|MID|...|NONE: one object snap mode on/off (the Object Snap toolbar).</summary>
        private void SnapModeCommand(string arg)
        {
            var name = arg.Trim().ToUpperInvariant();
            if (name == "NONE" || name == "OFF")
            {
                foreach (var b in _snapButtons.Values) b.IsChecked = false;
                _canvas.SnapModes = SnapModes.None; _settings.SnapModes = 0; _settings.Save();
                Log("  object snaps all off");
                return;
            }
            if (name.Length > 3) name = name.Substring(0, 3);
            if (!SnapModeNames.Contains(name)) { Log("  SNAPMODE END, MID, INT, CEN, QUA, PER, NEA, NOD or NONE"); return; }
            var mode = SnapModeOf(name);
            bool on = (_canvas.SnapModes & mode) == 0;
            if (_snapButtons.TryGetValue(mode, out var t)) t.IsChecked = on; else SetSnapMode(mode, on);
        }

        // ---- View > Toolbars -------------------------------------------------------------------------

        private void FillToolbarsMenu(MenuItem menu)
        {
            menu.Items.Clear();
            menu.SubmenuOpened += (s, e) =>
            {
                if (e.OriginalSource != menu) return;
                menu.Items.Clear();
                foreach (var item in ToolbarMenuItems()) menu.Items.Add(item);
            };
            foreach (var item in ToolbarMenuItems()) menu.Items.Add(item);
        }

        private IEnumerable<object> ToolbarMenuItems()
        {
            bool mscadHeader = false;
            foreach (var def in ToolbarCatalog.All)
            {
                if (def.FromMscad && !mscadHeader)
                {
                    mscadHeader = true;
                    yield return new Separator();
                }
                var bar = _toolbars[def.Key];
                var item = new MenuItem { Header = def.Name, IsCheckable = true, IsChecked = bar.Visibility == Visibility.Visible, StaysOpenOnClick = true };
                item.Click += (s, e) => { bar.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed; SaveToolbarLayout(); };
                yield return item;
            }
            yield return new Separator();
            var all = new MenuItem { Header = "Show all" };
            all.Click += (s, e) => { foreach (var b in _toolbars.Values) b.Visibility = Visibility.Visible; SaveToolbarLayout(); };
            yield return all;
            var none = new MenuItem { Header = "Hide the FD survey bars" };
            none.Click += (s, e) => { foreach (var d in ToolbarCatalog.All.Where(d => d.FromMscad)) _toolbars[d.Key].Visibility = Visibility.Collapsed; SaveToolbarLayout(); };
            yield return none;
            var reset = new MenuItem { Header = "Reset toolbar layout" };
            reset.Click += (s, e) => ResetToolbarLayout();
            yield return reset;
        }

        private void ResetToolbarLayout()
        {
            if (_tray == null) return;
            _tray.ToolBars.Clear();
            var nextIndex = new Dictionary<int, int>();
            foreach (var def in ToolbarCatalog.All)
            {
                var bar = _toolbars[def.Key];
                nextIndex.TryGetValue(def.Band, out int index);
                nextIndex[def.Band] = index + 1;
                bar.Band = def.Band; bar.BandIndex = index; bar.Visibility = Visibility.Visible;
                _tray.ToolBars.Add(bar);
            }
            SaveToolbarLayout();
            Log("  toolbars back to their default layout");
        }

        /// <summary>Remembers each toolbar's row, place and visibility ("key:band:index:1;...").</summary>
        private void SaveToolbarLayout()
        {
            _settings.ToolbarLayout = string.Join(";", _toolbars.Select(kv =>
                kv.Key + ":" + kv.Value.Band.ToString(CultureInfo.InvariantCulture) + ":" + kv.Value.BandIndex.ToString(CultureInfo.InvariantCulture) + ":" + (kv.Value.Visibility == Visibility.Visible ? "1" : "0")));
            _settings.Save();
        }

        private static Dictionary<string, (int Band, int Index, bool Visible)> ParseToolbarLayout(string text)
        {
            var d = new Dictionary<string, (int, int, bool)>();
            foreach (var part in (text ?? "").Split(';'))
            {
                var f = part.Split(':');
                if (f.Length != 4) continue;
                if (int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int band) && int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                    d[f[0]] = (Math.Max(0, band), Math.Max(0, index), f[3] != "0");
            }
            return d;
        }
    }
}
