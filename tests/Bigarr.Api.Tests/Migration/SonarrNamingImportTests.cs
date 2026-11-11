using System.Net.Http.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Migration;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Shadow;
using Bigarr.Domain.Library;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Migration;

/// <summary>The one-shot Sonarr import copies Sonarr's <c>/config/naming</c> into media management; one Bigarr refuses is reported, not copied.</summary>
public sealed class SonarrNamingImportTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task Sonarr_naming_is_copied_and_an_unusable_one_is_reported()
    {
        var client = await ClientAsync(factory);
        var root = await SonarrImportTests.SeriesRootAsync(client, "bigarr-sonarr-naming-");
        await using var sonarr = await FakeSonarr.StartAsync();

        // An old Sonarr without the route: nothing changes.
        var none = await SonarrImportTests.ImportAsync(client, Request(sonarr.Url, root.Id));
        Assert.False(none.NamingImported);

        sonarr.Naming = new
        {
            renameEpisodes = false,
            multiEpisodeStyle = 1,
            standardEpisodeFormat = "{Series.Title}.S{season:00}E{episode:00}.{Episode.CleanTitle}.{Quality.Full}",
            dailyEpisodeFormat = "{Series Title} - {Air-Date} - {Episode Title} {Quality Full}",
            animeEpisodeFormat = "{Series Title} - {absolute:000} - {Episode Title} {Quality Full}",
            seriesFolderFormat = "{Series TitleYear} [tvdbid-{TvdbId}]",
            seasonFolderFormat = "Season {season}",
            specialsFolderFormat = "Extras Saison 0",
        };
        var report = await SonarrImportTests.ImportAsync(client, Request(sonarr.Url, root.Id));
        Assert.True(report.NamingImported);
        var naming = (await client.GetFromJsonAsync<SeriesNamingResponse>("/api/v1/mediamanagement/series-naming", Json))!;
        Assert.False(naming.RenameEpisodes);
        Assert.Equal(MultiEpisodeStyle.Duplicate, naming.MultiEpisodeStyle);
        Assert.Equal("{Series TitleYear} [tvdbid-{TvdbId}]", naming.SeriesFolderTemplate);
        Assert.Equal("Season {season}", naming.SeasonFolderTemplate);
        Assert.Equal("Extras Saison 0", naming.SpecialsFolderName);
        Assert.Equal("{Series Title} - {absolute:000} - {Episode Title} {Quality Full}", naming.AnimeEpisodeTemplate);

        // A format without any quality or version token would make two versions collide: kept out, reported.
        sonarr.Naming = new
        {
            renameEpisodes = true,
            multiEpisodeStyle = 5,
            standardEpisodeFormat = "{Series Title} - S{season:00}E{episode:00}",
            dailyEpisodeFormat = "{Series Title} - {Air-Date} {Quality Full}",
            animeEpisodeFormat = "{Series Title} - {absolute:000} {Quality Full}",
            seriesFolderFormat = "{Series Title}",
            seasonFolderFormat = "Season {season:00}",
            specialsFolderFormat = "Specials",
        };
        var refused = await SonarrImportTests.ImportAsync(client, Request(sonarr.Url, root.Id));
        Assert.False(refused.NamingImported);
        Assert.Contains(refused.Issues, i => i.Title == "Naming" && i.Reason.Contains("{Quality Full}", StringComparison.Ordinal));
        Assert.False((await client.GetFromJsonAsync<SeriesNamingResponse>("/api/v1/mediamanagement/series-naming", Json))!.RenameEpisodes);
    }

    private static SonarrImportRequest Request(string url, int rootId) =>
        new(url, FakeSonarr.Key, "1080p", [], new() { [FakeSonarr.RootPath] = rootId }, ImportHistory: false, ImportBlocklist: false, ImportProfileQualities: false, ImportQualitySizes: false, ImportCustomFormats: false);
}
