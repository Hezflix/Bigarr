using System.Text.RegularExpressions;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Downloads;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Downloads;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Downloads;

/// <summary>Validation, mapping and the test action of the download clients.</summary>
public sealed partial class DownloadClientService(BigarrDbContext db, DownloadClientResolver resolver, QueueState queue, ILogger<DownloadClientService> logger)
{
    public const int MaxNameLength = 128;

    public const int MaxCategoryLength = 64;

    public async Task<List<DownloadClientResponse>> ListAsync(CancellationToken ct)
    {
        var clients = await db.DownloadClients.AsNoTracking().OrderBy(c => c.Priority).ThenBy(c => c.Name).ToListAsync(ct);
        return clients.Select(ToResponse).ToList();
    }

    public async Task<DownloadClientResponse?> GetAsync(int id, CancellationToken ct)
    {
        var client = await db.DownloadClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return client is null ? null : ToResponse(client);
    }

    public async Task<Result<DownloadClientResponse>> AddAsync(DownloadClientRequest request, CancellationToken ct)
    {
        var invalid = Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        if (await db.DownloadClients.AnyAsync(c => c.Name == request.Name.Trim(), ct))
        {
            return Failure.Conflict("Download client name already used", $"A download client named '{request.Name.Trim()}' exists.");
        }

        var client = new DownloadClient { Name = request.Name.Trim() };
        Apply(client, request);
        db.DownloadClients.Add(client);
        await db.SaveChangesAsync(ct);
        queue.RequestRefresh();
        logger.LogInformation("Download client {Name} ({Implementation}) added", client.Name, client.Implementation);
        return Result<DownloadClientResponse>.Ok(ToResponse(client));
    }

    public async Task<Result<DownloadClientResponse>> UpdateAsync(int id, DownloadClientRequest request, CancellationToken ct)
    {
        var invalid = Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        var client = await db.DownloadClients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null)
        {
            return Failure.NotFound("Download client");
        }

        if (await db.DownloadClients.AnyAsync(c => c.Id != id && c.Name == request.Name.Trim(), ct))
        {
            return Failure.Conflict("Download client name already used", $"A download client named '{request.Name.Trim()}' exists.");
        }

        client.Name = request.Name.Trim();
        Apply(client, request);
        await db.SaveChangesAsync(ct);
        queue.RequestRefresh();
        return Result<DownloadClientResponse>.Ok(ToResponse(client));
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct)
    {
        var client = await db.DownloadClients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null)
        {
            return Failure.NotFound("Download client");
        }

        // Grabs keep the snapshotted name (FK set to null); the tracker flags them as orphaned.
        db.DownloadClients.Remove(client);
        await db.SaveChangesAsync(ct);
        queue.ForgetClient(id);
        queue.RequestRefresh();
        logger.LogInformation("Download client {Name} deleted", client.Name);
        return Result<bool>.Ok(true);
    }

    /// <summary>Tests an unsaved configuration, or a saved one when <paramref name="id"/> is given.</summary>
    public async Task<Result<DownloadClientTestResponse>> TestAsync(DownloadClientRequest? request, int? id, CancellationToken ct)
    {
        DownloadClient client;
        if (id is not null)
        {
            var saved = await db.DownloadClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
            if (saved is null)
            {
                return Failure.NotFound("Download client");
            }

            client = saved;
        }
        else
        {
            if (request is null)
            {
                return Failure.Invalid("Nothing to test", "Send the download client configuration in the body.");
            }

            var invalid = Validate(request);
            if (invalid is not null)
            {
                return invalid;
            }

            client = new DownloadClient { Name = request.Name.Trim() };
            Apply(client, request);
        }

        try
        {
            var info = await resolver.For(client).TestAsync(client, ct);
            return Result<DownloadClientTestResponse>.Ok(new DownloadClientTestResponse(info.Version, info.ApiVersion, info.Category, info.CategoryCreated));
        }
        catch (DownloadClientException ex)
        {
            logger.LogWarning("Test of download client {Name} failed ({Kind}): {Message}", client.Name, ex.Kind, ex.Message);
            return new Failure(StatusCodes.Status400BadRequest, "Download client test failed", ex.Message);
        }
    }

    public static DownloadClientResponse ToResponse(DownloadClient client) => new(
        client.Id,
        client.Name,
        client.Implementation,
        client.Enabled,
        client.Host,
        client.Port,
        client.UseSsl,
        client.UrlBase,
        client.Username,
        client.Password,
        client.Category,
        client.AddPaused,
        client.Priority,
        client.Protocol,
        client.CreatedAt);

    public static Failure? Validate(DownloadClientRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Failure.Invalid("Name is required");
        }

        if (request.Name.Trim().Length > MaxNameLength)
        {
            return Failure.Invalid("Name is too long", $"At most {MaxNameLength} characters.");
        }

        if (!Enum.IsDefined(request.Implementation))
        {
            return Failure.Invalid("Unknown implementation");
        }

        if (request.Implementation == DownloadClientImplementation.QBittorrent)
        {
            var host = request.Host?.Trim();
            if (string.IsNullOrWhiteSpace(host) || Uri.CheckHostName(host) == UriHostNameType.Unknown)
            {
                return Failure.Invalid("Invalid host", "A host name or an address, e.g. qbittorrent or 192.168.1.10, without scheme or port.");
            }

            if (request.Port is < 1 or > 65535)
            {
                return Failure.Invalid("Invalid port", "Between 1 and 65535.");
            }
        }

        if (string.IsNullOrWhiteSpace(request.Category) || request.Category.Trim().Length > MaxCategoryLength || !CategoryRegex().IsMatch(request.Category.Trim()))
        {
            return Failure.Invalid("Invalid category", "Letters, digits, dots, dashes, underscores and spaces; at most 64 characters.");
        }

        if (request.Priority is < DownloadClient.MinPriority or > DownloadClient.MaxPriority)
        {
            return Failure.Invalid("Invalid priority", $"Between {DownloadClient.MinPriority} (tried first) and {DownloadClient.MaxPriority}.");
        }

        return null;
    }

    private static void Apply(DownloadClient client, DownloadClientRequest request)
    {
        var qbt = request.Implementation == DownloadClientImplementation.QBittorrent;
        client.Implementation = request.Implementation;
        client.Enabled = request.Enabled;
        client.Host = qbt ? request.Host!.Trim() : null;
        client.Port = qbt ? request.Port : 0;
        client.UseSsl = qbt && request.UseSsl;
        client.UrlBase = qbt && !string.IsNullOrWhiteSpace(request.UrlBase) ? "/" + request.UrlBase.Trim().Trim('/') : null;
        client.Username = qbt && !string.IsNullOrWhiteSpace(request.Username) ? request.Username.Trim() : null;
        client.Password = qbt && !string.IsNullOrEmpty(request.Password) ? request.Password : null;
        client.Category = request.Category.Trim();
        client.AddPaused = request.AddPaused;
        client.Priority = request.Priority;
    }

    [GeneratedRegex(@"^[A-Za-z0-9._ -]+$")]
    private static partial Regex CategoryRegex();
}
