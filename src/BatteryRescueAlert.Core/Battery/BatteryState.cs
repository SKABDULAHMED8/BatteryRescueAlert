namespace BatteryRescueAlert.Core.Battery;

/// <summary>Whether Windows is able to give us battery information right now.</summary>
public enum BatteryAvailability
{
    /// <summary>Windows did not report usable data (temporary or API failure). Never treated as 0%.</summary>
    Unknown,
    /// <summary>Windows reports that this device has no battery (desktop PC, VM).</summary>
    NoBattery,
    /// <summary>A valid percentage is available.</summary>
    Available,
}

public enum PowerSource
{
    Unknown,
    Battery,
    Ac,
}

/// <summary>
/// Immutable snapshot of what Windows reports about the system battery.
/// The percentage is exactly the value Windows reports; for multi-battery devices this is
/// the system-wide value from GetSystemPowerStatus (no local aggregation is performed).
/// </summary>
public sealed record BatteryState(
    BatteryAvailability Availability,
    int? Percent,
    PowerSource Source,
    bool IsCharging,
    bool IsFullyCharged)
{
    public static BatteryState Unavailable { get; } =
        new(BatteryAvailability.Unknown, null, PowerSource.Unknown, false, false);

    public bool OnAc => Source == PowerSource.Ac;

    public bool HasPercent => Availability == BatteryAvailability.Available && Percent is >= 0 and <= 100;
}
