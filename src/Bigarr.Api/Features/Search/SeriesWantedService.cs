using System.Collections.Concurrent;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Search;

public sealed record SeriesWantedItemResponse(
    int TargetId,
    int SeriesId,
    string SeriesTitle,
    int? Year,
    string? PosterUrl,
    int VersionId,
    string VersionName,
    string QualityProfile,
    int EpisodeId,
    int SeasonNumber,
    int EpisodeNumber,
    int? AbsoluteEpisodeNumber,
    string EpisodeTitle,
    DateTime? AirDateUtc,
    WantedFilter Kind,
    string? CurrentQuality,
    int? CurrentScore,
    string CutoffQuality,
    DateTime? LastSearchAt,
    int SearchAttempts,
    /// <summary>When the missing/cutoff-unmet job may search it again; null means at its next pass.</summary>
    DateTime? NextSearchAt);

public sealed record SeriesWantedPageResponse(List<SeriesWantedItemResponse> Items, int Total, int Page, int PageSize);

/// <summary>Search what the list shows: these targets, or every row of a filter (missing, cutoff unmet) when none is given.</summary>
public sealed record SeriesWantedSearchRequest(List<int>? TargetIds, WantedFilter? Filter = null);

/// <summary>The episodes one series version wants searched, and what the backoff knows of them.</summary>
public sealed record SeriesWantedGroup(int VersionId, int SeriesId, List<int> EpisodeIds, DateTime? LastSearchAt, DateTime? LatestAirDateUtc);

/// <summary>
/// What the series library still wants, Sonarr's Wanted pages: per (version, episode) pair, monitored on both sides,
/// aired, without an active download, in a managed version, with no file (missing) or a file below the version's
/// cutoff, quality then custom format score, while upgrades are allowed (cutoff unmet). Computed, not stored, from light
/// rows like the film list (PR #27): SQL keeps the candidate pairs, the cutoff is judged in memory, and an episode file's
/// custom formats are matched only when its score decides, remembered between requests (<see cref="EpisodeFileFormatMatches"/>).
/// Sorted like Sonarr's Wanted: most recently aired first.
/// </summary>
public sealed class SeriesWantedService(BigarrDbContext db, CustomFormatScorer scorer, EpisodeFileFormatMatches matches, IOptions<SearchOptions> options, TimeProvider time)
{
    public async Task<SeriesWantedPageResponse> PageAsync(WantedFilter? filter, int? seriesId, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var list = await ListAsync(filter, seriesId, ct);
        var items = await ItemsAsync(list, list.Rows.Skip((page - 1) * pageSize).Take(pageSize).ToList(), ct);
        return new SeriesWantedPageResponse(items, list.Rows.Count, page, pageSize);
    }

    /// <summary>The wanted rows among <paramref name="targetIds"/> (every one when null), grouped per version for the search queue.</summary>
    public async Task<List<SeriesWantedGroup>> GroupsAsync(IReadOnlyCollection<int>? targetIds, WantedFilter? filter, CancellationToken ct)
    {
        var list = await ListAsync(filter, null, ct);
        var rows = targetIds is null ? list.Rows : list.Rows.Where(r => targetIds.Contains(r.Row.TargetId)).ToList();
        return Group(rows.Select(r => r.Row));
    }

    /// <summary>
    /// What the missing/cutoff-unmet job searches this pass: the episodes whose backoff has elapsed, grouped per version,
    /// the version whose episodes were searched longest ago first (never first), then the most recently aired; bounded.
    /// </summary>
    public async Task<List<SeriesWantedGroup>> DueAsync(int batchSize, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var settings = options.Value;
        var list = await ListAsync(null, null, ct);
        return Group(list.Rows.Select(r => r.Row).Where(r => SearchBackoff.IsDue(r.LastSearchAt, r.SearchAttempts, settings, now)))
            .Take(Math.Max(1, batchSize))
            .ToList();
    }

    private static List<SeriesWantedGroup> Group(IEnumerable<TargetRow> rows) =>
        rows.GroupBy(r => (r.VersionId, r.SeriesId))
            .Select(g => new SeriesWantedGroup(
                g.Key.VersionId,
                g.Key.SeriesId,
                g.OrderBy(r => r.SeasonNumber).ThenBy(r => r.EpisodeNumber).Select(r => r.EpisodeId).ToList(),
                g.Any(r => r.LastSearchAt is null) ? null : g.Min(r => r.LastSearchAt),
                g.Max(r => r.AirDateUtc)))
            .OrderBy(g => g.LastSearchAt ?? DateTime.MinValue)
            .ThenByDescending(g => g.LatestAirDateUtc)
            .ThenBy(g => g.VersionId)
            .ToList();

