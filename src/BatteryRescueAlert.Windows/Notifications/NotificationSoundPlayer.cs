using System;
using System.IO;
using System.Media;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using BatteryRescueAlert.Core.Diagnostics;
using BatteryRescueAlert.Core.Notifications;
using BatteryRescueAlert.Core.Settings;

namespace BatteryRescueAlert.Windows.Notifications
{
    /// <summary>
    /// Plays the current Windows notification event sound or an imported local tone.
    /// Custom audio is capped at five seconds so a long music file cannot keep playing after an alert.
    /// </summary>
    public sealed class NotificationSoundPlayer : IDisposable
    {
        private const double CustomTonePlaybackSeconds = 5;
        private readonly NotificationSoundCatalog _catalog;
        private readonly Func<string> _getSelectedSoundId;
        private readonly ILog _log;
        private readonly DispatcherTimer _cutoffTimer;
        private SoundPlayer? _systemPlayer;
        private MediaPlayer? _mediaPlayer;
        private bool _disposed;

        public NotificationSoundPlayer(NotificationSoundCatalog catalog, Func<string> getSelectedSoundId, ILog log)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _getSelectedSoundId = getSelectedSoundId ?? throw new ArgumentNullException(nameof(getSelectedSoundId));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _cutoffTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(CustomTonePlaybackSeconds)
            };
            _cutoffTimer.Tick += CutoffTimer_Tick;
        }

        public NotificationResult PlaySelected() => Play(_getSelectedSoundId());

        /// <summary>Plays the Windows notification event sound regardless of the selected battery alarm tone.</summary>
        public NotificationResult PlaySystemNotificationDefault()
        {
            if (_disposed)
            {
                return NotificationResult.Failed("The notification sound service has stopped.");
            }

            StopCustomTone();
            StopSystemSound();
            PlaySystemNotificationFallback();
            return NotificationResult.Ok;
        }

        public NotificationResult Play(string soundId)
        {
            if (_disposed)
            {
                return NotificationResult.Failed("The notification sound service has stopped.");
            }

            StopCustomTone();
            StopSystemSound();

            if (!string.IsNullOrWhiteSpace(soundId) && soundId.StartsWith("custom:", StringComparison.Ordinal))
            {
                string? path = _catalog.GetCustomTonePath(soundId);
                if (path is not null && File.Exists(path))
                {
                    try
                    {
                        PlayCustomTone(path);
                        _log.Info("Custom notification tone playback requested");
                        return NotificationResult.Ok;
                    }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException or UriFormatException or NotSupportedException or ArgumentException)
                    {
                        StopCustomTone();
                        _log.Error("Custom tone could not be opened; using the Windows notification sound", ex);
                    }
                }
                else
                {
                    _log.Warn("Selected custom tone is missing; using the Windows notification sound");
                }

                PlaySystemNotificationFallback();
                return NotificationResult.Ok;
            }

            PlaySystemTone(soundId);
            return NotificationResult.Ok;
        }

        private void PlayCustomTone(string path)
        {
            var player = new MediaPlayer { Volume = 0.9 };
            _mediaPlayer = player;
            player.MediaOpened += (_, _) =>
            {
                if (!ReferenceEquals(_mediaPlayer, player))
                {
                    return;
                }

                player.Position = TimeSpan.Zero;
                player.Play();
                _cutoffTimer.Stop();
                _cutoffTimer.Start();
            };
            player.MediaEnded += (_, _) => StopCustomTone(player);
            player.MediaFailed += (_, args) =>
            {
                _log.Warn("The selected custom audio file could not be decoded: " + args.ErrorException?.Message);
                bool wasCurrent = ReferenceEquals(_mediaPlayer, player);
                StopCustomTone(player);
                if (wasCurrent)
                {
                    PlaySystemNotificationFallback();
                }
            };
            player.Open(new Uri(path, UriKind.Absolute));
        }

        public void Stop()
        {
            StopCustomTone();
            StopSystemSound();
        }

        private void StopCustomTone(MediaPlayer? expectedPlayer = null)
        {
            _cutoffTimer.Stop();
            if (_mediaPlayer is null || (expectedPlayer is not null && !ReferenceEquals(_mediaPlayer, expectedPlayer)))
            {
                return;
            }

            MediaPlayer player = _mediaPlayer;
            _mediaPlayer = null;
            try
            {
                player.Stop();
                player.Close();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Security.SecurityException or IOException)
            {
                _log.Warn("Stopping a custom notification tone failed: " + ex.Message);
            }
        }

        private void PlaySystemTone(string? soundId)
        {
            string eventName;
            if (soundId is not null && soundId.StartsWith("system-event:", StringComparison.Ordinal))
            {
                eventName = soundId.Substring("system-event:".Length);
            }
            else
            {
                eventName = soundId switch
                {
                    "system:asterisk" => "SystemAsterisk",
                    "system:exclamation" => "SystemExclamation",
                    "system:question" => "SystemQuestion",
                    "system:hand" => "SystemHand",
                    _ => "Notification.Default"
                };
            }

            if (TryPlayRegistryWave(eventName))
            {
                return;
            }

            if (!string.Equals(eventName, "Notification.Default", StringComparison.Ordinal) &&
                TryPlayRegistryWave("Notification.Default"))
            {
                return;
            }

            PlayAsteriskFallback();
        }

        private void PlaySystemNotificationFallback()
        {
            if (!TryPlayRegistryWave("Notification.Default"))
            {
                PlayAsteriskFallback();
            }
        }

        private bool TryPlayRegistryWave(string eventName)
        {
            string? path = GetCurrentSystemSoundPath(eventName);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                _systemPlayer = new SoundPlayer(path);
                _systemPlayer.Play();
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or NotSupportedException or System.Security.SecurityException)
            {
                _log.Warn("Could not play the Windows sound event " + eventName + ": " + ex.Message);
                StopSystemSound();
                return false;
            }
        }

        private static string? GetCurrentSystemSoundPath(string eventName)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                    @"AppEvents\Schemes\Apps\.Default\" + eventName + @"\.Current");
                string? raw = key?.GetValue(null, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return null;
                }

                string path = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
                return Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase) ? path : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
            {
                return null;
            }
        }

        private void PlayAsteriskFallback()
        {
            try
            {
                SystemSounds.Asterisk.Play();
            }
            catch (Exception ex)
            {
                // Sound is best-effort only. A device/driver issue must never break the visible popup.
                _log.Warn("The Windows notification sound and Asterisk fallback could not be played: " + ex.Message);
            }
        }

        private void StopSystemSound()
        {
            if (_systemPlayer is null)
            {
                return;
            }

            try
            {
                _systemPlayer.Stop();
                _systemPlayer.Dispose();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Security.SecurityException or IOException)
            {
                _log.Warn("Stopping the previous system tone failed: " + ex.Message);
            }
            finally
            {
                _systemPlayer = null;
            }
        }

        private void CutoffTimer_Tick(object? sender, EventArgs e) => StopCustomTone();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
            _cutoffTimer.Tick -= CutoffTimer_Tick;
        }
    }
}
