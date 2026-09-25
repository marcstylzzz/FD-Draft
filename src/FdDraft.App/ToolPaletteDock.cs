using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FdDraft.Core.Standards;

namespace FdDraft.App
{
    /// <summary>
    /// The tool palette docked on the right, like MSCAD's: a slim strip of tabs (their names
    /// turned to read upward) along the window edge, and a panel of tool buttons that slides
    /// out over the drawing when a tab is hovered and tucks away again when the mouse leaves.
    /// The pin keeps it open. <see cref="Strip"/> goes beside the canvas, <see cref="Flyout"/>
    /// over its right edge.
    /// </summary>
    public sealed class ToolPaletteDock
    {
        public const double PanelWidth = 270;

        /// <summary>A tool button was clicked.</summary>
        public event Action<PaletteTool>? ToolClicked;
        /// <summary>"Edit palette…" was clicked.</summary>
        public event Action? EditRequested;

        public FrameworkElement Strip { get; }
        public FrameworkElement Flyout { get; }

        private readonly StackPanel _tabs = new StackPanel();
        private readonly StackPanel _tools = new StackPanel();
        private readonly TextBlock _title = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        private readonly ToggleButton _pin = new ToggleButton { Content = "📌", ToolTip = "Keep the palette open", Width = 24, Height = 22, Margin = new Thickness(1, 2, 1, 2), Padding = new Thickness(0) };
        private readonly Border _panel;
        private readonly DispatcherTimer _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        private ToolPalette _palette = new ToolPalette();
        private int _current = -1;
        private bool _open;

        private static readonly Brush TabBack = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
        private static readonly Brush TabActive = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x7F));
        private static readonly Brush Edge = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xB8));

        public ToolPaletteDock()
        {
            var edit = new Button { Content = "✎", ToolTip = "Edit the palette (palette.ini)", Width = 24, Height = 22, Margin = new Thickness(1, 2, 1, 2), Padding = new Thickness(0) };
            edit.Click += (s, e) => EditRequested?.Invoke();
            _pin.Checked += (s, e) => { if (_current < 0 && _palette.Tabs.Count > 0) ShowTab(0); Open(); };
            _pin.Unchecked += (s, e) => ScheduleClose();
            var strip = new DockPanel { Width = 28, Background = new SolidColorBrush(Color.FromRgb(0xD6, 0xD6, 0xD6)) };
            var top = new StackPanel();
            top.Children.Add(_pin);
            top.Children.Add(edit);
            DockPanel.SetDock(top, Dock.Top);
            strip.Children.Add(top);
            strip.Children.Add(_tabs);
            Strip = new Border { BorderBrush = Edge, BorderThickness = new Thickness(1, 0, 0, 0), Child = strip };

            var header = new DockPanel { Height = 28, Background = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4)) };
            header.Children.Add(_title);
            var body = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            body.Children.Add(header);
            body.Children.Add(new ScrollViewer { Content = _tools, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            _panel = new Border
            {
                Width = 0, HorizontalAlignment = HorizontalAlignment.Right, Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)),
                BorderBrush = Edge, BorderThickness = new Thickness(1, 0, 0, 0), Child = body, ClipToBounds = true,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.35, Direction = 180 },
            };
            Flyout = _panel;

            // Stays open while the mouse is over the strip or the panel; closes a moment after it leaves both.
            Strip.MouseEnter += (s, e) => _closeTimer.Stop();
            _panel.MouseEnter += (s, e) => _closeTimer.Stop();
            Strip.MouseLeave += (s, e) => ScheduleClose();
            _panel.MouseLeave += (s, e) => ScheduleClose();
            _closeTimer.Tick += (s, e) =>
            {
                _closeTimer.Stop();
                if (_pin.IsChecked == true || Strip.IsMouseOver || _panel.IsMouseOver) return;
                Close();
            };
        }

        /// <summary>Shows a palette's tabs (replacing any before).</summary>
        public void Load(ToolPalette palette)
        {
            _palette = palette;
            _tabs.Children.Clear();
            for (int i = 0; i < palette.Tabs.Count; i++)
            {
                int index = i;
                var label = new TextBlock { Text = palette.Tabs[i].Name, LayoutTransform = new RotateTransform(-90), Margin = new Thickness(0, 8, 0, 8) };
                var tab = new Border { Child = label, Background = TabBack, BorderBrush = Edge, BorderThickness = new Thickness(0, 0, 0, 1), Cursor = Cursors.Hand, Padding = new Thickness(3, 0, 3, 0) };
                tab.MouseEnter += (s, e) => { ShowTab(index); Open(); };
                tab.MouseLeftButtonDown += (s, e) => { ShowTab(index); Open(); };
                _tabs.Children.Add(tab);
            }
            if (_current >= palette.Tabs.Count) _current = -1;
            if (_current >= 0) ShowTab(_current);
        }

        private void ShowTab(int index)
        {
            if (index < 0 || index >= _palette.Tabs.Count) return;
            _current = index;
            for (int i = 0; i < _tabs.Children.Count; i++)
                ((Border)_tabs.Children[i]).Background = i == index ? TabActive : TabBack;
            var tab = _palette.Tabs[index];
            _title.Text = tab.Name;
            _tools.Children.Clear();
            foreach (var tool in tab.Tools)
            {
                var b = new Button
                {
                    Content = new TextBlock { Text = tool.Label, TextWrapping = TextWrapping.Wrap },
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 9, 10, 9), Margin = new Thickness(2, 1, 2, 1),
                    Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 13.5,
                    ToolTip = Describe(tool), Cursor = Cursors.Hand,
                };
                b.Click += (s, e) =>
                {
                    if (_pin.IsChecked != true) Close();
                    ToolClicked?.Invoke(tool);
                };
                _tools.Children.Add(b);
            }
            if (tab.Tools.Count == 0)
                _tools.Children.Add(new TextBlock { Text = "(no tools on this tab - ✎ to add some)", Foreground = Brushes.Gray, Margin = new Thickness(10), TextWrapping = TextWrapping.Wrap });
        }

        private static string Describe(PaletteTool t)
        {
            string what = t.Kind switch
            {
                PaletteToolKind.Text => "Text" + (t.Layer.Length > 0 ? " on " + t.Layer : "") + (t.Style.Length > 0 ? ", style " + t.Style : "") + (t.HeightMm > 0 ? ", " + t.HeightMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " mm on paper" : ""),
                PaletteToolKind.Line => "Line" + (t.Layer.Length > 0 ? " on " + t.Layer : ""),
                _ => t.Command,
            };
            return t.Tip.Length > 0 ? what + "\n" + t.Tip : what;
        }

        private void Open()
        {
            _closeTimer.Stop();
            if (_open) return;
            _open = true;
            _panel.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(PanelWidth, TimeSpan.FromMilliseconds(140)) { EasingFunction = new QuadraticEase() });
        }

        private void ScheduleClose()
        {
            if (_pin.IsChecked == true) return;
            _closeTimer.Stop();
            _closeTimer.Start();
        }

        private void Close()
        {
            if (!_open) return;
            _open = false;
            _panel.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)) { EasingFunction = new QuadraticEase() });
            for (int i = 0; i < _tabs.Children.Count; i++) ((Border)_tabs.Children[i]).Background = TabBack;
        }
    }
}
