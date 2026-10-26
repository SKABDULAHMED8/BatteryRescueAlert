using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using BatteryRescueAlert.Core.ViewModels;

namespace BatteryRescueAlert.Desktop
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            FitToWorkArea(center: true);
            Loaded += (_, _) => FitToWorkArea(center: false);
        }

        private const int WmSettingChange = 0x001A;
        private const int WmDisplayChange = 0x007E;
        private const int WmDpiChanged = 0x02E0;
        private HwndSource? _windowSource;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _windowSource?.AddHook(WindowMessageHook);
        }

        protected override void OnClosed(EventArgs e)
        {
            _windowSource?.RemoveHook(WindowMessageHook);
            _windowSource = null;
            base.OnClosed(e);
        }

        private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message is WmSettingChange or WmDisplayChange or WmDpiChanged)
            {
                // Let Windows complete its non-client/maximize recalculation first. In particular,
                // do not impose MaxWidth/MaxHeight on the maximized HWND: that breaks taskbar
                // auto-hide and can leave a desktop strip after a display/DPI change.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (WindowState != WindowState.Maximized)
                    {
                        FitToWorkArea(center: false);
                    }
                }), DispatcherPriority.ApplicationIdle);
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Keep the complete title bar and system buttons inside the usable desktop area.
        /// The content area scrolls, so the window can safely become shorter on small screens.
        /// WPF dimensions and SystemParameters.WorkArea are both device-independent units.
        /// </summary>
        private void FitToWorkArea(bool center)
        {
            Rect workArea = SystemParameters.WorkArea;
            const double margin = 16;
            double maxNormalWidth = Math.Max(320, workArea.Width - margin * 2);
            double maxNormalHeight = Math.Max(360, workArea.Height - margin * 2);

            // Clamp only the normal/restored size. Do not set MaxWidth/MaxHeight: Windows must
            // calculate maximized bounds itself so the window follows taskbar auto-hide, monitor
            // changes, and non-client frame metrics correctly.
            MinWidth = Math.Min(460, maxNormalWidth);
            MinHeight = Math.Min(560, maxNormalHeight);
            Width = Math.Min(Width, maxNormalWidth);
            Height = Math.Min(Height, maxNormalHeight);

            if (center || double.IsNaN(Left) || double.IsNaN(Top))
            {
                Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
                Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
                return;
            }

            Left = Math.Max(workArea.Left, Math.Min(Left, workArea.Right - Width));
            Top = Math.Max(workArea.Top, Math.Min(Top, workArea.Bottom - Height));
        }

        /// <summary>When false (the default) closing the window only hides it; the app keeps running in the tray.</summary>
        public bool AllowClose { get; set; }

        /// <summary>Raised when the window is hidden because the user closed it.</summary>
        public event Action? HiddenToTray;

        /// <summary>0 = Dashboard, 1 = Settings, 2 = About.</summary>
        public void ShowPage(int page)
        {
            RadioButton tab = page switch
            {
                1 => NavSettings,
                2 => NavAbout,
                _ => NavDashboard,
            };
            tab.IsChecked = true;
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            // Checked fires during InitializeComponent before every element exists.
            if (PageDashboard is null || PageSettings is null || PageAbout is null || SaveBar is null)
            {
                return;
            }

            if (sender is not RadioButton { Tag: string tag } ||
                !int.TryParse(tag, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int page))
            {
                return;
            }

            PageDashboard.Visibility = page == 0 ? Visibility.Visible : Visibility.Collapsed;
            PageSettings.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
            PageAbout.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
            SaveBar.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Digits only; the view model still validates the range and ordering.</summary>
        private void Number_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!AllowClose)
            {
                e.Cancel = true;
                Hide();
                HiddenToTray?.Invoke();
                return;
            }

            base.OnClosing(e);
        }
    }
}
