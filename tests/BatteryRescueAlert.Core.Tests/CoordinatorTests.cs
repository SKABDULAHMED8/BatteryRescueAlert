using BatteryRescueAlert.Core.Battery;
using BatteryRescueAlert.Core.Coordination;
using BatteryRescueAlert.Core.Notifications;
using BatteryRescueAlert.Core.Settings;
using Xunit;

namespace BatteryRescueAlert.Core.Tests;

public class AlertCoordinatorTests
{
    private sealed class FakeNotifier : INotificationService
    {
        public List<AlertMessage> Shown { get; } = new();
        public bool Fail { get; set; }
        public bool Throw { get; set; }

        public NotificationResult Show(AlertMessage message)
        {
            if (Throw)
            {
                throw new InvalidOperationException("boom");
            }

            if (Fail)
            {
                return NotificationResult.Failed("Windows did not accept the notification.");
            }

            Shown.Add(message);
            return NotificationResult.Ok;
        }
    }

    private sealed class Rig
    {
        public FakeNotifier Notifier { get; } = new();
        public int Persisted { get; private set; }
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        public AlertCoordinator Coordinator { get; }
        public List<CoordinatorSnapshot> Updates { get; } = new();

        public Rig(AppSettings? settings = null, AlertCycleState? state = null)
        {
            Coordinator = new AlertCoordinator(settings ?? AppSettings.CreateDefault(),
                new ThresholdEngine(state), Notifier, _ => Persisted++, clock: () => Now);
            Coordinator.Updated += Updates.Add;
        }
    }

    private static BatteryState Bat(int p) => new(BatteryAvailability.Available, p, PowerSource.Battery, false, false);
    private static BatteryState Ac(int p) => new(BatteryAvailability.Available, p, PowerSource.Ac, true, false);

    [Fact]
    public void CrossingThreshold_ShowsExactlyOneNotification_WithSpecText()
    {
        var rig = new Rig();
        rig.Coordinator.OnBatteryState(Bat(31));
        rig.Coordinator.OnBatteryState(Bat(29));
        rig.Coordinator.OnBatteryState(Bat(28));

        var shown = Assert.Single(rig.Notifier.Shown);
        Assert.Equal("Battery is at 29%. Save your work and connect your charger soon.", shown.Body);
        Assert.True(rig.Persisted >= 1);
    }

    [Fact]
    public void NotificationsDisabled_NothingShown_ButLastAlertIsRecorded()
    {
        var s = AppSettings.CreateDefault();
        s.NotificationsEnabled = false;
        var rig = new Rig(s);

        rig.Coordinator.OnBatteryState(Bat(19));

        Assert.Empty(rig.Notifier.Shown);
        var snap = rig.Coordinator.Snapshot();
        Assert.NotNull(snap.LastAlert);
        Assert.False(snap.LastAlert!.Delivered);
    }

    [Fact]
    public void NotifierFailure_IsReported_AndDoesNotCrash()
    {
        var rig = new Rig();
        rig.Notifier.Fail = true;
        rig.Coordinator.OnBatteryState(Bat(24));

        var snap = rig.Coordinator.Snapshot();
        Assert.Equal("Windows did not accept the notification.", snap.NotificationProblem);
        Assert.False(snap.LastAlert!.Delivered);
    }

    [Fact]
    public void NotifierThrowing_IsContained()
    {
        var rig = new Rig();
        rig.Notifier.Throw = true;
        var ex = Record.Exception(() => rig.Coordinator.OnBatteryState(Bat(24)));
        Assert.Null(ex);
        Assert.NotNull(rig.Coordinator.Snapshot().NotificationProblem);
    }

    [Fact]
    public void UpdatedHandlerThrowing_IsContained()
    {
        var rig = new Rig();
        rig.Coordinator.Updated += _ => throw new InvalidOperationException("ui bug");
        Assert.Null(Record.Exception(() => rig.Coordinator.OnBatteryState(Bat(50))));
    }

    [Fact]
    public void PausedThenResumed_WarnsOnResumeIfStillLow()
    {
        var rig = new Rig();
        rig.Coordinator.SetPaused(true);
        rig.Coordinator.OnBatteryState(Bat(24));
        Assert.Empty(rig.Notifier.Shown);

        rig.Coordinator.SetPaused(false);
        Assert.Single(rig.Notifier.Shown);
    }

    [Fact]
    public void ChargingBelowThreshold_ThenUnplug_WarnsOnce()
    {
        var rig = new Rig();
        rig.Coordinator.OnBatteryState(Ac(22));
        Assert.Empty(rig.Notifier.Shown);

        rig.Coordinator.OnBatteryState(Bat(22));
        rig.Coordinator.OnBatteryState(Ac(22));
        rig.Coordinator.OnBatteryState(Bat(22));
        Assert.Single(rig.Notifier.Shown);
    }

