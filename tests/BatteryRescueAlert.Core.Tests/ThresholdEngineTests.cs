using BatteryRescueAlert.Core.Battery;
using BatteryRescueAlert.Core.Settings;
using Xunit;

namespace BatteryRescueAlert.Core.Tests;

public class ThresholdEngineTests
{
    private static BatteryState OnBattery(int percent) =>
        new(BatteryAvailability.Available, percent, PowerSource.Battery, false, false);

    private static BatteryState Plugged(int percent, bool charging = true) =>
        new(BatteryAvailability.Available, percent, PowerSource.Ac, charging, percent == 100 && !charging);

    private static AppSettings Defaults() => AppSettings.CreateDefault(); // 30 / 25 / 20

    private static ThresholdKind? Kind(EvaluationResult r) => r.Alert?.Kind;

    // ---- Spec examples (section 31) -------------------------------------------------------------

    [Fact]
    public void Battery31_Threshold30_NoAlert()
    {
        var r = new ThresholdEngine().Evaluate(OnBattery(31), Defaults());
        Assert.Null(r.Alert);
    }

    [Fact]
    public void Battery30_Threshold30_Alert()
    {
        var r = new ThresholdEngine().Evaluate(OnBattery(30), Defaults());
        Assert.Equal(ThresholdKind.Warning1, Kind(r));
        Assert.Equal(30, r.Alert!.BatteryPercent);
    }

    [Fact]
    public void Battery29_Threshold30_Alert()
    {
        var r = new ThresholdEngine().Evaluate(OnBattery(29), Defaults());
        Assert.Equal(ThresholdKind.Warning1, Kind(r));
    }

    [Fact]
    public void Previous31_Current29_Threshold30_Alert()
    {
        var engine = new ThresholdEngine();
        Assert.Null(engine.Evaluate(OnBattery(31), Defaults()).Alert);
        var r = engine.Evaluate(OnBattery(29), Defaults());
        Assert.Equal(ThresholdKind.Warning1, Kind(r));
        Assert.Equal(29, r.Alert!.BatteryPercent);
    }

    [Fact]
    public void Previous29_Current28_Threshold30_NoDuplicateAlert()
    {
        var engine = new ThresholdEngine();
        Assert.NotNull(engine.Evaluate(OnBattery(29), Defaults()).Alert);
        Assert.Null(engine.Evaluate(OnBattery(28), Defaults()).Alert);
        Assert.Null(engine.Evaluate(OnBattery(28), Defaults()).Alert);
    }

    // ---- Discharge cycle (section 12) -----------------------------------------------------------

    [Fact]
    public void FullDischargeSequence_FiresEachThresholdExactlyOnce_InOrder()
    {
        var engine = new ThresholdEngine();
        var fired = new List<ThresholdKind>();
        foreach (int p in new[] { 100, 80, 60, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 10, 5 })
        {
            if (engine.Evaluate(OnBattery(p), Defaults()).Alert is { } a)
            {
                fired.Add(a.Kind);
            }
        }

        Assert.Equal(new[] { ThresholdKind.Warning1, ThresholdKind.Warning2, ThresholdKind.Critical }, fired);
    }

    [Fact]
    public void MultipleThresholdsCrossedAtOnce_ProducesSingleMostSevereAlert()
    {
        var engine = new ThresholdEngine();
        engine.Evaluate(OnBattery(40), Defaults());

        var r = engine.Evaluate(OnBattery(18), Defaults());
        Assert.Equal(ThresholdKind.Critical, Kind(r));
        Assert.True(r.StateChanged);
        Assert.Equal(3, engine.State.Fired.Count);

        Assert.Null(engine.Evaluate(OnBattery(17), Defaults()).Alert);
    }

    [Fact]
    public void JumpPastTwoThresholds_AlertsForLowerOne()
    {
        var engine = new ThresholdEngine();
        engine.Evaluate(OnBattery(35), Defaults());
        var r = engine.Evaluate(OnBattery(24), Defaults()); // crosses 30 and 25
        Assert.Equal(ThresholdKind.Warning2, Kind(r));
    }

    // ---- Charging / AC (section 13) -------------------------------------------------------------

    [Fact]
    public void WhileCharging_NoAlert_AndReasonIsOnAcPower()
    {
        var engine = new ThresholdEngine();
        var r = engine.Evaluate(Plugged(18), Defaults());
        Assert.Null(r.Alert);
        Assert.Equal(SuppressionReason.OnAcPower, r.Reason);
    }

    [Fact]
    public void ChargingBelowThreshold_DoesNotConsume_SoUnplugWarnsOnce()
    {
        var engine = new ThresholdEngine();
        Assert.Null(engine.Evaluate(Plugged(22), Defaults()).Alert);

        var unplugged = engine.Evaluate(OnBattery(22), Defaults());
        Assert.Equal(ThresholdKind.Warning2, Kind(unplugged)); // 25% crossed, 20% not

        // plug / unplug flapping must not repeat it
        Assert.Null(engine.Evaluate(Plugged(22), Defaults()).Alert);
        Assert.Null(engine.Evaluate(OnBattery(22), Defaults()).Alert);
    }

