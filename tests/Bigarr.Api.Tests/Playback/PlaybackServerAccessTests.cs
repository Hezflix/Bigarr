using System.Net;
using System.Text.Json;
using Bigarr.Api.Features.Playback;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Compat;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Playback;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Playback;

/// <summary>
/// Who still has access to the server, from a fake plex.tv: shared with this server or a Plex Home member is on, the
/// owner always; plex.tv down keeps the last answer; a Tautulli hint gives way to plex.tv.
/// </summary>
public sealed class PlaybackServerAccessTests
{
    [Fact]
    public async Task The_users_page_tells_who_is_still_on_the_server_and_keeps_it_when_plex_tv_is_down()
    {
        await using var plexTv = await FakePlexTv.StartAsync();
        await using var plex = await FakePlex.StartAsync();
        await using var factory = new Factory(plexTv.Url);
        var session = await CompatTestHelpers.SessionAsync(factory);
        plexTv.Friends.AddRange(
        [
            new FakePlexTv.Friend("101", "anna", [FakePlex.MachineIdentifier]),
            new FakePlexTv.Friend("102", "bruno", ["another-server"]),
            new FakePlexTv.Friend("103", "chloe", [FakePlex.MachineIdentifier], Pending: true),
            new FakePlexTv.Friend("104", "dimitri", [], Home: true),
            new FakePlexTv.Friend("106", "felix", [FakePlex.MachineIdentifier]),
        ]);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var server = db.MediaServers.Add(new MediaServer { Name = "Plex", Url = plex.Url, Token = FakePlex.Token }).Entity;
            await db.SaveChangesAsync();
            db.MediaServerUsers.AddRange(
                new MediaServerUser { MediaServerId = server.Id, ExternalId = "1", Name = "owner", IsOwner = true },
                new MediaServerUser { MediaServerId = server.Id, ExternalId = "101", Name = "anna" },
                new MediaServerUser { MediaServerId = server.Id, ExternalId = "102", Name = "bruno" },
                new MediaServerUser { MediaServerId = server.Id, ExternalId = "103", Name = "chloe" },
                new MediaServerUser { MediaServerId = server.Id, ExternalId = "104", Name = "dimitri" },
                new MediaServerUser { MediaServerId = server.Id, ExternalId = "105", Name = "emma" },
                // Tautulli said felix had left; plex.tv says he is back.
                new MediaServerUser { MediaServerId = server.Id, ExternalId = "106", Name = "felix", OnServer = false });
            await db.SaveChangesAsync();
        }

        var before = await UsersAsync(session);
        Assert.Equal(JsonValueKind.Null, before["anna"].GetProperty("onServer").ValueKind);
        Assert.True(before["owner"].GetProperty("onServer").GetBoolean());
        Assert.False(before["felix"].GetProperty("onServer").GetBoolean());
        Assert.Equal(JsonValueKind.Null, before["felix"].GetProperty("onServerCheckedAt").ValueKind);

        Assert.Equal(HttpStatusCode.OK, (await session.PostAsync($"/api/v1/system/tasks/{ServerAccessJob.JobName}/run", null)).StatusCode);
        var users = await UsersAsync(session);
        Assert.True(users["owner"].GetProperty("onServer").GetBoolean());
        Assert.True(users["anna"].GetProperty("onServer").GetBoolean());
        Assert.False(users["bruno"].GetProperty("onServer").GetBoolean());
        Assert.False(users["chloe"].GetProperty("onServer").GetBoolean());
        Assert.True(users["dimitri"].GetProperty("onServer").GetBoolean());
        Assert.False(users["emma"].GetProperty("onServer").GetBoolean());
        Assert.True(users["felix"].GetProperty("onServer").GetBoolean());
        Assert.Equal(JsonValueKind.String, users["felix"].GetProperty("onServerCheckedAt").ValueKind);

        // plex.tv down: the last answer stays, nobody turns off.
        plexTv.Friends.Clear();
        plexTv.Down = true;
        await session.PostAsync($"/api/v1/system/tasks/{ServerAccessJob.JobName}/run", null);
        var down = await UsersAsync(session);
        Assert.True(down["anna"].GetProperty("onServer").GetBoolean());
        Assert.True(down["dimitri"].GetProperty("onServer").GetBoolean());

        // Back up, anna no longer shared with: off; the owner stays on.
        plexTv.Down = false;
        await session.PostAsync($"/api/v1/system/tasks/{ServerAccessJob.JobName}/run", null);
        var after = await UsersAsync(session);
        Assert.False(after["anna"].GetProperty("onServer").GetBoolean());
        Assert.True(after["owner"].GetProperty("onServer").GetBoolean());
    }

    [Fact]
    public async Task The_server_access_task_keeps_its_schedule_when_the_scheduler_is_disabled()
    {
        await using var factory = new Factory("http://127.0.0.1:9");
        var session = await CompatTestHelpers.SessionAsync(factory);
        var tasks = await (await session.GetAsync("/api/v1/system/tasks")).ReadJsonAsync();
        Assert.True(tasks.EnumerateArray().Single(t => t.GetProperty("name").GetString() == ServerAccessJob.JobName).GetProperty("enabled").GetBoolean());
    }

    private static async Task<Dictionary<string, JsonElement>> UsersAsync(HttpClient session) =>
        (await (await session.GetAsync("/api/v1/playback/users")).ReadJsonAsync()).EnumerateArray().ToDictionary(u => u.GetProperty("serverName").GetString()!);

    private sealed class Factory(string plexTvUrl)
        : BigarrApiFactory(null, new Dictionary<string, string> { ["Bigarr:PlexTv:BaseUrl"] = plexTvUrl }, deleteOnDispose: true);
}
