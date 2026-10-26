using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using BatteryRescueAlert.Core.Diagnostics;
using BatteryRescueAlert.Windows.Native;

namespace BatteryRescueAlert.Windows.Power
{
    /// <summary>
    /// An invisible top-level window that receives WM_POWERBROADCAST.
    ///
    /// It is deliberately NOT a message-only window: PBT_APMPOWERSTATUSCHANGE and the resume events are
    /// broadcast messages, and broadcasts reach top-level windows only. The window is never shown
    /// (no WS_VISIBLE), has no size and no taskbar button. Must be created on the UI thread.
    /// </summary>
    internal sealed class PowerEventWindow : IDisposable
    {
        private const int WsPopup = unchecked((int)0x80000000);

        private readonly ILog _log;
        private HwndSource? _source;
        private IntPtr _batteryHandle;
        private IntPtr _powerSourceHandle;

        public PowerEventWindow(ILog log) => _log = log;

        /// <summary>Raised on the UI thread for every power message we care about.</summary>
        public event Action? PowerChanged;

        public bool Start()
        {
            try
            {
                var parameters = new HwndSourceParameters("BatteryRescueAlertPowerWindow")
                {
                    PositionX = 0,
                    PositionY = 0,
                    Width = 0,
                    Height = 0,
                    WindowStyle = WsPopup,
                    ParentWindow = IntPtr.Zero,
                };

                _source = new HwndSource(parameters);
                _source.AddHook(WndProc);

                Guid battery = NativeMethods.GuidBatteryPercentageRemaining;
                _batteryHandle = NativeMethods.RegisterPowerSettingNotification(
                    _source.Handle, ref battery, NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);
                if (_batteryHandle == IntPtr.Zero)
                {
                    _log.Warn($"RegisterPowerSettingNotification(battery) failed, Win32 error {Marshal.GetLastWin32Error()}");
                }

                Guid powerSource = NativeMethods.GuidAcDcPowerSource;
                _powerSourceHandle = NativeMethods.RegisterPowerSettingNotification(
                    _source.Handle, ref powerSource, NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);
                if (_powerSourceHandle == IntPtr.Zero)
                {
                    _log.Warn($"RegisterPowerSettingNotification(power source) failed, Win32 error {Marshal.GetLastWin32Error()}");
                }

                // Even if registration failed, WM_POWERBROADCAST/PBT_APMPOWERSTATUSCHANGE and resume
                // still arrive, and the monitor's safety refresh covers the rest.
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("Creating the power event window failed", ex);
                return false;
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_POWERBROADCAST)
            {
                switch (wParam.ToInt64())
                {
                    case NativeMethods.PBT_APMPOWERSTATUSCHANGE:
                    case NativeMethods.PBT_POWERSETTINGCHANGE:
                    case NativeMethods.PBT_APMRESUMEAUTOMATIC:
                    case NativeMethods.PBT_APMRESUMESUSPEND:
                        try
                        {
                            PowerChanged?.Invoke();
                        }
                        catch (Exception ex)
                        {
                            _log.Error("Power handler threw", ex);
                        }

                        break;
                }
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_batteryHandle != IntPtr.Zero)
            {
                NativeMethods.UnregisterPowerSettingNotification(_batteryHandle);
                _batteryHandle = IntPtr.Zero;
            }

            if (_powerSourceHandle != IntPtr.Zero)
            {
                NativeMethods.UnregisterPowerSettingNotification(_powerSourceHandle);
                _powerSourceHandle = IntPtr.Zero;
            }

            if (_source != null)
            {
                _source.RemoveHook(WndProc);
                _source.Dispose();
                _source = null;
            }
        }
    }
}
