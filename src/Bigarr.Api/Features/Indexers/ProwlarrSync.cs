using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Api.Infrastructure.Indexers.Prowlarr;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Indexers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Indexers;

public sealed record ProwlarrSyncResult(int Total, int Added, int Updated, int Removed, int Skipped, List<string> SkippedNames);

public sealed record ProwlarrStatusResponse(
    bool Configured,
    /// <summary>Host of the configured Prowlarr, for display; the key never leaves the server.</summary>
    string? Host,
    bool SyncOnStartup,
    DateTime? LastSyncAt,
    string? LastError,
    ProwlarrSyncResult? LastResult);

/// <summary>In-memory outcome of the last sync, shown on the indexers page.</summary>
public sealed class ProwlarrSyncState
{
    private readonly Lock _lock = new();

    public DateTime? LastSyncAt { get; private set; }

    public string? LastError { get; private set; }

    public ProwlarrSyncResult? LastResult { get; private set; }

    public void Succeeded(ProwlarrSyncResult result, DateTime at)
    {
        lock (_lock)
        {
            LastSyncAt = at;
            LastError = null;
            LastResult = result;
        }
    }

    public void Failed(string error, DateTime at)
    {
        lock (_lock)
        {
            LastSyncAt = at;
            LastError = error;
        }
    }
}

