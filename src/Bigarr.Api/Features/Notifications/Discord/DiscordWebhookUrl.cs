using System.Text.RegularExpressions;

namespace Bigarr.Api.Features.Notifications.Discord;

/// <summary>
/// Which URLs are Discord webhooks. The Discord format only posts to a channel webhook as Discord's "Copy Webhook
/// URL" gives it, and the other formats refuse one: Discord answers their bodies with "Cannot send an empty message".
/// </summary>
public static partial class DiscordWebhookUrl
{
    public const string Shape = "https://discord.com/api/webhooks/{id}/{token}";

    private static readonly string[] Domains = ["discord.com", "discordapp.com"];

    /// <summary>discord.com and discordapp.com, and the <c>ptb.</c> and <c>canary.</c> hosts of the test clients.</summary>
    public static bool IsDiscordHost(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return Domains.Any(d => uri.Host.Equals(d, StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals($"ptb.{d}", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals($"canary.{d}", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A channel webhook over https: <c>/api/webhooks/{id}/{token}</c>, an API version segment allowed, a query (<c>thread_id</c>) too.</summary>
    public static bool IsChannelWebhook(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && IsDiscordHost(uri) && ChannelWebhookPath().IsMatch(uri.AbsolutePath);
    }

    /// <summary>Any Discord webhook, Slack- and GitHub-compatible ones included: none of them reads a Bigarr or Radarr body.</summary>
    public static bool IsAnyWebhook(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return IsDiscordHost(uri) && AnyWebhookPath().IsMatch(uri.AbsolutePath);
    }

    [GeneratedRegex(@"^/api(?:/v\d+)?/webhooks/\d+/[A-Za-z0-9_-]+$")]
    private static partial Regex ChannelWebhookPath();

    [GeneratedRegex(@"^/api(?:/v\d+)?/webhooks/", RegexOptions.IgnoreCase)]
    private static partial Regex AnyWebhookPath();
}