    private async Task<WantedList> ListAsync(WantedFilter? filter, int? seriesId, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var targets = db.EpisodeTargets.AsNoTracking()
            .Where(t => t.Monitored && t.Version.Monitored && t.Version.Mode == VersionMode.Managed)
            .Where(t => t.Episode.AirDateUtc != null && t.Episode.AirDateUtc <= now)
            .Where(t => !db.GrabTargets.Any(g => g.Active && g.TargetId == t.Id))
            .Where(t => t.EpisodeFileId == null || t.Version.QualityProfile.UpgradeAllowed);
        if (seriesId is { } id)
        {
            targets = targets.Where(t => t.Version.SeriesId == id);
        }

        targets = filter switch
        {
            WantedFilter.Missing => targets.Where(t => t.EpisodeFileId == null),
            WantedFilter.CutoffUnmet => targets.Where(t => t.EpisodeFileId != null),
            _ => targets,
        };

        var rows = await targets
            .Select(t => new TargetRow(
                t.Id,
                t.VersionId,
                t.Version.SeriesId,
                t.EpisodeId,
                t.Episode.SeasonNumber,
                t.Episode.EpisodeNumber,
                t.Episode.AirDateUtc,
                t.Version.QualityProfileId,
                t.LastSearchAt,
                t.SearchAttempts,
                t.EpisodeFile == null
                    ? null
                    : new EpisodeFileRow(
                        t.EpisodeFile.Id,
                        t.EpisodeFile.QualityId,
                        t.EpisodeFile.OriginalReleaseTitle,
                        t.EpisodeFile.RelativePath,
                        t.EpisodeFile.Size,
                        t.EpisodeFile.IndexerFlags,
                        t.EpisodeFile.Languages,
                        t.EpisodeFile.ReleaseGroup,
                        t.Version.Series.Year,
                        t.Version.Series.OriginalLanguage)))
            .ToListAsync(ct);

        var profiles = await db.QualityProfiles.AsNoTracking().Include(p => p.FormatScores).ToDictionaryAsync(p => p.Id, ct);
        var definitions = await db.QualityDefinitions.AsNoTracking().ToDictionaryAsync(q => q.Id, ct);
        var formats = rows.Any(r => r.File is not null) ? (await scorer.CalculatorAsync(ct), await scorer.SignatureAsync(ct)) : default;
        int Score(EpisodeFileRow file, QualityProfile profile) =>
            ProfileRanking.FormatScore(profile, matches.Match(file, definitions.GetValueOrDefault(file.QualityId), formats.Item1!, formats.Item2!));
        var list = new WantedList([], profiles, definitions, Score);

        foreach (var row in rows.OrderByDescending(r => r.AirDateUtc).ThenBy(r => r.SeriesId).ThenBy(r => r.SeasonNumber).ThenBy(r => r.EpisodeNumber).ThenBy(r => r.VersionId))
        {
            if (row.File is null)
            {
                list.Rows.Add(new WantedRow(row, WantedFilter.Missing, null));
                continue;
            }

            var profile = profiles[row.QualityProfileId];
            int? score = null;
            if (ProfileRanking.CutoffMet(profile, row.File.QualityId))
            {
                // At the cutoff quality the score decides, unless no score this profile gives can fall short of it.
                var lowest = profile.FormatScores.Where(s => s.Score < 0).Sum(s => s.Score);
                if (profile.CutoffFormatScore <= lowest)
                {
                    continue;
                }

                score = Score(row.File, profile);
                if (score >= profile.CutoffFormatScore)
                {
                    continue;
                }
            }

            list.Rows.Add(new WantedRow(row, WantedFilter.CutoffUnmet, score));
        }

        if (filter != WantedFilter.Missing)
        {
            matches.Retain((await db.EpisodeFiles.AsNoTracking().Select(f => f.Id).ToListAsync(ct)).ToHashSet());
        }

        return list;
    }

    private async Task<List<SeriesWantedItemResponse>> ItemsAsync(WantedList list, List<WantedRow> rows, CancellationToken ct)
    {
        var episodeIds = rows.Select(r => r.Row.EpisodeId).Distinct().ToList();
        var episodes = await db.Episodes.AsNoTracking().Where(e => episodeIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Title, e.AbsoluteEpisodeNumber }).ToDictionaryAsync(e => e.Id, ct);
        var versionIds = rows.Select(r => r.Row.VersionId).Distinct().ToList();
        var versions = await db.SeriesVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Name, v.SeriesId, v.Series.Title, v.Series.Year, v.Series.Images }).ToDictionaryAsync(v => v.Id, ct);
        var settings = options.Value;

        return rows.Select(w =>
        {
            var row = w.Row;
            var version = versions[row.VersionId];
            var episode = episodes[row.EpisodeId];
            var profile = list.Profiles[row.QualityProfileId];
            return new SeriesWantedItemResponse(
                row.TargetId,
                row.SeriesId,
                version.Title,
                version.Year,
                version.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
                row.VersionId,
                version.Name,
                profile.Name,
                row.EpisodeId,
                row.SeasonNumber,
                row.EpisodeNumber,
                episode.AbsoluteEpisodeNumber,
                episode.Title,
                row.AirDateUtc,
                w.Kind,
                row.File is null ? null : list.Definitions.GetValueOrDefault(row.File.QualityId)?.Name,
                row.File is null ? null : w.Score ?? list.Score(row.File, profile),
                WantedService.CutoffName(profile, list.Definitions),
                row.LastSearchAt,
                row.SearchAttempts,
                SearchBackoff.NextAllowed(row.LastSearchAt, row.SearchAttempts, settings));
        }).ToList();
    }

    private sealed record TargetRow(
        int TargetId,
        int VersionId,
        int SeriesId,
        int EpisodeId,
        int SeasonNumber,
        int EpisodeNumber,
        DateTime? AirDateUtc,
        int QualityProfileId,
        DateTime? LastSearchAt,
        int SearchAttempts,
        EpisodeFileRow? File);

    private sealed record WantedRow(TargetRow Row, WantedFilter Kind, int? Score);

    private sealed record WantedList(List<WantedRow> Rows, Dictionary<int, QualityProfile> Profiles, Dictionary<int, QualityDefinition> Definitions, Func<EpisodeFileRow, QualityProfile, int> Score);
}

