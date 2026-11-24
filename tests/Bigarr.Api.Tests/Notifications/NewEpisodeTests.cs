using System.Collections.Concurrent;
using System.Text.Json;
using Bigarr.Api.Features.Notifications.NewEpisodes;
using Bigarr.Api.Infrastructure.Auth.External;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Hosting;
using Bigarr.Api.Tests.Indexers;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Common;
using Bigarr.Domain.Events;
using Bigarr.Domain.Library;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Notifications;
using Bigarr.Domain.Playback;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Show = Bigarr.Domain.Tv.Series;

namespace Bigarr.Api.Tests.Notifications;

/// <summary>
/// New episodes told in Discord direct messages, on invented data against a fake Plex, a fake plex.tv and a fake Discord:
/// who follows a show (plays, the server owner, opt-outs, choices), the batch of a season pack, waiting for Plex and the
/// timeout, and the bot's answers (403 for one user, 429, the token kept out of every log line).
/// <code>
///   user   Plex account      role   Discord   watched          notes
///   root   (none)            admin  -         -                the first admin
///   owner  9001 (owner, 1)   admin  D1        Invented Show    the server's owner: Plex numbers them 1
///   alice  1001              user   D2        Invented Show
///   bob    1002              user   D3        Invented Show    opted out of Invented Show
///   carol  1003              user   D4        Invented Show    "new episode" choice off
///   dave   1004              user   -         Invented Show    no Discord id
///   erin   1005              user   D5        Other Show
/// </code>
/// </summary>
public sealed class NewEpisodeTests : IAsyncLifetime
{
    private const string BotToken = "fake-bot-token-0123456789";
    private const string D1 = "100000000000000001";
    private const string D2 = "100000000000000002";
    private const string D3 = "100000000000000003";
    private const string D4 = "100000000000000004";
    private const string D5 = "100000000000000005";
    private const string ShowKey = "30";
    private const string Machine = "fake-machine-id";

    private static readonly DateTime T0 = new(2026, 11, 20, 20, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(T0));
    private readonly LogCapture _logs = new();
    private readonly List<string> _plexFiles = [];
    private FakeHttpServer _plex = null!;
    private FakeHttpServer _plexTv = null!;
    private FakeHttpServer _discord = null!;
    private ServiceProvider _services = null!;
    private Show _show = null!;
    private Show _other = null!;
    private readonly Dictionary<string, int> _users = [];
    private int _episodeId = 500;

    /// <summary>Discord's answer to a message, by Discord user; 200 when not set.</summary>
    private readonly ConcurrentDictionary<string, Queue<FakeHttpServer.Answer>> _discordAnswers = new();

