using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Series;

/// <summary>Two versions of a show in one folder (the default for a second version): a file there belongs to one of them.</summary>
public sealed class SharedFolderScanTests(SeriesApiFactory factory) : IClassFixture<SeriesApiFactory>
{
    [Fact]
    public async Task A_file_in_a_folder_two_versions_share_is_attached_to_one_version_only()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        var first = series.Versions.Single();
        var folder = Path.Combine(first.RootFolderPath, first.FolderName);
        try
        {
            var profiles = (await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!;
            var uhd = profiles.Single(p => p.Name == QualitySeeder.SeriesUhdProfileName);
            var added = await client.PostAsJsonAsync($"/api/v1/series/{series.Id}/versions", new AddSeriesVersionRequest("4k", uhd.Id), Json);
            Assert.True(added.StatusCode == HttpStatusCode.Created, await added.Content.ReadAsStringAsync());
            var second = (await client.GetFromJsonAsync<SeriesResponse>($"/api/v1/series/{series.Id}", Json))!.Versions.Single(v => v.Id != first.Id);
            Assert.Equal(folder, Path.Combine(second.RootFolderPath, second.FolderName));

            Directory.CreateDirectory(Path.Combine(folder, "Season 01"));
            await File.WriteAllBytesAsync(Path.Combine(folder, "Season 01", "Breaking.Bad.S01E01.1080p.BluRay.x264-GRP.mkv"), new byte[4096]);

            // Twice: the second scan must not hand the file to the sibling either.
            await RescanAsync(client, series.Id);
            await RescanAsync(client, series.Id);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var rows = await db.EpisodeFiles.AsNoTracking().Where(f => f.VersionId == first.Id || f.VersionId == second.Id).ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal(first.Id, row.VersionId);
            Assert.Empty(await db.UnmatchedEpisodeFiles.AsNoTracking().Where(u => u.SeriesId == series.Id).ToListAsync());
            Assert.Equal(1, await db.EpisodeTargets.AsNoTracking().CountAsync(t => t.Episode.SeriesId == series.Id && t.EpisodeFileId != null));

            // A library scanned before siblings were known: the second version holds a copy of the row. The next scan
            // drops the copy, keeps the older row and never touches the file.
            var original = await db.EpisodeFiles.AsNoTracking().SingleAsync(f => f.Id == row.Id);
            var target = await db.EpisodeTargets.SingleAsync(t => t.VersionId == second.Id && t.Episode.SeriesId == series.Id && t.Episode.SeasonNumber == 1 && t.Episode.EpisodeNumber == 1);
            target.EpisodeFile = new Bigarr.Domain.Tv.EpisodeFile
            {
                VersionId = second.Id,
                RelativePath = original.RelativePath,
                SeasonNumber = 1,
                Size = original.Size,
                QualityId = original.QualityId,
            };
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await RescanAsync(client, series.Id);

            Assert.Equal(row.Id, Assert.Single(await db.EpisodeFiles.AsNoTracking().Where(f => f.VersionId == first.Id || f.VersionId == second.Id).ToListAsync()).Id);
            Assert.Null((await db.EpisodeTargets.AsNoTracking().SingleAsync(t => t.Id == target.Id)).EpisodeFileId);
            Assert.True(File.Exists(Path.Combine(folder, original.RelativePath)));
        }
        finally
        {
            TryDelete(folder);
        }
    }

    private static async Task RescanAsync(HttpClient client, int seriesId)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/v1/series/{seriesId}/rescan", null)).StatusCode);
        await WaitUntilAsync(async () =>
        {
            var status = (await client.GetFromJsonAsync<ScanStatusResponse>($"/api/v1/series/{seriesId}/rescan", Json))!;
            Assert.NotEqual(ScanState.Failed, status.State);
            return status.State is ScanState.Completed ? status : null;
        }, "the rescan to finish");
    }
}
