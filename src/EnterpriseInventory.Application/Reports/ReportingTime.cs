using System.Globalization;

namespace EnterpriseInventory.Application.Reports;

/// <summary>Days and months of the reporting time zone (<c>Reporting:TimeZone</c>) as moments, and how files name the zone.</summary>
public static class ReportingTime
{
    /// <summary>When a local wall-clock time happens; a time a clock change skips is moved past the change.</summary>
    public static DateTimeOffset ToMoment(DateTime local, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    /// <summary>The local midnight that starts <paramref name="day"/>.</summary>
    public static DateTimeOffset StartOf(DateOnly day, TimeZoneInfo zone) => ToMoment(day.ToDateTime(TimeOnly.MinValue), zone);

    /// <summary>The wall-clock time of <paramref name="moment"/>, as a spreadsheet date cell holds it.</summary>
    public static DateTime Local(DateTimeOffset moment, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(moment, zone).DateTime;

    /// <summary>E.g. <c>Europe/Istanbul (UTC+03:00)</c>, with the offset in force at <paramref name="localNow"/>.</summary>
    public static string Describe(TimeZoneInfo zone, DateTimeOffset localNow)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var offset = localNow.Offset;
        return string.Create(CultureInfo.InvariantCulture, $"{zone.Id} (UTC{(offset < TimeSpan.Zero ? '-' : '+')}{offset:hh\\:mm})");
    }
}
