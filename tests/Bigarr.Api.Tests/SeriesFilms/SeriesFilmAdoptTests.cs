using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Features.SeriesFilms;
using Bigarr.Api.Features.Server.Health;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Shadow;
using Bigarr.Domain.Library;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.SeriesFilms;

/// <summary>
/// A special that already has a file when it is linked (S8, decision 6): the film adopts it (moved by a rename, never
/// copied) unless the film has its own, which then wins. The review page's dry run says so beforehand, row by row.
/// </summary>
public sealed class SeriesFilmAdoptTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>, IDisposable
{
    private const int DemonSlayer = 348545;
    private const int Firefly = 78874;
    private const int InfinityCastle = 1311031;

    private readonly string _films = Directory.CreateTempSubdirectory("bigarr-films-").FullName;
    private readonly string _shows = Directory.CreateTempSubdirectory("bigarr-shows-").FullName;
    private readonly string _bin = Directory.CreateTempSubdirectory("bigarr-films-bin-").FullName;
    private readonly string _downloads = Directory.CreateTempSubdirectory("bigarr-films-dl-").FullName;

    public void Dispose()
    {
        foreach (var dir in new[] { _films, _shows, _bin, _downloads })
        {
            MakeWritable(dir);
            TryDelete(dir);
        }
    }

    [Fact]
    public async Task A_special_with_its_own_file_gives_it_to_the_film_moved_not_copied_and_the_film_is_not_searched()
    {
        var client = await ClientAsync(factory);
        await ConfigureAsync(client, _bin);
        var (filmRoot, showRoot) = await SeriesFilmFlowTests.RootsAsync(client, _films, _shows);
        var indexer = await AddMockIndexerAsync(client, "Mock adopt idx");
        var downloadClient = await AddMockClientAsync(client, "Mock adopt qbt");
        try
        {
            var series = await SeriesAsync(client, Firefly, showRoot.Id);
            var version = series.Versions.Single();
            var serenity = (await SeriesFilmFlowTests.FilmsAsync(client, series.Id)).Films.Single(f => f.Title == "Serenity");
            var specialPath = await PlantSpecialFileAsync(series.Id, version.Id, serenity.EpisodeId, "Specials/Firefly - S00E01 - Serenity Bluray-1080p.mkv", ParserQuality.Bluray1080p.Id);
            var inode = SeriesFilmFlowTests.Inode(specialPath);

            // The dry run: the film is added, takes the special's file, and the special keeps it as a hardlink.
            var row = (await ReviewAsync(client)).Rows.Single(r => r.EpisodeId == serenity.EpisodeId);
            Assert.Equal((SeriesFilmState.Suggested, true, false, false), (row.State, row.SpecialHasFile, row.FilmHasFile, row.FilmInLibrary));
            Assert.Equal(("Serenity", 2005), (row.FilmTitle, row.FilmYear));
            Assert.Equal([FilmFileStepKind.AddFilm, FilmFileStepKind.AdoptSpecialFile, FilmFileStepKind.HardlinkIntoSpecials], row.Plan.Select(s => s.Kind));
            var adopt = row.Plan[1];
            Assert.Equal(specialPath, adopt.From);
            Assert.StartsWith(filmRoot.Path, adopt.To, StringComparison.Ordinal);
            Assert.True(File.Exists(specialPath), "a dry run touches nothing");

            // Linked from the review page (bulk): an unknown special is reported, the others go on.
            var linked = await client.PostAsJsonAsync("/api/v1/series-films/link", new LinkSeriesFilmsRequest([serenity.EpisodeId, 987654]), Json);
            Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
            var results = (await linked.Content.ReadFromJsonAsync<List<LinkSeriesFilmResult>>(Json))!;
            Assert.True(results.Single(r => r.EpisodeId == serenity.EpisodeId).Linked);
            Assert.False(results.Single(r => r.EpisodeId == 987654).Linked);

            // The film's file is the special's bytes, under the film naming, with the special's quality: one inode.
            var movieId = (await ReviewAsync(client)).Rows.Single(r => r.EpisodeId == serenity.EpisodeId).MovieId!.Value;
            var filmVersion = (await GetMovieAsync(client, movieId)).Versions.Single();
            var film = await WaitForFileAsync(client, movieId, filmVersion.Id);
            Assert.Equal("Bluray-1080p", film.File!.Quality);
            var filmPath = Path.Combine(filmRoot.Path, filmVersion.FolderName, film.File.RelativePath);
            Assert.Equal(inode, SeriesFilmFlowTests.Inode(filmPath));
            Assert.DoesNotContain(".bigarr-adopt-", film.File.RelativePath, StringComparison.Ordinal);
            var imported = Assert.Single(await HistoryAsync(client, movieId, Bigarr.Domain.History.HistoryEventType.Imported));
            Assert.Equal(specialPath, imported.Data["adoptedFrom"]);

            // …and the special is its hardlink, recorded as the film's link.
            var special = await WaitUntilAsync(async () => (await FilesAsync(client, series.Id)).Files.FirstOrDefault(f => f.LinkedFromPath == filmPath), "the special's hardlink");
            var showFolder = Path.Combine(version.RootFolderPath, version.FolderName);
            Assert.Equal(inode, SeriesFilmFlowTests.Inode(Path.Combine(showFolder, special.RelativePath)));
            Assert.Single(Directory.GetFiles(Path.Combine(showFolder, "Specials")));
            Assert.Empty(Directory.GetFiles(_bin, "*", SearchOption.AllDirectories));

            // Not searched: the film starts downloaded; no grab was made for it.
            Assert.Empty(await HistoryAsync(client, movieId, Bigarr.Domain.History.HistoryEventType.Grabbed));
            Assert.DoesNotContain((await RefreshAsync(client)).Items, i => i.MovieId == movieId);

            var after = (await ReviewAsync(client)).Rows.Single(r => r.EpisodeId == serenity.EpisodeId);
            Assert.Equal(SeriesFilmState.Linked, after.State);
            Assert.Equal([FilmFileStepKind.AlreadyHardlinked], after.Plan.Select(s => s.Kind));
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
        }
    }

