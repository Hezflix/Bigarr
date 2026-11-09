using Bigarr.Api.Infrastructure.Metadata.Fixtures;
using Bigarr.Api.Infrastructure.Tv;
using Bigarr.Api.Infrastructure.Xem;
using Bigarr.Domain.Tv;
using EpisodeParser = Bigarr.Parsing.Episodes.Parser;

namespace Bigarr.Api.Tests.Series;

/// <summary>The shared mapper on the fixture catalogue's real episode lists (TVDB order).</summary>
public sealed class EpisodeMapperTests
{
    private static readonly FixtureSeriesMetadataProvider Catalogue = new();

    [Theory]
    [InlineData("Breaking.Bad.S01E03.720p.BluRay.x264-GRP", "S01E03")]
    [InlineData("Breaking Bad - 2x05 - Breakage [HDTV-720p]", "S02E05")]
    [InlineData("Breaking.Bad.S05E16.Felina.1080p.WEB-DL.DD5.1.H.264-GRP", "S05E16")]
    public void A_standard_release_maps_to_its_tvdb_episode(string title, string expected)
    {
        var (series, episodes) = Show(81189);

        var mapping = EpisodeMapper.Map(EpisodeParser.ParseTitle(title), series, episodes, sceneSource: true);

        Assert.True(mapping.IsMapped, mapping.Detail);
        Assert.Equal([expected], Labels(mapping));
    }

    [Fact]
    public void A_multi_episode_file_maps_to_every_episode_it_holds()
    {
        var (series, episodes) = Show(81189);

        var mapping = EpisodeMapper.Map(EpisodeParser.ParseTitle("Breaking.Bad.S01E02E03.720p.BluRay.x264-GRP"), series, episodes, sceneSource: false);

        Assert.Equal(["S01E02", "S01E03"], Labels(mapping));
    }

    [Fact]
    public void A_season_pack_maps_to_the_whole_season_and_a_multi_season_pack_to_every_season()
    {
        var (series, episodes) = Show(81189);

        var season = EpisodeMapper.Map(EpisodeParser.ParseTitle("Breaking.Bad.S02.1080p.BluRay.x264-GRP"), series, episodes, sceneSource: true);
        Assert.Equal(episodes.Count(e => e.SeasonNumber == 2), season.Episodes.Count);
        Assert.All(season.Episodes, e => Assert.Equal(2, e.SeasonNumber));

        var multi = EpisodeMapper.Map(EpisodeParser.ParseTitle("Breaking.Bad.S01-S03.1080p.BluRay.x264-GRP"), series, episodes, sceneSource: true);
        Assert.Equal(episodes.Count(e => e.SeasonNumber is >= 1 and <= 3), multi.Episodes.Count);
    }

    [Fact]
    public void A_special_maps_to_season_zero()
    {
        var (series, episodes) = Show(81189);

        var mapping = EpisodeMapper.Map(EpisodeParser.ParseTitle("Breaking.Bad.S00E02.720p.WEB-DL-GRP"), series, episodes, sceneSource: false);

        Assert.Equal(["S00E02"], Labels(mapping));
    }

    [Fact]
    public void An_unknown_episode_is_reported_not_guessed()
    {
        var (series, episodes) = Show(81189);

        var mapping = EpisodeMapper.Map(EpisodeParser.ParseTitle("Breaking.Bad.S09E01.720p.HDTV.x264-GRP"), series, episodes, sceneSource: false);

        Assert.Equal(EpisodeMappingStatus.EpisodeNotFound, mapping.Status);
        Assert.Contains("S09E01", mapping.Detail);
    }

    [Fact]
    public void A_daily_show_maps_by_air_date_and_a_date_in_a_standard_show_is_rejected()
    {
        var (daily, dailyEpisodes) = Show(71256);
        var mapping = EpisodeMapper.Map(EpisodeParser.ParseTitle("The.Daily.Show.2026.11.10.Guest.1080p.WEB.h264-GRP"), daily, dailyEpisodes, sceneSource: true);
        var episode = Assert.Single(mapping.Episodes);
        Assert.Equal(new DateOnly(2026, 11, 10), episode.AirDate);

        var (standard, standardEpisodes) = Show(81189);
        var wrong = EpisodeMapper.Map(EpisodeParser.ParseTitle("Breaking.Bad.2013.09.29.1080p.WEB.h264-GRP"), standard, standardEpisodes, sceneSource: true);
        Assert.Equal(EpisodeMappingStatus.WrongSeriesType, wrong.Status);
    }

