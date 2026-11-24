using System.Net.Http.Json;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Shadow;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>
/// The series twin of <see cref="ShadowCompareEarlierPickTests"/>: Bigarr picked an episode, Sonarr grabbed it days later.
/// Compare shows one row, Bigarr first, even when the period asked starts after Bigarr's pick; a pick older than the
/// 14-day limit stays a row of its own.
/// </summary>
public sealed class SeriesCompareEarlierPickTests(ShadowApiFactory factory) : IClassFixture<ShadowApiFactory>
{
    private const int Show = 990_024;
    private const int OtherShow = 990_025;

    [Fact]
    public async Task A_pick_days_before_sonarrs_grab_shows_as_bigarr_first()
    {
        var client = await ClientAsync(factory);
        var now = DateTime.UtcNow;
        await SeedAsync(Show, "Show.S01E05.1080p.WEB-DL-A", "g-show-web", now.AddDays(-3), "Show.S01E05.1080p.BluRay-B", now);

        foreach (var days in new[] { 0, 1, 7 })
        {
            var compare = (await client.GetFromJsonAsync<SeriesCompareResponse>($"/api/v1/shadow/compare/series?tvdbId={Show}&days={days}", Json))!;
            var row = Assert.Single(compare.Rows);
            Assert.Equal(SeriesCompareOutcome.Different, row.Outcome);
            Assert.Single(row.Sonarr);
            Assert.Equal("Show.S01E05.1080p.WEB-DL-A", Assert.Single(row.Bigarr).Title);
            Assert.InRange(row.BigarrLeadSeconds!.Value, (long)TimeSpan.FromDays(3).TotalSeconds - 60, (long)TimeSpan.FromDays(3).TotalSeconds + 60);
            Assert.Equal(new SeriesCompareCounts(0, 1, 0, 0, 0), compare.Summary);
        }
    }

    [Fact]
    public async Task A_pick_older_than_fourteen_days_stays_one_sided()
    {
        var client = await ClientAsync(factory);
        var now = DateTime.UtcNow;
        await SeedAsync(OtherShow, "Other.S01E05.1080p.WEB-DL-A", "g-other-web", now.AddDays(-15), "Other.S01E05.1080p.BluRay-B", now);

        var all = (await client.GetFromJsonAsync<SeriesCompareResponse>($"/api/v1/shadow/compare/series?tvdbId={OtherShow}&days=0", Json))!;
        Assert.Equal(2, all.Rows.Count);
        Assert.Single(all.Rows, r => r.Outcome == SeriesCompareOutcome.SonarrOnly);
        Assert.Single(all.Rows, r => r.Outcome == SeriesCompareOutcome.BigarrOnly);

        var recent = (await client.GetFromJsonAsync<SeriesCompareResponse>($"/api/v1/shadow/compare/series?tvdbId={OtherShow}&days=1", Json))!;
        Assert.Equal(SeriesCompareOutcome.SonarrOnly, Assert.Single(recent.Rows).Outcome);
    }

    private async Task SeedAsync(int tvdbId, string pickTitle, string pickGuid, DateTime pickedAt, string grabTitle, DateTime grabbedAt)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var sonarr = new FollowedSonarr { Name = $"Sonarr {tvdbId}", Url = "http://sonarr.invalid", ApiKey = "key", VersionName = "1080p" };
        db.FollowedSonarrs.Add(sonarr);
        var grab = new SonarrGrab
        {
            FollowedSonarr = sonarr,
            SonarrHistoryId = tvdbId,
            SonarrSeriesId = 1,
            TvdbId = tvdbId,
            SeriesTitle = "Earlier Pick Show",
            VersionName = "1080p",
            GrabbedAt = grabbedAt,
            ReleaseTitle = grabTitle,
            ReleaseGuid = $"g-sonarr-{tvdbId}",
        };
        grab.Episodes.Add(new SonarrGrabEpisode { Grab = grab, SonarrHistoryId = tvdbId, SonarrEpisodeId = 1, SeasonNumber = 1, EpisodeNumber = 5 });
        db.SonarrGrabs.Add(grab);
        db.SeriesShadowPicks.Add(new SeriesShadowPick
        {
            TvdbId = tvdbId,
            SeriesTitle = "Earlier Pick Show",
            VersionName = "1080p",
            Trigger = "rss",
            PickedAt = pickedAt,
            LastPickedAt = pickedAt,
            ReleaseTitle = pickTitle,
            ReleaseGuid = pickGuid,
            Episodes = [new SeriesShadowPickEpisode { SeasonNumber = 1, EpisodeNumber = 5 }],
        });
        await db.SaveChangesAsync();
    }
}
