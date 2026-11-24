using Bigarr.Api.Features.Notifications.Discord;
using Bigarr.Api.Features.Notifications.NewEpisodes;
using Bigarr.Domain.Notifications;

namespace Bigarr.Api.Tests.Notifications;

/// <summary>The admin's wording of the new-episode message and where its Plex link goes.</summary>
public sealed class NewEpisodeTemplateTests
{
    private static readonly DateTime T0 = new(2026, 11, 24, 20, 0, 0, DateTimeKind.Utc);
    private static readonly PlexShowLink Link = new("abc", "42");

    [Fact]
    public void Empty_texts_give_the_english_defaults()
    {
        var blank = new NewEpisodeSettings { Content = "   ", Title = "" };
        var payload = NewEpisodeMessages.Payload("Invented Show", null, [new AnnouncedEpisode(1, 2, "Second", null)], Link, T0, blank);
        Assert.Equal(
            DiscordWebhook.Serialize(NewEpisodeMessages.Payload("Invented Show", null, [new AnnouncedEpisode(1, 2, "Second", null)], Link, T0)),
            DiscordWebhook.Serialize(payload));
        Assert.Equal("📺 Invented Show - Season 1 · Episode 2 - Second 👈 just added to Plex!", payload.Content);
        var embed = Assert.Single(payload.Embeds!);
        Assert.Equal("Invented Show - Second (S1 · E2)", embed.Title);
        var field = Assert.Single(embed.Fields!);
        Assert.Equal("View on Plex", field.Name);
        Assert.StartsWith("[Open the show on Plex](https://app.plex.tv/", field.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Own_texts_fill_their_placeholders_for_one_episode()
    {
        var french = new NewEpisodeSettings
        {
            Content = "📺 {Show} - Saison {Season} · Episode {Episode} - {EpisodeTitle} 👈 vient d'être ajouté à Plex !",
            Title = "{Show} S{Season:00}E{Episode:00}",
            LinkName = "Plex",
            LinkText = "Lien vers l'épisode [{Show}]",
        };
        var payload = NewEpisodeMessages.Payload("Invented Show", null, [new AnnouncedEpisode(1, 2, null, null)], Link, T0, french);
        Assert.Equal("📺 Invented Show - Saison 1 · Episode 2 - Episode 2 👈 vient d'être ajouté à Plex !", payload.Content);
        var embed = Assert.Single(payload.Embeds!);
        Assert.Equal("Invented Show S01E02", embed.Title);
        var field = Assert.Single(embed.Fields!);
        Assert.Equal("Plex", field.Name);

        // Brackets would close the link text early.
        Assert.StartsWith("[Lien vers l'épisode (Invented Show)](https://app.plex.tv/", field.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Own_texts_fill_the_batch_lines_and_the_count_of_the_rest()
    {
        var settings = new NewEpisodeSettings
        {
            BatchContent = "{Count} nouveaux épisodes de {Show}",
            BatchTitle = "{Show} ({Range})",
            BatchLine = "{Season}x{Episode:00} {EpisodeTitle}",
            BatchMore = "+{More}",
        };
        var episodes = Enumerable.Range(1, 17).Select(n => new AnnouncedEpisode(3, n, $"Part {n}", null)).Reverse().ToList();
        var payload = NewEpisodeMessages.Payload("Invented Show", null, episodes, null, T0, settings);
        Assert.Equal("17 nouveaux épisodes de Invented Show", payload.Content);
        var embed = Assert.Single(payload.Embeds!);
        Assert.Equal("Invented Show (S03 · E01–E17)", embed.Title);
        var lines = embed.Description!.Split('\n');
        Assert.Equal("3x01 Part 1", lines[0]);
        Assert.Equal("+2", lines[^1]);
    }

    [Fact]
    public void Unknown_placeholders_are_reported_per_text_and_left_as_typed()
    {
        var settings = new NewEpisodeSettings
        {
            Content = "{Show} {Title} {Show}",
            BatchContent = "{EpisodeTitle} {Count} {Count:00}",
            BatchLine = "{Show:00} { not one } {EpisodeTitle:000}",
            LinkText = "{Show}",
        };

        var problems = NewEpisodeTemplates.Validate(settings);
        Assert.Equal(
            [
                new TemplateProblem(NewEpisodeText.Content, "{Title}"),
                new TemplateProblem(NewEpisodeText.BatchContent, "{EpisodeTitle}"),
                new TemplateProblem(NewEpisodeText.BatchLine, "{Show:00}"),
                new TemplateProblem(NewEpisodeText.BatchLine, "{EpisodeTitle:000}"),
            ],
            problems);
        Assert.Empty(NewEpisodeTemplates.Validate(new NewEpisodeSettings()));

        var payload = NewEpisodeMessages.Payload("Invented Show", null, [new AnnouncedEpisode(1, 1, "One", null)], null, T0, settings);
        Assert.Equal("Invented Show {Title} Invented Show", payload.Content);
    }

    [Fact]
    public void The_link_opens_app_plex_tv_or_the_servers_own_address()
    {
        const string Route = "#!/server/abc/details?key=%2Flibrary%2Fmetadata%2F42";
        Assert.Equal($"https://app.plex.tv/desktop/{Route}", NewEpisodeMessages.PlexUrl(Link));
        Assert.Equal($"https://app.plex.tv/desktop/{Route}", NewEpisodeMessages.PlexUrl(Link, new NewEpisodeSettings { PlexWebAddress = "https://plex.example.com" }));
        Assert.Equal(
            $"https://plex.example.com/web/index.html{Route}",
            NewEpisodeMessages.PlexUrl(Link, new NewEpisodeSettings { LinkTarget = PlexLinkTarget.ServerAddress, PlexWebAddress = " https://plex.example.com/ " }));
        Assert.Equal(
            $"http://example.com:32400/plex/web/index.html{Route}",
            NewEpisodeMessages.PlexUrl(Link, new NewEpisodeSettings { LinkTarget = PlexLinkTarget.ServerAddress, PlexWebAddress = "http://example.com:32400/plex" }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plex.example.com")]
    [InlineData("ftp://plex.example.com")]
    [InlineData("https://plex.example.com/?x=1")]
    [InlineData("https://user:pass@plex.example.com")]
    public void The_servers_address_falls_back_to_app_plex_tv_when_missing_or_invalid(string? address)
    {
        Assert.Null(NewEpisodeTemplates.WebAddress(address));
        var url = NewEpisodeMessages.PlexUrl(Link, new NewEpisodeSettings { LinkTarget = PlexLinkTarget.ServerAddress, PlexWebAddress = address });
        Assert.StartsWith("https://app.plex.tv/desktop/", url, StringComparison.Ordinal);
    }
}
