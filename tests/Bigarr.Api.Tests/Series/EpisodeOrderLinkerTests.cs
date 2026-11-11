using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Metadata.Fixtures;
using Bigarr.Api.Infrastructure.Tv;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Tv;
using EpisodeParser = Bigarr.Parsing.Episodes.Parser;

namespace Bigarr.Api.Tests.Series;

/// <summary>
/// S6's linking rules on real TMDB answers (recorded into the fixtures): Frieren, whose TMDB default order merges TVDB's
/// two seasons into one and which has an absolute episode group; Breaking Bad, whose DVD group splits TVDB's season 5.
/// Plus the mapper's fallback on the order and the release checks.
/// </summary>
public sealed class EpisodeOrderLinkerTests
{
    private const int FrierenTmdb = 209867;
    private const int BreakingBadTmdb = 1396;
    private const string FrierenAbsoluteGroup = "69b0121aa45e0d35504ebcd0";
    private const string BreakingBadDvdGroup = "69f50757054263b7bc87e32a";

    private static readonly FixtureSeriesMetadataProvider Provider = new();

    [Fact]
    public async Task Tmdb_default_order_merges_frierens_two_tvdb_seasons_into_one()
    {
        var (_, episodes) = await EpisodesAsync(424536);
        var order = (await Provider.GetOrderEpisodesAsync(FrierenTmdb, new OrderChoice(EpisodeOrder.TmdbDefault), CancellationToken.None))!;

        var link = EpisodeOrderLinker.Link(episodes, order);

        // Season 1 is linked by TMDB id, number for number.
        var e05 = link.Numbers[Episode(episodes, 1, 5)];
        Assert.Equal((1, 5, 5), (e05.SeasonNumber, e05.EpisodeNumber, e05.AbsoluteNumber));
        Assert.Equal(OrderLinkRule.TmdbId, e05.Rule);

        // TVDB's season 2 has no TMDB ids in the fixture: found on its air date and title, as TMDB's S01E29–E38.
        var s2e1 = link.Numbers[Episode(episodes, 2, 1)];
        Assert.Equal((1, 29, 29), (s2e1.SeasonNumber, s2e1.EpisodeNumber, s2e1.AbsoluteNumber));
        Assert.Equal(OrderLinkRule.DateAndTitle, s2e1.Rule);
        var s2e10 = link.Numbers[Episode(episodes, 2, 10)];
        Assert.Equal((1, 38), (s2e10.SeasonNumber, s2e10.EpisodeNumber));

        // Every TVDB episode of the (trimmed) fixture is in the order; TMDB's extra specials are listed, not tracked.
        Assert.Empty(link.NotInOrder);
        Assert.NotEmpty(link.OrderOnly);
        Assert.All(link.OrderOnly, o => Assert.Equal(0, o.SeasonNumber));
    }

    [Fact]
    public async Task An_absolute_episode_group_numbers_anime_by_its_groups_and_running_absolute_numbers()
    {
        var (_, episodes) = await EpisodesAsync(424536);
        var order = (await Provider.GetOrderEpisodesAsync(FrierenTmdb, new OrderChoice(EpisodeOrder.TmdbEpisodeGroup, FrierenAbsoluteGroup), CancellationToken.None))!;

        var link = EpisodeOrderLinker.Link(episodes, order);

        var s2e1 = link.Numbers[Episode(episodes, 2, 1)];
        Assert.Equal((2, 1, 29), (s2e1.SeasonNumber, s2e1.EpisodeNumber, s2e1.AbsoluteNumber));
        var s1e28 = link.Numbers[Episode(episodes, 1, 28)];
        Assert.Equal((1, 28, 28), (s1e28.SeasonNumber, s1e28.EpisodeNumber, s1e28.AbsoluteNumber));

        // The group's "Specials" part is season 0, without absolute numbers.
        var special = link.Numbers[Episode(episodes, 0, 1)];
        Assert.Equal(0, special.SeasonNumber);
        Assert.Null(special.AbsoluteNumber);
    }

    [Fact]
    public async Task A_dvd_group_splits_breaking_bads_fifth_season_and_leaves_the_specials_out()
    {
        var (_, episodes) = await EpisodesAsync(81189);
        var order = (await Provider.GetOrderEpisodesAsync(BreakingBadTmdb, new OrderChoice(EpisodeOrder.TmdbEpisodeGroup, BreakingBadDvdGroup), CancellationToken.None))!;

        var link = EpisodeOrderLinker.Link(episodes, order);

        Assert.Equal((5, 8), Numbers(link, Episode(episodes, 5, 8)));
        Assert.Equal((6, 1), Numbers(link, Episode(episodes, 5, 9)));
        Assert.Equal((6, 8), Numbers(link, Episode(episodes, 5, 16)));
        Assert.Equal((2, 13), Numbers(link, Episode(episodes, 2, 13)));

        // The DVD group lists no specials: TVDB's are not in the order, and no episode of the order is left over.
        Assert.All(link.NotInOrder, e => Assert.Equal(0, e.SeasonNumber));
        Assert.Equal(episodes.Count(e => e.SeasonNumber == 0), link.NotInOrder.Count);
        Assert.Empty(link.OrderOnly);
    }