    [Fact]
    public void TestNotification_ShowsSampleMessage()
    {
        var rig = new Rig();
        var r = rig.Coordinator.SendTest();
        Assert.True(r.Requested);
        Assert.Equal(AlertMessages.Test, Assert.Single(rig.Notifier.Shown));
    }

    [Fact]
    public void TestNotification_WhenNotificationsDisabled_ExplainsInsteadOfShowing()
    {
        var s = AppSettings.CreateDefault();
        s.NotificationsEnabled = false;
        var rig = new Rig(s);

        var r = rig.Coordinator.SendTest();
        Assert.False(r.Requested);
        Assert.Contains("turned off", r.Error);
        Assert.Empty(rig.Notifier.Shown);
    }

    [Fact]
    public void TestNotification_Failure_SurfacesProblem()
    {
        var rig = new Rig();
        rig.Notifier.Fail = true;
        Assert.False(rig.Coordinator.SendTest().Requested);
        Assert.NotNull(rig.Coordinator.Snapshot().NotificationProblem);
    }

    [Fact]
    public void ApplySettings_RejectsInvalid_AndKeepsPrevious()
    {
        var rig = new Rig();
        var bad = AppSettings.CreateDefault();
        bad.Critical.Percent = 50;

        var result = rig.Coordinator.ApplySettings(bad);
        Assert.False(result.IsValid);
        Assert.Equal(20, rig.Coordinator.Snapshot().Settings.Critical.Percent);
    }

    [Fact]
    public void ApplySettings_RaisingThreshold_DoesNotCauseSurpriseAlert()
    {
        var rig = new Rig();
        rig.Coordinator.OnBatteryState(Bat(35));

        var edited = AppSettings.CreateDefault();
        edited.Warning1.Percent = 40;
        Assert.True(rig.Coordinator.ApplySettings(edited).IsValid);

        Assert.Empty(rig.Notifier.Shown);
    }

    [Fact]
    public void ApplySettings_ChangedThresholdCanWarnAtItsNewBoundary()
    {
        var previous = AppSettings.CreateDefault();
        previous.Warning1.Percent = 71;
        var state = new AlertCycleState();
        state.Fired.Add(ThresholdKind.Warning1);
        var rig = new Rig(previous, state);
        rig.Coordinator.OnBatteryState(Bat(75));

        var edited = previous.Clone();
        edited.Warning1.Percent = 72;
        Assert.True(rig.Coordinator.ApplySettings(edited).IsValid);
        rig.Coordinator.OnBatteryState(Bat(72));

        Assert.Contains(rig.Notifier.Shown, message => message.Body.Contains("72%"));
    }

    [Fact]
    public void ApplySettings_ReEnablingAlertsWhileLow_Warns()
    {
        var s = AppSettings.CreateDefault();
        s.AlertsEnabled = false;
        var rig = new Rig(s);
        rig.Coordinator.OnBatteryState(Bat(22));
        Assert.Empty(rig.Notifier.Shown);

        var on = s.Clone();
        on.AlertsEnabled = true;
        rig.Coordinator.ApplySettings(on);
        Assert.Single(rig.Notifier.Shown);
    }

    [Fact]
    public void RestoredState_PreventsRepeatAlertAfterRestart()
    {
        var firstRun = new Rig();
        firstRun.Coordinator.OnBatteryState(Bat(22));
        Assert.Single(firstRun.Notifier.Shown);

        var state = new AlertCycleState();
        state.Fired.Add(ThresholdKind.Warning1);
        state.Fired.Add(ThresholdKind.Warning2);
        var secondRun = new Rig(state: state);
        secondRun.Coordinator.OnBatteryState(Bat(22));
        Assert.Empty(secondRun.Notifier.Shown);
    }

    [Fact]
    public void BatteryUnavailable_DoesNotCrash_AndShowsNothing()
    {
        var rig = new Rig();
        rig.Coordinator.OnBatteryState(BatteryState.Unavailable);
        Assert.Empty(rig.Notifier.Shown);
        Assert.Equal(SuppressionReason.NoBatteryData, rig.Coordinator.Snapshot().Reason);
    }

    [Fact]
    public void EveryEvent_PublishesAnUpdate()
    {
        var rig = new Rig();
        rig.Coordinator.OnBatteryState(Bat(80));
        rig.Coordinator.OnBatteryState(Bat(79));
        Assert.Equal(2, rig.Updates.Count);
    }
}

public class DashboardPresenterTests
{
    private static CoordinatorSnapshot Snap(BatteryState b, Action<AppSettings>? tweak = null, bool paused = false, string? problem = null)
    {
        var s = AppSettings.CreateDefault();
        tweak?.Invoke(s);
        return new CoordinatorSnapshot(b, s, paused, SuppressionReason.None, null, problem);
    }

