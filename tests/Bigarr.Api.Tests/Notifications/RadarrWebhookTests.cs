using System.Text.Json;
using System.Text.Json.Nodes;
using Bigarr.Api.Features.Notifications.Radarr;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Events;
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Tests.Notifications;

/// <summary>
/// Radarr-format bodies: each mapped event against what Radarr's own payload classes write for the same data
/// (<c>RadarrSamples/</c>), and the Bigarr format carrying at least as much.
/// </summary>
public sealed class RadarrWebhookTests
{
    private const string AppUrl = "https://bigarr.example.org";
    private const string Folder = "/movies/The Matrix (1999) {4k}";
    private const string ReleaseTitle = "The.Matrix.1999.2160p.UHD.BluRay.x265-GROUP";

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
        "en");

    private static readonly VersionRef Version = new(
        42, "4k", VersionMode.Managed, false, true, "4K", Folder, [new TagRef(3, "kids"), new TagRef(1, "alice")], Path.Combine(Folder, "The Matrix (1999) [Bluray-2160p].mkv"), 25_000_000_000);

    private static readonly ReleaseRef Release = new(
        ReleaseTitle, "Bluray-2160p", "Mock", 25_000_000_000, "GROUP", 50, "abcdef0123", 1, ["English"], IndexerFlags.Freeleech | IndexerFlags.Internal, ["x265 (HD)", "UHD Bluray"], [5, 7], 1500);

    private static readonly FileRef Previous = new(
        70,
        Path.Combine(Folder, "The Matrix (1999) [Bluray-1080p].mkv"),
        "Bluray-1080p",
        10_000_000_000,
        RelativePath: "The Matrix (1999) [Bluray-1080p].mkv",
        QualityVersion: 1,
        ReleaseGroup: "OLD",
        SceneName: "The.Matrix.1999.1080p.BluRay.x264-OLD",
        Languages: ["English"],
        IndexerFlags: IndexerFlags.None,
        DateAdded: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        RecycleBinPath: "/recycle/The Matrix (1999) [Bluray-1080p].mkv");

    private static FileRef File(string relativePath) => new(
        77,
        Path.Combine(Folder, relativePath),
        "Bluray-2160p",
        25_000_000_000,
        ["x265 (HD)"],
        100,
        relativePath,
        1,
        "GROUP",
        ReleaseTitle,
        ["English"],
        IndexerFlags.Freeleech,
        new MediaInfoRef(5.1, "EAC3", ["eng", "eng"], 3840, 2160, ["eng", "fre"], "x265", "HDR", "HDR10"),
        new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc),
        [5]);

    private static FileRef Imported => File("The Matrix (1999) [Bluray-2160p].mkv") with { SourcePath = $"/downloads/{ReleaseTitle}/the.matrix.mkv" };

    /// <summary>Sample name, event, the delivery's version.</summary>
    public static TheoryData<string, DomainEvent, int?> MappedEvents => new()
    {
        { "grab", new ReleaseGrabbed(Movie, Version, Release, 9, "qBittorrent", "ABCDEF0123", "rss", "QBittorrent"), 42 },
        { "download", new FileImported(Movie, Version, Imported, Release, 9, false, "hardlink", "qBittorrent", "QBittorrent", "ABCDEF0123"), 42 },
        { "upgrade", new FileUpgraded(Movie, Version, Imported, Previous, Release, 9, false, "hardlink", "qBittorrent", "QBittorrent", "ABCDEF0123"), 42 },
        { "rename", new FileRenamed(Movie, Version, File("The Matrix (1999) {4k} [Bluray-2160p].mkv"), Path.Combine(Folder, "The Matrix (1999) [Bluray-2160p].mkv")), 42 },
        { "movieAdded", new MovieAdded(Movie, Version), 42 },
        { "movieDelete", new MovieDeleted(Movie, true, [Version with { Id = 41, Name = "1080p", IsDefault = true }, Version]), 42 },
        { "movieFileDelete", new FileDeleted(Movie, Version, File("The Matrix (1999) [Bluray-2160p].mkv") with { RecycleBinPath = "/recycle/x.mkv" }, "recycled"), 42 },
        { "health", new HealthIssueRaised("indexers", HealthIssueType.Warning, "No indexer is enabled"), null },
        { "healthRestored", new HealthIssueResolved("indexers", "No indexer is enabled", HealthIssueType.Warning), null },
        { "manualInteractionRequired", new ImportFailed(Movie, Version, Release, 9, "No video file in the download.", "qBittorrent", "QBittorrent", "ABCDEF0123"), 42 },
        { "test", new TestEvent("Bigarr can reach the sink."), null },
    };

    [Theory]
    [MemberData(nameof(MappedEvents))]
    public void Each_mapped_event_is_what_Radarr_sends(string sample, DomainEvent domainEvent, int? versionId)
    {
        // Through the outbox's JSON, as the sender reads it.
        var body = RadarrWebhook.Body(domainEvent.Type, EventJson.Serialize(domainEvent), versionId, AppUrl);

        Assert.NotNull(body);
        Assert.Equal(Canonical(JsonNode.Parse(Sample(sample))), Canonical(JsonNode.Parse(body)));
    }

    [Theory]
    [MemberData(nameof(MappedEvents))]
    public void The_Bigarr_format_carries_every_value_Radarr_sends(string sample, DomainEvent domainEvent, int? versionId)
    {
        _ = versionId;
        if (sample == "test")
        {
            return; // Radarr's test describes a fake film, not the event
        }

        var bigarr = Leaves(JsonNode.Parse(WebhookClient.Body(domainEvent.Type, 1, EventJson.Serialize(domainEvent), domainEvent.OccurredAt, AppUrl)), null);
        var missing = Leaves(JsonNode.Parse(Sample(sample)), null).Except(bigarr).ToList();

        Assert.True(missing.Count == 0, $"{sample}: Radarr sends {string.Join(", ", missing)} and the Bigarr format does not.");
    }

    [Theory]
    [InlineData("versionAdded")]
    [InlineData("versionUpdated")]
    [InlineData("versionDeleted")]
    [InlineData("downloadFailed")]
    [InlineData("defaultVersionChanged")]
    public void Events_Radarr_has_no_word_for_have_no_body(string type)
    {
        DomainEvent domainEvent = type switch
        {
            "versionAdded" => new VersionAdded(Movie, Version),
            "versionUpdated" => new VersionUpdated(Movie, Version),
            "versionDeleted" => new VersionDeleted(Movie, Version, false),
            "downloadFailed" => new DownloadFailed(Movie, Version, Release, 9, "Stalled", true, true),
            _ => new DefaultVersionChanged(Movie, Version, null),
        };

        Assert.Equal(type, domainEvent.Type);
        Assert.Null(RadarrWebhook.Body(type, EventJson.Serialize(domainEvent), Version.Id, AppUrl));
    }

    [Fact]
    public void An_import_failure_about_no_film_has_no_body()
    {
        var failed = new ImportFailed(null, null, Release, 9, "No film matches this release.");

        Assert.Null(RadarrWebhook.Body(failed.Type, EventJson.Serialize(failed), null, AppUrl));
    }

    [Fact]
    public void Each_version_of_a_deleted_film_is_its_own_Radarr_movie()
    {
        var deleted = new MovieDeleted(Movie, false, [Version with { Id = 41, Name = "1080p", IsDefault = true, Folder = "/movies/The Matrix (1999)" }, Version]);
        var json = EventJson.Serialize(deleted);

        using var hd = JsonDocument.Parse(RadarrWebhook.Body(deleted.Type, json, 41, AppUrl)!);
        using var uhd = JsonDocument.Parse(RadarrWebhook.Body(deleted.Type, json, 42, AppUrl)!);
        Assert.Equal(41, hd.RootElement.GetProperty("movie").GetProperty("id").GetInt32());
        Assert.Equal("/movies/The Matrix (1999)", hd.RootElement.GetProperty("movie").GetProperty("folderPath").GetString());
        Assert.Equal(0, hd.RootElement.GetProperty("movieFolderSize").GetInt64()); // files kept
        Assert.Equal(42, uhd.RootElement.GetProperty("movie").GetProperty("id").GetInt32());

        // A delivery created before the endpoint had a version: the film's default version.
        using var fallback = JsonDocument.Parse(RadarrWebhook.Body(deleted.Type, json, null, AppUrl)!);
        Assert.Equal(41, fallback.RootElement.GetProperty("movie").GetProperty("id").GetInt32());
    }

    [Fact]
    public void An_event_stored_before_the_snapshots_were_enriched_still_reads()
    {
        // The shape the outbox held before: no overview, tags, file details or client type.
        var old = """{"movie":{"id":7,"tmdbId":603,"imdbId":"tt0133093","title":"The Matrix","year":1999},"version":{"id":42,"name":"4k","mode":"managed","isDefault":true,"monitored":true,"qualityProfile":"4K"},"release":{"title":"The.Matrix.1999.2160p.UHD.BluRay.x265-GROUP","quality":"Bluray-2160p","indexer":"Mock","size":1},"grabId":9,"downloadClient":"qBittorrent","downloadId":"abc","trigger":"rss","occurredAt":"2026-09-15T10:00:00Z"}""";

        using var doc = JsonDocument.Parse(RadarrWebhook.Body("grabbed", old, 42, AppUrl)!);
        var movie = doc.RootElement.GetProperty("movie");
        Assert.Equal("Grab", doc.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(42, movie.GetProperty("id").GetInt32());
        Assert.Equal("9999-12-31", movie.GetProperty("releaseDate").GetString());
        Assert.Equal(0, movie.GetProperty("tags").GetArrayLength());
        Assert.Equal("Bluray-2160p", doc.RootElement.GetProperty("release").GetProperty("quality").GetString());
    }

    private static string Sample(string name)
    {
        using var stream = typeof(RadarrWebhookTests).Assembly.GetManifestResourceStream($"RadarrWebhook.{name}.json")
            ?? throw new InvalidOperationException($"Sample {name} not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Keys sorted at every level: Radarr writes the base class's members last, the order means nothing.</summary>
    private static string Canonical(JsonNode? node) => Sort(node)?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sort(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Sort).ToArray()),
        _ => node?.DeepClone(),
    };

    /// <summary>
    /// Every scalar value of a body. Radarr's renderings of a Bigarr value in its own vocabulary are left out: the
    /// event name, the flags' names, the cover URLs under the compat id, the original language's name, the enum words
    /// Radarr adds and the relative path it derives from the previous path.
    /// </summary>
    private static HashSet<string> Leaves(JsonNode? node, string? key)
    {
        string[] derived = ["eventType", "url", "indexerFlags", "originalLanguage", "addMethod", "deleteReason", "downloadStatus", "previousRelativePath"];
        var leaves = new HashSet<string>(StringComparer.Ordinal);
        if (key is not null && derived.Contains(key))
        {
            return leaves;
        }

        switch (node)
        {
            case JsonObject o:
                foreach (var (k, v) in o)
                {
                    leaves.UnionWith(Leaves(v, k));
                }

                break;
            case JsonArray a:
                foreach (var item in a)
                {
                    leaves.UnionWith(Leaves(item, key));
                }

                break;
            case JsonValue v when v.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False):
                leaves.Add(v.ToJsonString());
                break;
        }

        return leaves;
    }
}
