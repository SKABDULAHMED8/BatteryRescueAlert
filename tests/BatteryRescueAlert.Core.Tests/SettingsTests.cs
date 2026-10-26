using System.Text.Json;
using BatteryRescueAlert.Core.Battery;
using BatteryRescueAlert.Core.Diagnostics;
using BatteryRescueAlert.Core.Settings;
using Xunit;

namespace BatteryRescueAlert.Core.Tests;

public class SettingsValidatorTests
{
    [Fact]
    public void Defaults_AreValid_30_25_20()
    {
        var s = AppSettings.CreateDefault();
        Assert.True(SettingsValidator.Validate(s).IsValid);
        Assert.Equal(new[] { 30, 25, 20 }, s.Thresholds.Select(t => t.Percent).ToArray());
        Assert.True(s.Thresholds.All(t => t.Enabled));
        Assert.Equal(AppSettings.DefaultNotificationSoundId, s.NotificationSoundId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(-5)]
    [InlineData(255)]
    public void OutOfRangePercent_IsRejected(int value)
    {
        var s = AppSettings.CreateDefault();
        s.Warning1.Percent = value;
        var r = SettingsValidator.Validate(s);
        Assert.False(r.IsValid);
        Assert.NotNull(r.ErrorFor(SettingsValidator.FieldWarning1));
    }

    [Fact]
    public void CriticalNotBelowWarning2_IsRejected()
    {
        var s = AppSettings.CreateDefault();
        s.Critical.Percent = 25;
        Assert.NotNull(SettingsValidator.Validate(s).ErrorFor(SettingsValidator.FieldCritical));
    }

    [Fact]
    public void Warning2NotBelowWarning1_IsRejected()
    {
        var s = AppSettings.CreateDefault();
        s.Warning2.Percent = 35;
        Assert.NotNull(SettingsValidator.Validate(s).ErrorFor(SettingsValidator.FieldWarning2));
    }

    [Fact]
    public void DuplicatePercentages_AreRejected()
    {
        var s = AppSettings.CreateDefault();
        s.Warning2.Percent = 30;
        Assert.False(SettingsValidator.Validate(s).IsValid);
    }

    [Fact]
    public void Null_IsRejected_NotThrown()
    {
        Assert.False(SettingsValidator.Validate(null).IsValid);
        var s = AppSettings.CreateDefault();
        s.Warning2 = null!;
        Assert.False(SettingsValidator.Validate(s).IsValid);
    }

    [Fact]
    public void ThresholdsDiffer_DetectsEditsButNotUnrelatedFlags()
    {
        var a = AppSettings.CreateDefault();
        var b = a.Clone();
        b.NotificationsEnabled = false;
        Assert.False(a.ThresholdsDiffer(b));
        b.Critical.Enabled = false;
        Assert.True(a.ThresholdsDiffer(b));
    }
}

public sealed class SettingsRepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bra-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private SettingsRepository Repo() => new(Path.Combine(_dir, "settings.json"));

    [Fact]
    public void MissingFile_YieldsDefaults_WithoutWarning()
    {
        var s = Repo().LoadOrDefault(out string? warning);
        Assert.Null(warning);
        Assert.Equal(30, s.Warning1.Percent);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        var s = AppSettings.CreateDefault();
        s.AlertsEnabled = false;
        s.NotificationsEnabled = false;
        s.NotificationSoundId = "custom:tone-test.mp3";
        s.DiagnosticLogging = true;
        s.Warning1.Percent = 40;
        s.Warning2.Enabled = false;
        s.Critical.Percent = 10;

        Assert.True(Repo().TrySave(s, out string? error), error);
        var loaded = Repo().LoadOrDefault(out string? warning);

        Assert.Null(warning);
        Assert.False(loaded.AlertsEnabled);
        Assert.False(loaded.NotificationsEnabled);
        Assert.Equal("custom:tone-test.mp3", loaded.NotificationSoundId);
        Assert.True(loaded.DiagnosticLogging);
        Assert.Equal(40, loaded.Warning1.Percent);
        Assert.False(loaded.Warning2.Enabled);
        Assert.Equal(10, loaded.Critical.Percent);
    }

    [Fact]
    public void InvalidSettings_AreNeverWritten()
    {
        var s = AppSettings.CreateDefault();
        s.Critical.Percent = 90;
        Assert.False(Repo().TrySave(s, out string? error));
        Assert.NotNull(error);
        Assert.False(File.Exists(Path.Combine(_dir, "settings.json")));
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults_WithWarning()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ this is not json");

        var s = Repo().LoadOrDefault(out string? warning);
        Assert.NotNull(warning);
        Assert.Equal(20, s.Critical.Percent);
    }

    [Fact]
    public void ContradictoryThresholdsOnDisk_AreReplacedByDefaults_ButOtherFlagsKept()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            """{ "AlertsEnabled": false, "Warning1": {"Percent": 10}, "Warning2": {"Percent": 50}, "Critical": {"Percent": 90} }""");

        var s = Repo().LoadOrDefault(out string? warning);
        Assert.NotNull(warning);
        Assert.False(s.AlertsEnabled);
        Assert.Equal(new[] { 30, 25, 20 }, s.Thresholds.Select(t => t.Percent).ToArray());
    }

    [Fact]
    public void NullThresholdObjects_AreRepaired()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), """{ "Warning1": null, "Critical": null }""");

        var s = Repo().LoadOrDefault(out _);
        Assert.Equal(ThresholdKind.Warning1, s.Warning1.Kind);
        Assert.Equal(ThresholdKind.Critical, s.Critical.Kind);
        Assert.True(SettingsValidator.Validate(s).IsValid);
    }

    [Fact]
    public void AlertState_RoundTrips_AndCorruptFileGivesEmptyState()
    {
        var repo = new AlertStateRepository(Path.Combine(_dir, "state.json"));
        var state = new Core.Battery.AlertCycleState();
        state.Fired.Add(ThresholdKind.Warning2);
        Assert.True(repo.TrySave(state, out _));
        Assert.Contains(ThresholdKind.Warning2, repo.LoadOrNew().Fired);

        File.WriteAllText(Path.Combine(_dir, "state.json"), "garbage");
        Assert.Empty(repo.LoadOrNew().Fired);
    }

    [Fact]
    public void AlertState_Version1IsMigratedAndStaleFlagsAreCleared()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "state.json");
        File.WriteAllText(path, """{ "Version": 1, "Fired": ["Warning1", "Warning2"] }""");

        var repo = new AlertStateRepository(path);
        Assert.Empty(repo.LoadOrNew().Fired);

        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(AlertCycleSnapshot.CurrentVersion, json.RootElement.GetProperty("Version").GetInt32());
    }

    [Fact]
    public void Save_ToUnwritableLocation_ReturnsErrorInsteadOfThrowing()
    {
        // A path whose "directory" is actually a file cannot be created.
        Directory.CreateDirectory(_dir);
        string blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "x");
        var repo = new SettingsRepository(Path.Combine(blocker, "settings.json"));

        Assert.False(repo.TrySave(AppSettings.CreateDefault(), out string? error));
        Assert.NotNull(error);
    }
}