    [Fact]
    public void Two_episodes_on_one_day_without_a_part_are_ambiguous()
    {
        var (daily, episodes) = Show(71256);
        var sameDay = episodes.First(e => e.AirDate is not null);
        episodes.Add(new Episode { Id = 9999, SeasonNumber = sameDay.SeasonNumber, EpisodeNumber = 999, AirDate = sameDay.AirDate, Title = "Second" });

        var mapping = EpisodeMapper.Map(EpisodeParser.ParseTitle($"The.Daily.Show.{sameDay.AirDate:yyyy.MM.dd}.1080p.WEB.h264-GRP"), daily, episodes, sceneSource: true);

        Assert.Equal(EpisodeMappingStatus.Ambiguous, mapping.Status);
        Assert.Empty(mapping.Episodes);
    }

    [Fact]
    public void Anime_absolute_numbers_map_through_tvdb_absolute_numbers()
    {
        var (frieren, episodes) = Show(XemFakes.FrierenTvdbId);

        var single = EpisodeMapper.Map(EpisodeParser.ParseTitle("[SubsPlease] Sousou no Frieren - 05 (1080p) [ABCD1234].mkv"), frieren, episodes, sceneSource: false);
        Assert.Equal(["S01E05"], Labels(single));

        var later = EpisodeMapper.Map(EpisodeParser.ParseTitle("[SubsPlease] Sousou no Frieren - 33 (1080p) [ABCD1234].mkv"), frieren, episodes, sceneSource: false);
        Assert.Equal(["S02E05"], Labels(later));

        var batch = EpisodeMapper.Map(EpisodeParser.ParseTitle("[Group] Sousou no Frieren - 01-03 [1080p]"), frieren, episodes, sceneSource: false);
        Assert.Equal(["S01E01", "S01E02", "S01E03"], Labels(batch));
    }

    /// <summary>
    /// The S2 acceptance case: the scene restarts Frieren's count for its second season ("Sousou no Frieren S2 - 05"),
    /// TVDB goes on (absolute 33 = S02E05). XEM's name gives the scene season, XEM's mapping the TVDB episode.
    /// </summary>
    [Fact]
    public async Task An_anime_release_with_scene_numbering_maps_to_the_tvdb_episode_through_xem()
    {
        var (frieren, episodes) = Show(XemFakes.FrierenTvdbId);
        var client = XemFakes.RecordedClient();
        var mappings = await client.GetMappingsAsync(XemFakes.FrierenTvdbId, CancellationToken.None);
        var names = await client.GetNamesAsync(CancellationToken.None);
        frieren = frieren with { UseSceneNumbering = XemNumbering.Apply(episodes, mappings) };

        var index = new SeriesTitleIndex(
            [new IndexedSeries(frieren.Id, XemFakes.FrierenTvdbId, "Frieren: Beyond Journey's End", 2023, ["Sousou no Frieren"])],
            names.Select(n => new SceneAlias(n.TvdbId, n.Title, n.SceneSeasonNumber)));

        foreach (var title in new[] { "[SubsPlease] Sousou no Frieren S2 - 05 (1080p) [ABCD1234].mkv", "[Erai-raws] Sousou no Frieren 2nd Season - 05 [1080p][Multiple Subtitle]" })
        {
            var parsed = EpisodeParser.ParseTitle(title);
            var match = index.Find(parsed);
            Assert.Equal(frieren.Id, match.Series?.Id);
            Assert.Equal(SeriesMatchKind.SceneName, match.Kind);
            Assert.Equal(2, match.SceneSeasonNumber);

            var mapping = EpisodeMapper.Map(parsed, frieren, episodes, sceneSource: true, match.SceneSeasonNumber);
            Assert.Equal(["S02E05"], Labels(mapping));
            Assert.Equal(33, mapping.Episodes[0].AbsoluteEpisodeNumber);
        }

        // Without XEM the same name would land on the first season: that is what the mapping prevents.
        var plain = EpisodeMapper.Map(EpisodeParser.ParseTitle("[SubsPlease] Sousou no Frieren S2 - 05 (1080p) [ABCD1234].mkv"), frieren, episodes, sceneSource: false);
        Assert.Equal(["S01E05"], Labels(plain));
    }

