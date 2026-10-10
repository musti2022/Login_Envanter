using System.ComponentModel.DataAnnotations;

namespace EnterpriseInventory.Application.Exports;

/// <summary>Excel exports and reports, from the <c>Reporting</c> configuration section.</summary>
public sealed class ReportingOptions : IValidatableObject
{
    public const string SectionName = "Reporting";

    /// <summary>
    /// The time zone dates are written in. Excel cells have no time zone, so the file shows times as the users read
    /// them; the info sheet names the zone. An IANA ID such as <c>Europe/Istanbul</c> (Windows IDs work on Windows too).
    /// </summary>
    [Required]
    public string TimeZone { get; set; } = "Europe/Istanbul";

    /// <summary>A larger export is refused with a message asking for narrower filters.</summary>
    [Range(1, 1_000_000)]
    public int MaxExportRows { get; set; } = 50_000;

    public TimeZoneInfo ResolveTimeZone() => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(TimeZone))
        {
            yield break;
        }

        string? error = null;
        try
        {
            ResolveTimeZone();
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            error = $"Reporting:TimeZone '{TimeZone}' is not a time zone this server knows.";
        }

        if (error is not null)
        {
            yield return new ValidationResult(error, [nameof(TimeZone)]);
        }
    }
}
