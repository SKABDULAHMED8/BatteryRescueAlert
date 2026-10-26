using BatteryRescueAlert.Core.Battery;
using BatteryRescueAlert.Core.Coordination;
using BatteryRescueAlert.Core.Notifications;
using BatteryRescueAlert.Core.Settings;
using BatteryRescueAlert.Core.ViewModels;
using Xunit;

namespace BatteryRescueAlert.Core.Tests;

public class MainViewModelTests
{
    private sealed class Harness
    {
        public AppSettings Saved { get; private set; } = AppSettings.CreateDefault();
        public int SaveCalls { get; private set; }
        public bool SaveShouldFail { get; set; }
        public NotificationResult TestResult { get; set; } = NotificationResult.Ok;
        public bool? PausedRequest { get; private set; }
        public int OpenSettingsCalls { get; private set; }
        public MainViewModel Vm { get; }

        public Harness(string? loadWarning = null)
        {
            Vm = new MainViewModel(
                AppSettings.CreateDefault(),
                s =>
                {
                    SaveCalls++;
                    if (SaveShouldFail)
                    {
                        return new SaveOutcome(false, "Your settings could not be saved.");
                    }

                    Saved = s.Clone();
                    return new SaveOutcome(true, "Settings saved.", s);
                },
                () => TestResult,
                p => PausedRequest = p,
                () => OpenSettingsCalls++,
                "1.0.0", @"%APPDATA%\BatteryRescueAlert", loadWarning);
        }
    }

