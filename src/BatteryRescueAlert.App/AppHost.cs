using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;
using BatteryRescueAlert.Core.Battery;
using BatteryRescueAlert.Core.Coordination;
using BatteryRescueAlert.Core.Diagnostics;
using BatteryRescueAlert.Core.Notifications;
using BatteryRescueAlert.Core.Settings;
using BatteryRescueAlert.Core.ViewModels;
using BatteryRescueAlert.Windows.Notifications;
using BatteryRescueAlert.Windows.Power;
using BatteryRescueAlert.Windows.Startup;
using BatteryRescueAlert.Windows.Tray;

namespace BatteryRescueAlert.Desktop
{
    /// <summary>
    /// Composition root: creates the services, connects Windows events to the coordinator and the coordinator
    /// to the window and tray. Everything runs on the UI thread.
    /// </summary>
    internal sealed class AppHost : IDisposable
    {
        private readonly Application _app;
        private readonly SettingsRepository _settingsRepository;
        private readonly AlertStateRepository _stateRepository;
        private readonly ILog _log;

        private AppSettings _settings;
        private string? _loadWarning;
        private TrayIconService? _tray;
        private WindowsBatteryMonitor? _monitor;
        private AlertCoordinator? _coordinator;
        private WindowsNotificationService? _notifier;
        private NotificationSoundCatalog? _soundCatalog;
        private NotificationSoundPlayer? _soundPlayer;
        private MainViewModel? _viewModel;
        private MainWindow? _window;
        private bool _trayHintShown;
        private bool _disposed;

        public AppHost(Application app)
        {
            _app = app;
            _settings = AppSettings.CreateDefault();
            // The log reads the CURRENT setting every time, so toggling it takes effect immediately.
            _log = new RollingFileLog(AppPaths.LogFile, () => _settings.DiagnosticLogging);
            _settingsRepository = new SettingsRepository(AppPaths.SettingsFile, _log);
            _stateRepository = new AlertStateRepository(AppPaths.StateFile, _log);
        }

        public void Start(bool background)
        {
            _settings = _settingsRepository.LoadOrDefault(out _loadWarning);
            // The registry is the source of truth for "start with Windows".
            _settings.StartWithWindows = StartupRegistration.IsEnabled();
            _soundCatalog = new NotificationSoundCatalog(AppPaths.CustomTonesDirectory);
            NormalizeSelectedTone();
            var soundPlayer = new NotificationSoundPlayer(_soundCatalog, () => _settings.NotificationSoundId, _log);
            _soundPlayer = soundPlayer;
            _log.Info("Application started");

            _tray = new TrayIconService(_log);
            bool trayOk = _tray.Initialize();
            _tray.OpenRequested += () => ShowMainWindow();
            _tray.SettingsRequested += () => ShowMainWindow(page: 1);
            _tray.TestRequested += OnTrayTestRequested;
            _tray.PauseToggled += paused => _coordinator?.SetPaused(paused);
            _tray.ExitRequested += ExitApplication;

            var notifier = new WindowsNotificationService(_log, soundPlayer);
            _notifier = notifier;
            _coordinator = new AlertCoordinator(
                _settings,
                new ThresholdEngine(_stateRepository.LoadOrNew()),
                notifier,
                state => _stateRepository.TrySave(state, out _),
                _log);

            _viewModel = new MainViewModel(
                _settings,
                SaveSettings,
                () => _coordinator.SendTest(),
                paused => _coordinator.SetPaused(paused),
                OpenWindowsNotificationSettings,
                GetVersion(),
                @"%APPDATA%\" + AppPaths.FolderName,
                _loadWarning,
                getSoundOptions: () => _soundCatalog?.GetOptions() ?? Array.Empty<NotificationSoundOption>(),
                addCustomTone: AddCustomTone,
                deleteCustomTone: DeleteCustomTone,
                previewSound: soundId => _soundPlayer?.Play(soundId) ?? NotificationResult.Failed("The sound service is unavailable."));
            UpdateStartupNote();

            _coordinator.Updated += OnCoordinatorUpdated;
            _viewModel.Apply(_coordinator.Snapshot());

            _monitor = new WindowsBatteryMonitor(_log);
            _monitor.StateChanged += (_, state) => _coordinator.OnBatteryState(state);
            _monitor.Start();

            // Never start invisible: without a tray icon the window is the only way to reach the app.
            if (!background || !trayOk)
            {
                ShowMainWindow();
            }
        }

