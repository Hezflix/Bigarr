using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Series;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Search;

/// <summary>
/// A season pinned to a release group the user chose, not the one with the most files: the preview, the Wanted list,
/// the automatic alignment and its give-up, RSS's choice and Compare's reason follow the pin; "keep as is" still wins;
/// unpinning goes back to the files.
/// </summary>
public sealed class SeasonGroupPinTests(SeasonAutoAlignmentApiFactory factory) : IClassFixture<SeasonAutoAlignmentApiFactory>
{
    [Fact]
    public async Task A_pinned_group_replaces_the_majority_everywhere_until_unpinned()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);
        var version = series.Versions.Single();

        // Season 5 at the cutoff: nine files from OTHER (the majority), seven from ROVERS, whose pack is in the feed.
        var episodes = (await client.GetFromJsonAsync<List<Bigarr.Api.Features.Series.EpisodeResponse>>($"/api/v1/series/{series.Id}/episodes", Json))!
            .Where(e => e.SeasonNumber == 5).OrderBy(e => e.EpisodeNumber).ToList();
        foreach (var e in episodes)
        {
            await FileAsync(series.Id, version.Id, e.EpisodeNumber, e.EpisodeNumber <= 9 ? "OTHER" : "ROVERS");
        }

        var otherIds = episodes.Where(e => e.EpisodeNumber <= 9).Select(e => e.Id).ToList();
        var roversIds = episodes.Where(e => e.EpisodeNumber > 9).Select(e => e.Id).ToList();
        await SetAutoAlignAsync(client, true);
        var url = $"/api/v1/series/{series.Id}/versions/{version.Id}/seasons/5/alignment";

        // Unpinned: OTHER has the most files, the ROVERS episodes wait for OTHER.
        var preview = (await client.GetFromJsonAsync<SeasonAlignmentResponse>(url, Json))!;
        Assert.Equal(("OTHER", "OTHER", (string?)null), (preview.SeasonGroup, preview.MajorityGroup, preview.PinnedGroup));
        Assert.Equal(roversIds, (await WantedAsync(client, series.Id)).Where(w => w.AlignTo == "OTHER").Select(w => w.EpisodeId).Order());

        // A group absent from the season, and specials, cannot be pinned.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{url}/group", new SeasonAlignmentGroupRequest("KAF"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/series/{series.Id}/versions/{version.Id}/seasons/0/alignment/group", new SeasonAlignmentGroupRequest("OTHER"), Json)).StatusCode);

        // Pin ROVERS (fewer files; any case): the preview, the series page and Wanted follow it.
        var pinned = await PinAsync(client, url, "rovers");
        Assert.Equal(("ROVERS", "OTHER", "ROVERS"), (pinned.SeasonGroup, pinned.MajorityGroup, pinned.PinnedGroup));
        Assert.Equal(otherIds, pinned.Episodes.Select(e => e.EpisodeId).Order());
        Assert.All(pinned.Episodes, e => Assert.Equal("OTHER", e.CurrentGroup));
        var state = (await client.GetFromJsonAsync<Bigarr.Api.Features.Series.SeriesResponse>($"/api/v1/series/{series.Id}", Json))!
            .Seasons.Single(s => s.SeasonNumber == 5).Versions.Single();
        Assert.Equal(("ROVERS", 9), (state.SeasonGroup, state.OffGroupEpisodes));
        var wanted = await WantedAsync(client, series.Id);
        Assert.Equal(otherIds, wanted.Select(w => w.EpisodeId).Order());
        Assert.All(wanted, w => Assert.Equal("ROVERS", w.AlignTo));

        // "Keep as is" still wins over the pin.
        await client.PutAsJsonAsync(url, new SeasonAlignmentKeepRequest(true), Json);
        Assert.Empty(await WantedAsync(client, series.Id));
        await client.PutAsJsonAsync(url, new SeasonAlignmentKeepRequest(false), Json);

        // The give-up counts for the pinned group too.
        factory.Clock.Now = SeasonAutoAlignmentApiFactory.Start + TimeSpan.FromDays(2) + TimeSpan.FromMinutes(1);
        Assert.Empty(await WantedAsync(client, series.Id));
        Assert.All((await client.GetFromJsonAsync<SeasonAlignmentResponse>(url, Json))!.Episodes, e => Assert.True(e.GaveUp));
        factory.Clock.Now = SeasonAutoAlignmentApiFactory.Start + TimeSpan.FromHours(1);

        // Unpin: back to the files' majority. Choosing the majority's chip unpins as well.
        var unpinned = await PinAsync(client, url, null);
        Assert.Equal(("OTHER", (string?)null), (unpinned.SeasonGroup, unpinned.PinnedGroup));
        Assert.Equal(roversIds, (await WantedAsync(client, series.Id)).Select(w => w.EpisodeId).Order());
        Assert.Null((await PinAsync(client, url, "OTHER")).PinnedGroup);

        // Pinned again, RSS takes ROVERS's season pack (refused while OTHER was the group), and Compare names ROVERS.
        var refused = await RssPackAsync(client);
        Assert.Null(refused.AttributedVersionId);
        await PinAsync(client, url, "ROVERS");
        factory.Clock.Now = SeasonAutoAlignmentApiFactory.Start + TimeSpan.FromHours(2);
        var taken = await RssPackAsync(client);
        Assert.Equal(version.Id, taken.AttributedVersionId);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var pick = await db.SeriesShadowPicks.SingleAsync(p => p.Id == taken.ShadowPickId);
            Assert.Equal(("ROVERS", true), (pick.SeasonGroup, pick.SeasonGroupRealigned));
        }

        var compare = (await client.GetFromJsonAsync<SeriesCompareResponse>($"/api/v1/shadow/compare/series?tvdbId={SeriesSearchTests.BreakingBad}", Json))!;
        var row = Assert.Single(compare.Rows, r => r.Bigarr.Any(p => p.Id == taken.ShadowPickId));
        Assert.Equal((SeriesCompareOutcome.Intended, "season-group"), (row.Outcome, row.Reason));
        Assert.Contains("ROVERS", row.ReasonDetail, StringComparison.Ordinal);
    }

    private static async Task<SeasonAlignmentResponse> PinAsync(HttpClient client, string url, string? group)
    {
        var response = await client.PutAsJsonAsync($"{url}/group", new SeasonAlignmentGroupRequest(group), Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SeasonAlignmentResponse>(Json))!;
    }

    private static async Task<List<SeriesWantedItemResponse>> WantedAsync(HttpClient client, int seriesId) =>
        (await client.GetFromJsonAsync<SeriesWantedPageResponse>($"/api/v1/series/wanted?filter=cutoffUnmet&seriesId={seriesId}&pageSize=200", Json))!
            .Items.Where(i => i.SeasonNumber == 5).ToList();

    private static async Task<RssReleaseResponse> RssPackAsync(HttpClient client)
    {
        var sync = await client.PostAsync("/api/v1/rss/sync", null);
        Assert.Equal(HttpStatusCode.OK, sync.StatusCode);
        return (await client.GetFromJsonAsync<List<RssReleaseResponse>>("/api/v1/rss/releases", Json))!.Single(r => r.Title == "Breaking.Bad.S05.1080p.BluRay.x264-ROVERS");
    }

    private static async Task SetAutoAlignAsync(HttpClient client, bool on)
    {
        var current = (await client.GetFromJsonAsync<MediaManagementResponse>("/api/v1/mediamanagement", Json))!;
        var put = await client.PutAsJsonAsync("/api/v1/mediamanagement", new MediaManagementRequest(
            current.RenameFiles, current.FileNameTemplate, current.FolderNameTemplate, current.TransferMode, current.RecycleBinPath, current.RecycleBinRetentionDays,
            AlignSeasonGroups: on), Json);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
    }

    private async Task FileAsync(int seriesId, int versionId, int number, string group)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var target = await db.EpisodeTargets.Include(t => t.Episode).SingleAsync(t => t.VersionId == versionId && t.Episode.SeriesId == seriesId && t.Episode.SeasonNumber == 5 && t.Episode.EpisodeNumber == number);
        target.EpisodeFile = new EpisodeFile
        {
            VersionId = versionId,
            RelativePath = $"Season 5/Breaking Bad - S05E{number:00}.mkv",
            SeasonNumber = 5,
            Size = 4_000_000_000,
            QualityId = ParserQuality.Bluray1080p.Id,
            OriginalReleaseTitle = $"Breaking.Bad.S05E{number:00}.1080p.BluRay.x264-{group}",
            ReleaseGroup = group,
        };
        await db.SaveChangesAsync();
    }
}
