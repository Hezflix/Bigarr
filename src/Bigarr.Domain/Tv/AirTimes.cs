using System.Globalization;

namespace Bigarr.Domain.Tv;

/// <summary>
/// Turns a local air date and time into UTC, the way Sonarr's <c>airDateUtc</c> reads: the time is local to the
/// series' country. Unknown country or time zone: the time is taken as UTC; no time: midnight.
/// </summary>
public static class AirTimes
{
    private static readonly Dictionary<string, string> CountryZones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["us"] = "America/New_York", ["ca"] = "America/Toronto", ["mx"] = "America/Mexico_City", ["br"] = "America/Sao_Paulo",
        ["gb"] = "Europe/London", ["ie"] = "Europe/Dublin", ["fr"] = "Europe/Paris", ["de"] = "Europe/Berlin",
        ["es"] = "Europe/Madrid", ["it"] = "Europe/Rome", ["nl"] = "Europe/Amsterdam", ["be"] = "Europe/Brussels",
        ["se"] = "Europe/Stockholm", ["dk"] = "Europe/Copenhagen", ["no"] = "Europe/Oslo", ["fi"] = "Europe/Helsinki",
        ["pl"] = "Europe/Warsaw", ["ru"] = "Europe/Moscow", ["tr"] = "Europe/Istanbul",
        ["jp"] = "Asia/Tokyo", ["kr"] = "Asia/Seoul", ["cn"] = "Asia/Shanghai", ["tw"] = "Asia/Taipei",
        ["in"] = "Asia/Kolkata", ["th"] = "Asia/Bangkok", ["au"] = "Australia/Sydney", ["nz"] = "Pacific/Auckland",
    };

    public static DateTime? ToUtc(DateOnly? airDate, string? airTime, string? country)
    {
        if (airDate is not { } date)
        {
            return null;
        }

        var time = TimeOnly.TryParseExact(airTime?.Trim(), ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : TimeOnly.MinValue;
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);

        if (country is not null && CountryZones.TryGetValue(country, out var zoneId))
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
                return zone.IsInvalidTime(local) ? DateTime.SpecifyKind(local, DateTimeKind.Utc) : TimeZoneInfo.ConvertTimeToUtc(local, zone);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // No tz database (minimal container): fall through to UTC.
            }
        }

        return DateTime.SpecifyKind(local, DateTimeKind.Utc);
    }
}
