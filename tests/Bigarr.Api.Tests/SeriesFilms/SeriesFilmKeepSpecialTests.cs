using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.SeriesFilms;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Shadow;
using Bigarr.Domain.Library;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;
using static Bigarr.Api.Tests.SeriesFilms.SeriesFilmAdoptTests;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.SeriesFilms;

/// <summary>
/// When the special and the film both have a file, the user may keep the special's (S8): it becomes the film's file
/// (moved, never copied) and the film's own goes to the recycle bin. The review page shows both files and both plans.
/// </summary>
public sealed class SeriesFilmKeepSpecialTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>, IDisposable
{
    private const int DemonSlayer = 348545;
    private const int InfinityCastle = 1311031;

    private readonly string _films = Directory.CreateTempSubdirectory("bigarr-films-").FullName;
    private readonly string _shows = Directory.CreateTempSubdirectory("bigarr-shows-").FullName;
    private readonly string _bin = Directory.CreateTempSubdirectory("bigarr-films-bin-").FullName;
    private readonly string _downloads = Directory.CreateTempSubdirectory("bigarr-films-dl-").FullName;

    public void Dispose()
    {
        foreach (var dir in new[] { _films, _shows, _bin, _downloads })
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public async Task When_both_have_a_file_and_the_special_is_kept_it_becomes_the_films_file_and_the_films_goes_to_the_recycle_bin()
    {
        var client = await ClientAsync(factory);
        await ConfigureAsync(client, _bin);
        var (filmRoot, showRoot) = await SeriesFilmFlowTests.RootsAsync(client, _films, _shows);
        var series = await SeriesAsync(client, DemonSlayer, showRoot.Id);
        var version = series.Versions.Single();
        var castle = (await SeriesFilmFlowTests.FilmsAsync(client, series.Id)).Films.Single(f => f.Label == "S00E16");

        var (hd, _) = await ProfilesAsync(client);
        var movie = await AddMovieAsync(client, InfinityCastle, hd, filmRoot.Id);
        var filmVersion = movie.Versions.Single();
        var download = Path.Combine(_downloads, "Demon.Slayer.Infinity.Castle.2025.720p.WEB-DL-GRP.mkv");
        await File.WriteAllTextAsync(download, "the film's own bytes");
        var manual = await client.PostAsJsonAsync("/api/v1/manualimport", new ManualImportRequest([new ManualImportItemRequest(download, filmVersion.Id, ParserQuality.WEBDL720p.Id)], TransferMode.Copy), Json);
        Assert.Equal(HttpStatusCode.OK, manual.StatusCode);
        var film = await WaitForFileAsync(client, movie.Id, filmVersion.Id);
        var filmPath = Path.Combine(filmRoot.Path, filmVersion.FolderName, film.File!.RelativePath);
        var specialPath = await PlantSpecialFileAsync(factory.Services, series.Id, version.Id, castle.EpisodeId, "Specials/Demon Slayer - S00E16 - Infinity Castle Bluray-1080p.mkv", ParserQuality.Bluray1080p.Id);
        var inode = SeriesFilmFlowTests.Inode(specialPath);

        // Both files are shown, and both ways: the film's wins by default, or the special's replaces it.
        var row = (await ReviewAsync(client)).Rows.Single(r => r.EpisodeId == castle.EpisodeId);
        Assert.Equal((filmPath, "WEBDL-720p"), (row.FilmFile!.Path, row.FilmFile.Quality));
        var specialFile = Assert.Single(row.SpecialFiles);
        Assert.Equal((specialPath, "Bluray-1080p", version.Name, false), (specialFile.Path, specialFile.Quality, specialFile.Version, specialFile.IsFilmLink));
        Assert.Equal([FilmFileStepKind.ReuseFilm, FilmFileStepKind.FilmFileWins, FilmFileStepKind.HardlinkIntoSpecials], row.Plan.Select(s => s.Kind));
        Assert.NotNull(row.KeepSpecialPlan);
        Assert.Equal([FilmFileStepKind.ReuseFilm, FilmFileStepKind.SpecialFileWins, FilmFileStepKind.RecycleFilmFile, FilmFileStepKind.HardlinkIntoSpecials], row.KeepSpecialPlan.Select(s => s.Kind));
        Assert.Equal((specialPath, filmPath), (row.KeepSpecialPlan[1].From, row.KeepSpecialPlan[2].From));

        var linked = await client.PostAsJsonAsync("/api/v1/series-films/link", new LinkSeriesFilmsRequest([castle.EpisodeId], [castle.EpisodeId]), Json);
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);

        // The film's file is now the special's bytes (its quality too), and the special its hardlink: one inode.
        var kept = await WaitUntilAsync(async () => (await GetMovieAsync(client, movie.Id)).Versions.Single().File is { Quality: "Bluray-1080p" } f ? f : null, "the special's file as the film's");
        var keptPath = Path.Combine(filmRoot.Path, filmVersion.FolderName, kept.RelativePath);
        Assert.Equal(inode, SeriesFilmFlowTests.Inode(keptPath));
        var special = await WaitUntilAsync(async () => (await FilesAsync(client, series.Id)).Files.FirstOrDefault(f => f.LinkedFromPath == keptPath), "the special's hardlink");
        var showFolder = Path.Combine(version.RootFolderPath, version.FolderName);
        Assert.Equal(inode, SeriesFilmFlowTests.Inode(Path.Combine(showFolder, special.RelativePath)));

        // The film's own file went to the recycle bin, not the special's.
        var binned = Assert.Single(Directory.GetFiles(_bin, "*", SearchOption.AllDirectories));
        Assert.Equal("the film's own bytes", await File.ReadAllTextAsync(binned));

        var after = (await ReviewAsync(client)).Rows.Single(r => r.EpisodeId == castle.EpisodeId);
        Assert.Equal((SeriesFilmState.Linked, (List<FilmFileStep>?)null), (after.State, after.KeepSpecialPlan));
        Assert.Equal([FilmFileStepKind.AlreadyHardlinked], after.Plan.Select(s => s.Kind));
    }
}
