using Bigarr.Api.Features.Migration;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.RadarrImport;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Shadow;

/// <summary>
/// Settings → Followed Radarrs: the Radarrs Bigarr follows in shadow mode, each mapped to a version with the import's
/// mapping. The API key is stored like an indexer's and only ever sent back masked; a masked or blank key on update keeps
/// the stored one, like Radarr's own privacy fields.
/// </summary>
public sealed class FollowedRadarrService(BigarrDbContext db, RadarrClient radarr, RadarrImportService imports, RadarrFollower follower, IOptions<ShadowOptions> options, TimeProvider time, ILogger<FollowedRadarrService> logger)
{
    public const string KeyMask = "********";

    public const int MaxNameLength = 64;

    public async Task<ShadowStatusResponse> StatusAsync(CancellationToken ct) => new(
        options.Value.Enabled,
        await db.ShadowPicks.CountAsync(ct),
        await db.ShadowPicks.MaxAsync(p => (DateTime?)p.PickedAt, ct),
        await db.RadarrGrabs.CountAsync(ct),
        await db.ReleaseSightings.CountAsync(ct),
        await ListAsync(ct));

    public async Task<List<FollowedRadarrResponse>> ListAsync(CancellationToken ct)
    {
        var rows = await db.FollowedRadarrs.AsNoTracking().OrderBy(f => f.Name).ToListAsync(ct);
        var counts = await CountsAsync(ct);
        return rows.Select(r => ToResponse(r, counts)).ToList();
    }

    public async Task<FollowedRadarrResponse?> GetAsync(int id, CancellationToken ct)
    {
        var row = await db.FollowedRadarrs.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        return row is null ? null : ToResponse(row, await CountsAsync(ct));
    }

    public async Task<Result<FollowedRadarrResponse>> AddAsync(FollowedRadarrRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(request, apiKeyRequired: true, ct) is { } invalid)
        {
            return invalid;
        }

        var name = request.Name.Trim();
        if (await db.FollowedRadarrs.AnyAsync(f => f.Name == name, ct))
        {
            return Failure.Conflict("Name already used", $"A followed Radarr named '{name}' exists.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        // Radarr's history is read from now on: what happened before is the import's business.
        var follow = new FollowedRadarr { Name = name, Url = request.Url.Trim(), ApiKey = request.ApiKey!.Trim(), VersionName = request.VersionName.Trim(), HistoryCheckpoint = now, CreatedAt = now };
        Apply(follow, request);
        db.FollowedRadarrs.Add(follow);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Following Radarr {Name} at {Url} as version {Version}", follow.Name, follow.Url, follow.VersionName);
        return Result<FollowedRadarrResponse>.Ok(ToResponse(follow, await CountsAsync(ct)));
    }

    public async Task<Result<FollowedRadarrResponse>> UpdateAsync(int id, FollowedRadarrRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(request, apiKeyRequired: false, ct) is { } invalid)
        {
            return invalid;
        }

        var follow = await db.FollowedRadarrs.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (follow is null)
        {
            return Failure.NotFound("Followed Radarr");
        }

        var name = request.Name.Trim();
        if (await db.FollowedRadarrs.AnyAsync(f => f.Id != id && f.Name == name, ct))
        {
            return Failure.Conflict("Name already used", $"A followed Radarr named '{name}' exists.");
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
        return Result<FollowedRadarrResponse>.Ok(ToResponse(follow, await CountsAsync(ct)));
    }

    /// <summary>Forgets the Radarr and its grabs; Bigarr's picks and the films the follow brought in stay.</summary>
    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct)
    {
        var deleted = await db.FollowedRadarrs.Where(f => f.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Failure.NotFound("Followed Radarr") : Result<bool>.Ok(true);
    }

    /// <summary>Proves the URL and key of an unsaved form, or of a saved Radarr (then a masked key means the stored one).</summary>
    public async Task<Result<FollowedRadarrTestResponse>> TestAsync(FollowedRadarrRequest? request, int? id, CancellationToken ct)
    {
        if (await ConnectionAsync(request, id, ct) is not { } connection)
        {
            return id is null ? Failure.Invalid("Radarr URL and API key are required") : Failure.NotFound("Followed Radarr");
        }

        try
        {
            var status = await radarr.StatusAsync(connection, ct);
            var movies = await radarr.MoviesAsync(connection, ct);
            return Result<FollowedRadarrTestResponse>.Ok(new FollowedRadarrTestResponse(status.AppName, status.Version, movies.Count));
        }
        catch (RadarrUnavailableException ex)
        {
            return new Failure(StatusCodes.Status502BadGateway, "Radarr did not answer", ex.Message);
        }
    }

    /// <summary>The import's preview (profiles, root folders, suggestions) for a saved Radarr, with its stored key.</summary>
    public async Task<Result<RadarrImportPreviewResponse>> PreviewAsync(int id, CancellationToken ct)
    {
        var follow = await db.FollowedRadarrs.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        return follow is null ? Failure.NotFound("Followed Radarr") : await imports.PreviewAsync(new RadarrImportPreviewRequest(follow.Url, follow.ApiKey), ct);
    }

    /// <summary>A pass now, whatever the schedule says.</summary>
    public async Task<Result<RadarrFollowPass>> SyncAsync(int id, CancellationToken ct)
    {
        if (!await db.FollowedRadarrs.AnyAsync(f => f.Id == id, ct))
        {
            return Failure.NotFound("Followed Radarr");
        }

        var pass = await follower.SyncAsync(id, ct);
        return pass.Success ? Result<RadarrFollowPass>.Ok(pass) : new Failure(StatusCodes.Status502BadGateway, $"{pass.Name} could not be followed", pass.Summary);
    }

    private async Task<RadarrConnection?> ConnectionAsync(FollowedRadarrRequest? request, int? id, CancellationToken ct)
    {
        string? storedKey = null;
        string? storedUrl = null;
        if (id is not null)
        {
            var saved = await db.FollowedRadarrs.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
            if (saved is null)
            {
                return null;
            }

            storedKey = saved.ApiKey;
            storedUrl = saved.Url;
        }

        var url = string.IsNullOrWhiteSpace(request?.Url) ? storedUrl : request.Url.Trim();
        var key = KeyGiven(request?.ApiKey) ? request!.ApiKey!.Trim() : storedKey;
        return string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key) ? null : new RadarrConnection(url, key);
    }

