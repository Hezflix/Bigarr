using System.Text.Json;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Domain.Events;
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Tests.Domain;

public sealed class DomainEventTests
{
    private static readonly SeriesRef Show = new(4, 81189, 1396, "tt0903747", "Breaking Bad", 2008, Bigarr.Domain.Tv.SeriesType.Standard);

    private static readonly SeriesVersionRef ShowVersion = new(5, "1080p", true, true, "HD-1080p");

    private static readonly EpisodeRef Pilot = new(6, 1, 1, "Pilot");

    [Fact]
    public void Every_event_type_is_in_the_catalogue_and_serializes_camel_case_without_the_type_property()
    {
        var movie = new MovieRef(1, 603, "tt0133093", "The Matrix", 1999);
        var version = new VersionRef(2, "1080p", VersionMode.Managed, true, true, "1080p");
        DomainEvent[] events =
        [
            new MovieAdded(movie, version),
            new VersionAdded(movie, version),
            new VersionUpdated(movie, version),
            new MovieDeleted(movie, false, [version]),
            new VersionDeleted(movie, version, false),
            new ReleaseGrabbed(movie, version, new ReleaseRef("The.Matrix.1999.1080p", "Bluray-1080p", "Mock", 1, null, 12, "abc"), 7, "qbt", "abc", "rss"),
            new FileImported(movie, version, new FileRef(3, "/x", "Bluray-1080p", 1), null, null, false, "Hardlinked"),
            new FileUpgraded(movie, version, new FileRef(3, "/x", "Bluray-1080p", 1), new FileRef(2, "/y", "WEBDL-1080p", 1), null, null, false, "Copied"),
            new DownloadFailed(movie, version, new ReleaseRef("t", null, null, 0), 7, "Disk full", true, true),
            new ImportFailed(movie, version, new ReleaseRef("t", null, null, 0), 7, "No video"),
            new FileDeleted(movie, version, new FileRef(3, "/x", null, 1), "deleted"),
            new FileRenamed(movie, version, new FileRef(3, "/x2", null, 1), "/x"),
            new DefaultVersionChanged(movie, version, null),
            new HealthIssueRaised("indexers", HealthIssueType.Warning, "No indexer"),
            new HealthIssueResolved("indexers", "No indexer"),
            new TestEvent("hi"),
            new SeriesAdded(Show, ShowVersion),
            new SeriesDeleted(Show, true, [ShowVersion]),
            new SeriesVersionDeleted(Show, ShowVersion, false),
            new SeriesVersionAdded(Show, ShowVersion),
            new SeriesVersionUpdated(Show, ShowVersion, EpisodesChanged: true),
            new SeriesDefaultVersionChanged(Show, ShowVersion, null),
            new EpisodeGrabbed(Show, ShowVersion, [Pilot], new ReleaseRef("Breaking.Bad.S01E01.1080p", "WEBDL-1080p", "Mock", 1), 7, "qbt", "abc", "rss"),
            new EpisodeFileImported(Show, ShowVersion, [Pilot], new FileRef(3, "/x", "WEBDL-1080p", 1), [new FileRef(2, "/y", "HDTV-720p", 1)], null, 7, false, "Hardlinked"),
            new EpisodeFileRenamed(Show, ShowVersion, [new RenamedFileRef(new FileRef(3, "/x2", null, 1), "/x")]),
            new EpisodeFileDeleted(Show, ShowVersion, [Pilot], new FileRef(3, "/x", null, 1), "manual"),
            new EpisodeDownloadFailed(Show, ShowVersion, [Pilot], new ReleaseRef("t", null, null, 0), 7, "Stalled", true, true),
            new EpisodeImportFailed(Show, ShowVersion, [Pilot], new ReleaseRef("t", null, null, 0), 7, "No video"),
        ];

        foreach (var domainEvent in events)
        {
            Assert.True(DomainEventCatalog.IsKnown(domainEvent.Type), domainEvent.Type);
            using var doc = JsonDocument.Parse(EventJson.Serialize(domainEvent));
            Assert.False(doc.RootElement.TryGetProperty("type", out _), $"{domainEvent.Type} leaks its type into the payload");
            Assert.True(doc.RootElement.TryGetProperty("occurredAt", out _));
        }

        Assert.Equal(DomainEventCatalog.All.Count, events.Select(e => e.Type).Distinct().Count());

        using var grabbed = JsonDocument.Parse(EventJson.Serialize(events.OfType<ReleaseGrabbed>().Single()));
        Assert.Equal("managed", grabbed.RootElement.GetProperty("version").GetProperty("mode").GetString());
        Assert.Equal("Bluray-1080p", grabbed.RootElement.GetProperty("release").GetProperty("quality").GetString());
    }

    [Fact]
    public void The_webhook_body_wraps_the_payload_and_the_signature_is_an_hmac_of_it()
    {
        var body = WebhookClient.Body("grabbed", 42, """{"grabId":7}""", new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc));
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("grabbed", doc.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(42, doc.RootElement.GetProperty("eventId").GetInt64());
        Assert.Equal(7, doc.RootElement.GetProperty("data").GetProperty("grabId").GetInt32());
        Assert.StartsWith("2026-09-15T12:00:00", doc.RootElement.GetProperty("occurredAt").GetString(), StringComparison.Ordinal);

        var signature = WebhookClient.Sign("secret", body);
        Assert.StartsWith("sha256=", signature, StringComparison.Ordinal);
        Assert.Equal(7 + 64, signature.Length);
        Assert.Equal(signature, WebhookClient.Sign("secret", body));
        Assert.NotEqual(signature, WebhookClient.Sign("other", body));
    }
}
