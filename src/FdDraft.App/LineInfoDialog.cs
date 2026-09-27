using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FdDraft.Core.Geometry;

namespace FdDraft.App
{
    /// <summary>
    /// INFO on a line: FD-Draft's take on MSCAD's "CAD Line Computations" window. Shows the line's
    /// numbers; the buttons hand off to the matching tool (Traverse from here, Turned Angle,
    /// Tangent to Arc, Curve Calcs) through <see cref="Choice"/>.
    /// </summary>
    public sealed class LineInfoDialog : Window
    {
        public enum Action { None, Traverse, TurnedAngle, TangentToArc, CurveCalcs, ListLine }

        /// <summary>What the drafter clicked (None for OK / close).</summary>
        public Action Choice { get; private set; } = Action.None;

        public LineInfoDialog(LineComputation c, string what, string bearing, string rotatedBearing, int decimals, double scaleFactor)
        {
            Title = "Line Computations";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            string F(double v) => v.ToString("F" + decimals, CultureInfo.InvariantCulture);

            var root = new StackPanel { Margin = new Thickness(14) };
            var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var kind = new TextBlock { Text = "Line", Margin = new Thickness(0, 0, 24, 0) };
            var desc = new TextBlock { Text = what, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 };
            Grid.SetColumn(desc, 1);
            head.Children.Add(kind); head.Children.Add(desc);
            root.Children.Add(head);

            var brg = new Grid();
            brg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            brg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            void BrgRow(int r, string label, string value)
            {
                brg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3) };
                var v = Field(value); Grid.SetColumn(v, 1);
                Grid.SetRow(l, r); Grid.SetRow(v, r);
                brg.Children.Add(l); brg.Children.Add(v);
            }
            BrgRow(0, "Bearing:", bearing);
            BrgRow(1, "Rotated bearing:", rotatedBearing);
            root.Children.Add(brg);

            var g = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            for (int k = 0; k < 4; k++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            string sfNote = Math.Abs(scaleFactor - 1) > 1e-12 ? " (÷ " + scaleFactor.ToString("0.########", CultureInfo.InvariantCulture) + ")" : "";
            var rows = new (string L1, string V1, string L2, string V2)[]
            {
                ("From N:", F(c.FromN), "Horizontal dist:", F(c.Horizontal)),
                ("From E:", F(c.FromE), "Scaled horiz" + sfNote + ":", F(c.ScaledHorizontal)),
                ("From Z:", F(c.FromZ), "Slope dist:", F(c.Slope)),
                ("To N:", F(c.ToN), "Scaled slope:", F(c.ScaledSlope)),
                ("To E:", F(c.ToE), "% Grade:", c.GradePercent.ToString("F3", CultureInfo.InvariantCulture) + " %"),
                ("To Z:", F(c.ToZ), "Delta Z:", F(c.DeltaZ)),
            };
            for (int r = 0; r < rows.Length; r++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var cells = new FrameworkElement[]
                {
                    new TextBlock { Text = rows[r].L1, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 6, 3) },
                    Field(rows[r].V1),
                    new TextBlock { Text = rows[r].L2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 3, 6, 3) },
                    Field(rows[r].V2),
                };
                for (int k = 0; k < 4; k++) { Grid.SetRow(cells[k], r); Grid.SetColumn(cells[k], k); g.Children.Add(cells[k]); }
            }
            root.Children.Add(g);

            var buttons = new UniformGrid3();
            Button B(string text, Action a, string? tip = null, bool enabled = true)
            {
                var b = new Button { Content = text, Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5), IsEnabled = enabled, ToolTip = tip };
                if (!enabled) ToolTipService.SetShowOnDisabled(b, true);
                b.Click += (s, e) => { Choice = a; DialogResult = true; };
                return b;
            }
            const string notYet = "Not in FD-Draft yet";
            buttons.Add(B("Angle/Angle", Action.None, notYet, false));
            buttons.Add(B("Tangent to Arc", Action.TangentToArc, "A line from a point tangent to an arc (TANLINE)"));
            buttons.Add(B("Turned Angle", Action.TurnedAngle, "Points by turned angle and distance (TURNANGLE)"));
            buttons.Add(B("Traverse", Action.Traverse, "Traverse or side shots from a point you pick - bearing and distance legs, scale factor applied"));
            buttons.Add(B("Deflection", Action.None, notYet, false));
            buttons.Add(B("List Line >>", Action.ListLine, "Write these numbers to the command history"));
            buttons.Add(B("Proportioning", Action.None, notYet, false));
            root.Children.Add(buttons.Panel);

            var addDb = new CheckBox { Content = "Add lines to coordinate database", IsEnabled = false, Margin = new Thickness(4, 8, 0, 4), ToolTip = "The FD-Pro job is read-only in FD-Draft" };
            ToolTipService.SetShowOnDisabled(addDb, true);
            root.Children.Add(addDb);

            var bottom = new UniformGrid3();
            var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5) };
            ok.Click += (s, e) => { Choice = Action.None; DialogResult = true; };
            bottom.Add(ok);
            var help = new Button { Content = "Help", Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5) };
            help.Click += (s, e) => MessageBox.Show(this,
                "Traverse: pick the point to start from, then type bearing and distance legs (the scale factor is applied to typed distances).\n" +
                "Turned Angle, Tangent to Arc and Curve Calcs start those tools.\nList Line writes these numbers to the command history.",
                "Line Computations", MessageBoxButton.OK, MessageBoxImage.Information);
            bottom.Add(help);
            bottom.Add(B("Curve Calcs", Action.CurveCalcs, "The curve solver (CURVECALC)"));
            root.Children.Add(bottom.Panel);

            Content = root;
        }

        private static TextBox Field(string value) => new TextBox
        {
            Text = value, IsReadOnly = true, Margin = new Thickness(0, 3, 0, 3), Padding = new Thickness(3, 2, 3, 2),
            MinWidth = 120, BorderThickness = new Thickness(1),
        };

        /// <summary>Three buttons to a row, equal widths.</summary>
        private sealed class UniformGrid3
        {
            public readonly System.Windows.Controls.Primitives.UniformGrid Panel = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 0) };
            public void Add(UIElement e) => Panel.Children.Add(e);
        }
    }
}
