using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Downloads;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.History;
using Bigarr.Domain.Movies;
using Bigarr.Parsing;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Downloads;

/// <summary>
/// Grab (blueprint §5): send the release to a download client, then write the durable mapping (<see cref="Grab"/> +
/// <see cref="GrabTarget"/>) and the journal entry in one transaction. Interactive: the user decided, the engine's
/// verdict is not re-checked here; only "one active download per target" is.
/// </summary>
public sealed class GrabService(BigarrDbContext db, DownloadClientResolver resolver, QueueState queue, TimeProvider time, ILogger<GrabService> logger)
{
    public async Task<Result<GrabResponse>> GrabAsync(GrabRequest request, CancellationToken ct)
    {
        var invalid = Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        var version = await db.MovieVersions.Include(v => v.Movie).FirstOrDefaultAsync(v => v.Id == request.VersionId, ct);
        if (version is null)
        {
            return Failure.NotFound("Version");
        }

        if (await db.GrabTargets.AnyAsync(t => t.TargetId == version.Id && t.Active, ct))
        {
            return Failure.Conflict("Already downloading", $"Version {version.Name} of {version.Movie.Title} already has an active download. Remove it from the queue first.");
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
        db.History.Add(HistoryFor(grab, version, request, chosen, now));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // The partial unique index refused a second active grab that slipped in between the check and the write.
            logger.LogWarning(ex, "Grab of {Title} for version {Version} refused by the database; removing it from {Client}", request.Title, version.Id, chosen.Name);
            await TryRemoveAsync(chosen, downloadId, ct);
            return Failure.Conflict("Already downloading", $"Version {version.Name} of {version.Movie.Title} already has an active download.");
        }

        queue.RequestRefresh();
        logger.LogInformation("Grabbed {Title} for {Movie} [{Version}] → {Client} ({DownloadId})", request.Title, version.Movie.Title, version.Name, chosen.Name, downloadId);
        return Result<GrabResponse>.Ok(new GrabResponse(grab.Id, downloadId, chosen.Id, chosen.Name, grab.State, grab.GrabbedAt));
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