    [Fact]
    public void Without_tmdb_ids_numbers_need_an_air_date_within_two_days_and_titles_link_what_moved()
    {
        var episodes = new List<Episode>
        {
            Make(1, 1, 1, new DateOnly(2020, 1, 1), "Arrival"),
            Make(2, 1, 2, new DateOnly(2020, 1, 8), "Departure"),
            Make(3, 1, 3, new DateOnly(2020, 1, 15), "Lost at Sea"),
            Make(4, 2, 1, new DateOnly(2021, 1, 1), "Homecoming"),
        };
        var order = new List<OrderEpisode>
        {
            // Same numbers, aired two days apart: the same episode.
            Order(1, 1, 1, 1, new DateOnly(2020, 1, 3), "Pilot"),
            // Same numbers, aired three days apart and another title: not it.
            Order(1, 2, 1, 2, new DateOnly(2020, 1, 11), "Something Else"),
            // TVDB's S02E01 is the order's S01E03 (TMDB's own S01E03 too): found on its title and air date.
            Order(1, 3, 1, 3, new DateOnly(2021, 1, 2), "Homecoming"),
            // Only in the order.
            Order(1, 4, 1, 4, new DateOnly(2020, 2, 1), "Bonus"),
        };

        var link = EpisodeOrderLinker.Link(episodes, order);

        Assert.Equal(OrderLinkRule.Numbers, link.Numbers[episodes[0]].Rule);
        Assert.Equal((1, 3), Numbers(link, episodes[3]));
        Assert.Equal(OrderLinkRule.DateAndTitle, link.Numbers[episodes[3]].Rule);
        Assert.Equal([episodes[1], episodes[2]], link.NotInOrder);
        Assert.Equal(["Something Else", "Bonus"], link.OrderOnly.Select(o => o.Title));
    }

    [Fact]
    public async Task The_mapper_falls_back_on_the_order_and_air_dates_and_titles_confirm_or_contradict_a_hit()
    {
        var (series, episodes) = await EpisodesAsync(81189);
        var order = (await Provider.GetOrderEpisodesAsync(BreakingBadTmdb, new OrderChoice(EpisodeOrder.TmdbEpisodeGroup, BreakingBadDvdGroup), CancellationToken.None))!;
        EpisodeOrderLinker.Apply(series, episodes, EpisodeOrderLinker.Link(episodes, order));
        series.Ordering = EpisodeOrder.TmdbEpisodeGroup;
        var mappable = MappableSeries.From(series);

        // TVDB has no season 6: the TVDB pass finds nothing; the order's S06E06 is TVDB's S05E14.
        var parsed = EpisodeParser.ParseTitle("Breaking.Bad.S06E06.Ozymandias.1080p.BluRay.x264-GRP");
        Assert.False(EpisodeMapper.Map(parsed, mappable, episodes, sceneSource: true).IsMapped);
        var byOrder = EpisodeMapper.MapOrder(parsed, mappable, episodes);
        Assert.True(byOrder.IsMapped);
        Assert.Equal(EpisodeNumbering.Order, byOrder.Numbering);
        Assert.Equal((5, 14), (byOrder.Episodes.Single().SeasonNumber, byOrder.Episodes.Single().EpisodeNumber));

        // Its title is in the name: confirmed. Without it: ambiguous (held). A pack of the order: never confirmed.
        Assert.True(OrderFallbackCheck.Verify(parsed!.ReleaseTitle, parsed, byOrder.Episodes).Confirmed);
        var bare = EpisodeParser.ParseTitle("Breaking.Bad.S06E06.1080p.BluRay.x264-GRP")!;
        var bareVerdict = OrderFallbackCheck.Verify(bare.ReleaseTitle, bare, EpisodeMapper.MapOrder(bare, mappable, episodes).Episodes);
        Assert.False(bareVerdict.Confirmed);
        Assert.Null(bareVerdict.Contradiction);
        var pack = EpisodeParser.ParseTitle("Breaking.Bad.S06.1080p.BluRay.x264-GRP")!;
        var packEpisodes = EpisodeMapper.MapOrder(pack, mappable, episodes);
        Assert.Equal(8, packEpisodes.Episodes.Count);
        Assert.False(OrderFallbackCheck.Verify(pack.ReleaseTitle, pack, packEpisodes.Episodes).Confirmed);

        // Releases still map on TVDB first, recorded as such; an air date is the same in every order.
        var tvdb = EpisodeMapper.Map(EpisodeParser.ParseTitle("Breaking.Bad.S05E14.1080p.BluRay.x264-GRP"), mappable, episodes, sceneSource: true);
        Assert.Equal(EpisodeNumbering.Tvdb, tvdb.Numbering);
        Assert.Equal((5, 14), (tvdb.Episodes.Single().SeasonNumber, tvdb.Episodes.Single().EpisodeNumber));
        Assert.False(EpisodeMapper.MapOrder(EpisodeParser.ParseTitle("Breaking.Bad.2013.09.15.1080p.WEB.h264-GRP"), mappable, episodes).IsMapped);

        // An air date in the name far from the episode's contradicts the order's hit.
        var dated = new Bigarr.Parsing.Episodes.Model.ParsedEpisodeInfo { ReleaseTitle = "x", AirDate = "2013-01-01" };
        Assert.NotNull(OrderFallbackCheck.Verify("x", dated, byOrder.Episodes).Contradiction);
        var near = new Bigarr.Parsing.Episodes.Model.ParsedEpisodeInfo { ReleaseTitle = "x", AirDate = byOrder.Episodes[0].AirDate!.Value.AddDays(2).ToString("yyyy-MM-dd") };
        Assert.True(OrderFallbackCheck.Verify("x", near, byOrder.Episodes).Confirmed);
    }

