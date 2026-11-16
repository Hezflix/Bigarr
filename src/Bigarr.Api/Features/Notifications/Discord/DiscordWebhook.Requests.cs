using Bigarr.Domain.Events;
using Bigarr.Domain.Issues;
using Bigarr.Domain.Notifications;
using Bigarr.Domain.Requests;

namespace Bigarr.Api.Features.Notifications.Discord;

/// <summary>What a request or issue message needs beside the event: where Bigarr is, and who to mention.</summary>
/// <param name="ApplicationUrl">Bigarr's public address; without it the title and the author link nowhere, like Seerr.</param>
/// <param name="Mentions">Discord user ids, pinged in the message's content.</param>
public sealed record DiscordContext(string? ApplicationUrl = null, IReadOnlyList<string>? Mentions = null);

/// <summary>
/// Requests and issues as Seerr's Discord agent posts them (<c>server/lib/notifications/agents/discord.ts</c>): the event as the
/// author linking to Bigarr, the title and year linking to the title's Discover page, the poster as the thumbnail,
/// <c>Requested By</c> and <c>Request Status</c> inline, the seasons of a series, Seerr's colors, the people concerned
/// mentioned in the content. Nothing about files, releases or indexers: the channel may be read by the requesters.
/// </summary>
public static partial class DiscordWebhook
{
    private static DiscordEmbed? RequestEmbed(DomainEvent domainEvent, string at, DiscordContext context) => domainEvent switch
    {
        RequestCreated { AutoApproved: false } e => Request(e.Request, $"New {Kind(e.Request)} Request", "Pending Approval", SeerrColor.Orange, at, context),
        RequestApproved e => Request(e.Request, e.AutoApproved ? $"{Kind(e.Request)} Request Automatically Approved" : $"{Kind(e.Request)} Request Approved", "Processing", SeerrColor.Purple, at, context),
        RequestDeclined e => Request(e.Request, $"{Kind(e.Request)} Request Declined", "Declined", SeerrColor.Red, at, context, ("Reason", e.Reason, false)),
        RequestAvailable e => Request(e.Request, $"{Kind(e.Request)} Request Now Available", "Available", SeerrColor.Green, at, context),
        RequestFailed e => Request(e.Request, $"{Kind(e.Request)} Request Failed", "Failed", SeerrColor.Red, at, context),
        IssueCreated e => Issue(e.Issue, $"New {IssueName(e.Issue)} Issue Reported", SeerrColor.Red, at, context),
        IssueCommented e => Seerr(
            $"New Comment on {IssueName(e.Issue)} Issue",
            Subject(e.Issue.Title, e.Issue.Year),
            IssueUrl(context.ApplicationUrl, e.Issue.Id),
            SeerrColor.Orange,
            e.Issue.PosterUrl,
            at,
            context,
            Fields([($"Comment from {e.CommentedBy}", e.Message, false), .. Affected(e.Issue)]),
            Message(e.Issue.Message)),
        IssueResolved e => Issue(e.Issue with { Status = IssueStatus.Resolved }, $"{IssueName(e.Issue)} Issue Resolved", SeerrColor.Green, at, context),
        IssueReopened e => Issue(e.Issue with { Status = IssueStatus.Open }, $"{IssueName(e.Issue)} Issue Reopened", SeerrColor.Red, at, context),
        _ => null,
    };

    /// <summary>The issue's page on Bigarr, as Seerr links <c>/issues/12</c>: the reporter and the handlers both open it.</summary>
    public static string? IssueUrl(string? applicationUrl, int issueId) =>
        string.IsNullOrWhiteSpace(applicationUrl) ? null : $"{applicationUrl.TrimEnd('/')}/issues/{issueId}";

    /// <summary>Seerr's <c>IssueTypeName</c>.</summary>
    private static string IssueName(IssueRef issue) => issue.IssueType switch
    {
        IssueType.Video => "Video",
        IssueType.Audio => "Audio",
        IssueType.Subtitles => "Subtitle",
        _ => "Other",
    };

