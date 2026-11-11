using Bigarr.Api.Features.Notifications.Discord;
using Bigarr.Api.Features.Notifications.Radarr;
using Bigarr.Api.Features.Tags;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Events;
using Bigarr.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Notifications;

/// <summary>Outgoing webhooks: the endpoints, what they subscribe to, and how their deliveries are doing.</summary>
public sealed class NotificationService(
    BigarrDbContext db,
    TagService tags,
    WebhookClient webhooks,
    WebhookSignal signal,
    IOptions<NotificationsOptions> options,
    TimeProvider time,
    ILogger<NotificationService> logger)
{
    public async Task<List<NotificationEndpointResponse>> ListAsync(CancellationToken ct)
    {
        var endpoints = await db.NotificationEndpoints.AsNoTracking().Include(e => e.Tags).OrderBy(e => e.Name).ToListAsync(ct);
        var stats = await StatsAsync(endpoints.Select(e => e.Id).ToList(), ct);
        return endpoints.Select(e => ToResponse(e, stats.GetValueOrDefault(e.Id))).ToList();
    }

    public async Task<NotificationEndpointResponse?> GetAsync(int id, CancellationToken ct)
    {
        var endpoint = await db.NotificationEndpoints.AsNoTracking().Include(e => e.Tags).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (endpoint is null)
        {
            return null;
        }

        var stats = await StatsAsync([id], ct);
        return ToResponse(endpoint, stats.GetValueOrDefault(id));
    }

    public async Task<Result<NotificationEndpointResponse>> AddAsync(NotificationEndpointRequest request, CancellationToken ct)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        if (await db.NotificationEndpoints.AnyAsync(e => e.Name == request.Name.Trim(), ct))
        {
            return Failure.Conflict("Name already used", $"A notification named '{request.Name.Trim()}' already exists.");
        }

        var resolved = await tags.ResolveAsync(request.Tags ?? [], ct);
        if (!resolved.IsSuccess)
        {
            return resolved.Failure!;
        }

        var endpoint = new NotificationEndpoint { Name = request.Name.Trim(), Url = request.Url.Trim(), CreatedAt = time.GetUtcNow().UtcDateTime, Tags = resolved.Value! };
        Apply(endpoint, request);
        db.NotificationEndpoints.Add(endpoint);
        await db.SaveChangesAsync(ct);
        // By name only: the URL may carry a token, as Discord's does.
        logger.LogInformation("Notification endpoint {Name} added ({Format} format)", endpoint.Name, endpoint.Format);
        return Result<NotificationEndpointResponse>.Ok(ToResponse(endpoint, null));
    }

    public async Task<Result<NotificationEndpointResponse>> UpdateAsync(int id, NotificationEndpointRequest request, CancellationToken ct)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var endpoint = await db.NotificationEndpoints.Include(e => e.Tags).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (endpoint is null)
        {
            return Failure.NotFound("Notification");
        }

        if (await db.NotificationEndpoints.AnyAsync(e => e.Id != id && e.Name == request.Name.Trim(), ct))
        {
            return Failure.Conflict("Name already used", $"A notification named '{request.Name.Trim()}' already exists.");
        }

        var resolved = await tags.ResolveAsync(request.Tags ?? [], ct);
        if (!resolved.IsSuccess)
        {
            return resolved.Failure!;
        }

        Apply(endpoint, request);
        endpoint.Tags.Clear();
        foreach (var tag in resolved.Value!)
        {
            endpoint.Tags.Add(tag);
        }

        await db.SaveChangesAsync(ct);
        if (endpoint.Enabled)
        {
            signal.Notify();
        }

        var stats = await StatsAsync([id], ct);
        return Result<NotificationEndpointResponse>.Ok(ToResponse(endpoint, stats.GetValueOrDefault(id)));
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct)
    {
        var deleted = await db.NotificationEndpoints.Where(e => e.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Failure.NotFound("Notification") : Result<bool>.Ok(true);
    }

    /// <summary>Sends a test event right away, to the saved endpoint or to an unsaved form, and reports what the receiver answered.</summary>
    public async Task<Result<WebhookTestResponse>> TestAsync(NotificationEndpointRequest? request, int? id, CancellationToken ct)
    {
        NotificationEndpoint endpoint;
        if (request is not null)
        {
            if (Validate(request) is { } invalid)
            {
                return invalid;
            }

            endpoint = new NotificationEndpoint { Name = request.Name.Trim(), Url = request.Url.Trim() };
            Apply(endpoint, request);
        }
        else
        {
            var saved = await db.NotificationEndpoints.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
            if (saved is null)
            {
                return Failure.NotFound("Notification");
            }

            endpoint = saved;
        }

        // Radarr's own test payload for a Radarr-format endpoint: what a tool written for Radarr expects to see. A Discord
        // channel gets the test embed.
        var now = time.GetUtcNow().UtcDateTime;
        var message = $"Bigarr can reach {endpoint.Name}.";
        var result = endpoint.Format switch
        {
            WebhookFormat.Radarr => await webhooks.SendAsync(endpoint, "test", RadarrWebhook.Serialize(RadarrWebhook.Test(options.Value.ApplicationUrl ?? string.Empty)), "test", ct),
            WebhookFormat.Discord => await webhooks.SendAsync(endpoint, "test", DiscordWebhook.Serialize(DiscordWebhook.Test(message, now, endpoint.Discord)), "test", ct),
            WebhookFormat.Sonarr => await webhooks.SendAsync(endpoint, "test", Sonarr.SonarrWebhook.Serialize(Sonarr.SonarrWebhook.Test(options.Value.ApplicationUrl ?? string.Empty)), "test", ct),
            _ => await webhooks.PostAsync(endpoint, "test", 0, EventJson.Serialize(new TestEvent(message) { OccurredAt = now }), now, "test", ct),
        };
        if (!result.Success)
        {
            return new Failure(StatusCodes.Status502BadGateway, "The endpoint did not accept the test event", result.Error);
        }

        return Result<WebhookTestResponse>.Ok(new WebhookTestResponse(result.StatusCode, (int)result.Elapsed.TotalMilliseconds));
    }

    public async Task<Result<List<WebhookDeliveryResponse>>> DeliveriesAsync(int endpointId, int? limit, CancellationToken ct)
    {
        if (!await db.NotificationEndpoints.AnyAsync(e => e.Id == endpointId, ct))
        {
            return Failure.NotFound("Notification");
        }

        var take = Math.Clamp(limit ?? 50, 1, 500);
        var deliveries = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.EndpointId == endpointId)
            .OrderByDescending(d => d.Id)
            .Take(take)
            .ToListAsync(ct);
        return Result<List<WebhookDeliveryResponse>>.Ok(deliveries.Select(ToResponse).ToList());
    }

    /// <summary>Makes a delivery due now, given up or not; the attempt counter starts over so the escalation does too.</summary>
    public async Task<Result<WebhookDeliveryResponse>> RetryAsync(long deliveryId, CancellationToken ct)
    {
        var delivery = await db.WebhookDeliveries.FirstOrDefaultAsync(d => d.Id == deliveryId, ct);
        if (delivery is null)
        {
            return Failure.NotFound("Delivery");
        }

        if (delivery.DeliveredAt is not null)
        {
            return Failure.Conflict("Already delivered", "This delivery was accepted by the endpoint.");
        }

        delivery.Attempts = 0;
        delivery.NextAttemptAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        signal.Notify();
        return Result<WebhookDeliveryResponse>.Ok(ToResponse(delivery));
    }

    public static Failure? Validate(NotificationEndpointRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Failure.Invalid("A name is required");
        }

        if (!Uri.TryCreate(request.Url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Failure.Invalid("Invalid URL", "The URL must be absolute and start with http:// or https://.");
        }

        var unknown = (request.Events ?? []).Where(e => !DomainEventCatalog.IsKnown(e)).ToList();
        if (unknown.Count > 0)
        {
            return Failure.Invalid("Unknown event type", $"Unknown event type(s): {string.Join(", ", unknown)}.");
        }

        if (!Enum.IsDefined(request.Format))
        {
            return Failure.Invalid("Unknown format", "The format must be bigarr, radarr or discord.");
        }

        // Discord answers anything but its own message body with "Cannot send an empty message".
        if (request.Format == WebhookFormat.Discord && !DiscordWebhookUrl.IsChannelWebhook(uri))
        {
            return Failure.Invalid("Not a Discord webhook URL", $"The Discord format posts to a channel webhook, {DiscordWebhookUrl.Shape}, as Discord's \"Copy Webhook URL\" gives it.");
        }

        if (request.Format != WebhookFormat.Discord && DiscordWebhookUrl.IsAnyWebhook(uri))
        {
            return Failure.Invalid("Discord webhook URL", "This is a Discord webhook: Discord only reads its own messages, not Bigarr's or Radarr's webhook body. Pick the Discord format.");
        }

        if (request.Format == WebhookFormat.Discord && ValidateDiscord(request.Discord) is { } discord)
        {
            return discord;
        }

        if (!CompatView.TryParse(request.View, out _))
        {
            return Failure.Invalid("Invalid view", "The view must be 'default', 'all' or 'version:<name>' (a lowercase slug).");
        }

        var unsent = request.Format == WebhookFormat.Radarr ? (request.Events ?? []).Where(e => WebhookRouting.RadarrEventType(e) is null).ToList() : [];
        if (unsent.Count > 0)
        {
            return Failure.Invalid("Not sent in Radarr's format", $"Radarr's webhook has no counterpart for: {string.Join(", ", unsent)}.");
        }

        var unsentSonarr = request.Format == WebhookFormat.Sonarr ? (request.Events ?? []).Where(e => WebhookRouting.SonarrEventType(e) is null).ToList() : [];
        if (unsentSonarr.Count > 0)
        {
            return Failure.Invalid("Not sent in Sonarr's format", $"Sonarr's webhook has no counterpart for: {string.Join(", ", unsentSonarr)}.");
        }

        var unposted = request.Format == WebhookFormat.Discord ? (request.Events ?? []).Where(e => !WebhookRouting.SentToDiscord(e)).ToList() : [];
        if (unposted.Count > 0)
        {
            return Failure.Invalid("Not sent to Discord", $"The Discord format posts nothing for: {string.Join(", ", unposted)}.");
        }

        return null;
    }

    private static Failure? ValidateDiscord(DiscordSettings? settings)
    {
        if (settings is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(settings.Avatar)
            && (!Uri.TryCreate(settings.Avatar.Trim(), UriKind.Absolute, out var avatar) || (avatar.Scheme != Uri.UriSchemeHttp && avatar.Scheme != Uri.UriSchemeHttps)))
        {
            return Failure.Invalid("Invalid avatar URL", "The avatar must be an image URL starting with http:// or https://.");
        }

        var known = (settings.GrabFields ?? []).All(Enum.IsDefined)
            && (settings.ImportFields ?? []).All(Enum.IsDefined)
            && (settings.ManualInteractionFields ?? []).All(Enum.IsDefined);
        return known ? null : Failure.Invalid("Unknown Discord field", "A grab, import or import failure field is not one Discord messages have.");
    }

    private static void Apply(NotificationEndpoint endpoint, NotificationEndpointRequest request)
    {
        endpoint.Name = request.Name.Trim();
        endpoint.Url = request.Url.Trim();
        // Discord has nothing to check a signature with.
        endpoint.Secret = string.IsNullOrWhiteSpace(request.Secret) || request.Format == WebhookFormat.Discord ? null : request.Secret;
        endpoint.Enabled = request.Enabled;
        endpoint.Events = (request.Events ?? []).Distinct(StringComparer.Ordinal).OrderBy(e => e, StringComparer.Ordinal).ToList();
        endpoint.Format = request.Format;
        endpoint.View = CompatView.Parse(string.IsNullOrWhiteSpace(request.View) ? "default" : request.View);
        endpoint.Discord = request.Format == WebhookFormat.Discord ? (request.Discord ?? new DiscordSettings()).Normalized() : null;
    }

    private async Task<Dictionary<int, DeliveryStats>> StatsAsync(List<int> endpointIds, CancellationToken ct)
    {
        if (endpointIds.Count == 0)
        {
            return [];
        }

        var rows = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => endpointIds.Contains(d.EndpointId))
            .GroupBy(d => d.EndpointId)
            .Select(g => new
            {
                EndpointId = g.Key,
                Pending = g.Count(d => d.DeliveredAt == null && d.NextAttemptAt != null),
                Failed = g.Count(d => d.DeliveredAt == null && d.NextAttemptAt == null),
                LastDeliveredAt = g.Max(d => d.DeliveredAt),
            })
            .ToListAsync(ct);

        var lastErrors = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => endpointIds.Contains(d.EndpointId) && d.LastError != null)
            .GroupBy(d => d.EndpointId)
            .Select(g => g.OrderByDescending(d => d.LastAttemptAt).Select(d => new { d.EndpointId, d.LastError, d.LastAttemptAt }).First())
            .ToListAsync(ct);

        var result = rows.ToDictionary(r => r.EndpointId, r => new DeliveryStats(r.Pending, r.Failed, r.LastDeliveredAt, null));
        foreach (var error in lastErrors)
        {
            // Only worth showing when nothing succeeded since.
            var stats = result.GetValueOrDefault(error.EndpointId) ?? new DeliveryStats(0, 0, null, null);
            if (stats.LastDeliveredAt is null || error.LastAttemptAt > stats.LastDeliveredAt)
            {
                result[error.EndpointId] = stats with { LastError = error.LastError };
            }
        }

        return result;
    }

    private static NotificationEndpointResponse ToResponse(NotificationEndpoint endpoint, DeliveryStats? stats) => new(
        endpoint.Id,
        endpoint.Name,
        endpoint.Url,
        !string.IsNullOrEmpty(endpoint.Secret),
        endpoint.Enabled,
        endpoint.Events,
        endpoint.Tags.Select(t => t.Id).Order().ToList(),
        endpoint.Format,
        endpoint.View.ToString(),
        endpoint.Discord,
        stats?.Pending ?? 0,
        stats?.Failed ?? 0,
        stats?.LastDeliveredAt,
        stats?.LastError,
        endpoint.CreatedAt);

    private static WebhookDeliveryResponse ToResponse(WebhookDelivery d) =>
        new(d.Id, d.EventId, d.EventType, d.State, d.Attempts, d.NextAttemptAt, d.LastAttemptAt, d.DeliveredAt, d.LastStatusCode, d.LastError, d.CreatedAt);

    private sealed record DeliveryStats(int Pending, int Failed, DateTime? LastDeliveredAt, string? LastError);
}
