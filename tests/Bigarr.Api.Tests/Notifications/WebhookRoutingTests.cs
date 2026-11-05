using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Events;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Notifications;
using Bigarr.Domain.Tags;

namespace Bigarr.Api.Tests.Notifications;

/// <summary>
/// Which deliveries an event makes: tags like Radarr's connections, in the Radarr format the view and the events Radarr
/// knows, in the Discord format the events worth a message.
/// </summary>
public sealed class WebhookRoutingTests
{
    private static readonly Tag Kids = new() { Id = 1, Label = "kids" };
    private static readonly Tag Alice = new() { Id = 2, Label = "alice" };

    private static readonly MovieRef Movie = new(7, 603, "tt0133093", "The Matrix", 1999);
    private static readonly VersionRef Hd = new(41, "1080p", VersionMode.Managed, true, true, "1080p", Tags: [new TagRef(Kids.Id, Kids.Label)]);
    private static readonly VersionRef Uhd = new(42, "4k", VersionMode.Managed, false, true, "4K");
    private static readonly ReleaseRef Release = new("The.Matrix.1999.1080p.BluRay.x264-GROUP", "Bluray-1080p", "Mock", 1);

    private static NotificationEndpoint Endpoint(WebhookFormat format = WebhookFormat.Bigarr, CompatView? view = null, params Tag[] tags) =>
        new() { Name = "hook", Url = "http://localhost/hook", Format = format, View = view ?? CompatView.Default, Tags = tags.ToList() };

    private static IEnumerable<int> SnapshotTags(VersionRef version) => (version.Tags ?? []).Select(t => t.Id);

    private static List<int?> Route(NotificationEndpoint endpoint, DomainEvent domainEvent) =>
        WebhookRouting.Deliveries(endpoint, domainEvent.Type, domainEvent, SnapshotTags);

    [Fact]
    public void An_untagged_endpoint_gets_every_film()
    {
        Assert.Equal([null], Route(Endpoint(), new ReleaseGrabbed(Movie, Hd, Release, 1, "qbt", "abc", "rss")));
        Assert.Equal([null], Route(Endpoint(), new ReleaseGrabbed(Movie, Uhd, Release, 1, "qbt", "abc", "rss")));
    }

    [Fact]
    public void A_tagged_endpoint_gets_only_the_versions_that_share_a_tag()
    {
        Assert.Equal([null], Route(Endpoint(tags: [Alice, Kids]), new ReleaseGrabbed(Movie, Hd, Release, 1, "qbt", "abc", "rss")));
        Assert.Empty(Route(Endpoint(tags: [Kids]), new ReleaseGrabbed(Movie, Uhd, Release, 1, "qbt", "abc", "rss")));
        Assert.Empty(Route(Endpoint(tags: [Alice]), new ReleaseGrabbed(Movie, Hd, Release, 1, "qbt", "abc", "rss")));
    }

    [Fact]
    public void A_film_level_event_matches_when_any_of_its_versions_does()
    {
        var deleted = new MovieDeleted(Movie, false, [Hd, Uhd]);

        Assert.Equal([null], Route(Endpoint(tags: [Kids]), deleted));
        Assert.Empty(Route(Endpoint(tags: [Alice]), deleted));
    }

    [Fact]
    public void Health_and_test_ignore_the_tags()
    {
        var tagged = Endpoint(tags: [Alice]);

        Assert.Equal([null], Route(tagged, new HealthIssueRaised("indexers", HealthIssueType.Warning, "No indexer")));
        Assert.Equal([null], Route(tagged, new HealthIssueResolved("indexers", "No indexer", HealthIssueType.Warning)));
        Assert.Equal([null], Route(tagged, new TestEvent("hello")));
        Assert.Equal([null], Route(Endpoint(WebhookFormat.Radarr, CompatView.ForVersion("4k"), Alice), new HealthIssueRaised("indexers", HealthIssueType.Error, "No indexer")));
    }

    [Fact]
    public void A_download_nothing_matched_reaches_only_untagged_endpoints()
    {
        var failed = new DownloadFailed(null, null, Release, 1, "Stalled", true, false);

        Assert.Equal([null], Route(Endpoint(), failed));
        Assert.Empty(Route(Endpoint(tags: [Kids]), failed));
    }

    [Fact]
    public void The_subscription_comes_first()
    {
        var endpoint = Endpoint();
        endpoint.Events = ["imported"];

        Assert.Empty(Route(endpoint, new ReleaseGrabbed(Movie, Hd, Release, 1, "qbt", "abc", "rss")));
    }

    [Fact]
    public void Live_tags_are_read_for_a_version_whose_snapshot_has_none()
    {
        var grabbed = new ReleaseGrabbed(Movie, Uhd, Release, 1, "qbt", "abc", "rss");
        var live = new Dictionary<int, List<int>> { [Uhd.Id] = [Alice.Id] };

        Assert.Equal([null], WebhookRouting.Deliveries(Endpoint(tags: [Alice]), grabbed.Type, grabbed, v => v.Tags?.Select(t => t.Id) ?? live.GetValueOrDefault(v.Id) ?? []));
    }

