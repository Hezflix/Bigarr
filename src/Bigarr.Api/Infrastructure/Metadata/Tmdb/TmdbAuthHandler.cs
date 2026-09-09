using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Metadata.Tmdb;

/// <summary>
/// Adds the credential (v4 bearer token when the key looks like a JWT, <c>api_key</c> query otherwise)
/// and retries once on 429 (honoring <c>Retry-After</c>), on 5xx and on a dropped connection.
/// </summary>
public sealed class TmdbAuthHandler(IOptions<MetadataOptions> options) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = options.Value.ApiKey ?? string.Empty;
        if (key.Contains('.'))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }
        else if (request.RequestUri is not null)
        {
            var uri = request.RequestUri.ToString();
            request.RequestUri = new Uri(uri + (uri.Contains('?') ? "&" : "?") + "api_key=" + Uri.EscapeDataString(key));
        }

        TimeSpan delay;
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500)
            {
                return response;
            }

            delay = response.StatusCode == HttpStatusCode.TooManyRequests
                ? response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1)
                : TimeSpan.FromMilliseconds(500);
            response.Dispose();
        }
        catch (HttpRequestException)
        {
            delay = TimeSpan.FromMilliseconds(500);
        }

        await Task.Delay(delay > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : delay, cancellationToken);

        using var retry = await CloneAsync(request);
        return await base.SendAsync(retry, cancellationToken);
    }

    private static Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return Task.FromResult(clone);
    }
}
