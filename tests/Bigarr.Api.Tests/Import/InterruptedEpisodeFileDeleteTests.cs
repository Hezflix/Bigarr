using System.Net.Http.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Search;
using Bigarr.Api.Tests.Series;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Import;

/// <summary>Deleting an episode file removes it from the disk, then its row: the caller going away in between must not leave a row for a file that is gone.</summary>
public sealed class InterruptedEpisodeFileDeleteTests
{
    [Fact]
    public async Task An_episode_file_delete_whose_caller_goes_away_after_the_file_is_gone_still_removes_the_row()
    {
        var clock = new TrippingClock(SeriesEndpointsTests.Now);
        using var factory = new ConfiguredBigarrApiFactory(null, null, time: clock);
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        var version = series.Versions.Single();
        var folder = Path.Combine(version.RootFolderPath, version.FolderName);
        const string relative = "Season 1/Breaking Bad - S01E01 - Pilot Bluray-1080p.mkv";
        var path = Path.Combine(folder, relative);
        try
        {
            var fileId = await FileAsync(factory, series.Id, version.Id, path, relative);

            // The request is aborted the moment the file is gone, before the row is.
            using var aborted = new CancellationTokenSource();
            clock.Trip(() => !File.Exists(path), aborted);
            using (var scope = factory.Services.CreateScope())
            {
                try
                {
                    await scope.ServiceProvider.GetRequiredService<SeriesManualImportService>().DeleteEpisodeFileAsync(series.Id, fileId, aborted.Token);
                }
                catch (OperationCanceledException)
                {
                    // what the endpoint would see
                }
            }

            Assert.True(aborted.IsCancellationRequested, "the delete never reached the file");
            Assert.False(File.Exists(path));
            var files = (await client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{series.Id}/files", Json))!.Files;
            Assert.DoesNotContain(files, f => f.Id == fileId);
        }
        finally
        {
            TryDelete(folder);
        }
    }

    /// <summary>A real file in the version folder, recorded as the first episode's file (as an import would).</summary>
    private static async Task<int> FileAsync(BigarrApiFactory factory, int seriesId, int versionId, string path, string relativePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, new byte[4096]);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var target = await db.EpisodeTargets.Include(t => t.Episode).SingleAsync(t => t.VersionId == versionId && t.Episode.SeriesId == seriesId && t.Episode.SeasonNumber == 1 && t.Episode.EpisodeNumber == 1);
        var file = new EpisodeFile
        {
            VersionId = versionId,
            RelativePath = relativePath,
            SeasonNumber = 1,
            Size = 4096,
            QualityId = ParserQuality.Bluray1080p.Id,
            OriginalReleaseTitle = "Breaking.Bad.S01E01.1080p.BluRay.x264-GRP",
            ReleaseGroup = "GRP",
        };
        target.EpisodeFile = file;
        await db.SaveChangesAsync();
        return file.Id;
    }

    /// <summary>A clock that cancels a token the first time it is read once a condition holds (the service stamps its event after the file went).</summary>
    private sealed class TrippingClock(DateTimeOffset now) : TimeProvider
    {
        private Func<bool>? _when;
        private CancellationTokenSource? _cancel;

        public void Trip(Func<bool> when, CancellationTokenSource cancel)
        {
            _cancel = cancel;
            _when = when;
        }

        public override DateTimeOffset GetUtcNow()
        {
            if (_when is { } when && _cancel is { IsCancellationRequested: false } cancel && when())
            {
                cancel.Cancel();
            }

            return now;
        }
    }
}
