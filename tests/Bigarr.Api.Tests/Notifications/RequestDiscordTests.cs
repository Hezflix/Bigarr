using System.Text.Json;
using Bigarr.Api.Features.Notifications.Discord;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Events;
using Bigarr.Domain.Issues;
using Bigarr.Domain.Notifications;
using Bigarr.Domain.Requests;

namespace Bigarr.Api.Tests.Notifications;

/// <summary>The request messages and who they mention, pure: the rules the delivery test drives end to end.</summary>
public sealed class RequestDiscordTests
{
    private static readonly RequestRef Film = new(7, RequestMediaType.Movie, 603, null, "The Matrix", 1999, "https://image.tmdb.org/t/p/w500/m.jpg", false, [], "demo-viewer", 2);

    private static readonly User Admin = new() { Id = 1, Username = "demo-admin", PasswordHash = string.Empty, Role = UserRole.Admin, DiscordUserId = "100000000000000001" };

    private static readonly User Viewer = new() { Id = 2, Username = "demo-viewer", PasswordHash = string.Empty, DiscordUserId = "100000000000000002" };

    private static readonly User Manager = new() { Id = 3, Username = "demo-manager", PasswordHash = string.Empty, Role = UserRole.RequestManager, DiscordUserId = "100000000000000003" };

    [Fact]
    public void Mentions_follow_who_the_event_concerns_and_their_choices()
    {
        User[] everyone = [Admin, Viewer, Manager];
        Assert.Equal(["100000000000000001", "100000000000000003"], Mentions.For(new RequestCreated(Film, AutoApproved: false), everyone));
        Assert.Empty(Mentions.For(new RequestCreated(Film, AutoApproved: true), everyone));
        Assert.Equal(["100000000000000002"], Mentions.For(new RequestApproved(Film, "demo-admin", AutoApproved: false), everyone));
        Assert.Empty(Mentions.For(new RequestApproved(Film, null, AutoApproved: true), everyone));
        Assert.Equal(["100000000000000002"], Mentions.For(new RequestDeclined(Film, "demo-manager", "No"), everyone));
        Assert.Equal(["100000000000000002"], Mentions.For(new RequestAvailable(Film), everyone));
        Assert.Equal(["100000000000000001", "100000000000000002", "100000000000000003"], Mentions.For(new RequestFailed(Film, "No root folder", Forward: false), everyone));

        // A manager's own request waiting for approval: the other managers only.
        var managersOwn = Film with { RequestedBy = "demo-manager", RequestedByUserId = 3 };
        Assert.Equal(["100000000000000001"], Mentions.For(new RequestCreated(managersOwn, AutoApproved: false), everyone));

        // Opted out, no id, disabled: never mentioned.
        var quiet = new User { Id = 2, Username = "demo-viewer", PasswordHash = string.Empty, DiscordUserId = "100000000000000002", DiscordMentions = ["requestAvailable"] };
        Assert.Empty(Mentions.For(new RequestDeclined(Film, "demo-admin", null), [quiet]));
        Assert.Single(Mentions.For(new RequestAvailable(Film), [quiet]));
        Assert.Empty(Mentions.For(new RequestAvailable(Film), [new User { Id = 2, Username = "demo-viewer", PasswordHash = string.Empty }]));
        Assert.Empty(Mentions.For(new RequestAvailable(Film), [new User { Id = 2, Username = "demo-viewer", PasswordHash = string.Empty, DiscordUserId = "100000000000000002", DisabledAt = DateTime.UnixEpoch }]));
    }

    [Fact]
    public void Issue_mentions_follow_Seerr_reporter_or_handlers_never_the_author()
    {
        var issue = new IssueRef(5, IssueType.Audio, IssueScope.Episode, IssueStatus.Open, RequestMediaType.Series, 1396, 81189, "Demo Show", 2008, null, 1, "S01E02 · Demo", "1080p", "No sound", "demo-viewer", 2);
        User[] everyone = [Admin, Viewer, Manager];
        Assert.Equal(["100000000000000001", "100000000000000003"], Mentions.For(new IssueCreated(issue), everyone));
        Assert.Equal(["100000000000000002"], Mentions.For(new IssueCommented(issue, 1, "Looking", "demo-admin", 1), everyone));
        Assert.Equal(["100000000000000001", "100000000000000003"], Mentions.For(new IssueCommented(issue, 2, "Still", "demo-viewer", 2), everyone));
        Assert.Equal(["100000000000000002"], Mentions.For(new IssueResolved(issue, "demo-manager"), everyone));
        Assert.Empty(Mentions.For(new IssueResolved(issue, "demo-viewer"), everyone));
        Assert.Equal(["100000000000000001", "100000000000000003"], Mentions.For(new IssueReopened(issue, "demo-viewer"), everyone));

        var embed = DiscordWebhook.Payload(new IssueCreated(issue), null, new DiscordContext("https://bigarr.example"))!.Embeds![0];
        Assert.Equal("New Audio Issue Reported", embed.Author!.Name);
        Assert.Equal("https://bigarr.example/issues/5", embed.Url);
        Assert.Contains(embed.Fields!, f => f is { Name: "Affected Episode", Value: "S01E02 · Demo" });
        Assert.DoesNotContain(embed.Fields!, f => f.Value.Contains("1080p", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("100000000000000002", "100000000000000002", true)]
    [InlineData(" <@100000000000000002> ", "100000000000000002", true)]
    [InlineData("<@!100000000000000002>", "100000000000000002", true)]
    [InlineData("", null, true)]
    [InlineData("demo-viewer#0001", null, false)]
    [InlineData("1234", null, false)]
    public void Discord_ids_are_snowflakes(string input, string? expected, bool valid)
    {
        Assert.Equal(expected, Mentions.NormalizeDiscordUserId(input, out var ok));
        Assert.Equal(valid, ok);
    }

    [Fact]
    public void A_4K_series_only_TVDB_knows_links_to_Discovers_search_and_without_an_address_links_nowhere()
    {
        var series = new RequestRef(8, RequestMediaType.Series, null, 81189, "Demo Show", null, null, true, [3, 1], "demo-viewer", 2);
        var linked = DiscordWebhook.Payload(new RequestCreated(series, AutoApproved: false), null, new DiscordContext("https://bigarr.example/", ["100000000000000001"]))!;
        var embed = Assert.Single(linked.Embeds!);
        Assert.Equal("New 4K Series Request", embed.Author!.Name);
        Assert.Equal("Demo Show", embed.Title);
        Assert.Equal("https://bigarr.example/discover/search?q=tvdb%3A81189", embed.Url);
        Assert.Null(embed.Thumbnail);
        Assert.Contains(embed.Fields!, f => f is { Name: "Requested Seasons", Value: "1, 3" });
        Assert.Equal("<@100000000000000001>", linked.Content);

        var bare = DiscordWebhook.Payload(new RequestAvailable(Film), null)!;
        Assert.Null(bare.Embeds![0].Url);
        Assert.Null(bare.Embeds[0].Author!.Url);
        Assert.Null(bare.Content);
        using var doc = JsonDocument.Parse(DiscordWebhook.Serialize(bare));
        Assert.Empty(doc.RootElement.GetProperty("allowed_mentions").GetProperty("users").EnumerateArray());

        // Seerr posts an auto-approval once: from the approval, never from the creation.
        Assert.Null(DiscordWebhook.Payload(new RequestCreated(Film, AutoApproved: true), null));
    }
}