/// <summary>What an episode file's custom formats are matched on (<see cref="CustomFormatScorer.EpisodeFileInput"/>).</summary>
public sealed record EpisodeFileRow(
    int Id,
    int QualityId,
    string? OriginalReleaseTitle,
    string RelativePath,
    long Size,
    IndexerFlags IndexerFlags,
    List<string> Languages,
    string? ReleaseGroup,
    int? Year,
    string? OriginalLanguage)
{
    public bool Equals(EpisodeFileRow? other) =>
        other is not null && Id == other.Id && QualityId == other.QualityId && OriginalReleaseTitle == other.OriginalReleaseTitle
        && RelativePath == other.RelativePath && Size == other.Size && IndexerFlags == other.IndexerFlags && ReleaseGroup == other.ReleaseGroup
        && Year == other.Year && OriginalLanguage == other.OriginalLanguage && Languages.SequenceEqual(other.Languages, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Id, QualityId, RelativePath, Size);
}

/// <summary>
/// The custom formats each episode file matched, kept between requests, like <see cref="FileFormatMatches"/> for films:
/// reused only for the same file with the same inputs and the same custom formats.
/// </summary>
public sealed class EpisodeFileFormatMatches
{
    private readonly ConcurrentDictionary<int, (EpisodeFileRow Input, string Signature, int[] Ids)> _entries = new();

    public IReadOnlyList<int> Match(EpisodeFileRow file, QualityDefinition? quality, CustomFormatCalculator calculator, string signature)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(calculator);
        if (_entries.TryGetValue(file.Id, out var entry) && entry.Signature == signature && entry.Input.Equals(file))
        {
            return entry.Ids;
        }

        var ids = calculator.Match(CustomFormatScorer.EpisodeFileInput(
                file.OriginalReleaseTitle, file.RelativePath, file.Size, file.IndexerFlags, quality, file.Languages, file.ReleaseGroup, file.Year, file.OriginalLanguage))
            .Select(m => m.Id).ToArray();
        _entries[file.Id] = (file, signature, ids);
        return ids;
    }

    public void Retain(IReadOnlySet<int> fileIds)
    {
        ArgumentNullException.ThrowIfNull(fileIds);
        foreach (var id in _entries.Keys.Where(id => !fileIds.Contains(id)))
        {
            _entries.TryRemove(id, out _);
        }
    }
}

public static class SeriesWantedEndpoints
{
    public static IEndpointRouteBuilder MapSeriesWanted(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/series/wanted").WithTags("Series").RequireAuthorization(Infrastructure.Auth.AuthPolicies.Api);
        // filter is matched by name, case-insensitive (missing | cutoffUnmet); absent means both.
        group.MapGet("/", async Task<Microsoft.AspNetCore.Http.HttpResults.Results<Microsoft.AspNetCore.Http.HttpResults.Ok<SeriesWantedPageResponse>, Microsoft.AspNetCore.Http.HttpResults.ValidationProblem>> (SeriesWantedService wanted, CancellationToken ct, string? filter = null, int? seriesId = null, int page = 1, int pageSize = 50) =>
        {
            if (!SearchEndpoints.TryParseFilter(filter, out var kind))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["filter"] = [$"Unknown filter '{filter}'."] });
            }

            return TypedResults.Ok(await wanted.PageAsync(kind, seriesId, page, pageSize, ct));
        });

        // The Wanted page's search buttons: queued, searched one version at a time by the series search runner.
        group.MapPost("/search", async (SeriesWantedSearchRequest request, SeriesWantedService wanted, SeriesSearchQueue queue, CancellationToken ct) =>
        {
            var groups = await wanted.GroupsAsync(request.TargetIds, request.Filter, ct);
            var queued = groups.Count(g => queue.TryEnqueue(new SeriesSearchRequest(g.VersionId, g.EpisodeIds, SearchTrigger.User)));
            return TypedResults.Ok(new WantedSearchResponse(queued));
        });
        return app;
    }
}
