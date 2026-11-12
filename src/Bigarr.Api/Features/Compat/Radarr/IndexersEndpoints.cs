using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bigarr.Api.Features.Indexers;
using Bigarr.Domain.Indexers;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary>
/// <c>/indexer</c>: what Prowlarr calls to push its indexers into an application, as it does into Radarr.
/// It reads the schema, posts one Torznab indexer per tracker pointing at its own per-indexer endpoint, and on each sync
/// adopts the rows whose <c>baseUrl</c> starts with its URL or whose <c>apiKey</c> is its key, its indexer id being the
/// <c>/{id}/</c> at the end of <c>baseUrl</c>. Usenet indexers are refused: Bigarr has no usenet client.
/// </summary>
public static partial class IndexersEndpoints
{
    public static void MapCompatIndexers(this RouteGroupBuilder group)
    {
        group.MapGet("/indexer/schema", JsonHttpResult<List<RadarrIndexer>> () => RadarrCompat.Ok(Schema()));
        group.MapGet("/indexer", async Task<JsonHttpResult<List<RadarrIndexer>>> (IndexerService indexers, CancellationToken ct) =>
            RadarrCompat.Ok((await indexers.ListAsync(ct)).Where(i => i.Implementation == IndexerImplementation.Torznab).Select(ToResource).ToList()));

        group.MapGet("/indexer/{id:int}", async Task<Results<JsonHttpResult<RadarrIndexer>, NotFound>> (int id, IndexerService indexers, CancellationToken ct) =>
        {
            var indexer = await indexers.GetAsync(id, ct);
            return indexer is null || indexer.Implementation != IndexerImplementation.Torznab ? TypedResults.NotFound() : RadarrCompat.Ok(ToResource(indexer));
        });

        group.MapPost("/indexer", async Task<Results<JsonHttpResult<RadarrIndexer>, JsonHttpResult<List<RadarrValidationFailure>>>> (RadarrIndexer resource, IndexerService indexers, CancellationToken ct, bool forceSave = false) =>
        {
            var request = ToRequest(resource, out var invalid);
            if (request is null)
            {
                return invalid!;
            }

            // An indexer Prowlarr already has here (same ProwlarrId) is updated and returned: Prowlarr maps onto its id.
            var result = await indexers.AddAsync(request, ct);
            return result.IsSuccess ? RadarrCompat.Created(ToResource(result.Value!)) : RadarrCompat.BadRequest("Name", result.Failure!.Detail ?? result.Failure.Title);
        });

        group.MapPut("/indexer/{id:int}", async Task<Results<JsonHttpResult<RadarrIndexer>, JsonHttpResult<List<RadarrValidationFailure>>, NotFound>> (int id, RadarrIndexer resource, IndexerService indexers, CancellationToken ct, bool forceSave = false) =>
        {
            var request = ToRequest(resource, out var invalid);
            if (request is null)
            {
                return invalid!;
            }

            var result = await indexers.UpdateAsync(id, request, ct);
            if (result.IsSuccess)
            {
                return RadarrCompat.Ok(ToResource(result.Value!));
            }

            return result.Failure!.StatusCode == StatusCodes.Status404NotFound ? TypedResults.NotFound() : RadarrCompat.BadRequest("Name", result.Failure.Detail ?? result.Failure.Title);
        });

        // Radarr answers 200 for an id it does not have, and Prowlarr only drops its mapping on success: an indexer deleted
        // here first must not leave Prowlarr failing on it at every sync.
        group.MapDelete("/indexer/{id:int}", async Task<Ok> (int id, IndexerService indexers, CancellationToken ct) =>
        {
            await indexers.DeleteAsync(id, ct);
            return TypedResults.Ok();
        });

        // Prowlarr's application test: a Newznab indexer pointing at its aggregate endpoint. Only the caps request has
        // to succeed; the version header on the answer is what Prowlarr actually checks.
        group.MapPost("/indexer/test", async Task<Results<Ok, JsonHttpResult<List<RadarrValidationFailure>>>> (RadarrIndexer resource, IndexerService indexers, CancellationToken ct) =>
        {
            var request = ToRequest(resource, out var invalid, allowUsenetForTest: true);
            if (request is null)
            {
                return invalid!;
            }

            var result = await indexers.TestAsync(request, null, ct);
            return result.IsSuccess ? TypedResults.Ok() : RadarrCompat.BadRequest("BaseUrl", result.Failure!.Detail ?? result.Failure.Title);
        });

        group.MapPost("/indexer/testall", Ok () => TypedResults.Ok());
    }

