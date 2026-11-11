using Bigarr.Api.Features.Migration;
using Bigarr.Api.Infrastructure.RadarrImport;
using Bigarr.Api.Infrastructure.SonarrImport;
using Bigarr.Api.Tests.Hosting;
using Bigarr.Domain.History;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Migration;

/// <summary>
/// The Sonarr client on payloads in Sonarr v4's exact shape (fields Bigarr does not read included), Sonarr v3's single
/// file language, the routes a Sonarr may lack, the key, and the translation of Sonarr's quality ids to Bigarr's.
/// </summary>
public sealed class SonarrClientTests
{
    private const string Key = "sonarr-client-key";

    private const string SeriesJson = """
        [{"title":"Frieren: Beyond Journey's End","alternateTitles":[{"title":"Sousou no Frieren","sceneSeasonNumber":-1},{"title":"Sousou no Frieren S2","sceneSeasonNumber":2}],
          "sortTitle":"frieren beyond journeys end","status":"continuing","ended":false,"overview":"An elf mage.","network":"Nippon TV","airTime":"23:00",
          "images":[{"coverType":"poster","url":"/MediaCover/2/poster.jpg?lastWrite=638","remoteUrl":"https://artworks.thetvdb.com/banners/v4/series/424536/posters/a.jpg"}],
          "originalLanguage":{"id":8,"name":"Japanese"},
          "seasons":[{"seasonNumber":0,"monitored":false,"statistics":{"episodeFileCount":0,"episodeCount":0,"totalEpisodeCount":3,"sizeOnDisk":0,"releaseGroups":[],"percentOfEpisodes":0.0}},
                     {"seasonNumber":1,"monitored":true,"statistics":{"previousAiring":"2024-03-22T14:00:00Z","episodeFileCount":28,"episodeCount":28,"totalEpisodeCount":28,"sizeOnDisk":39728193536,"releaseGroups":["SubsPlease"],"percentOfEpisodes":100.0}}],
          "year":2023,"path":"/tv-anime/Frieren - Beyond Journey's End (2023)","qualityProfileId":4,"seasonFolder":true,"monitored":true,"monitorNewItems":"all",
          "useSceneNumbering":true,"runtime":24,"tvdbId":424536,"tvRageId":0,"tvMazeId":68542,"tmdbId":209867,"firstAired":"2023-09-29T00:00:00Z","lastAired":"2026-01-16T00:00:00Z",
          "seriesType":"anime","cleanTitle":"frierenbeyondjourneysend","imdbId":"tt22248376","titleSlug":"frieren-beyond-journeys-end","rootFolderPath":"/tv-anime/",
          "certification":"TV-14","genres":["Adventure","Animation","Anime","Fantasy"],"tags":[3],"added":"2024-01-04T18:21:07Z",
          "addOptions":null,"ratings":{"votes":1250,"value":9.1},"statistics":{"seasonCount":2,"episodeFileCount":28,"episodeCount":28,"totalEpisodeCount":41,"sizeOnDisk":39728193536,"releaseGroups":["SubsPlease"],"percentOfEpisodes":100.0},
          "languageProfileId":1,"id":2}]
        """;

    private const string EpisodesJson = """
        [{"seriesId":2,"tvdbId":9989321,"episodeFileId":17,"seasonNumber":1,"episodeNumber":1,"title":"The Journey's End","airDate":"2023-09-29","airDateUtc":"2023-09-29T14:00:00Z",
          "lastSearchTime":"2024-01-04T18:22:00Z","runtime":24,"finaleType":null,"overview":"The party returns.","hasFile":true,"monitored":true,"absoluteEpisodeNumber":1,
          "sceneAbsoluteEpisodeNumber":1,"sceneEpisodeNumber":1,"sceneSeasonNumber":1,"unverifiedSceneNumbering":false,"images":[],"id":4001},
         {"seriesId":2,"tvdbId":10001234,"episodeFileId":0,"seasonNumber":0,"episodeNumber":1,"title":"Mini Anime 1","airDate":"2024-01-05","runtime":2,"hasFile":false,"monitored":false,"unverifiedSceneNumbering":false,"id":4100}]
        """;

