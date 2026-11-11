using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Downloads;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.Events;
using Bigarr.Domain.History;
using Bigarr.Domain.Movies;
using Bigarr.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Downloads;

/// <summary>
/// Grab (blueprint §5): send the release to a download client, then write the durable mapping (<see cref="Grab"/> +
/// <see cref="GrabTarget"/>) and the journal entry in one transaction. Interactive: the user decided, the engine's
/// verdict is not re-checked here; only "one active download per target" is. In shadow mode every path ends in a
/// recorded pick instead (<see cref="ShadowPickRecorder"/>): no client is called, whatever is configured.
/// </summary>
public sealed class GrabService(
    BigarrDbContext db,
    DownloadClientResolver resolver,
    QueueState queue,
    OutboxWriter outbox,
    CustomFormatScorer scorer,
    ShadowPickRecorder picks,
    SeriesShadowPickRecorder seriesPicks,
    IOptions<ShadowOptions> shadow,
    TimeProvider time,
    ILogger<GrabService> logger)
{
    /// <summary>The user picked the release in the interactive search.</summary>
    public const string InteractiveTrigger = "interactive";

    public Task<Result<GrabResponse>> GrabAsync(GrabRequest request, CancellationToken ct) => GrabAsync(request, InteractiveTrigger, ct);

    /// <param name="trigger">Who decided: <c>interactive</c>, <c>rss</c>, <c>search:user</c>… Kept on the journal and the event.</param>
    public async Task<Result<GrabResponse>> GrabAsync(GrabRequest request, string trigger, CancellationToken ct)
    {
        var invalid = Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        if (request.IsSeries)
        {
            return await GrabEpisodesAsync(request, trigger, ct);
        }

        // Folder, tags, file and format scores feed the event's snapshot.
        var version = await db.MovieVersions
            .Include(v => v.Movie)
            .Include(v => v.QualityProfile).ThenInclude(p => p.FormatScores)
            .Include(v => v.RootFolder)
            .Include(v => v.Tags)
            .Include(v => v.File)
            .FirstOrDefaultAsync(v => v.Id == request.VersionId, ct);
        if (version is null)
        {
            return Failure.NotFound("Version");
        }

        if (await db.GrabTargets.AnyAsync(t => t.TargetId == version.Id && t.Active, ct))
        {
            return Failure.Conflict("Already downloading", $"Version {version.Name} of {version.Movie.Title} already has an active download. Remove it from the queue first.");
        }

        if (shadow.Value.Enabled)
        {
            var pick = await picks.RecordAsync(version, request, trigger, ct);
            return Result<GrabResponse>.Ok(new GrabResponse(null, null, null, null, null, pick.PickedAt, pick.Id));
        }

        var clients = await db.DownloadClients.AsNoTracking().Where(c => c.Enabled).OrderBy(c => c.Priority).ThenBy(c => c.Id).ToListAsync(ct);
        if (clients.Count == 0)
        {
            return Failure.Invalid("No download client", "No enabled download client. Add one in Settings → Download clients.");
        }

        var indexer = await db.Indexers.AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.IndexerId, ct);
        var download = new DownloadRequest(
            request.Title,
            request.DownloadUrl,
            request.MagnetUrl,
            request.InfoHash,
            request.Size,
            indexer?.SeedRatio,
            indexer?.SeedTimeMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null);

        DownloadClient? chosen = null;
        string? downloadId = null;
        var errors = new List<string>();
        foreach (var client in clients)
        {
            try
            {
                downloadId = await resolver.For(client).AddAsync(client, download, ct);
                chosen = client;
                break;
            }
            catch (DownloadClientException ex)
            {
                logger.LogWarning("Download client {Client} refused {Title} ({Kind}): {Message}", client.Name, request.Title, ex.Kind, ex.Message);
                errors.Add($"{client.Name}: {ex.Message}");
                if (ex.Kind == DownloadClientFailureKind.InvalidRelease)
                {
                    // Another client would not make the release any better.
                    return new Failure(StatusCodes.Status400BadRequest, "Release cannot be downloaded", ex.Message);
                }
            }
        }

        if (chosen is null || downloadId is null)
        {
            return new Failure(StatusCodes.Status502BadGateway, "No download client accepted the release", string.Join(" · ", errors));
        }

        var now = time.GetUtcNow().UtcDateTime;
        var grab = new Grab
        {
            IndexerId = indexer?.Id,
            IndexerName = indexer?.Name ?? request.Indexer,
            DownloadClientId = chosen.Id,
            DownloadClientName = chosen.Name,
            DownloadId = downloadId,
            ReleaseTitle = request.Title,
            ReleaseGuid = request.Guid,
            InfoHash = downloadId,
            Size = request.Size,
            IndexerFlags = request.Flags,
            Protocol = request.Protocol,
            GrabbedAt = now,
        };
        grab.AddTarget(version);
        db.Grabs.Add(grab);
        var history = HistoryFor(grab, version, request, chosen, now);
        history.Data["trigger"] = trigger;
        db.History.Add(history);

        // Two saves in one transaction: the event carries the grab id, which only exists after the first insert.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // The partial unique index refused a second active grab that slipped in between the check and the write.
            await transaction.RollbackAsync(ct);
            logger.LogWarning(ex, "Grab of {Title} for version {Version} refused by the database; removing it from {Client}", request.Title, version.Id, chosen.Name);
            await TryRemoveAsync(chosen, downloadId, ct);
            return Failure.Conflict("Already downloading", $"Version {version.Name} of {version.Movie.Title} already has an active download.");
        }

        outbox.Publish(new ReleaseGrabbed(
            MovieRef.From(version.Movie),
            VersionRef.From(version),
            EventSnapshots.Release(request.Title, grab.IndexerName, request.Size, request.Flags, version.Movie, version.QualityProfile, await scorer.CalculatorAsync(ct), request.Seeders, downloadId),
            grab.Id,
            chosen.Name,
            downloadId,
            trigger,
            chosen.Implementation.ToString())
        { OccurredAt = now });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        queue.RequestRefresh();
        logger.LogInformation("Grabbed {Title} for {Movie} [{Version}] → {Client} ({DownloadId}, {Trigger})", request.Title, version.Movie.Title, version.Name, chosen.Name, downloadId, trigger);
        return Result<GrabResponse>.Ok(new GrabResponse(grab.Id, downloadId, chosen.Id, chosen.Name, grab.State, grab.GrabbedAt));
    }

    /// <summary>
    /// A series release: one grab, one <see cref="GrabTarget"/> per (version, episode) pair it covers, sent with the
    /// client's series category. In shadow mode a series pick is recorded instead and nothing is downloaded.
    /// </summary>
    private async Task<Result<GrabResponse>> GrabEpisodesAsync(GrabRequest request, string trigger, CancellationToken ct)
    {
        var version = await db.SeriesVersions
            .Include(v => v.Series)
            .Include(v => v.QualityProfile).ThenInclude(p => p.FormatScores)
            .FirstOrDefaultAsync(v => v.Id == request.VersionId, ct);
        if (version is null)
        {
            return Failure.NotFound("Series version");
        }

        var episodeIds = request.EpisodeIds!.Distinct().ToList();
        var targets = await db.EpisodeTargets
            .Include(t => t.Episode)
            .Where(t => t.VersionId == version.Id && episodeIds.Contains(t.EpisodeId))
            .ToListAsync(ct);
        if (targets.Count != episodeIds.Count)
        {
            var missing = episodeIds.Except(targets.Select(t => t.EpisodeId)).ToList();
            return Failure.Invalid("Unknown episodes", $"Episode(s) {string.Join(", ", missing)} are not episodes of {version.Series.Title} [{version.Name}].");
        }

        var targetIds = targets.Select(t => t.Id).ToList();
        if (await db.GrabTargets.AnyAsync(t => targetIds.Contains(t.TargetId) && t.Active, ct))
        {
            return Failure.Conflict("Already downloading", $"An episode of this release already has an active download for {version.Series.Title} [{version.Name}].");
        }

        foreach (var target in targets)
        {
            target.Version = version;
        }

        if (shadow.Value.Enabled)
        {
            var pick = await seriesPicks.RecordAsync(version, targets, request, trigger, ct);
            return Result<GrabResponse>.Ok(new GrabResponse(null, null, null, null, null, pick.PickedAt, pick.Id));
        }

        var clients = await db.DownloadClients.AsNoTracking().Where(c => c.Enabled).OrderBy(c => c.Priority).ThenBy(c => c.Id).ToListAsync(ct);
        if (clients.Count == 0)
        {
            return Failure.Invalid("No download client", "No enabled download client. Add one in Settings → Download clients.");
        }

        var ordered = targets.OrderBy(t => t.Episode.SeasonNumber).ThenBy(t => t.Episode.EpisodeNumber).ToList();
        var labels = ordered.Select(t => EpisodeLabel(t.Episode)).ToList();
        var indexer = await db.Indexers.AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.IndexerId, ct);
        var (chosen, downloadId, failure) = await SendAsync(clients, client => new DownloadRequest(
            request.Title,
            request.DownloadUrl,
            request.MagnetUrl,
            request.InfoHash,
            request.Size,
            indexer?.SeedRatio,
            indexer?.SeedTimeMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null,
            client.SeriesCategory,
            labels), request.Title, ct);
        if (failure is not null)
        {
            return failure;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var grab = new Grab
        {
            IndexerId = indexer?.Id,
            IndexerName = indexer?.Name ?? request.Indexer,
            DownloadClientId = chosen!.Id,
            DownloadClientName = chosen.Name,
            DownloadId = downloadId,
            ReleaseTitle = request.Title,
            ReleaseGuid = request.Guid,
            InfoHash = downloadId,
            Size = request.Size,
            IndexerFlags = request.Flags,
            Protocol = request.Protocol,
            GrabbedAt = now,
        };
        foreach (var target in ordered)
        {
            grab.AddTarget(target);
            target.SearchAttempts = 0;
            var history = SeriesHistory(version, target, HistoryEventType.Grabbed, request.Title, grab.IndexerId, grab.IndexerName, now);
            history.Data["downloadClient"] = chosen.Name;
            history.Data["downloadId"] = downloadId!;
            history.Data["size"] = request.Size.ToString(System.Globalization.CultureInfo.InvariantCulture);
            history.Data["releaseGuid"] = request.Guid;
            history.Data["trigger"] = trigger;
            if (ordered.Count > 1)
            {
                history.Data["episodes"] = string.Join(", ", labels);
            }

            if (request.Seeders is { } seeders)
            {
                history.Data["seeders"] = seeders.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (EpisodeQualityOf(request.Title) is { } quality)
            {
                history.Data["quality"] = quality;
            }

            db.History.Add(history);
        }

        db.Grabs.Add(grab);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Grab of {Title} for series version {Version} refused by the database; removing it from {Client}", request.Title, version.Id, chosen.Name);
            await TryRemoveAsync(chosen, downloadId!, ct);
            return Failure.Conflict("Already downloading", $"An episode of this release already has an active download for {version.Series.Title} [{version.Name}].");
        }

        queue.RequestRefresh();
        logger.LogInformation("Grabbed {Title} for {Series} [{Version}] ({Episodes}) → {Client} ({DownloadId}, {Trigger})", request.Title, version.Series.Title, version.Name, string.Join(", ", labels), chosen.Name, downloadId, trigger);
        return Result<GrabResponse>.Ok(new GrabResponse(grab.Id, downloadId, chosen.Id, chosen.Name, grab.State, grab.GrabbedAt));
    }

    /// <summary>Tries the enabled clients in priority order; the first that accepts the release wins.</summary>
    private async Task<(DownloadClient? Client, string? DownloadId, Failure? Failure)> SendAsync(List<DownloadClient> clients, Func<DownloadClient, DownloadRequest> build, string title, CancellationToken ct)
    {
        var errors = new List<string>();
        foreach (var client in clients)
        {
            try
            {
                var downloadId = await resolver.For(client).AddAsync(client, build(client), ct);
                return (client, downloadId, null);
            }
            catch (DownloadClientException ex)
            {
                logger.LogWarning("Download client {Client} refused {Title} ({Kind}): {Message}", client.Name, title, ex.Kind, ex.Message);
                errors.Add($"{client.Name}: {ex.Message}");
                if (ex.Kind == DownloadClientFailureKind.InvalidRelease)
                {
                    return (null, null, new Failure(StatusCodes.Status400BadRequest, "Release cannot be downloaded", ex.Message));
                }
            }
        }

        return (null, null, new Failure(StatusCodes.Status502BadGateway, "No download client accepted the release", string.Join(" · ", errors)));
    }

    /// <summary><c>S01E05</c>; specials are season 0.</summary>
    public static string EpisodeLabel(Bigarr.Domain.Tv.Episode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}";
    }

    /// <summary>A journal line about one episode of a series version, with the snapshots that outlive a deletion.</summary>
    public static HistoryEvent SeriesHistory(Bigarr.Domain.Tv.SeriesVersion version, Bigarr.Domain.Tv.EpisodeTarget target, HistoryEventType type, string? sourceTitle, int? indexerId, string? indexerName, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(target);
        var data = new Dictionary<string, string>();
        if (target.Episode is { } episode)
        {
            data["episode"] = EpisodeLabel(episode);
            data["episodeId"] = episode.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new HistoryEvent
        {
            TargetId = target.Id,
            SeriesId = version.SeriesId,
            TvdbId = version.Series?.TvdbId,
            SeriesTitle = version.Series?.Title,
            VersionName = version.Name,
            EventType = type,
            SourceTitle = sourceTitle,
            IndexerId = indexerId,
            IndexerName = indexerName,
            Data = data,
            CreatedAt = now,
        };
    }

    /// <summary>The quality Sonarr's parser reads in an episode release title; null when it reads none.</summary>
    public static string? EpisodeQualityOf(string title)
    {
        try
        {
            var name = Bigarr.Parsing.Episodes.Parser.ParseTitle(title)?.Quality?.Quality?.Name;
            return string.IsNullOrEmpty(name) || name == "Unknown" ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static Failure? Validate(GrabRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.VersionId <= 0)
        {
            return Failure.Invalid("A version is required");
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Failure.Invalid("A release title is required");
        }

        if (string.IsNullOrWhiteSpace(request.DownloadUrl) && string.IsNullOrWhiteSpace(request.MagnetUrl) && string.IsNullOrWhiteSpace(request.InfoHash))
        {
            return Failure.Invalid("Release has no link", "A download URL, a magnet or an info hash is needed.");
        }

        if (request.Protocol != DownloadProtocol.Torrent)
        {
            return Failure.Invalid("Unsupported protocol", "Only torrents can be grabbed for now.");
        }

        return null;
    }

    private static HistoryEvent HistoryFor(Grab grab, MovieVersion version, GrabRequest request, DownloadClient client, DateTime now)
    {
        var data = new Dictionary<string, string>
        {
            ["downloadClient"] = client.Name,
            ["downloadId"] = grab.DownloadId ?? string.Empty,
            ["size"] = request.Size.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["releaseGuid"] = request.Guid,
        };

        if (request.Seeders is { } seeders)
        {
            data["seeders"] = seeders.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (request.Flags != IndexerFlags.None)
        {
            data["indexerFlags"] = request.Flags.ToString();
        }

        var quality = QualityOf(request.Title);
        if (quality is not null)
        {
            data["quality"] = quality;
        }

        return new HistoryEvent
        {
            TargetId = version.Id,
            MovieId = version.MovieId,
            TmdbId = version.Movie.TmdbId,
            MovieTitle = version.Movie.Title,
            VersionName = version.Name,
            EventType = HistoryEventType.Grabbed,
            SourceTitle = request.Title,
            IndexerId = grab.IndexerId,
            IndexerName = grab.IndexerName,
            Data = data,
            CreatedAt = now,
        };
    }

    /// <summary>The quality the parser reads in a release title, for display; null when the title is not parsable.</summary>
    public static string? QualityOf(string title)
    {
        try
        {
            var name = Parser.ParseMovieTitle(title)?.Quality?.Quality?.Name;
            return string.IsNullOrEmpty(name) || name == "Unknown" ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task TryRemoveAsync(DownloadClient client, string downloadId, CancellationToken ct)
    {
        try
        {
            await resolver.For(client).RemoveAsync(client, downloadId, deleteFiles: true, ct);
        }
        catch (DownloadClientException ex)
        {
            logger.LogWarning("Could not remove {DownloadId} from {Client} after a refused grab: {Message}", downloadId, client.Name, ex.Message);
        }
    }
}
