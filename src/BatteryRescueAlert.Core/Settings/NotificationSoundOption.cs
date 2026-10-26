namespace BatteryRescueAlert.Core.Settings;

/// <summary>One selectable system or imported notification tone shown in Settings.</summary>
public sealed record NotificationSoundOption(string Id, string Name, bool IsCustom = false);
