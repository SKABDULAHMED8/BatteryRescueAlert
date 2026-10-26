using System;
using System.Runtime.InteropServices;

namespace BatteryRescueAlert.Windows.Native
{
    /// <summary>
    /// Win32 declarations. Every name, value and layout below was checked against Microsoft Learn:
    /// WM_POWERBROADCAST, RegisterPowerSettingNotification, SYSTEM_POWER_STATUS, Power Setting GUIDs.
    /// </summary>
    internal static class NativeMethods
    {
        internal const int WM_POWERBROADCAST = 0x0218;

        internal const int PBT_APMRESUMESUSPEND = 0x0007;
        internal const int PBT_APMPOWERSTATUSCHANGE = 0x000A;
        internal const int PBT_APMRESUMEAUTOMATIC = 0x0012;
        internal const int PBT_POWERSETTINGCHANGE = 0x8013;

        internal const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;

        /// <summary>DWORD 0..100, remaining battery capacity.</summary>
        internal static readonly Guid GuidBatteryPercentageRemaining = new Guid("A7AD8041-B45A-4CAE-87A3-EECBB468A9E1");

        /// <summary>DWORD: 0 = AC, 1 = onboard battery, 2 = short-term source (UPS).</summary>
        internal static readonly Guid GuidAcDcPowerSource = new Guid("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid powerSettingGuid, int flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterPowerSettingNotification(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        internal struct SystemPowerStatus
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    }
}
