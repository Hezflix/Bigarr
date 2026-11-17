using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Automation;
using Bigarr.Api.Tests.Series;
using Bigarr.Domain.Library;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Search;

/// <summary>Shadow mode on a clock the test moves: the alignment gives up after two days.</summary>
public sealed class SeasonAutoAlignmentApiFactory : BigarrApiFactory
{
    public static readonly DateTimeOffset Start = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    public SeasonAutoAlignmentApiFactory()
        : this(new AdjustableClock(Start))
    {
    }

    private SeasonAutoAlignmentApiFactory(AdjustableClock clock)
        : base(null, new Dictionary<string, string> { ["Bigarr:Shadow:Enabled"] = "true" }, deleteOnDispose: true, time: clock)
    {
        Clock = clock;
    }

    public AdjustableClock Clock { get; }
}

/// <summary>
/// The automatic season alignment (off by default) on the mock indexer and feed: off-group episodes become cutoff unmet
/// and RSS takes the season group's pack; "keep as is" and the two-day give-up turn it off.
/// </summary>
public sealed class SeasonAutoAlignmentTests(SeasonAutoAlignmentApiFactory factory) : IClassFixture<SeasonAutoAlignmentApiFactory>
{
    [Fact]
    public async Task Auto_alignment_wants_off_group_episodes_until_kept_as_is_or_given_up_and_rss_takes_the_group_pack()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);
        var version = series.Versions.Single();

        // Season 5 at the profile's cutoff (Bluray-1080p): thirteen files from ROVERS, the last three from another group.
        var episodes = (await client.GetFromJsonAsync<List<Bigarr.Api.Features.Series.EpisodeResponse>>($"/api/v1/series/{series.Id}/episodes", Json))!
            .Where(e => e.SeasonNumber == 5).OrderBy(e => e.EpisodeNumber).ToList();
        Assert.Equal(16, episodes.Count);
        foreach (var e in episodes)
        {
            await FileAsync(series.Id, version.Id, 5, e.EpisodeNumber, e.EpisodeNumber <= 13 ? "ROVERS" : "OTHER");
        }

        var offGroup = episodes.Where(e => e.EpisodeNumber > 13).Select(e => e.Id).ToList();

        // 1. Off by default: the season is finished, the feed's ROVERS pack is no upgrade.
        Assert.False((await client.GetFromJsonAsync<MediaManagementResponse>("/api/v1/mediamanagement", Json))!.AlignSeasonGroups);
        Assert.Empty(await AlignWantedAsync(client, series.Id));
        var before = await RssPackAsync(client);
        Assert.Null(before.AttributedVersionId);
        Assert.Contains(before.Versions.Single().Rejections, r => r.Reason.StartsWith("Not an upgrade for every episode", StringComparison.Ordinal));

        // 2. Switched on: the three episodes from the other group are cutoff unmet, waiting for ROVERS.
        await SetAutoAlignAsync(client, true);
        var wanted = await AlignWantedAsync(client, series.Id);
        Assert.Equal(offGroup, wanted.Select(w => w.EpisodeId).Order());
        Assert.All(wanted, w => Assert.Equal(("ROVERS", WantedFilter.CutoffUnmet), (w.AlignTo, w.Kind)));
        var url = $"/api/v1/series/{series.Id}/versions/{version.Id}/seasons/5/alignment";
        var preview = (await client.GetFromJsonAsync<SeasonAlignmentResponse>(url, Json))!;
        Assert.True(preview.AutoAlign);
        Assert.All(preview.Episodes, e => Assert.Equal((SeasonAutoAlignmentApiFactory.Start + TimeSpan.FromDays(2)).UtcDateTime, e.GivesUpAt));

        // 3. Keep as is: the season is left alone; switched back, it is wanted again.
        var kept = await (await client.PutAsJsonAsync(url, new SeasonAlignmentKeepRequest(true), Json)).Content.ReadFromJsonAsync<SeasonAlignmentResponse>(Json);
        Assert.True(kept!.KeepAsIs);
        Assert.Empty(await AlignWantedAsync(client, series.Id));
        var page = (await client.GetFromJsonAsync<Bigarr.Api.Features.Series.SeriesResponse>($"/api/v1/series/{series.Id}", Json))!;
        Assert.True(page.Seasons.Single(s => s.SeasonNumber == 5).Versions.Single().KeepAsIs);
        await client.PutAsJsonAsync(url, new SeasonAlignmentKeepRequest(false), Json);
        Assert.Equal(3, (await AlignWantedAsync(client, series.Id)).Count);

        // 4. Two days on, the group never came: the alignment gives up on them.
        factory.Clock.Now = SeasonAutoAlignmentApiFactory.Start + TimeSpan.FromDays(2) + TimeSpan.FromMinutes(1);
        Assert.Empty(await AlignWantedAsync(client, series.Id));
        Assert.All((await client.GetFromJsonAsync<SeasonAlignmentResponse>(url, Json))!.Episodes, e => Assert.True(e.GaveUp));

        // 5. Within the two days, RSS takes the group's season pack for the version.
        factory.Clock.Now = SeasonAutoAlignmentApiFactory.Start + TimeSpan.FromHours(1);
        var after = await RssPackAsync(client);
        Assert.Equal(version.Id, after.AttributedVersionId);
        Assert.NotNull(after.ShadowPickId);

        // The pick says the season's group decided it; Compare shows it as an intended difference, not a mismatch.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var pick = await db.SeriesShadowPicks.SingleAsync(p => p.Id == after.ShadowPickId);
            Assert.Equal(("ROVERS", true), (pick.SeasonGroup, pick.SeasonGroupRealigned));
        }

        var compare = (await client.GetFromJsonAsync<Bigarr.Api.Features.Shadow.SeriesCompareResponse>($"/api/v1/shadow/compare/series?tvdbId={SeriesSearchTests.BreakingBad}", Json))!;
        var row = Assert.Single(compare.Rows, r => r.Bigarr.Any(p => p.Id == after.ShadowPickId));
        Assert.Equal(Bigarr.Api.Features.Shadow.SeriesCompareOutcome.Intended, row.Outcome);
        Assert.Equal("season-group", row.Reason);
        Assert.Equal(1, compare.Summary.Intended);
        Assert.Equal(0, compare.Summary.BigarrOnly);
    }

    private static async Task<List<SeriesWantedItemResponse>> AlignWantedAsync(HttpClient client, int seriesId) =>
        (await client.GetFromJsonAsync<SeriesWantedPageResponse>($"/api/v1/series/wanted?filter=cutoffUnmet&seriesId={seriesId}&pageSize=200", Json))!
            .Items.Where(i => i.SeasonNumber == 5).ToList();

    private static async Task<RssReleaseResponse> RssPackAsync(HttpClient client)
    {
        var sync = await client.PostAsync("/api/v1/rss/sync", null);
        Assert.Equal(HttpStatusCode.OK, sync.StatusCode);
        var releases = (await client.GetFromJsonAsync<List<RssReleaseResponse>>("/api/v1/rss/releases", Json))!;
        return releases.Single(r => r.Title == "Breaking.Bad.S05.1080p.BluRay.x264-ROVERS");
    }

    private static async Task SetAutoAlignAsync(HttpClient client, bool on)
    {
        var current = (await client.GetFromJsonAsync<MediaManagementResponse>("/api/v1/mediamanagement", Json))!;
        var put = await client.PutAsJsonAsync("/api/v1/mediamanagement", new MediaManagementRequest(
            current.RenameFiles, current.FileNameTemplate, current.FolderNameTemplate, current.TransferMode, current.RecycleBinPath, current.RecycleBinRetentionDays,
            AlignSeasonGroups: on), Json);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(on, (await put.Content.ReadFromJsonAsync<MediaManagementResponse>(Json))!.AlignSeasonGroups);
    }

    private async Task FileAsync(int seriesId, int versionId, int season, int number, string group)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var target = await db.EpisodeTargets.Include(t => t.Episode).SingleAsync(t => t.VersionId == versionId && t.Episode.SeriesId == seriesId && t.Episode.SeasonNumber == season && t.Episode.EpisodeNumber == number);
        target.EpisodeFile = new EpisodeFile
        {
            VersionId = versionId,
            RelativePath = $"Season {season}/Breaking Bad - S{season:00}E{number:00}.mkv",
            SeasonNumber = season,
            Size = 4_000_000_000,
            QualityId = ParserQuality.Bluray1080p.Id,
            OriginalReleaseTitle = $"Breaking.Bad.S{season:00}E{number:00}.1080p.BluRay.x264-{group}",
            ReleaseGroup = group,
        };
        await db.SaveChangesAsync();
    }
}