    [Fact]
    public async Task An_adoption_the_filesystem_refuses_copies_nothing_keeps_the_special_and_shows_in_health()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            return; // permissions are what refuse the move here
        }

        var client = await ClientAsync(factory);
        await ConfigureAsync(client, _bin);
        var (filmRoot, showRoot) = await SeriesFilmFlowTests.RootsAsync(client, _films, _shows);
        var series = await SeriesAsync(client, DemonSlayer, showRoot.Id);
        var version = series.Versions.Single();
        var mugen = (await SeriesFilmFlowTests.FilmsAsync(client, series.Id)).Films.Single(f => f.Label == "S00E02");
        var specialPath = await PlantSpecialFileAsync(series.Id, version.Id, mugen.EpisodeId, "Specials/Demon Slayer - S00E02 - Mugen Train WEBDL-1080p.mkv", ParserQuality.WEBDL1080p.Id);

        File.SetUnixFileMode(filmRoot.Path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/series/{series.Id}/films", new AddSeriesFilmRequest(mugen.EpisodeId), Json)).StatusCode);
            var error = await WaitUntilAsync(async () => (await SeriesFilmFlowTests.FilmsAsync(client, series.Id)).Films.Single(f => f.EpisodeId == mugen.EpisodeId).Link!.LinkError, "the refused adoption");
            Assert.Contains("not copied", error, StringComparison.Ordinal);

            // The special keeps its own file and record; the film has nothing, not even a stray copy.
            Assert.True(File.Exists(specialPath));
            Assert.Contains((await FilesAsync(client, series.Id)).Files, f => f.EpisodeIds.Contains(mugen.EpisodeId) && f.LinkedFromPath is null);
            var row = (await ReviewAsync(client)).Rows.Single(r => r.EpisodeId == mugen.EpisodeId);
            var filmVersion = (await GetMovieAsync(client, row.MovieId!.Value)).Versions.Single();
            Assert.Null(filmVersion.File);
            Assert.False(Directory.Exists(Path.Combine(filmRoot.Path, filmVersion.FolderName)));
            var check = await client.PostAsync("/api/v1/health/check", null);
            var health = (await check.Content.ReadFromJsonAsync<HealthResponse>(Json))!.Issues;
            Assert.Contains(health, h => h.Source == "seriesFilmLinks" && h.Message.Contains("S00E02", StringComparison.Ordinal));
            row = (await ReviewAsync(client)).Rows.Single(r => r.EpisodeId == mugen.EpisodeId);
            Assert.Equal(FilmFileStepKind.LinkError, row.Plan[0].Kind);
            Assert.Contains(row.Plan, s => s.Kind == FilmFileStepKind.AdoptSpecialFile);
        }
        finally
        {
            MakeWritable(filmRoot.Path);
        }

        // Fixed: the next pass adopts it.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/series-films/links/reconcile", null)).StatusCode);
        var fixedRow = (await ReviewAsync(client)).Rows.Single(r => r.EpisodeId == mugen.EpisodeId);
        Assert.Null(fixedRow.LinkError);
        Assert.True(fixedRow.FilmHasFile);
    }

    [Fact]
    public async Task When_both_have_a_file_the_films_wins_and_the_specials_goes_to_the_recycle_bin()
    {
        var client = await ClientAsync(factory);
        await ConfigureAsync(client, _bin);
        var (filmRoot, showRoot) = await SeriesFilmFlowTests.RootsAsync(client, _films, _shows);
        var series = await SeriesAsync(client, DemonSlayer, showRoot.Id);
        var version = series.Versions.Single();
        var castle = (await SeriesFilmFlowTests.FilmsAsync(client, series.Id)).Films.Single(f => f.Label == "S00E16");

        // The film is in the library with its file (imported by hand); the special has a file of its own.
        var (hd, _) = await ProfilesAsync(client);
        var movie = await AddMovieAsync(client, InfinityCastle, hd, filmRoot.Id);
        var filmVersion = movie.Versions.Single();
        var download = Path.Combine(_downloads, "Demon.Slayer.Infinity.Castle.2025.1080p.WEB-DL-GRP.mkv");
        await File.WriteAllTextAsync(download, "the film's own bytes");
        var manual = await client.PostAsJsonAsync("/api/v1/manualimport", new ManualImportRequest([new ManualImportItemRequest(download, filmVersion.Id, ParserQuality.WEBDL1080p.Id)], TransferMode.Copy), Json);
        Assert.Equal(HttpStatusCode.OK, manual.StatusCode);
        var film = await WaitForFileAsync(client, movie.Id, filmVersion.Id);
        var filmPath = Path.Combine(filmRoot.Path, filmVersion.FolderName, film.File!.RelativePath);
        var specialPath = await PlantSpecialFileAsync(series.Id, version.Id, castle.EpisodeId, "Specials/Demon Slayer - S00E16 - Infinity Castle HDTV-720p.mkv", ParserQuality.HDTV720p.Id);

        var row = (await ReviewAsync(client)).Rows.Single(r => r.EpisodeId == castle.EpisodeId);
        Assert.Equal((true, true, true, movie.Id), (row.FilmInLibrary, row.FilmHasFile, row.SpecialHasFile, row.MovieId));
        Assert.Equal([FilmFileStepKind.ReuseFilm, FilmFileStepKind.FilmFileWins, FilmFileStepKind.HardlinkIntoSpecials], row.Plan.Select(s => s.Kind));
        Assert.Equal(specialPath, row.Plan[1].From);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/series-films/link", new LinkSeriesFilmsRequest([castle.EpisodeId]), Json)).StatusCode);
        var special = await WaitUntilAsync(async () => (await FilesAsync(client, series.Id)).Files.FirstOrDefault(f => f.LinkedFromPath == filmPath), "the film's hardlink");
        var showFolder = Path.Combine(version.RootFolderPath, version.FolderName);
        Assert.Equal(SeriesFilmFlowTests.Inode(filmPath), SeriesFilmFlowTests.Inode(Path.Combine(showFolder, special.RelativePath)));
        Assert.False(File.Exists(specialPath));
        var binned = Assert.Single(Directory.GetFiles(_bin, "*S00E16*", SearchOption.AllDirectories));
        Assert.Equal("the special's own bytes", await File.ReadAllTextAsync(binned));
        Assert.Equal("WEBDL-1080p", (await GetMovieAsync(client, movie.Id)).Versions.Single().File!.Quality);
    }

    [Fact]
    public async Task The_review_lists_every_candidate_with_what_linking_would_do_and_dismissing_takes_it_out_of_the_suggestions()
    {
        var client = await ClientAsync(factory);
        await ConfigureAsync(client, _bin);
        var (filmRoot, showRoot) = await SeriesFilmFlowTests.RootsAsync(client, _films, _shows);
        var series = await SeriesAsync(client, DemonSlayer, showRoot.Id);
        var films = (await SeriesFilmFlowTests.FilmsAsync(client, series.Id)).Films;
        var bond = films.Single(f => f.Label == "S00E01");

        var review = await ReviewAsync(client);
        Assert.False(review.ShadowEnabled);
        Assert.False(review.AutoAdd);
        var row = review.Rows.Single(r => r.EpisodeId == bond.EpisodeId);
        Assert.Equal(("Demon Slayer: Kimetsu no Yaiba", "S00E01", FilmConfidence.Likely, "before season 1"), (row.SeriesTitle, row.Label, row.Confidence, row.PlacementLabel));
        Assert.Equal(820232, row.TmdbId);
        Assert.NotNull(row.FilmTitle);

        // Nothing on either side, film not in the library: added, then searched by the film engine.
        if (row.State == SeriesFilmState.Suggested)
        {
            Assert.Equal([FilmFileStepKind.AddFilm, FilmFileStepKind.SearchFilm], row.Plan.Select(s => s.Kind));
            Assert.StartsWith(filmRoot.Path, row.Plan[0].To, StringComparison.Ordinal);
            Assert.Contains("Sibling", row.Plan[0].To, StringComparison.Ordinal);
        }

        // The film already in the library without a file: reused, it keeps waiting for its own search.
        var (hd, _) = await ProfilesAsync(client);
        var e15Tmdb = films.Single(f => f.Label == "S00E15").TmdbId!.Value;
        var existing = (await client.GetFromJsonAsync<List<Bigarr.Api.Features.Movies.MovieSummary>>("/api/v1/movies", Json))!.FirstOrDefault(m => m.TmdbId == e15Tmdb);
        var movieId = existing?.Id ?? (await AddMovieAsync(client, e15Tmdb, hd, filmRoot.Id)).Id;
        var reused = (await ReviewAsync(client)).Rows.Single(r => r.Label == "S00E15" && r.SeriesId == series.Id);
        Assert.Equal((true, movieId), (reused.FilmInLibrary, reused.MovieId));
        Assert.Equal([FilmFileStepKind.ReuseFilm, FilmFileStepKind.NothingOnDisk], reused.Plan.Select(s => s.Kind));

        // Once it has its file and the special has none: the film's file is hardlinked into the show.
        var filmVersion = (await GetMovieAsync(client, movieId)).Versions.Single(v => v.IsDefault);
        var download = Path.Combine(_downloads, "Demon.Slayer.Hashira.Training.2024.1080p.WEB-DL-GRP.mkv");
        await File.WriteAllTextAsync(download, "film bytes");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/manualimport", new ManualImportRequest([new ManualImportItemRequest(download, filmVersion.Id, ParserQuality.WEBDL1080p.Id)], TransferMode.Copy), Json)).StatusCode);
        await WaitForFileAsync(client, movieId, filmVersion.Id);
        var withFile = (await ReviewAsync(client)).Rows.Single(r => r.Label == "S00E15" && r.SeriesId == series.Id);
        Assert.Equal((true, false), (withFile.FilmHasFile, withFile.SpecialHasFile));
        Assert.Equal([FilmFileStepKind.ReuseFilm, FilmFileStepKind.HardlinkIntoSpecials], withFile.Plan.Select(s => s.Kind));
        Assert.Contains("S00E15", withFile.Plan[1].To, StringComparison.Ordinal);

        // Dismissed: out of the suggestions (badge, series page), still on the review page; restoring brings it back.
        var before = (await client.GetFromJsonAsync<SeriesFilmSummaryResponse>("/api/v1/series-films/summary", Json))!;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/series/{series.Id}/films/{bond.EpisodeId}/dismiss", null)).StatusCode);
        var after = (await client.GetFromJsonAsync<SeriesFilmSummaryResponse>("/api/v1/series-films/summary", Json))!;
        Assert.Equal((before.Suggested - 1, before.Dismissed + 1), (after.Suggested, after.Dismissed));
        Assert.Equal(SeriesFilmState.Dismissed, (await ReviewAsync(client)).Rows.Single(r => r.EpisodeId == bond.EpisodeId).State);
        Assert.DoesNotContain((await SeriesFilmFlowTests.FilmsAsync(client, series.Id)).Films, f => f.EpisodeId == bond.EpisodeId);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/series/{series.Id}/films/{bond.EpisodeId}/dismiss")).StatusCode);
        Assert.Equal(SeriesFilmState.Suggested, (await ReviewAsync(client)).Rows.Single(r => r.EpisodeId == bond.EpisodeId).State);
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    internal static Task<SeriesFilmReviewResponse> ReviewAsync(HttpClient client) =>
        client.GetFromJsonAsync<SeriesFilmReviewResponse>("/api/v1/series-films/review", Json)!;

    internal static Task<SeriesFilesResponse> FilesAsync(HttpClient client, int seriesId) =>
        client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{seriesId}/files", Json)!;

    /// <summary>The show already in this factory's library, or added now.</summary>
    internal static async Task<SeriesResponse> SeriesAsync(HttpClient client, int tvdbId, int rootId)
    {
        var existing = (await client.GetFromJsonAsync<List<SeriesSummary>>("/api/v1/series", Json))!.FirstOrDefault(s => s.TvdbId == tvdbId);
        return existing is null
            ? await SeriesFilmFlowTests.AddSeriesAsync(client, tvdbId, rootId)
            : (await client.GetFromJsonAsync<SeriesResponse>($"/api/v1/series/{existing.Id}", Json))!;
    }

    /// <summary>A file the special has of its own, on disk and recorded, as a series import would leave it.</summary>
    private Task<string> PlantSpecialFileAsync(int seriesId, int versionId, int episodeId, string relativePath, int qualityId) =>
        PlantSpecialFileAsync(factory.Services, seriesId, versionId, episodeId, relativePath, qualityId);

    internal static async Task<string> PlantSpecialFileAsync(IServiceProvider services, int seriesId, int versionId, int episodeId, string relativePath, int qualityId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var version = await db.SeriesVersions.Include(v => v.RootFolder).SingleAsync(v => v.Id == versionId && v.SeriesId == seriesId);
        var path = Path.Combine(version.RootFolder.Path, version.FolderName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "the special's own bytes");
        var target = await db.EpisodeTargets.SingleAsync(t => t.VersionId == versionId && t.EpisodeId == episodeId);
        var file = new EpisodeFile
        {
            VersionId = versionId,
            RelativePath = relativePath,
            SeasonNumber = 0,
            Size = new FileInfo(path).Length,
            DateAdded = DateTime.UtcNow,
            QualityId = qualityId,
            ReleaseGroup = "GRP",
            Languages = ["Japanese"],
        };
        file.Episodes.Add(target);
        db.EpisodeFiles.Add(file);
        await db.SaveChangesAsync();
        return path;
    }

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
        }
    }
}

