using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace FdDraft.App
{
    /// <summary>
    /// "Traverse or Side Shots" (after MSCAD): stays open beside the drawing. Each OK computes one
    /// leg from the current point - bearing (plus any bearing correction) and distance (times the
    /// scale factor when Input scale is on) - and hands it to <see cref="Leg"/>. Traverse moves on
    /// to the new point; Side Shot stays put. The pad pans, zooms and undoes the last leg.
    /// </summary>
    public sealed class TraverseDialog : Window
    {
        /// <summary>A leg to draw. Returns null when drawn, else why not (shown in the dialog).</summary>
        public Func<TraverseRequest, string?>? Leg;
        /// <summary>Pad buttons: "up" "down" "left" "right" "in" "out" "extents" "here" "undo".</summary>
        public Action<string>? Pad;

        private readonly TextBox _bearing = new TextBox { MinWidth = 150, Margin = new Thickness(0, 3, 0, 3) };
        private readonly TextBox _distance = new TextBox { MinWidth = 150, Margin = new Thickness(0, 3, 0, 3) };
        private readonly CheckBox _useCorrection = new CheckBox { Content = "Bearing correction", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3) };
        private readonly TextBox _correction = new TextBox { Text = "0°00'00\"", MinWidth = 150, Margin = new Thickness(0, 3, 0, 3), IsEnabled = false };
        private readonly RadioButton _traverse = new RadioButton { Content = "Traverse", IsChecked = true, Margin = new Thickness(0, 0, 16, 0) };
        private readonly RadioButton _sideShot = new RadioButton { Content = "Side Shot" };
        private readonly CheckBox _inputScale = new CheckBox { Margin = new Thickness(0, 6, 0, 0) };
        private readonly CheckBox _addPoints = new CheckBox { Content = "Add points, next pt:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3), IsChecked = true };
        private readonly TextBox _nextPt = new TextBox { MinWidth = 70, Margin = new Thickness(0, 3, 0, 3) };
        private readonly CheckBox _labelBearing = new CheckBox { Content = "Draw bearings", Margin = new Thickness(0, 0, 16, 0), IsChecked = true };
        private readonly CheckBox _labelDistance = new CheckBox { Content = "Draw distances", IsChecked = true };
        private readonly TextBlock _from = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 6), MaxWidth = 330 };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MaxWidth = 330 };
        private readonly double _scaleFactor;

        public TraverseDialog(string fromText, string bearing, double scaleFactor, int nextPoint)
        {
            Title = "Traverse or Side Shots";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _scaleFactor = scaleFactor > 0 ? scaleFactor : 1;

            _from.Text = fromText;
            _bearing.Text = bearing;
            _distance.Text = "0";
            _nextPt.Text = nextPoint.ToString(CultureInfo.InvariantCulture);
            bool scaled = Math.Abs(_scaleFactor - 1) > 1e-12;
            _inputScale.Content = "Input scale: typed distance × " + _scaleFactor.ToString("0.########", CultureInfo.InvariantCulture) + (scaled ? "" : " (job has no scale factor)");
            _inputScale.IsChecked = scaled;
            _inputScale.IsEnabled = scaled;
            _useCorrection.Checked += (s, e) => _correction.IsEnabled = true;
            _useCorrection.Unchecked += (s, e) => _correction.IsEnabled = false;
            _nextPt.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding(nameof(CheckBox.IsChecked)) { Source = _addPoints });
            foreach (var tb in new[] { _bearing, _distance, _correction, _nextPt })
                tb.GotKeyboardFocus += (s, e) => ((TextBox)s!).SelectAll();

            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(_from);

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int row = 0;
            void Row(FrameworkElement label, FrameworkElement field)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                label.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetRow(label, row); Grid.SetRow(field, row); Grid.SetColumn(field, 1);
                g.Children.Add(label); g.Children.Add(field);
                row++;
            }
            TextBlock L(string t) => new TextBlock { Text = t, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3) };
            _bearing.ToolTip = "N73.1010E, NE73.1010, N73-10-10E, or an azimuth 253.1010 (DD.MMSS)";
            _distance.ToolTip = "The distance as measured (ground) - Input scale turns it to grid";
            _correction.ToolTip = "Added to every bearing: 0.0130 or 0-01-30 or -0°01'30\"";
            Row(L("Bearing:"), _bearing);
            Row(L("Distance:"), _distance);
            Row(_useCorrection, _correction);
            Row(_addPoints, _nextPt);
            root.Children.Add(g);

            var mode = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
            _traverse.ToolTip = "Each leg starts where the last one ended";
            _sideShot.ToolTip = "Each shot starts from the same point";
            mode.Children.Add(_traverse); mode.Children.Add(_sideShot);
            root.Children.Add(mode);
            root.Children.Add(_inputScale);
            var labels = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            labels.Children.Add(_labelBearing); labels.Children.Add(_labelDistance);
            root.Children.Add(labels);

            // The pad: pan arrows, zoom in / out / extents, centre on the current point, undo the last leg.
            var pad = new UniformGrid { Columns = 3, Rows = 3, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            Button P(string glyph, string cmd, string tip)
            {
                var b = new Button { Content = glyph, Width = 34, Height = 30, Margin = new Thickness(2), ToolTip = tip, FontSize = 15, Focusable = false };
                b.Click += (s, e) => Pad?.Invoke(cmd);
                return b;
            }
            pad.Children.Add(P("◎", "here", "Centre on the current point"));
            pad.Children.Add(P("▲", "up", "Pan up"));
            pad.Children.Add(P("⛶", "extents", "Zoom extents"));
            pad.Children.Add(P("◀", "left", "Pan left"));
            pad.Children.Add(P("↶", "undo", "Undo the last leg"));
            pad.Children.Add(P("▶", "right", "Pan right"));
            pad.Children.Add(P("－", "out", "Zoom out"));
            pad.Children.Add(P("▼", "down", "Pan down"));
            pad.Children.Add(P("＋", "in", "Zoom in"));
            root.Children.Add(pad);

            root.Children.Add(_status);

            var buttons = new UniformGrid { Columns = 3, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "OK", IsDefault = true, Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5) };
            ok.Click += (s, e) => Submit();
            var cancel = new Button { Content = "Close", IsCancel = true, Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5) };
            cancel.Click += (s, e) => Close();
            var help = new Button { Content = "Help", Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5) };
            help.Click += (s, e) => MessageBox.Show(this,
                "Type the bearing and distance and press Enter (OK) for each leg; the window stays open.\n\n" +
                "Bearing: N73.1010E, NE73.1010, N73-10-10E, or an azimuth in DD.MMSS (253.1010).\n" +
                "Distance: as measured. With Input scale on it is multiplied by the job's scale factor before it is drawn (ground to grid); the labels print it back as ground.\n" +
                "Bearing correction: added to every bearing you type.\n\n" +
                "Traverse: the next leg starts at the new point. Side Shot: every shot starts from the same point.\n" +
                "The pad pans and zooms the drawing; the middle button undoes the last leg. Close ends.",
                "Traverse or Side Shots", MessageBoxButton.OK, MessageBoxImage.Information);
            buttons.Children.Add(ok); buttons.Children.Add(cancel); buttons.Children.Add(help);
            root.Children.Add(buttons);

            Content = root;
            Loaded += (s, e) => { _bearing.Focus(); _bearing.SelectAll(); };
        }

        /// <summary>Updates the "from" line after a leg or an undo.</summary>
        public void SetFrom(string text) => _from.Text = text;

        public void SetNextPoint(int n) => _nextPt.Text = n.ToString(CultureInfo.InvariantCulture);

        private void Submit()
        {
            double correction = 0;
            if (_useCorrection.IsChecked == true && !FdDraft.Core.Geometry.Cogo.TryParseAngleText(_correction.Text, out correction))
            { Show("the bearing correction doesn't read - e.g. 0.0130 or 0-01-30", true); return; }
            int next = 0;
            if (_addPoints.IsChecked == true && !int.TryParse(_nextPt.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out next))
            { Show("type a whole number for the next point", true); _nextPt.Focus(); return; }
            var req = new TraverseRequest
            {
                Bearing = _bearing.Text, Distance = _distance.Text, CorrectionRadians = correction,
                InputScale = _inputScale.IsChecked == true ? _scaleFactor : 1.0,
                SideShot = _sideShot.IsChecked == true,
                PointNumber = _addPoints.IsChecked == true ? next : (int?)null,
                LabelBearing = _labelBearing.IsChecked == true, LabelDistance = _labelDistance.IsChecked == true,
            };
            string? error = Leg?.Invoke(req);
            if (error != null) { Show(error, true); _bearing.Focus(); return; }
            Show("", false);
            _distance.Text = "0";
            _bearing.Focus(); _bearing.SelectAll();
        }

        private void Show(string text, bool bad)
        {
            _status.Text = text;
            _status.Foreground = bad ? Brushes.Firebrick : Brushes.DimGray;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            // Enter in Bearing moves on to Distance, as MSCAD does; Enter in Distance draws the leg.
            if (e.Key == Key.Enter && Keyboard.FocusedElement == _bearing) { _distance.Focus(); e.Handled = true; return; }
            base.OnPreviewKeyDown(e);
        }
    }

    /// <summary>One OK from the Traverse dialog.</summary>
    public sealed class TraverseRequest
    {
        public string Bearing { get; set; } = "";
        public string Distance { get; set; } = "";
        public double CorrectionRadians { get; set; }
        public double InputScale { get; set; } = 1.0;
        public bool SideShot { get; set; }
        public int? PointNumber { get; set; }
        public bool LabelBearing { get; set; } = true;
        public bool LabelDistance { get; set; } = true;
    }
}
