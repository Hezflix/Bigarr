using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Infrastructure.Metadata.Fixtures;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Library;
using Bigarr.Domain.Metadata;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Library;

/// <summary>
/// A metadata provider that gets no answer in time throws a <see cref="TaskCanceledException"/>, the type a real
/// cancellation throws too. The scan must tell them apart: the folder that timed out is unmapped, the scan goes on.
/// </summary>
public sealed class LibraryScanTimeoutTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-lib-timeout-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public async Task A_metadata_timeout_on_one_folder_leaves_the_rest_of_the_scan_running()
    {
        Video("The Matrix (1999)", "The.Matrix.1999.1080p.BluRay.x264-GRP.mkv");
        Video("Amelie (2001)", "Amelie.2001.720p.BluRay.x264-GRP.mkv");
        Video("Parasite (2019)", "Parasite.2019.1080p.BluRay.x264-GRP.mkv");

        await using var factory = new TimeoutFactory(query => query.Contains("Amelie", StringComparison.OrdinalIgnoreCase));
        var client = factory.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        var added = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(_root));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var root = (await added.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!;

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/v1/rootfolders/{root.Id}/scan", null)).StatusCode);
        var scan = await WaitAsync(client, root.Id);

        Assert.Equal(ScanState.Completed, scan.State);
        Assert.Null(scan.Error);
        Assert.Equal(3, scan.FoldersTotal);
        Assert.Equal(2, scan.MoviesAdded);
        Assert.Equal(1, scan.Unmapped);

        var movies = (await client.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!;
        Assert.Contains(movies, m => m.TmdbId == 603);
        Assert.Contains(movies, m => m.TmdbId == 496243);
        Assert.DoesNotContain(movies, m => m.TmdbId == 194);

        var unmapped = (await client.GetFromJsonAsync<List<UnmappedFolderResponse>>($"/api/v1/rootfolders/{root.Id}/unmapped", Json))!;
        var failed = Assert.Single(unmapped);
        Assert.Equal("Amelie (2001)", failed.RelativePath);
        Assert.Equal(UnmappedReason.MetadataError, failed.Reason);
    }

    private static async Task<ScanStatusResponse> WaitAsync(HttpClient client, int rootFolderId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var status = (await client.GetFromJsonAsync<ScanStatusResponse>($"/api/v1/rootfolders/{rootFolderId}/scan", Json))!;
            if (status.State is ScanState.Completed or ScanState.Failed)
            {
                return status;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Scan did not finish within 30 seconds.");
    }

    private void Video(string folder, string file)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, folder)).FullName;
        File.WriteAllBytes(Path.Combine(dir, file), new byte[4096]);
    }

    /// <summary>The real app, its fixture catalogue behind a provider that times out for the searches a test names.</summary>
    private sealed class TimeoutFactory(Func<string, bool> timesOut) : BigarrApiFactory(null, null, deleteOnDispose: true)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddSingleton<IMetadataProvider>(new TimingOutProvider(timesOut)));
        }
    }

    private sealed class TimingOutProvider(Func<string, bool> timesOut) : IMetadataProvider
    {
        private readonly FixtureMetadataProvider _fixtures = new();

        public string Name => "timing-out";

        public Task<MovieMetadata?> GetMovieAsync(int tmdbId, CancellationToken cancellationToken) =>
            _fixtures.GetMovieAsync(tmdbId, cancellationToken);

        public Task<MovieMetadata?> GetMovieByImdbIdAsync(string imdbId, CancellationToken cancellationToken) =>
            _fixtures.GetMovieByImdbIdAsync(imdbId, cancellationToken);

        public Task<IReadOnlyList<MovieMetadata>> SearchAsync(string query, int? year, CancellationToken cancellationToken) =>
            timesOut(query)
                ? throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.")
                : _fixtures.SearchAsync(query, year, cancellationToken);
    }
}