/// <summary>Shadow mode: linking a special records the link and the film, and touches no file on either side.</summary>
public sealed class SeriesFilmShadowFilesTests(ShadowApiFactory factory) : IClassFixture<ShadowApiFactory>, IDisposable
{
    private const int Firefly = 78874;

    private readonly string _films = Directory.CreateTempSubdirectory("bigarr-films-").FullName;
    private readonly string _shows = Directory.CreateTempSubdirectory("bigarr-shows-").FullName;

    public void Dispose()
    {
        TryDelete(_films);
        TryDelete(_shows);
    }

    [Fact]
    public async Task Shadow_mode_links_without_touching_a_file()
    {
        var client = await ClientAsync(factory);
        var (filmRoot, showRoot) = await SeriesFilmFlowTests.RootsAsync(client, _films, _shows);
        var series = await SeriesFilmAdoptTests.SeriesAsync(client, Firefly, showRoot.Id);
        var version = series.Versions.Single();
        var serenity = (await SeriesFilmFlowTests.FilmsAsync(client, series.Id)).Films.Single(f => f.Title == "Serenity");
        var specialPath = await SeriesFilmAdoptTests.PlantSpecialFileAsync(factory.Services, series.Id, version.Id, serenity.EpisodeId, "Specials/Firefly - S00E01 - Serenity Bluray-1080p.mkv", ParserQuality.Bluray1080p.Id);

        var review = await SeriesFilmAdoptTests.ReviewAsync(client);
        Assert.True(review.ShadowEnabled);
        Assert.Equal([FilmFileStepKind.AddFilm, FilmFileStepKind.ShadowNoFiles], review.Rows.Single(r => r.EpisodeId == serenity.EpisodeId).Plan.Select(s => s.Kind));

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/series-films/link", new LinkSeriesFilmsRequest([serenity.EpisodeId]), Json)).StatusCode);
        var pass = await client.PostAsync("/api/v1/series-films/links/reconcile", null);
        Assert.Equal(new LinkPass(0, 0, 0, 0), await pass.Content.ReadFromJsonAsync<LinkPass>(Json));

        var row = (await SeriesFilmAdoptTests.ReviewAsync(client)).Rows.Single(r => r.EpisodeId == serenity.EpisodeId);
        Assert.Equal(SeriesFilmState.Linked, row.State);
        Assert.Equal([FilmFileStepKind.ShadowNoFiles], row.Plan.Select(s => s.Kind));
        Assert.True(File.Exists(specialPath));
        Assert.Contains((await SeriesFilmAdoptTests.FilesAsync(client, series.Id)).Files, f => f.EpisodeIds.Contains(serenity.EpisodeId) && f.LinkedFromPath is null);
        Assert.Null((await GetMovieAsync(client, row.MovieId!.Value)).Versions.Single().File);
        Assert.Empty(Directory.GetFileSystemEntries(filmRoot.Path, "*", SearchOption.AllDirectories));
    }
}
