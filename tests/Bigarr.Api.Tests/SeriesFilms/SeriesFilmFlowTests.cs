using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Features.SeriesFilms;
using Bigarr.Api.Features.Server.Health;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Compat;
using Bigarr.Domain.Library;
using Bigarr.Domain.Tv;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.SeriesFilms;

/// <summary>
/// The S8 acceptance on the mock indexer and client: an anime's film special is added as a film, grabbed and imported once
/// by the film engine, and appears as the special of the series through a hardlink that follows upgrades and deletion.
/// </summary>
public sealed class SeriesFilmFlowTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>, IDisposable
{
    private const int DemonSlayer = 348545;
    private const int Firefly = 78874;
    private const int MugenTrain = 635302;

    private readonly string _films = Directory.CreateTempSubdirectory("bigarr-films-").FullName;
    private readonly string _shows = Directory.CreateTempSubdirectory("bigarr-shows-").FullName;
    private readonly string _bin = Directory.CreateTempSubdirectory("bigarr-films-bin-").FullName;

    public void Dispose()
    {
        foreach (var dir in new[] { _films, _shows, _bin })
        {
            MakeWritable(dir);
            TryDelete(dir);
        }
    }

    [Fact]
    public async Task A_film_special_is_grabbed_by_the_film_engine_imported_once_and_hardlinked_into_the_series()
    {
        var client = await ClientAsync(factory);
        await ConfigureAsync(client, _bin);
        var (filmRoot, showRoot) = await RootsAsync(client, _films, _shows);
        var series = await AddSeriesAsync(client, DemonSlayer, showRoot.Id);
        var version = series.Versions.Single();
        var showFolder = Path.Combine(version.RootFolderPath, version.FolderName);
        var indexer = await AddMockIndexerAsync(client, "Mock films idx");
        var downloadClient = await AddMockClientAsync(client, "Mock films qbt");
        try
        {
            // Detection ran at add: flagged films and the likely compilation are suggested, the recap specials are not.
            var films = await FilmsAsync(client, series.Id);
            Assert.False(films.AutoAdd);
            Assert.Equal(["S00E01", "S00E02", "S00E15", "S00E16"], films.Films.Select(f => f.Label));
            var mugen = films.Films.Single(f => f.Label == "S00E02");
            Assert.Equal(FilmConfidence.Flagged, mugen.Confidence);
            Assert.Equal(MugenTrain, mugen.TmdbId);
            Assert.Equal("after season 1", mugen.PlacementLabel);
            Assert.Null(mugen.Link);
            var bond = films.Films.Single(f => f.Label == "S00E01");
            Assert.Equal(FilmConfidence.Likely, bond.Confidence);
            Assert.Equal(820232, bond.TmdbId);
            Assert.Equal("before season 1", bond.PlacementLabel);

            // Add as film: a Movie on the default film profile (1080p) in the chosen film root.
            var added = await client.PostAsJsonAsync($"/api/v1/series/{series.Id}/films", new AddSeriesFilmRequest(mugen.EpisodeId, RootFolderId: filmRoot.Id, SearchOnAdd: false), Json);
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
            var link = (await added.Content.ReadFromJsonAsync<SeriesFilmsResponse>(Json))!.Films.Single(f => f.Label == "S00E02").Link!;
            var movie = await GetMovieAsync(client, link.MovieId);
            Assert.Equal(MugenTrain, movie.TmdbId);
            var filmVersion = movie.Versions.Single();
            Assert.Equal(QualitySeeder.HdProfileName, filmVersion.QualityProfile);
            Assert.Equal(filmRoot.Id, filmVersion.RootFolderId);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/series/{series.Id}/films", new AddSeriesFilmRequest(mugen.EpisodeId), Json)).StatusCode);

            // The film page says where it sits; the episode list says who owns it.
            var seriesLinks = (await client.GetFromJsonAsync<List<MovieSeriesLinkResponse>>($"/api/v1/movies/{movie.Id}/series-links", Json))!;
            var line = Assert.Single(seriesLinks);
            Assert.Equal(("S00E02", "after season 1", series.Id), (line.Label, line.PlacementLabel, line.SeriesId));
            var episode = (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{series.Id}/episodes", Json))!.Single(e => e.Id == mugen.EpisodeId);
            Assert.Equal(movie.Id, episode.OwnedByFilm!.MovieId);
            Assert.True(episode.Versions.Single().Monitored, "linking monitors the special where the version is monitored");

            // The series engine leaves it alone: no slot for RSS or automatic search, no Wanted row, no episode search.
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var targets = (await scope.ServiceProvider.GetRequiredService<SeriesTargetsFactory>().LoadAsync(series.Id, CancellationToken.None))!;
                Assert.Null(targets.Slot(version.Id, mugen.EpisodeId));
                Assert.Contains("Mugen Train", targets.FilmOwned[mugen.EpisodeId], StringComparison.Ordinal);
                Assert.NotNull(targets.Slot(version.Id, films.Films.Single(f => f.Label == "S00E15").EpisodeId));
            }

            var wanted = (await client.GetFromJsonAsync<Bigarr.Api.Features.Search.SeriesWantedPageResponse>($"/api/v1/series/wanted?filter=missing&seriesId={series.Id}&pageSize=200", Json))!;
            Assert.DoesNotContain(wanted.Items, i => i.EpisodeId == mugen.EpisodeId);
            Assert.Contains(wanted.Items, i => i.SeasonNumber == 0 && i.EpisodeNumber == 15 || i.SeasonNumber > 0);
            Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/v1/series/{series.Id}/search?episodeId={mugen.EpisodeId}")).StatusCode);

            // The film engine searches, grabs and imports it like any film.
            var search = await SearchAsync(client, movie.Id);
            var webdl = search.Releases.First(r => r.Title.Contains("1080p.WEB-DL", StringComparison.Ordinal) && r.Rejections.Count == 0);
            await GrabAndCompleteAsync(client, webdl, filmVersion.Id);
            var imported = await WaitForFileAsync(client, movie.Id, filmVersion.Id);
            var filmPath = Path.Combine(filmRoot.Path, filmVersion.FolderName, imported.File!.RelativePath);
            Assert.True(File.Exists(filmPath));

            // …and the special appears in the series' Specials folder as a hardlink: one copy on disk.
            var special = await WaitForLinkAsync(client, series.Id, f => f.LinkedFromPath == filmPath);
            Assert.Equal("Specials", Path.GetDirectoryName(special.RelativePath));
            Assert.Contains("S00E02", special.RelativePath, StringComparison.Ordinal);
            Assert.Equal("WEBDL-1080p", special.Quality);
            Assert.Equal(Inode(filmPath), Inode(Path.Combine(showFolder, special.RelativePath)));
            Assert.Single(await HistoryAsync(client, movie.Id, Bigarr.Domain.History.HistoryEventType.Imported));

            // A manual delete of the special is refused: the film owns it.
            Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/v1/series/{series.Id}/episodefiles/{special.Id}")).StatusCode);

            // Radarr compat sees a normal film with its file.
            var (compat, _) = await CompatTestHelpers.KeyClientAsync(factory, client, "films-compat", "default");
            var radarr = JsonDocument.Parse(await compat.GetStringAsync("/api/v3/movie")).RootElement.EnumerateArray().Single(m => m.GetProperty("tmdbId").GetInt32() == MugenTrain);
            Assert.True(radarr.GetProperty("hasFile").GetBoolean());

            // An upgrade by the film engine re-creates the special on the new file; the old name is gone.
            var bluray = search.Releases.First(r => r.Title.EndsWith("1080p.BluRay.x264-SPARKS", StringComparison.Ordinal) && !r.Title.Contains("REPACK", StringComparison.Ordinal));
            await GrabAndCompleteAsync(client, bluray, filmVersion.Id);
            var upgraded = await WaitForFileAsync(client, movie.Id, filmVersion.Id, v => v.File!.Quality == "Bluray-1080p");
            var newPath = Path.Combine(filmRoot.Path, filmVersion.FolderName, upgraded.File!.RelativePath);
            var relinked = await WaitForLinkAsync(client, series.Id, f => f.LinkedFromPath == newPath);
            Assert.Equal("Bluray-1080p", relinked.Quality);
            Assert.Equal(Inode(newPath), Inode(Path.Combine(showFolder, relinked.RelativePath)));
            Assert.False(File.Exists(Path.Combine(showFolder, special.RelativePath)), "the special of the replaced file is gone");
            Assert.Single(Directory.GetFiles(Path.Combine(showFolder, "Specials")));

            // Deleting the film's file (to the recycle bin) takes the special with it.
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/v1/movies/{movie.Id}/versions/{filmVersion.Id}/file")).StatusCode);
            await WaitUntilAsync(async () => (await FilesAsync(client, series.Id)).Files.Any(f => f.EpisodeIds.Contains(mugen.EpisodeId)) ? null : new object(), "the special to go with the film's file");
            Assert.Empty(Directory.GetFiles(Path.Combine(showFolder, "Specials")));
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
        }
    }