    private const string FilesJson = """
        [{"seriesId":2,"seasonNumber":1,"relativePath":"Season 01/[SubsPlease] Sousou no Frieren - 01-02 (1080p).mkv","path":"/tv-anime/Frieren - Beyond Journey's End (2023)/Season 01/[SubsPlease] Sousou no Frieren - 01-02 (1080p).mkv",
          "size":2878291002,"dateAdded":"2024-01-04T18:30:00Z","sceneName":"[SubsPlease] Sousou no Frieren - 01-02 (1080p) [A1B2C3D4]","releaseGroup":"SubsPlease",
          "languages":[{"id":8,"name":"Japanese"},{"id":1,"name":"English"}],
          "quality":{"quality":{"id":20,"name":"Bluray-1080p Remux","source":"blurayRaw","resolution":1080},"revision":{"version":2,"real":0,"isRepack":false}},
          "customFormats":[{"id":5,"name":"Anime Web Tier 01"}],"customFormatScore":1400,"indexerFlags":0,"releaseType":"multiEpisode",
          "mediaInfo":{"audioBitrate":0,"audioChannels":2,"audioCodec":"AAC","audioLanguages":"jpn","videoCodec":"x264","resolution":"1920x1080","runTime":"47:58","scanType":"Progressive","subtitles":"eng"},
          "qualityCutoffNotMet":false,"id":17},
         {"seriesId":2,"seasonNumber":1,"relativePath":"Season 01/v3 file.mkv","size":10,"language":{"id":1,"name":"English"},
          "quality":{"quality":{"id":13,"name":"Bluray-480p","source":"bluray","resolution":480},"revision":{"version":1,"real":0,"isRepack":false}},"id":18}]
        """;

    private const string SinceJson = """
        [{"episodeId":4001,"seriesId":2,"sourceTitle":"[SubsPlease] Sousou no Frieren (01-28) (1080p) [Batch]","languages":[{"id":8,"name":"Japanese"}],
          "quality":{"quality":{"id":3,"name":"WEBDL-1080p","source":"web","resolution":1080},"revision":{"version":1,"real":0,"isRepack":false}},
          "customFormats":[{"id":5,"name":"Anime Web Tier 01"}],"customFormatScore":1400,"qualityCutoffNotMet":false,"date":"2026-11-09T12:05:00Z",
          "downloadId":"0123456789ABCDEF0123456789ABCDEF01234567","eventType":"grabbed",
          "data":{"indexer":"Nyaa (Prowlarr)","nzbInfoUrl":"https://nyaa.invalid/view/1","releaseGroup":"SubsPlease","age":"0","ageHours":"1.2","ageMinutes":"70.5",
                  "publishedDate":"2026-11-09T10:55:00Z","downloadClient":"qBittorrent","downloadClientName":"qBittorrent","size":"39728193536","downloadUrl":"https://nyaa.invalid/1.torrent",
                  "guid":"https://nyaa.invalid/view/1","tvdbId":"424536","tvRageId":"0","imdbId":"","protocol":"2","customFormatScore":"1400","seriesMatchType":"0",
                  "releaseSource":"Rss","indexerFlags":"0","releaseType":"SeasonPack","torrentInfoHash":"0123456789ABCDEF0123456789ABCDEF01234567"},
          "episode":{"seriesId":2,"tvdbId":9989321,"episodeFileId":17,"seasonNumber":1,"episodeNumber":1,"title":"The Journey's End","airDate":"2023-09-29","runtime":24,"hasFile":true,"monitored":true,"absoluteEpisodeNumber":1,"unverifiedSceneNumbering":false,"id":4001},
          "id":90211}]
        """;

