using System;
using System.Windows.Threading;
using BatteryRescueAlert.Core.Battery;
using BatteryRescueAlert.Core.Diagnostics;
using BatteryRescueAlert.Windows.Native;

namespace BatteryRescueAlert.Windows.Power
{
    /// <summary>
    /// Event-driven battery monitor. The process is idle until Windows sends a power message.
    ///
    /// Reads happen only
    ///   - once at start,
    ///   - ~300 ms after a power message (bursts are coalesced into one read),
    ///   - on resume from sleep (a power message),
    ///   - on a safety refresh (default 5 minutes) in case a driver misses an event.
    /// There is no tight polling loop. Must be created and used on the UI thread.
    /// </summary>
    public sealed class WindowsBatteryMonitor : IDisposable
    {
        private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(300);

        private readonly ILog _log;
        private readonly PowerEventWindow _window;
        private readonly DispatcherTimer _debounce;
        private readonly DispatcherTimer _safetyRefresh;
        private bool _started;
        private BatteryState? _last;

        public WindowsBatteryMonitor(ILog log, TimeSpan? safetyRefreshInterval = null)
        {
            _log = log;
            _window = new PowerEventWindow(log);
            _window.PowerChanged += OnPowerChanged;

            _debounce = new DispatcherTimer { Interval = DebounceDelay };
            _debounce.Tick += (_, _) =>
            {
                _debounce.Stop();
                Refresh();
            };

            _safetyRefresh = new DispatcherTimer { Interval = safetyRefreshInterval ?? TimeSpan.FromMinutes(5) };
            _safetyRefresh.Tick += (_, _) => Refresh();
        }

        /// <summary>Raised on the UI thread when the reported state differs from the previous one.</summary>
        public event EventHandler<BatteryState>? StateChanged;

        public BatteryState Current => _last ?? BatteryState.Unavailable;

        public void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            if (!_window.Start())
            {
                _log.Warn("Power events unavailable; relying on the safety refresh only");
            }

            _safetyRefresh.Start();
            Refresh();
        }

        /// <summary>Reads the current state from Windows and raises <see cref="StateChanged"/> if it changed.</summary>
        public void Refresh()
        {
            BatteryState state = ReadNow(_log);
            if (_last is not null && _last.Equals(state))
            {
                return;
            }

            _last = state;
            StateChanged?.Invoke(this, state);
        }

        internal static BatteryState ReadNow(ILog log)
        {
            try
            {
                if (!NativeMethods.GetSystemPowerStatus(out NativeMethods.SystemPowerStatus status))
                {
                    log.Warn("GetSystemPowerStatus failed");
                    return BatteryState.Unavailable;
                }

                return PowerStatusMapper.Map(status.ACLineStatus, status.BatteryFlag, status.BatteryLifePercent);
            }
            catch (Exception ex)
            {
                log.Error("Reading the power status failed", ex);
                return BatteryState.Unavailable;
            }
        }

        private void OnPowerChanged()
        {
            // Restart the debounce window so a burst of messages causes a single read.
            _debounce.Stop();
            _debounce.Start();
        }

        public void Dispose()
        {
            _started = false;
            _debounce.Stop();
            _safetyRefresh.Stop();
            _window.PowerChanged -= OnPowerChanged;
            _window.Dispose();
        }
    }
}
