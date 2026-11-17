using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Issues;
using Bigarr.Api.Features.Notifications;
using Bigarr.Api.Features.Notifications.Discord;
using Bigarr.Api.Features.Requests;
using Bigarr.Api.Features.Users;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Tests.Hosting;
using Bigarr.Api.Tests.Issues;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Api.Tests.Requests;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Issues;
using Bigarr.Domain.Notifications;
using Bigarr.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Requests.RequestsEnv;

namespace Bigarr.Api.Tests.Notifications;

/// <summary>
/// R5's acceptance: every request and issue event, made by the real request and issue flows, reaches a fake Discord in Seerr's layout, with
/// the people concerned mentioned by their Discord user id. Demo users and ids only.
/// </summary>
public sealed class RequestDiscordDeliveryTests
{
    private const string WebhookPath = "/api/webhooks/123456789/fake-token";
    private const string WebhookUrl = $"https://discord.com{WebhookPath}";
    private const string AppUrl = "https://bigarr.example";
    private const string AdminDiscord = "100000000000000001";
    private const string ViewerDiscord = "100000000000000002";
    private const string ManagerDiscord = "100000000000000003";

    private static readonly UserPermissions Defaults = UserPermissions.Defaults();

    [Fact]
    public async Task Each_request_event_reaches_Discord_in_Seerrs_layout_and_mentions_who_it_concerns()
    {
        await using var discord = await FakeHttpServer.StartAsync();
        discord.Responder = r => r.Path == WebhookPath ? new FakeHttpServer.Answer(204) : null;
        await using var env = await StartAsync(
            seriesRoot: false,
            extra: new Dictionary<string, string> { ["Bigarr:Notifications:SweepSeconds"] = "1", ["Bigarr:Notifications:ApplicationUrl"] = AppUrl },
            testServices: services => services.AddHttpClient(WebhookClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new DiscordDeliveryTests.ToFakeDiscord(new Uri(discord.Url))));

        // The connection picks the request events like any other; they are about no film, so its tags would not hold them back.
        var types = (await env.Admin.GetFromJsonAsync<List<NotificationEventTypeResponse>>("/api/v1/notifications/events", Json))!;
        Assert.All(types.Where(t => t.Type.StartsWith("request", StringComparison.Ordinal)), t => Assert.True(t.SentToDiscord && t.IgnoresTags));
        var created = await env.Admin.PostAsJsonAsync(
            "/api/v1/notifications",
            new NotificationEndpointRequest("Requests", WebhookUrl, Format: WebhookFormat.Discord, Events: ["requestCreated", "requestApproved", "requestDeclined", "requestAvailable", "requestFailed"]),
            Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // Discord ids: the admin's own profile, a manager's set by the admin, a viewer's own (who never sees the managers' choice).
        await SetMineAsync(env.Admin, AdminDiscord, null);
        var viewer = await env.UserAsync("demo-viewer", Defaults);
        var mine = await SetMineAsync(viewer, $"<@{ViewerDiscord}>", null);
        Assert.Equal(ViewerDiscord, mine.DiscordUserId);
        Assert.DoesNotContain(mine.Choices, c => c.Type == "requestCreated");
        Assert.Equal(["requestApproved", "requestDeclined", "requestAvailable", "requestFailed", "issueCommented", "issueResolved", "issueReopened"], mine.Mentions);
        var refused = await viewer.PutAsJsonAsync("/api/v1/profile/notifications", new UserNotificationsDto("not-an-id", []), Json);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        await env.UserAsync("demo-manager", Defaults, UserRole.RequestManager);
        var users = (await env.Admin.GetFromJsonAsync<List<UserResponse>>("/api/v1/users", Json))!;
        var manager = users.Single(u => u.Username == "demo-manager");
        var updated = await env.Admin.PutAsJsonAsync($"/api/v1/users/{manager.Id}", new UpdateUserRequest(manager.Role, manager.Permissions, Notifications: new UserNotificationsDto(ManagerDiscord, ["requestCreated"])), Json);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(["requestCreated"], (await updated.Content.ReadFromJsonAsync<UserResponse>(Json))!.Notifications.Mentions);

        // Pending: the people who approve are mentioned, the requester is not.
        var asked = await AskOkAsync(viewer, RequestMediaType.Movie, 603);
        var pending = await MessageAsync(discord, "New Movie Request");
        AssertEmbed(pending, "The Matrix (1999)", $"{AppUrl}/discover/movie/603", SeerrColor.Orange, "Pending Approval");
        AssertMentions(pending, AdminDiscord, ManagerDiscord);
        Assert.StartsWith("https://image.tmdb.org/", pending.Embed.GetProperty("thumbnail").GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.Equal(AppUrl, pending.Embed.GetProperty("author").GetProperty("url").GetString());

        // Approved: the requester; the manager opted out of it, the admin acted.
        await env.ApproveAsync(env.Admin, asked.Id);
        var approved = await MessageAsync(discord, "Movie Request Approved");
        AssertEmbed(approved, "The Matrix (1999)", $"{AppUrl}/discover/movie/603", SeerrColor.Purple, "Processing");
        AssertMentions(approved, ViewerDiscord);

        // Available: the requester, once the file is there.
        await env.DbAsync(async db =>
        {
            db.MediaFiles.Add(TestData.NewFile(await db.MovieVersions.SingleAsync(v => v.Movie.TmdbId == 603), "The.Matrix.1999.1080p.mkv"));
            return await db.SaveChangesAsync();
        });
        await env.RunJobAsync(RequestAvailabilityJob.JobName);
        var available = await MessageAsync(discord, "Movie Request Now Available");
        AssertEmbed(available, "The Matrix (1999)", $"{AppUrl}/discover/movie/603", SeerrColor.Green, "Available");
        AssertMentions(available, ViewerDiscord);

        // Declined, with the reason the requester is told.
        var second = await AskOkAsync(viewer, RequestMediaType.Movie, 604);
        await MessageAsync(discord, "New Movie Request", skip: 1);
        var declinedResponse = await env.Admin.PostAsJsonAsync($"/api/v1/requests/{second.Id}/decline", new DeclineRequestRequest("Already on another server"), Json);
        Assert.Equal(HttpStatusCode.OK, declinedResponse.StatusCode);
        var declined = await MessageAsync(discord, "Movie Request Declined");
        AssertEmbed(declined, "The Matrix Reloaded (2003)", $"{AppUrl}/discover/movie/604", SeerrColor.Red, "Declined");
        Assert.Equal("Already on another server", Field(declined, "Reason"));
        AssertMentions(declined, ViewerDiscord);

        // Failed (no series root folder): the requester and the admin, the seasons asked for, never why.
        var series = await AskOkAsync(viewer, RequestMediaType.Series, 1396, seasons: [1, 2]);
        await MessageAsync(discord, "New Series Request");
        await env.ApproveAsync(env.Admin, series.Id);
        var failed = await MessageAsync(discord, "Series Request Failed");
        AssertEmbed(failed, "Breaking Bad (2008)", $"{AppUrl}/discover/tv/1396", SeerrColor.Red, "Failed");
        Assert.Equal("1, 2", Field(failed, "Requested Seasons"));
        AssertMentions(failed, AdminDiscord, ViewerDiscord);
        Assert.DoesNotContain("root folder", failed.Body, StringComparison.OrdinalIgnoreCase);

        // Approved by the user's own flags: one message, Seerr's "automatically approved", no one mentioned.
        var trusted = await env.UserAsync("demo-trusted", new UserPermissions { RequestFilms = true, AutoApproveFilms = true });
        await SetMineAsync(trusted, "100000000000000004", null);
        await AskOkAsync(trusted, RequestMediaType.Movie, 604);
        var autoApproved = await MessageAsync(discord, "Movie Request Automatically Approved");
        AssertMentions(autoApproved);
        Assert.Single(discord.Requests, r => r.Body.Contains("demo-trusted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Each_issue_event_reaches_Discord_in_Seerrs_layout_without_file_release_or_indexer()
    {
        await using var discord = await FakeHttpServer.StartAsync();
        discord.Responder = r => r.Path == WebhookPath ? new FakeHttpServer.Answer(204) : null;
        await using var env = await StartAsync(
            extra: new Dictionary<string, string> { ["Bigarr:Notifications:SweepSeconds"] = "1", ["Bigarr:Notifications:ApplicationUrl"] = AppUrl },
            testServices: services => services.AddHttpClient(WebhookClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new DiscordDeliveryTests.ToFakeDiscord(new Uri(discord.Url))));
        await env.FilmInLibraryAsync(withGrab: true);
        var created = await env.Admin.PostAsJsonAsync(
            "/api/v1/notifications",
            new NotificationEndpointRequest("Issues", WebhookUrl, Format: WebhookFormat.Discord, Events: ["issueCreated", "issueCommented", "issueResolved", "issueReopened"]),
            Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await SetMineAsync(env.Admin, AdminDiscord, null);
        var viewer = await env.UserAsync("demo-viewer", Defaults);
        var mine = await SetMineAsync(viewer, ViewerDiscord, null);
        Assert.DoesNotContain(mine.Choices, c => c.Type == "issueCreated");
        var managerClient = await env.UserAsync("demo-manager", Defaults, UserRole.RequestManager);
        await SetMineAsync(managerClient, ManagerDiscord, null);

        // Reported: the people who handle it.
        var report = await viewer.PostAsJsonAsync("/api/v1/issues", new CreateIssueRequest(RequestMediaType.Movie, 603, IssueType.Video, IssueScope.Movie, "Green blocks all over"), Json);
        Assert.True(report.IsSuccessStatusCode, await report.Content.ReadAsStringAsync());
        var issue = (await report.Content.ReadFromJsonAsync<IssueResponse>(Json))!;
        var reported = await MessageAsync(discord, "New Video Issue Reported");
        AssertIssue(reported, issue.Id, SeerrColor.Red, "Open");
        Assert.Equal("Green blocks all over", reported.Embed.GetProperty("description").GetString());
        AssertMentions(reported, AdminDiscord, ManagerDiscord);

        // A reply from the admin: the reporter. The reporter's answer: the people who handle it.
        await PostOkAsync(env.Admin, $"/api/v1/issues/{issue.Id}/comments", new IssueMessageRequest("Looking for another release."));
        var reply = await MessageAsync(discord, "New Comment on Video Issue");
        Assert.Equal("Looking for another release.", Field(reply, "Comment from admin"));
        Assert.Equal($"{AppUrl}/issues/{issue.Id}", reply.Embed.GetProperty("url").GetString());
        Assert.Equal((int)SeerrColor.Orange, reply.Embed.GetProperty("color").GetInt32());
        AssertMentions(reply, ViewerDiscord);
        await PostOkAsync(viewer, $"/api/v1/issues/{issue.Id}/comments", new IssueMessageRequest("Still there at 00:42."));
        AssertMentions(await MessageAsync(discord, "New Comment on Video Issue", skip: 1), AdminDiscord, ManagerDiscord);

        // Resolved by the admin: the reporter. Reopened by the reporter: the people who handle it.
        await PostOkAsync(env.Admin, $"/api/v1/issues/{issue.Id}/resolve");
        var resolved = await MessageAsync(discord, "Video Issue Resolved");
        AssertIssue(resolved, issue.Id, SeerrColor.Green, "Resolved");
        AssertMentions(resolved, ViewerDiscord);
        await PostOkAsync(viewer, $"/api/v1/issues/{issue.Id}/reopen");
        var reopened = await MessageAsync(discord, "Video Issue Reopened");
        AssertIssue(reopened, issue.Id, SeerrColor.Red, "Open");
        AssertMentions(reopened, AdminDiscord, ManagerDiscord);

        // The channel may be read by reporters: no file, release, indexer or version, whatever the admin's own view has.
        Assert.All(discord.Requests, r =>
        {
            Assert.DoesNotContain("BluRay", r.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("Demo indexer", r.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(".mkv", r.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("1080p", r.Body, StringComparison.Ordinal);
        });
    }

    private static async Task PostOkAsync(HttpClient client, string path, object? body = null)
    {
        var response = body is null ? await client.PostAsync(path, null) : await client.PostAsJsonAsync(path, body, Json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static void AssertIssue(Message message, int issueId, SeerrColor color, string status)
    {
        Assert.Equal("The Matrix (1999)", message.Embed.GetProperty("title").GetString());
        Assert.Equal($"{AppUrl}/issues/{issueId}", message.Embed.GetProperty("url").GetString());
        Assert.Equal((int)color, message.Embed.GetProperty("color").GetInt32());
        Assert.Equal("demo-viewer", Field(message, "Reported By"));
        Assert.Equal("Video", Field(message, "Issue Type"));
        Assert.Equal(status, Field(message, "Issue Status"));
    }

    private static async Task<MyNotificationsResponse> SetMineAsync(HttpClient client, string? discordId, List<string>? mentions)
    {
        var current = (await client.GetFromJsonAsync<MyNotificationsResponse>("/api/v1/profile/notifications", Json))!;
        var response = await client.PutAsJsonAsync("/api/v1/profile/notifications", new UserNotificationsDto(discordId, mentions ?? current.Mentions), Json);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<MyNotificationsResponse>(Json))!;
    }

    private sealed record Message(string Body, JsonElement Root, JsonElement Embed);

    /// <summary>The <paramref name="skip"/>+1-th message whose embed author is <paramref name="eventName"/>.</summary>
    private static async Task<Message> MessageAsync(FakeHttpServer discord, string eventName, int skip = 0)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var found = discord.Requests
                .Select(r => (r.Body, Root: JsonDocument.Parse(r.Body).RootElement))
                .Where(m => m.Root.GetProperty("embeds")[0].TryGetProperty("author", out var author) && author.GetProperty("name").GetString() == eventName)
                .Skip(skip)
                .Select(m => new Message(m.Body, m.Root, m.Root.GetProperty("embeds")[0]))
                .FirstOrDefault();
            if (found is not null)
            {
                return found;
            }

            Assert.True(DateTime.UtcNow < deadline, $"Discord never got \"{eventName}\"; got: {string.Join(" | ", discord.Requests.Select(r => r.Body))}");
            await Task.Delay(50);
        }
    }

    private static void AssertEmbed(Message message, string title, string url, SeerrColor color, string status)
    {
        Assert.Equal(title, message.Embed.GetProperty("title").GetString());
        Assert.Equal(url, message.Embed.GetProperty("url").GetString());
        Assert.Equal((int)color, message.Embed.GetProperty("color").GetInt32());
        Assert.Equal("demo-viewer", Field(message, "Requested By"));
        Assert.Equal(status, Field(message, "Request Status"));
    }

    private static string? Field(Message message, string name) =>
        message.Embed.GetProperty("fields").EnumerateArray().Where(f => f.GetProperty("name").GetString() == name).Select(f => f.GetProperty("value").GetString()).FirstOrDefault();

    /// <summary>Exactly these users pinged, and nobody else allowed to be.</summary>
    private static void AssertMentions(Message message, params string[] ids)
    {
        var content = message.Root.TryGetProperty("content", out var c) ? c.GetString() : null;
        Assert.Equal(ids.Length == 0 ? null : string.Join(' ', ids.Select(id => $"<@{id}>")), content);
        var allowed = message.Root.GetProperty("allowed_mentions");
        Assert.Empty(allowed.GetProperty("parse").EnumerateArray());
        Assert.Equal(ids, allowed.GetProperty("users").EnumerateArray().Select(u => u.GetString()!).ToArray());
    }
}