/// <summary>
/// Read-only Prowlarr sync (blueprint §4): every torrent indexer Prowlarr knows becomes a Torznab indexer pointing at
/// Prowlarr's per-indexer endpoint, with Prowlarr's key. Rows are matched on <see cref="Indexer.ProwlarrId"/>; the
/// fields Prowlarr owns are refreshed, the ones tuned here (categories, seeders, rate limit, seed criteria) are kept.
/// The proper Prowlarr push (it calling our Radarr-compatible API) is M7.
/// </summary>
public sealed class ProwlarrSyncService(
    BigarrDbContext db,
    IProwlarrClient prowlarr,
    IOptions<ProwlarrOptions> options,
    ProwlarrSyncState state,
    IndexerGateway gateway,
    TimeProvider time,
    ILogger<ProwlarrSyncService> logger)
{
    public ProwlarrStatusResponse Status()
    {
        var settings = options.Value;
        string? host = null;
        if (settings.IsConfigured && Uri.TryCreate(settings.NormalizedBaseUrl, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
        }

        return new ProwlarrStatusResponse(settings.IsConfigured, host, settings.SyncOnStartup, state.LastSyncAt, state.LastError, state.LastResult);
    }

    public async Task<Result<ProwlarrSyncResult>> SyncAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            return Failure.Invalid("Prowlarr is not configured", "Set Bigarr:Prowlarr:BaseUrl and Bigarr:Prowlarr:ApiKey (environment: Bigarr__Prowlarr__BaseUrl, Bigarr__Prowlarr__ApiKey).");
        }

        IReadOnlyList<ProwlarrIndexer> remote;
        try
        {
            remote = await prowlarr.GetIndexersAsync(ct);
        }
        catch (IndexerException ex)
        {
            logger.LogWarning("Prowlarr sync failed ({Kind}): {Message}", ex.Kind, ex.Message);
            state.Failed(ex.Message, time.GetUtcNow().UtcDateTime);
            return new Failure(StatusCodes.Status502BadGateway, "Prowlarr sync failed", ex.Message);
        }

        var result = await ApplyAsync(remote, settings, ct);
        state.Succeeded(result, time.GetUtcNow().UtcDateTime);
        logger.LogInformation("Prowlarr sync: {Total} indexer(s), {Added} added, {Updated} updated, {Removed} removed, {Skipped} skipped",
            result.Total, result.Added, result.Updated, result.Removed, result.Skipped);
        return Result<ProwlarrSyncResult>.Ok(result);
    }

    private async Task<ProwlarrSyncResult> ApplyAsync(IReadOnlyList<ProwlarrIndexer> remote, ProwlarrOptions settings, CancellationToken ct)
    {
        var existing = await db.Indexers.Where(i => i.ProwlarrId != null).ToDictionaryAsync(i => i.ProwlarrId!.Value, ct);
        var takenNames = (await db.Indexers.Where(i => i.ProwlarrId == null).Select(i => i.Name).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var updated = 0;
        var skipped = new List<string>();
        var seen = new HashSet<int>();

        foreach (var item in remote)
        {
            if (!item.IsTorrent)
            {
                skipped.Add($"{item.Name} (usenet)"); // no usenet download client yet
                continue;
            }

            seen.Add(item.Id);
            var baseUrl = $"{settings.NormalizedBaseUrl}/{item.Id}";
            var name = UniqueName(item.Name, takenNames, existing.Values.Where(i => i.ProwlarrId != item.Id).Select(i => i.Name));

            if (existing.TryGetValue(item.Id, out var indexer))
            {
                var wasEnabled = indexer.EnableRss || indexer.EnableAutomaticSearch || indexer.EnableInteractiveSearch;
                indexer.Name = name;
                indexer.BaseUrl = baseUrl;
                indexer.ApiPath = "/api";
                indexer.ApiKey = settings.ApiKey!.Trim();
                indexer.Priority = Math.Clamp(item.Priority, Indexer.MinPriority, Indexer.MaxPriority);
                if (!item.Enable)
                {
                    indexer.EnableRss = indexer.EnableAutomaticSearch = indexer.EnableInteractiveSearch = false;
                }
                else if (!wasEnabled)
                {
                    indexer.EnableRss = item.SupportsRss;
                    indexer.EnableAutomaticSearch = indexer.EnableInteractiveSearch = item.SupportsSearch;
                }

                updated++;
            }
            else
            {
                db.Indexers.Add(new Indexer
                {
                    Name = name,
                    Implementation = IndexerImplementation.Torznab,
                    BaseUrl = baseUrl,
                    ApiPath = "/api",
                    ApiKey = settings.ApiKey!.Trim(),
                    Priority = Math.Clamp(item.Priority, Indexer.MinPriority, Indexer.MaxPriority),
                    EnableRss = item.Enable && item.SupportsRss,
                    EnableAutomaticSearch = item.Enable && item.SupportsSearch,
                    EnableInteractiveSearch = item.Enable && item.SupportsSearch,
                    ProwlarrId = item.Id,
                    CreatedAt = time.GetUtcNow().UtcDateTime,
                });
                added++;
            }

            takenNames.Add(name);
        }

        var gone = existing.Values.Where(i => !seen.Contains(i.ProwlarrId!.Value)).ToList();
        foreach (var indexer in gone)
        {
            db.Indexers.Remove(indexer);
            gateway.Forget(indexer.Id);
        }

        await db.SaveChangesAsync(ct);
        return new ProwlarrSyncResult(remote.Count, added, updated, gone.Count, skipped.Count, skipped);
    }

    /// <summary>Prowlarr names are unique on its side, but a hand-made indexer here may already use one.</summary>
    private static string UniqueName(string wanted, HashSet<string> takenByManual, IEnumerable<string> takenBySiblings)
    {
        var name = string.IsNullOrWhiteSpace(wanted) ? "Prowlarr indexer" : wanted.Trim();
        if (name.Length > IndexerService.MaxNameLength - 12)
        {
            name = name[..(IndexerService.MaxNameLength - 12)].TrimEnd();
        }

        var taken = new HashSet<string>(takenByManual, StringComparer.OrdinalIgnoreCase);
        taken.UnionWith(takenBySiblings);
        if (!taken.Contains(name))
        {
            return name;
        }

        var candidate = $"{name} (Prowlarr)";
        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{name} (Prowlarr {n})";
        }

        return candidate;
    }
}

/// <summary>Imports Prowlarr's indexers once, right after the database is ready, without holding the web host back.</summary>
public sealed class ProwlarrStartupSync(IServiceScopeFactory scopeFactory, IOptions<ProwlarrOptions> options, ILogger<ProwlarrStartupSync> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsConfigured)
        {
            logger.LogInformation("Prowlarr not configured (Bigarr__Prowlarr__BaseUrl / ApiKey): indexers are managed by hand.");
            return;
        }

        if (!options.Value.SyncOnStartup)
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sync = scope.ServiceProvider.GetRequiredService<ProwlarrSyncService>();
            var result = await sync.SyncAsync(stoppingToken);
            if (!result.IsSuccess)
            {
                logger.LogWarning("Prowlarr startup sync skipped: {Detail}", result.Failure!.Detail);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Prowlarr startup sync crashed");
        }
    }
}
