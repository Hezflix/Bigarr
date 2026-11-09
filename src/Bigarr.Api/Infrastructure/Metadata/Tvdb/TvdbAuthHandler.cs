using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Metadata.Tvdb;

/// <summary>
/// Logs in with the project API key (no PIN), adds the bearer token, logs in again once on 401, and retries once on
/// 429 (honoring <c>Retry-After</c>), on 5xx and on a dropped connection, like the TMDB handler.
/// </summary>
public sealed class TvdbAuthHandler(IOptions<TvdbOptions> options, TvdbTokenCache tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokens.GetAsync(LoginAsync, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage? response = null;
        TimeSpan? delay;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                tokens.Invalidate(token);
                token = await tokens.GetAsync(LoginAsync, cancellationToken);
                using var again = Clone(request, token);
                return await base.SendAsync(again, cancellationToken);
            }

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
            response?.Dispose();
            delay = TimeSpan.FromMilliseconds(500);
        }

        await Task.Delay(delay.Value > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : delay.Value, cancellationToken);

        using var retry = Clone(request, token);
        return await base.SendAsync(retry, cancellationToken);
    }

    private async Task<string> LoginAsync(CancellationToken cancellationToken)
    {
        var url = new Uri(new Uri(options.Value.BaseUrl), "login");
        using var login = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { apikey = options.Value.ApiKey }),
        };

        using var response = await base.SendAsync(login, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // The key never goes into the message.
            throw new HttpRequestException($"TVDB login failed ({(int)response.StatusCode}).", null, response.StatusCode);
        }

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("token", out var token) && token.GetString() is { Length: > 0 } value
            ? value
            : throw new HttpRequestException("TVDB login answered without a token.");
    }

    private static HttpRequestMessage Clone(HttpRequestMessage request, string token)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        clone.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return clone;
    }
}
