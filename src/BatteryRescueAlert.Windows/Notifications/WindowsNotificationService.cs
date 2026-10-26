using System;
using System.Windows;
using BatteryRescueAlert.Core.Diagnostics;
using BatteryRescueAlert.Core.Notifications;

namespace BatteryRescueAlert.Windows.Notifications
{
    /// <summary>
    /// Displays an app-owned on-screen popup. Unlike a legacy tray balloon, the popup is a real
    /// WPF window owned by this process, so the Test Notification exercises the same display path
    /// used by battery threshold alerts and does not depend on Windows toast registration/settings.
    /// </summary>
    public sealed class WindowsNotificationService : INotificationService
    {
        private readonly ILog _log;
        private readonly NotificationSoundPlayer _soundPlayer;
        private NotificationPopupWindow? _popup;

        public WindowsNotificationService(ILog log, NotificationSoundPlayer soundPlayer)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _soundPlayer = soundPlayer ?? throw new ArgumentNullException(nameof(soundPlayer));
        }

        public NotificationResult Show(AlertMessage message)
        {
            if (message is null)
            {
                return NotificationResult.Failed("There is no notification message to display.");
            }

            try
            {
                Application? app = Application.Current;
                if (app is null)
                {
                    return NotificationResult.Failed("The desktop notification window is not available.");
                }

                return app.Dispatcher.CheckAccess()
                    ? ShowOnUiThread(message)
                    : app.Dispatcher.Invoke(() => ShowOnUiThread(message));
            }
            catch (Exception ex)
            {
                _log.Error("Displaying the on-screen notification failed", ex);
                return NotificationResult.Failed("Battery Rescue Alert could not display its on-screen alert.");
            }
        }

        private NotificationResult ShowOnUiThread(AlertMessage message)
        {
            if (_popup is null)
            {
                var created = new NotificationPopupWindow();
                _popup = created;
                created.Closed += (_, _) => _popup = null;
            }

            NotificationPopupWindow? popup = _popup;
            if (popup is null)
            {
                return NotificationResult.Failed("Battery Rescue Alert could not create its on-screen alert.");
            }

            popup.Present(message);
            if (!popup.IsVisible)
            {
                return NotificationResult.Failed("Battery Rescue Alert could not open its on-screen alert.");
            }

            // Sound failures must not suppress the visible alert. Battery alerts and the Test
            // Notification use the selected tone. Informational tray-hint popups request the
            // Windows Notification event tone explicitly, independently of that selection.
            try
            {
                NotificationResult soundResult = message.UseSystemNotificationSound
                    ? _soundPlayer.PlaySystemNotificationDefault()
                    : _soundPlayer.PlaySelected();
                if (!soundResult.Requested)
                {
                    _log.Warn(soundResult.Error ?? "The notification sound could not be played.");
                }
            }
            catch (Exception ex)
            {
                // Audio is best-effort. An audio device, codec, or registry error must never turn
                // a successfully displayed battery warning into a failed notification.
                _log.Warn("The on-screen alert was displayed, but sound playback failed: " + ex.Message);
            }

            _log.Info($"On-screen notification displayed: {message.Title}");
            return NotificationResult.Ok;
        }
    }
}