    [Fact]
    public void InitialDraft_MatchesDefaults_AndIsNotDirty()
    {
        var h = new Harness();
        Assert.Equal("30", h.Vm.Warning1Text);
        Assert.Equal("25", h.Vm.Warning2Text);
        Assert.Equal("20", h.Vm.CriticalText);
        Assert.True(h.Vm.AlertsEnabled);
        Assert.False(h.Vm.IsDirty);
        Assert.False(h.Vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void EditingMakesDirty_AndEnablesSave()
    {
        var h = new Harness();
        h.Vm.Warning1Text = "35";
        Assert.True(h.Vm.IsDirty);
        Assert.False(h.Vm.HasErrors);
        Assert.True(h.Vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void ContradictoryValues_ShowErrors_AndBlockSave()
    {
        var h = new Harness();
        h.Vm.CriticalText = "28";
        Assert.True(h.Vm.HasErrors);
        Assert.Equal("Critical must be lower than Warning 2.", h.Vm.CriticalError);
        Assert.False(h.Vm.SaveCommand.CanExecute(null));
        h.Vm.SaveCommand.Execute(null);
        Assert.Equal(0, h.SaveCalls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("2.5")]
    [InlineData("1e1")]
    [InlineData("0")]
    [InlineData("100")]
    public void InvalidText_IsRejected(string text)
    {
        var h = new Harness();
        h.Vm.Warning1Text = text;
        Assert.True(h.Vm.HasWarning1Error);
        Assert.False(h.Vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Save_PersistsDraft_AndClearsDirty()
    {
        var h = new Harness();
        h.Vm.Warning1Text = "40";
        h.Vm.NotificationsEnabled = false;
        h.Vm.SaveCommand.Execute(null);

        Assert.Equal(1, h.SaveCalls);
        Assert.Equal(40, h.Saved.Warning1.Percent);
        Assert.False(h.Saved.NotificationsEnabled);
        Assert.False(h.Vm.IsDirty);
        Assert.Equal("Settings saved.", h.Vm.SaveStatus);
    }

    [Fact]
    public void SaveFailure_KeepsDraftDirty_AndShowsMessage()
    {
        var h = new Harness { SaveShouldFail = true };
        h.Vm.Warning1Text = "40";
        h.Vm.SaveCommand.Execute(null);

        Assert.True(h.Vm.IsDirty);
        Assert.Equal("Your settings could not be saved.", h.Vm.SaveStatus);
    }

    [Fact]
    public void Revert_RestoresAppliedValues()
    {
        var h = new Harness();
        h.Vm.Warning1Text = "99";
        h.Vm.AlertsEnabled = false;
        h.Vm.RevertCommand.Execute(null);

        Assert.Equal("30", h.Vm.Warning1Text);
        Assert.True(h.Vm.AlertsEnabled);
        Assert.False(h.Vm.IsDirty);
        Assert.False(h.Vm.HasErrors);
    }

    [Fact]
    public void EditingBackToOriginal_IsNotDirty()
    {
        var h = new Harness();
        h.Vm.Warning1Text = "35";
        h.Vm.Warning1Text = "30";
        Assert.False(h.Vm.IsDirty);
    }

    [Fact]
    public void SelectedCustomTone_IsMarkedDirtyAndSaved()
    {
        AppSettings saved = AppSettings.CreateDefault();
        var options = new[]
        {
            new NotificationSoundOption(AppSettings.DefaultNotificationSoundId, "Windows notification (default)"),
            new NotificationSoundOption("custom:tone-example.mp3", "Custom: example", IsCustom: true)
        };
        var vm = new MainViewModel(
            AppSettings.CreateDefault(),
            settings =>
            {
                saved = settings.Clone();
                return new SaveOutcome(true, "Settings saved.", settings);
            },
            () => NotificationResult.Ok,
            _ => { },
            () => { },
            "1.0.5",
            @"%APPDATA%\BatteryRescueAlert",
            getSoundOptions: () => options);

        vm.NotificationSoundId = "custom:tone-example.mp3";
        Assert.True(vm.IsDirty);
        Assert.True(vm.IsSelectedCustomTone);
        Assert.True(vm.SaveCommand.CanExecute(null));
        vm.SaveCommand.Execute(null);

        Assert.Equal("custom:tone-example.mp3", saved.NotificationSoundId);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void AddingCustomTone_ReportsBackupAndSaveChangesGuidance()
    {
        var vm = new MainViewModel(
            AppSettings.CreateDefault(),
            settings => new SaveOutcome(true, "Settings saved.", settings),
            () => NotificationResult.Ok,
            _ => { },
            () => { },
            "1.0.5",
            @"%APPDATA%\BatteryRescueAlert",
            getSoundOptions: () => new[]
            {
                new NotificationSoundOption(AppSettings.DefaultNotificationSoundId, "Windows notification (default)")
            },
            addCustomTone: () => new NotificationSoundOption("custom:tone-example.mp3", "Custom: example", IsCustom: true));

        vm.AddCustomToneCommand.Execute(null);

        Assert.True(vm.IsSelectedCustomTone);
        Assert.Contains("Backup copy saved", vm.ToneStatus);
        Assert.Contains("original file", vm.ToneStatus);
        Assert.Contains("Save changes", vm.ToneStatus);
    }

    [Fact]
    public void DeletingLastCustomTone_SelectsWindowsDefault()
    {
        var options = new[]
        {
            new NotificationSoundOption(AppSettings.DefaultNotificationSoundId, "Windows notification (default)"),
            new NotificationSoundOption("custom:tone-example.mp3", "Custom: example", IsCustom: true)
        };
        var vm = new MainViewModel(
            AppSettings.CreateDefault(),
            settings => new SaveOutcome(true, "Settings saved.", settings),
            () => NotificationResult.Ok,
            _ => { },
            () => { },
            "1.0.5",
            @"%APPDATA%\BatteryRescueAlert",
            getSoundOptions: () => options,
            deleteCustomTone: _ => true);

        vm.NotificationSoundId = "custom:tone-example.mp3";
        vm.DeleteCustomToneCommand.Execute(null);

        Assert.Equal(AppSettings.DefaultNotificationSoundId, vm.NotificationSoundId);
        Assert.False(vm.IsSelectedCustomTone);
        Assert.Contains("No custom tones remain", vm.ToneStatus);
        Assert.DoesNotContain(vm.NotificationSoundOptions, option => option.IsCustom);
    }

    [Fact]
    public void TestCommand_ReportsSuccessAndFailure()
    {
        var h = new Harness();
        h.Vm.TestCommand.Execute(null);
        Assert.Equal(MainViewModel.TestSentMessage, h.Vm.TestStatus);

        h.TestResult = NotificationResult.Failed("Notifications are turned off in Settings.");
        h.Vm.TestCommand.Execute(null);
        Assert.Equal("Notifications are turned off in Settings.", h.Vm.TestStatus);
        Assert.True(h.Vm.HasTestStatus);
    }

    [Fact]
    public void PauseCommand_RequestsOppositeOfCurrentState()
    {
        var h = new Harness();
        h.Vm.TogglePauseCommand.Execute(null);
        Assert.Equal(true, h.PausedRequest);

        h.Vm.Apply(Snapshot(paused: true));
        Assert.Equal("Resume alerts", h.Vm.PauseButtonText);
        h.Vm.TogglePauseCommand.Execute(null);
        Assert.Equal(false, h.PausedRequest);
    }

    [Fact]
    public void Apply_UpdatesDashboard_WithoutTouchingDraft()
    {
        var h = new Harness();
        h.Vm.Warning1Text = "35"; // user is mid-edit

        h.Vm.Apply(Snapshot(percent: 24));

        Assert.Equal("24%", h.Vm.PercentText);
        Assert.Equal(24, h.Vm.PercentValue);
        Assert.Equal("Battery is low", h.Vm.Headline);
        Assert.Equal(BatteryLevel.Low, h.Vm.Level);
        Assert.Equal(3, h.Vm.ThresholdRows.Count);
        Assert.Equal("30%", h.Vm.ThresholdRows[0].PercentText);
        Assert.Equal("35", h.Vm.Warning1Text);
        Assert.True(h.Vm.IsDirty);
    }

    [Fact]
    public void LoadWarning_IsShownAsNotice()
    {
        var h = new Harness("Your saved settings could not be read, so defaults are being used.");
        h.Vm.Apply(Snapshot());
        Assert.True(h.Vm.HasNotices);
        Assert.Contains("defaults are being used", h.Vm.Notices[0]);
    }

    [Fact]
    public void Apply_ShowsLastAlert()
    {
        var h = new Harness();
        var alert = new LastAlertInfo(DateTimeOffset.Now, AlertMessages.Test, true);
        h.Vm.Apply(Snapshot() with { LastAlert = alert });
        Assert.True(h.Vm.HasLastAlert);
        Assert.Contains("Test notification", h.Vm.LastAlertText);
    }

    [Fact]
    public void PropertyChanged_IsRaised()
    {
        var h = new Harness();
        var changed = new List<string?>();
        h.Vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        h.Vm.Apply(Snapshot(percent: 10));
        Assert.Contains("PercentText", changed);
        Assert.Contains("Headline", changed);
    }

    private static CoordinatorSnapshot Snapshot(int percent = 72, bool paused = false) =>
        new(new BatteryState(BatteryAvailability.Available, percent, PowerSource.Battery, false, false),
            AppSettings.CreateDefault(), paused, SuppressionReason.None, null, null);
}
