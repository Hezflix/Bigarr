using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Shadow;

/// <summary>Bigarr's side of a series row: one pick, the episodes it covers, and what Sonarr made of the release.</summary>
public sealed record SeriesComparePickResponse(
    int Id,
    DateTime PickedAt,
    DateTime LastPickedAt,
    int Picks,
    string Trigger,
    string Title,
    string? Indexer,
    long Size,
    string? Quality,
    string? ReleaseGroup,
    List<string> Languages,
    List<string> CustomFormats,
    int CustomFormatScore,
    DateTimeOffset? PublishDate,
    bool FullSeason,
    string Episodes,
    SonarrView? Sonarr,
    /// <summary>A film's pick standing for a special that is that film (S8): "Demon Slayer: Mugen Train (2020)".</summary>
    string? Film = null);

/// <summary>Sonarr's side of a series row: one grab, the episodes it covers, how it ended, and Bigarr's engine on it.</summary>
public sealed record SeriesCompareGrabResponse(
    int Id,
    string SonarrName,
    DateTime GrabbedAt,
    string Title,
    string? Indexer,
    long Size,
    string? Quality,
    string? ReleaseGroup,
    List<string> Languages,
    List<string> CustomFormats,
    int? CustomFormatScore,
    DateTime? PublishedAt,
    string? ReleaseSource,
    string? ReleaseType,
    bool FullSeason,
    string Episodes,
    RadarrGrabState State,
    DateTime? ResolvedAt,
    BigarrVerdict? Bigarr);

public sealed record SeriesCompareRowResponse(
    string Key,
    SeriesCompareOutcome Outcome,
    int TvdbId,
    int? SeriesId,
    string SeriesTitle,
    int? Year,
    string? PosterUrl,
    string VersionName,
    DateTime At,
    /// <summary>Every episode either side covers on the row.</summary>
    string Episodes,
    /// <summary>The first season the row covers, for grouping by season.</summary>
    int SeasonNumber,
    List<SeriesCompareGrabResponse> Sonarr,
    List<SeriesComparePickResponse> Bigarr,
    /// <summary>Other picks for those episodes while Sonarr's download was in flight, or upgrades within the group.</summary>
    List<SeriesComparePickResponse> AlsoPicked,
    /// <summary>Positive when Bigarr picked first: by how many seconds. Null on a one-sided row.</summary>
    long? BigarrLeadSeconds,
    /// <summary>Sonarr-only rows: the RSS pass's sighting of Sonarr's release (null: never seen in Bigarr's feeds).</summary>
    CompareSightingResponse? Sighting);

public sealed record SeriesCompareCounts(int Same, int Different, int BigarrOnly, int SonarrOnly)
{
    public int Total => Same + Different + BigarrOnly + SonarrOnly;
}

public sealed record SeriesCompareVersionSummary(string VersionName, SeriesCompareCounts Counts);

/// <summary>One show of Activity → Compare · Sonarr: one line until opened, its rows newest first.</summary>
public sealed record SeriesCompareGroupResponse(
    int TvdbId,
    int? SeriesId,
    string SeriesTitle,
    int? Year,
    string? PosterUrl,
    SeriesCompareCounts Counts,
    /// <summary>The latest row's time.</summary>
    DateTime LatestAt,
    List<SeriesCompareRowResponse> Rows);

/// <param name="Total">Shows with at least one row of the asked outcome; pages count shows, not rows.</param>
public sealed record SeriesCompareResponse(
    DateTime? From,
    DateTime To,
    int WindowHours,
    bool ShadowEnabled,
    List<string> Versions,
    SeriesCompareCounts Summary,
    List<SeriesCompareVersionSummary> ByVersion,
    List<SeriesCompareGroupResponse> Groups,
    int Total,
    int Page,
    int PageSize);

/// <summary>
/// Activity → Compare for series: per series, version and episode set, what Sonarr grabbed next to what Bigarr picked
/// (<see cref="SeriesComparePairing"/>), with Bigarr's engine on Sonarr's releases, Sonarr's parse of Bigarr's, and
/// whether Bigarr's feeds ever carried what Sonarr took.
/// </summary>
public sealed class SeriesCompareService(BigarrDbContext db, IOptions<ShadowOptions> options, TimeProvider time)
{
    /// <param name="tvdbId">One series only: the series page's own comparison.</param>
    public async Task<SeriesCompareResponse> CompareAsync(string? version, SeriesCompareOutcome? outcome, int days, int windowHours, int page, int pageSize, CancellationToken ct, int? tvdbId = null)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var window = TimeSpan.FromHours(Math.Clamp(windowHours, 1, CompareService.MaxWindowHours));
        DateTime? from = days > 0 ? now.AddDays(-days) : null;
        var loadFrom = from is { } f ? f - window : DateTime.MinValue;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var grabQuery = db.SonarrGrabs.AsNoTracking().Include(g => g.FollowedSonarr).Include(g => g.Episodes).Where(g => g.GrabbedAt >= loadFrom);
        var pickQuery = db.SeriesShadowPicks.AsNoTracking().Include(p => p.Episodes).Where(p => p.PickedAt >= loadFrom && p.TvdbId != null);
        if (!string.IsNullOrWhiteSpace(version))
        {
            grabQuery = grabQuery.Where(g => g.VersionName == version);
            pickQuery = pickQuery.Where(p => p.VersionName == version);
        }

