using BatteryRescueAlert.Core.Battery;
using BatteryRescueAlert.Core.Diagnostics;
using BatteryRescueAlert.Core.Notifications;
using BatteryRescueAlert.Core.Settings;

namespace BatteryRescueAlert.Core.Coordination;

public sealed record LastAlertInfo(DateTimeOffset At, AlertMessage Message, bool Delivered);

/// <summary>Everything the UI/tray needs to render. Immutable; <see cref="Settings"/> is a private copy.</summary>
public sealed record CoordinatorSnapshot(
    BatteryState Battery,
    AppSettings Settings,
    bool Paused,
    SuppressionReason Reason,
    LastAlertInfo? LastAlert,
    string? NotificationProblem);

/// <summary>
/// Glue between Windows battery events and the UI that contains no Windows or WPF types:
/// evaluates the threshold engine, delivers notifications (respecting settings, de-duplication and failures),
/// persists alert state and publishes snapshots. Must be called from a single thread (the UI thread).
/// </summary>
public sealed class AlertCoordinator
{
    private readonly ThresholdEngine _engine;
    private readonly INotificationService _notifier;
    private readonly NotificationDeduper _deduper;
    private readonly Action<AlertCycleState> _persistState;
    private readonly ILog _log;
    private readonly Func<DateTimeOffset> _clock;

    private AppSettings _settings;
    private BatteryState _battery = BatteryState.Unavailable;
    private SuppressionReason _reason = SuppressionReason.NoBatteryData;
    private LastAlertInfo? _lastAlert;
    private string? _notificationProblem;
    private bool _paused;

    public AlertCoordinator(
        AppSettings settings,
        ThresholdEngine engine,
        INotificationService notifier,
        Action<AlertCycleState> persistState,
        ILog? log = null,
        NotificationDeduper? deduper = null,
        Func<DateTimeOffset>? clock = null)
    {
        _settings = settings.Clone();
        _engine = engine;
        _notifier = notifier;
        _persistState = persistState;
        _log = log ?? NullLog.Instance;
        _deduper = deduper ?? new NotificationDeduper();
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public event Action<CoordinatorSnapshot>? Updated;

    public bool Paused => _paused;

    public CoordinatorSnapshot Snapshot() =>
        new(_battery, _settings.Clone(), _paused, _reason, _lastAlert, _notificationProblem);

    /// <summary>Feed a new reading from Windows (startup, power event, resume or safety refresh).</summary>
    public void OnBatteryState(BatteryState state)
    {
        if (!Equals(_battery, state))
        {
            _log.Info(state.HasPercent
                ? $"Battery status: {state.Percent}% source={state.Source} charging={state.IsCharging}"
                : $"Battery status unavailable ({state.Availability})");
        }

        _battery = state;
        Evaluate();
    }

    public void SetPaused(bool paused)
    {
        if (_paused == paused)
        {
            return;
        }

        _paused = paused;
        _log.Info(paused ? "Alerts paused" : "Alerts resumed");
        Evaluate();
    }

    /// <summary>Applies already-persisted settings. Returns the validation result; invalid settings are rejected.</summary>
    public ValidationResult ApplySettings(AppSettings next)
    {
        var validation = SettingsValidator.Validate(next);
        if (!validation.IsValid)
        {
            return validation;
        }

        AppSettings previousSettings = _settings;
        bool thresholdsChanged = previousSettings.ThresholdsDiffer(next);
        _settings = next.Clone();

        if (thresholdsChanged && _engine.OnSettingsChanged(_battery, previousSettings, _settings))
        {
            Persist();
        }

        _log.Info("Settings applied");
        Evaluate();
        return validation;
    }

    /// <summary>Shows the sample notification. Respects the "Show notifications" setting.</summary>
    public NotificationResult SendTest()
    {
        if (!_settings.NotificationsEnabled)
        {
            return NotificationResult.Failed("Notifications are turned off in Settings. Turn them on to receive alerts.");
        }

        _log.Info("Test notification requested");
        var result = Safe(AlertMessages.Test);
        _notificationProblem = result.Requested ? null : result.Error;
        if (!result.Requested)
        {
            _log.Warn("Test notification failed");
        }

        Publish();
        return result;
    }

    private void Evaluate()
    {
        var result = _engine.Evaluate(_battery, _settings, _paused);
        _reason = result.Reason;

        if (result.StateChanged)
        {
            Persist();
        }

        if (result.Alert is { } alert)
        {
            Deliver(alert);
        }

        Publish();
    }

    private void Deliver(BatteryAlert alert)
    {
        var message = AlertMessages.For(alert);
        var now = _clock();
        _log.Info($"Threshold crossed: {alert.Kind} (reported {alert.BatteryPercent}%)");

        if (!_settings.NotificationsEnabled)
        {
            _lastAlert = new LastAlertInfo(now, message, Delivered: false);
            _log.Info("Alert not shown: notifications are turned off");
            return;
        }

        if (!_deduper.TryAcquire(alert.Kind.ToString(), now))
        {
            _log.Info("Duplicate notification suppressed");
            return;
        }

        _log.Info("Notification requested");
        var result = Safe(message);
        _lastAlert = new LastAlertInfo(now, message, result.Requested);
        _notificationProblem = result.Requested ? null : result.Error;
        if (!result.Requested)
        {
            _log.Warn("Notification failure");
        }
    }

    private NotificationResult Safe(AlertMessage message)
    {
        try
        {
            return _notifier.Show(message);
        }
        catch (Exception ex)
        {
            _log.Error("Notification threw", ex);
            return NotificationResult.Failed("Windows could not show the notification.");
        }
    }

    private void Persist()
    {
        try
        {
            _persistState(_engine.State);
        }
        catch (Exception ex)
        {
            _log.Error("Persisting alert state failed", ex);
        }
    }

    private void Publish()
    {
        try
        {
            Updated?.Invoke(Snapshot());
        }
        catch (Exception ex)
        {
            _log.Error("Update handler threw", ex);
        }
    }
}
