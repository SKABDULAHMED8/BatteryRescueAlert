using System;
using Microsoft.Win32;

namespace BatteryRescueAlert.Windows.Theming
{
    /// <summary>Reads the Windows "app mode" (light or dark) setting. Defaults to light if unavailable.</summary>
    public static class SystemTheme
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public static bool IsLight()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
                return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
