using System;
using System.IO;
using Microsoft.Win32;

namespace BatteryRescueAlert.Windows.Startup
{
    /// <summary>
    /// "Start with Windows" for the CURRENT USER only (HKCU Run key). No administrator rights, no services,
    /// no scheduled tasks. The registry is the single source of truth for the checkbox.
    /// </summary>
    public static class StartupRegistration
    {
        public const string BackgroundArgument = "--background";

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ValueName = "BatteryRescueAlert";

        public static string BuildCommand(string exePath) => "\"" + exePath + "\" " + BackgroundArgument;

        public static bool IsEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) is string;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// True when the user switched the entry off in Task Manager / Windows Settings.
        /// Windows then ignores the Run value, so we tell the user instead of pretending it works.
        /// </summary>
        public static bool IsBlockedByWindowsSettings()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: false);
                return key?.GetValue(ValueName) is byte[] data && data.Length > 0 && (data[0] & 1) == 1;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TrySet(bool enabled, string? exePath, out string? error)
        {
            error = null;
            try
            {
                if (!enabled)
                {
                    using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                    key?.DeleteValue(ValueName, throwOnMissingValue: false);
                    return true;
                }

                if (string.IsNullOrWhiteSpace(exePath) ||
                    !string.Equals(Path.GetExtension(exePath), ".exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileNameWithoutExtension(exePath), "dotnet", StringComparison.OrdinalIgnoreCase))
                {
                    error = "Starting with Windows is only available when running the installed BatteryRescueAlert.exe.";
                    return false;
                }

                using RegistryKey runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                runKey.SetValue(ValueName, BuildCommand(exePath), RegistryValueKind.String);
                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                error = "Windows did not allow changing the startup setting for your account.";
                return false;
            }
        }
    }
}
