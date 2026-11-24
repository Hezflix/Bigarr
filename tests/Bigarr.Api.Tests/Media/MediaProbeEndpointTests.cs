using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Infrastructure.Media;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Api.Tests.Search;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Tv;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Media;

/// <summary>The probe a film's version or an episode file runs on demand: ffprobe's whole output, nothing stored.</summary>
public sealed class MediaProbeEndpointTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private const string Output = """
        {"streams":[{"index":0,"codec_name":"hevc","codec_type":"video","profile":"Main 10","width":3840,"height":2160,"side_data_list":[{"side_data_type":"DOVI configuration record","dv_profile":8}]},
                    {"index":1,"codec_name":"truehd","codec_type":"audio","channels":8,"tags":{"language":"eng","title":"TrueHD Atmos 7.1"},"disposition":{"default":1,"forced":0}}],
         "format":{"format_name":"matroska,webm","duration":"7200.000000","bit_rate":"60000000"}}
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-probe-").FullName;
    private readonly FakeProbe _probe = new();
    private readonly ProbeApiFactory _factory;

    public MediaProbeEndpointTests() => _factory = new ProbeApiFactory(_probe);

    public void Dispose()
    {
        _factory.Dispose();
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
    public async Task A_film_file_is_probed_now_and_the_whole_output_comes_back()
    {
        var client = await ClientAsync(_factory);
        var (movieId, versionId, path) = await SeedFilmAsync(withFile: true);

        var response = await client.GetAsync($"/api/v1/movies/{movieId}/versions/{versionId}/file/probe");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<MediaProbeResponse>(Json))!;
        Assert.Equal(path, body.Path);
        Assert.Equal(Now.UtcDateTime, body.ProbedAt);
        var streams = body.Probe.GetProperty("streams");
        Assert.Equal(2, streams.GetArrayLength());
        Assert.Equal("DOVI configuration record", streams[0].GetProperty("side_data_list")[0].GetProperty("side_data_type").GetString());
        Assert.Equal("matroska,webm", body.Probe.GetProperty("format").GetProperty("format_name").GetString());
        Assert.Equal([path], _probe.Calls);
    }

    [Fact]
    public async Task A_film_file_gone_from_disk_is_a_404_and_the_probe_is_not_run()
    {
        var client = await ClientAsync(_factory);
        var (movieId, versionId, path) = await SeedFilmAsync(withFile: true);
        File.Delete(path);

        var response = await client.GetAsync($"/api/v1/movies/{movieId}/versions/{versionId}/file/probe");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Json))!;
        Assert.Equal("File not on disk", problem.Title);
        Assert.Contains(path, problem.Detail);
        Assert.Empty(_probe.Calls);
    }

    [Fact]
    public async Task A_version_without_a_file_or_an_unknown_version_is_a_404()
    {
        var client = await ClientAsync(_factory);
        var (movieId, versionId, _) = await SeedFilmAsync(withFile: false);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/movies/{movieId}/versions/{versionId}/file/probe")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/movies/{movieId}/versions/{versionId + 1000}/file/probe")).StatusCode);
    }

    [Fact]
    public async Task Without_ffprobe_or_when_it_fails_the_answer_is_a_409_that_says_why()
    {
        var client = await ClientAsync(_factory);
        var (movieId, versionId, _) = await SeedFilmAsync(withFile: true);

        _probe.Next = new RawProbe(RawProbeOutcome.ToolMissing, Error: "ffprobe was not found.");
        var missing = await client.GetAsync($"/api/v1/movies/{movieId}/versions/{versionId}/file/probe");
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        Assert.Equal("ffprobe not available", (await missing.Content.ReadFromJsonAsync<ProblemDetails>(Json))!.Title);

        _probe.Next = new RawProbe(RawProbeOutcome.Failed, Error: "ffprobe exited with code 1: Invalid data found when processing input");
        var failed = await client.GetAsync($"/api/v1/movies/{movieId}/versions/{versionId}/file/probe");
        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        var problem = (await failed.Content.ReadFromJsonAsync<ProblemDetails>(Json))!;
        Assert.Equal("ffprobe could not read the file", problem.Title);
        Assert.Contains("Invalid data", problem.Detail);
    }

    [Fact]
    public async Task An_episode_file_is_probed_the_same_way_and_a_missing_one_is_a_404()
    {
        var client = await ClientAsync(_factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        var versionId = series.Versions.OrderBy(v => v.Id).First().Id;

        int fileId;
        string path;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var target = await db.EpisodeTargets.Include(t => t.Episode).Include(t => t.Version).ThenInclude(v => v.RootFolder)
                .SingleAsync(t => t.VersionId == versionId && t.Episode.SeasonNumber == 1 && t.Episode.EpisodeNumber == 1);
            var file = new EpisodeFile
            {
                VersionId = versionId,
                RelativePath = "Season 1/Demo Show - S01E01 - Pilot Bluray-1080p.mkv",
                SeasonNumber = 1,
                Size = 16,
                QualityId = ParserQuality.Bluray1080p.Id,
            };
            target.EpisodeFile = file;
            await db.SaveChangesAsync();
            fileId = file.Id;
            path = Path.Combine(target.Version.RootFolder.Path, target.Version.FolderName, file.RelativePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, new byte[16]);

        var response = await client.GetAsync($"/api/v1/series/{series.Id}/episodefiles/{fileId}/probe");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<MediaProbeResponse>(Json))!;
        Assert.Equal(path, body.Path);
        Assert.Equal("hevc", body.Probe.GetProperty("streams")[0].GetProperty("codec_name").GetString());

        File.Delete(path);
        var gone = await client.GetAsync($"/api/v1/series/{series.Id}/episodefiles/{fileId}/probe");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal("File not on disk", (await gone.Content.ReadFromJsonAsync<ProblemDetails>(Json))!.Title);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/series/{series.Id}/episodefiles/{fileId + 1000}/probe")).StatusCode);
    }

    /// <summary>A film with one version; its file (when asked) is a small file on disk.</summary>
    private async Task<(int MovieId, int VersionId, string Path)> SeedFilmAsync(bool withFile)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var profile = await db.QualityProfiles.FirstAsync(p => p.Kind == ProfileKind.Movies);
        var root = await db.RootFolders.FirstOrDefaultAsync(r => r.Path == _root) ?? new RootFolder { Path = _root };
        var title = $"Probe Film {Guid.NewGuid():N}";
        var movie = new Movie
        {
            TmdbId = Random.Shared.Next(900_000, 999_999),
            Title = title,
            SortTitle = SortTitle.Build(title),
            Year = 2026,
            TmdbStatus = MovieStatus.Released,
            DefaultRootFolder = root,
        };
        var version = new MovieVersion
        {
            Movie = movie,
            Name = "2160p",
            IsDefault = true,
            Monitored = true,
            QualityProfile = profile,
            RootFolder = root,
            FolderName = $"{title} (2026)",
            MinimumAvailability = MinimumAvailability.Released,
        };
        var path = Path.Combine(_root, version.FolderName, $"{title}.mkv");
        if (withFile)
        {
            version.File = new MediaFile { RelativePath = $"{title}.mkv", Size = 16, QualityId = ParserQuality.Bluray2160p.Id };
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, new byte[16]);
        }

        movie.Versions.Add(version);
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return (movie.Id, version.Id, path);
    }

    private sealed class ProbeApiFactory(FakeProbe probe) : BigarrApiFactory(null, null, deleteOnDispose: true, new FixedTimeProvider(Now))
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMediaInfoReader>();
                services.AddSingleton<IMediaInfoReader>(probe);
            });
        }
    }

    /// <summary>Answers <see cref="Output"/>, or <see cref="Next"/> when a test sets it, and remembers the paths it was asked.</summary>
    private sealed class FakeProbe : IMediaInfoReader
    {
        public List<string> Calls { get; } = [];

        public RawProbe? Next { get; set; }

        public bool IsAvailable => true;

        public Task<MediaInfo?> ReadAsync(string path, string? releaseTitle, CancellationToken ct) => Task.FromResult<MediaInfo?>(null);

        public Task<RawProbe> ProbeAsync(string path, CancellationToken ct)
        {
            Calls.Add(path);
            return Task.FromResult(Next ?? RawProbe.Ok(Output));
        }
    }
}