    /// <summary>Seerr's issue embed: the report as the description, who reported it, its type and status.</summary>
    private static DiscordEmbed Issue(IssueRef issue, string title, SeerrColor color, string at, DiscordContext context) => Seerr(
        title,
        Subject(issue.Title, issue.Year),
        IssueUrl(context.ApplicationUrl, issue.Id),
        color,
        issue.PosterUrl,
        at,
        context,
        Fields([("Reported By", issue.ReportedBy, true), ("Issue Type", IssueName(issue), true), ("Issue Status", issue.Status == IssueStatus.Open ? "Open" : "Resolved", true), .. Affected(issue)]),
        Message(issue.Message));

    /// <summary>Seerr's extra fields for a season or an episode issue.</summary>
    private static (string Name, string? Value, bool Inline)[] Affected(IssueRef issue) =>
        [("Affected Season", issue.SeasonNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture), true), ("Affected Episode", issue.EpisodeLabel, true)];

    /// <summary>Seerr cuts the message at 300 characters.</summary>
    private static string? Message(string? text) => string.IsNullOrWhiteSpace(text) ? null : Cut(text, OverviewLimit + Ellipsis.Length);

    /// <summary>Seerr's subject: the title and the year.</summary>
    public static string Subject(string title, int? year) => Cut(year > 0 ? $"{title} ({year})" : title, TitleLimit)!;

    /// <summary>
    /// The title's page on Bigarr's Discover, as Seerr links <c>/movie/603</c>; a series only TVDB knows opens Discover's
    /// search on its TVDB id. Null without the application's address.
    /// </summary>
    public static string? DiscoverUrl(string? applicationUrl, RequestMediaType mediaType, int? tmdbId, int? tvdbId)
    {
        if (string.IsNullOrWhiteSpace(applicationUrl))
        {
            return null;
        }

        var root = applicationUrl.TrimEnd('/');
        return (mediaType, tmdbId, tvdbId) switch
        {
            (RequestMediaType.Movie, { } id, _) => $"{root}/discover/movie/{id}",
            (RequestMediaType.Series, { } id, _) => $"{root}/discover/tv/{id}",
            (RequestMediaType.Series, null, { } id) => $"{root}/discover/search?q=tvdb%3A{id}",
            _ => null,
        };
    }

    /// <summary>The mentions as Discord pings them, with only those users allowed to be pinged.</summary>
    private static DiscordPayload Mention(DiscordPayload payload, IReadOnlyList<string>? mentions)
    {
        var ids = (mentions ?? []).Where(Mentions.IsDiscordUserId).Distinct(StringComparer.Ordinal).ToList();
        return payload with
        {
            Content = ids.Count == 0 ? null : string.Join(' ', ids.Select(id => $"<@{id}>")),
            AllowedMentions = new DiscordAllowedMentions([], ids),
        };
    }

    private static string Kind(RequestRef request) => (request.FourK ? "4K " : string.Empty) + (request.MediaType == RequestMediaType.Movie ? "Movie" : "Series");

    private static DiscordEmbed Request(RequestRef request, string title, string status, SeerrColor color, string at, DiscordContext context, params (string Name, string? Value, bool Inline)[] extra)
    {
        var seasons = request.MediaType == RequestMediaType.Series && request.Seasons.Count > 0 ? string.Join(", ", request.Seasons.Order()) : null;
        var fields = Fields([("Requested By", request.RequestedBy, true), ("Request Status", status, true), ("Requested Seasons", seasons, true), .. extra]);
        return Seerr(title, Subject(request.Title, request.Year), DiscoverUrl(context.ApplicationUrl, request.MediaType, request.TmdbId, request.TvdbId), color, request.PosterUrl, at, context, fields);
    }

    /// <summary>Seerr's embed: the event as the author, the subject as the title, the poster as the thumbnail.</summary>
    private static DiscordEmbed Seerr(string eventName, string subject, string? url, SeerrColor color, string? posterUrl, string at, DiscordContext context, List<DiscordField> fields, string? description = null) => new(
        subject,
        description,
        url,
        (int)color,
        new DiscordAuthor(eventName, Url: string.IsNullOrWhiteSpace(context.ApplicationUrl) ? null : context.ApplicationUrl),
        Absolute(posterUrl) is { } poster ? new DiscordImage(poster) : null,
        null,
        at,
        fields);

    /// <summary>Discord only fetches absolute http(s) URLs.</summary>
    private static string? Absolute(string? url) =>
        url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? url : null;
}
