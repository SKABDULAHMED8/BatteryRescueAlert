namespace BatteryRescueAlert.Core.Settings;

public sealed record ValidationError(string Field, string Message);

public sealed class ValidationResult
{
    public ValidationResult(IReadOnlyList<ValidationError> errors) => Errors = errors;

    public IReadOnlyList<ValidationError> Errors { get; }
    public bool IsValid => Errors.Count == 0;

    public string? ErrorFor(string field) => Errors.FirstOrDefault(e => e.Field == field)?.Message;
}

/// <summary>
/// Rules: every percentage is a whole number from 1 to 99 and the order is always
/// Warning 1 &gt; Warning 2 &gt; Critical, so the configuration can never contradict itself.
/// The rules apply to disabled thresholds too, which keeps the form predictable.
/// </summary>
public static class SettingsValidator
{
    public const string FieldWarning1 = nameof(AppSettings.Warning1);
    public const string FieldWarning2 = nameof(AppSettings.Warning2);
    public const string FieldCritical = nameof(AppSettings.Critical);

    public static ValidationResult Validate(AppSettings? settings)
    {
        var errors = new List<ValidationError>();
        if (settings?.Warning1 is null || settings.Warning2 is null || settings.Critical is null)
        {
            errors.Add(new ValidationError(FieldWarning1, "Threshold settings are incomplete."));
            return new ValidationResult(errors);
        }

        CheckRange(errors, FieldWarning1, "Warning 1", settings.Warning1.Percent);
        CheckRange(errors, FieldWarning2, "Warning 2", settings.Warning2.Percent);
        CheckRange(errors, FieldCritical, "Critical", settings.Critical.Percent);

        if (!errors.Any(e => e.Field is FieldWarning1 or FieldWarning2) &&
            settings.Warning2.Percent >= settings.Warning1.Percent)
        {
            errors.Add(new ValidationError(FieldWarning2, "Warning 2 must be lower than Warning 1."));
        }

        if (!errors.Any(e => e.Field is FieldWarning2 or FieldCritical) &&
            settings.Critical.Percent >= settings.Warning2.Percent)
        {
            errors.Add(new ValidationError(FieldCritical, "Critical must be lower than Warning 2."));
        }

        return new ValidationResult(errors);
    }

    private static void CheckRange(List<ValidationError> errors, string field, string label, int percent)
    {
        if (percent < AppSettings.MinPercent || percent > AppSettings.MaxPercent)
        {
            errors.Add(new ValidationError(field,
                $"{label} must be a whole number from {AppSettings.MinPercent} to {AppSettings.MaxPercent}."));
        }
    }
}
