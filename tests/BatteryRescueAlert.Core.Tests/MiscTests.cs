using BatteryRescueAlert.Core.Battery;
using BatteryRescueAlert.Core.Diagnostics;
using BatteryRescueAlert.Core.Notifications;
using BatteryRescueAlert.Core.Settings;
using Xunit;

namespace BatteryRescueAlert.Core.Tests;

public class PowerStatusMapperTests
{
    [Fact]
    public void OnBattery_Discharging()
    {
        var s = PowerStatusMapper.Map(acLineStatus: 0, batteryFlag: 1, batteryLifePercent: 72);
        Assert.Equal(BatteryAvailability.Available, s.Availability);
        Assert.Equal(72, s.Percent);
        Assert.Equal(PowerSource.Battery, s.Source);
        Assert.False(s.IsCharging);
    }

    [Fact]
    public void OnAc_Charging()
    {
        var s = PowerStatusMapper.Map(1, 8, 18);
        Assert.Equal(PowerSource.Ac, s.Source);
        Assert.True(s.IsCharging);
        Assert.False(s.IsFullyCharged);
    }

    [Fact]
    public void OnAc_At100_NotCharging_IsFullyCharged()
    {
        var s = PowerStatusMapper.Map(1, 1, 100);
        Assert.True(s.IsFullyCharged);
    }

    [Fact]
    public void OnAc_NotCharging_Below100_IsPluggedInButNotFull()
    {
        var s = PowerStatusMapper.Map(1, 1, 80);
        Assert.False(s.IsCharging);
        Assert.False(s.IsFullyCharged);
        Assert.True(s.OnAc);
    }

    [Fact]
    public void NoBattery_Flag128()
    {
        var s = PowerStatusMapper.Map(1, 128, 255);
        Assert.Equal(BatteryAvailability.NoBattery, s.Availability);
        Assert.Null(s.Percent);
        Assert.False(s.HasPercent);
    }

    [Fact]
    public void UnknownPercent255_IsUnknown_NotZero()
    {
        var s = PowerStatusMapper.Map(0, 1, 255);
        Assert.Equal(BatteryAvailability.Unknown, s.Availability);
        Assert.Null(s.Percent);
    }

    [Fact]
    public void UnknownFlag255_WithValidPercent_IsStillUsable()
    {
        var s = PowerStatusMapper.Map(0, 255, 40);
        Assert.True(s.HasPercent);
        Assert.False(s.IsCharging);
    }

    [Fact]
    public void UnknownFlag255_AndUnknownPercent_IsUnknown()
    {
        var s = PowerStatusMapper.Map(255, 255, 255);
        Assert.Equal(BatteryAvailability.Unknown, s.Availability);
        Assert.Equal(PowerSource.Unknown, s.Source);
    }

    [Fact]
    public void CombinedFlags_LowAndCharging_AreHandled()
    {
        var s = PowerStatusMapper.Map(1, 2 | 8, 12);
        Assert.True(s.IsCharging);
        Assert.Equal(12, s.Percent);
    }
}

public class AlertMessageTests
{
    [Theory]
    [InlineData(ThresholdKind.Warning1, 30, "Battery is at 30%. Save your work and connect your charger soon.", "Battery Rescue Alert")]
    [InlineData(ThresholdKind.Warning2, 25, "Battery is at 25%. Your battery is getting low. Plug in now.", "Battery Rescue Alert")]
    [InlineData(ThresholdKind.Critical, 20, "Battery is at 20%. Connect your charger immediately and save important work.", "Battery Rescue Alert — Critical")]
    public void Messages_MatchSpecification(ThresholdKind kind, int percent, string body, string title)
    {
        var m = AlertMessages.For(new BatteryAlert(kind, percent, percent));
        Assert.Equal(body, m.Body);
        Assert.Equal(title, m.Title);
    }

    [Fact]
    public void Message_UsesActualReportedPercentage()
    {
        var m = AlertMessages.For(new BatteryAlert(ThresholdKind.Warning1, 30, 28));
        Assert.Contains("28%", m.Body);
    }

    [Fact]
    public void Messages_AvoidAlarmistClaims()
    {
        foreach (ThresholdKind k in Enum.GetValues<ThresholdKind>())
        {
            string body = AlertMessages.For(new BatteryAlert(k, 20, 20)).Body.ToLowerInvariant();
            Assert.DoesNotContain("shut down", body);
            Assert.DoesNotContain("shutdown", body);
            Assert.DoesNotContain("lose", body);
        }
    }

    [Fact]
    public void TestMessage_MatchesSpecification()
    {
        Assert.Equal("Test notification — your battery warning is working correctly.", AlertMessages.Test.Body);
    }
}

public class NotificationDeduperTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SameKeyWithinWindow_IsBlocked()
    {
        var d = new NotificationDeduper(TimeSpan.FromSeconds(60));
        Assert.True(d.TryAcquire("w1", T0));
        Assert.False(d.TryAcquire("w1", T0.AddSeconds(10)));
    }

    [Fact]
    public void SameKeyAfterWindow_IsAllowed()
    {
        var d = new NotificationDeduper(TimeSpan.FromSeconds(60));
        Assert.True(d.TryAcquire("w1", T0));
        Assert.True(d.TryAcquire("w1", T0.AddSeconds(61)));
    }

    [Fact]
    public void DifferentKeys_AreIndependent()
    {
        var d = new NotificationDeduper();
        Assert.True(d.TryAcquire("w1", T0));
        Assert.True(d.TryAcquire("critical", T0));
    }

    [Fact]
    public void ClockMovingBackwards_DoesNotBlockForever()
    {
        var d = new NotificationDeduper(TimeSpan.FromSeconds(60));
        Assert.True(d.TryAcquire("w1", T0));
        Assert.True(d.TryAcquire("w1", T0.AddHours(-1)));
    }
}

public sealed class RollingFileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bra-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Disabled_WritesNothing()
    {
        var log = new RollingFileLog(Path.Combine(_dir, "a.log"), () => false);
        log.Info("hello");
        Assert.False(File.Exists(Path.Combine(_dir, "a.log")));
    }

    [Fact]
    public void Enabled_WritesLine()
    {
        var path = Path.Combine(_dir, "a.log");
        new RollingFileLog(path, () => true).Info("Application started");
        Assert.Contains("Application started", File.ReadAllText(path));
    }

    [Fact]
    public void ExceedingMaxSize_RotatesToSingleBackup()
    {
        var path = Path.Combine(_dir, "a.log");
        var log = new RollingFileLog(path, () => true, maxBytes: 500);
        for (int i = 0; i < 200; i++)
        {
            log.Info("Battery percentage changed");
        }

        Assert.True(File.Exists(path + ".1"));
        Assert.False(File.Exists(path + ".2"));
        Assert.True(new FileInfo(path).Length < 1000);
    }

    [Fact]
    public void Error_LogsExceptionTypeButNotMessage()
    {
        var path = Path.Combine(_dir, "a.log");
        new RollingFileLog(path, () => true).Error("Failed", new InvalidOperationException("C:\\Users\\someone\\secret.docx"));
        string text = File.ReadAllText(path);
        Assert.Contains("InvalidOperationException", text);
        Assert.DoesNotContain("secret.docx", text);
    }

    [Fact]
    public void UnwritableLocation_NeverThrows()
    {
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "x");
        var log = new RollingFileLog(Path.Combine(blocker, "a.log"), () => true);
        var ex = Record.Exception(() => log.Info("x"));
        Assert.Null(ex);
    }
}
