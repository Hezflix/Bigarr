using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Migration;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Shadow;
using Bigarr.Domain.Naming;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Migration;

/// <summary>
/// The one-shot Radarr import copies Radarr's "Replace Illegal Characters" and "Colon Replacement" (from <c>/config/naming</c>)
/// into the film settings, the series settings untouched; a follow never rewrites them.
/// </summary>
public sealed class RadarrNamingImportTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task Radarrs_colon_and_illegal_character_settings_are_copied_into_the_film_settings()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var library = Directory.CreateTempSubdirectory("bigarr-radarr-naming-").FullName;
        var root = await AddRootAsync(client, library);
        await using var radarr = await FakeRadarr.StartAsync();
        var series = (await client.GetFromJsonAsync<SeriesNamingResponse>("/api/v1/mediamanagement/series-naming", Json))!;

        try
        {
            // A Radarr from before the setting has no such route: nothing changes.
            await SetFilmAsync(client, true, ColonReplacement.Dash);
            Assert.False((await ImportAsync(client, radarr, hd, root.Id)).NamingImported);
            await AssertFilmAsync(client, true, ColonReplacement.Dash);

            // Radarr 5 names the format: camelCase, like every enum it sends.
            radarr.Naming = new { renameMovies = true, replaceIllegalCharacters = false, colonReplacementFormat = "spaceDashSpace", standardMovieFormat = "{Movie Title} ({Release Year})", movieFolderFormat = "{Movie Title} ({Release Year})" };
            Assert.True((await ImportAsync(client, radarr, hd, root.Id)).NamingImported);
            await AssertFilmAsync(client, false, ColonReplacement.SpaceDashSpace);

            foreach (var (sent, expected) in new (object, ColonReplacement)[] { ("delete", ColonReplacement.Delete), ("dash", ColonReplacement.Dash), ("spaceDash", ColonReplacement.SpaceDash), ("smart", ColonReplacement.Smart), ("Smart", ColonReplacement.Smart), (1, ColonReplacement.Dash), (3, ColonReplacement.SpaceDashSpace), (0, ColonReplacement.Delete) })
            {
                radarr.Naming = new { replaceIllegalCharacters = true, colonReplacementFormat = sent };
                Assert.True((await ImportAsync(client, radarr, hd, root.Id)).NamingImported);
                await AssertFilmAsync(client, true, expected);
            }

            // Radarr has no custom colon: a format Bigarr does not know leaves the colon as it was (the other setting is still copied).
            radarr.Naming = new { replaceIllegalCharacters = false, colonReplacementFormat = "custom" };
            Assert.True((await ImportAsync(client, radarr, hd, root.Id)).NamingImported);
            await AssertFilmAsync(client, false, ColonReplacement.Delete);

            // "Import Radarr's naming" off: nothing is copied.
            radarr.Naming = new { replaceIllegalCharacters = true, colonReplacementFormat = "dash" };
            Assert.False((await ImportAsync(client, radarr, hd, root.Id, importNaming: false)).NamingImported);
            await AssertFilmAsync(client, false, ColonReplacement.Delete);

            // The series settings are Sonarr's: a Radarr import never touches them.
            var after = (await client.GetFromJsonAsync<SeriesNamingResponse>("/api/v1/mediamanagement/series-naming", Json))!;
            Assert.Equal((series.ReplaceIllegalCharacters, series.ColonReplacement, series.CustomColonReplacement), (after.ReplaceIllegalCharacters, after.ColonReplacement, after.CustomColonReplacement));

            // Following never rewrites them: a pass after Radarr changed its mind keeps the film settings.
            Assert.True((await ImportAsync(client, radarr, hd, root.Id, follow: true)).NamingImported);
            await AssertFilmAsync(client, true, ColonReplacement.Dash);
            radarr.Naming = new { replaceIllegalCharacters = false, colonReplacementFormat = "delete" };
            var followed = Assert.Single(await client.GetFromJsonAsync<List<FollowedRadarrResponse>>("/api/v1/shadow/radarrs", Json) ?? []);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/shadow/radarrs/{followed.Id}/sync", null)).StatusCode);
            await AssertFilmAsync(client, true, ColonReplacement.Dash);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/shadow/radarrs/{followed.Id}")).StatusCode);
        }
        finally
        {
            await SetFilmAsync(client, true, ColonReplacement.Smart);
            TryDelete(library);
        }
    }

    private static async Task SetFilmAsync(HttpClient client, bool replace, ColonReplacement colon)
    {
        var response = await client.PutAsJsonAsync("/api/v1/mediamanagement", new MediaManagementRequest(true, MediaManagementConfigTemplates.File, MediaManagementConfigTemplates.Folder, Bigarr.Domain.Library.TransferMode.HardlinkOrCopy, null, 7, ReplaceIllegalCharacters: replace, ColonReplacement: colon), Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertFilmAsync(HttpClient client, bool replace, ColonReplacement colon)
    {
        var film = (await client.GetFromJsonAsync<MediaManagementResponse>("/api/v1/mediamanagement", Json))!;
        Assert.Equal((replace, colon), (film.ReplaceIllegalCharacters, film.ColonReplacement));
    }

    private static async Task<RadarrImportStatusResponse> ImportAsync(HttpClient client, FakeRadarr radarr, int profileId, int rootId, bool importNaming = true, bool follow = false)
    {
        var started = await client.PostAsJsonAsync("/api/v1/migration/radarr/import", new RadarrImportRequest(
            radarr.Url, FakeRadarr.Key, "1080p", new() { [1] = profileId }, new() { [FakeRadarr.RootPath] = rootId }, Follow: follow, ImportNaming: importNaming), Json);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        return await WaitUntilAsync(async () =>
        {
            var status = (await client.GetFromJsonAsync<RadarrImportStatusResponse>("/api/v1/migration/radarr/status", Json))!;
            Assert.NotEqual(RadarrImportState.Failed, status.State);
            return status.State == RadarrImportState.Completed ? status : null;
        }, "the Radarr import");
    }
}

file static class MediaManagementConfigTemplates
{
    public const string File = Bigarr.Domain.Library.MediaManagementConfig.DefaultFileNameTemplate;

    public const string Folder = Bigarr.Domain.Library.MediaManagementConfig.DefaultFolderNameTemplate;
}
