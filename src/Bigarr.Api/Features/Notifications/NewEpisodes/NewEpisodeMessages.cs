using System.Globalization;
using Bigarr.Api.Features.Notifications.Discord;
using Bigarr.Domain.Notifications;

namespace Bigarr.Api.Features.Notifications.NewEpisodes;

/// <summary>An episode as the message tells it.</summary>
public sealed record AnnouncedEpisode(int SeasonNumber, int EpisodeNumber, string? Title, string? Overview);

/// <summary>Where the show is on Plex, for the "View on Plex" link: the server's id on plex.tv and the show's rating key.</summary>
public sealed record PlexShowLink(string MachineIdentifier, string ShowRatingKey);

/// <summary>
/// The direct message about new episodes, the layout of the Discord bot it replaces: a content line, then an embed with
/// the show and episode as the title, the episode's summary, the show's poster as the image, dark purple, a timestamp
/// and a "View on Plex" link. Several episodes make one message that lists them. The texts are the admin's
/// (<see cref="NewEpisodeTemplates"/>), English by default; nothing in it pings anyone.
/// </summary>
public static class NewEpisodeMessages
{
    /// <summary>Episodes listed one per line in a batch's description; the rest are counted.</summary>
    public const int ListedEpisodes = 15;

    /// <summary>The summary of a single episode is cut here, well under Discord's limit for a description.</summary>
    public const int SummaryLimit = 1024;

    public static DiscordPayload Payload(string showTitle, string? posterUrl, IReadOnlyList<AnnouncedEpisode> episodes, PlexShowLink? plex, DateTime at, NewEpisodeSettings? settings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(showTitle);
        ArgumentNullException.ThrowIfNull(episodes);
        if (episodes.Count == 0)
        {
            throw new ArgumentException("An announcement tells at least one episode.", nameof(episodes));
        }

        string Text(NewEpisodeText field, Dictionary<string, object> values) => NewEpisodeTemplates.Render(NewEpisodeTemplates.Text(settings, field), values);
        Dictionary<string, object> Of(AnnouncedEpisode e) => new()
        {
            ["Show"] = showTitle,
            ["Season"] = e.SeasonNumber,
            ["Episode"] = e.EpisodeNumber,
            ["EpisodeTitle"] = EpisodeTitle(e),
        };

        var ordered = episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList();
        string content;
        string title;
        string? description;
        if (ordered.Count == 1)
        {
            var values = Of(ordered[0]);
            content = Text(NewEpisodeText.Content, values);
            title = Text(NewEpisodeText.Title, values);
            description = Cut(ordered[0].Overview, SummaryLimit);
        }
        else
        {
            var values = new Dictionary<string, object> { ["Show"] = showTitle, ["Count"] = ordered.Count, ["Range"] = Range(ordered) };
            content = Text(NewEpisodeText.BatchContent, values);
            title = Text(NewEpisodeText.BatchTitle, values);
            var lines = ordered.Take(ListedEpisodes).Select(e => Text(NewEpisodeText.BatchLine, Of(e))).ToList();
            if (ordered.Count > ListedEpisodes)
            {
                lines.Add(Text(NewEpisodeText.BatchMore, new() { ["Show"] = showTitle, ["More"] = ordered.Count - ListedEpisodes }));
            }

            description = string.Join('\n', lines);
        }

        var link = plex is null ? null : PlexUrl(plex, settings);
        var show = new Dictionary<string, object> { ["Show"] = showTitle };
        var embed = new DiscordEmbed(
            title,
            description,
            null,
            (int)SeerrColor.DarkPurple,
            null,
            null,
            Absolute(posterUrl) is { } poster ? new DiscordImage(poster) : null,
            at.ToString("O", CultureInfo.InvariantCulture),
            link is null ? null : [new DiscordField(Text(NewEpisodeText.LinkName, show), $"[{LinkLabel(Text(NewEpisodeText.LinkText, show))}]({link})")]);

        // A bot's message: no username or avatar to set, and nothing it says may ping anyone.
        return DiscordWebhook.Fit(new DiscordPayload(content, null, null, [embed], new DiscordAllowedMentions([], [])));
    }

    /// <summary>
    /// The show on Plex Web: app.plex.tv, which every Plex user can open, or the server's own address when the admin
    /// chose it and gave a valid one. Plex Web on any address opens any server the user can reach, by its id.
    /// </summary>
    public static string PlexUrl(PlexShowLink link, NewEpisodeSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(link);
        var route = $"#!/server/{Uri.EscapeDataString(link.MachineIdentifier)}/details?key={Uri.EscapeDataString("/library/metadata/" + link.ShowRatingKey)}";
        if (settings is { LinkTarget: PlexLinkTarget.ServerAddress } && NewEpisodeTemplates.WebAddress(settings.PlexWebAddress) is { } address)
        {
            return $"{address}/web/index.html{route}";
        }

        return $"https://app.plex.tv/desktop/{route}";
    }

    /// <summary>Brackets would end Discord's link text early.</summary>
    private static string LinkLabel(string text) => text.Replace('[', '(').Replace(']', ')');

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
