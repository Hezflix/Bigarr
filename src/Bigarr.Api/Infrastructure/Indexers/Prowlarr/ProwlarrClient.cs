using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Domain.Indexers;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Indexers.Prowlarr;

public interface IProwlarrClient
{
    Task<ProwlarrSystemStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ProwlarrIndexer>> GetIndexersAsync(CancellationToken cancellationToken);
}

/// <summary>Read-only client for Prowlarr's own API (<c>X-Api-Key</c>). Redirects are not followed: a login page is an answer.</summary>
public sealed class ProwlarrClient(IHttpClientFactory httpClientFactory, IOptions<ProwlarrOptions> options) : IProwlarrClient
{
    public const string HttpClientName = "prowlarr";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ProwlarrSystemStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        GetAsync<ProwlarrSystemStatus>("/api/v1/system/status", cancellationToken);

    public Task<IReadOnlyList<ProwlarrIndexer>> GetIndexersAsync(CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<ProwlarrIndexer>>("/api/v1/indexer", cancellationToken);

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            throw new IndexerException(IndexerFailureKind.Request, "Prowlarr is not configured (Bigarr:Prowlarr:BaseUrl and ApiKey).");
        }

        var http = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, settings.NormalizedBaseUrl + path);
        request.Headers.Add("X-Api-Key", settings.ApiKey!.Trim());
        request.Headers.Accept.ParseAdd("application/json");

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            HttpFailures.ThrowIfFailed(response, "Prowlarr");
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
                   ?? throw new IndexerException(IndexerFailureKind.InvalidResponse, "Prowlarr returned an empty document.");
        }
        catch (HttpRequestException ex)
        {
            throw new IndexerException(IndexerFailureKind.Unreachable, $"Could not reach Prowlarr: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IndexerException(IndexerFailureKind.Unreachable, $"Prowlarr did not answer within {http.Timeout.TotalSeconds:0} seconds.", ex);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new IndexerException(IndexerFailureKind.InvalidResponse, $"Prowlarr answered with something that is not its API: {ex.Message}", ex);
        }
    }
}

/// <summary>HTTP status handling shared by the Torznab and Prowlarr clients.</summary>
public static class HttpFailures
{
    public static void ThrowIfFailed(HttpResponseMessage response, string who)
    {
        ArgumentNullException.ThrowIfNull(response);

        var status = (int)response.StatusCode;
        if (status is >= 300 and < 400)
        {
            // An SSO proxy (oauth2-proxy, Authelia…) in front of the indexer answers every path with a redirect to its login page.
            var target = response.Headers.Location?.Host;
            throw new IndexerException(
                IndexerFailureKind.Authentication,
                target is null
                    ? $"{who} redirected the request (HTTP {status}): a login page is probably in front of it."
                    : $"{who} redirected to {target} (HTTP {status}): a login page is in front of it. Exempt its API paths from the SSO, or use an internal URL.");
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                throw new IndexerException(IndexerFailureKind.Authentication, $"HTTP {status}: {who} refused the request. Check the URL and the API key.");
            case HttpStatusCode.TooManyRequests:
                throw new IndexerException(IndexerFailureKind.RateLimited, $"HTTP 429: {who} is rate limiting us.");
            case >= HttpStatusCode.InternalServerError:
                throw new IndexerException(IndexerFailureKind.ServerError, $"HTTP {status}: {who} failed.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new IndexerException(IndexerFailureKind.Request, $"HTTP {status} from {who}.");
        }
    }
}
