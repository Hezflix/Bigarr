using System.Text.Json;
using Bigarr.Api.Features.Notifications.Discord;
using Bigarr.Api.Features.Notifications.Sonarr;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Events;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Notifications;
using Bigarr.Domain.Tags;
using Bigarr.Domain.Tv;

namespace Bigarr.Api.Tests.Notifications;

/// <summary>
/// Sonarr-format bodies against Sonarr v4's payload classes (<c>main</c> branch, <c>NzbDrone.Core/Notifications/Webhook</c>):
/// every member each class declares is written, nothing else, with Sonarr's names and value shapes; the series is the
/// version (the compat line); routing sends series events only, once per version the view sees and the tags match;
/// Discord lays series events out like Sonarr's Discord connection.
/// </summary>
public sealed class SonarrWebhookTests
{
    private const string AppUrl = "https://bigarr.example.org";
    private const string Folder = "/tv/Breaking Bad (2008)";

    private static readonly SeriesRef Show = new(
        3,
        81189,
        1396,
        "tt0903747",
        "Breaking Bad",
        2008,
        SeriesType.Standard,
        "A chemistry teacher turns to crime.",
        ["Drama", "Crime"],
        [new ImageRef(ImageType.Poster, "https://image.tmdb.org/t/p/original/poster.jpg"), new ImageRef(ImageType.Fanart, "https://image.tmdb.org/t/p/original/fanart.jpg")],
        "en",
        "us",
        "AMC");

    private static readonly SeriesVersionRef Version = new(12, "1080p", true, true, "Series 1080p", Folder, [new TagRef(2, "kids"), new TagRef(1, "alice")]);

    private static readonly EpisodeRef Pilot = new(40, 1, 1, "Pilot", null, new DateOnly(2008, 1, 20), new DateTime(2008, 1, 21, 2, 0, 0, DateTimeKind.Utc), 349232, "Walter White, a struggling teacher...");

    private static readonly EpisodeRef Second = new(41, 1, 2, "Cat's in the Bag...", null, new DateOnly(2008, 1, 27), new DateTime(2008, 1, 28, 2, 0, 0, DateTimeKind.Utc), 349235, "Walt and Jesse attempt...");

    private static readonly ReleaseRef Release = new(
        "Breaking.Bad.S01E01.1080p.BluRay.x264-DEMAND", "Bluray-1080p", "Mock", 2_000_000_000, "DEMAND", 50, "abcdef0123", 1, ["English"], IndexerFlags.Freeleech, ["x264"], [5], 100);

