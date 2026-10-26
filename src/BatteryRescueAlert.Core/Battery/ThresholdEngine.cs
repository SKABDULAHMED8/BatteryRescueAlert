using BatteryRescueAlert.Core.Settings;

namespace BatteryRescueAlert.Core.Battery;

public enum SuppressionReason
{
    /// <summary>Evaluation ran normally (an alert may or may not have been produced).</summary>
    None,
    /// <summary>No usable percentage; state was left untouched. Missing data is never treated as 0%.</summary>
    NoBatteryData,
    InvalidSettings,
    AlertsDisabled,
    Paused,
    /// <summary>Connected to power / charging: alerts are paused, the threshold is not consumed.</summary>
    OnAcPower,
}

public sealed record BatteryAlert(ThresholdKind Kind, int ThresholdPercent, int BatteryPercent);

public sealed record EvaluationResult(BatteryAlert? Alert, SuppressionReason Reason, bool StateChanged);

/// <summary>
/// Pure, hardware-independent alert logic.
///
/// Rules
///  1. A threshold fires when it is enabled, not yet fired in this cycle and the reported percentage is
///     at or below it. The device never has to report the exact value (31 -> 29 fires the 30% alert).
///  2. If several thresholds are crossed at once (for example 40 -> 18) exactly one alert is produced,
///     for the most severe (lowest) one, and all crossed thresholds are marked as fired.
///  3. A fired threshold re-arms when the percentage reaches threshold + <see cref="ResetMargin"/>.
///     That hysteresis avoids both spam around a boundary and resetting on every tiny power change.
///  4. While on AC / charging, or when alerts are paused or disabled, nothing is consumed. Unplugging
///     below a threshold therefore produces one warning, and repeated plug/unplug does not repeat it.
///  5. Editing thresholds silently consumes thresholds the battery is already below
///     (<see cref="OnSettingsChanged"/>) so an edit never triggers a surprise alert.
/// </summary>
public sealed class ThresholdEngine
{
    /// <summary>Percentage points above a threshold at which a fired threshold re-arms.</summary>
    public const int ResetMargin = 5;

    public ThresholdEngine(AlertCycleState? state = null) => State = state ?? new AlertCycleState();

    public AlertCycleState State { get; }

    public EvaluationResult Evaluate(BatteryState battery, AppSettings settings, bool alertsPaused = false)
    {
        ArgumentNullException.ThrowIfNull(battery);
        ArgumentNullException.ThrowIfNull(settings);

        if (!battery.HasPercent)
        {
            return new EvaluationResult(null, SuppressionReason.NoBatteryData, false);
        }

        int percent = battery.Percent!.Value;

        if (!SettingsValidator.Validate(settings).IsValid)
        {
            State.LastPercent = percent;
            return new EvaluationResult(null, SuppressionReason.InvalidSettings, false);
        }

        bool changed = ReArmRecovered(percent, settings);
        State.LastPercent = percent;

        if (!settings.AlertsEnabled)
        {
            return new EvaluationResult(null, SuppressionReason.AlertsDisabled, changed);
        }

        if (alertsPaused)
        {
            return new EvaluationResult(null, SuppressionReason.Paused, changed);
        }

        if (battery.Source == PowerSource.Ac || battery.IsCharging)
        {
            return new EvaluationResult(null, SuppressionReason.OnAcPower, changed);
        }

        var crossed = settings.Thresholds
            .Where(t => t.Enabled && percent <= t.Percent && !State.Fired.Contains(t.Kind))
            .OrderBy(t => t.Percent)
            .ToList();

        if (crossed.Count == 0)
        {
            return new EvaluationResult(null, SuppressionReason.None, changed);
        }

        foreach (var t in crossed)
        {
            State.Fired.Add(t.Kind);
        }

        var worst = crossed[0];
        return new EvaluationResult(new BatteryAlert(worst.Kind, worst.Percent, percent), SuppressionReason.None, true);
    }

    /// <summary>
    /// Applies threshold edits without reusing a fired flag from the old threshold definition.
    /// If a newly configured threshold is already passed, it is silently consumed to avoid a
    /// surprise notification at Save time. If the battery is still above the new threshold,
    /// the threshold is re-armed and will fire when the battery crosses it.
    /// Returns true when the persisted state changed.
    /// </summary>
    public bool OnSettingsChanged(BatteryState battery, AppSettings previousSettings, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(battery);
        ArgumentNullException.ThrowIfNull(previousSettings);
        ArgumentNullException.ThrowIfNull(settings);

        if (!SettingsValidator.Validate(settings).IsValid)
        {
            return false;
        }

        int? percent = battery.HasPercent ? battery.Percent!.Value : null;
        bool changed = false;
        if (percent is int currentPercent)
        {
            changed = ReArmRecovered(currentPercent, settings);
            State.LastPercent = currentPercent;
        }

        foreach (var t in settings.Thresholds)
        {
            var previous = previousSettings.Thresholds.First(p => p.Kind == t.Kind);
            bool definitionChanged = previous.Enabled != t.Enabled || previous.Percent != t.Percent;
            if (!definitionChanged)
            {
                continue;
            }

            // A fired flag belongs to the old threshold definition. Clear it before deciding
            // whether the new definition is already passed at the current battery percentage.
            if (State.Fired.Remove(t.Kind))
            {
                changed = true;
            }

            if (percent is int value && t.Enabled && value <= t.Percent && State.Fired.Add(t.Kind))
            {
                changed = true;
            }
        }

        return changed;
    }

    private bool ReArmRecovered(int percent, AppSettings settings)
    {
        bool changed = false;
        foreach (var t in settings.Thresholds)
        {
            if (percent >= t.Percent + ResetMargin && State.Fired.Remove(t.Kind))
            {
                changed = true;
            }
        }

        return changed;
    }
}
