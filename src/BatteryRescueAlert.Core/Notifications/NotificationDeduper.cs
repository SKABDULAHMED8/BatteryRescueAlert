namespace BatteryRescueAlert.Core.Notifications;

/// <summary>
/// Last line of defence against notification spam: the same key is allowed at most once per window.
/// The threshold engine already prevents duplicates per discharge cycle; this protects against bugs,
/// event storms and rapid settings changes.
/// </summary>
public sealed class NotificationDeduper
{
    private readonly TimeSpan _window;
    private readonly Dictionary<string, DateTimeOffset> _last = new();

    public NotificationDeduper(TimeSpan? window = null) => _window = window ?? TimeSpan.FromSeconds(60);

    public bool TryAcquire(string key, DateTimeOffset now)
    {
        if (_last.TryGetValue(key, out var previous) && now - previous < _window && now >= previous)
        {
            return false;
        }

        _last[key] = now;
        return true;
    }
}