    private async Task<Failure?> ValidateAsync(FollowedRadarrRequest request, bool apiKeyRequired, CancellationToken ct)
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
            return Failure.Invalid("An API key is required", "Radarr → Settings → General → API Key.");
        }

        // The mapping is the import's: same checks on the version name, the profiles and the root folders.
        return await imports.ValidateAsync(new RadarrImportRequest(request.Url!, KeyMask, request.VersionName, request.ProfileMap ?? [], request.RootFolderMap ?? []), ct);
    }

    private static bool KeyGiven(string? key) => !string.IsNullOrWhiteSpace(key) && key.Trim() != KeyMask;

    private static void Apply(FollowedRadarr follow, FollowedRadarrRequest request)
    {
        follow.ProfileMap = new Dictionary<int, int>(request.ProfileMap ?? []);
        follow.RootFolderMap = new Dictionary<string, int>(request.RootFolderMap ?? [], StringComparer.Ordinal);
        follow.ImportTags = request.ImportTags;
        follow.KeepMonitored = request.KeepMonitored;
        follow.IncludeMoviesWithoutFile = request.IncludeMoviesWithoutFile;
        follow.Enabled = request.Enabled;
    }

    private async Task<Dictionary<int, (int Grabs, int InFlight)>> CountsAsync(CancellationToken ct) =>
        (await db.RadarrGrabs.AsNoTracking()
            .GroupBy(g => g.FollowedRadarrId)
            .Select(g => new { Id = g.Key, Grabs = g.Count(), InFlight = g.Count(x => x.State == RadarrGrabState.Grabbed) })
            .ToListAsync(ct))
        .ToDictionary(c => c.Id, c => (c.Grabs, c.InFlight));

    private static FollowedRadarrResponse ToResponse(FollowedRadarr follow, Dictionary<int, (int Grabs, int InFlight)> counts)
    {
        var (grabs, inFlight) = counts.GetValueOrDefault(follow.Id);
        return new FollowedRadarrResponse(
            follow.Id,
            follow.Name,
            follow.Url,
            KeyMask,
            follow.VersionName,
            follow.ProfileMap,
            follow.RootFolderMap,
            follow.ImportTags,
            follow.KeepMonitored,
            follow.IncludeMoviesWithoutFile,
            follow.Enabled,
            follow.HistoryCheckpoint,
            follow.LastSyncAt,
            follow.LastSummary,
            follow.LastError,
            follow.LastErrorAt,
            grabs,
            inFlight,
            follow.CreatedAt);
    }
}