    private const string ParseJson = """
        {"title":"[SubsPlease] Sousou no Frieren - 05 (1080p) [ABCD1234].mkv",
         "parsedEpisodeInfo":{"releaseTitle":"[SubsPlease] Sousou no Frieren - 05 (1080p) [ABCD1234].mkv","seriesTitle":"Sousou no Frieren","seriesTitleInfo":{"title":"Sousou no Frieren"},
           "quality":{"quality":{"id":3,"name":"WEBDL-1080p","source":"web","resolution":1080},"revision":{"version":1,"real":0,"isRepack":false}},
           "seasonNumber":0,"episodeNumbers":[],"absoluteEpisodeNumbers":[5],"specialAbsoluteEpisodeNumbers":[],"languages":[{"id":8,"name":"Japanese"}],
           "fullSeason":false,"isPartialSeason":false,"isMultiSeason":false,"isSeasonExtra":false,"isSplitEpisode":false,"special":false,"releaseGroup":"SubsPlease","releaseHash":"ABCD1234","seasonPart":0,"releaseTokens":"(1080p)","isDaily":false,"isAbsoluteNumbering":true,"isPossibleSpecialEpisode":false,"isPossibleSceneSeasonSpecial":false},
         "series":{"title":"Frieren: Beyond Journey's End","tvdbId":424536,"seriesType":"anime","qualityProfileId":4,"monitored":true,"seasonFolder":true,"useSceneNumbering":true,"runtime":24,"year":2023,"id":2},
         "episodes":[{"seriesId":2,"tvdbId":9989330,"episodeFileId":0,"seasonNumber":1,"episodeNumber":5,"title":"Phantoms of the Dead","hasFile":false,"monitored":true,"absoluteEpisodeNumber":5,"unverifiedSceneNumbering":false,"id":4005}],
         "languages":[{"id":8,"name":"Japanese"}],"customFormats":[{"id":5,"name":"Anime Web Tier 01"}],"customFormatScore":1400}
        """;

    private const string ProfileJson = """
        [{"name":"HD-1080p","upgradeAllowed":true,"cutoff":20,
          "items":[{"quality":{"id":13,"name":"Bluray-480p","source":"bluray","resolution":480},"items":[],"allowed":false},
                   {"name":"WEB 1080p","items":[{"quality":{"id":3,"name":"WEBDL-1080p","source":"web","resolution":1080},"items":[],"allowed":true},{"quality":{"id":15,"name":"WEBRip-1080p","source":"webRip","resolution":1080},"items":[],"allowed":true}],"allowed":true,"id":1001},
                   {"quality":{"id":7,"name":"Bluray-1080p","source":"bluray","resolution":1080},"items":[],"allowed":true},
                   {"quality":{"id":20,"name":"Bluray-1080p Remux","source":"blurayRaw","resolution":1080},"items":[],"allowed":true},
                   {"quality":{"id":21,"name":"Bluray-2160p Remux","source":"blurayRaw","resolution":2160},"items":[],"allowed":false}],
          "minFormatScore":0,"cutoffFormatScore":0,"minUpgradeFormatScore":1,"formatItems":[{"format":5,"name":"Anime Web Tier 01","score":1400}],"id":4}]
        """;

