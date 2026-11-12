using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.SeriesFilms;
using Bigarr.Api.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.SeriesFilms;

/// <summary>Following Sonarr with specials that are films (S8): Sonarr's grab of the special is judged against the film's picks.</summary>
public sealed class SeriesFilmShadowTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>, IDisposable
{
    private const int DemonSlayer = 348545;

    private readonly string _films = Directory.CreateTempSubdirectory("bigarr-films-").FullName;
    private readonly string _shows = Directory.CreateTempSubdirectory("bigarr-shows-").FullName;

    public void Dispose()
    {
        TryDelete(_films);
        TryDelete(_shows);
    }

    [Fact]
    public async Task A_sonarr_grab_of_a_film_special_pairs_with_the_films_pick()
    {
        var client = await ClientAsync(factory);
        var (filmRoot, showRoot) = await SeriesFilmFlowTests.RootsAsync(client, _films, _shows);
        var existing = (await client.GetFromJsonAsync<List<Bigarr.Api.Features.Series.SeriesSummary>>("/api/v1/series", Json))!.FirstOrDefault(s => s.TvdbId == DemonSlayer);
        var seriesId = existing?.Id ?? (await SeriesFilmFlowTests.AddSeriesAsync(client, DemonSlayer, showRoot.Id)).Id;
        var castle = (await SeriesFilmFlowTests.FilmsAsync(client, seriesId)).Films.Single(f => f.Label == "S00E16");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/series/{seriesId}/films", new AddSeriesFilmRequest(castle.EpisodeId, RootFolderId: filmRoot.Id, SearchOnAdd: false), Json)).StatusCode);

        // Sonarr grabbed the special; Bigarr's film engine had picked the same release for the film, and another later.
        const string release = "Demon.Slayer.Kimetsu.no.Yaiba.Infinity.Castle.2025.1080p.WEB-DL.DDP5.1.H.264-GRP";
        var at = DateTime.UtcNow.AddHours(-2);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Bigarr.Api.Infrastructure.Persistence.BigarrDbContext>();
            var sonarr = new Bigarr.Domain.Shadow.FollowedSonarr { Name = "Sonarr films", Url = "http://sonarr.invalid", ApiKey = "k", VersionName = "1080p" };
            db.FollowedSonarrs.Add(sonarr);
            var grab = new Bigarr.Domain.Shadow.SonarrGrab
            {
                FollowedSonarr = sonarr,
                SonarrHistoryId = 1,
                SonarrSeriesId = 1,
                TvdbId = DemonSlayer,
                SeriesId = seriesId,
                SeriesTitle = "Demon Slayer: Kimetsu no Yaiba",
                VersionName = "1080p",
                GrabbedAt = at,
                ReleaseTitle = release,
                ReleaseGuid = "guid-castle",
            };
            grab.Episodes.Add(new Bigarr.Domain.Shadow.SonarrGrabEpisode { Grab = grab, SonarrHistoryId = 1, SonarrEpisodeId = 1, EpisodeId = castle.EpisodeId, SeasonNumber = 0, EpisodeNumber = 16 });
            db.SonarrGrabs.Add(grab);
            db.ShadowPicks.Add(new Bigarr.Domain.Shadow.ShadowPick
            {
                TmdbId = 1311031,
                MovieTitle = "Demon Slayer: Kimetsu no Yaiba Infinity Castle",
                VersionName = "1080p",
                Trigger = "rss",
                PickedAt = at.AddMinutes(-20),
                LastPickedAt = at.AddMinutes(-20),
                ReleaseTitle = release,
                ReleaseGuid = "guid-castle",
            });
            await db.SaveChangesAsync();
        }

        var compare = (await client.GetFromJsonAsync<Bigarr.Api.Features.Shadow.SeriesCompareResponse>($"/api/v1/shadow/compare/series?tvdbId={DemonSlayer}", Json))!;
        var row = compare.Groups.SelectMany(g => g.Rows).Single(r => r.Episodes == "S00E16");
        Assert.Equal(Bigarr.Api.Features.Shadow.SeriesCompareOutcome.Same, row.Outcome);
        var pick = Assert.Single(row.Bigarr);
        Assert.Equal("Demon Slayer: Kimetsu no Yaiba Infinity Castle (2025)", pick.Film);
        Assert.Equal(release, pick.Title);
    }

    [Fact]
    public async Task A_film_already_in_the_library_is_linked_as_is()
    {
        var client = await ClientAsync(factory);
        var (filmRoot, showRoot) = await SeriesFilmFlowTests.RootsAsync(client, _films, _shows);

        // The film came first (from Radarr, or added by hand), with its own version name.
        var (hd, _) = await ProfilesAsync(client);
        var created = await client.PostAsJsonAsync("/api/v1/movies", new Bigarr.Api.Features.Movies.AddMovieRequest(635302, hd, filmRoot.Id, VersionName: "radarr"), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var movie = (await created.Content.ReadFromJsonAsync<Bigarr.Api.Features.Movies.MovieResponse>(Json))!;

        var existing = (await client.GetFromJsonAsync<List<Bigarr.Api.Features.Series.SeriesSummary>>("/api/v1/series", Json))!.FirstOrDefault(s => s.TvdbId == DemonSlayer);
        var seriesId = existing?.Id ?? (await SeriesFilmFlowTests.AddSeriesAsync(client, DemonSlayer, showRoot.Id)).Id;
        var mugen = (await SeriesFilmFlowTests.FilmsAsync(client, seriesId)).Films.Single(f => f.Label == "S00E02");
        Assert.Equal(movie.Id, mugen.ExistingMovieId);

        var added = await client.PostAsJsonAsync($"/api/v1/series/{seriesId}/films", new AddSeriesFilmRequest(mugen.EpisodeId), Json);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal(movie.Id, (await added.Content.ReadFromJsonAsync<SeriesFilmsResponse>(Json))!.Films.Single(f => f.Label == "S00E02").Link!.MovieId);
        var after = await GetMovieAsync(client, movie.Id);
        Assert.Equal("radarr", Assert.Single(after.Versions).Name);
        Assert.Single((await client.GetFromJsonAsync<List<Bigarr.Api.Features.Movies.MovieSummary>>("/api/v1/movies", Json))!, m => m.TmdbId == 635302);
    }
}
