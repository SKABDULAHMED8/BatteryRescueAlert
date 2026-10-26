using System.Text.Json;
using System.Text.Json.Serialization;
using BatteryRescueAlert.Core.Battery;
using BatteryRescueAlert.Core.Diagnostics;

namespace BatteryRescueAlert.Core.Settings;

/// <summary>Where app data lives. Per-user, no administrator rights needed.</summary>
public static class AppPaths
{
    public const string FolderName = "BatteryRescueAlert";

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string StateFile => Path.Combine(DataDirectory, "state.json");
    public static string LogFile => Path.Combine(DataDirectory, "diagnostic.log");
    public static string CustomTonesDirectory => Path.Combine(DataDirectory, "CustomTones");
}

/// <summary>Minimal JSON persistence with atomic replace. Never throws; failures are returned as text.</summary>
public sealed class JsonFileStore<T> where T : class
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ILog _log;

    public JsonFileStore(string path, ILog? log = null)
    {
        _path = path;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>Returns null with error == null when the file simply does not exist.</summary>
    public T? TryLoad(out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<T>(stream, Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = "The saved file could not be read.";
            _log.Error("Load failed", ex);
            return null;
        }
    }

    public bool TrySave(T value, out string? error)
    {
        error = null;
        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
            File.Move(temp, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = "Your settings could not be saved. Check that the app data folder is writable.";
            _log.Error("Save failed", ex);
            return false;
        }
    }
}

public sealed class SettingsRepository
{
    private readonly JsonFileStore<AppSettings> _store;

    public SettingsRepository(string path, ILog? log = null) => _store = new JsonFileStore<AppSettings>(path, log);

    /// <summary>Loads settings; falls back to safe defaults and explains why via <paramref name="warning"/>.</summary>
    public AppSettings LoadOrDefault(out string? warning)
    {
        var loaded = _store.TryLoad(out string? error);
        warning = error is null ? null : "Your saved settings could not be read, so defaults are being used.";

        if (loaded is null)
        {
            return AppSettings.CreateDefault();
        }

        loaded.EnsureComplete();
        if (!SettingsValidator.Validate(loaded).IsValid)
        {
            warning = "Your saved thresholds were not valid, so the default thresholds are being used.";
            var defaults = AppSettings.CreateDefault();
            loaded.Warning1 = defaults.Warning1;
            loaded.Warning2 = defaults.Warning2;
            loaded.Critical = defaults.Critical;
        }

        return loaded;
    }

    /// <summary>Validates, then saves. Invalid settings are never written.</summary>
    public bool TrySave(AppSettings settings, out string? error)
    {
        var result = SettingsValidator.Validate(settings);
        if (!result.IsValid)
        {
            error = result.Errors[0].Message;
            return false;
        }

        return _store.TrySave(settings, out error);
    }
}

public sealed class AlertStateRepository
{
    private readonly JsonFileStore<AlertCycleSnapshot> _store;
    private readonly ILog _log;

    public AlertStateRepository(string path, ILog? log = null)
    {
        _log = log ?? NullLog.Instance;
        _store = new JsonFileStore<AlertCycleSnapshot>(path, _log);
    }

    public AlertCycleState LoadOrNew()
    {
        var snapshot = _store.TryLoad(out _);
        var state = AlertCycleState.FromSnapshot(snapshot);

        if (snapshot is not null && snapshot.Version < AlertCycleSnapshot.CurrentVersion)
        {
            // Persist the migration immediately so an empty migrated state does not repeat it at every launch.
            if (!_store.TrySave(state.ToSnapshot(), out string? error) && error is not null)
            {
                _log.Warn("Could not persist the alert-state migration: " + error);
            }
        }

        return state;
    }

    public bool TrySave(AlertCycleState state, out string? error) => _store.TrySave(state.ToSnapshot(), out error);
}