    [Fact]
    public async Task Anime_absolute_numbers_are_shared_and_the_order_absolute_is_the_fallback()
    {
        var (series, episodes) = await EpisodesAsync(424536);
        var order = (await Provider.GetOrderEpisodesAsync(FrierenTmdb, new OrderChoice(EpisodeOrder.TmdbDefault), CancellationToken.None))!;
        EpisodeOrderLinker.Apply(series, episodes, EpisodeOrderLinker.Link(episodes, order));
        series.Ordering = EpisodeOrder.TmdbDefault;
        var mappable = MappableSeries.From(series);

        // "- 29": TVDB's absolute number 29 already names S02E01, recorded as absolute.
        var parsed = EpisodeParser.ParseTitle("[SubsPlease] Sousou no Frieren - 29 (1080p) [ABCD1234].mkv");
        var mapping = EpisodeMapper.Map(parsed, mappable, episodes, sceneSource: true);
        Assert.Equal(EpisodeNumbering.Absolute, mapping.Numbering);
        Assert.Equal((2, 1), (mapping.Episodes.Single().SeasonNumber, mapping.Episodes.Single().EpisodeNumber));

        // In the order, the same number is S01E29: the order fallback finds the same episode.
        var fallback = EpisodeMapper.MapOrder(parsed, mappable, episodes);
        Assert.Same(mapping.Episodes.Single(), fallback.Episodes.Single());
        var seasonal = EpisodeMapper.MapOrder(EpisodeParser.ParseTitle("Sousou.no.Frieren.S01E29.1080p.WEB.H264-GRP"), mappable, episodes);
        Assert.Same(mapping.Episodes.Single(), seasonal.Episodes.Single());
    }

    internal static async Task<(Bigarr.Domain.Tv.Series Series, List<Episode> Episodes)> EpisodesAsync(int tvdbId)
    {
        var metadata = (await Provider.GetByTvdbIdAsync(tvdbId, withEpisodes: true, CancellationToken.None))!;
        var series = new Bigarr.Domain.Tv.Series { Id = 1, Title = metadata.Title, SortTitle = metadata.Title, SeriesType = metadata.SuggestedType, TmdbId = metadata.TmdbId, TvdbId = metadata.TvdbId };
        var id = 0;
        var episodes = metadata.Episodes.Select(e => new Episode
        {
            Id = ++id,
            SeriesId = 1,
            TvdbId = e.TvdbId,
            TmdbId = e.TmdbId,
            SeasonNumber = e.SeasonNumber,
            EpisodeNumber = e.EpisodeNumber,
            AbsoluteEpisodeNumber = e.AbsoluteEpisodeNumber,
            AirDate = e.AirDate,
            Title = e.Title,
        }).ToList();
        return (series, episodes);
    }

    private static Episode Episode(List<Episode> episodes, int season, int number) => episodes.Single(e => e.SeasonNumber == season && e.EpisodeNumber == number);

    private static (int, int) Numbers(OrderLink link, Episode episode) => (link.Numbers[episode].SeasonNumber, link.Numbers[episode].EpisodeNumber);

    private static Episode Make(int id, int season, int number, DateOnly aired, string title) =>
        new() { Id = id, SeasonNumber = season, EpisodeNumber = number, AirDate = aired, Title = title };

    private static OrderEpisode Order(int season, int number, int defaultSeason, int defaultNumber, DateOnly aired, string title) =>
        new() { SeasonNumber = season, EpisodeNumber = number, DefaultSeasonNumber = defaultSeason, DefaultEpisodeNumber = defaultNumber, AirDate = aired, Title = title };
}
