using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Bigarr.Api.Features.Notifications.Discord;
using Bigarr.Api.Infrastructure.Events;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Notifications.NewEpisodes;

/// <summary>What Discord made of one direct message.</summary>
/// <param name="Refused">Discord will not take a message for this user, whatever the retry: they share no server with the bot, closed their DMs, or are unknown.</param>
/// <param name="RetryAfter">On a 429: how long Discord asked the bot to wait.</param>
public sealed record DiscordDmResult(bool Success, int? StatusCode, string? Error, TimeSpan Elapsed, bool Refused = false, TimeSpan? RetryAfter = null);

/// <summary>
/// Direct messages from a Discord bot over plain REST, without a gateway connection: open the DM channel
/// (<c>POST /users/@me/channels</c>, which gives back the existing one), then post in it
/// (<c>POST /channels/{id}/messages</c>). The token goes in the <c>Authorization</c> header only, and the HTTP client
/// logs nothing; errors name the user by Discord id, never the token.
/// </summary>
public sealed class DiscordBotClient(IHttpClientFactory httpClientFactory, IOptions<NewEpisodeOptions> options, TimeProvider time, ILogger<DiscordBotClient> logger)
{
    public const string HttpClientName = "bigarr-discord-bot";

    /// <summary>Discord's codes that no retry changes: unknown user, cannot send messages to this user, invalid recipient.</summary>
    private static readonly HashSet<int> RefusedCodes = [10013, 50007, 50033];

    public async Task<DiscordDmResult> SendAsync(string token, string discordUserId, string body, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(discordUserId);
        ArgumentNullException.ThrowIfNull(body);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var channel = await PostAsync(token, "users/@me/channels", JsonSerializer.Serialize(new { recipient_id = discordUserId }), ct);
            if (channel.Failure is { } refused)
            {
                return refused with { Elapsed = stopwatch.Elapsed };
            }

            using var doc = JsonDocument.Parse(channel.Body ?? "{}");
            if (!doc.RootElement.TryGetProperty("id", out var id) || id.GetString() is not { Length: > 0 } channelId)
            {
                return new DiscordDmResult(false, channel.StatusCode, "Discord opened no direct message channel.", stopwatch.Elapsed);
            }

            var message = await PostAsync(token, $"channels/{Uri.EscapeDataString(channelId)}/messages", body, ct);
            return message.Failure is { } failed
                ? failed with { Elapsed = stopwatch.Elapsed }
                : new DiscordDmResult(true, message.StatusCode, null, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException or JsonException)
        {
            var error = ex is TaskCanceledException ? "No answer before the timeout." : ex.Message;
            logger.LogDebug("Direct message to Discord user {DiscordUser} failed: {Error}", discordUserId, error);
            return new DiscordDmResult(false, null, error, stopwatch.Elapsed);
        }
    }

    private async Task<(int StatusCode, string? Body, DiscordDmResult? Failure)> PostAsync(string token, string path, string json, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.Value.DiscordApiUrl.TrimEnd('/')}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", token.Trim());
        request.Headers.Accept.ParseAdd("application/json");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var status = (int)response.StatusCode;
        var answer = await WebhookClient.ReadAsync(response, ct);
        if (response.IsSuccessStatusCode)
        {
            return (status, answer, null);
        }

        var error = DiscordWebhook.Error(answer) ?? $"HTTP {status} {response.ReasonPhrase}".Trim();
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var wait = WebhookClient.RetryAfter(response, time.GetUtcNow()) ?? DiscordWebhook.RetryAfter(answer) ?? TimeSpan.FromSeconds(5);
            return (status, answer, new DiscordDmResult(false, status, error, TimeSpan.Zero, RetryAfter: wait));
        }

        // 403 on a DM is Discord's "Cannot send messages to this user" (no server in common, DMs closed); a 401 is the
        // token, which concerns every user and is retried like any other failure.
        var refused = response.StatusCode == HttpStatusCode.Forbidden || (Code(answer) is { } code && RefusedCodes.Contains(code));
        return (status, answer, new DiscordDmResult(false, status, error, TimeSpan.Zero, Refused: refused));
    }

    private static int? Code(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("code", out var code) && code.TryGetInt32(out var number) ? number : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