    [Fact]
    public void ACConnected_ThenDisconnected_AtHighBattery_NoAlert()
    {
        var engine = new ThresholdEngine();
        Assert.Null(engine.Evaluate(Plugged(80), Defaults()).Alert);
        Assert.Null(engine.Evaluate(OnBattery(80), Defaults()).Alert);
    }

    [Fact]
    public void RechargeAboveResetMargin_ReArmsAllThresholds()
    {
        var engine = new ThresholdEngine();
        foreach (int p in new[] { 31, 29, 24, 19 })
        {
            engine.Evaluate(OnBattery(p), Defaults());
        }

        Assert.Equal(3, engine.State.Fired.Count);

        var charging = engine.Evaluate(Plugged(40), Defaults());
        Assert.True(charging.StateChanged);
        Assert.Empty(engine.State.Fired); // 40 >= 30 + 5

        engine.Evaluate(OnBattery(40), Defaults());
        Assert.Equal(ThresholdKind.Warning1, Kind(engine.Evaluate(OnBattery(29), Defaults())));
    }

    [Fact]
    public void SmallRecoveryWithinHysteresis_DoesNotReArm()
    {
        var engine = new ThresholdEngine();
        engine.Evaluate(OnBattery(19), Defaults()); // everything fired
        engine.Evaluate(Plugged(24), Defaults());   // 24 < 20 + 5, so nothing re-arms yet
        engine.Evaluate(OnBattery(24), Defaults());

        Assert.Equal(3, engine.State.Fired.Count);
        Assert.Null(engine.Evaluate(OnBattery(22), Defaults()).Alert);
        Assert.Null(engine.Evaluate(OnBattery(19), Defaults()).Alert); // still the same cycle: no spam

        engine.Evaluate(Plugged(25), Defaults());   // 25 >= 20 + 5: Critical re-arms (30 and 25 stay fired)
        Assert.DoesNotContain(ThresholdKind.Critical, engine.State.Fired);
        Assert.Equal(ThresholdKind.Critical, Kind(engine.Evaluate(OnBattery(19), Defaults())));
    }

    [Fact]
    public void FullyCharged_NoAlert_AndResets()
    {
        var engine = new ThresholdEngine();
        engine.Evaluate(OnBattery(19), Defaults());
        var r = engine.Evaluate(Plugged(100, charging: false), Defaults());
        Assert.Null(r.Alert);
        Assert.Empty(engine.State.Fired);
    }

    // ---- Startup (section 11) -------------------------------------------------------------------

    [Fact]
    public void StartupBelowThreshold_WithNoSavedState_AlertsOnceForMostSevere()
    {
        var engine = new ThresholdEngine();
        var first = engine.Evaluate(OnBattery(22), Defaults());
        Assert.Equal(ThresholdKind.Warning2, Kind(first));
        Assert.Null(engine.Evaluate(OnBattery(22), Defaults()).Alert);
    }

    [Fact]
    public void StartupBelowThreshold_WithRestoredState_DoesNotRepeatAlert()
    {
        var first = new ThresholdEngine();
        first.Evaluate(OnBattery(22), Defaults());
        var snapshot = first.State.ToSnapshot();

        var afterRestart = new ThresholdEngine(AlertCycleState.FromSnapshot(snapshot));
        Assert.Null(afterRestart.Evaluate(OnBattery(21), Defaults()).Alert);
    }

    [Fact]
    public void RestoredState_IsClearedWhenBatteryHasRecovered()
    {
        var state = new AlertCycleState();
        state.Fired.Add(ThresholdKind.Critical);
        var engine = new ThresholdEngine(state);

        var r = engine.Evaluate(OnBattery(90), Defaults());
        Assert.True(r.StateChanged);
        Assert.Empty(engine.State.Fired);
    }

    [Fact]
    public void Snapshot_IgnoresUnknownEnumValues()
    {
        var snapshot = new AlertCycleSnapshot { Fired = new List<ThresholdKind> { (ThresholdKind)99, ThresholdKind.Critical } };
        var state = AlertCycleState.FromSnapshot(snapshot);
        Assert.Single(state.Fired);
        Assert.Contains(ThresholdKind.Critical, state.Fired);
    }

    [Fact]
    public void LegacyVersion1Snapshot_DiscardsUnqualifiedFiredFlags()
    {
        var snapshot = new AlertCycleSnapshot
        {
            Version = 1,
            Fired = new List<ThresholdKind> { ThresholdKind.Warning1, ThresholdKind.Warning2 },
        };

        Assert.Empty(AlertCycleState.FromSnapshot(snapshot).Fired);
    }

    // ---- Settings interaction -------------------------------------------------------------------

    [Fact]
    public void DisabledThreshold_NeverFires()
    {
        var s = Defaults();
        s.Warning1.Enabled = false;
        var engine = new ThresholdEngine();

        Assert.Null(engine.Evaluate(OnBattery(29), s).Alert);
        Assert.Equal(ThresholdKind.Warning2, Kind(engine.Evaluate(OnBattery(25), s)));
    }

