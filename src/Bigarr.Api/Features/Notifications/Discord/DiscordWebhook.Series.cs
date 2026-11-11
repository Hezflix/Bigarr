using System.Globalization;
using Bigarr.Domain.Events;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Notifications;
using Bigarr.Domain.Tv;

namespace Bigarr.Api.Features.Notifications.Discord;

/// <summary>
/// Series events as Sonarr's Discord connection posts them (<c>NzbDrone.Core/Notifications/Discord/Discord.cs</c>): the
/// title is <c>Show - 1x05 - Episode</c> (<c>Show - 2026-11-10 - Episode</c> for a daily show), the link goes to TheTVDB,
/// the links field names TheTVDB, Trakt and IMDb; the fields are the same choices as for films (Sonarr's field types
/// are Radarr's), plus the version.
/// </summary>
public static partial class DiscordWebhook
{
    private static DiscordEmbed? SeriesEmbed(DiscordSettings s, DomainEvent domainEvent, string at) => domainEvent switch
    {
        EpisodeGrabbed e => EpisodeGrab(s, e.Series, e.Version, e.Episodes, e.Release, "Episode Grabbed", DiscordColor.Standard, at),
        EpisodeFileImported e => EpisodeImport(s, e, at),
        SeriesAdded e => Show(s, e.Series, "Series Added", DiscordColor.Success, at),

        // Sonarr's SeriesDeleteMessage wording.
        SeriesDeleted e => Show(s, e.Series, e.DeletedFiles ? "Series removed and all files were deleted" : "Series removed, files were not deleted", DiscordColor.Danger, at),
        SeriesVersionDeleted e => ShowEmbed(s, e.Series, null, e.DeletedFiles ? "Version removed and its files were deleted" : "Version removed, files were not deleted", DiscordColor.Danger, at, ShowImage(e.Series, ImageType.Poster, chosen: true), null, Fields(
            ("Version", e.Version.Name, true),
            ("Quality Profile", e.Version.QualityProfile, true))),
        EpisodeFileDeleted e => ShowEmbed(s, e.Series, EpisodeTitle(e.Series, e.Episodes), "Episode Deleted", DiscordColor.Danger, at, null, null, Fields(
            ("Version", e.Version.Name, true),
            ("Reason", e.Reason, true),
            ("File name", Code(e.File.Path), false))),

        // Sonarr's rename embed: the show's title only.
        EpisodeFileRenamed e => new DiscordEmbed(Cut(e.Series.Title, TitleLimit), null, null, null, null, null, null, null, null),
        EpisodeImportFailed { Series: { } series } e => EpisodeImportFailure(s, e, series, at),
        EpisodeDownloadFailed { Series: { } series } e => EpisodeGrab(s, series, e.Version, e.Episodes, e.Release, "Download Failed", DiscordColor.Danger, at, FailedReason(e.Reason, e.Blocklisted, e.SearchRequested)),
        _ => null,
    };

    /// <summary>Sonarr's <c>GetTitle</c>: <c>Show - 1x05x06 - First + Second</c>, a daily show by air date, cut to the title limit.</summary>
    public static string? EpisodeTitle(SeriesRef? series, IReadOnlyList<EpisodeRef> episodes)
    {
        if (series is null)
        {
            return null;
        }

        if (episodes.Count == 0)
        {
            return Cut(series.Title, TitleLimit);
        }

        if (series.Type == SeriesType.Daily)
        {
            var first = episodes[0];
            return Cut($"{series.Title} - {first.AirDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} - {first.Title}", TitleLimit);
        }

        var numbers = string.Concat(episodes.Select(e => $"x{e.EpisodeNumber:00}"));
        return Cut($"{series.Title} - {episodes[0].SeasonNumber}{numbers} - {string.Join(" + ", episodes.Select(e => e.Title))}", TitleLimit);
    }