    [Fact]
    public async Task The_client_reads_sonarr_v4_payloads()
    {
        await using var server = await FakeHttpServer.StartAsync();
        server.Responder = r =>
        {
            if (!r.Headers.TryGetValue("X-Api-Key", out var key) || key != Key)
            {
                return new FakeHttpServer.Answer(401);
            }

            return r.Path switch
            {
                "/sonarr/api/v3/series" => new FakeHttpServer.Answer(200, SeriesJson),
                "/sonarr/api/v3/episode" when r.Query == "?seriesId=2" => new FakeHttpServer.Answer(200, EpisodesJson),
                "/sonarr/api/v3/episodefile" when r.Query == "?seriesId=2" => new FakeHttpServer.Answer(200, FilesJson),
                "/sonarr/api/v3/history/since" => new FakeHttpServer.Answer(200, SinceJson),
                "/sonarr/api/v3/parse" => new FakeHttpServer.Answer(200, ParseJson),
                "/sonarr/api/v3/qualityprofile" => new FakeHttpServer.Answer(200, ProfileJson),
                _ => new FakeHttpServer.Answer(404),
            };
        };

        using var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var client = new SonarrClient(services.GetRequiredService<IHttpClientFactory>());
        var connection = new SonarrConnection(server.Url + "/sonarr/api/v3/", Key);

        var show = Assert.Single(await client.SeriesAsync(connection, CancellationToken.None));
        Assert.Equal(424536, show.TvdbId);
        Assert.Equal(209867, show.TmdbId);
        Assert.Equal("anime", show.SeriesType);
        Assert.Equal("all", show.MonitorNewItems);
        Assert.Equal(28, show.Statistics!.EpisodeFileCount);
        Assert.Equal(39728193536, show.Statistics.SizeOnDisk);
        Assert.False(show.Seasons!.Single(s => s.SeasonNumber == 0).Monitored);
        Assert.Equal("/tv-anime", SonarrImportService.RootPathOf(show));
        Assert.Equal(Bigarr.Domain.Tv.SeriesType.Anime, SonarrImportService.SeriesTypeOf(show.SeriesType));
        Assert.Equal(Bigarr.Domain.Tv.SeriesStatus.Continuing, SonarrImportService.Status(show.Status, show.Ended));

        var episodes = await client.EpisodesAsync(connection, 2, CancellationToken.None);
        Assert.Equal(1, episodes[0].AbsoluteEpisodeNumber);
        Assert.Equal(17, episodes[0].EpisodeFileId);
        Assert.Null(episodes[1].AbsoluteEpisodeNumber);

        var files = await client.EpisodeFilesAsync(connection, 2, CancellationToken.None);
        Assert.Equal(["Japanese", "English"], files[0].Languages!.Select(l => l.Name));
        Assert.Equal(30, SonarrQualities.ToBigarr(files[0].Quality!.Quality)!.Id);
        Assert.Equal("English", files[1].Language!.Name);
        Assert.Equal(20, SonarrQualities.ToBigarr(files[1].Quality!.Quality)!.Id);
        Assert.Equal(Path.Combine("Season 01", "[SubsPlease] Sousou no Frieren - 01-02 (1080p).mkv"), SonarrImportService.RelativePathOf(files[0], show));

        var since = Assert.Single(await client.HistorySinceAsync(connection, DateTime.UtcNow, CancellationToken.None));
        Assert.Equal(4001, since.Episode!.Id);
        Assert.Equal("SeasonPack", since.Data!["releaseType"]);
        Assert.Equal(HistoryEventType.Grabbed, SonarrImportService.HistoryType(since.EventType));
        Assert.Equal(HistoryEventType.Imported, SonarrImportService.HistoryType("seriesFolderImported"));
        Assert.Equal(HistoryEventType.FileDeleted, SonarrImportService.HistoryType("episodeFileDeleted"));
        Assert.Null(SonarrImportService.HistoryType("downloadIgnored"));

        var parse = await client.ParseAsync(connection, "[SubsPlease] Sousou no Frieren - 05 (1080p) [ABCD1234].mkv", CancellationToken.None);
        Assert.Equal(424536, parse.Series!.TvdbId);
        Assert.Equal([5], parse.ParsedEpisodeInfo!.AbsoluteEpisodeNumbers);
        Assert.Equal(1400, parse.CustomFormatScore);

        // A Sonarr profile in Bigarr's ids: the remuxes and Bluray-480p move, the cutoff (a remux) too, the group stays.
        var profile = SonarrQualities.ToBigarr(Assert.Single(await client.ProfilesAsync(connection, CancellationToken.None)));
        Assert.Equal(30, profile.Cutoff);
        var translated = RadarrProfileQualities.Translate(profile, [0, 3, 7, 15, 20, 30, 31]);
        Assert.NotNull(translated);
        Assert.Equal(30, translated.Cutoff);
        Assert.Contains(translated.Items, i => i.GroupId == 1001 && i.QualityIds.SequenceEqual([3, 15]) && i.Allowed);
        Assert.Contains(translated.Items, i => i.QualityIds.SequenceEqual([31]) && !i.Allowed);

        // Routes Sonarr v4 lacks answer empty; a wrong key is spelled out.
        Assert.Empty(await client.LanguageProfilesAsync(connection, CancellationToken.None));
        Assert.Empty(await client.ExclusionsAsync(connection, CancellationToken.None));
        var refused = await Assert.ThrowsAsync<SonarrUnavailableException>(() => client.SeriesAsync(connection with { ApiKey = "nope" }, CancellationToken.None));
        Assert.Contains("401", refused.Message, StringComparison.Ordinal);
        Assert.Null(await client.SeriesAsync(connection, 99, CancellationToken.None));
    }
}