    private static BatteryState Bat(int p) => new(BatteryAvailability.Available, p, PowerSource.Battery, false, false);

    [Fact]
    public void Healthy_AnswersThreeQuestions()
    {
        var m = DashboardPresenter.Present(Snap(Bat(72)));
        Assert.Equal("72%", m.PercentText);
        Assert.Equal("Battery is OK", m.Headline);
        Assert.Equal("No action needed. You will be warned at 30%.", m.Action);
        Assert.Equal("On battery", m.PowerStateText);
        Assert.Equal(BatteryLevel.Good, m.Level);
    }

    [Theory]
    [InlineData(30, "Battery is getting low", BatteryLevel.Low)]
    [InlineData(25, "Battery is low", BatteryLevel.Low)]
    [InlineData(24, "Battery is low", BatteryLevel.Low)]
    [InlineData(20, "Battery is critically low", BatteryLevel.Critical)]
    [InlineData(5, "Battery is critically low", BatteryLevel.Critical)]
    public void LowLevels(int percent, string headline, BatteryLevel level)
    {
        var m = DashboardPresenter.Present(Snap(Bat(percent)));
        Assert.Equal(headline, m.Headline);
        Assert.Equal(level, m.Level);
        Assert.False(string.IsNullOrWhiteSpace(m.Action));
    }

    [Fact]
    public void LowBattery_ActionMatchesSpec()
    {
        Assert.Equal("Plug in your charger and save your work.", DashboardPresenter.Present(Snap(Bat(24))).Action);
    }

    [Fact]
    public void Charging_ShowsPausedMessage()
    {
        var m = DashboardPresenter.Present(Snap(new BatteryState(BatteryAvailability.Available, 18, PowerSource.Ac, true, false)));
        Assert.Equal("Charging", m.Headline);
        Assert.Contains("low-battery alerts are paused while connected to power", m.Action);
        Assert.Equal("Status: charging", m.TrayStatusLine);
        Assert.Equal("Battery: 18%", m.TrayBatteryLine);
    }

    [Fact]
    public void FullyCharged()
    {
        var m = DashboardPresenter.Present(Snap(new BatteryState(BatteryAvailability.Available, 100, PowerSource.Ac, false, true)));
        Assert.Equal("Fully charged", m.Headline);
    }

    [Fact]
    public void NoBattery_ShowsDash_NotZero()
    {
        var m = DashboardPresenter.Present(Snap(new BatteryState(BatteryAvailability.NoBattery, null, PowerSource.Ac, false, false)));
        Assert.Equal("—", m.PercentText);
        Assert.Equal("No battery detected", m.Headline);
        Assert.Equal(BatteryLevel.Unknown, m.Level);
    }

    [Fact]
    public void Unavailable_ShowsDash_NotZero()
    {
        var m = DashboardPresenter.Present(Snap(BatteryState.Unavailable));
        Assert.Equal("—", m.PercentText);
        Assert.Equal("Battery information unavailable", m.Headline);
    }

    [Fact]
    public void Notices_ReflectPauseDisabledAlertsAndProblems()
    {
        Assert.Contains(DashboardPresenter.Present(Snap(Bat(50), paused: true)).Notices, n => n.Contains("paused"));
        Assert.Contains(DashboardPresenter.Present(Snap(Bat(50), s => s.AlertsEnabled = false)).Notices, n => n.Contains("turned off"));
        Assert.Contains(DashboardPresenter.Present(Snap(Bat(50), s => s.NotificationsEnabled = false)).Notices, n => n.Contains("Notifications are turned off"));
        Assert.Contains("Windows did not accept it", DashboardPresenter.Present(Snap(Bat(50), problem: "Windows did not accept it")).Notices);
        Assert.Empty(DashboardPresenter.Present(Snap(Bat(50))).Notices);
    }

    [Fact]
    public void StatusIsNeverColorOnly_EveryStateHasTextAndGlyph()
    {
        foreach (var b in new[]
        {
            Bat(80), Bat(28), Bat(10), BatteryState.Unavailable,
            new BatteryState(BatteryAvailability.Available, 50, PowerSource.Ac, true, false),
        })
        {
            var m = DashboardPresenter.Present(Snap(b));
            Assert.False(string.IsNullOrWhiteSpace(m.PowerStateText));
            Assert.False(string.IsNullOrWhiteSpace(m.PowerStateGlyph));
            Assert.False(string.IsNullOrWhiteSpace(m.Headline));
        }
    }

    [Fact]
    public void DisabledCritical_IsNotReportedAsCritical()
    {
        var m = DashboardPresenter.Present(Snap(Bat(15), s => s.Critical.Enabled = false));
        Assert.Equal("Battery is low", m.Headline);
    }
}
