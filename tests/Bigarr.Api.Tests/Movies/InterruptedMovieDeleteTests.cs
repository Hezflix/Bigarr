using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Movies;

/// <summary>
/// A film delete that removes a file takes it off the disk, then saves. The caller going away in between (a closed
/// tab, a proxy timeout) must not leave a version that still claims a file that is gone: nothing repairs that afterwards.
/// </summary>
public sealed class InterruptedMovieDeleteTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-interrupted-movie-delete-").FullName;

    public void Dispose() => TryDelete(_root);

    [Fact]
    public async Task A_file_delete_whose_caller_goes_away_after_the_file_is_gone_still_clears_the_version()
    {
        var clock = new TrippingClock(DateTimeOffset.UtcNow);
        using var factory = new ConfiguredBigarrApiFactory(null, null, time: clock);
        var client = await ClientAsync(factory);
        var (movie, filePath) = await ScannedMatrixAsync(client);
        var version = (await GetMovieAsync(client, movie.Id)).Versions.Single();

        using var aborted = new CancellationTokenSource();
        clock.Trip(() => !File.Exists(filePath), aborted);
        using (var scope = factory.Services.CreateScope())
        {
            try
            {
                await scope.ServiceProvider.GetRequiredService<MovieService>().DeleteFileAsync(movie.Id, version.Id, aborted.Token);
            }
            catch (OperationCanceledException)
            {
                // what the endpoint would see
            }
        }

        Assert.True(aborted.IsCancellationRequested, "the delete never reached the file");
        Assert.False(File.Exists(filePath));
        Assert.Null((await GetMovieAsync(client, movie.Id)).Versions.Single().File);
    }

    [Fact]
    public async Task A_version_delete_whose_caller_goes_away_after_the_file_is_gone_still_removes_the_version()
    {
        var clock = new TrippingClock(DateTimeOffset.UtcNow);
        using var factory = new ConfiguredBigarrApiFactory(null, null, time: clock);
        var client = await ClientAsync(factory);
        var (movie, _) = await ScannedMatrixAsync(client);
        var (_, uhd) = await ProfilesAsync(client);
        var second = await AddVersionAsync(client, movie.Id, new AddVersionRequest("4k", VersionMode.Managed, uhd));
        var filePath = await GiveFileAsync(factory, movie.Id, second.Id);

        using var aborted = new CancellationTokenSource();
        clock.Trip(() => !File.Exists(filePath), aborted);
        using (var scope = factory.Services.CreateScope())
        {
            try
            {
                await scope.ServiceProvider.GetRequiredService<MovieService>().DeleteVersionAsync(movie.Id, second.Id, deleteFiles: true, promote: null, addImportExclusion: false, aborted.Token);
            }
            catch (OperationCanceledException)
            {
                // what the endpoint would see
            }
        }

        Assert.True(aborted.IsCancellationRequested, "the delete never reached the file");
        Assert.False(File.Exists(filePath));
        Assert.Single((await GetMovieAsync(client, movie.Id)).Versions);
    }

    [Fact]
    public async Task Deleting_the_last_version_with_its_file_still_removes_the_film_when_the_caller_goes_away()
    {
        var clock = new TrippingClock(DateTimeOffset.UtcNow);
        using var factory = new ConfiguredBigarrApiFactory(null, null, time: clock);
        var client = await ClientAsync(factory);
        var (movie, filePath) = await ScannedMatrixAsync(client);
        var version = (await GetMovieAsync(client, movie.Id)).Versions.Single();

        using var aborted = new CancellationTokenSource();
        clock.Trip(() => !File.Exists(filePath), aborted);
        using (var scope = factory.Services.CreateScope())
        {
            try
            {
                await scope.ServiceProvider.GetRequiredService<MovieService>().DeleteVersionAsync(movie.Id, version.Id, deleteFiles: true, promote: null, addImportExclusion: true, aborted.Token);
            }
            catch (OperationCanceledException)
            {
                // what the endpoint would see
            }
        }

        Assert.True(aborted.IsCancellationRequested, "the delete never reached the file");
        Assert.False(File.Exists(filePath));
        Assert.DoesNotContain((await client.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!, m => m.Id == movie.Id);
    }

    /// <summary>A film found by a scan, with its file on disk.</summary>
    private async Task<(MovieSummary Movie, string FilePath)> ScannedMatrixAsync(HttpClient client)
    {
        var folder = Path.Combine(_root, "The Matrix (1999)");
        Directory.CreateDirectory(folder);
        var filePath = Path.Combine(folder, "The.Matrix.1999.1080p.BluRay.x264-GRP.mkv");
        File.WriteAllBytes(filePath, new byte[64 * 1024]);
        var root = await AddRootAsync(client, _root);
        await ScanAsync(client, root.Id);
        var movie = (await client.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!.Single(m => m.TmdbId == 603);
        return (movie, filePath);
    }

    /// <summary>A real file in the version's folder, recorded as its file (as an import would).</summary>
    private static async Task<string> GiveFileAsync(BigarrApiFactory factory, int movieId, int versionId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var version = await db.MovieVersions.Include(v => v.RootFolder).SingleAsync(v => v.Id == versionId && v.MovieId == movieId);
        const string name = "The.Matrix.1999.2160p.BluRay.x265-GRP.mkv";
        var path = Path.Combine(version.RootFolder.Path, version.FolderName, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, new byte[4096]);
        version.File = new MediaFile { RelativePath = name, Size = 4096, QualityId = ParserQuality.Bluray2160p.Id };
        await db.SaveChangesAsync();
        return path;
    }

    private static async Task ScanAsync(HttpClient client, int rootId)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/v1/rootfolders/{rootId}/scan", null)).StatusCode);
        await WaitUntilAsync(async () =>
        {
            var status = (await client.GetFromJsonAsync<ScanStatusResponse>($"/api/v1/rootfolders/{rootId}/scan", Json))!;
            Assert.NotEqual(ScanState.Failed, status.State);
            return status.State is ScanState.Completed ? status : null;
        }, "the scan to finish");
    }

    /// <summary>A clock that cancels a token the first time it is read once a condition holds (the service stamps its history line after the file went).</summary>
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