        if (tvdbId is { } show)
        {
            grabQuery = grabQuery.Where(g => g.TvdbId == show);
            pickQuery = pickQuery.Where(p => p.TvdbId == show);
        }

        var grabs = (await grabQuery.AsSplitQuery().ToListAsync(ct)).ToDictionary(g => g.Id);
        var picks = (await pickQuery.AsSplitQuery().ToListAsync(ct)).ToDictionary(p => p.Id);
        var filmPicks = await FilmPicksAsync(grabs.Values, loadFrom, ct);
        var rows = SeriesComparePairing.Match(
                grabs.Values.Select(g => new SeriesComparePairing.Grab(g.Id, g.TvdbId, g.VersionName, g.GrabbedAt, g.ReleaseGuid, g.InfoHash, g.ReleaseTitle, g.ResolvedAt, g.State == RadarrGrabState.Grabbed, EpisodesOf(g))),
                picks.Values.Select(p => new SeriesComparePairing.Pick(p.Id, p.TvdbId!.Value, p.VersionName, p.PickedAt, p.ReleaseGuid, p.InfoHash, p.ReleaseTitle, EpisodesOf(p)))
                    .Concat(filmPicks.Values.Select(f => f.Pair)),
                window,
                now)
            .Where(r => from is null || r.At >= from)
            .ToList();

