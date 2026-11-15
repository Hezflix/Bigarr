using Bigarr.Api.Features.Users;
using Bigarr.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Discover;

/// <summary>A TMDB title found in the library: its library id (for the admin's link) and its availability.</summary>
public sealed record FilmMatch(int MovieId, AvailabilityResponse Availability);

/// <summary>A series found in the library, with the availability of each of its seasons by season number.</summary>
public sealed record SeriesMatch(int SeriesId, AvailabilityResponse Availability, IReadOnlyDictionary<int, AvailabilityResponse> Seasons);

/// <summary>
/// Availability read from the library on every request (no scan, nothing stored): films matched by TMDB id, series by
/// TMDB id (or TVDB id on a title page, where TMDB gives it). "Standard" is the title's default version, "4K" the
/// version carrying the name set in Discover settings.
/// </summary>
public sealed class LibraryAvailability(BigarrDbContext db, TimeProvider time)
{
    private string? _fourK;
    private bool _fourKRead;

    /// <summary>The 4K version name, lower case; null when Discover shows the default version only.</summary>
    public async Task<string?> FourKVersionAsync(CancellationToken ct)
    {
        if (!_fourKRead)
        {
            var name = (await UserSettings.ReadAsync(db, ct)).DiscoverFourKVersion;
            _fourK = string.IsNullOrWhiteSpace(name) ? null : name.Trim().ToLowerInvariant();
            _fourKRead = true;
        }

        return _fourK;
    }

    public async Task<IReadOnlyDictionary<int, FilmMatch>> FilmsAsync(IReadOnlyCollection<int> tmdbIds, CancellationToken ct)
    {
        if (tmdbIds.Count == 0)
        {
            return new Dictionary<int, FilmMatch>();
        }

        var fourK = await FourKVersionAsync(ct);
        var versions = await db.MovieVersions.AsNoTracking()
            .Where(v => tmdbIds.Contains(v.Movie.TmdbId))
            .Select(v => new { v.Id, v.Movie.TmdbId, v.MovieId, v.IsDefault, v.Name, v.Monitored, HasFile = v.File != null })
            .ToListAsync(ct);

        return versions.GroupBy(v => v.TmdbId).ToDictionary(g => g.Key, g =>
        {
            var standard = StandardOf(g, v => v.IsDefault, v => v.Name, v => v.Id, fourK);
            var fourKVersion = fourK is null ? null : g.FirstOrDefault(v => v.Name == fourK);
            return new FilmMatch(
                g.First().MovieId,
                new AvailabilityResponse(
                    standard is null ? AvailabilityState.NotInLibrary : Availability.OfFilm(standard.HasFile, standard.Monitored),
                    fourK is null ? null : fourKVersion is null ? AvailabilityState.NotInLibrary : Availability.OfFilm(fourKVersion.HasFile, fourKVersion.Monitored)));
        });
    }

    public async Task<IReadOnlyDictionary<int, SeriesMatch>> SeriesAsync(IReadOnlyCollection<int> tmdbIds, CancellationToken ct)
    {
        if (tmdbIds.Count == 0)
        {
            return new Dictionary<int, SeriesMatch>();
        }

        var series = await db.Series.AsNoTracking()
            .Where(s => s.TmdbId != null && tmdbIds.Contains(s.TmdbId.Value))
            .Select(s => new { s.Id, TmdbId = s.TmdbId!.Value })
            .ToListAsync(ct);
        var matches = await MatchSeriesAsync(series.Select(s => s.Id).ToList(), ct);
        return series.Where(s => matches.ContainsKey(s.Id)).ToDictionary(s => s.TmdbId, s => matches[s.Id]);
    }

    /// <summary>A title page: TMDB gives the TVDB id too, which finds a series added from TVDB without a TMDB id.</summary>
    public async Task<SeriesMatch?> SeriesAsync(int tmdbId, int? tvdbId, CancellationToken ct)
    {
        var id = await db.Series.AsNoTracking()
            .Where(s => s.TmdbId == tmdbId || (tvdbId != null && s.TvdbId == tvdbId))
            .OrderBy(s => s.TmdbId == tmdbId ? 0 : 1)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct);
        if (id is null)
        {
            return null;
        }

