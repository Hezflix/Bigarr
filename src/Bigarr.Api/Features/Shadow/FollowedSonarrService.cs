using Bigarr.Api.Features.Migration;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.SonarrImport;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Shadow;

/// <summary>
/// Settings → Followed Sonarrs: the Sonarrs Bigarr follows in shadow mode, each mapped to a series version with the import's
/// mapping, the twin of <see cref="FollowedRadarrService"/>. The API key is stored like an indexer's and only ever sent
/// back masked; a masked or blank key on update keeps the stored one.
/// </summary>
public sealed class FollowedSonarrService(
    BigarrDbContext db,
    SonarrClient sonarr,
    SonarrImportService imports,
    SonarrFollower follower,
    SonarrFollowWakeups wakeups,
    TimeProvider time,
    ILogger<FollowedSonarrService> logger)
{
    public const string KeyMask = FollowedRadarrService.KeyMask;

    public const int MaxNameLength = 64;

    public async Task<List<FollowedSonarrResponse>> ListAsync(CancellationToken ct)
    {
        var rows = await db.FollowedSonarrs.AsNoTracking().OrderBy(f => f.Name).ToListAsync(ct);
        var counts = await CountsAsync(ct);
        return rows.Select(r => ToResponse(r, counts)).ToList();
    }

    public async Task<FollowedSonarrResponse?> GetAsync(int id, CancellationToken ct)
    {
        var row = await db.FollowedSonarrs.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        return row is null ? null : ToResponse(row, await CountsAsync(ct));
    }

    public async Task<Result<FollowedSonarrResponse>> AddAsync(FollowedSonarrRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(request, apiKeyRequired: true, ct) is { } invalid)
        {
            return invalid;
        }

        var name = request.Name.Trim();
        if (await db.FollowedSonarrs.AnyAsync(f => f.Name == name, ct))
        {
            return Failure.Conflict("Name already used", $"A followed Sonarr named '{name}' exists.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        // Sonarr's history is read from now on: what happened before is the import's business.
        var follow = new FollowedSonarr { Name = name, Url = request.Url.Trim(), ApiKey = request.ApiKey!.Trim(), VersionName = request.VersionName.Trim(), HistoryCheckpoint = now, CreatedAt = now };
        Apply(follow, request);
        db.FollowedSonarrs.Add(follow);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Following Sonarr {Name} as version {Version}", follow.Name, follow.VersionName);
        return Result<FollowedSonarrResponse>.Ok(ToResponse(follow, await CountsAsync(ct)));
    }

    public async Task<Result<FollowedSonarrResponse>> UpdateAsync(int id, FollowedSonarrRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(request, apiKeyRequired: false, ct) is { } invalid)
        {
            return invalid;
        }

        var follow = await db.FollowedSonarrs.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (follow is null)
        {
            return Failure.NotFound("Followed Sonarr");
        }

        var name = request.Name.Trim();
        if (await db.FollowedSonarrs.AnyAsync(f => f.Id != id && f.Name == name, ct))
        {
            return Failure.Conflict("Name already used", $"A followed Sonarr named '{name}' exists.");
        }

        follow.Name = name;
        follow.Url = request.Url.Trim();
        follow.VersionName = request.VersionName.Trim();
        if (KeyGiven(request.ApiKey))
        {
            follow.ApiKey = request.ApiKey!.Trim();
        }

        Apply(follow, request);
        await db.SaveChangesAsync(ct);
        return Result<FollowedSonarrResponse>.Ok(ToResponse(follow, await CountsAsync(ct)));
    }

    /// <summary>Forgets the Sonarr and its grabs; Bigarr's picks and the series the follow brought in stay.</summary>
    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct)
    {
        var deleted = await db.FollowedSonarrs.Where(f => f.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Failure.NotFound("Followed Sonarr") : Result<bool>.Ok(true);
    }

    /// <summary>Proves the URL and key of an unsaved form, or of a saved Sonarr (then a masked key means the stored one).</summary>
    public async Task<Result<FollowedSonarrTestResponse>> TestAsync(FollowedSonarrRequest? request, int? id, CancellationToken ct)
    {
        if (await ConnectionAsync(request, id, ct) is not { } connection)
        {
            return id is null ? Failure.Invalid("Sonarr URL and API key are required") : Failure.NotFound("Followed Sonarr");
        }

        try
        {
            var status = await sonarr.StatusAsync(connection, ct);
            var series = await sonarr.SeriesAsync(connection, ct);
            return Result<FollowedSonarrTestResponse>.Ok(new FollowedSonarrTestResponse(status.AppName, status.Version, series.Count));
        }
        catch (SonarrUnavailableException ex)
        {
            return new Failure(StatusCodes.Status502BadGateway, "Sonarr did not answer", ex.Message);
        }
    }

    /// <summary>The import's preview (profiles, root folders, suggestions) for a saved Sonarr, with its stored key.</summary>
    public async Task<Result<SonarrImportPreviewResponse>> PreviewAsync(int id, CancellationToken ct)
    {
        var follow = await db.FollowedSonarrs.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        return follow is null ? Failure.NotFound("Followed Sonarr") : await imports.PreviewAsync(new SonarrImportPreviewRequest(follow.Url, follow.ApiKey), ct);
    }

    /// <summary>A pass now, whatever the schedule says: it reads the whole series list and every series' episodes.</summary>
    public async Task<Result<SonarrFollowPass>> SyncAsync(int id, CancellationToken ct)
    {
        if (!await db.FollowedSonarrs.AnyAsync(f => f.Id == id, ct))
        {
            return Failure.NotFound("Followed Sonarr");
        }

        var pass = await follower.SyncAsync(id, ct, fullList: true, everySeries: true);
        return pass.Success ? Result<SonarrFollowPass>.Ok(pass) : new Failure(StatusCodes.Status502BadGateway, $"{pass.Name} could not be followed", pass.Summary);
    }

    /// <summary>
    /// Sonarr's webhook called (Sonarr → Settings → Connect → Webhook): the event is noted, and anything but Sonarr's
    /// <c>Test</c> wakes a pass. A series added or deleted is not in Sonarr's history: those events ask the pass to read the
    /// whole series list. The body is not trusted for anything else: the pass reads Sonarr's history as always.
    /// </summary>
    public async Task<Result<bool>> WebhookAsync(int id, string? eventType, CancellationToken ct)
    {
        var follow = await db.FollowedSonarrs.AsNoTracking().Where(f => f.Id == id).Select(f => new { f.Name, f.Enabled }).FirstOrDefaultAsync(ct);
        if (follow is null)
        {
            return Failure.NotFound("Followed Sonarr");
        }

        var name = string.IsNullOrWhiteSpace(eventType) ? "Unknown" : eventType.Trim();
        name = name.Length <= 64 ? name : name[..64];
        var now = time.GetUtcNow().UtcDateTime;
        await db.FollowedSonarrs.Where(f => f.Id == id).ExecuteUpdateAsync(s => s.SetProperty(f => f.LastWebhookAt, now).SetProperty(f => f.LastWebhookEvent, name), ct);

        var wakes = follow.Enabled && !string.Equals(name, "Test", StringComparison.OrdinalIgnoreCase);
        if (wakes)
        {
            wakeups.Wake(id, fullList: ReadsSeriesList(name));
        }

        logger.LogInformation("Sonarr webhook {Event} for {Name}{Wakes}", name, follow.Name, wakes ? ": a pass follows" : string.Empty);
        return Result<bool>.Ok(wakes);
    }

    /// <summary>The events that change which series Sonarr has, or files its history may not carry.</summary>
    private static bool ReadsSeriesList(string eventType) =>
        eventType.Equals("SeriesAdd", StringComparison.OrdinalIgnoreCase)
        || eventType.Equals("SeriesDelete", StringComparison.OrdinalIgnoreCase)
        || eventType.Equals("EpisodeFileDelete", StringComparison.OrdinalIgnoreCase);

    private async Task<SonarrConnection?> ConnectionAsync(FollowedSonarrRequest? request, int? id, CancellationToken ct)
    {
        string? storedKey = null;
        string? storedUrl = null;
        if (id is not null)
        {
            var saved = await db.FollowedSonarrs.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
            if (saved is null)
            {
                return null;
            }

            storedKey = saved.ApiKey;
            storedUrl = saved.Url;
        }

        var url = string.IsNullOrWhiteSpace(request?.Url) ? storedUrl : request.Url.Trim();
        var key = KeyGiven(request?.ApiKey) ? request!.ApiKey!.Trim() : storedKey;
        return string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key) ? null : new SonarrConnection(url, key);
    }

    private async Task<Failure?> ValidateAsync(FollowedSonarrRequest request, bool apiKeyRequired, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MaxNameLength)
        {
            return Failure.Invalid("A name is required", $"At most {MaxNameLength} characters.");
        }

        if (!Uri.TryCreate(request.Url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Failure.Invalid("Invalid URL", "The URL must be absolute and start with http:// or https://.");
        }

        if (apiKeyRequired && !KeyGiven(request.ApiKey))
        {
            return Failure.Invalid("An API key is required", "Sonarr → Settings → General → API Key.");
        }

        // The mapping is the import's: same checks on the version name, the profiles and the (series) root folders.
        return await imports.ValidateAsync(new SonarrImportRequest(request.Url!, KeyMask, request.VersionName, request.ProfileMap ?? [], request.RootFolderMap ?? []), ct);
    }

    private static bool KeyGiven(string? key) => !string.IsNullOrWhiteSpace(key) && key.Trim() != KeyMask;

    private static void Apply(FollowedSonarr follow, FollowedSonarrRequest request)
    {
        follow.ProfileMap = new Dictionary<int, int>(request.ProfileMap ?? []);
        follow.RootFolderMap = new Dictionary<string, int>(request.RootFolderMap ?? [], StringComparer.Ordinal);
        follow.ImportTags = request.ImportTags;
        follow.KeepMonitored = request.KeepMonitored;
        follow.Enabled = request.Enabled;
    }

    private async Task<Dictionary<int, (int Grabs, int InFlight)>> CountsAsync(CancellationToken ct) =>
        (await db.SonarrGrabs.AsNoTracking()
            .GroupBy(g => g.FollowedSonarrId)
            .Select(g => new { Id = g.Key, Grabs = g.Count(), InFlight = g.Count(x => x.State == RadarrGrabState.Grabbed) })
            .ToListAsync(ct))
        .ToDictionary(c => c.Id, c => (c.Grabs, c.InFlight));

    private static FollowedSonarrResponse ToResponse(FollowedSonarr follow, Dictionary<int, (int Grabs, int InFlight)> counts)
    {
        var (grabs, inFlight) = counts.GetValueOrDefault(follow.Id);
        return new FollowedSonarrResponse(
            follow.Id,
            follow.Name,
            follow.Url,
            KeyMask,
            follow.VersionName,
            follow.ProfileMap,
            follow.RootFolderMap,
            follow.ImportTags,
            follow.KeepMonitored,
            follow.Enabled,
            follow.HistoryCheckpoint,
            follow.LastSyncAt,
            follow.LastSummary,
            follow.LastError,
            follow.LastErrorAt,
            follow.LastWebhookAt,
            follow.LastWebhookEvent,
            grabs,
            inFlight,
            follow.CreatedAt);
    }
}