        var summary = Count(rows);
        var byVersion = rows
            .GroupBy(r => r.Version)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new SeriesCompareVersionSummary(g.Key, Count(g)))
            .ToList();

        // Paged by show: a show with a hundred rows is one line until opened.
        var shows = rows.Where(r => outcome is null || r.Outcome == outcome)
            .GroupBy(r => r.TvdbId)
            .Select(g => (TvdbId: g.Key, Rows: g.OrderByDescending(r => r.At).ToList()))
            .OrderByDescending(g => g.Rows[0].At)
            .ToList();
        var pageShows = shows.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var pageRows = pageShows.SelectMany(g => g.Rows).ToList();

        var tvdbIds = pageRows.Select(r => r.TvdbId).Distinct().ToList();
        var series = (await db.Series.AsNoTracking().Where(s => s.TvdbId != null && tvdbIds.Contains(s.TvdbId.Value)).ToListAsync(ct)).ToDictionary(s => s.TvdbId!.Value);
        var sightings = await SightingsAsync(pageRows.Where(r => r.Outcome == SeriesCompareOutcome.SonarrOnly).Select(r => grabs[r.Sonarr[0].Id]).ToList(), ct);

        var responses = pageRows.Select(row =>
        {
            var rowGrabs = row.Sonarr.Select(g => grabs[g.Id]).ToList();
            var rowPicks = row.Bigarr.Where(p => p.Id > 0).Select(p => picks[p.Id]).ToList();
            var rowFilmPicks = row.Bigarr.Where(p => p.Id < 0).Select(p => filmPicks[p.Id]).ToList();
            var show = series.GetValueOrDefault(row.TvdbId);
            var title = show?.Title ?? rowGrabs.FirstOrDefault()?.SeriesTitle ?? rowPicks.FirstOrDefault()?.SeriesTitle ?? rowFilmPicks[0].SeriesTitle;
            var pickedAt = rowPicks.Select(p => p.PickedAt).Concat(rowFilmPicks.Select(f => f.Pick.PickedAt)).ToList();
            long? lead = rowGrabs.Count > 0 && pickedAt.Count > 0 ? (long)(rowGrabs.Min(g => g.GrabbedAt) - pickedAt.Min()).TotalSeconds : null;
            var key = $"{string.Join(".", rowGrabs.Select(g => $"g{g.Id}"))}-{string.Join(".", rowPicks.Select(p => $"p{p.Id}").Concat(rowFilmPicks.Select(f => $"f{f.Pick.Id}")))}";
            return new SeriesCompareRowResponse(
                key,
                row.Outcome,
                row.TvdbId,
                show?.Id ?? rowGrabs.FirstOrDefault()?.SeriesId ?? rowPicks.FirstOrDefault()?.SeriesId ?? rowFilmPicks.FirstOrDefault()?.SeriesId,
                title,
                show?.Year,
                show?.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
                row.Version,
                row.At,
                SonarrFollower.EpisodesLabel(row.Episodes),
                row.Episodes.Count == 0 ? 0 : row.Episodes.Min(e => e.Season),
                rowGrabs.Select(ToResponse).ToList(),
                rowPicks.Select(ToResponse).Concat(rowFilmPicks.Select(ToResponse)).ToList(),
                row.AlsoPicked.Select(p => p.Id < 0 ? ToResponse(filmPicks[p.Id]) : ToResponse(picks[p.Id])).ToList(),
                lead,
                rowGrabs.Count == 1 && rowPicks.Count == 0 && sightings.TryGetValue(rowGrabs[0].Id, out var sighting) ? Sighting(sighting, row.Version) : null);
        }).ToList();
        var byShow = responses.ToLookup(r => r.TvdbId);
        var groups = pageShows.Select(g =>
        {
            var first = byShow[g.TvdbId].First();
            return new SeriesCompareGroupResponse(
                g.TvdbId,
                first.SeriesId,
                first.SeriesTitle,
                first.Year,
                first.PosterUrl,
                Count(g.Rows),
                g.Rows[0].At,
                byShow[g.TvdbId].ToList());
        }).ToList();

        var versions = (await db.SeriesShadowPicks.AsNoTracking().Select(p => p.VersionName).Distinct().ToListAsync(ct))
            .Union(await db.SonarrGrabs.AsNoTracking().Select(g => g.VersionName).Distinct().ToListAsync(ct))
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        return new SeriesCompareResponse(from, now, (int)window.TotalHours, options.Value.Enabled, versions, summary, byVersion, groups, shows.Count, page, pageSize);
    }

    /// <summary>A film pick shown as the Bigarr side of a special that is that film (ids negated so they never clash).</summary>
    private sealed record FilmPick(SeriesComparePairing.Pick Pair, ShadowPick Pick, int SeriesId, string SeriesTitle, int EpisodeNumber, string Film);

    /// <summary>
    /// Specials that are films (S8) are never picked on the series side: the film engine picks for the film. So a
    /// Sonarr grab of such a special pairs with the film's picks (by identity first, as always), once per Sonarr version
    /// that grabbed the special.
    /// </summary>
    private async Task<Dictionary<int, FilmPick>> FilmPicksAsync(IEnumerable<SonarrGrab> grabs, DateTime loadFrom, CancellationToken ct)
    {
        var specials = grabs.SelectMany(g => g.Episodes.Where(e => e.SeasonNumber == 0).Select(e => (g.TvdbId, g.VersionName, e.EpisodeNumber))).Distinct().ToList();
        var result = new Dictionary<int, FilmPick>();
        if (specials.Count == 0)
        {
            return result;
        }

        var tvdbIds = specials.Select(s => s.TvdbId).Distinct().ToList();
        var links = await db.SeriesMovieLinks.AsNoTracking()
            .Where(l => l.Series.TvdbId != null && tvdbIds.Contains(l.Series.TvdbId.Value) && l.Episode.SeasonNumber == 0)
            .Select(l => new { TvdbId = l.Series.TvdbId!.Value, l.SeriesId, SeriesTitle = l.Series.Title, l.Episode.EpisodeNumber, l.MovieId, l.Movie.TmdbId, l.Movie.Title, l.Movie.Year })
            .ToListAsync(ct);
        if (links.Count == 0)
        {
            return result;
        }

        var tmdbIds = links.Select(l => l.TmdbId).Distinct().ToList();
        var filmPicks = await db.ShadowPicks.AsNoTracking().Where(p => tmdbIds.Contains(p.TmdbId) && p.PickedAt >= loadFrom).ToListAsync(ct);
        foreach (var link in links)
        {
            foreach (var version in specials.Where(s => s.TvdbId == link.TvdbId && s.EpisodeNumber == link.EpisodeNumber).Select(s => s.VersionName).Distinct())
            {
                foreach (var pick in filmPicks.Where(p => p.TmdbId == link.TmdbId))
                {
                    // One id per (pick, version) so the same film pick can stand in each Sonarr version's key.
                    var id = -(result.Count + 1);
                    var film = link.Year is { } year ? $"{link.Title} ({year})" : link.Title;
                    result[id] = new FilmPick(
                        new SeriesComparePairing.Pick(id, link.TvdbId, version, pick.PickedAt, pick.ReleaseGuid, pick.InfoHash, pick.ReleaseTitle, new HashSet<(int, int)> { (0, link.EpisodeNumber) }),
                        pick,
                        link.SeriesId,
                        link.SeriesTitle,
                        link.EpisodeNumber,
                        film);
                }
            }
        }

        return result;
    }

    private static SeriesComparePickResponse ToResponse(FilmPick film) => new(
        film.Pair.Id, film.Pick.PickedAt, film.Pick.LastPickedAt, film.Pick.Picks, film.Pick.Trigger, film.Pick.ReleaseTitle, film.Pick.IndexerName, film.Pick.Size, film.Pick.Quality,
        film.Pick.ReleaseGroup, film.Pick.Languages, film.Pick.CustomFormats, film.Pick.CustomFormatScore, film.Pick.PublishDate, false, $"S00E{film.EpisodeNumber:00}", null, film.Film);

    private static HashSet<(int Season, int Episode)> EpisodesOf(SonarrGrab grab) => grab.Episodes.Select(e => (e.SeasonNumber, e.EpisodeNumber)).ToHashSet();

    private static HashSet<(int Season, int Episode)> EpisodesOf(SeriesShadowPick pick) => pick.Episodes.Select(e => (e.SeasonNumber, e.EpisodeNumber)).ToHashSet();

    private static SeriesCompareCounts Count(IEnumerable<SeriesComparePairing.Row> rows)
    {
        var list = rows.ToList();
        return new SeriesCompareCounts(
            list.Count(r => r.Outcome == SeriesCompareOutcome.Same),
            list.Count(r => r.Outcome == SeriesCompareOutcome.Different),
            list.Count(r => r.Outcome == SeriesCompareOutcome.BigarrOnly),
            list.Count(r => r.Outcome == SeriesCompareOutcome.SonarrOnly));
    }

    /// <summary>The first sighting of each grab's release in Bigarr's feeds: same guid, same info hash, or same title.</summary>
    private async Task<Dictionary<int, ReleaseSighting>> SightingsAsync(List<SonarrGrab> grabs, CancellationToken ct)
    {
        if (grabs.Count == 0)
        {
            return [];
        }

        var guids = grabs.Select(g => g.ReleaseGuid).OfType<string>().Distinct().ToList();
        var hashes = grabs.Select(g => g.InfoHash).OfType<string>().Distinct().ToList();
        var titles = grabs.Select(g => g.ReleaseTitle).Distinct().ToList();
        var found = await db.ReleaseSightings.AsNoTracking()
            .Where(s => guids.Contains(s.Guid) || (s.InfoHash != null && hashes.Contains(s.InfoHash)) || titles.Contains(s.Title))
            .OrderBy(s => s.SeenAt)
            .ToListAsync(ct);

        var result = new Dictionary<int, ReleaseSighting>();
        foreach (var grab in grabs)
        {
            var sighting = found.FirstOrDefault(s =>
                (grab.ReleaseGuid is not null && s.Guid == grab.ReleaseGuid)
                || (grab.InfoHash is not null && s.InfoHash == grab.InfoHash)
                || string.Equals(s.Title, grab.ReleaseTitle, StringComparison.OrdinalIgnoreCase));
            if (sighting is not null)
            {
                result[grab.Id] = sighting;
            }
        }

        return result;
    }

    private static CompareSightingResponse Sighting(ReleaseSighting sighting, string versionName) => new(
        sighting.SeenAt,
        sighting.IndexerName,
        sighting.Title,
        sighting.TvdbId is not null,
        sighting.UnmatchedReason,
        sighting.AttributedVersion,
        sighting.Verdicts.FirstOrDefault(v => v.VersionName == versionName));

    private static SeriesComparePickResponse ToResponse(SeriesShadowPick pick) => new(
        pick.Id, pick.PickedAt, pick.LastPickedAt, pick.Picks, pick.Trigger, pick.ReleaseTitle, pick.IndexerName, pick.Size, pick.Quality, pick.ReleaseGroup,
        pick.Languages, pick.CustomFormats, pick.CustomFormatScore, pick.PublishDate, pick.FullSeason,
        SonarrFollower.EpisodesLabel(pick.Episodes.Select(e => (e.SeasonNumber, e.EpisodeNumber))), pick.SonarrView);

    private static SeriesCompareGrabResponse ToResponse(SonarrGrab grab) => new(
        grab.Id, grab.FollowedSonarr.Name, grab.GrabbedAt, grab.ReleaseTitle, grab.IndexerName, grab.Size, grab.Quality, grab.ReleaseGroup, grab.Languages,
        grab.CustomFormats, grab.CustomFormatScore, grab.PublishedAt, grab.ReleaseSource, grab.ReleaseType, grab.FullSeason,
        SonarrFollower.EpisodesLabel(grab.Episodes.Select(e => (e.SeasonNumber, e.EpisodeNumber))), grab.State, grab.ResolvedAt, grab.BigarrVerdict);
}