    [Fact]
    public void ThresholdRaised_WhileBelow_DoesNotAlertImmediately_ButLowerOnesStillWork()
    {
        var engine = new ThresholdEngine();
        engine.Evaluate(OnBattery(35), Defaults());

        var previous = Defaults();
        var edited = previous.Clone();
        edited.Warning1.Percent = 40;
        Assert.True(engine.OnSettingsChanged(OnBattery(35), previous, edited));

        Assert.Null(engine.Evaluate(OnBattery(35), edited).Alert);
        Assert.Equal(ThresholdKind.Warning2, Kind(engine.Evaluate(OnBattery(25), edited)));
    }

    [Fact]
    public void ChangedThreshold_ClearsOldFiredFlag_AndCanFireAtNewBoundary()
    {
        // Warning1 was fired when its configured threshold was 71%. The user changes it to 72%
        // while the battery is at 75%: 75 is below the new 5-point re-arm level (77), so the
        // old implementation incorrectly left the flag set and never warned at 72%.
        var previous = Defaults();
        previous.Warning1.Percent = 71;
        var state = new AlertCycleState();
        state.Fired.Add(ThresholdKind.Warning1);
        var engine = new ThresholdEngine(state);

        var edited = previous.Clone();
        edited.Warning1.Percent = 72;
        engine.OnSettingsChanged(OnBattery(75), previous, edited);

        Assert.DoesNotContain(ThresholdKind.Warning1, engine.State.Fired);
        Assert.Equal(ThresholdKind.Warning1, Kind(engine.Evaluate(OnBattery(72), edited)));
    }

    [Fact]
    public void ChangedThreshold_AlreadyBelowAtSave_IsConsumedWithoutImmediateAlert()
    {
        var previous = Defaults();
        var edited = previous.Clone();
        edited.Warning1.Percent = 40;
        var engine = new ThresholdEngine();

        Assert.True(engine.OnSettingsChanged(OnBattery(35), previous, edited));
        Assert.Null(engine.Evaluate(OnBattery(35), edited).Alert);
    }

    [Fact]
    public void AlertsDisabled_ProducesNoAlert_AndEnablingLaterAlertsIfStillLow()
    {
        var s = Defaults();
        s.AlertsEnabled = false;
        var engine = new ThresholdEngine();

        var off = engine.Evaluate(OnBattery(22), s);
        Assert.Null(off.Alert);
        Assert.Equal(SuppressionReason.AlertsDisabled, off.Reason);

        s.AlertsEnabled = true;
        Assert.Equal(ThresholdKind.Warning2, Kind(engine.Evaluate(OnBattery(22), s)));
    }

    [Fact]
    public void Paused_ProducesNoAlert_AndResumeWarnsIfStillLow()
    {
        var engine = new ThresholdEngine();
        var paused = engine.Evaluate(OnBattery(24), Defaults(), alertsPaused: true);
        Assert.Null(paused.Alert);
        Assert.Equal(SuppressionReason.Paused, paused.Reason);

        Assert.Equal(ThresholdKind.Warning2, Kind(engine.Evaluate(OnBattery(24), Defaults())));
    }

    [Fact]
    public void InvalidSettings_ProduceNoAlert_AndDoNotThrow()
    {
        var s = Defaults();
        s.Critical.Percent = 40; // contradicts Warning 2
        var r = new ThresholdEngine().Evaluate(OnBattery(10), s);
        Assert.Null(r.Alert);
        Assert.Equal(SuppressionReason.InvalidSettings, r.Reason);
    }

    // ---- Robustness (section 25) ----------------------------------------------------------------

    [Theory]
    [InlineData(BatteryAvailability.Unknown)]
    [InlineData(BatteryAvailability.NoBattery)]
    public void UnavailableBattery_NeverAlerts_NeverTreatedAsZero(BatteryAvailability availability)
    {
        var engine = new ThresholdEngine();
        var state = new BatteryState(availability, null, PowerSource.Unknown, false, false);

        var r = engine.Evaluate(state, Defaults());
        Assert.Null(r.Alert);
        Assert.Equal(SuppressionReason.NoBatteryData, r.Reason);
        Assert.Empty(engine.State.Fired);
    }

    [Fact]
    public void TemporaryDropout_DoesNotLoseState()
    {
        var engine = new ThresholdEngine();
        Assert.NotNull(engine.Evaluate(OnBattery(29), Defaults()).Alert);
        engine.Evaluate(BatteryState.Unavailable, Defaults());
        Assert.Null(engine.Evaluate(OnBattery(28), Defaults()).Alert);
    }

    [Fact]
    public void UnknownPowerSource_OnLowBattery_StillAlerts()
    {
        var state = new BatteryState(BatteryAvailability.Available, 19, PowerSource.Unknown, false, false);
        Assert.Equal(ThresholdKind.Critical, Kind(new ThresholdEngine().Evaluate(state, Defaults())));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var engine = new ThresholdEngine();
        Assert.Throws<ArgumentNullException>(() => engine.Evaluate(null!, Defaults()));
        Assert.Throws<ArgumentNullException>(() => engine.Evaluate(OnBattery(50), null!));
    }
}
