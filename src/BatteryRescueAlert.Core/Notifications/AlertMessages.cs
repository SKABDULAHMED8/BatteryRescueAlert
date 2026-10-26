using BatteryRescueAlert.Core.Battery;
using BatteryRescueAlert.Core.Settings;

namespace BatteryRescueAlert.Core.Notifications;

public enum AlertSeverity
{
    Info,
    Warning,
    Critical,
}

public sealed record AlertMessage(string Title, string Body, AlertSeverity Severity, bool UseSystemNotificationSound = false);

public sealed record NotificationResult(bool Requested, string? Error)
{
    public static NotificationResult Ok { get; } = new(true, null);
    public static NotificationResult Failed(string error) => new(false, error);
}

/// <summary>Abstraction so the engine/host can be tested without Windows.</summary>
public interface INotificationService
{
    NotificationResult Show(AlertMessage message);
}

/// <summary>Calm, accurate wording. No claim that a shutdown is guaranteed or can be prevented.</summary>
public static class AlertMessages
{
    public const string Title = "Battery Rescue Alert";
    public const string CriticalTitle = "Battery Rescue Alert — Critical";

    public static AlertMessage For(BatteryAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        int p = alert.BatteryPercent;
        return alert.Kind switch
        {
            ThresholdKind.Warning1 => new AlertMessage(Title,
                $"Battery is at {p}%. Save your work and connect your charger soon.", AlertSeverity.Warning),
            ThresholdKind.Warning2 => new AlertMessage(Title,
                $"Battery is at {p}%. Your battery is getting low. Plug in now.", AlertSeverity.Warning),
            _ => new AlertMessage(CriticalTitle,
                $"Battery is at {p}%. Connect your charger immediately and save important work.", AlertSeverity.Critical),
        };
    }

    public static AlertMessage Test { get; } = new(Title,
        "Test notification — your battery warning is working correctly.", AlertSeverity.Info);
}
