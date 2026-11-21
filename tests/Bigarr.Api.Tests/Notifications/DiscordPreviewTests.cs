using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Notifications.Discord;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Events;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Notifications;

/// <summary>The Discord form's preview: one message per event the format posts, through the real builder, from the library when it can.</summary>
public sealed class DiscordPreviewTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Every_event_the_Discord_format_posts_has_a_preview()
    {
        var posted = DomainEventCatalog.Entries.Where(e => WebhookRouting.SentToDiscord(e.Type)).Select(e => e.Type).ToList();

        var previews = DiscordPreview.All(null, null, null, Now, null);

        Assert.Equal(posted, previews.Select(p => p.EventType));
        Assert.All(previews, p => Assert.NotEmpty(p.Payload.Embeds!));
    }

    [Fact]
    public void The_preview_follows_the_fields_and_the_names_of_the_settings()
    {
        static List<string> GrabFields(DiscordSettings? settings) =>
            DiscordPreview.All(null, null, settings, Now, null).Single(p => p.EventType == "grabbed").Payload.Embeds![0].Fields!.Select(f => f.Name).ToList();

        Assert.Contains("Indexer", GrabFields(null));
        Assert.DoesNotContain("Indexer", GrabFields(new DiscordSettings(GrabFields: [DiscordGrabField.Quality, DiscordGrabField.Size])));

        var named = DiscordPreview.All(null, null, new DiscordSettings(Username: "Plex night", Author: "Home"), Now, null).Single(p => p.EventType == "grabbed").Payload;
        Assert.Equal("Plex night", named.Username);
        Assert.Equal("Home", named.Embeds![0].Author!.Name);
    }

    [Fact]
    public async Task The_endpoint_previews_the_film_last_put_on_disk_and_falls_back_to_a_sample()
    {
        using var factory = new ConfiguredBigarrApiFactory(null, new Dictionary<string, string> { ["Bigarr:Notifications:Enabled"] = "false" });
        var client = await ClientAsync(factory);

        var empty = await client.PostAsJsonAsync("/api/v1/notifications/discord/preview", new DiscordPreviewRequest(), Json);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        var sample = (await empty.Content.ReadFromJsonAsync<List<DiscordPreviewResponse>>(Json))!;
        Assert.Equal("Sample Film (2024)", sample.Single(p => p.EventType == "grabbed").Payload.Embeds![0].Title);

        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(Path.GetTempPath(), $"bigarr-preview-{Guid.NewGuid():N}"));
        var movie = await AddMovieAsync(client, 603, hd, root.Id);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var versionId = await db.MovieVersions.Where(v => v.MovieId == movie.Id).Select(v => v.Id).FirstAsync();
            db.MediaFiles.Add(new MediaFile
            {
                VersionId = versionId,
                RelativePath = "The Matrix (1999) [Bluray-1080p].mkv",
                OriginalReleaseTitle = "The.Matrix.1999.1080p.BluRay.x264-GROUP",
                ReleaseGroup = "GROUP",
                Size = 9_000_000_000,
                QualityId = 7,
                Languages = ["English"],
                DateAdded = Now,
            });
            await db.SaveChangesAsync();
        }

        var previews = (await client.PostAsJsonAsync("/api/v1/notifications/discord/preview", new DiscordPreviewRequest(new DiscordSettings(GrabFields: [DiscordGrabField.Release])), Json)
            .ContinueWith(t => t.Result.Content.ReadFromJsonAsync<List<DiscordPreviewResponse>>(Json)).Unwrap())!;
        var grab = previews.Single(p => p.EventType == "grabbed").Payload.Embeds![0];
        Assert.Equal($"{movie.Title} ({movie.Year})", grab.Title);
        var release = Assert.Single(grab.Fields!);
        Assert.Contains("The.Matrix.1999.1080p.BluRay.x264-GROUP", release.Value, StringComparison.Ordinal);
    }
}
