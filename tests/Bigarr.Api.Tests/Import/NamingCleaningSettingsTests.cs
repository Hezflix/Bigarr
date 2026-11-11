using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Tests.Search;
using Bigarr.Api.Tests.Series;
using Bigarr.Domain.Library;
using Bigarr.Domain.Naming;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Import;

/// <summary>
/// "Replace Illegal Characters" and "Colon Replacement" in Settings → Media management, for films and for series apart:
/// defaults, validation, the live preview, and the names they give to new folders and renamed files.
/// </summary>
public sealed class NamingCleaningSettingsTests(SeriesApiFactory factory) : IClassFixture<SeriesApiFactory>, IDisposable
{
    private const int MissionImpossible = 954;
    private const int DunePartTwo = 693134;
    private const int FrierenTvdb = 424536;

    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-naming-lib-").FullName;

    public void Dispose() => TryDelete(_root);

    [Fact]
    public async Task Film_settings_default_to_smart_refuse_custom_and_drive_the_preview_folders_and_renames()
    {
        var client = await ClientAsync(factory);
        try
        {
            var defaults = (await client.GetFromJsonAsync<MediaManagementResponse>("/api/v1/mediamanagement", Json))!;
            Assert.True(defaults.ReplaceIllegalCharacters);
            Assert.Equal(ColonReplacement.Smart, defaults.ColonReplacement);

            // Radarr has no custom colon: refused, and so is a value nobody knows.
            var custom = await client.PutAsJsonAsync("/api/v1/mediamanagement", Film() with { ColonReplacement = ColonReplacement.Custom }, Json);
            Assert.Equal(HttpStatusCode.BadRequest, custom.StatusCode);
            Assert.Contains("custom", await custom.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/mediamanagement", Film() with { ColonReplacement = (ColonReplacement)42 }, Json)).StatusCode);

            // A request that says nothing about the settings leaves them alone.
            var untouched = await client.PutAsJsonAsync("/api/v1/mediamanagement", Film(), Json);
            Assert.Equal(HttpStatusCode.OK, untouched.StatusCode);
            var kept = (await untouched.Content.ReadFromJsonAsync<MediaManagementResponse>(Json))!;
            Assert.Equal((true, ColonReplacement.Smart), (kept.ReplaceIllegalCharacters, kept.ColonReplacement));

            // The preview renders a draft on a title of the user's choosing, with the draft's settings.
            string? Preview(NamingPreviewRequest request) =>
                client.PostAsJsonAsync("/api/v1/mediamanagement/naming/preview", request, Json).Result.Content.ReadFromJsonAsync<NamingPreviewResponse>(Json).Result!.FolderName;
            const string folder = "{Movie Title} {(Release Year)}";
            Assert.Equal("Star Wars - Andor (1999)", Preview(new NamingPreviewRequest(null, folder, SampleTitle: "Star Wars: Andor")));
            Assert.Equal("Star Wars- Andor (1999)", Preview(new NamingPreviewRequest(null, folder, true, ColonReplacement.Dash, "Star Wars: Andor")));
            Assert.Equal("Star Wars Andor (1999)", Preview(new NamingPreviewRequest(null, folder, false, ColonReplacement.Dash, "Star Wars: Andor")));
            Assert.Equal("Face+Off (1999)", Preview(new NamingPreviewRequest(null, folder, SampleTitle: "Face/Off")));
            Assert.Equal("The Matrix (1999)", Preview(new NamingPreviewRequest(null, folder)));

            // Saved: the settings page shows the preview with them, and a new version's folder follows them.
            var dash = await client.PutAsJsonAsync("/api/v1/mediamanagement", Film() with { ColonReplacement = ColonReplacement.Dash }, Json);
            Assert.Equal(HttpStatusCode.OK, dash.StatusCode);
            var (hd, _) = await ProfilesAsync(client);
            var filmRoot = await AddRootAsync(client, Path.Combine(_root, "added"));
            var mission = await AddMovieAsync(client, MissionImpossible, hd, filmRoot.Id);
            Assert.Equal("Mission- Impossible (1996)", mission.Versions.Single().FolderName);
            await client.DeleteAsync($"/api/v1/movies/{mission.Id}");

            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/mediamanagement", Film() with { ReplaceIllegalCharacters = false }, Json)).StatusCode);
            var dune = await AddMovieAsync(client, DunePartTwo, hd, filmRoot.Id);
            Assert.Equal("Dune Part Two (2024)", dune.Versions.Single().FolderName);
            await client.DeleteAsync($"/api/v1/movies/{dune.Id}");

            // A rename follows them as well.
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/mediamanagement", Film() with { ReplaceIllegalCharacters = true, ColonReplacement = ColonReplacement.SpaceDash }, Json)).StatusCode);
            var library = Path.Combine(_root, "scanned");
            var video = Path.Combine(library, "Mission - Impossible (1996)", "Mission.Impossible.1996.1080p.BluRay.x264-GRP.mkv");
            Directory.CreateDirectory(Path.GetDirectoryName(video)!);
            File.WriteAllBytes(video, new byte[64 * 1024]);
            var scanRoot = await AddRootAsync(client, library);
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/v1/rootfolders/{scanRoot.Id}/scan", null)).StatusCode);
            var movie = await WaitUntilAsync(async () => (await client.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!.FirstOrDefault(m => m.TmdbId == MissionImpossible), "the scan to find Mission: Impossible");
            var preview = Assert.Single((await client.GetFromJsonAsync<List<RenamePreviewResponse>>($"/api/v1/rename?movieId={movie.Id}", Json))!);
            Assert.Equal("Mission - Impossible (1996) [Bluray-1080p].mkv", preview.NewRelativePath);

            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/mediamanagement", Film() with { ColonReplacement = ColonReplacement.Delete }, Json)).StatusCode);
            preview = Assert.Single((await client.GetFromJsonAsync<List<RenamePreviewResponse>>($"/api/v1/rename?movieId={movie.Id}", Json))!);
            Assert.Equal("Mission Impossible (1996) [Bluray-1080p].mkv", preview.NewRelativePath);
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
        finally
        {
            await client.PutAsJsonAsync("/api/v1/mediamanagement", Film() with { ReplaceIllegalCharacters = true, ColonReplacement = ColonReplacement.Smart }, Json);
        }
    }

    [Fact]
    public async Task Series_settings_default_to_smart_take_a_custom_colon_and_drive_the_preview_and_folders()
    {
        var client = await ClientAsync(factory);
        try
        {
            var defaults = await SeriesNamingAsync(client);
            Assert.True(defaults.ReplaceIllegalCharacters);
            Assert.Equal(ColonReplacement.Smart, defaults.ColonReplacement);
            Assert.Equal(string.Empty, defaults.CustomColonReplacement);

            // Sonarr's validator: the replacement cannot hold what it replaces, nor another illegal character.
            foreach (var bad in new[] { ":", "a/b", "?" })
            {
                var refused = await client.PutAsJsonAsync("/api/v1/mediamanagement/series-naming", Series(defaults) with { ColonReplacement = ColonReplacement.Custom, CustomColonReplacement = bad }, Json);
                Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
                Assert.Contains("Custom colon replacement", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }

            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/mediamanagement/series-naming", Series(defaults) with { ColonReplacement = (ColonReplacement)42 }, Json)).StatusCode);

            // A request that says nothing about the settings leaves them alone.
            var untouched = (await (await client.PutAsJsonAsync("/api/v1/mediamanagement/series-naming", Series(defaults), Json)).Content.ReadFromJsonAsync<SeriesNamingResponse>(Json))!;
            Assert.Equal((true, ColonReplacement.Smart), (untouched.ReplaceIllegalCharacters, untouched.ColonReplacement));

            // The preview renders every template on a title of the user's choosing with the draft's settings.
            async Task<SeriesNamingPreviewResponse> Preview(ColonReplacement colon, string custom = "", bool replace = true) =>
                (await (await client.PostAsJsonAsync("/api/v1/mediamanagement/series-naming/preview", new SeriesNamingPreviewRequest(
                    defaults.SeriesFolderTemplate, defaults.SeasonFolderTemplate, defaults.SpecialsFolderName, defaults.StandardEpisodeTemplate, defaults.DailyEpisodeTemplate, defaults.AnimeEpisodeTemplate, defaults.MultiEpisodeStyle,
                    replace, colon, custom, "Star Wars: Andor"), Json)).Content.ReadFromJsonAsync<SeriesNamingPreviewResponse>(Json))!;
            Assert.Equal("Star Wars - Andor (2010)", (await Preview(ColonReplacement.Smart)).SeriesFolder);
            Assert.StartsWith("Star Wars - Andor - S01E01 - ", (await Preview(ColonReplacement.Smart)).StandardEpisode, StringComparison.Ordinal);
            Assert.Equal("Star Wars- Andor (2010)", (await Preview(ColonReplacement.Dash)).SeriesFolder);
            Assert.Equal("Star Wars∶ Andor (2010)", (await Preview(ColonReplacement.Custom, "∶")).SeriesFolder);
            Assert.StartsWith("Star Wars∶ Andor - S01E01", (await Preview(ColonReplacement.Custom, "∶")).StandardEpisode, StringComparison.Ordinal);
            Assert.Equal("Star Wars Andor (2010)", (await Preview(ColonReplacement.Dash, replace: false)).SeriesFolder);
            var invalid = await Preview(ColonReplacement.Custom, "/");
            Assert.True(invalid.Errors.ContainsKey("customColonReplacement"));

            // Saved: the page gets the settings back, and a new series folder follows them.
            var saved = await client.PutAsJsonAsync("/api/v1/mediamanagement/series-naming", Series(defaults) with { ColonReplacement = ColonReplacement.Custom, CustomColonReplacement = "∶" }, Json);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var custom = (await saved.Content.ReadFromJsonAsync<SeriesNamingResponse>(Json))!;
            Assert.Equal((ColonReplacement.Custom, "∶"), (custom.ColonReplacement, custom.CustomColonReplacement));

            var frieren = await SeriesSetup.AddAsync(client, FrierenTvdb);
            Assert.Equal("Frieren∶ Beyond Journey's End (2023)", frieren.Versions.Single().FolderName);
            await client.DeleteAsync($"/api/v1/series/{frieren.Id}");

            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/mediamanagement/series-naming", Series(defaults) with { ColonReplacement = ColonReplacement.SpaceDashSpace }, Json)).StatusCode);
            frieren = await SeriesSetup.AddAsync(client, FrierenTvdb);
            Assert.Equal("Frieren - Beyond Journey's End (2023)", frieren.Versions.Single().FolderName);
            await client.DeleteAsync($"/api/v1/series/{frieren.Id}");

            // The film settings are not the series' (Radarr and Sonarr are configured apart).
            var film = (await client.GetFromJsonAsync<MediaManagementResponse>("/api/v1/mediamanagement", Json))!;
            Assert.Equal(ColonReplacement.Smart, film.ColonReplacement);
        }
        finally
        {
            await client.PutAsJsonAsync("/api/v1/mediamanagement/series-naming", Series(await SeriesNamingAsync(client)) with { ReplaceIllegalCharacters = true, ColonReplacement = ColonReplacement.Smart, CustomColonReplacement = string.Empty }, Json);
        }
    }

    [Fact]
    public void Shipped_defaults_are_both_apps_defaults()
    {
        var config = MediaManagementConfig.Default();
        Assert.Equal((true, ColonReplacement.Smart), (config.ReplaceIllegalCharacters, config.ColonReplacement));
        Assert.Equal((true, ColonReplacement.Smart, string.Empty), (config.SeriesReplaceIllegalCharacters, config.SeriesColonReplacement, config.SeriesCustomColonReplacement));
        Assert.Equal(NameCleaning.Default, config.FilmNameCleaning);
        Assert.Equal(NameCleaning.Default, config.SeriesNameCleaning);
    }

    private static MediaManagementRequest Film() =>
        new(true, MediaManagementConfig.DefaultFileNameTemplate, MediaManagementConfig.DefaultFolderNameTemplate, TransferMode.HardlinkOrCopy, null, 7);

    private static SeriesNamingRequest Series(SeriesNamingResponse n) => new(
        n.RenameEpisodes, n.SeriesFolderTemplate, n.SeasonFolderTemplate, n.SpecialsFolderName, n.StandardEpisodeTemplate, n.DailyEpisodeTemplate, n.AnimeEpisodeTemplate, n.MultiEpisodeStyle);

    private static async Task<SeriesNamingResponse> SeriesNamingAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<SeriesNamingResponse>("/api/v1/mediamanagement/series-naming", Json))!;
}
