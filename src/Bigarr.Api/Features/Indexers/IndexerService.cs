using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Indexers;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Indexers;

/// <summary>Validation, mapping and the test action of the Indexers slice.</summary>
public sealed class IndexerService(BigarrDbContext db, IndexerGateway gateway, Search.RssFeedState rss, ILogger<IndexerService> logger)
{
    public const int MaxNameLength = 128;

    public async Task<List<IndexerResponse>> ListAsync(CancellationToken ct)
    {
        var indexers = await db.Indexers.AsNoTracking().OrderBy(i => i.Priority).ThenBy(i => i.Name).ToListAsync(ct);
        return indexers.Select(ToResponse).ToList();
    }

    public async Task<IndexerResponse?> GetAsync(int id, CancellationToken ct)
    {
        var indexer = await db.Indexers.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        return indexer is null ? null : ToResponse(indexer);
    }

    public async Task<Result<IndexerResponse>> AddAsync(IndexerRequest request, CancellationToken ct)
    {
        var invalid = Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        // One row per Prowlarr indexer. Prowlarr adding one that is already here (a row of the former read-only sync, or
        // a mapping it lost) takes that row over: its id becomes Prowlarr's mapping and the settings Prowlarr does not send stay.
        if (request.ProwlarrId is { } prowlarrId
            && await db.Indexers.Where(i => i.ProwlarrId == prowlarrId).Select(i => (int?)i.Id).FirstOrDefaultAsync(ct) is { } linked)
        {
            logger.LogInformation("Prowlarr indexer {ProwlarrId} is already indexer {Id}: updating it instead of adding another", prowlarrId, linked);
            return await UpdateAsync(linked, request, ct);
        }

        if (await db.Indexers.AnyAsync(i => i.Name == request.Name.Trim(), ct))
        {
            return Failure.Conflict("Indexer name already used", $"An indexer named '{request.Name.Trim()}' exists.");
        }

        var indexer = new Indexer { Name = request.Name.Trim() };
        Apply(indexer, request);
        db.Indexers.Add(indexer);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Indexer {Name} ({Implementation}) added", indexer.Name, indexer.Implementation);
        return Result<IndexerResponse>.Ok(ToResponse(indexer));
    }

    public async Task<Result<IndexerResponse>> UpdateAsync(int id, IndexerRequest request, CancellationToken ct)
    {
        var invalid = Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        var indexer = await db.Indexers.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (indexer is null)
        {
            return Failure.NotFound("Indexer");
        }

        if (await db.Indexers.AnyAsync(i => i.Id != id && i.Name == request.Name.Trim(), ct))
        {
            return Failure.Conflict("Indexer name already used", $"An indexer named '{request.Name.Trim()}' exists.");
        }

        if (request.ProwlarrId is { } prowlarrId && await db.Indexers.AnyAsync(i => i.Id != id && i.ProwlarrId == prowlarrId, ct))
        {
            return Failure.Conflict("Prowlarr indexer already linked", $"Another indexer is linked to Prowlarr indexer {prowlarrId}.");
        }

        indexer.Name = request.Name.Trim();
        Apply(indexer, request);
        await db.SaveChangesAsync(ct);
        return Result<IndexerResponse>.Ok(ToResponse(indexer));
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct)
    {
        var indexer = await db.Indexers.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (indexer is null)
        {
            return Failure.NotFound("Indexer");
        }

        db.Indexers.Remove(indexer);
        await db.SaveChangesAsync(ct);
        gateway.Forget(id);
        rss.ForgetIndexer(id);
        logger.LogInformation("Indexer {Name} deleted", indexer.Name);
        return Result<bool>.Ok(true);
    }

    /// <summary>Tests an unsaved configuration, or a saved one when <paramref name="id"/> is given (then the backoff state is reset on success).</summary>
    public async Task<Result<IndexerTestResponse>> TestAsync(IndexerRequest? request, int? id, CancellationToken ct)
    {
        Indexer indexer;
        if (id is not null)
        {
            var saved = await db.Indexers.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
            if (saved is null)
            {
                return Failure.NotFound("Indexer");
            }

            indexer = saved;
        }
        else
        {
            if (request is null)
            {
                return Failure.Invalid("Nothing to test", "Send the indexer configuration in the body.");
            }

            var invalid = Validate(request);
            if (invalid is not null)
            {
                return invalid;
            }

            indexer = new Indexer { Name = request.Name.Trim() };
            Apply(indexer, request);
        }

        try
        {
            var (capabilities, recent) = await gateway.TestAsync(indexer, ct);
            return Result<IndexerTestResponse>.Ok(new IndexerTestResponse(
                capabilities.SupportsMovieSearch,
                capabilities.SupportsImdbSearch,
                capabilities.SupportsTmdbSearch,
                capabilities.Categories.Count,
                recent));
        }
        catch (IndexerException ex)
        {
            logger.LogWarning("Test of indexer {Name} failed ({Kind}): {Message}", indexer.Name, ex.Kind, ex.Message);
            return new Failure(StatusCodes.Status400BadRequest, "Indexer test failed", ex.Message);
        }
    }