        return (await MatchSeriesAsync([id.Value], ct)).GetValueOrDefault(id.Value);
    }

    /// <summary>Series by library id (a request knows the series it landed in, even one without a TMDB id).</summary>
    public async Task<Dictionary<int, SeriesMatch>> MatchSeriesAsync(List<int> seriesIds, CancellationToken ct)
    {
        var result = new Dictionary<int, SeriesMatch>();
        if (seriesIds.Count == 0)
        {
            return result;
        }

        var fourK = await FourKVersionAsync(ct);
        var versions = await db.SeriesVersions.AsNoTracking()
            .Where(v => seriesIds.Contains(v.SeriesId))
            .Select(v => new { v.Id, v.SeriesId, v.IsDefault, v.Name, v.Monitored })
            .ToListAsync(ct);
        var versionIds = versions.Select(v => v.Id).ToList();
        var episodes = (await db.EpisodeTargets.AsNoTracking()
                .Where(t => versionIds.Contains(t.VersionId))
                .Select(t => new { t.VersionId, t.Episode.SeasonNumber, t.Episode.AirDateUtc, t.Episode.AirDate, t.Monitored, HasFile = t.EpisodeFileId != null })
                .ToListAsync(ct))
            .ToLookup(e => e.VersionId);
        var seasonNumbers = (await db.Seasons.AsNoTracking()
                .Where(s => seriesIds.Contains(s.SeriesId))
                .Select(s => new { s.SeriesId, s.SeasonNumber })
                .ToListAsync(ct))
            .ToLookup(s => s.SeriesId, s => s.SeasonNumber);

        // Episode.HasAired, on projected columns: without a date an episode has not aired.
        var now = time.GetUtcNow().UtcDateTime;
        bool Aired(DateTime? utc, DateOnly? date) => utc is { } u ? u <= now : date is { } d && d.ToDateTime(TimeOnly.MinValue) <= now;

        foreach (var seriesId in seriesIds)
        {
            var mine = versions.Where(v => v.SeriesId == seriesId).ToList();
            var standard = StandardOf(mine, v => v.IsDefault, v => v.Name, v => v.Id, fourK);
            var fourKVersion = fourK is null ? null : mine.FirstOrDefault(v => v.Name == fourK);

            List<(int Season, EpisodeSlot Slot)> Slots(int? versionId, bool versionMonitored) => versionId is null
                ? []
                : episodes[versionId.Value].Select(e => (e.SeasonNumber, new EpisodeSlot(Aired(e.AirDateUtc, e.AirDate), e.HasFile, e.Monitored && versionMonitored))).ToList();

            var standardSlots = Slots(standard?.Id, standard?.Monitored ?? false);
            var fourKSlots = Slots(fourKVersion?.Id, fourKVersion?.Monitored ?? false);

            // A monitored version whose show has no episode listed yet is on its way, as a whole; a season needs episodes.
            AvailabilityState State(bool exists, bool versionMonitored, List<(int Season, EpisodeSlot Slot)> slots, Func<int, bool> seasons, bool whole)
            {
                if (!exists)
                {
                    return AvailabilityState.NotInLibrary;
                }

                var scoped = slots.Where(s => seasons(s.Season)).Select(s => s.Slot).ToList();
                return scoped.Count == 0 && whole && versionMonitored ? AvailabilityState.Processing : Availability.OfEpisodes(scoped);
            }

            // The series as a whole leaves the specials out: a missing special never makes a complete show partial.
            AvailabilityResponse Of(Func<int, bool> seasons, bool whole) => new(
                State(standard is not null, standard?.Monitored ?? false, standardSlots, seasons, whole),
                fourK is null ? null : State(fourKVersion is not null, fourKVersion?.Monitored ?? false, fourKSlots, seasons, whole));

            var bySeason = seasonNumbers[seriesId].Distinct().Order().ToDictionary(n => n, n => Of(s => s == n, whole: false));
            result[seriesId] = new SeriesMatch(seriesId, Of(s => s > 0, whole: true), bySeason);
        }

        return result;
    }

    /// <summary>
    /// The version a title's "standard" slot stands for: its default version, unless the default is the 4K one (a title
    /// first requested in 4K only); then its first other version. Null when it has only the 4K version.
    /// </summary>
    public static T? StandardOf<T>(IEnumerable<T> versions, Func<T, bool> isDefault, Func<T, string> name, Func<T, int> id, string? fourK)
        where T : class =>
        versions.Where(v => fourK is null || !string.Equals(name(v), fourK, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(isDefault)
            .ThenBy(id)
            .FirstOrDefault();
}