    [Fact]
    public void The_Radarr_format_sends_only_the_versions_in_its_view()
    {
        var hdGrab = new ReleaseGrabbed(Movie, Hd, Release, 1, "qbt", "abc", "rss");
        var uhdGrab = new ReleaseGrabbed(Movie, Uhd, Release, 1, "qbt", "abc", "rss");

        Assert.Equal([41], Route(Endpoint(WebhookFormat.Radarr), hdGrab));
        Assert.Empty(Route(Endpoint(WebhookFormat.Radarr), uhdGrab));
        Assert.Equal([42], Route(Endpoint(WebhookFormat.Radarr, CompatView.ForVersion("4k")), uhdGrab));
        Assert.Empty(Route(Endpoint(WebhookFormat.Radarr, CompatView.ForVersion("4k")), hdGrab));
        Assert.Equal([41], Route(Endpoint(WebhookFormat.Radarr, CompatView.All), hdGrab));
    }

    [Fact]
    public void A_deleted_film_is_one_Radarr_movie_per_version_in_the_view_each_judged_on_its_tags()
    {
        var deleted = new MovieDeleted(Movie, true, [Hd, Uhd]);

        Assert.Equal([41, 42], Route(Endpoint(WebhookFormat.Radarr, CompatView.All), deleted));
        Assert.Equal([41], Route(Endpoint(WebhookFormat.Radarr), deleted));
        Assert.Equal([42], Route(Endpoint(WebhookFormat.Radarr, CompatView.ForVersion("4k")), deleted));
        Assert.Equal([41], Route(Endpoint(WebhookFormat.Radarr, CompatView.All, Kids), deleted));
    }

    [Theory]
    [InlineData("versionAdded")]
    [InlineData("versionUpdated")]
    [InlineData("versionDeleted")]
    [InlineData("downloadFailed")]
    [InlineData("defaultVersionChanged")]
    public void The_Radarr_format_skips_events_Radarr_has_no_word_for(string type)
    {
        DomainEvent domainEvent = type switch
        {
            "versionAdded" => new VersionAdded(Movie, Hd),
            "versionUpdated" => new VersionUpdated(Movie, Hd),
            "versionDeleted" => new VersionDeleted(Movie, Hd, false),
            "downloadFailed" => new DownloadFailed(Movie, Hd, Release, 1, "Stalled", true, false),
            _ => new DefaultVersionChanged(Movie, Hd, Uhd),
        };

        Assert.Null(WebhookRouting.RadarrEventType(type));
        Assert.Empty(Route(Endpoint(WebhookFormat.Radarr, CompatView.All), domainEvent));
        Assert.Equal([null], Route(Endpoint(), domainEvent));
    }

    [Fact]
    public void The_Radarr_format_skips_an_import_failure_about_no_film()
    {
        Assert.Empty(Route(Endpoint(WebhookFormat.Radarr, CompatView.All), new ImportFailed(null, null, Release, 1, "No film matches")));
        Assert.Equal([41], Route(Endpoint(WebhookFormat.Radarr), new ImportFailed(Movie, Hd, Release, 1, "No video file")));
    }

    [Fact]
    public void The_Discord_format_leaves_version_settings_changes_out_of_the_channel()
    {
        Assert.False(WebhookRouting.SentToDiscord("versionUpdated"));
        Assert.Empty(Route(Endpoint(WebhookFormat.Discord), new VersionUpdated(Movie, Hd)));
    }

    [Theory]
    [InlineData("versionAdded")]
    [InlineData("versionDeleted")]
    [InlineData("defaultVersionChanged")]
    public void The_Discord_format_posts_versions_added_deleted_or_made_default(string type)
    {
        DomainEvent domainEvent = type switch
        {
            "versionAdded" => new VersionAdded(Movie, Hd),
            "versionDeleted" => new VersionDeleted(Movie, Hd, false),
            _ => new DefaultVersionChanged(Movie, Hd, Uhd),
        };

        Assert.True(WebhookRouting.SentToDiscord(type));
        Assert.Equal([null], Route(Endpoint(WebhookFormat.Discord), domainEvent));
        Assert.Equal([null], Route(Endpoint(WebhookFormat.Discord, tags: [Kids]), domainEvent));
        Assert.Empty(Route(Endpoint(WebhookFormat.Discord, tags: [Alice]), domainEvent));
    }

    [Fact]
    public void The_Discord_format_is_one_message_per_event_filtered_like_the_Bigarr_format()
    {
        var deleted = new MovieDeleted(Movie, true, [Hd, Uhd]);
        var failed = new DownloadFailed(Movie, Hd, Release, 1, "Stalled", true, false);
        var unmatched = new ImportFailed(null, null, Release, 1, "No film matches");

        Assert.Equal([null], Route(Endpoint(WebhookFormat.Discord), deleted));
        Assert.Equal([null], Route(Endpoint(WebhookFormat.Discord, tags: [Kids]), deleted));
        Assert.Empty(Route(Endpoint(WebhookFormat.Discord, tags: [Alice]), deleted));
        Assert.Equal([null], Route(Endpoint(WebhookFormat.Discord), failed));
        Assert.Equal([null], Route(Endpoint(WebhookFormat.Discord), unmatched));
        Assert.Empty(Route(Endpoint(WebhookFormat.Discord, tags: [Kids]), unmatched));
        Assert.Equal([null], Route(Endpoint(WebhookFormat.Discord, tags: [Alice]), new HealthIssueRaised("indexers", HealthIssueType.Error, "No indexer")));

        var subscribed = Endpoint(WebhookFormat.Discord);
        subscribed.Events = ["imported"];
        Assert.Empty(Route(subscribed, new ReleaseGrabbed(Movie, Hd, Release, 1, "qbt", "abc", "rss")));
    }
}