    public async Task InitializeAsync()
    {
        _plex = await FakeHttpServer.StartAsync();
        _plex.Responder = r => r.Path switch
        {
            "/" => new FakeHttpServer.Answer(200, """{"MediaContainer":{"machineIdentifier":"__M__"}}""".Replace("__M__", Machine, StringComparison.Ordinal)),
            $"/library/metadata/{ShowKey}/allLeaves" => new FakeHttpServer.Answer(200, Leaves()),
            _ => null,
        };
        _plexTv = await FakeHttpServer.StartAsync();
        _plexTv.Responder = r => r.Path == "/api/v2/user" ? new FakeHttpServer.Answer(200, """{"id": 9001, "username": "owner"}""") : null;
        _discord = await FakeHttpServer.StartAsync();
        _discord.Responder = Discord;

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bigarr:NewEpisodes:DiscordApiUrl"] = _discord.Url + "/api/v10",
            ["Bigarr:PlexTv:BaseUrl"] = _plexTv.Url,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton<TimeProvider>(_time);
        services.AddScoped(_ => _database.CreateContext());
        services.Configure<NotificationsOptions>(config.GetSection(NotificationsOptions.Section));
        services.AddBigarrMediaServers(config);
        services.AddBigarrNewEpisodes(config);
        _services = services.BuildServiceProvider();

        await using var db = _database.CreateContext();
        var root = db.RootFolders.Add(new RootFolder { Path = "/data/tv" }).Entity;
        _show = db.Series.Add(new Show { TvdbId = 700001, Title = "Invented Show", SortTitle = "invented show", Year = 2024, DefaultRootFolder = root, Images = [new MediaImage(ImageType.Poster, "https://images.example.org/invented-show.jpg")] }).Entity;
        _other = db.Series.Add(new Show { TvdbId = 700002, Title = "Other Show", SortTitle = "other show", DefaultRootFolder = root }).Entity;
        var server = db.MediaServers.Add(new MediaServer { Name = "Plex", Url = _plex.Url, Token = "fake-plex-token", PathMappings = [new PathMapping("/data", "/media")] }).Entity;
        db.InstanceSettings.Add(new InstanceSettings { DiscordBotToken = BotToken });
        await db.SaveChangesAsync();

        var showItem = Item(db, server, "301", _show.TvdbId, ShowKey);
        var otherItem = Item(db, server, "401", _other.TvdbId, "40");
        AddUser(db, server, "root", UserRole.Admin, null, null, null);
        AddUser(db, server, "owner", UserRole.Admin, D1, "9001", showItem, owner: true);
        AddUser(db, server, "alice", UserRole.User, D2, "1001", showItem);
        AddUser(db, server, "bob", UserRole.User, D3, "1002", showItem);
        AddUser(db, server, "carol", UserRole.User, D4, "1003", showItem, mentions: [Mentions.Choices[0]]);
        AddUser(db, server, "dave", UserRole.User, null, "1004", showItem);
        AddUser(db, server, "erin", UserRole.User, D5, "1005", otherItem);
        await db.SaveChangesAsync();
        db.SeriesNotificationOptOuts.Add(new SeriesNotificationOptOut { UserId = _users["bob"], SeriesId = _show.Id });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _plex.DisposeAsync();
        await _plexTv.DisposeAsync();
        await _discord.DisposeAsync();
        _database.Dispose();
    }

    [Fact]
    public async Task Followers_are_who_played_the_show_mapped_through_plex_sign_in_and_the_owner_minus_opt_outs_and_choices()
    {
        await using var scope = _services.CreateAsyncScope();
        var followers = scope.ServiceProvider.GetRequiredService<NewEpisodeFollowers>();

        // The owner's plays (Plex account 1) are the user signed in with the plex.tv account the server's token belongs
        // to, not the first admin. bob opted out, carol turned the choice off, dave has no Discord id, erin watches another show.
        var found = await followers.ForSeriesAsync(_show.Id, CancellationToken.None);
        Assert.Equal(["owner", "alice"], found.Recipients.Select(u => u.Username));
        Assert.Equal(ShowKey, Assert.Single(found.PlexShows).ShowRatingKey);
        Assert.Equal(["erin"], (await followers.ForSeriesAsync(_other.Id, CancellationToken.None)).Recipients.Select(u => u.Username));

        // Each user's own list: the show, with their switch; nothing to follow without a Plex account Bigarr has seen play.
        var alice = Assert.Single((await followers.ShowsOfAsync(_users["alice"], CancellationToken.None))!);
        Assert.Equal((_show.Id, "Invented Show", true), (alice.SeriesId, alice.Title, alice.Notify));
        Assert.Equal("https://images.example.org/invented-show.jpg", alice.PosterUrl);
        Assert.False(Assert.Single((await followers.ShowsOfAsync(_users["bob"], CancellationToken.None))!).Notify);
        Assert.Equal("Invented Show", Assert.Single((await followers.ShowsOfAsync(_users["owner"], CancellationToken.None))!).Title);
        Assert.Null(await followers.ShowsOfAsync(_users["root"], CancellationToken.None));
    }

    [Fact]
    public async Task Without_plex_tv_the_owner_falls_back_to_the_first_admin()
    {
        _plexTv.Responder = _ => new FakeHttpServer.Answer(503);
        await using var scope = _services.CreateAsyncScope();
        var followers = scope.ServiceProvider.GetRequiredService<NewEpisodeFollowers>();

        // root has no Discord id: the owner's plays now count as root's, so only alice is told.
        Assert.Equal(["alice"], (await followers.ForSeriesAsync(_show.Id, CancellationToken.None)).Recipients.Select(u => u.Username));
        Assert.Equal("Invented Show", Assert.Single((await followers.ShowsOfAsync(_users["root"], CancellationToken.None))!).Title);
        Assert.Null(await followers.ShowsOfAsync(_users["owner"], CancellationToken.None));
    }