    private static readonly FileRef File = new(
        77,
        $"{Folder}/Season 1/Breaking Bad - S01E01 - Pilot Bluray-1080p.mkv",
        "Bluray-1080p",
        2_000_000_000,
        ["x264"],
        100,
        "Season 1/Breaking Bad - S01E01 - Pilot Bluray-1080p.mkv",
        1,
        "DEMAND",
        "Breaking.Bad.S01E01.1080p.BluRay.x264-DEMAND",
        ["English"],
        IndexerFlags.None,
        new MediaInfoRef(5.1, "DTS", ["eng"], 1920, 1080, ["eng"], "x264", "SDR", string.Empty),
        new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc),
        [5],
        "/downloads/Breaking.Bad.S01E01.1080p.BluRay.x264-DEMAND/bb.s01e01.mkv");

    private static readonly FileRef Previous = File with { Id = 70, Quality = "HDTV-720p", RelativePath = "Season 1/Breaking Bad - S01E01 - Pilot HDTV-720p.mkv", Path = $"{Folder}/Season 1/Breaking Bad - S01E01 - Pilot HDTV-720p.mkv", SourcePath = null, RecycleBinPath = "/recycle/Breaking Bad (2008)/Season 1/old.mkv" };

    private static readonly string[] SeriesMembers = ["id", "title", "titleSlug", "path", "tvdbId", "tvMazeId", "tmdbId", "imdbId", "type", "year", "genres", "images", "tags", "originalLanguage"];

    private static readonly string[] EpisodeMembers = ["id", "episodeNumber", "seasonNumber", "title", "overview", "airDate", "airDateUtc", "seriesId", "tvdbId"];

    private static readonly string[] FileMembers = ["id", "relativePath", "path", "quality", "qualityVersion", "releaseGroup", "sceneName", "size", "dateAdded", "languages", "mediaInfo"];

    [Fact]
    public void A_grab_is_sonarrs_grab_payload()
    {
        using var doc = Body(new EpisodeGrabbed(Show, Version, [Pilot], Release, 9, "qBittorrent", "ABCDEF0123", "rss", "QBittorrent"));
        var root = doc.RootElement;

        Members(root, "eventType", "instanceName", "applicationUrl", "series", "episodes", "release", "downloadClient", "downloadClientType", "downloadId", "customFormatInfo");
        Members(root.GetProperty("series"), SeriesMembers);
        Members(root.GetProperty("episodes")[0], EpisodeMembers);
        Members(root.GetProperty("release"), "quality", "qualityVersion", "releaseGroup", "releaseTitle", "indexer", "size", "customFormatScore", "customFormats", "languages");
        Assert.Equal("Grab", root.GetProperty("eventType").GetString());
        Assert.Equal("Bigarr", root.GetProperty("instanceName").GetString());
        Assert.Equal(AppUrl, root.GetProperty("applicationUrl").GetString());

        var series = root.GetProperty("series");
        Assert.Equal(12, series.GetProperty("id").GetInt32()); // the version: the compat series
        Assert.Equal("breaking-bad", series.GetProperty("titleSlug").GetString());
        Assert.Equal(Folder, series.GetProperty("path").GetString());
        Assert.Equal("standard", series.GetProperty("type").GetString());
        Assert.Equal(0, series.GetProperty("tvMazeId").GetInt32());
        Assert.Equal(["alice", "kids"], series.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToArray());
        Assert.Equal("English", series.GetProperty("originalLanguage").GetProperty("name").GetString());
        Assert.Equal("/MediaCover/12/poster.jpg", series.GetProperty("images")[0].GetProperty("url").GetString());

        var episode = root.GetProperty("episodes")[0];
        Assert.Equal("2008-01-20", episode.GetProperty("airDate").GetString());
        Assert.Equal(new DateTime(2008, 1, 21, 2, 0, 0, DateTimeKind.Utc), episode.GetProperty("airDateUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(12, episode.GetProperty("seriesId").GetInt32());
        Assert.Equal(349232, episode.GetProperty("tvdbId").GetInt32());

        Assert.Equal(100, root.GetProperty("release").GetProperty("customFormatScore").GetInt32());
        Assert.Equal(5, root.GetProperty("customFormatInfo").GetProperty("customFormats")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public void An_import_and_an_upgrade_are_sonarrs_download_payload()
    {
        using var imported = Body(new EpisodeFileImported(Show, Version, [Pilot], File, null, Release, 9, false, "Hardlinked", "qBittorrent", "QBittorrent", "ABCDEF0123"));
        var root = imported.RootElement;
        Members(root, "eventType", "instanceName", "applicationUrl", "series", "episodes", "episodeFile", "isUpgrade", "downloadClient", "downloadClientType", "downloadId", "customFormatInfo", "release");
        Members(root.GetProperty("episodeFile"), [.. FileMembers, "sourcePath"]);
        Members(root.GetProperty("release"), "releaseTitle", "indexer", "size", "releaseType");
        Assert.Equal("Download", root.GetProperty("eventType").GetString());
        Assert.False(root.GetProperty("isUpgrade").GetBoolean());
        Assert.Equal("singleEpisode", root.GetProperty("release").GetProperty("releaseType").GetString());
        Assert.Equal(File.SourcePath, root.GetProperty("episodeFile").GetProperty("sourcePath").GetString());
        Assert.Equal(5.1m, root.GetProperty("episodeFile").GetProperty("mediaInfo").GetProperty("audioChannels").GetDecimal());

        using var upgraded = Body(new EpisodeFileImported(Show, Version, [Pilot, Second], File, [Previous], Release with { Title = "Breaking.Bad.S01.1080p.BluRay.x264-DEMAND" }, 9, false, "Hardlinked"));
        Assert.True(upgraded.RootElement.GetProperty("isUpgrade").GetBoolean());
        var deleted = Assert.Single(upgraded.RootElement.GetProperty("deletedFiles").EnumerateArray());
        Members(deleted, [.. FileMembers, "recycleBinPath"]);
        Assert.Equal(Previous.RecycleBinPath, deleted.GetProperty("recycleBinPath").GetString());
        Assert.Equal("seasonPack", upgraded.RootElement.GetProperty("release").GetProperty("releaseType").GetString());
        Assert.Equal(2, upgraded.RootElement.GetProperty("episodes").GetArrayLength());
    }

    [Fact]
    public void Rename_series_and_file_deletions_health_and_manual_interaction_are_sonarrs()
    {
        using var rename = Body(new EpisodeFileRenamed(Show, Version, [new RenamedFileRef(File, $"{Folder}/Season 1/old name.mkv", "Season 1/old name.mkv")]));
        Members(rename.RootElement, "eventType", "instanceName", "applicationUrl", "series", "renamedEpisodeFiles");
        var renamed = rename.RootElement.GetProperty("renamedEpisodeFiles")[0];
        Members(renamed, [.. FileMembers, "sourcePath", "previousRelativePath", "previousPath"]);
        Assert.Equal("Season 1/old name.mkv", renamed.GetProperty("previousRelativePath").GetString());

        using var added = Body(new SeriesAdded(Show, Version));
        Members(added.RootElement, "eventType", "instanceName", "applicationUrl", "series");
        Assert.Equal("SeriesAdd", added.RootElement.GetProperty("eventType").GetString());

        var removed = new SeriesDeleted(Show, true, [Version, Version with { Id = 13, Name = "4k", IsDefault = false }]);
        using var hd = Body(removed, 12);
        using var uhd = Body(removed, 13);
        Members(hd.RootElement, "eventType", "instanceName", "applicationUrl", "series", "deletedFiles");
        Assert.Equal(12, hd.RootElement.GetProperty("series").GetProperty("id").GetInt32());
        Assert.Equal(13, uhd.RootElement.GetProperty("series").GetProperty("id").GetInt32());
        Assert.True(uhd.RootElement.GetProperty("deletedFiles").GetBoolean());

        using var fileDeleted = Body(new EpisodeFileDeleted(Show, Version, [Pilot], File with { RecycleBinPath = "/recycle/x.mkv", SourcePath = null }, "manual"));
        Members(fileDeleted.RootElement, "eventType", "instanceName", "applicationUrl", "series", "episodes", "episodeFile", "deleteReason");
        Members(fileDeleted.RootElement.GetProperty("episodeFile"), FileMembers); // Sonarr does not say where it went
        Assert.Equal("EpisodeFileDelete", fileDeleted.RootElement.GetProperty("eventType").GetString());
        Assert.Equal("manual", fileDeleted.RootElement.GetProperty("deleteReason").GetString());

        using var health = Body(new HealthIssueRaised("indexers", HealthIssueType.Warning, "No indexer"));
        Members(health.RootElement, "eventType", "instanceName", "level", "message", "type");
        Assert.Equal("warning", health.RootElement.GetProperty("level").GetString());

        using var manual = Body(new EpisodeImportFailed(Show, Version, [Pilot], Release, 9, "No video file.", "qBittorrent", "QBittorrent", "ABCDEF0123"));
        Members(manual.RootElement, "eventType", "instanceName", "applicationUrl", "series", "episodes", "downloadInfo", "downloadClient", "downloadClientType", "downloadId", "downloadStatus", "downloadStatusMessages", "customFormatInfo", "release");
        Assert.Equal("ManualInteractionRequired", manual.RootElement.GetProperty("eventType").GetString());

        using var test = JsonDocument.Parse(SonarrWebhook.Serialize(SonarrWebhook.Test(AppUrl)));
        Assert.Equal("Test", test.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(1234, test.RootElement.GetProperty("series").GetProperty("tvdbId").GetInt32());
        Assert.Equal("Test title", test.RootElement.GetProperty("episodes")[0].GetProperty("title").GetString());
    }

    [Fact]
    public void Film_events_and_events_sonarr_has_no_word_for_have_no_body()
    {
        var movie = new MovieRef(1, 603, null, "The Matrix", 1999);
        var film = new VersionRef(2, "1080p", VersionMode.Managed, true, true, "HD");
        DomainEvent[] none =
        [
            new MovieAdded(movie, film),
            new ReleaseGrabbed(movie, film, Release, 9, "qbt", "abc", "rss"),
            new EpisodeDownloadFailed(Show, Version, [Pilot], Release, 9, "Stalled", true, true),
            new SeriesVersionDeleted(Show, Version, false),
        ];

        foreach (var domainEvent in none)
        {
            Assert.Null(SonarrWebhook.Body(domainEvent.Type, EventJson.Serialize(domainEvent), null, AppUrl));
        }

        Assert.Null(SonarrWebhook.Body("episodeImportFailed", EventJson.Serialize(new EpisodeImportFailed(null, null, [], Release, 9, "Nothing matched")), null, AppUrl));
    }

    [Fact]
    public void A_sonarr_endpoint_gets_one_delivery_per_series_version_its_view_and_tags_allow()
    {
        var uhd = Version with { Id = 13, Name = "4k", IsDefault = false, Tags = null };
        var removed = new SeriesDeleted(Show, false, [Version, uhd]);
        var tagsOf = new Dictionary<int, int[]> { [12] = [1], [13] = [7] };
        IEnumerable<int> SeriesTags(SeriesVersionRef v) => tagsOf[v.Id];
        IEnumerable<int> FilmTags(VersionRef v) => [];

        var all = new NotificationEndpoint { Name = "all", Url = "http://x", Format = WebhookFormat.Sonarr, View = CompatView.All };
        Assert.Equal([12, 13], WebhookRouting.Deliveries(all, removed.Type, removed, FilmTags, SeriesTags).Select(v => v!.Value).ToArray());

        var defaultOnly = new NotificationEndpoint { Name = "default", Url = "http://x", Format = WebhookFormat.Sonarr };
        Assert.Equal([12], WebhookRouting.Deliveries(defaultOnly, removed.Type, removed, FilmTags, SeriesTags).Select(v => v!.Value).ToArray());

        var tagged = new NotificationEndpoint { Name = "tagged", Url = "http://x", Format = WebhookFormat.Sonarr, View = CompatView.All, Tags = [new Tag { Id = 7, Label = "uhd" }] };
        Assert.Equal([13], WebhookRouting.Deliveries(tagged, removed.Type, removed, FilmTags, SeriesTags).Select(v => v!.Value).ToArray());

        // Health ignores tags; a film event has nothing in Sonarr's format; a Radarr endpoint never sees a series event.
        var health = new HealthIssueRaised("indexers", HealthIssueType.Error, "down");
        Assert.Equal([null], WebhookRouting.Deliveries(tagged, health.Type, health, FilmTags, SeriesTags));
        var film = new MovieAdded(new MovieRef(1, 603, null, "The Matrix", 1999), new VersionRef(2, "1080p", VersionMode.Managed, true, true, "HD"));
        Assert.Empty(WebhookRouting.Deliveries(all, film.Type, film, FilmTags, SeriesTags));
        var radarr = new NotificationEndpoint { Name = "radarr", Url = "http://x", Format = WebhookFormat.Radarr, View = CompatView.All };
        Assert.Empty(WebhookRouting.Deliveries(radarr, removed.Type, removed, FilmTags, SeriesTags));

        // The Bigarr format with tags: a series event matches through its versions' tags.
        var bigarr = new NotificationEndpoint { Name = "bigarr", Url = "http://x", Tags = [new Tag { Id = 7, Label = "uhd" }] };
        Assert.Equal([null], WebhookRouting.Deliveries(bigarr, removed.Type, removed, FilmTags, SeriesTags));
        var grab = new EpisodeGrabbed(Show, Version, [Pilot], Release, 9, "qbt", "abc", "rss");
        Assert.Empty(WebhookRouting.Deliveries(bigarr, grab.Type, grab, FilmTags, SeriesTags));
    }

    [Fact]
    public void Discord_lays_series_events_out_like_sonarr()
    {
        var grab = DiscordWebhook.Payload(new EpisodeGrabbed(Show, Version, [Pilot, Second], Release, 9, "qbt", "abc", "rss"), null)!;
        var embed = Assert.Single(grab.Embeds!);
        Assert.Equal("Breaking Bad - 1x01x02 - Pilot + Cat's in the Bag...", embed.Title);
        Assert.Equal("Episode Grabbed", embed.Description);
        Assert.Equal("http://thetvdb.com/?tab=series&id=81189", embed.Url);
        Assert.Contains(embed.Fields!, f => f.Name == "Links" && f.Value.Contains("[The TVDB](https://thetvdb.com/?tab=series&id=81189)", StringComparison.Ordinal) && f.Value.Contains("[IMDB](https://imdb.com/title/tt0903747/)", StringComparison.Ordinal));
        Assert.Contains(embed.Fields!, f => f.Name == "Version" && f.Value == "1080p");
        Assert.Equal("Walter White, a struggling teacher...", embed.Fields!.Single(f => f.Name == "Overview").Value);

        var upgrade = Assert.Single(DiscordWebhook.Payload(new EpisodeFileImported(Show, Version, [Pilot], File, [Previous], Release, 9, false, "Hardlinked"), null)!.Embeds!);
        Assert.Equal("Episode Upgraded", upgrade.Description);

        var daily = Show with { Title = "The Daily Show", Type = SeriesType.Daily, TvdbId = 71256 };
        var tonight = new EpisodeRef(90, 31, 127, "TBA", null, new DateOnly(2026, 11, 9));
        Assert.Equal("The Daily Show - 2026-11-09 - TBA", Assert.Single(DiscordWebhook.Payload(new EpisodeGrabbed(daily, Version, [tonight], Release, 9, "qbt", "abc", "rss"), null)!.Embeds!).Title);

        var renamed = DiscordWebhook.Payload(new EpisodeFileRenamed(Show, Version, [new RenamedFileRef(File, "/old.mkv")]), null)!;
        Assert.Equal("Renamed", renamed.Content);
        Assert.Equal("Breaking Bad", Assert.Single(renamed.Embeds!).Title);

        var failed = Assert.Single(DiscordWebhook.Payload(new EpisodeDownloadFailed(Show, Version, [Pilot], Release, 9, "Stalled", true, true), null)!.Embeds!);
        Assert.Equal("Download Failed", failed.Description);
        Assert.Contains("Blocklisted, searching for another release.", failed.Fields!.Single(f => f.Name == "Reason").Value, StringComparison.Ordinal);
        Assert.True(WebhookRouting.SentToDiscord("episodeDownloadFailed"));
    }

    private static JsonDocument Body(DomainEvent domainEvent, int? versionId = null) =>
        JsonDocument.Parse(SonarrWebhook.Body(domainEvent.Type, EventJson.Serialize(domainEvent), versionId, AppUrl) ?? throw new InvalidOperationException($"{domainEvent.Type} has no body"));

    /// <summary>Exactly these members: Sonarr's class declares them all and the fixture fills them all.</summary>
    private static void Members(JsonElement element, params string[] expected) =>
        Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
}
