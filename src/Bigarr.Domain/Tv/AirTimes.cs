using System.Globalization;

namespace Bigarr.Domain.Tv;

/// <summary>
/// Turns a local air date and time into UTC the way Sonarr's <c>airDateUtc</c> reads. Sonarr gets that value from its
/// metadata proxy (Skyhook), which reads the time in the time zone of the <b>network</b>, not of the show's country: The
/// Crown (a British show on Netflix) airs at 08:00 New York time, Vinland Saga at 00:30 Tokyo time (the day before in
/// UTC), Doctor Who at 18:30 London time (GMT or BST). So the zone comes from the network's country first, then from the
/// show's; a US network reads Eastern time. Unknown country or time zone: the time is taken as UTC; no time: midnight.
/// </summary>
public static class AirTimes
{
    /// <summary>ISO 3166-1 alpha-2 (lower case) → the IANA zone a network of that country airs in.</summary>
    private static readonly Dictionary<string, string> CountryZones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["us"] = "America/New_York", ["ca"] = "America/Toronto", ["mx"] = "America/Mexico_City", ["br"] = "America/Sao_Paulo",
        ["ar"] = "America/Argentina/Buenos_Aires", ["cl"] = "America/Santiago", ["co"] = "America/Bogota", ["pe"] = "America/Lima",
        ["gb"] = "Europe/London", ["ie"] = "Europe/Dublin", ["fr"] = "Europe/Paris", ["de"] = "Europe/Berlin",
        ["at"] = "Europe/Vienna", ["ch"] = "Europe/Zurich", ["es"] = "Europe/Madrid", ["pt"] = "Europe/Lisbon",
        ["it"] = "Europe/Rome", ["nl"] = "Europe/Amsterdam", ["be"] = "Europe/Brussels", ["lu"] = "Europe/Luxembourg",
        ["se"] = "Europe/Stockholm", ["dk"] = "Europe/Copenhagen", ["no"] = "Europe/Oslo", ["fi"] = "Europe/Helsinki",
        ["is"] = "Atlantic/Reykjavik", ["pl"] = "Europe/Warsaw", ["cz"] = "Europe/Prague", ["sk"] = "Europe/Bratislava",
        ["hu"] = "Europe/Budapest", ["ro"] = "Europe/Bucharest", ["bg"] = "Europe/Sofia", ["gr"] = "Europe/Athens",
        ["hr"] = "Europe/Zagreb", ["rs"] = "Europe/Belgrade", ["si"] = "Europe/Ljubljana", ["ee"] = "Europe/Tallinn",
        ["lv"] = "Europe/Riga", ["lt"] = "Europe/Vilnius", ["ua"] = "Europe/Kyiv", ["ru"] = "Europe/Moscow",
        ["tr"] = "Europe/Istanbul", ["il"] = "Asia/Jerusalem", ["ae"] = "Asia/Dubai", ["sa"] = "Asia/Riyadh",
        ["eg"] = "Africa/Cairo", ["za"] = "Africa/Johannesburg", ["ng"] = "Africa/Lagos",
        ["jp"] = "Asia/Tokyo", ["kr"] = "Asia/Seoul", ["cn"] = "Asia/Shanghai", ["hk"] = "Asia/Hong_Kong",
        ["tw"] = "Asia/Taipei", ["in"] = "Asia/Kolkata", ["th"] = "Asia/Bangkok", ["ph"] = "Asia/Manila",
        ["id"] = "Asia/Jakarta", ["my"] = "Asia/Kuala_Lumpur", ["sg"] = "Asia/Singapore", ["vn"] = "Asia/Ho_Chi_Minh",
        ["au"] = "Australia/Sydney", ["nz"] = "Pacific/Auckland",
    };

    /// <param name="networkCountry">Country of the network the show airs on (TVDB's <c>originalNetwork.country</c>): decides first.</param>
    /// <param name="country">The show's own country, when the network's is unknown.</param>
    public static DateTime? ToUtc(DateOnly? airDate, string? airTime, string? networkCountry, string? country)
    {
        if (airDate is not { } date)
        {
            return null;
        }

        var time = TimeOnly.TryParseExact(airTime?.Trim(), ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : TimeOnly.MinValue;
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);

        if ((Zone(networkCountry) ?? Zone(country)) is { } zone)
        {
            // A time the spring change skips does not exist: read it an hour on (Bigarr's choice, the proxy's rule is not public).
            return zone.IsInvalidTime(local) ? TimeZoneInfo.ConvertTimeToUtc(local.AddHours(1), zone) : TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }

        return DateTime.SpecifyKind(local, DateTimeKind.Utc);
    }

    /// <summary>The time zone of a country's networks, or null when unknown (or no tz database on the machine).</summary>
    public static TimeZoneInfo? Zone(string? country)
    {
        if (country is null || !CountryZones.TryGetValue(country.Trim(), out var zoneId))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sonarr's <c>AdjustMultiEpisodeAirTime</c>: episodes of a season airing the same day are spread one runtime apart,
    /// in episode order, so a double bill shows as two slots; more than three on one day (a streaming drop) are left
    /// alone, except in the specials.
    /// </summary>
    public static void SpreadSameDayEpisodes(IEnumerable<Episode> episodes, int? runtimeMinutes)
    {
        ArgumentNullException.ThrowIfNull(episodes);

        var groups = episodes.Where(e => e.AirDateUtc is not null)
            .GroupBy(e => (e.SeasonNumber, e.AirDate))
            .Where(g => g.Count() > 1);
        foreach (var group in groups)
        {
            if (group.Key.SeasonNumber != 0 && group.Count() > 3)
            {
                continue;
            }

            var index = 0;
            foreach (var episode in group.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber))
            {
                episode.AirDateUtc = episode.AirDateUtc!.Value.AddMinutes((runtimeMinutes ?? 0) * index);
                index++;
            }
        }
    }

    /// <summary>
    /// Sonarr's <c>AdjustDirectToDvdAirDate</c>: an ended show none of whose episodes has a date (a direct-to-video
    /// release) gets the series' first air date on every episode, so it counts as aired.
    /// </summary>
    public static void DateDirectToVideo(IReadOnlyCollection<Episode> episodes, SeriesStatus status, DateOnly? firstAired)
    {
        ArgumentNullException.ThrowIfNull(episodes);

        if (status != SeriesStatus.Ended || firstAired is not { } first || episodes.Count == 0 || episodes.Any(e => e.AirDateUtc is not null))
        {
            return;
        }

        foreach (var episode in episodes)
        {
            episode.AirDate = first;
            episode.AirDateUtc = first.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        }
    }
}
