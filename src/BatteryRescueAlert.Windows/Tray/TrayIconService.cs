using System;
using System.Drawing;
using System.Windows.Forms;
using BatteryRescueAlert.Core.Diagnostics;

namespace BatteryRescueAlert.Windows.Tray
{
    /// <summary>
    /// System tray icon and menu (Battery / Status / Open / Settings / Test Notification / Pause Alerts / Exit).
    /// Visible desktop notifications are handled by WindowsNotificationService, not by legacy tray balloons.
    /// </summary>
    public sealed class TrayIconService : IDisposable
    {
        private const int MaxTooltipLength = 63; // NotifyIcon.Text throws above this.

        private readonly ILog _log;
        private NotifyIcon? _icon;
        private ContextMenuStrip? _menu;
        private ToolStripLabel? _batteryLabel;
        private ToolStripLabel? _statusLabel;
        private ToolStripMenuItem? _pauseItem;
        private Font? _headerFont;
        private Icon? _ownedIcon;
        private bool _paused;

        public TrayIconService(ILog log) => _log = log;

        public event Action? OpenRequested;
        public event Action? SettingsRequested;
        public event Action? TestRequested;
        public event Action? ExitRequested;

        /// <summary>Raised with the NEW desired state (true = pause).</summary>
        public event Action<bool>? PauseToggled;

        public bool IsAvailable => _icon is { Visible: true };

        public bool Initialize()
        {
            try
            {
                _menu = new ContextMenuStrip();
                _headerFont = new Font(_menu.Font, FontStyle.Bold);

                var header = new ToolStripLabel("Battery Rescue Alert") { Font = _headerFont };
                _batteryLabel = new ToolStripLabel("Battery: checking…");
                _statusLabel = new ToolStripLabel("Status: checking…");
                var open = new ToolStripMenuItem("Open");
                var settings = new ToolStripMenuItem("Settings");
                var test = new ToolStripMenuItem("Test Notification");
                _pauseItem = new ToolStripMenuItem("Pause Alerts");
                var exit = new ToolStripMenuItem("Exit");

                open.Click += (_, _) => OpenRequested?.Invoke();
                settings.Click += (_, _) => SettingsRequested?.Invoke();
                test.Click += (_, _) => TestRequested?.Invoke();
                _pauseItem.Click += (_, _) => PauseToggled?.Invoke(!_paused);
                exit.Click += (_, _) => ExitRequested?.Invoke();

                _menu.Items.AddRange(new ToolStripItem[]
                {
                    header, _batteryLabel, _statusLabel, new ToolStripSeparator(),
                    open, settings, test, _pauseItem, new ToolStripSeparator(), exit,
                });

                _ownedIcon = TryLoadAppIcon();
                _icon = new NotifyIcon
                {
                    Icon = _ownedIcon ?? SystemIcons.Information,
                    Text = "Battery Rescue Alert",
                    ContextMenuStrip = _menu,
                    Visible = true,
                };
                _icon.MouseClick += (_, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        OpenRequested?.Invoke();
                    }
                };

                return true;
            }
            catch (Exception ex)
            {
                _log.Error("Creating the tray icon failed", ex);
                return false;
            }
        }

        public void UpdateStatus(string batteryLine, string statusLine, bool paused)
        {
            _paused = paused;
            if (_batteryLabel != null)
            {
                _batteryLabel.Text = batteryLine;
            }

            if (_statusLabel != null)
            {
                _statusLabel.Text = paused ? statusLine + " (alerts paused)" : statusLine;
            }

            if (_pauseItem != null)
            {
                _pauseItem.Text = paused ? "Resume Alerts" : "Pause Alerts";
                _pauseItem.Checked = paused;
            }

            if (_icon != null)
            {
                string tip = "Battery Rescue Alert" + (paused ? " (paused)" : string.Empty) + "\n" + batteryLine;
                _icon.Text = tip.Length <= MaxTooltipLength ? tip : tip.Substring(0, MaxTooltipLength);
            }
        }

        private static Icon? TryLoadAppIcon()
        {
            try
            {
                string? path = Environment.ProcessPath;
                return path == null ? null : Icon.ExtractAssociatedIcon(path);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (_icon != null)
            {
                _icon.Visible = false; // removes the icon immediately instead of leaving a ghost
                _icon.Dispose();
                _icon = null;
            }

            _menu?.Dispose();
            _headerFont?.Dispose();
            _ownedIcon?.Dispose();
        }
    }
}
