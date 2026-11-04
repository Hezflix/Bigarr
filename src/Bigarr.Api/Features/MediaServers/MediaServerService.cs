using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.MediaServers;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.MediaServers;

/// <summary>The media servers Bigarr keeps informed (M8): CRUD, connection test, and a full scan on demand.</summary>
public sealed class MediaServerService(BigarrDbContext db, MediaServerClients clients, TimeProvider time, ILogger<MediaServerService> logger)
{
    public const int MaxNameLength = 64;

    public async Task<List<MediaServerResponse>> ListAsync(CancellationToken ct) =>
        (await db.MediaServers.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct)).Select(ToResponse).ToList();

    public async Task<MediaServerResponse?> GetAsync(int id, CancellationToken ct)
    {
        var server = await db.MediaServers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        return server is null ? null : ToResponse(server);
    }

    public async Task<Result<MediaServerResponse>> AddAsync(MediaServerRequest request, CancellationToken ct)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var name = request.Name.Trim();
        if (await db.MediaServers.AnyAsync(s => s.Name == name, ct))
        {
            return Failure.Conflict("Name already used", $"A media server named '{name}' already exists.");
        }

        var server = new MediaServer { Name = name, Url = request.Url.Trim(), Token = request.Token.Trim(), CreatedAt = time.GetUtcNow().UtcDateTime };
        Apply(server, request);
        db.MediaServers.Add(server);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Media server {Name} ({Kind}) added at {Url}", server.Name, server.Kind, server.Url);
        return Result<MediaServerResponse>.Ok(ToResponse(server));
    }

    public async Task<Result<MediaServerResponse>> UpdateAsync(int id, MediaServerRequest request, CancellationToken ct)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var server = await db.MediaServers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (server is null)
        {
            return Failure.NotFound("Media server");
        }

        var name = request.Name.Trim();
        if (await db.MediaServers.AnyAsync(s => s.Id != id && s.Name == name, ct))
        {
            return Failure.Conflict("Name already used", $"A media server named '{name}' already exists.");
        }

        Apply(server, request);
        await db.SaveChangesAsync(ct);
        return Result<MediaServerResponse>.Ok(ToResponse(server));
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct)
    {
        var deleted = await db.MediaServers.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Failure.NotFound("Media server") : Result<bool>.Ok(true);
    }

    /// <summary>Proves the connection of a saved server or of an unsaved form.</summary>
    public async Task<Result<MediaServerTestResponse>> TestAsync(MediaServerRequest? request, int? id, CancellationToken ct)
    {
        MediaServer server;
        if (request is not null)
        {
            if (Validate(request) is { } invalid)
            {
                return invalid;
            }

            server = new MediaServer { Name = request.Name.Trim(), Url = request.Url.Trim(), Token = request.Token.Trim() };
            Apply(server, request);
        }
        else
        {
            var saved = await db.MediaServers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
            if (saved is null)
            {
                return Failure.NotFound("Media server");
            }

            server = saved;
        }

        try
        {
            var info = await clients.For(server.Kind).TestAsync(server, ct);
            return Result<MediaServerTestResponse>.Ok(new MediaServerTestResponse(info.Name, info.Version, info.MovieLibraries));
        }
        catch (MediaServerException ex)
        {
            return new Failure(StatusCodes.Status502BadGateway, $"{server.Kind} did not accept the connection", ex.Message);
        }
    }

    /// <summary>A full scan of the server's movie libraries, now; the outcome lands on the row like an automatic refresh.</summary>
    public async Task<Result<MediaServerResponse>> RefreshAsync(int id, CancellationToken ct)
    {
        var server = await db.MediaServers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (server is null)
        {
            return Failure.NotFound("Media server");
        }

        var now = time.GetUtcNow().UtcDateTime;
        try
        {
            await clients.For(server.Kind).RefreshAllAsync(server, ct);
            server.LastRefreshAt = now;
            server.LastError = null;
            await db.SaveChangesAsync(ct);
            return Result<MediaServerResponse>.Ok(ToResponse(server));
        }
        catch (MediaServerException ex)
        {
            server.LastError = ex.Message;
            server.LastErrorAt = now;
            await db.SaveChangesAsync(ct);
            return new Failure(StatusCodes.Status502BadGateway, $"{server.Kind} refused the refresh", ex.Message);
        }
    }

    public static Failure? Validate(MediaServerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MaxNameLength)
        {
            return Failure.Invalid("A name is required", $"At most {MaxNameLength} characters.");
        }

        if (!Enum.IsDefined(request.Kind))
        {
            return Failure.Invalid("Unknown media server kind", "Expected plex, jellyfin or emby.");
        }

        if (!Uri.TryCreate(request.Url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Failure.Invalid("Invalid URL", "The URL must be absolute and start with http:// or https://.");
        }

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Failure.Invalid("A token is required", request.Kind == MediaServerKind.Plex ? "The Plex token of an account that can see the server." : "An API key created in the server's dashboard.");
        }

        if ((request.PathMappings ?? []).Any(m => string.IsNullOrWhiteSpace(m.From) || string.IsNullOrWhiteSpace(m.To)))
        {
            return Failure.Invalid("Incomplete path mapping", "Each mapping needs the Bigarr path and the server's path.");
        }

        return null;
    }

    private static void Apply(MediaServer server, MediaServerRequest request)
    {
        server.Name = request.Name.Trim();
        server.Kind = request.Kind;
        server.Url = request.Url.Trim();
        server.Token = request.Token.Trim();
        server.Enabled = request.Enabled;
        server.PathMappings = (request.PathMappings ?? []).Select(m => new PathMapping(m.From.Trim(), m.To.Trim())).ToList();
    }

    public static MediaServerResponse ToResponse(MediaServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        return new MediaServerResponse(
            server.Id,
            server.Name,
            server.Kind,
            server.Url,
            server.Token,
            server.Enabled,
            server.PathMappings.Select(m => new PathMappingDto(m.From, m.To)).ToList(),
            server.LastRefreshAt,
            server.LastError,
            server.LastErrorAt,
            server.IsFailing,
            server.CreatedAt);
    }
}
