using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Shadow;

/// <summary>Bigarr's side of a row: the pick, and what Radarr made of the release when it was asked.</summary>
public sealed record ComparePickResponse(
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
    RadarrView? Radarr);

/// <summary>Radarr's side of a row: the grab, how it ended, and what Bigarr's engine made of the release when it was read.</summary>
public sealed record CompareGrabResponse(
    int Id,
    string RadarrName,
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
    RadarrGrabState State,
    DateTime? ResolvedAt,
    BigarrVerdict? Bigarr);

/// <summary>What Bigarr's RSS pass saw of Radarr's release, if it ever did, and its verdict for the version then.</summary>
public sealed record CompareSightingResponse(DateTime SeenAt, string Indexer, string Title, bool Matched, string? UnmatchedReason, string? AttributedVersion, SightingVerdict? Verdict);

public sealed record CompareRowResponse(
    string Key,
    CompareOutcome Outcome,
    int TmdbId,
    int? MovieId,
    string MovieTitle,
    int? Year,
    string? PosterUrl,
    string VersionName,
    DateTime At,
    CompareGrabResponse? Radarr,
    ComparePickResponse? Bigarr,
    /// <summary>Other picks made for the version while Radarr's download was in flight.</summary>
    List<ComparePickResponse> AlsoPicked,
    /// <summary>Positive when Bigarr picked first: by how many seconds. Null on a one-sided row.</summary>
    long? BigarrLeadSeconds,
    /// <summary>Radarr-only rows: the RSS pass's sighting of Radarr's release (null: never seen in Bigarr's feeds).</summary>
    CompareSightingResponse? Sighting);

public sealed record CompareCounts(int Same, int Different, int BigarrOnly, int RadarrOnly)
{
    public int Total => Same + Different + BigarrOnly + RadarrOnly;
}

public sealed record CompareVersionSummary(string VersionName, CompareCounts Counts);

public sealed record CompareResponse(
    DateTime? From,
    DateTime To,
    int WindowHours,
    bool ShadowEnabled,
    /// <summary>Every version name with a pick or a grab, for the filter.</summary>
    List<string> Versions,
    /// <summary>Over the period and version asked, whatever the outcome filter.</summary>
    CompareCounts Summary,
    List<CompareVersionSummary> ByVersion,
    List<CompareRowResponse> Rows,
    int Total,
    int Page,
    int PageSize);

/// <summary>
/// Activity → Compare: per film and version, what Radarr grabbed next to what Bigarr picked (<see cref="ComparePairing"/>),
/// with the reasons on each side: Bigarr's engine on Radarr's release, Radarr's parse of Bigarr's, and whether Bigarr's
/// feeds ever carried what Radarr took.
/// </summary>
public sealed class CompareService(BigarrDbContext db, IOptions<ShadowOptions> options, TimeProvider time)
{
    public const int DefaultWindowHours = 24;

    public const int MaxWindowHours = 24 * 14;

    /// <param name="tmdbId">One film only: the film page's own comparison.</param>
    public async Task<CompareResponse> CompareAsync(string? version, CompareOutcome? outcome, int days, int windowHours, int page, int pageSize, CancellationToken ct, int? tmdbId = null)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var window = TimeSpan.FromHours(Math.Clamp(windowHours, 1, MaxWindowHours));
        DateTime? from = days > 0 ? now.AddDays(-days) : null;
        var loadFrom = from is { } f ? f - window : DateTime.MinValue;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var grabQuery = db.RadarrGrabs.AsNoTracking().Include(g => g.FollowedRadarr).Where(g => g.GrabbedAt >= loadFrom);
        var pickQuery = db.ShadowPicks.AsNoTracking().Where(p => p.PickedAt >= loadFrom);
        if (!string.IsNullOrWhiteSpace(version))
        {
            grabQuery = grabQuery.Where(g => g.VersionName == version);
            pickQuery = pickQuery.Where(p => p.VersionName == version);
        }

        if (tmdbId is { } film)
        {
            grabQuery = grabQuery.Where(g => g.TmdbId == film);
            pickQuery = pickQuery.Where(p => p.TmdbId == film);
        }

        var grabs = (await grabQuery.ToListAsync(ct)).ToDictionary(g => g.Id);
        var picks = (await pickQuery.ToListAsync(ct)).ToDictionary(p => p.Id);
        var pairs = ComparePairing.Match(
                grabs.Values.Select(g => new ComparePairing.Grab(g.Id, g.TmdbId, g.VersionName, g.GrabbedAt, g.ReleaseGuid, g.InfoHash, g.ReleaseTitle, g.ResolvedAt, g.State == RadarrGrabState.Grabbed)),
                picks.Values.Select(p => new ComparePairing.Pick(p.Id, p.TmdbId, p.VersionName, p.PickedAt, p.ReleaseGuid, p.InfoHash, p.ReleaseTitle)),
                window,
                now)
            .Where(p => from is null || p.At >= from)
            .ToList();

