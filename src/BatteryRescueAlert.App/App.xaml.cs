using System;
using System.Linq;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using BatteryRescueAlert.Windows.Startup;
using BatteryRescueAlert.Windows.Theming;

namespace BatteryRescueAlert.Desktop
{
    public partial class App : Application
    {
        // "Local\" scopes the names to the current Windows session, so two users on one PC do not collide.
        private const string MutexName = @"Local\BatteryRescueAlert.SingleInstance";
        private const string ActivateEventName = @"Local\BatteryRescueAlert.Activate";

        private Mutex? _singleInstance;
        private EventWaitHandle? _activate;
        private RegisteredWaitHandle? _activateWait;
        private AppHost? _host;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _singleInstance = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

            if (!isFirstInstance)
            {
                // Ask the running instance to show its window, then leave quietly.
                _activate.Set();
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (_, args) =>
            {
                _host?.LogUnhandled(args.Exception);
                args.Handled = true; // a battery utility must keep running
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                _host?.LogUnhandled(args.ExceptionObject as Exception);

            ApplyTheme();
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SessionEnding += (_, _) => _host?.Dispose();

            bool background = e.Args.Any(a => string.Equals(a, StartupRegistration.BackgroundArgument, StringComparison.OrdinalIgnoreCase));
            _host = new AppHost(this);
            _host.Start(background);

            // Event-driven (no polling): wakes only when a second instance signals.
            _activateWait = ThreadPool.RegisterWaitForSingleObject(
                _activate,
                (_, _) => Dispatcher.BeginInvoke(new Action(() => _host?.ShowMainWindow())),
                null, Timeout.Infinite, executeOnlyOnce: false);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _activateWait?.Unregister(null);
            _host?.Dispose();
            _activate?.Dispose();

            if (_singleInstance != null)
            {
                try
                {
                    _singleInstance.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Not owned (second instance): nothing to release.
                }

                _singleInstance.Dispose();
            }

            base.OnExit(e);
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                Dispatcher.BeginInvoke(new Action(ApplyTheme));
            }
        }

        /// <summary>Follows the Windows light/dark app mode.</summary>
        private void ApplyTheme()
        {
            string file = SystemTheme.IsLight() ? "Colors.Light.xaml" : "Colors.Dark.xaml";
            var palette = new ResourceDictionary { Source = new Uri("Themes/" + file, UriKind.Relative) };
            Resources.MergedDictionaries[0] = palette;
        }
    }
}
