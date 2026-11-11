using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bigarr.Api.Features.Notifications.Discord;
using Bigarr.Domain.Notifications;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Events;

/// <param name="RetryAfter">On a 429: how long the receiver asked to wait, when it said.</param>
public sealed record WebhookResult(bool Success, int? StatusCode, string? Error, TimeSpan Elapsed, TimeSpan? RetryAfter = null);

/// <summary>
/// One HTTP POST per delivery. The body wraps the event payload with its type and id, or is Radarr's payload for a
/// Radarr-format endpoint, or Discord's message for a Discord one; when the endpoint has a secret,
/// <c>X-Bigarr-Signature</c> carries <c>sha256=&lt;hex HMAC of the body&gt;</c> so the receiver can check the origin.
/// The URL may carry a token (Discord's does): it is never logged, the endpoint is named by its name only.
/// </summary>
public sealed class WebhookClient(IHttpClientFactory httpClientFactory, IOptions<NotificationsOptions> options, TimeProvider time, ILogger<WebhookClient> logger)
{
    public const string HttpClientName = "bigarr-webhooks";

    public const string SignatureHeader = "X-Bigarr-Signature";

    public const string EventHeader = "X-Bigarr-Event";

    public const string DeliveryHeader = "X-Bigarr-Delivery";

    /// <summary>Enough of an error answer for Discord's JSON; the rest of a large page is not read.</summary>
    private const int ErrorBodyLimit = 16 * 1024;

    public Task<WebhookResult> PostAsync(NotificationEndpoint endpoint, string eventType, long eventId, string payloadJson, DateTime occurredAt, string deliveryId, CancellationToken ct) =>
        SendAsync(endpoint, eventType, Body(eventType, eventId, payloadJson, occurredAt, options.Value.ApplicationUrl), deliveryId, ct);

    /// <summary>Posts a body already built; the Bigarr headers go along whatever the format, the receiver ignores what it does not know.</summary>
    public async Task<WebhookResult> SendAsync(NotificationEndpoint endpoint, string eventType, string body, string deliveryId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (endpoint.Format is WebhookFormat.Radarr or WebhookFormat.Sonarr or WebhookFormat.Discord)
            {
                // What Radarr's WebhookProxy and DiscordProxy send besides the JSON content type.
                request.Headers.Accept.ParseAdd("application/json");
            }

            request.Headers.TryAddWithoutValidation(EventHeader, eventType);
            request.Headers.TryAddWithoutValidation(DeliveryHeader, deliveryId);
            if (!string.IsNullOrEmpty(endpoint.Secret))
            {
                request.Headers.TryAddWithoutValidation(SignatureHeader, Sign(endpoint.Secret, body));
            }

            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                return new WebhookResult(true, status, null, stopwatch.Elapsed);
            }

            // Discord says what is wrong in JSON ({"message", "code"}) and how long to wait on a 429 (retry_after).
            var discord = endpoint.Format == WebhookFormat.Discord;
            var answer = discord ? await ReadAsync(response, ct) : null;
            var retryAfter = response.StatusCode == HttpStatusCode.TooManyRequests ? RetryAfter(response) ?? (discord ? DiscordWebhook.RetryAfter(answer) : null) : null;
            var error = (discord ? DiscordWebhook.Error(answer) : null) ?? $"HTTP {status} {response.ReasonPhrase}".Trim();
            return new WebhookResult(false, status, error, stopwatch.Elapsed, retryAfter);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            var message = ex is TaskCanceledException ? "No answer before the timeout." : ex.Message;
            logger.LogDebug(ex, "Webhook {Endpoint} failed: {Message}", endpoint.Name, message);
            return new WebhookResult(false, null, message, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// The wire shape: <c>{ eventType, eventId, occurredAt, instance, data }</c>. <c>instance.applicationUrl</c> is
    /// Radarr's <c>applicationUrl</c>, for links back to Bigarr; empty until <c>Bigarr:Notifications:ApplicationUrl</c> is set.
    /// </summary>
    public static string Body(string eventType, long eventId, string payloadJson, DateTime occurredAt, string? applicationUrl = null)
    {
        var body = new JsonObject
        {
            ["eventType"] = eventType,
            ["eventId"] = eventId,
            ["occurredAt"] = occurredAt.ToString("O", CultureInfo.InvariantCulture),
            ["instance"] = new JsonObject
            {
                ["name"] = "Bigarr",
                ["version"] = Features.Server.SystemEndpoints.Version,
                ["applicationUrl"] = applicationUrl ?? string.Empty,
            },
            ["data"] = JsonNode.Parse(payloadJson),
        };
        return body.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public static string Sign(string secret, string body) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    /// <summary>The <c>Retry-After</c> header: seconds (Discord may send a fraction) or a date.</summary>
    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is { } header)
        {
            if (header.Delta is { } delta)
            {
                return delta;
            }

            if (header.Date is { } date)
            {
                var left = date - time.GetUtcNow();
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        return response.Headers.TryGetValues("Retry-After", out var values)
            && double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds >= 0
                ? TimeSpan.FromSeconds(seconds)
                : null;
    }

    private static async Task<string?> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[ErrorBodyLimit];
            var read = 0;
            int chunk;
            while (read < buffer.Length && (chunk = await stream.ReadAsync(buffer.AsMemory(read), ct)) > 0)
            {
                read += chunk;
            }

            return Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // the status alone still says something
            return null;
        }
    }
}
