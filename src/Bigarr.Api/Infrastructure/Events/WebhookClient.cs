using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bigarr.Domain.Notifications;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Events;

public sealed record WebhookResult(bool Success, int? StatusCode, string? Error, TimeSpan Elapsed);

/// <summary>
/// One HTTP POST per delivery. The body wraps the event payload with its type and id, or is Radarr's payload for a
/// Radarr-format endpoint; when the endpoint has a secret, <c>X-Bigarr-Signature</c> carries
/// <c>sha256=&lt;hex HMAC of the body&gt;</c> so the receiver can check the origin.
/// </summary>
public sealed class WebhookClient(IHttpClientFactory httpClientFactory, IOptions<NotificationsOptions> options, ILogger<WebhookClient> logger)
{
    public const string HttpClientName = "bigarr-webhooks";

    public const string SignatureHeader = "X-Bigarr-Signature";

    public const string EventHeader = "X-Bigarr-Event";

    public const string DeliveryHeader = "X-Bigarr-Delivery";

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
            if (endpoint.Format == WebhookFormat.Radarr)
            {
                // What Radarr's WebhookProxy sends besides the JSON content type.
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

            return new WebhookResult(false, status, $"HTTP {status} {response.ReasonPhrase}".Trim(), stopwatch.Elapsed);
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
}