    /// <summary>Both contracts Prowlarr looks up by implementation name; it copies the fields it syncs from here.</summary>
    public static List<RadarrIndexer> Schema() =>
    [
        new RadarrIndexer
        {
            Implementation = "Torznab",
            ImplementationName = "Torznab",
            ConfigContract = "TorznabSettings",
            InfoLink = "https://github.com/Hezflix/Bigarr",
            Protocol = "torrent",
            Fields = Fields(torrent: true),
        },
        new RadarrIndexer
        {
            Implementation = "Newznab",
            ImplementationName = "Newznab",
            ConfigContract = "NewznabSettings",
            InfoLink = "https://github.com/Hezflix/Bigarr",
            Protocol = "usenet",
            Fields = Fields(torrent: false),
        },
    ];

    private static List<RadarrField> Fields(bool torrent)
    {
        var order = 0;
        var fields = new List<RadarrField>
        {
            new() { Order = order++, Name = "baseUrl", Label = "URL", Type = "textbox" },
            new() { Order = order++, Name = "apiPath", Label = "API Path", Value = Element("/api"), Type = "textbox", Advanced = true },
            new() { Order = order++, Name = "apiKey", Label = "API Key", Type = "textbox", Privacy = "apiKey" },
            new() { Order = order++, Name = "categories", Label = "Categories", Value = Element(Indexer.DefaultCategories), Type = "select", SelectOptionsProviderAction = "newznabCategories" },
            new() { Order = order++, Name = "multiLanguages", Label = "Multi Languages", Value = Element(Array.Empty<int>()), Type = "select", Advanced = true },
            new() { Order = order++, Name = "removeYear", Label = "Remove year", Value = Element(false), Type = "checkbox", Advanced = true },
        };

        if (torrent)
        {
            fields.Add(new RadarrField { Order = order++, Name = "minimumSeeders", Label = "Minimum Seeders", Value = Element(1), Type = "number", Advanced = true });
            fields.Add(new RadarrField { Order = order++, Name = "seedCriteria.seedRatio", Label = "Seed Ratio", Type = "textbox", Advanced = true });
            fields.Add(new RadarrField { Order = order++, Name = "seedCriteria.seedTime", Label = "Seed Time", Type = "number", Advanced = true });
            fields.Add(new RadarrField { Order = order++, Name = "rejectBlocklistedTorrentHashesWhileGrabbing", Label = "Reject Blocklisted Torrent Hashes While Grabbing", Value = Element(false), Type = "checkbox", Advanced = true });
        }

        fields.Add(new RadarrField { Order = order, Name = "requiredFlags", Label = "Required Flags", Value = Element(Array.Empty<int>()), Type = "select", Advanced = true });
        return fields;
    }

    public static RadarrIndexer ToResource(IndexerResponse indexer)
    {
        ArgumentNullException.ThrowIfNull(indexer);

        var fields = Fields(torrent: true).Select(f => f.Name switch
        {
            // Prowlarr compares baseUrl textually with "{prowlarr}/{id}/": give the trailing slash back.
            "baseUrl" => f with { Value = Element((indexer.BaseUrl ?? string.Empty).TrimEnd('/') + "/") },
            "apiPath" => f with { Value = Element(indexer.ApiPath) },
            "apiKey" => f with { Value = Element(indexer.ApiKey ?? string.Empty) },
            "categories" => f with { Value = Element(indexer.Categories) },
            "minimumSeeders" => f with { Value = Element(indexer.MinimumSeeders) },
            "seedCriteria.seedRatio" => f with { Value = indexer.SeedRatio is { } ratio ? Element(ratio) : null },
            "seedCriteria.seedTime" => f with { Value = indexer.SeedTimeMinutes is { } minutes ? Element(minutes) : null },
            _ => f,
        }).ToList();

        return new RadarrIndexer
        {
            Id = indexer.Id,
            Name = indexer.Name,
            Implementation = "Torznab",
            ImplementationName = "Torznab",
            ConfigContract = "TorznabSettings",
            EnableRss = indexer.EnableRss,
            EnableAutomaticSearch = indexer.EnableAutomaticSearch,
            EnableInteractiveSearch = indexer.EnableInteractiveSearch,
            Protocol = "torrent",
            Priority = indexer.Priority,
            Fields = fields,
        };
    }