    [Fact]
    public async Task Auto_add_links_new_flagged_films_never_copies_and_unlinking_keeps_the_special_as_a_plain_file()
    {
        var client = await ClientAsync(factory);
        await ConfigureAsync(client, _bin);
        var (filmRoot, showRoot) = await RootsAsync(client, _films, _shows);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/v1/settings/series-films", new SeriesFilmSettingsRequest(true), Json)).StatusCode);
        var indexer = await AddMockIndexerAsync(client, "Mock firefly idx");
        var downloadClient = await AddMockClientAsync(client, "Mock firefly qbt");
        try
        {
            var series = await AddSeriesAsync(client, Firefly, showRoot.Id);
            var version = series.Versions.Single();
            var showFolder = Path.Combine(version.RootFolderPath, version.FolderName);

            // Serenity is flagged by TVDB: added on its own, in the first film root.
            var films = await FilmsAsync(client, series.Id);
            Assert.True(films.AutoAdd);
            var serenity = Assert.Single(films.Films);
            Assert.Equal("Serenity", serenity.Title);
            Assert.True(serenity.Link!.AutoAdded);
            var movie = await GetMovieAsync(client, serenity.Link.MovieId);
            var filmVersion = movie.Versions.Single();

            // A Specials folder the app cannot write into: the hardlink fails and nothing is copied; health says why.
            var specials = Path.Combine(showFolder, "Specials");
            Directory.CreateDirectory(specials);
            var readOnly = !OperatingSystem.IsWindows() && Environment.UserName != "root";
            if (!OperatingSystem.IsWindows() && readOnly)
            {
                File.SetUnixFileMode(specials, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }

            // Auto-add searches on add: the film engine grabs on its own; the mock client finishes it.
            var grabbed = await WaitForQueueRowAsync(client, r => r.MovieId == movie.Id, "the film's automatic grab");
            await CompleteAsync(client, grabbed.DownloadId!);
            await RefreshAsync(client);
            var imported = await WaitForFileAsync(client, movie.Id, filmVersion.Id);
            var filmPath = Path.Combine(filmRoot.Path, filmVersion.FolderName, imported.File!.RelativePath);
            if (readOnly)
            {
                var failed = await WaitUntilAsync(async () => (await FilmsAsync(client, series.Id)).Films.Single().Link!.LinkError, "the link error");
                Assert.Contains("not copied", failed, StringComparison.Ordinal);
                Assert.Empty(Directory.GetFiles(specials));
                var check = await client.PostAsync("/api/v1/health/check", null);
                var health = (await check.Content.ReadFromJsonAsync<HealthResponse>(Json))!.Issues;
                Assert.Contains(health, h => h.Source == "seriesFilmLinks" && h.Message.Contains("S00E01", StringComparison.Ordinal));
                MakeWritable(specials);
                Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/series-films/links/reconcile", null)).StatusCode);
            }

            var special = await WaitForLinkAsync(client, series.Id, f => f.LinkedFromPath == filmPath);
            Assert.Equal(Inode(filmPath), Inode(Path.Combine(showFolder, special.RelativePath)));
            Assert.Null((await FilmsAsync(client, series.Id)).Films.Single().Link!.LinkError);

            // Unlink: the film stays a film, the special keeps its file as an ordinary one (still the same inode) and is
            // the series engine's again.
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/series/{series.Id}/films/{serenity.EpisodeId}")).StatusCode);
            var plain = await WaitUntilAsync(async () => (await FilesAsync(client, series.Id)).Files.FirstOrDefault(f => f.Id == special.Id && f.LinkedFromPath is null), "the special to become a plain file");
            Assert.Equal(Inode(filmPath), Inode(Path.Combine(showFolder, plain.RelativePath)));
            Assert.NotNull((await GetMovieAsync(client, movie.Id)).Versions.Single().File);
            await using var scope = factory.Services.CreateAsyncScope();
            var targets = (await scope.ServiceProvider.GetRequiredService<SeriesTargetsFactory>().LoadAsync(series.Id, CancellationToken.None))!;
            Assert.NotNull(targets.Slot(version.Id, serenity.EpisodeId));

            // Unlinked by hand: a later refresh does not add it again.
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/series/{series.Id}/refresh", null)).StatusCode);
            Assert.Null((await FilmsAsync(client, series.Id)).Films.Single().Link);
        }
        finally
        {
            await client.PutAsJsonAsync("/api/v1/settings/series-films", new SeriesFilmSettingsRequest(false), Json);
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
        }
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    internal static async Task<(RootFolderResponse Films, RootFolderResponse Shows)> RootsAsync(HttpClient client, string films, string shows)
    {
        var roots = (await client.GetFromJsonAsync<List<RootFolderResponse>>("/api/v1/rootfolders", Json))!;
        var filmRoot = roots.FirstOrDefault(r => r.Kind == MediaKind.Movies) ?? await AddRootAsync(client, films);
        var showRoot = roots.FirstOrDefault(r => r.Kind == MediaKind.Series);
        if (showRoot is null)
        {
            var created = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(shows, MediaKind.Series));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            showRoot = (await created.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!;
        }

        return (filmRoot, showRoot);
    }

    internal static async Task<SeriesResponse> AddSeriesAsync(HttpClient client, int tvdbId, int rootId)
    {
        var profiles = (await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!;
        var hd = profiles.Single(p => p.Name == QualitySeeder.SeriesHdProfileName);
        var response = await client.PostAsJsonAsync("/api/v1/series", new AddSeriesRequest(hd.Id, rootId, TvdbId: tvdbId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SeriesResponse>(Json))!;
    }

    internal static Task<SeriesFilmsResponse> FilmsAsync(HttpClient client, int seriesId) =>
        client.GetFromJsonAsync<SeriesFilmsResponse>($"/api/v1/series/{seriesId}/films", Json)!;

    private static Task<SeriesFilesResponse> FilesAsync(HttpClient client, int seriesId) =>
        client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{seriesId}/files", Json)!;

    private static Task<EpisodeFileResponse> WaitForLinkAsync(HttpClient client, int seriesId, Func<EpisodeFileResponse, bool> match) =>
        WaitUntilAsync(async () => (await FilesAsync(client, seriesId)).Files.FirstOrDefault(f => f.LinkedFromPath is not null && match(f)), "the film's hardlink in the series");

    private static async Task ConfigureAsync(HttpClient client, string bin)
    {
        var response = await client.PutAsJsonAsync("/api/v1/mediamanagement", new MediaManagementRequest(true, MediaManagementConfig.DefaultFileNameTemplate, MediaManagementConfig.DefaultFolderNameTemplate, TransferMode.HardlinkOrCopy, bin, 7), Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static void MakeWritable(string path)
    {
        if (!OperatingSystem.IsWindows() && Directory.Exists(path))
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
            {
                File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    /// <summary>The file's inode number, from <c>stat</c> (GNU on Linux, BSD on macOS): same inode, same bytes.</summary>
    internal static string Inode(string path)
    {
        var info = new ProcessStartInfo("stat", OperatingSystem.IsMacOS() ? ["-f", "%i", path] : ["-c", "%i", path]) { RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"stat failed for {path}");
        return output;
    }
}