        var summary = Count(pairs);
        var byVersion = pairs
            .GroupBy(p => (p.Radarr?.Version ?? p.Bigarr!.Version))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new CompareVersionSummary(g.Key, Count(g)))
            .ToList();

        var filtered = pairs.Where(p => outcome is null || p.Outcome == outcome).OrderByDescending(p => p.At).ToList();
        var pageRows = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var tmdbIds = pageRows.Select(p => p.Radarr?.TmdbId ?? p.Bigarr!.TmdbId).Distinct().ToList();
        var movies = (await db.Movies.AsNoTracking().Where(m => tmdbIds.Contains(m.TmdbId)).ToListAsync(ct)).ToDictionary(m => m.TmdbId);
        var sightings = await SightingsAsync(pageRows.Where(p => p.Outcome == CompareOutcome.RadarrOnly).Select(p => grabs[p.Radarr!.Id]).ToList(), ct);

        var rows = pageRows.Select(pair =>
        {
            var grab = pair.Radarr is null ? null : grabs[pair.Radarr.Id];
            var pick = pair.Bigarr is null ? null : picks[pair.Bigarr.Id];
            var tmdbId = grab?.TmdbId ?? pick!.TmdbId;
            var movie = movies.GetValueOrDefault(tmdbId);
            var versionName = grab?.VersionName ?? pick!.VersionName;
            return new CompareRowResponse(
                $"{(grab is null ? "p" : $"g{grab.Id}")}-{(pick is null ? "" : $"p{pick.Id}")}",
                pair.Outcome,
                tmdbId,
                movie?.Id ?? grab?.MovieId ?? pick?.MovieId,
                movie?.Title ?? grab?.MovieTitle ?? pick!.MovieTitle,
                movie?.Year,
                movie?.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
                versionName,
                pair.At,
                grab is null ? null : ToResponse(grab),
                pick is null ? null : ToResponse(pick),
                pair.AlsoPicked.Select(p => ToResponse(picks[p.Id])).ToList(),
                grab is not null && pick is not null ? (long)(grab.GrabbedAt - pick.PickedAt).TotalSeconds : null,
                grab is not null && sightings.TryGetValue(grab.Id, out var sighting) ? Sighting(sighting, versionName) : null);
        }).ToList();

        var versions = (await db.ShadowPicks.AsNoTracking().Select(p => p.VersionName).Distinct().ToListAsync(ct))
            .Union(await db.RadarrGrabs.AsNoTracking().Select(g => g.VersionName).Distinct().ToListAsync(ct))
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        return new CompareResponse(from, now, (int)window.TotalHours, options.Value.Enabled, versions, summary, byVersion, rows, filtered.Count, page, pageSize);
    }

    private static CompareCounts Count(IEnumerable<ComparePairing.Pair> pairs)
    {
        var list = pairs.ToList();
        return new CompareCounts(
            list.Count(p => p.Outcome == CompareOutcome.Same),
            list.Count(p => p.Outcome == CompareOutcome.Different),
            list.Count(p => p.Outcome == CompareOutcome.BigarrOnly),
            list.Count(p => p.Outcome == CompareOutcome.RadarrOnly));
    }

    /// <summary>The first sighting of each grab's release in Bigarr's feeds: same guid, same info hash, or same title.</summary>
    private async Task<Dictionary<int, ReleaseSighting>> SightingsAsync(List<RadarrGrab> grabs, CancellationToken ct)
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
        sighting.TmdbId is not null,
        sighting.UnmatchedReason,
        sighting.AttributedVersion,
        sighting.Verdicts.FirstOrDefault(v => v.VersionName == versionName));

    private static ComparePickResponse ToResponse(ShadowPick pick) => new(
        pick.Id, pick.PickedAt, pick.LastPickedAt, pick.Picks, pick.Trigger, pick.ReleaseTitle, pick.IndexerName, pick.Size, pick.Quality, pick.ReleaseGroup,
        pick.Languages, pick.CustomFormats, pick.CustomFormatScore, pick.PublishDate, pick.RadarrView);

    private static CompareGrabResponse ToResponse(RadarrGrab grab) => new(
        grab.Id, grab.FollowedRadarr.Name, grab.GrabbedAt, grab.ReleaseTitle, grab.IndexerName, grab.Size, grab.Quality, grab.ReleaseGroup, grab.Languages,
        grab.CustomFormats, grab.CustomFormatScore, grab.PublishedAt, grab.ReleaseSource, grab.State, grab.ResolvedAt, grab.BigarrVerdict);
}
