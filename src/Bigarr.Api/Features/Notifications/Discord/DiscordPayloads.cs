using System.Text.Json.Serialization;

namespace Bigarr.Api.Features.Notifications.Discord;

/// <summary>
/// Discord's "execute webhook" body, the members Radarr's <c>DiscordPayload</c> sends. Discord answers
/// <c>Cannot send an empty message</c> to anything without <c>content</c> or <c>embeds</c>: that is why a Discord
/// channel cannot read the Bigarr or Radarr format.
/// </summary>
public sealed record DiscordPayload(
    string? Content,
    string? Username,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl,
    List<DiscordEmbed>? Embeds);

/// <param name="Color">A <see cref="DiscordColor"/>; none on a rename, like Radarr.</param>
/// <param name="Timestamp">ISO 8601; Discord shows it under the embed in the reader's time zone.</param>
public sealed record DiscordEmbed(
    string? Title,
    string? Description,
    string? Url,
    int? Color,
    DiscordAuthor? Author,
    DiscordImage? Thumbnail,
    DiscordImage? Image,
    string? Timestamp,
    List<DiscordField>? Fields);

public sealed record DiscordAuthor(string Name, [property: JsonPropertyName("icon_url")] string? IconUrl = null);

public sealed record DiscordImage(string Url);

public sealed record DiscordField(string Name, string Value, bool Inline = false);

/// <summary>Radarr's <c>DiscordColors</c>, same values.</summary>
public enum DiscordColor
{
    Danger = 15749200,
    Success = 2605644,
    Warning = 16753920,
    Standard = 16761392,
    Upgrade = 4089856,
}
