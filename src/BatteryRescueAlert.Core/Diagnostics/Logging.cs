using System.Globalization;

namespace BatteryRescueAlert.Core.Diagnostics;

public interface ILog
{
    void Info(string message);
    void Warn(string message);

    /// <summary>Logs the exception TYPE and HResult only: messages and stack traces can contain user paths.</summary>
    void Error(string message, Exception? exception = null);
}

public sealed class NullLog : ILog
{
    public static NullLog Instance { get; } = new();
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

/// <summary>
/// Small rolling text log: when the file exceeds <c>maxBytes</c> it is moved to "<c>.1</c>" (replacing
/// the previous backup), so total disk use stays below about twice the limit. Never throws.
/// Only write generic diagnostics: no document names, window titles or device identifiers.
/// </summary>
public sealed class RollingFileLog : ILog
{
    private readonly string _path;
    private readonly Func<bool> _isEnabled;
    private readonly long _maxBytes;
    private readonly object _gate = new();

    public RollingFileLog(string path, Func<bool> isEnabled, long maxBytes = 256 * 1024)
    {
        _path = path;
        _isEnabled = isEnabled;
        _maxBytes = maxBytes;
    }

    public void Info(string message) => Write("INFO ", message);
    public void Warn(string message) => Write("WARN ", message);

    public void Error(string message, Exception? exception = null)
    {
        string suffix = exception is null
            ? string.Empty
            : $" ({exception.GetType().Name}, HRESULT 0x{exception.HResult:X8})";
        Write("ERROR", message + suffix);
    }

    private void Write(string level, string message)
    {
        try
        {
            if (!_isEnabled())
            {
                return;
            }

            lock (_gate)
            {
                string? dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var info = new FileInfo(_path);
                if (info.Exists && info.Length > _maxBytes)
                {
                    File.Move(_path, _path + ".1", overwrite: true);
                }

                string line = string.Create(CultureInfo.InvariantCulture,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
                File.AppendAllText(_path, line);
            }
        }
        catch
        {
            // Logging must never break the app.
        }
    }
}