    private static DiscordEmbed EpisodeGrab(DiscordSettings s, SeriesRef series, SeriesVersionRef? version, List<EpisodeRef> episodes, ReleaseRef release, string description, DiscordColor color, string at, string? reason = null)
    {
        var chosen = s.GrabFields ?? DiscordSettings.DefaultGrabFields;
        var fields = new FieldList<DiscordGrabField>(chosen)
            .Add("Reason", reason)
            .Add(DiscordGrabField.Overview, "Overview", EpisodeOverview(series, episodes))
            .Add(DiscordGrabField.Genres, "Genres", series.Genres is { Count: > 0 } genres ? string.Join(", ", genres.Take(5)) : null)
            .Add(DiscordGrabField.Version, "Version", version?.Name, inline: true)
            .Add(DiscordGrabField.Quality, "Quality", release.Quality, inline: true)
            .Add(DiscordGrabField.Group, "Group", release.ReleaseGroup)
            .Add(DiscordGrabField.Size, "Size", release.Size > 0 ? Bytes(release.Size) : null, inline: true)
            .Add(DiscordGrabField.Links, "Links", ShowLinks(series))
            .Add(DiscordGrabField.Release, "Release", Code(release.Title))
            .Add(DiscordGrabField.Indexer, "Indexer", release.Indexer)
            .Add(DiscordGrabField.CustomFormats, "Custom Formats", Formats(release.CustomFormats))
            .Add(DiscordGrabField.CustomFormatScore, "Custom Format Score", release.CustomFormatScore?.ToString(CultureInfo.InvariantCulture))
            .Add(DiscordGrabField.Tags, "Tags", ShowTags(version))
            .Fields;
        return ShowEmbed(s, series, EpisodeTitle(series, episodes), description, color, at,
            ShowImage(series, ImageType.Poster, chosen.Contains(DiscordGrabField.Poster)), ShowImage(series, ImageType.Fanart, chosen.Contains(DiscordGrabField.Fanart)), fields);
    }

    private static DiscordEmbed EpisodeImport(DiscordSettings s, EpisodeFileImported e, string at)
    {
        var chosen = s.ImportFields ?? DiscordSettings.DefaultImportFields;
        var file = e.File;
        var fields = new FieldList<DiscordImportField>(chosen)
            .Add(DiscordImportField.Overview, "Overview", EpisodeOverview(e.Series, e.Episodes))
            .Add(DiscordImportField.Genres, "Genres", e.Series.Genres is { Count: > 0 } genres ? string.Join(", ", genres.Take(5)) : null)
            .Add(DiscordImportField.Version, "Version", e.Version.Name, inline: true)
            .Add(DiscordImportField.Quality, "Quality", file.Quality, inline: true)
            .Add(DiscordImportField.Codecs, "Codecs", Codecs(file.MediaInfo), inline: true)
            .Add(DiscordImportField.Group, "Group", file.ReleaseGroup)
            .Add(DiscordImportField.Size, "Size", Bytes(file.Size), inline: true)
            .Add(DiscordImportField.Languages, "Languages", Streams(file.MediaInfo?.AudioLanguages))
            .Add(DiscordImportField.Subtitles, "Subtitles", Streams(file.MediaInfo?.Subtitles))
            .Add(DiscordImportField.Links, "Links", ShowLinks(e.Series))
            .Add(DiscordImportField.Release, "Release", Code(file.SceneName ?? e.Release?.Title))
            .Add(DiscordImportField.Tags, "Tags", ShowTags(e.Version))
            .Add(DiscordImportField.CustomFormats, "Custom Formats", Formats(file.CustomFormats))
            .Add(DiscordImportField.CustomFormatScore, "Custom Format Score", file.CustomFormatScore?.ToString(CultureInfo.InvariantCulture))
            .Fields;
        return ShowEmbed(
            s,
            e.Series,
            EpisodeTitle(e.Series, e.Episodes),
            e.IsUpgrade ? "Episode Upgraded" : "Episode Imported",
            e.IsUpgrade ? DiscordColor.Upgrade : DiscordColor.Success,
            at,
            ShowImage(e.Series, ImageType.Poster, chosen.Contains(DiscordImportField.Poster)),
            ShowImage(e.Series, ImageType.Fanart, chosen.Contains(DiscordImportField.Fanart)),
            fields);
    }

