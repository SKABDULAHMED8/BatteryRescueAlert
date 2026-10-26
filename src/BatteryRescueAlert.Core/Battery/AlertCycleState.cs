using BatteryRescueAlert.Core.Settings;

namespace BatteryRescueAlert.Core.Battery;

/// <summary>
/// Remembers which thresholds have already alerted during the current discharge cycle.
/// The fired set and a small schema version are persisted to prevent duplicates across restarts.
/// No personal data is stored.
/// </summary>
public sealed class AlertCycleState
{
    public HashSet<ThresholdKind> Fired { get; } = new();

    /// <summary>Last percentage evaluated in this process. Informational only.</summary>
    public int? LastPercent { get; set; }

    public AlertCycleSnapshot ToSnapshot() => new() { Version = AlertCycleSnapshot.CurrentVersion, Fired = Fired.OrderBy(k => k).ToList() };

    public static AlertCycleState FromSnapshot(AlertCycleSnapshot? snapshot)
    {
        var state = new AlertCycleState();
        if (snapshot is null)
        {
            return state;
        }

        // Version 1 did not associate fired flags with the threshold definitions that produced
        // them. Those flags may belong to an older configuration and can suppress valid alerts.
        // Treat that legacy state as a new cycle once; settings.json is left untouched.
        if (snapshot.Version < AlertCycleSnapshot.CurrentVersion)
        {
            return state;
        }

        if (snapshot.Fired is { } fired)
        {
            foreach (var kind in fired.Where(Enum.IsDefined))
            {
                state.Fired.Add(kind);
            }
        }

        return state;
    }
}

/// <summary>JSON shape of the persisted alert state.</summary>
public sealed class AlertCycleSnapshot
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public List<ThresholdKind> Fired { get; set; } = new();
}
