using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BatteryRescueAlert.Core.Coordination;
using BatteryRescueAlert.Core.Notifications;
using BatteryRescueAlert.Core.Settings;

namespace BatteryRescueAlert.Core.ViewModels;

public sealed record SaveOutcome(bool Success, string Message, AppSettings? Applied = null);

public sealed record ThresholdRowViewModel(string PercentText, string Name, string StateText);

/// <summary>
/// State for the whole window: dashboard (read-only, driven by <see cref="Apply"/>) and the settings form
/// (editable draft with live validation). Contains no WPF types, so it is unit tested on any OS.
/// </summary>
public sealed class MainViewModel : ObservableBase
{
    public const string TestSentMessage =
        "Test alert opened. You should see the Battery Rescue Alert popup in the lower-right corner. Battery warnings use this same popup.";

    private readonly Func<AppSettings, SaveOutcome> _save;
    private readonly Func<NotificationResult> _test;
    private readonly Action<bool> _setPaused;
    private readonly Action _openNotificationSettings;
    private readonly Func<IReadOnlyList<NotificationSoundOption>> _getSoundOptions;
    private readonly Func<NotificationSoundOption?> _addCustomTone;
    private readonly Func<string, bool> _deleteCustomTone;
    private readonly Func<string, NotificationResult> _previewSound;
    private readonly string? _loadWarning;
    private AppSettings _applied;
    private bool _loading;

    // ---- dashboard ----
    private string _percentText = "—";
    private double _percentValue;
    private BatteryLevel _level = BatteryLevel.Unknown;
    private string _headline = "Checking battery…";
    private string _actionText = string.Empty;
    private string _powerStateText = string.Empty;
    private string _powerStateGlyph = "?";
    private bool _isPaused;
    private string? _lastAlertText;
    private string? _testStatus;

    // ---- settings draft ----
    private bool _alertsEnabled;
    private bool _notificationsEnabled;
    private bool _startWithWindows;
    private bool _diagnosticLogging;
    private bool _warning1Enabled;
    private bool _warning2Enabled;
    private bool _criticalEnabled;
    private string _warning1Text = string.Empty;
    private string _warning2Text = string.Empty;
    private string _criticalText = string.Empty;
    private string? _warning1Error;
    private string? _warning2Error;
    private string? _criticalError;
    private bool _isDirty;
    private string? _saveStatus;
    private string? _startupNote;
    private string _notificationSoundId = AppSettings.DefaultNotificationSoundId;
    private string? _toneStatus;

    public MainViewModel(
        AppSettings applied,
        Func<AppSettings, SaveOutcome> save,
        Func<NotificationResult> test,
        Action<bool> setPaused,
        Action openNotificationSettings,
        string version,
        string dataFolder,
        string? loadWarning = null,
        Func<IReadOnlyList<NotificationSoundOption>>? getSoundOptions = null,
        Func<NotificationSoundOption?>? addCustomTone = null,
        Func<string, bool>? deleteCustomTone = null,
        Func<string, NotificationResult>? previewSound = null)
    {
        _applied = applied.Clone();
        _save = save;
        _test = test;
        _setPaused = setPaused;
        _openNotificationSettings = openNotificationSettings;
        _loadWarning = loadWarning;
        _getSoundOptions = getSoundOptions ?? (() => new[]
        {
            new NotificationSoundOption(AppSettings.DefaultNotificationSoundId, "Windows notification (default)")
        });
        _addCustomTone = addCustomTone ?? (() => null);
        _deleteCustomTone = deleteCustomTone ?? (_ => false);
        _previewSound = previewSound ?? (_ => NotificationResult.Failed("Sound preview is unavailable."));
        foreach (NotificationSoundOption option in _getSoundOptions())
        {
            NotificationSoundOptions.Add(option);
        }
        if (!NotificationSoundOptions.Any(x => x.Id == AppSettings.DefaultNotificationSoundId))
        {
            NotificationSoundOptions.Insert(0, new NotificationSoundOption(
                AppSettings.DefaultNotificationSoundId, "Windows notification (default)"));
        }
        VersionText = version;
        DataFolderText = dataFolder;

        SaveCommand = new RelayCommand(Save, () => IsDirty && !HasErrors);
        RevertCommand = new RelayCommand(Revert, () => IsDirty);
        TestCommand = new RelayCommand(RunTest);
        TogglePauseCommand = new RelayCommand(() => _setPaused(!IsPaused));
        OpenNotificationSettingsCommand = new RelayCommand(_openNotificationSettings);
        AddCustomToneCommand = new RelayCommand(AddCustomTone);
        DeleteCustomToneCommand = new RelayCommand(DeleteSelectedCustomTone, () => IsSelectedCustomTone);
        PreviewSoundCommand = new RelayCommand(PreviewSelectedSound, () => !string.IsNullOrWhiteSpace(NotificationSoundId));

        LoadDraft(_applied);
    }