    [Fact]
    public async Task A_season_pack_makes_one_message_per_user_once_plex_lists_every_episode()
    {
        // Two files of a pack, then a third a minute later: one batch. An upgrade and an episode already collected add nothing.
        var e1 = Episode(1);
        var e2 = Episode(2);
        var e3 = Episode(3);
        await CollectAsync(Imported(_show, e1), Imported(_show, e2));
        _time.Advance(TimeSpan.FromSeconds(60));
        await CollectAsync(Imported(_show, e3), Imported(_show, e1), Imported(_show, Episode(4)) with { PreviousFiles = [new FileRef(9, "/data/old.mkv", null, 1)] });
        await using (var db = _database.CreateContext())
        {
            var announcement = await db.NewEpisodeAnnouncements.Include(a => a.Episodes).SingleAsync();
            Assert.Equal([1, 2, 3], announcement.Episodes.Select(e => e.EpisodeNumber).Order());
        }

        // Still inside the two minutes after the last episode: nothing asked, nothing sent.
        _time.Advance(TimeSpan.FromSeconds(90));
        Assert.Equal(new NewEpisodePass(0, 0, 0, 0), await PassAsync());
        Assert.Empty(_plex.Requests);

        // Closed, but Plex lists two of the three: it waits.
        _time.Advance(TimeSpan.FromSeconds(60));
        _plexFiles.AddRange([Plex(e1), Plex(e2)]);
        Assert.Equal(new NewEpisodePass(0, 1, 0, 0), await PassAsync());
        Assert.Empty(_discord.Requests);

        // The third is in: one message each for the owner and alice, listing the three.
        _plexFiles.Add(Plex(e3));
        Assert.Equal(new NewEpisodePass(1, 0, 2, 0), await PassAsync());
        var sent = _discord.Requests.Where(r => r.Path.EndsWith("/messages", StringComparison.Ordinal)).ToList();
        Assert.Equal([$"/api/v10/channels/dm-{D1}/messages", $"/api/v10/channels/dm-{D2}/messages"], sent.Select(r => r.Path));
        Assert.All(_discord.Requests, r => Assert.Equal($"Bot {BotToken}", r.Headers["Authorization"]));
        using (var doc = JsonDocument.Parse(sent[0].Body))
        {
            Assert.Equal("📺 Invented Show - 3 new episodes 👈 just added to Plex!", doc.RootElement.GetProperty("content").GetString());
            var embed = doc.RootElement.GetProperty("embeds")[0];
            Assert.Equal("Invented Show - 3 new episodes (S01 · E01–E03)", embed.GetProperty("title").GetString());
            Assert.Equal("S01 · E01 - Chapter 1\nS01 · E02 - Chapter 2\nS01 · E03 - Chapter 3", embed.GetProperty("description").GetString());
            Assert.Equal("https://images.example.org/invented-show.jpg", embed.GetProperty("image").GetProperty("url").GetString());
            Assert.Contains($"server/{Machine}/details?key=%2Flibrary%2Fmetadata%2F{ShowKey}", embed.GetProperty("fields")[0].GetProperty("value").GetString(), StringComparison.Ordinal);
            Assert.Empty(doc.RootElement.GetProperty("allowed_mentions").GetProperty("parse").EnumerateArray());
        }

        await using (var db = _database.CreateContext())
        {
            var announcement = await db.NewEpisodeAnnouncements.Include(a => a.Messages).SingleAsync();
            Assert.Equal(PlexWait.Seen, announcement.PlexWait);
            Assert.All(announcement.Messages, m => Assert.Equal(WebhookDeliveryState.Delivered, m.State));
        }

        // Nothing is ever sent twice, and the same episodes imported again (another version) start nothing new.
        await CollectAsync(Imported(_show, e1), Imported(_show, e2));
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(new NewEpisodePass(0, 0, 0, 0), await PassAsync());
        await using (var db = _database.CreateContext())
        {
            Assert.Equal(1, await db.NewEpisodeAnnouncements.CountAsync());
        }
    }