    public IndexerResponse ToResponse(Indexer indexer)
    {
        var status = gateway.Status(indexer.Id);
        return new IndexerResponse(
            indexer.Id,
            indexer.Name,
            indexer.Implementation,
            indexer.BaseUrl,
            indexer.ApiPath,
            indexer.ApiKey,
            indexer.Categories,
            indexer.Priority,
            indexer.EnableRss,
            indexer.EnableAutomaticSearch,
            indexer.EnableInteractiveSearch,
            indexer.MinimumSeeders,
            indexer.SeedRatio,
            indexer.SeedTimeMinutes,
            indexer.RateLimitSeconds,
            indexer.Protocol,
            indexer.ProwlarrId,
            new IndexerStatusResponse(status.Healthy, status.Failures, status.DisabledUntil, status.LastError, status.LastFailureAt, status.LastSuccessAt),
            indexer.CreatedAt);
    }

    public static Failure? Validate(IndexerRequest request)
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

        if (request.Implementation == IndexerImplementation.Torznab)
        {
            if (string.IsNullOrWhiteSpace(request.BaseUrl)
                || !Uri.TryCreate(request.BaseUrl.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return Failure.Invalid("Invalid URL", "A Torznab indexer needs an absolute http(s) URL, e.g. http://prowlarr:9696/1.");
            }
        }

        if (request.Priority is < Indexer.MinPriority or > Indexer.MaxPriority)
        {
            return Failure.Invalid("Invalid priority", $"Between {Indexer.MinPriority} (highest) and {Indexer.MaxPriority} (lowest).");
        }

        if (request.MinimumSeeders < 0)
        {
            return Failure.Invalid("Invalid minimum seeders", "Zero or more.");
        }

        if (request.RateLimitSeconds is < 0 or > 3600)
        {
            return Failure.Invalid("Invalid rate limit", "Between 0 and 3600 seconds.");
        }

        if (request.SeedRatio is < 0)
        {
            return Failure.Invalid("Invalid seed ratio");
        }

        if (request.SeedTimeMinutes is < 0)
        {
            return Failure.Invalid("Invalid seed time");
        }

        if (request.Categories is not null && request.Categories.Any(c => c <= 0))
        {
            return Failure.Invalid("Invalid categories", "Newznab category ids are positive integers, e.g. 2000, 2040.");
        }

        return null;
    }

    private static void Apply(Indexer indexer, IndexerRequest request)
    {
        indexer.Implementation = request.Implementation;
        indexer.BaseUrl = request.Implementation == IndexerImplementation.Torznab ? request.BaseUrl!.Trim().TrimEnd('/') : null;
        indexer.ApiPath = string.IsNullOrWhiteSpace(request.ApiPath) ? "/api" : "/" + request.ApiPath.Trim().Trim('/');
        indexer.ApiKey = string.IsNullOrWhiteSpace(request.ApiKey) ? null : request.ApiKey.Trim();
        indexer.Categories = request.Categories is { Count: > 0 } ? request.Categories.Distinct().OrderBy(c => c).ToList() : [.. Indexer.DefaultCategories];
        indexer.Priority = request.Priority;
        indexer.EnableRss = request.EnableRss;
        indexer.EnableAutomaticSearch = request.EnableAutomaticSearch;
        indexer.EnableInteractiveSearch = request.EnableInteractiveSearch;
        indexer.MinimumSeeders = request.MinimumSeeders;
        indexer.SeedRatio = request.SeedRatio;
        indexer.SeedTimeMinutes = request.SeedTimeMinutes;
        indexer.RateLimitSeconds = request.RateLimitSeconds ?? indexer.RateLimitSeconds;
        if (request.ProwlarrId is not null)
        {
            indexer.ProwlarrId = request.ProwlarrId;
        }
    }
}