    [Fact]
    public void Scene_season_and_episode_numbers_apply_to_scene_releases_only_and_only_in_tvdb_order()
    {
        // A show numbered differently by the scene: TVDB S03E08 is scene S01E61 (Naruto Shippuden style).
        var series = new MappableSeries(1, "Demo", SeriesType.Standard, EpisodeOrder.Tvdb, UseSceneNumbering: true);
        List<Episode> episodes =
        [
            new() { Id = 1, SeasonNumber = 3, EpisodeNumber = 8, AbsoluteEpisodeNumber = 61, Title = "a" },
            new() { Id = 2, SeasonNumber = 1, EpisodeNumber = 61, Title = "b" },
        ];
        XemNumbering.Apply(episodes, [new XemMapping(1, 61, 61, 3, 8, 61)]);

        var scene = EpisodeMapper.Map(EpisodeParser.ParseTitle("Demo.S01E61.720p.HDTV.x264-GRP"), series, episodes, sceneSource: true);
        Assert.Equal(1, Assert.Single(scene.Episodes).Id);

        var file = EpisodeMapper.Map(EpisodeParser.ParseTitle("Demo.S01E61.720p.HDTV.x264-GRP"), series, episodes, sceneSource: false);
        Assert.Equal(2, Assert.Single(file.Episodes).Id);
    }

    [Fact]
    public void Nothing_parsable_is_reported()
    {
        var (series, episodes) = Show(81189);

        Assert.Equal(EpisodeMappingStatus.NotParsable, EpisodeMapper.Map(EpisodeParser.ParseTitle("random-junk"), series, episodes, sceneSource: false).Status);
    }

    [Theory]
    [InlineData("Breaking Bad (2008)", "Breaking Bad", 2008, null, null)]
    [InlineData("Breaking Bad (2008) {tvdb-81189}", "Breaking Bad", 2008, 81189, null)]
    [InlineData("Breaking Bad [tvdbid-81189]", "Breaking Bad", null, 81189, null)]
    [InlineData("The.Daily.Show {tmdb-2224}", "The Daily Show", null, null, 2224)]
    [InlineData("Frieren - Beyond Journey's End", "Frieren - Beyond Journey's End", null, null, null)]
    public void Series_folder_names_give_title_year_and_ids(string folder, string title, int? year, int? tvdb, int? tmdb)
    {
        var info = SeriesFolderName.Parse(folder);

        Assert.Equal((title, year, tvdb, tmdb), (info.Title, info.Year, info.TvdbId, info.TmdbId));
    }

    [Fact]
    public void Two_library_series_with_one_title_are_ambiguous_unless_the_year_tells_them_apart()
    {
        var index = new SeriesTitleIndex(
            [new IndexedSeries(1, 10, "Doctor Who", 1963, []), new IndexedSeries(2, 20, "Doctor Who", 2005, [])],
            []);

        Assert.True(index.Find("Doctor Who", "Doctor Who", null).IsAmbiguous);
        Assert.Equal(2, index.Find("Doctor Who", "Doctor Who", 2005).Series?.Id);
        Assert.Equal(1, index.Find(EpisodeParser.ParseTitle("Doctor.Who.1963.S01E01.DVDRip.x264-GRP")!).Series?.Id);
    }

    [Fact]
    public void File_labels_name_first_and_last_episode_and_both_seasons_when_they_differ()
    {
        Assert.Equal("S01E01", Bigarr.Api.Features.Series.SeriesScanEndpoints.Label([(1, 1)]));
        Assert.Equal("S01E02-E03", Bigarr.Api.Features.Series.SeriesScanEndpoints.Label([(1, 2), (1, 3)]));
        Assert.Equal("S21E194-S22E01", Bigarr.Api.Features.Series.SeriesScanEndpoints.Label([(21, 194), (22, 1)]));
    }

    private static List<string> Labels(EpisodeMapping mapping) =>
        mapping.Episodes.Select(e => $"S{e.SeasonNumber:00}E{e.EpisodeNumber:00}").ToList();

    private static (MappableSeries Series, List<Episode> Episodes) Show(int tvdbId)
    {
        var metadata = Catalogue.GetByTvdbIdAsync(tvdbId, withEpisodes: true, CancellationToken.None).GetAwaiter().GetResult()!;
        var id = 0;
        var episodes = metadata.Episodes
            .Select(e => new Episode
            {
                Id = ++id,
                TvdbId = e.TvdbId,
                SeasonNumber = e.SeasonNumber,
                EpisodeNumber = e.EpisodeNumber,
                AbsoluteEpisodeNumber = e.AbsoluteEpisodeNumber,
                AirDate = e.AirDate,
                Title = e.Title,
            })
            .ToList();
        return (new MappableSeries(tvdbId, metadata.Title, metadata.SuggestedType, EpisodeOrder.Tvdb, UseSceneNumbering: false), episodes);
    }
}