    private static DiscordEmbed EpisodeImportFailure(DiscordSettings s, EpisodeImportFailed e, SeriesRef series, string at)
    {
        var chosen = s.ManualInteractionFields ?? DiscordSettings.DefaultManualInteractionFields;
        var fields = new FieldList<DiscordManualInteractionField>(chosen)
            .Add("Reason", e.Reason)
            .Add(DiscordManualInteractionField.Overview, "Overview", EpisodeOverview(series, e.Episodes))
            .Add(DiscordManualInteractionField.Version, "Version", e.Version?.Name, inline: true)
            .Add(DiscordManualInteractionField.Quality, "Quality", e.Release.Quality, inline: true)
            .Add(DiscordManualInteractionField.Size, "Size", e.Release.Size > 0 ? Bytes(e.Release.Size) : null, inline: true)
            .Add(DiscordManualInteractionField.Links, "Links", ShowLinks(series))
            .Add(DiscordManualInteractionField.DownloadTitle, "Download", Code(e.Release.Title))
            .Add(DiscordManualInteractionField.Tags, "Tags", ShowTags(e.Version))
            .Fields;
        return ShowEmbed(s, series, EpisodeTitle(series, e.Episodes), "Import Failed", DiscordColor.Warning, at,
            ShowImage(series, ImageType.Poster, chosen.Contains(DiscordManualInteractionField.Poster)), ShowImage(series, ImageType.Fanart, chosen.Contains(DiscordManualInteractionField.Fanart)), fields);
    }

    /// <summary>Added and deleted shows: the links, and the poster and fanart when the import fields have them, like Sonarr.</summary>
    private static DiscordEmbed Show(DiscordSettings s, SeriesRef series, string description, DiscordColor color, string at)
    {
        var chosen = s.ImportFields ?? DiscordSettings.DefaultImportFields;
        return ShowEmbed(s, series, Cut(series.Title, TitleLimit), description, color, at,
            ShowImage(series, ImageType.Poster, chosen.Contains(DiscordImportField.Poster)), ShowImage(series, ImageType.Fanart, chosen.Contains(DiscordImportField.Fanart)),
            Fields(("Links", ShowLinks(series), false)));
    }

    private static DiscordEmbed ShowEmbed(DiscordSettings s, SeriesRef series, string? title, string description, DiscordColor color, string at, DiscordImage? thumbnail, DiscordImage? image, List<DiscordField>? fields) => new(
        title ?? Cut(series.Title, TitleLimit),
        description,
        series.TvdbId is > 0 ? $"http://thetvdb.com/?tab=series&id={series.TvdbId}" : null,
        (int)color,
        new DiscordAuthor(s.Author ?? InstanceName),
        thumbnail,
        image,
        at,
        fields);

    /// <summary>Sonarr shows the first episode's overview (cut like Radarr's), else the show's.</summary>
    private static string? EpisodeOverview(SeriesRef series, IReadOnlyList<EpisodeRef> episodes) =>
        Cut(episodes.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Overview))?.Overview ?? series.Overview, OverviewLimit + Ellipsis.Length);

    /// <summary>Sonarr's links: TheTVDB and Trakt by the TVDB id, IMDb when known.</summary>
    private static string? ShowLinks(SeriesRef? series)
    {
        if (series is null)
        {
            return null;
        }

        var links = new List<string>();
        if (series.TvdbId is > 0)
        {
            links.Add($"[The TVDB](https://thetvdb.com/?tab=series&id={series.TvdbId})");
            links.Add($"[Trakt](https://trakt.tv/search/tvdb/{series.TvdbId}?id_type=show)");
        }

        if (!string.IsNullOrWhiteSpace(series.ImdbId))
        {
            links.Add($"[IMDB](https://imdb.com/title/{series.ImdbId}/)");
        }

        return links.Count == 0 ? null : string.Join(" / ", links);
    }

    private static string? ShowTags(SeriesVersionRef? version) =>
        version?.Tags is { Count: > 0 } tags ? string.Join(", ", tags.Select(t => t.Label).Where(l => !string.IsNullOrWhiteSpace(l)).Order(StringComparer.Ordinal).Take(5)) : null;

    private static DiscordImage? ShowImage(SeriesRef series, ImageType type, bool chosen) =>
        chosen
        && series.Images?.FirstOrDefault(i => i.CoverType == type)?.RemoteUrl is { } url
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? new DiscordImage(url)
            : null;

    private static string FailedReason(string reason, bool blocklisted, bool searching)
    {
        var followUp = (blocklisted, searching) switch
        {
            (true, true) => "Blocklisted, searching for another release.",
            (true, false) => "Blocklisted.",
            (false, true) => "Searching for another release.",
            _ => null,
        };
        return followUp is null ? reason : $"{reason}\n{followUp}";
    }
}
