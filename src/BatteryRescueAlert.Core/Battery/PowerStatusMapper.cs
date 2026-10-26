namespace BatteryRescueAlert.Core.Battery;

/// <summary>
/// Converts the raw bytes of the Win32 SYSTEM_POWER_STATUS structure into a <see cref="BatteryState"/>.
/// Kept free of P/Invoke so it can be unit tested on any OS.
/// Reference: GetSystemPowerStatus / SYSTEM_POWER_STATUS (Microsoft Learn).
/// </summary>
public static class PowerStatusMapper
{
    private const byte BatteryFlagCharging = 8;
    private const byte BatteryFlagNoBattery = 128;
    private const byte Unknown = 255;

    public static BatteryState Map(byte acLineStatus, byte batteryFlag, byte batteryLifePercent)
    {
        var source = acLineStatus switch
        {
            1 => PowerSource.Ac,
            0 => PowerSource.Battery,
            _ => PowerSource.Unknown,
        };

        // 255 means "unknown status"; otherwise bit 128 means "no system battery".
        if (batteryFlag != Unknown && (batteryFlag & BatteryFlagNoBattery) != 0)
        {
            return new BatteryState(BatteryAvailability.NoBattery, null, source, false, false);
        }

        bool charging = batteryFlag != Unknown && (batteryFlag & BatteryFlagCharging) != 0;

        if (batteryLifePercent > 100) // 255 = unknown
        {
            return new BatteryState(BatteryAvailability.Unknown, null, source, charging, false);
        }

        bool full = source == PowerSource.Ac && !charging && batteryLifePercent == 100;
        return new BatteryState(BatteryAvailability.Available, batteryLifePercent, source, charging, full);
    }
}
