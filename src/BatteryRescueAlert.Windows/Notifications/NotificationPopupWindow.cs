using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BatteryRescueAlert.Core.Notifications;

namespace BatteryRescueAlert.Windows.Notifications
{
    /// <summary>
    /// An app-owned desktop popup. It does not depend on Windows Notification Center,
    /// toast registration, Focus Assist, or the legacy NotifyIcon balloon API.
    /// </summary>
    internal sealed class NotificationPopupWindow : Window
    {
        private readonly Border _accent;
        private readonly TextBlock _title;
        private readonly TextBlock _body;
        private readonly DispatcherTimer _timer;

        public NotificationPopupWindow()
        {
            Width = 384;
            Height = 144;
            MinWidth = 384;
            MinHeight = 144;
            MaxWidth = 384;
            MaxHeight = 144;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Title = "Battery Rescue Alert";

            var outer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(27, 35, 49)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(75, 88, 108)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(15),
                SnapsToDevicePixels = true
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _accent = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(63, 145, 245)),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 13, 0)
            };
            Grid.SetColumn(_accent, 0);
            Grid.SetRowSpan(_accent, 2);
            grid.Children.Add(_accent);

            _title = new TextBlock
            {
                Text = "Battery Rescue Alert",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_title, 1);
            Grid.SetRow(_title, 0);
            grid.Children.Add(_title);

            var close = new Button
            {
                Content = "×",
                Width = 25,
                Height = 25,
                Padding = new Thickness(0),
                Margin = new Thickness(8, -4, -4, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(218, 226, 238)),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 20,
                Cursor = System.Windows.Input.Cursors.Hand,
                Focusable = false,
                ToolTip = "Dismiss"
            };
            close.Click += (_, _) => Close();
            Grid.SetColumn(close, 2);
            Grid.SetRow(close, 0);
            grid.Children.Add(close);

            _body = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                LineHeight = 18,
                Foreground = new SolidColorBrush(Color.FromRgb(225, 232, 242)),
                Margin = new Thickness(0, 7, 0, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(_body, 1);
            Grid.SetColumnSpan(_body, 2);
            Grid.SetRow(_body, 1);
            grid.Children.Add(_body);

            outer.Child = grid;
            Content = outer;

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(8)
            };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                Close();
            };
            Closed += (_, _) => _timer.Stop();
        }

        public void Present(AlertMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);
            _title.Text = message.Title;
            _body.Text = message.Body;
            _accent.Background = new SolidColorBrush(message.Severity switch
            {
                AlertSeverity.Critical => Color.FromRgb(239, 68, 68),
                AlertSeverity.Warning => Color.FromRgb(245, 158, 11),
                _ => Color.FromRgb(59, 130, 246)
            });

            if (!IsVisible)
            {
                Rect area = SystemParameters.WorkArea;
                Left = Math.Max(area.Left + 12, area.Right - Width - 18);
                Top = Math.Max(area.Top + 12, area.Bottom - Height - 18);
                Show();
            }

            _timer.Stop();
            _timer.Start();
        }
    }
}
