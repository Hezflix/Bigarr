using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Notifications.NewEpisodes;
using Bigarr.Api.Features.Users;
using Bigarr.Api.Infrastructure.Http;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Library;
using Bigarr.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;
using Show = Bigarr.Domain.Tv.Series;

namespace Bigarr.Api.Tests.Notifications;

/// <summary>The bot's token as the admin sets it (a secret never sent back), and a user's own shows and their switch.</summary>
public sealed class NewEpisodeApiTests
{
    private const string Token = "fake-bot-token-abcdef";

    [Fact]
    public async Task The_bot_token_is_a_secret_kept_by_the_mask_and_cleared_by_an_empty_value()
    {
        await using var factory = new BigarrApiFactory();
        var client = await ClientAsync(factory);

        Assert.Equal(new DiscordBotResponse(false, null), await client.GetFromJsonAsync<DiscordBotResponse>("/api/v1/notifications/discord-bot", Json));
        var invalid = await client.PutAsJsonAsync("/api/v1/notifications/discord-bot", new DiscordBotRequest("two words"), Json);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var saved = await client.PutAsJsonAsync("/api/v1/notifications/discord-bot", new DiscordBotRequest($"  {Token} "), Json);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.DoesNotContain(Token, await saved.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(new DiscordBotResponse(true, SecretMask.Value), await client.GetFromJsonAsync<DiscordBotResponse>("/api/v1/notifications/discord-bot", Json));
        Assert.Equal(Token, await StoredAsync(factory));

        // The form sends the mask back unchanged: the token stays.
        await client.PutAsJsonAsync("/api/v1/notifications/discord-bot", new DiscordBotRequest(SecretMask.Value), Json);
        Assert.Equal(Token, await StoredAsync(factory));

        await client.PutAsJsonAsync("/api/v1/notifications/discord-bot", new DiscordBotRequest(string.Empty), Json);
        Assert.Null(await StoredAsync(factory));
        Assert.Empty((await client.GetFromJsonAsync<List<NewEpisodeAnnouncementResponse>>("/api/v1/notifications/new-episodes", Json))!);
    }

    [Fact]
    public async Task A_user_sees_the_new_episode_choice_on_and_turns_a_show_off_and_on()
    {
        await using var factory = new BigarrApiFactory();
        var client = await ClientAsync(factory);

        // On by default, like every choice of a user who never chose.
        var mine = (await client.GetFromJsonAsync<MyNotificationsResponse>("/api/v1/profile/notifications", Json))!;
        Assert.Contains(mine.Choices, c => c.Type == Mentions.NewEpisode && !c.ManagersOnly);
        Assert.Contains(Mentions.NewEpisode, mine.Mentions);

        // The admin of the tests never played anything and has no Discord id or bot yet.
        var shows = (await client.GetFromJsonAsync<FollowedShowsResponse>("/api/v1/profile/shows", Json))!;
        Assert.Equal((false, false, false), (shows.Linked, shows.BotConfigured, shows.NewEpisodes));
        Assert.Empty(shows.Shows);

        int seriesId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var root = db.RootFolders.Add(new RootFolder { Path = Path.Combine(factory.ConfigDirectory, "tv") }).Entity;
            var show = db.Series.Add(new Show { TvdbId = 700001, Title = "Invented Show", SortTitle = "invented show", DefaultRootFolder = root }).Entity;
            await db.SaveChangesAsync();
            seriesId = show.Id;
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/v1/profile/shows/{seriesId}", new FollowShowRequest(false), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/v1/profile/shows/{seriesId}", new FollowShowRequest(false), Json)).StatusCode);
        Assert.Equal(1, await OptOutsAsync(factory));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/v1/profile/shows/{seriesId}", new FollowShowRequest(true), Json)).StatusCode);
        Assert.Equal(0, await OptOutsAsync(factory));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/v1/profile/shows/9999", new FollowShowRequest(false), Json)).StatusCode);
    }

    private static async Task<string?> StoredAsync(BigarrApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return (await db.InstanceSettings.AsNoTracking().SingleAsync()).DiscordBotToken;
    }

    private static async Task<int> OptOutsAsync(BigarrApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BigarrDbContext>().SeriesNotificationOptOuts.CountAsync();
    }
}