    /// <summary>Prowlarr's resource back into the slice's request; null with the failure to answer when it cannot be.</summary>
    public static IndexerRequest? ToRequest(RadarrIndexer resource, out JsonHttpResult<List<RadarrValidationFailure>>? invalid, bool allowUsenetForTest = false)
    {
        ArgumentNullException.ThrowIfNull(resource);
        invalid = null;

        var isTorznab = string.Equals(resource.Implementation, "Torznab", StringComparison.OrdinalIgnoreCase);
        var isNewznab = string.Equals(resource.Implementation, "Newznab", StringComparison.OrdinalIgnoreCase);
        if (!isTorznab && !(isNewznab && allowUsenetForTest))
        {
            invalid = RadarrCompat.BadRequest("Implementation", isNewznab
                ? "Bigarr has no usenet download client: Newznab indexers are not accepted. Sync only torrent indexers to this application."
                : $"Unknown indexer implementation '{resource.Implementation}'.");
            return null;
        }

        var baseUrl = Field(resource, "baseUrl").String();
        var apiKey = Field(resource, "apiKey").String();
        var categories = Field(resource, "categories").Ints();
        var minimumSeeders = Field(resource, "minimumSeeders").Int() ?? 1;
        var seedRatio = Field(resource, "seedCriteria.seedRatio").Double();
        var seedTime = Field(resource, "seedCriteria.seedTime").Int();
        var apiPath = Field(resource, "apiPath").String();

        int? prowlarrId = null;
        if (baseUrl is not null && ProwlarrIndexerRegex().Match(baseUrl) is { Success: true } match)
        {
            prowlarrId = int.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture);
        }

        return new IndexerRequest(
            string.IsNullOrWhiteSpace(resource.Name) ? "Prowlarr indexer" : resource.Name.Trim(),
            IndexerImplementation.Torznab,
            baseUrl,
            string.IsNullOrWhiteSpace(apiPath) ? "/api" : apiPath,
            apiKey,
            categories is { Count: > 0 } ? categories : null,
            Math.Clamp(resource.Priority, Indexer.MinPriority, Indexer.MaxPriority),
            resource.EnableRss,
            resource.EnableAutomaticSearch,
            resource.EnableInteractiveSearch,
            Math.Max(0, minimumSeeders),
            seedRatio is { } r && r > 0 ? r : null,
            seedTime is { } t && t > 0 ? t : null,
            ProwlarrId: prowlarrId);
    }

    private static JsonElement? Field(RadarrIndexer resource, string name) =>
        resource.Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static string? String(this JsonElement? element) => element is { ValueKind: JsonValueKind.String } e ? e.GetString() : element is { ValueKind: JsonValueKind.Number } n ? n.GetRawText() : null;

    private static int? Int(this JsonElement? element) => element switch
    {
        { ValueKind: JsonValueKind.Number } e when e.TryGetInt32(out var i) => i,
        { ValueKind: JsonValueKind.Number } e => (int)e.GetDouble(),
        { ValueKind: JsonValueKind.String } e when int.TryParse(e.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
        _ => null,
    };

    private static double? Double(this JsonElement? element) => element switch
    {
        { ValueKind: JsonValueKind.Number } e => e.GetDouble(),
        { ValueKind: JsonValueKind.String } e when double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    private static List<int>? Ints(this JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Array } e ? e.EnumerateArray().Select(x => ((JsonElement?)x).Int()).Where(i => i is not null).Select(i => i!.Value).ToList() : null;

    private static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, RadarrCompat.Json);

    [GeneratedRegex(@"/(?<id>\d{1,10})/?$")]
    private static partial Regex ProwlarrIndexerRegex();
}
