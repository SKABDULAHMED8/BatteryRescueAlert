using System.Text.Json.Serialization;

namespace BatteryRescueAlert.Core.Settings;

public sealed class ThresholdSetting
{
    public ThresholdKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    public int Percent { get; set; }

    public ThresholdSetting Clone() => new() { Kind = Kind, Enabled = Enabled, Percent = Percent };
}

/// <summary>User configuration persisted as JSON under %APPDATA%. Contains no personal data.</summary>
public sealed class AppSettings
{
    public const int CurrentVersion = 1;
    public const int MinPercent = 1;
    public const int MaxPercent = 99;
    public const string DefaultNotificationSoundId = "system:notification";

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Master switch for low-battery alerts.</summary>
    public bool AlertsEnabled { get; set; } = true;

    /// <summary>Whether alerts are shown as Windows notifications.</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Selected alert tone. Custom-tone identifiers refer to copies in the app data folder.</summary>
    public string NotificationSoundId { get; set; } = DefaultNotificationSoundId;

    /// <summary>Informational mirror of the HKCU Run entry. The registry is the source of truth.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Optional small rolling diagnostic log. Off by default.</summary>
    public bool DiagnosticLogging { get; set; }

    public ThresholdSetting Warning1 { get; set; } = new() { Kind = ThresholdKind.Warning1, Enabled = true, Percent = 30 };
    public ThresholdSetting Warning2 { get; set; } = new() { Kind = ThresholdKind.Warning2, Enabled = true, Percent = 25 };
    public ThresholdSetting Critical { get; set; } = new() { Kind = ThresholdKind.Critical, Enabled = true, Percent = 20 };

    [JsonIgnore]
    public IReadOnlyList<ThresholdSetting> Thresholds => new[] { Warning1, Warning2, Critical };

    public static AppSettings CreateDefault() => new();

    public AppSettings Clone() => new()
    {
        Version = Version,
        AlertsEnabled = AlertsEnabled,
        NotificationsEnabled = NotificationsEnabled,
        NotificationSoundId = NotificationSoundId,
        StartWithWindows = StartWithWindows,
        DiagnosticLogging = DiagnosticLogging,
        Warning1 = Warning1.Clone(),
        Warning2 = Warning2.Clone(),
        Critical = Critical.Clone(),
    };

    /// <summary>Repairs structurally incomplete data (for example a JSON null) after loading.</summary>
    public void EnsureComplete()
    {
        var defaults = CreateDefault();
        Warning1 ??= defaults.Warning1;
        Warning2 ??= defaults.Warning2;
        Critical ??= defaults.Critical;
        if (string.IsNullOrWhiteSpace(NotificationSoundId))
        {
            NotificationSoundId = DefaultNotificationSoundId;
        }
        Warning1.Kind = ThresholdKind.Warning1;
        Warning2.Kind = ThresholdKind.Warning2;
        Critical.Kind = ThresholdKind.Critical;
    }

    /// <summary>True when the threshold part (enabled flags or percentages) differs.</summary>
    public bool ThresholdsDiffer(AppSettings other) =>
        Thresholds.Zip(other.Thresholds, (a, b) => a.Enabled != b.Enabled || a.Percent != b.Percent).Any(x => x);
}