    [Fact]
    public async Task Plex_is_waited_for_until_the_timeout_then_the_message_goes_anyway()
    {
        await CollectAsync(Imported(_show, Episode(1)));
        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(new NewEpisodePass(0, 1, 0, 0), await PassAsync());
        _time.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(new NewEpisodePass(0, 1, 0, 0), await PassAsync());
        Assert.Empty(_discord.Requests);

        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(new NewEpisodePass(1, 0, 2, 0), await PassAsync());
        await using var db = _database.CreateContext();
        Assert.Equal(PlexWait.TimedOut, (await db.NewEpisodeAnnouncements.SingleAsync()).PlexWait);

        // A single episode: the old bot's layout, its summary as the description.
        var message = _discord.Requests.First(r => r.Path.EndsWith("/messages", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(message.Body);
        Assert.Equal("📺 Invented Show - Season 1 · Episode 1 - Chapter 1 👈 just added to Plex!", doc.RootElement.GetProperty("content").GetString());
        var embed = doc.RootElement.GetProperty("embeds")[0];
        Assert.Equal("Invented Show - Chapter 1 (S1 · E1)", embed.GetProperty("title").GetString());
        Assert.Equal("What happens in chapter 1.", embed.GetProperty("description").GetString());
        Assert.Equal((int)Bigarr.Api.Features.Notifications.Discord.SeerrColor.DarkPurple, embed.GetProperty("color").GetInt32());
    }

    [Fact]
    public async Task Nobody_following_ends_the_announcement_without_asking_plex()
    {
        await using (var db = _database.CreateContext())
        {
            db.SeriesNotificationOptOuts.Add(new SeriesNotificationOptOut { UserId = _users["erin"], SeriesId = _other.Id });
            await db.SaveChangesAsync();
        }

        await CollectAsync(Imported(_other, Episode(1)));
        _time.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(new NewEpisodePass(1, 0, 0, 0), await PassAsync());
        Assert.Empty(_plex.Requests);
        Assert.Empty(_discord.Requests);
        await using (var db = _database.CreateContext())
        {
            Assert.Equal(PlexWait.NoRecipient, (await db.NewEpisodeAnnouncements.SingleAsync()).PlexWait);
        }
    }

    [Fact]
    public async Task A_user_discord_refuses_is_given_up_alone_and_a_429_holds_the_pass_without_spending_an_attempt()
    {
        // The owner's first message is rate limited (2 s); alice is refused for good: no server in common with the bot.
        _discordAnswers[D1] = new([new FakeHttpServer.Answer(429, """{"message": "You are being rate limited.", "retry_after": 2.5, "global": false}""")]);
        _discordAnswers[D2] = new([new FakeHttpServer.Answer(403, """{"message": "Cannot send messages to this user", "code": 50007}""")]);
        await CollectAsync(Imported(_show, Episode(1)));
        _plexFiles.Add(Plex(Episode(1)));
        _time.Advance(TimeSpan.FromMinutes(3));

        Assert.Equal(new NewEpisodePass(1, 0, 0, 1), await PassAsync());
        await using (var db = _database.CreateContext())
        {
            var messages = await db.NewEpisodeMessages.OrderBy(m => m.Id).ToListAsync();
            Assert.Equal((WebhookDeliveryState.Pending, 0, 429), (messages[0].State, messages[0].Attempts, messages[0].LastStatusCode));
            Assert.Equal(_time.GetUtcNow().UtcDateTime.AddSeconds(2.5), messages[0].NextAttemptAt);

            // alice waits with the owner: the bot was told to wait, whoever the message is for.
            Assert.Equal(messages[0].NextAttemptAt, messages[1].NextAttemptAt);
            Assert.Equal(0, messages[1].Attempts);
        }

        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(new NewEpisodePass(0, 0, 1, 1), await PassAsync());
        await using (var db = _database.CreateContext())
        {
            var messages = await db.NewEpisodeMessages.OrderBy(m => m.Id).ToListAsync();
            Assert.Equal((WebhookDeliveryState.Delivered, 1), (messages[0].State, messages[0].Attempts));
            Assert.Equal((WebhookDeliveryState.GivenUp, 403), (messages[1].State, messages[1].LastStatusCode));
            Assert.Equal("Discord: Cannot send messages to this user (50007)", messages[1].LastError);
        }

        // Given up is final; nothing is due any more.
        _time.Advance(TimeSpan.FromHours(1));
        var before = _discord.Requests.Count;
        Assert.Equal(new NewEpisodePass(0, 0, 0, 0), await PassAsync());
        Assert.Equal(before, _discord.Requests.Count);

        Assert.Contains(_logs.Lines, l => l.Contains("Discord refuses direct messages", StringComparison.Ordinal));
        Assert.DoesNotContain(_logs.Lines, l => l.Contains(BotToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Other_failures_retry_with_the_webhook_escalation_and_the_token_is_never_logged()
    {
        _discordAnswers[D1] = new([new FakeHttpServer.Answer(500), new FakeHttpServer.Answer(502)]);
        await CollectAsync(Imported(_show, Episode(1)));
        _plexFiles.Add(Plex(Episode(1)));
        _time.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(new NewEpisodePass(1, 0, 1, 1), await PassAsync());

        // First retry a minute later (Radarr's escalation), then five.
        _time.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(new NewEpisodePass(0, 0, 0, 0), await PassAsync());
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new NewEpisodePass(0, 0, 0, 1), await PassAsync());
        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(new NewEpisodePass(0, 0, 1, 0), await PassAsync());
        await using (var db = _database.CreateContext())
        {
            var owner = await db.NewEpisodeMessages.SingleAsync(m => m.DiscordUserId == D1);
            Assert.Equal((WebhookDeliveryState.Delivered, 3), (owner.State, owner.Attempts));
        }

        Assert.NotEmpty(_logs.Lines);
        Assert.DoesNotContain(_logs.Lines, l => l.Contains(BotToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nothing_is_collected_without_a_bot_token()
    {
        await using (var db = _database.CreateContext())
        {
            (await db.InstanceSettings.SingleAsync()).DiscordBotToken = null;
            await db.SaveChangesAsync();
        }

        await CollectAsync(Imported(_show, Episode(1)));
        await using var check = _database.CreateContext();
        Assert.Empty(await check.NewEpisodeAnnouncements.ToListAsync());
    }

    [Fact]
    public void The_message_cuts_a_long_batch_and_leaves_the_link_out_without_plex()
    {
        var episodes = Enumerable.Range(1, 20).Select(n => new AnnouncedEpisode(2, n, n == 4 ? null : $"Part {n}", null)).ToList();
        var payload = NewEpisodeMessages.Payload("Invented Show", "not a url", episodes, null, T0);
        var embed = Assert.Single(payload.Embeds!);
        Assert.Equal("Invented Show - 20 new episodes (S02 · E01–E20)", embed.Title);
        var lines = embed.Description!.Split('\n');
        Assert.Equal(NewEpisodeMessages.ListedEpisodes + 1, lines.Length);
        Assert.Equal("S02 · E04 - Episode 4", lines[3]);
        Assert.Equal("… and 5 more", lines[^1]);
        Assert.Null(embed.Image);
        Assert.Null(embed.Fields);
        Assert.Equal(
            "https://app.plex.tv/desktop/#!/server/abc/details?key=%2Flibrary%2Fmetadata%2F42",
            NewEpisodeMessages.PlexUrl(new PlexShowLink("abc", "42")));
    }

    private async Task<NewEpisodePass> PassAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NewEpisodeAnnouncements>().RunAsync(CancellationToken.None);
    }

    private async Task CollectAsync(params EpisodeFileImported[] events)
    {
        await using var db = _database.CreateContext();
        await NewEpisodeCollector.CollectAsync(db, events, _time.GetUtcNow().UtcDateTime, _services.GetRequiredService<IOptions<NewEpisodeOptions>>().Value, CancellationToken.None);
        await db.SaveChangesAsync();
    }

    private static EpisodeRef Episode(int number) =>
        new(500 + number, 1, number, $"Chapter {number}", Overview: $"What happens in chapter {number}.");

    private static string FileOf(EpisodeRef episode) => $"/data/tv/Invented Show/Season 01/Invented.Show.S01E{episode.EpisodeNumber:00}.mkv";

    private static string Plex(EpisodeRef episode) => FileOf(episode).Replace("/data/", "/media/", StringComparison.Ordinal);

    private EpisodeFileImported Imported(Show series, EpisodeRef episode) => new(
        new SeriesRef(series.Id, series.TvdbId, null, null, series.Title, series.Year, SeriesType.Standard),
        new SeriesVersionRef(1, "1080p", true, true, "HD"),
        [episode],
        new FileRef(++_episodeId, FileOf(episode), "WEBDL-1080p", 1_000_000),
        null,
        null,
        null,
        false,
        "hardlink");

    private string Leaves() => JsonSerializer.Serialize(new
    {
        MediaContainer = new
        {
            Metadata = _plexFiles.Select((file, i) => new { parentIndex = 1, index = 100 + i, Media = new[] { new { Part = new[] { new { file } } } } }),
        },
    });

    private FakeHttpServer.Answer? Discord(FakeHttpServer.Received request)
    {
        if (request.Path == "/api/v10/users/@me/channels")
        {
            using var doc = JsonDocument.Parse(request.Body);
            return new FakeHttpServer.Answer(200, $$"""{"id": "dm-{{doc.RootElement.GetProperty("recipient_id").GetString()}}", "type": 1}""");
        }

        const string prefix = "/api/v10/channels/dm-";
        if (request.Path.StartsWith(prefix, StringComparison.Ordinal) && request.Path.EndsWith("/messages", StringComparison.Ordinal))
        {
            var user = request.Path[prefix.Length..^"/messages".Length];
            return _discordAnswers.TryGetValue(user, out var answers) && answers.TryDequeue(out var answer)
                ? answer
                : new FakeHttpServer.Answer(200, """{"id": "1"}""");
        }

        return null;
    }

    private static MediaServerItem Item(BigarrDbContext db, MediaServer server, string ratingKey, int? showTvdbId, string showKey) =>
        db.MediaServerItems.Add(new MediaServerItem
        {
            MediaServerId = server.Id,
            RatingKey = ratingKey,
            Kind = MediaItemKind.Episode,
            Title = "Chapter 1",
            ShowTitle = "A show",
            ShowTvdbId = showTvdbId,
            ShowRatingKey = showKey,
            SeasonNumber = 1,
            EpisodeNumber = 1,
            UpdatedAt = T0.AddDays(-1),
        }).Entity;

    private void AddUser(BigarrDbContext db, MediaServer server, string name, UserRole role, string? discord, string? plexId, MediaServerItem? watched, bool owner = false, List<string>? mentions = null)
    {
        var user = db.Users.Add(new User { Username = name, PasswordHash = string.Empty, Role = role, DiscordUserId = discord, DiscordMentions = mentions }).Entity;
        db.SaveChanges();
        _users[name] = user.Id;
        if (plexId is not null)
        {
            db.ExternalLogins.Add(new ExternalLogin { Provider = PlexIdentityProvider.ProviderKey, ExternalId = plexId, UserId = user.Id });
        }

        if (watched is null)
        {
            return;
        }

        var account = db.MediaServerUsers.Add(new MediaServerUser { MediaServerId = server.Id, ExternalId = owner ? "1" : plexId!, Name = name, IsOwner = owner, FirstSeenAt = T0.AddDays(-30) }).Entity;
        db.Plays.Add(new Play { User = account, Item = watched, StartedAt = T0.AddDays(-2), StoppedAt = T0.AddDays(-2).AddMinutes(40), PlayedSeconds = 2400, Watched = true });
    }

    private sealed class LogCapture : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Logger(LogCapture owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Lines.Enqueue($"{category}: {formatter(state, exception)} {exception}");
        }
    }
}
