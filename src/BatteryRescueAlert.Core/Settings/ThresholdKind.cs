namespace BatteryRescueAlert.Core.Settings;

public enum ThresholdKind
{
    Warning1,
    Warning2,
    Critical,
}

public static class ThresholdKindExtensions
{
    /// <summary>Label used on the dashboard.</summary>
    public static string DisplayName(this ThresholdKind kind) => kind switch
    {
        ThresholdKind.Warning1 => "Early warning",
        ThresholdKind.Warning2 => "Low battery",
        ThresholdKind.Critical => "Critical",
        _ => kind.ToString(),
    };

    /// <summary>Label used in the settings form.</summary>
    public static string SettingsLabel(this ThresholdKind kind) => kind switch
    {
        ThresholdKind.Warning1 => "Warning 1",
        ThresholdKind.Warning2 => "Warning 2",
        ThresholdKind.Critical => "Critical",
        _ => kind.ToString(),
    };
}