        public void ShowMainWindow(int page = 0)
        {
            if (_viewModel is null)
            {
                return;
            }

            if (_window is null)
            {
                _window = new MainWindow(_viewModel);
                _window.HiddenToTray += OnWindowHidden;
            }

            _window.ShowPage(page);
            if (!_window.IsVisible)
            {
                _window.Show();
            }

            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.Activate();
        }

        public void LogUnhandled(Exception? exception) => _log.Error("Unhandled exception", exception);

        private void OnCoordinatorUpdated(CoordinatorSnapshot snapshot)
        {
            _viewModel?.Apply(snapshot);
            DashboardModel model = DashboardPresenter.Present(snapshot);
            _tray?.UpdateStatus(model.TrayBatteryLine, model.TrayStatusLine, snapshot.Paused);
        }

        private void OnTrayTestRequested()
        {
            if (_coordinator is null)
            {
                return;
            }

            NotificationResult result = _coordinator.SendTest();
            if (!result.Requested)
            {
                // The tray test must never fail silently: show the reason in the window.
                ShowMainWindow();
                MessageBox.Show(result.Error ?? "The test notification could not be shown.",
                    "Battery Rescue Alert", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private SaveOutcome SaveSettings(AppSettings draft)
        {
            if (_coordinator is null)
            {
                return new SaveOutcome(false, "The application is still starting. Please try again.");
            }

            ValidationResult validation = SettingsValidator.Validate(draft);
            if (!validation.IsValid)
            {
                return new SaveOutcome(false, validation.Errors[0].Message);
            }

            // Persist first, so a failed write never leaves Windows startup half-changed.
            if (!_settingsRepository.TrySave(draft, out string? saveError))
            {
                return new SaveOutcome(false, saveError ?? "Your settings could not be saved.");
            }

            string message = "Settings saved.";
            if (draft.StartWithWindows != StartupRegistration.IsEnabled())
            {
                if (!StartupRegistration.TrySet(draft.StartWithWindows, Environment.ProcessPath, out string? startupError))
                {
                    draft.StartWithWindows = StartupRegistration.IsEnabled();
                    _settingsRepository.TrySave(draft, out _);
                    message = "Settings saved, but the startup option could not be changed. " + startupError;
                }
            }

            _settings = draft;
            _coordinator.ApplySettings(draft);
            UpdateStartupNote();
            return new SaveOutcome(true, message, draft);
        }

        private void NormalizeSelectedTone()
        {
            if (_soundCatalog is null || !_settings.NotificationSoundId.StartsWith("custom:", StringComparison.Ordinal))
            {
                return;
            }

            string? selectedPath = _soundCatalog.GetCustomTonePath(_settings.NotificationSoundId);
            if (selectedPath is not null && File.Exists(selectedPath))
            {
                return;
            }

            _log.Warn("Saved custom tone is missing. Resetting notification sound to Windows default.");
            _settings.NotificationSoundId = AppSettings.DefaultNotificationSoundId;
            if (!_settingsRepository.TrySave(_settings, out string? error) && error is not null)
            {
                _log.Warn("Could not save the notification sound fallback: " + error);
            }
        }

        private NotificationSoundOption? AddCustomTone()
        {
            if (_soundCatalog is null)
            {
                return null;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Add a custom notification tone",
                Filter = "Supported audio tones (*.wav;*.mp3;*.wma;*.m4a;*.aac;*.flac)|*.wav;*.mp3;*.wma;*.m4a;*.aac;*.flac|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
                DereferenceLinks = true
            };

            if (dialog.ShowDialog(_window) != true)
            {
                return null;
            }

            NotificationSoundOption imported;
            try
            {
                imported = _soundCatalog.Import(dialog.FileName);
            }
            catch (Exception ex)
            {
                _log.Warn("Importing a custom notification tone failed: " + ex.Message);
                MessageBox.Show(_window,
                    ex.Message,
                    "Could not add custom tone",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            MessageBox.Show(_window,
                "Custom tone added and backed up successfully.\n\n" +
                "Backup folder:\n" + _soundCatalog.FolderPath + "\n\n" +
                "Battery Rescue Alert uses this backup copy. You can move or delete the original file from Downloads or another folder without affecting the saved copy.\n\n" +
                "To remove the backup later, select this tone and click 'Delete selected custom tone'.\n\n" +
                "The tone is selected in Settings as a draft. Click 'Save changes' to apply it to battery alerts.",
                "Custom tone backed up",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return imported;
        }

        private bool DeleteCustomTone(string soundId)
        {
            if (_soundCatalog is null)
            {
                MessageBox.Show(_window, "The custom tone library is unavailable.",
                    "Could not delete custom tone", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            _soundPlayer?.Stop();
            if (!_soundCatalog.TryDelete(soundId, out string? error))
            {
                MessageBox.Show(_window,
                    error ?? "The custom tone could not be deleted.",
                    "Could not delete custom tone",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            // Remove stale selections immediately. If the last custom tone is removed, select the
            // Windows default even when the deleted file was only an unused custom option.
            bool removedWasActive = string.Equals(_settings.NotificationSoundId, soundId, StringComparison.Ordinal);
            bool customTonesRemain = _soundCatalog.GetOptions().Any(option => option.IsCustom);
            bool defaultToneSaved = true;
            string? saveError = null;
            if (removedWasActive || !customTonesRemain)
            {
                AppSettings updated = _settings.Clone();
                updated.NotificationSoundId = AppSettings.DefaultNotificationSoundId;
                _settings = updated;
                defaultToneSaved = _settingsRepository.TrySave(updated, out saveError);
                if (!defaultToneSaved && saveError is not null)
                {
                    _log.Warn("Could not persist the Windows default tone after deleting a custom tone: " + saveError);
                }
                _coordinator?.ApplySettings(updated);
            }

            string confirmation = "The custom tone was removed from the Battery Rescue Alert backup successfully.";
            if (!customTonesRemain)
            {
                confirmation += "\n\nNo custom tones remain in the backup folder, so 'Windows notification (default)' has been selected automatically.";
            }
            else if (removedWasActive)
            {
                confirmation += "\n\nThe removed tone was active. 'Windows notification (default)' has been selected automatically. Other backed-up custom tones are still available.";
            }
            else
            {
                confirmation += "\n\nOther custom tones remain available. Your currently saved alert sound has not changed.";
            }

            if (!defaultToneSaved)
            {
                confirmation += "\n\nWarning: the fallback setting could not be saved to disk. The app has switched to the Windows default for this session. Please open Settings and save your changes.";
            }

            MessageBox.Show(_window, confirmation,
                defaultToneSaved ? "Custom tone removed" : "Custom tone removed — settings need saving",
                MessageBoxButton.OK,
                defaultToneSaved ? MessageBoxImage.Information : MessageBoxImage.Warning);

            return true;
        }

        private void UpdateStartupNote()
        {
            if (_viewModel is null)
            {
                return;
            }

            _viewModel.StartupNote = _settings.StartWithWindows && StartupRegistration.IsBlockedByWindowsSettings()
                ? "Windows has switched this off for the app (Settings > Apps > Startup, or Task Manager). Turn it back on there."
                : null;
        }

        private void OnWindowHidden()
        {
            if (_trayHintShown || _tray is null || !_tray.IsAvailable)
            {
                return;
            }

            _trayHintShown = true;
            NotificationResult result = _notifier?.Show(new AlertMessage(
                "Battery Rescue Alert",
                "Still running in the notification area. Use the tray icon to open or exit.",
                AlertSeverity.Info,
                UseSystemNotificationSound: true))
                ?? NotificationResult.Failed("The on-screen notification service is not available.");
            if (!result.Requested)
            {
                _log.Warn("Tray hint could not be displayed");
            }
        }

        private void OpenWindowsNotificationSettings()
        {
            try
            {
                // Fixed, well-known Windows settings URI; no user input is ever placed in this string.
                Process.Start(new ProcessStartInfo("ms-settings:notifications") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _log.Error("Opening Windows notification settings failed", ex);
            }
        }

        private void ExitApplication()
        {
            if (_window is not null)
            {
                _window.AllowClose = true;
                _window.Close();
            }

            Dispose();
            _app.Shutdown();
        }

        private static string GetVersion()
        {
            Assembly assembly = typeof(AppHost).Assembly;
            string? info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info))
            {
                int plus = info!.IndexOf('+');
                return plus > 0 ? info.Substring(0, plus) : info;
            }

            return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _log.Info("Application stopped");
            _monitor?.Dispose();
            _tray?.Dispose();
            _soundPlayer?.Dispose();
        }
    }
}
