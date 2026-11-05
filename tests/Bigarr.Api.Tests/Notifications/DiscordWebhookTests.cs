using System.Text.Json.Nodes;
using Bigarr.Api.Features.Notifications.Discord;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Events;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Notifications;

namespace Bigarr.Api.Tests.Notifications;

/// <summary>
/// Discord-format bodies: each event as Radarr's Discord connection lays it out (title, colors, fields in Radarr's
/// order), the version Bigarr adds, the failures Radarr does not post, the settings, and Discord's limits.
/// </summary>
public sealed class DiscordWebhookTests
{
    private const string Folder = "/movies/The Matrix (1999) {4k}";
    private const string ReleaseTitle = "The.Matrix.1999.2160p.UHD.BluRay.x265-GROUP";
    private static readonly DateTime OccurredAt = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);

    private static readonly MovieRef Movie = new(
        7,
        603,
        "tt0133093",
        "The Matrix",
        1999,
        "A hacker learns the truth about his reality.",
        ["Action", "Science Fiction"],
        [new ImageRef(ImageType.Poster, "https://image.tmdb.org/t/p/original/poster.jpg"), new ImageRef(ImageType.Fanart, "https://image.tmdb.org/t/p/original/fanart.jpg")],
        new DateOnly(1999, 3, 31),
        new DateOnly(1999, 9, 21),
        null,
        "en",
        new RatingsRef(new RatingValue(8.218, 26000), new RatingValue(8.7, 2000000)));

    private static readonly VersionRef Version = new(
        42, "4k", VersionMode.Managed, false, true, "4K", Folder, [new TagRef(3, "kids"), new TagRef(1, "alice")], Path.Combine(Folder, "The Matrix (1999) [Bluray-2160p].mkv"), 25_000_000_000);

    private static readonly ReleaseRef Release = new(
        ReleaseTitle, "Bluray-2160p", "Mock", 25_000_000_000, "GROUP", 50, "abcdef0123", 1, ["English"], IndexerFlags.Freeleech, ["x265 (HD)", "UHD Bluray"], [5, 7], 1500);

    private static readonly FileRef File = new(
        77,
        Path.Combine(Folder, "The Matrix (1999) [Bluray-2160p].mkv"),
        "Bluray-2160p",
        25_000_000_000,
        ["x265 (HD)"],
        100,
        "The Matrix (1999) [Bluray-2160p].mkv",
        1,
        "GROUP",
        ReleaseTitle,
        ["English"],
        IndexerFlags.Freeleech,
        new MediaInfoRef(5.1, "EAC3", ["eng", "eng"], 3840, 2160, ["eng", "fre"], "x265", "HDR", "HDR10"),
        OccurredAt,
        [5]);

    private static readonly FileRef Previous = File with { Id = 70, Quality = "Bluray-1080p", Path = Path.Combine(Folder, "old.mkv") };

    private static DomainEvent At(DomainEvent domainEvent) => domainEvent with { OccurredAt = OccurredAt };

    private static JsonNode Body(DomainEvent domainEvent, DiscordSettings? settings = null) =>
        JsonNode.Parse(DiscordWebhook.Serialize(DiscordWebhook.Payload(domainEvent, settings)!))!;

    private static JsonNode Embed(DomainEvent domainEvent, DiscordSettings? settings = null) => Body(domainEvent, settings)["embeds"]![0]!;

    private static List<(string Name, string Value, bool Inline)> Fields(JsonNode embed) =>
        embed["fields"]?.AsArray().Select(f => (f!["name"]!.GetValue<string>(), f["value"]!.GetValue<string>(), f["inline"]!.GetValue<bool>())).ToList() ?? [];

    [Fact]
    public void A_grab_is_Radarrs_embed_with_the_version()
    {
        var body = Body(At(new ReleaseGrabbed(Movie, Version, Release, 9, "qBittorrent", "ABCDEF0123", "rss", "QBittorrent")));
        var embed = body["embeds"]![0]!;

        Assert.Null(body["content"]);
        Assert.Null(body["username"]);
        Assert.Equal("The Matrix (1999)", embed["title"]!.GetValue<string>());
        Assert.Equal("Movie Grabbed", embed["description"]!.GetValue<string>());
        Assert.Equal("https://www.themoviedb.org/movie/603", embed["url"]!.GetValue<string>());
        Assert.Equal((int)DiscordColor.Standard, embed["color"]!.GetValue<int>());
        Assert.Equal("Bigarr", embed["author"]!["name"]!.GetValue<string>());
        Assert.Equal("https://image.tmdb.org/t/p/original/poster.jpg", embed["thumbnail"]!["url"]!.GetValue<string>());
        Assert.Equal("https://image.tmdb.org/t/p/original/fanart.jpg", embed["image"]!["url"]!.GetValue<string>());
        Assert.Equal("2026-11-05T10:00:00.0000000Z", embed["timestamp"]!.GetValue<string>());
        Assert.Equal(
            [
                ("Overview", "A hacker learns the truth about his reality.", false),
                ("Rating", "8.2", false),
                ("Genres", "Action, Science Fiction", false),
                ("Version", "4k", true),
                ("Quality", "Bluray-2160p", true),
                ("Group", "GROUP", false),
                ("Size", "23.3 GB", true),
                ("Links", "[TMDb](https://themoviedb.org/movie/603) / [Trakt](https://trakt.tv/movies/tt0133093) / [IMDb](https://imdb.com/title/tt0133093/)", false),
                ("Release", $"```{ReleaseTitle}```", false),
                ("Indexer", "Mock", false),
                ("Custom Formats", "x265 (HD)|UHD Bluray", false),
                ("Custom Format Score", "1500", false),
                ("Tags", "alice, kids", false),
            ],
            Fields(embed));
    }

    [Fact]
    public void An_import_and_an_upgrade_differ_by_their_wording_and_color()
    {
        var imported = Embed(At(new FileImported(Movie, Version, File, Release, 9, false, "hardlink", "qBittorrent", "QBittorrent", "ABCDEF0123")));
        var upgraded = Embed(At(new FileUpgraded(Movie, Version, File, Previous, Release, 9, false, "hardlink", "qBittorrent", "QBittorrent", "ABCDEF0123")));

        Assert.Equal("Movie Imported", imported["description"]!.GetValue<string>());
        Assert.Equal((int)DiscordColor.Success, imported["color"]!.GetValue<int>());
        Assert.Equal("Movie Upgraded", upgraded["description"]!.GetValue<string>());
        Assert.Equal((int)DiscordColor.Upgrade, upgraded["color"]!.GetValue<int>());

        // Radarr's default import fields: no custom formats.
        Assert.Equal(
            ["Overview", "Rating", "Genres", "Version", "Quality", "Codecs", "Group", "Size", "Languages", "Subtitles", "Links", "Release", "Tags"],
            Fields(imported).Select(f => f.Name));
        var fields = Fields(imported).ToDictionary(f => f.Name, f => f.Value);
        Assert.Equal("x265 / EAC3 5.1", fields["Codecs"]);
        Assert.Equal("eng", fields["Languages"]);
        Assert.Equal("eng/fre", fields["Subtitles"]);
        Assert.Equal($"```{ReleaseTitle}```", fields["Release"]);
        Assert.Equal(Fields(imported), Fields(upgraded));
    }

    [Fact]
    public void The_settings_pick_the_name_the_avatar_the_author_and_the_fields()
    {
        var settings = new DiscordSettings("Bigarr bot", "https://example.org/avatar.png", "Home", GrabFields: [DiscordGrabField.Quality, DiscordGrabField.Version, DiscordGrabField.Fanart]);
        var body = Body(At(new ReleaseGrabbed(Movie, Version, Release, 9, "qBittorrent", "ABCDEF0123", "rss")), settings);
        var embed = body["embeds"]![0]!;

        Assert.Equal("Bigarr bot", body["username"]!.GetValue<string>());
        Assert.Equal("https://example.org/avatar.png", body["avatar_url"]!.GetValue<string>());
        Assert.Equal("Home", embed["author"]!["name"]!.GetValue<string>());
        Assert.Null(embed["thumbnail"]);
        Assert.NotNull(embed["image"]);

        // Radarr's order, whatever the order they were picked in.
        Assert.Equal([("Version", "4k", true), ("Quality", "Bluray-2160p", true)], Fields(embed));
        Assert.Empty(Fields(Embed(At(new ReleaseGrabbed(Movie, Version, Release, 9, "qBittorrent", "ABCDEF0123", "rss")), new DiscordSettings(GrabFields: []))));
    }

    [Fact]
    public void Added_deleted_renamed_and_deleted_files_follow_Radarr()
    {
        var added = Embed(At(new MovieAdded(Movie, Version)));
        Assert.Equal("Movie Added", added["description"]!.GetValue<string>());
        Assert.Equal((int)DiscordColor.Success, added["color"]!.GetValue<int>());
        Assert.Equal(["Links"], Fields(added).Select(f => f.Name));
        Assert.NotNull(added["thumbnail"]);
        Assert.Null(Embed(At(new MovieAdded(Movie, Version)), new DiscordSettings(ImportFields: []))["thumbnail"]);

        Assert.Equal("Movie removed and all files were deleted", Embed(At(new MovieDeleted(Movie, true, [Version])))["description"]!.GetValue<string>());
        var kept = Embed(At(new MovieDeleted(Movie, false, [Version])));
        Assert.Equal("Movie removed, files were not deleted", kept["description"]!.GetValue<string>());
        Assert.Equal((int)DiscordColor.Danger, kept["color"]!.GetValue<int>());

        var renamed = Body(At(new FileRenamed(Movie, Version, File with { RelativePath = "The Matrix (1999) {4k} [Bluray-2160p].mkv" }, Path.Combine(Folder, "old name.mkv"))));
        Assert.Equal("Renamed", renamed["content"]!.GetValue<string>());
        Assert.Equal("old name.mkv renamed to The Matrix (1999) {4k} [Bluray-2160p].mkv", renamed["embeds"]![0]!["description"]!.GetValue<string>());
        Assert.Null(renamed["embeds"]![0]!["color"]);

        var fileDeleted = Embed(At(new FileDeleted(Movie, Version, File, "recycled")));
        Assert.Equal("Movie File Deleted", fileDeleted["description"]!.GetValue<string>());
        Assert.Equal((int)DiscordColor.Danger, fileDeleted["color"]!.GetValue<int>());
        Assert.Equal([("Version", "4k", true), ("Reason", "Moved to the recycle bin", true), ("File name", $"```{File.Path}```", false)], Fields(fileDeleted));
    }

    [Fact]
    public void Health_issues_are_colored_by_level()
    {
        var error = Embed(At(new HealthIssueRaised("indexers", HealthIssueType.Error, "No indexer is enabled")));
        Assert.Equal("indexers", error["title"]!.GetValue<string>());
        Assert.Equal("No indexer is enabled", error["description"]!.GetValue<string>());
        Assert.Equal((int)DiscordColor.Danger, error["color"]!.GetValue<int>());
        Assert.Null(error["url"]);
        Assert.Equal((int)DiscordColor.Warning, Embed(At(new HealthIssueRaised("indexers", HealthIssueType.Warning, "Slow")))["color"]!.GetValue<int>());

        var restored = Embed(At(new HealthIssueResolved("indexers", "No indexer is enabled", HealthIssueType.Error)));
        Assert.Equal("Health Issue Resolved: indexers", restored["title"]!.GetValue<string>());
        Assert.Equal("The following issue is now resolved: No indexer is enabled", restored["description"]!.GetValue<string>());
        Assert.Equal((int)DiscordColor.Success, restored["color"]!.GetValue<int>());
    }

    [Fact]
    public void Failures_Radarr_keeps_to_itself_say_why()
    {
        var download = Embed(At(new DownloadFailed(Movie, Version, Release, 9, "The torrent stalled", Blocklisted: true, SearchRequested: true)));
        Assert.Equal("Download Failed", download["description"]!.GetValue<string>());
        Assert.Equal((int)DiscordColor.Danger, download["color"]!.GetValue<int>());
        Assert.Equal(("Reason", "The torrent stalled\nBlocklisted, searching for another release.", false), Fields(download)[0]);
        Assert.Contains(("Release", $"```{ReleaseTitle}```", false), Fields(download));

        var import = Embed(At(new ImportFailed(Movie, Version, Release, 9, "Not an upgrade for 4k", "qBittorrent")));
        Assert.Equal("Import Failed", import["description"]!.GetValue<string>());
        Assert.Equal((int)DiscordColor.Warning, import["color"]!.GetValue<int>());
        Assert.Equal(
            ["Reason", "Overview", "Rating", "Genres", "Version", "Quality", "Group", "Size", "Links", "Download", "Tags"],
            Fields(import).Select(f => f.Name));

        // About no film: the release names it.
        var orphan = Embed(At(new ImportFailed(null, null, Release, 9, "No film matches")));
        Assert.Equal(ReleaseTitle, orphan["title"]!.GetValue<string>());
        Assert.Null(orphan["url"]);
        Assert.Null(orphan["thumbnail"]);
    }

    [Fact]
    public void The_test_message_is_an_embed()
    {
        var body = JsonNode.Parse(DiscordWebhook.Serialize(DiscordWebhook.Test("Bigarr can reach Discord.", OccurredAt, new DiscordSettings("Bigarr bot"))))!;
        var embed = body["embeds"]![0]!;

        Assert.Equal("Bigarr bot", body["username"]!.GetValue<string>());
        Assert.Equal("Test message from Bigarr", embed["title"]!.GetValue<string>());
        Assert.Equal("Bigarr can reach Discord.", embed["description"]!.GetValue<string>());
        Assert.Equal((int)DiscordColor.Standard, embed["color"]!.GetValue<int>());
    }

    [Fact]
    public void Version_bookkeeping_posts_nothing()
    {
        Assert.Null(DiscordWebhook.Payload(new VersionAdded(Movie, Version), null));
        Assert.Null(DiscordWebhook.Payload(new VersionUpdated(Movie, Version), null));
        Assert.Null(DiscordWebhook.Payload(new VersionDeleted(Movie, Version, false), null));
        Assert.Null(DiscordWebhook.Payload(new DefaultVersionChanged(Movie, Version, null), null));
        Assert.Null(DiscordWebhook.Body("versionAdded", EventJson.Serialize(new VersionAdded(Movie, Version)), null));
        Assert.NotNull(DiscordWebhook.Body("movieAdded", EventJson.Serialize(new MovieAdded(Movie, Version)), null));
    }

    [Fact]
    public void Every_text_is_cut_to_Discords_limits()
    {
        var longTitle = new string('T', 300) + "`";
        var movie = Movie with { Title = longTitle, Overview = new string('o', 5000), Genres = ["a", "b", "c", "d", "e", "f"] };
        var release = Release with { Title = new string('r', 2000), CustomFormats = Enumerable.Range(0, 400).Select(i => $"Format {i}").ToList() };
        var embed = Embed(At(new ReleaseGrabbed(movie, Version, release, 9, "qBittorrent", "ABCDEF0123", "rss")), new DiscordSettings(Author: new string('a', 400)));
        var fields = Fields(embed).ToDictionary(f => f.Name, f => f.Value);

        Assert.Equal(DiscordWebhook.TitleLimit, embed["title"]!.GetValue<string>().Length);
        Assert.EndsWith("...", embed["title"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(DiscordWebhook.AuthorLimit, embed["author"]!["name"]!.GetValue<string>().Length);
        Assert.Equal(303, fields["Overview"].Length);
        Assert.Equal("a, b, c, d, e", fields["Genres"]);
        Assert.Equal(DiscordWebhook.FieldValueLimit, fields["Release"].Length);
        Assert.StartsWith("```rrr", fields["Release"], StringComparison.Ordinal);
        Assert.EndsWith("...```", fields["Release"], StringComparison.Ordinal);
        Assert.True(fields["Custom Formats"].Length <= DiscordWebhook.FieldValueLimit);

        var health = Embed(At(new HealthIssueRaised("indexers", HealthIssueType.Error, new string('m', 10_000))));
        Assert.Equal(DiscordWebhook.DescriptionLimit, health["description"]!.GetValue<string>().Length);
    }

    [Fact]
    public void A_message_over_6000_characters_shortens_its_longest_texts()
    {
        var fields = Enumerable.Range(0, 30).Select(i => new DiscordField($"Field {i}", new string('v', 1000))).ToList();
        var embed = new DiscordEmbed("Title", new string('d', 3000), null, (int)DiscordColor.Standard, new DiscordAuthor("Bigarr"), null, null, null, fields);

        var fitted = DiscordWebhook.Fit(new DiscordPayload(null, null, null, [embed])).Embeds!.Single();

        Assert.Equal(DiscordWebhook.FieldCountLimit, fitted.Fields!.Count);
        Assert.True(DiscordWebhook.Length(fitted) <= DiscordWebhook.TotalLimit, $"{DiscordWebhook.Length(fitted)} characters");
        Assert.All(fitted.Fields, f => Assert.False(string.IsNullOrEmpty(f.Value)));
        Assert.StartsWith("Field 0", fitted.Fields[0].Name, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cut_never_splits_a_surrogate_pair()
    {
        var overview = new string('o', 299) + "😀😀😀";
        var grab = Embed(At(new ReleaseGrabbed(Movie with { Overview = overview }, Version, Release, 9, "qBittorrent", "ABCDEF0123", "rss")));

        Assert.Equal(new string('o', 299) + "...", Fields(grab).Single(f => f.Name == "Overview").Value);
    }

    [Theory]
    [InlineData("""{"message": "Cannot send an empty message", "code": 50006}""", "Discord: Cannot send an empty message (50006)")]
    [InlineData("""{"message": "Unknown Webhook", "code": 10015}""", "Discord: Unknown Webhook (10015)")]
    [InlineData("""{"message": "You are being rate limited.", "retry_after": 1.5, "global": false}""", "Discord: You are being rate limited.")]
    [InlineData(
        """{"message": "Invalid Form Body", "code": 50035, "errors": {"embeds": {"0": {"title": {"_errors": [{"code": "BASE_TYPE_MAX_LENGTH", "message": "Must be 256 or fewer in length."}]}}}}}""",
        "Discord: Invalid Form Body (50035): embeds.0.title: Must be 256 or fewer in length.")]
    [InlineData("<html>Bad gateway</html>", null)]
    [InlineData("""{"error": "nope"}""", null)]
    [InlineData("", null)]
    public void Discords_error_answer_becomes_the_failure_text(string body, string? expected)
    {
        Assert.Equal(expected, DiscordWebhook.Error(body));
    }

    [Fact]
    public void Discords_retry_after_is_read_in_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(1.5), DiscordWebhook.RetryAfter("""{"message": "You are being rate limited.", "retry_after": 1.5, "global": false}"""));
        Assert.Null(DiscordWebhook.RetryAfter("""{"message": "You are being rate limited."}"""));
        Assert.Null(DiscordWebhook.RetryAfter("not json"));
    }

    [Theory]
    [InlineData("https://discord.com/api/webhooks/123456789/fake-token", true)]
    [InlineData("https://discordapp.com/api/webhooks/123456789/fake_token", true)]
    [InlineData("https://ptb.discord.com/api/webhooks/123456789/fake-token", true)]
    [InlineData("https://canary.discord.com/api/webhooks/123456789/fake-token", true)]
    [InlineData("https://discord.com/api/v10/webhooks/123456789/fake-token", true)]
    [InlineData("https://discord.com/api/webhooks/123456789/fake-token?thread_id=42", true)]
    [InlineData("http://discord.com/api/webhooks/123456789/fake-token", false)]
    [InlineData("https://discord.com:8443/api/webhooks/123456789/fake-token", false)]
    [InlineData("https://discord.com/api/webhooks/123456789", false)]
    [InlineData("https://discord.com/api/webhooks/abc/fake-token", false)]
    [InlineData("https://discord.com/api/webhooks/123456789/fake-token/slack", false)]
    [InlineData("https://discord.example.org/api/webhooks/123456789/fake-token", false)]
    [InlineData("https://notdiscord.com/api/webhooks/123456789/fake-token", false)]
    public void Only_a_channel_webhook_URL_takes_the_Discord_format(string url, bool valid)
    {
        Assert.Equal(valid, DiscordWebhookUrl.IsChannelWebhook(new Uri(url)));
    }

    [Theory]
    [InlineData("https://discord.com/api/webhooks/123456789/fake-token", true)]
    [InlineData("https://discord.com/api/webhooks/123456789/fake-token/slack", true)]
    [InlineData("http://discordapp.com/api/webhooks/123456789/fake-token", true)]
    [InlineData("https://discord.com/channels/1/2", false)]
    [InlineData("https://example.org/api/webhooks/123456789/fake-token", false)]
    public void Any_Discord_webhook_URL_is_recognized(string url, bool webhook)
    {
        Assert.Equal(webhook, DiscordWebhookUrl.IsAnyWebhook(new Uri(url)));
    }
}
