using System.Globalization;
using Bigarr.Api.Features.Notifications.Discord;

namespace Bigarr.Api.Features.Notifications.NewEpisodes;

/// <summary>An episode as the message tells it.</summary>
public sealed record AnnouncedEpisode(int SeasonNumber, int EpisodeNumber, string? Title, string? Overview);

/// <summary>Where the show is on Plex, for the "View on Plex" link: the server's id on plex.tv and the show's rating key.</summary>
public sealed record PlexShowLink(string MachineIdentifier, string ShowRatingKey);

/// <summary>
/// The direct message about new episodes, the layout of the Discord bot it replaces: a content line, then an embed with
/// the show and episode as the title, the episode's summary, the show's poster as the image, dark purple, a timestamp
/// and a "View on Plex" link. Several episodes make one message that lists them. English, like every Discord message
/// of Bigarr; nothing in it pings anyone.
/// </summary>
public static class NewEpisodeMessages
{
    /// <summary>Episodes listed one per line in a batch's description; the rest are counted.</summary>
    public const int ListedEpisodes = 15;

    /// <summary>The summary of a single episode is cut here, well under Discord's limit for a description.</summary>
    public const int SummaryLimit = 1024;

    public static DiscordPayload Payload(string showTitle, string? posterUrl, IReadOnlyList<AnnouncedEpisode> episodes, PlexShowLink? plex, DateTime at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(showTitle);
        ArgumentNullException.ThrowIfNull(episodes);
        if (episodes.Count == 0)
        {
            throw new ArgumentException("An announcement tells at least one episode.", nameof(episodes));
        }

        var ordered = episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList();
        string content;
        string title;
        string? description;
        if (ordered.Count == 1)
        {
            var e = ordered[0];
            content = $"📺 {showTitle} - Season {e.SeasonNumber} · Episode {e.EpisodeNumber} - {EpisodeTitle(e)} 👈 just added to Plex!";
            title = $"{showTitle} - {EpisodeTitle(e)} (S{e.SeasonNumber} · E{e.EpisodeNumber})";
            description = Cut(e.Overview, SummaryLimit);
        }
        else
        {
            content = $"📺 {showTitle} - {ordered.Count} new episodes 👈 just added to Plex!";
            title = $"{showTitle} - {ordered.Count} new episodes ({Range(ordered)})";
            var lines = ordered.Take(ListedEpisodes).Select(e => $"S{e.SeasonNumber:00} · E{e.EpisodeNumber:00} - {EpisodeTitle(e)}").ToList();
            if (ordered.Count > ListedEpisodes)
            {
                lines.Add($"… and {ordered.Count - ListedEpisodes} more");
            }

            description = string.Join('\n', lines);
        }

        var link = plex is null ? null : PlexUrl(plex);
        var embed = new DiscordEmbed(
            title,
            description,
            null,
            (int)SeerrColor.DarkPurple,
            null,
            null,
            Absolute(posterUrl) is { } poster ? new DiscordImage(poster) : null,
            at.ToString("O", CultureInfo.InvariantCulture),
            link is null ? null : [new DiscordField("View on Plex", $"[Open the show on Plex]({link})")]);

        // A bot's message: no username or avatar to set, and nothing it says may ping anyone.
        return DiscordWebhook.Fit(new DiscordPayload(content, null, null, [embed], new DiscordAllowedMentions([], [])));
    }

    /// <summary>The show on Plex Web, the address every Plex user can open whatever the server's own address.</summary>
    public static string PlexUrl(PlexShowLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return $"https://app.plex.tv/desktop/#!/server/{Uri.EscapeDataString(link.MachineIdentifier)}/details?key={Uri.EscapeDataString("/library/metadata/" + link.ShowRatingKey)}";
    }

    private static string EpisodeTitle(AnnouncedEpisode e) =>
        string.IsNullOrWhiteSpace(e.Title) ? $"Episode {e.EpisodeNumber}" : e.Title.Trim();

    /// <summary><c>S01 · E01–E08</c> within a season, <c>S01–S02</c> across seasons.</summary>
    private static string Range(List<AnnouncedEpisode> ordered)
    {
        var first = ordered[0];
        var last = ordered[^1];
        return first.SeasonNumber == last.SeasonNumber
            ? $"S{first.SeasonNumber:00} · E{first.EpisodeNumber:00}–E{last.EpisodeNumber:00}"
            : $"S{first.SeasonNumber:00}–S{last.SeasonNumber:00}";
    }

    private static string? Cut(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();
        return text.Length <= max ? text : $"{text.AsSpan(0, max - 3).TrimEnd()}...";
    }

    /// <summary>Discord only fetches absolute http(s) URLs.</summary>
    private static string? Absolute(string? url) =>
        url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? url : null;
}
