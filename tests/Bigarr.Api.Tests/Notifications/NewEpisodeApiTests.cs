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

        Assert.Equal((false, null), Bot(await client.GetFromJsonAsync<DiscordBotResponse>("/api/v1/notifications/discord-bot", Json)));
        var invalid = await client.PutAsJsonAsync("/api/v1/notifications/discord-bot", new DiscordBotRequest("two words"), Json);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var saved = await client.PutAsJsonAsync("/api/v1/notifications/discord-bot", new DiscordBotRequest($"  {Token} "), Json);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.DoesNotContain(Token, await saved.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal((true, SecretMask.Value), Bot(await client.GetFromJsonAsync<DiscordBotResponse>("/api/v1/notifications/discord-bot", Json)));
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

    [Fact]
    public async Task The_message_wording_and_link_target_round_trip_and_leave_the_token_alone()
    {
        await using var factory = new BigarrApiFactory();
        var client = await ClientAsync(factory);
        await client.PutAsJsonAsync("/api/v1/notifications/discord-bot", new DiscordBotRequest(Token), Json);

        var empty = (await client.GetFromJsonAsync<DiscordBotResponse>("/api/v1/notifications/discord-bot", Json))!;
        Assert.Equal(PlexLinkTarget.AppPlexTv, empty.Message.LinkTarget);
        Assert.Null(empty.Message.Content);
        Assert.Equal(8, empty.Texts.Count);
        Assert.Contains("EpisodeTitle", empty.Texts.Single(t => t.Field == NewEpisodeText.Content).Placeholders);

        var french = new NewEpisodeMessageSettings(
            "📺 {Show} - Saison {Season} · Episode {Episode} - {EpisodeTitle} 👈 vient d'être ajouté à Plex !",
            "  ",
            null,
            null,
            null,
            null,
            null,
            "Lien vers l'épisode",
            PlexLinkTarget.ServerAddress,
            " https://plex.example.com/ ");
        var saved = await client.PutAsJsonAsync("/api/v1/notifications/new-episodes/message", french, Json);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var body = await saved.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Token, body, StringComparison.Ordinal);

        var read = (await client.GetFromJsonAsync<DiscordBotResponse>("/api/v1/notifications/discord-bot", Json))!;
        Assert.Equal(french with { Title = null, PlexWebAddress = "https://plex.example.com/" }, read.Message);
        Assert.Equal((true, SecretMask.Value), Bot(read));
        Assert.Equal(Token, await StoredAsync(factory));
    }

    [Fact]
    public async Task Unknown_placeholders_and_a_bad_address_are_refused_and_nothing_is_saved()
    {
        await using var factory = new BigarrApiFactory();
        var client = await ClientAsync(factory);

        var wrong = new NewEpisodeMessageSettings("{Show} {Title}", null, "{Show} {EpisodeTitle}", null, "S{Season:00} {Show:00}", null, null, null, PlexLinkTarget.ServerAddress, "ftp://plex.example.com");
        var refused = await client.PutAsJsonAsync("/api/v1/notifications/new-episodes/message", wrong, Json);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var problem = await refused.Content.ReadAsStringAsync();
        Assert.Contains("{Title}", problem, StringComparison.Ordinal);
        Assert.Contains("batchContent", problem, StringComparison.Ordinal);
        Assert.Contains("{Show:00}", problem, StringComparison.Ordinal);
        Assert.Contains("plexWebAddress", problem, StringComparison.Ordinal);

        var read = (await client.GetFromJsonAsync<DiscordBotResponse>("/api/v1/notifications/discord-bot", Json))!;
        Assert.Null(read.Message.Content);
        Assert.Equal(PlexLinkTarget.AppPlexTv, read.Message.LinkTarget);

        // The preview shows the same mistakes live, and still renders.
        var preview = (await (await client.PostAsJsonAsync("/api/v1/notifications/new-episodes/preview", wrong, Json)).Content.ReadFromJsonAsync<NewEpisodePreviewResponse>(Json))!;
        Assert.Equal(3, preview.Problems.Count);
        Assert.True(preview.AddressInvalid);
        Assert.Equal("Sample Show {Title}", preview.Single.Content);
        Assert.StartsWith("https://app.plex.tv/desktop/", preview.Single.Embeds![0].Fields![0].Value.Split("](")[1], StringComparison.Ordinal);
    }

    private static (bool, string?) Bot(DiscordBotResponse? bot) => (bot!.Configured, bot.Token);

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
