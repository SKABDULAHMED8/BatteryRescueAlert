using BatteryRescueAlert.Core.Battery;
using BatteryRescueAlert.Core.Settings;

namespace BatteryRescueAlert.Core.Coordination;

public enum BatteryLevel
{
    Unknown,
    Good,
    Charging,
    Low,
    Critical,
}

/// <summary>
/// The three questions every screen must answer at a glance:
/// how much battery (<see cref="PercentText"/>), what is happening (<see cref="Headline"/>),
/// what to do (<see cref="Action"/>).
/// </summary>
public sealed record DashboardModel(
    string PercentText,
    double PercentValue,
    BatteryLevel Level,
    string Headline,
    string Action,
    string PowerStateText,
    string PowerStateGlyph,
    IReadOnlyList<string> Notices,
    string TrayBatteryLine,
    string TrayStatusLine);

public static class DashboardPresenter
{
    public static DashboardModel Present(CoordinatorSnapshot s)
    {
        var b = s.Battery;
        var settings = s.Settings;
        var notices = BuildNotices(s);

        if (b.Availability == BatteryAvailability.NoBattery)
        {
            return new DashboardModel("—", 0, BatteryLevel.Unknown,
                "No battery detected",
                "This device does not report a battery, so there is nothing to monitor.",
                "Battery not available", "—", notices,
                "Battery: not available", "Status: no battery detected");
        }

        if (!b.HasPercent)
        {
            return new DashboardModel("—", 0, BatteryLevel.Unknown,
                "Battery information unavailable",
                "Windows is not reporting battery data right now. Battery Rescue Alert will keep waiting.",
                "Status unknown", "?", notices,
                "Battery: unavailable", "Status: unknown");
        }

        int p = b.Percent!.Value;
        string percentText = $"{p}%";

        if (b.Source == PowerSource.Ac || b.IsCharging)
        {
            if (b.IsFullyCharged)
            {
                return new DashboardModel(percentText, p, BatteryLevel.Charging,
                    "Fully charged", "You can unplug when you are ready.",
                    "Connected to power", "⚡", notices,
                    $"Battery: {percentText}", "Status: fully charged");
            }

            if (b.IsCharging)
            {
                return new DashboardModel(percentText, p, BatteryLevel.Charging,
                    "Charging", "Charging — low-battery alerts are paused while connected to power.",
                    "Charging", "⚡", notices,
                    $"Battery: {percentText}", "Status: charging");
            }

            return new DashboardModel(percentText, p, BatteryLevel.Charging,
                "Plugged in", "Low-battery alerts are paused while connected to power.",
                "Plugged in, not charging", "⚡", notices,
                $"Battery: {percentText}", "Status: plugged in");
        }

        var enabled = settings.Thresholds.Where(t => t.Enabled).ToList();
        string power = b.Source == PowerSource.Battery ? "On battery" : "Battery";
        string trayStatus = b.Source == PowerSource.Battery ? "Status: on battery" : "Status: battery";

        if (enabled.FirstOrDefault(t => t.Kind == ThresholdKind.Critical) is { } critical && p <= critical.Percent)
        {
            return new DashboardModel(percentText, p, BatteryLevel.Critical,
                "Battery is critically low",
                "Connect your charger immediately and save important work.",
                power, "!", notices, $"Battery: {percentText}", trayStatus);
        }

        if (enabled.FirstOrDefault(t => t.Kind == ThresholdKind.Warning2) is { } low && p <= low.Percent)
        {
            return new DashboardModel(percentText, p, BatteryLevel.Low,
                "Battery is low",
                "Plug in your charger and save your work.",
                power, "!", notices, $"Battery: {percentText}", trayStatus);
        }

        if (enabled.FirstOrDefault(t => t.Kind == ThresholdKind.Warning1) is { } early && p <= early.Percent)
        {
            return new DashboardModel(percentText, p, BatteryLevel.Low,
                "Battery is getting low",
                "Save your work and connect your charger soon.",
                power, "!", notices, $"Battery: {percentText}", trayStatus);
        }

        string action = !settings.AlertsEnabled
            ? "No action needed. Battery alerts are turned off."
            : enabled.Count == 0
                ? "No action needed."
                : $"No action needed. You will be warned at {enabled.Max(t => t.Percent)}%.";

        return new DashboardModel(percentText, p, BatteryLevel.Good,
            "Battery is OK", action, power, "●", notices, $"Battery: {percentText}", trayStatus);
    }

    private static List<string> BuildNotices(CoordinatorSnapshot s)
    {
        var notices = new List<string>();

        if (s.NotificationProblem is { } problem)
        {
            notices.Add(problem);
        }

        if (!s.Settings.AlertsEnabled)
        {
            notices.Add("Battery alerts are turned off in Settings.");
        }
        else if (s.Paused)
        {
            notices.Add("Alerts are paused. Choose Resume alerts to turn them back on.");
        }

        if (s.Settings.AlertsEnabled && !s.Settings.NotificationsEnabled)
        {
            notices.Add("Notifications are turned off, so warnings will not be shown.");
        }

        return notices;
    }
}