    // ===== static / about =====
    public string VersionText { get; }
    public string DataFolderText { get; }

    // ===== commands =====
    public RelayCommand SaveCommand { get; }
    public RelayCommand RevertCommand { get; }
    public RelayCommand TestCommand { get; }
    public RelayCommand TogglePauseCommand { get; }
    public RelayCommand OpenNotificationSettingsCommand { get; }
    public RelayCommand AddCustomToneCommand { get; }
    public RelayCommand DeleteCustomToneCommand { get; }
    public RelayCommand PreviewSoundCommand { get; }

    // ===== dashboard =====
    public ObservableCollection<string> Notices { get; } = new();
    public ObservableCollection<ThresholdRowViewModel> ThresholdRows { get; } = new();
    public ObservableCollection<NotificationSoundOption> NotificationSoundOptions { get; } = new();
    public bool HasNotices => Notices.Count > 0;

    public string PercentText { get => _percentText; private set => Set(ref _percentText, value); }
    public double PercentValue { get => _percentValue; private set => Set(ref _percentValue, value); }
    public BatteryLevel Level { get => _level; private set => Set(ref _level, value); }
    public string Headline { get => _headline; private set => Set(ref _headline, value); }
    public string ActionText { get => _actionText; private set => Set(ref _actionText, value); }
    public string PowerStateText { get => _powerStateText; private set => Set(ref _powerStateText, value); }
    public string PowerStateGlyph { get => _powerStateGlyph; private set => Set(ref _powerStateGlyph, value); }
    public string PauseButtonText => _isPaused ? "Resume alerts" : "Pause alerts";

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (Set(ref _isPaused, value))
            {
                Raise(nameof(PauseButtonText));
            }
        }
    }

    public string? LastAlertText
    {
        get => _lastAlertText;
        private set
        {
            if (Set(ref _lastAlertText, value))
            {
                Raise(nameof(HasLastAlert));
            }
        }
    }

    public bool HasLastAlert => !string.IsNullOrEmpty(_lastAlertText);

    public string? TestStatus
    {
        get => _testStatus;
        private set
        {
            if (Set(ref _testStatus, value))
            {
                Raise(nameof(HasTestStatus));
            }
        }
    }

    public bool HasTestStatus => !string.IsNullOrEmpty(_testStatus);

    /// <summary>Pushes a new snapshot into the dashboard. Does not disturb a settings draft being edited.</summary>
    public void Apply(CoordinatorSnapshot snapshot)
    {
        var m = DashboardPresenter.Present(snapshot);

        PercentText = m.PercentText;
        PercentValue = m.PercentValue;
        Level = m.Level;
        Headline = m.Headline;
        ActionText = m.Action;
        PowerStateText = m.PowerStateText;
        PowerStateGlyph = m.PowerStateGlyph;
        IsPaused = snapshot.Paused;

        Notices.Clear();
        if (_loadWarning is not null)
        {
            Notices.Add(_loadWarning);
        }

        foreach (string notice in m.Notices)
        {
            Notices.Add(notice);
        }

        Raise(nameof(HasNotices));

        ThresholdRows.Clear();
        foreach (var t in snapshot.Settings.Thresholds)
        {
            ThresholdRows.Add(new ThresholdRowViewModel(
                $"{t.Percent}%", t.Kind.DisplayName(), t.Enabled ? "Enabled" : "Off"));
        }

        LastAlertText = snapshot.LastAlert is { } a
            ? $"Last alert at {a.At.LocalDateTime.ToString("t", CultureInfo.CurrentCulture)}: {a.Message.Body}"
            : null;
    }

    // ===== settings draft =====
    public bool AlertsEnabled { get => _alertsEnabled; set { if (Set(ref _alertsEnabled, value)) DraftChanged(); } }
    public bool NotificationsEnabled { get => _notificationsEnabled; set { if (Set(ref _notificationsEnabled, value)) DraftChanged(); } }
    public bool StartWithWindows { get => _startWithWindows; set { if (Set(ref _startWithWindows, value)) DraftChanged(); } }
    public bool DiagnosticLogging { get => _diagnosticLogging; set { if (Set(ref _diagnosticLogging, value)) DraftChanged(); } }
    public bool Warning1Enabled { get => _warning1Enabled; set { if (Set(ref _warning1Enabled, value)) DraftChanged(); } }
    public bool Warning2Enabled { get => _warning2Enabled; set { if (Set(ref _warning2Enabled, value)) DraftChanged(); } }
    public bool CriticalEnabled { get => _criticalEnabled; set { if (Set(ref _criticalEnabled, value)) DraftChanged(); } }
    public string Warning1Text { get => _warning1Text; set { if (Set(ref _warning1Text, value)) DraftChanged(); } }
    public string Warning2Text { get => _warning2Text; set { if (Set(ref _warning2Text, value)) DraftChanged(); } }
    public string CriticalText { get => _criticalText; set { if (Set(ref _criticalText, value)) DraftChanged(); } }

    public string? Warning1Error { get => _warning1Error; private set { if (Set(ref _warning1Error, value)) Raise(nameof(HasWarning1Error)); } }
    public string? Warning2Error { get => _warning2Error; private set { if (Set(ref _warning2Error, value)) Raise(nameof(HasWarning2Error)); } }
    public string? CriticalError { get => _criticalError; private set { if (Set(ref _criticalError, value)) Raise(nameof(HasCriticalError)); } }
    public bool HasWarning1Error => _warning1Error is not null;
    public bool HasWarning2Error => _warning2Error is not null;
    public bool HasCriticalError => _criticalError is not null;
    public bool HasErrors => HasWarning1Error || HasWarning2Error || HasCriticalError;

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (Set(ref _isDirty, value))
            {
                Raise(nameof(HasUnsavedChanges));
            }
        }
    }

    public bool HasUnsavedChanges => _isDirty;

    public string? SaveStatus
    {
        get => _saveStatus;
        private set
        {
            if (Set(ref _saveStatus, value))
            {
                Raise(nameof(HasSaveStatus));
            }
        }
    }

    public bool HasSaveStatus => !string.IsNullOrEmpty(_saveStatus);

    public string? StartupNote
    {
        get => _startupNote;
        set
        {
            if (Set(ref _startupNote, value))
            {
                Raise(nameof(HasStartupNote));
            }
        }
    }

    public bool HasStartupNote => !string.IsNullOrEmpty(_startupNote);

    public string NotificationSoundId
    {
        get => _notificationSoundId;
        set
        {
            string next = string.IsNullOrWhiteSpace(value) ||
                !NotificationSoundOptions.Any(option => option.Id == value)
                ? AppSettings.DefaultNotificationSoundId
                : value;
            if (Set(ref _notificationSoundId, next))
            {
                Raise(nameof(IsSelectedCustomTone));
                DeleteCustomToneCommand.RaiseCanExecuteChanged();
                PreviewSoundCommand.RaiseCanExecuteChanged();
                ToneStatus = null;
                DraftChanged();
            }
        }
    }

    public bool IsSelectedCustomTone => NotificationSoundOptions.Any(
        option => option.Id == _notificationSoundId && option.IsCustom);

    public string? ToneStatus
    {
        get => _toneStatus;
        private set
        {
            if (Set(ref _toneStatus, value))
            {
                Raise(nameof(HasToneStatus));
            }
        }
    }

    public bool HasToneStatus => !string.IsNullOrWhiteSpace(_toneStatus);

    /// <summary>Synchronizes the draft baseline when a saved custom tone is deleted.</summary>
    public void OnCustomToneRemoved(string deletedId)
    {
        NotificationSoundOption? removed = NotificationSoundOptions.FirstOrDefault(option => option.Id == deletedId);
        if (removed is not null)
        {
            NotificationSoundOptions.Remove(removed);
        }

        bool customTonesRemain = NotificationSoundOptions.Any(option => option.IsCustom);
        if (!customTonesRemain || _applied.NotificationSoundId == deletedId)
        {
            // Removing the active custom file, or emptying the custom library, resets the persisted
            // baseline to the built-in Windows notification tone.
            _applied.NotificationSoundId = AppSettings.DefaultNotificationSoundId;
        }

        if (!customTonesRemain)
        {
            NotificationSoundId = AppSettings.DefaultNotificationSoundId;
        }
        else if (_notificationSoundId == deletedId)
        {
            // If the removed item was only an unsaved draft, return to the currently applied
            // choice when it still exists. If that was the deleted active tone, the baseline above
            // was already changed to the system default.
            string fallbackId = NotificationSoundOptions.Any(option => option.Id == _applied.NotificationSoundId)
                ? _applied.NotificationSoundId
                : AppSettings.DefaultNotificationSoundId;
            NotificationSoundId = fallbackId;
        }
        else
        {
            Raise(nameof(IsSelectedCustomTone));
            DeleteCustomToneCommand.RaiseCanExecuteChanged();
        }

        ToneStatus = customTonesRemain
            ? "Custom tone removed from the app's CustomTones backup. Other custom tones remain available."
            : "Custom tone removed from the app's CustomTones backup. No custom tones remain, so the Windows notification (default) is selected automatically.";
        Validate();
    }

    private void AddCustomTone()
    {
        NotificationSoundOption? added = _addCustomTone();
        if (added is null)
        {
            return;
        }

        NotificationSoundOption? existing = NotificationSoundOptions.FirstOrDefault(option => option.Id == added.Id);
        if (existing is null)
        {
            NotificationSoundOptions.Add(added);
        }

        NotificationSoundId = added.Id;
        ToneStatus = "Backup copy saved in the app's CustomTones folder. Deleting the original file will not affect this copy. Click Save changes to apply this tone to battery alerts.";
    }

    private void DeleteSelectedCustomTone()
    {
        string id = NotificationSoundId;
        NotificationSoundOption? selected = NotificationSoundOptions.FirstOrDefault(option => option.Id == id);
        if (selected is null || !selected.IsCustom)
        {
            return;
        }

        if (!_deleteCustomTone(id))
        {
            return;
        }

        OnCustomToneRemoved(id);
    }

    private void PreviewSelectedSound()
    {
        try
        {
            NotificationResult result = _previewSound(NotificationSoundId);
            ToneStatus = result.Requested
                ? "Sound preview requested. Custom files are played for up to 5 seconds."
                : result.Error ?? "The selected sound could not be played.";
        }
        catch (Exception ex)
        {
            ToneStatus = "The sound preview failed, but battery popups remain enabled. " + ex.Message;
        }
    }

    private void LoadDraft(AppSettings s)
    {
        _loading = true;
        AlertsEnabled = s.AlertsEnabled;
        NotificationsEnabled = s.NotificationsEnabled;
        NotificationSoundId = s.NotificationSoundId;
        StartWithWindows = s.StartWithWindows;
        DiagnosticLogging = s.DiagnosticLogging;
        Warning1Enabled = s.Warning1.Enabled;
        Warning2Enabled = s.Warning2.Enabled;
        CriticalEnabled = s.Critical.Enabled;
        Warning1Text = s.Warning1.Percent.ToString(CultureInfo.InvariantCulture);
        Warning2Text = s.Warning2.Percent.ToString(CultureInfo.InvariantCulture);
        CriticalText = s.Critical.Percent.ToString(CultureInfo.InvariantCulture);
        _loading = false;
        DraftChanged(clearStatus: false);
    }

    private void DraftChanged(bool clearStatus = true)
    {
        if (_loading)
        {
            return;
        }

        if (clearStatus)
        {
            SaveStatus = null;
        }

        Validate();
    }

    private static bool TryParsePercent(string text, out int value) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private void Validate()
    {
        var draft = _applied.Clone();
        draft.AlertsEnabled = _alertsEnabled;
        draft.NotificationsEnabled = _notificationsEnabled;
        draft.NotificationSoundId = _notificationSoundId;
        draft.StartWithWindows = _startWithWindows;
        draft.DiagnosticLogging = _diagnosticLogging;
        draft.Warning1.Enabled = _warning1Enabled;
        draft.Warning2.Enabled = _warning2Enabled;
        draft.Critical.Enabled = _criticalEnabled;

        const string notANumber = "Enter a whole number from 1 to 99.";
        bool p1 = TryParsePercent(_warning1Text, out int w1);
        bool p2 = TryParsePercent(_warning2Text, out int w2);
        bool pc = TryParsePercent(_criticalText, out int c);
        bool allParsed = p1 && p2 && pc;

        string? e1 = p1 ? null : notANumber;
        string? e2 = p2 ? null : notANumber;
        string? ec = pc ? null : notANumber;

        if (allParsed)
        {
            draft.Warning1.Percent = w1;
            draft.Warning2.Percent = w2;
            draft.Critical.Percent = c;
            var result = SettingsValidator.Validate(draft);
            e1 = result.ErrorFor(SettingsValidator.FieldWarning1);
            e2 = result.ErrorFor(SettingsValidator.FieldWarning2);
            ec = result.ErrorFor(SettingsValidator.FieldCritical);
        }

        Warning1Error = e1;
        Warning2Error = e2;
        CriticalError = ec;
        Raise(nameof(HasErrors));

        IsDirty = !allParsed || !SameAsApplied(draft);
        SaveCommand.RaiseCanExecuteChanged();
        RevertCommand.RaiseCanExecuteChanged();
    }

    private bool SameAsApplied(AppSettings d) =>
        d.AlertsEnabled == _applied.AlertsEnabled &&
        d.NotificationsEnabled == _applied.NotificationsEnabled &&
        d.NotificationSoundId == _applied.NotificationSoundId &&
        d.StartWithWindows == _applied.StartWithWindows &&
        d.DiagnosticLogging == _applied.DiagnosticLogging &&
        !d.ThresholdsDiffer(_applied);

    private AppSettings? TryBuildDraft()
    {
        if (!TryParsePercent(_warning1Text, out int w1) ||
            !TryParsePercent(_warning2Text, out int w2) ||
            !TryParsePercent(_criticalText, out int c))
        {
            return null;
        }

        var draft = _applied.Clone();
        draft.AlertsEnabled = _alertsEnabled;
        draft.NotificationsEnabled = _notificationsEnabled;
        draft.NotificationSoundId = _notificationSoundId;
        draft.StartWithWindows = _startWithWindows;
        draft.DiagnosticLogging = _diagnosticLogging;
        draft.Warning1.Enabled = _warning1Enabled;
        draft.Warning1.Percent = w1;
        draft.Warning2.Enabled = _warning2Enabled;
        draft.Warning2.Percent = w2;
        draft.Critical.Enabled = _criticalEnabled;
        draft.Critical.Percent = c;
        return draft;
    }

    private void Save()
    {
        var draft = TryBuildDraft();
        if (draft is null || !SettingsValidator.Validate(draft).IsValid)
        {
            Validate();
            SaveStatus = "Fix the highlighted fields before saving.";
            return;
        }

        SaveOutcome outcome = _save(draft);
        if (outcome.Success)
        {
            _applied = (outcome.Applied ?? draft).Clone();
            LoadDraft(_applied);
        }

        SaveStatus = outcome.Message;
    }

    private void Revert()
    {
        LoadDraft(_applied);
        SaveStatus = "Changes discarded.";
    }

    private void RunTest()
    {
        var result = _test();
        TestStatus = result.Requested ? TestSentMessage : result.Error ?? "The test notification could not be shown.";
    }
}
