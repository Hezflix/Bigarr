using System.Web;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Infrastructure.Indexers.Torznab;
using Bigarr.Api.Infrastructure.Tv;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Tv;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bigarr.Api.Tests.Indexers;

using Series = Bigarr.Domain.Tv.Series;

/// <summary>Sonarr's request tiers per series type, the series categories, the caps' tv-search and XEM numbering in the queries.</summary>
public sealed class SeriesQueryTests
{
    private static readonly IndexerCapabilities Full = TorznabParser.ParseCapabilities(AnimeCaps);

    private static readonly IReadOnlyList<int> Tv = [5000, 5040];

    private const string AnimeCaps = """
        <?xml version="1.0" encoding="UTF-8"?>
        <caps>
          <searching>
            <search available="yes" supportedParams="q" />
            <tv-search available="yes" supportedParams="q,season,ep,tvdbid,tmdbid,imdbid" />
            <movie-search available="yes" supportedParams="q,imdbid,tmdbid" />
          </searching>
          <categories>
            <category id="2000" name="Movies"><subcat id="2040" name="Movies/HD" /></category>
            <category id="5000" name="TV">
              <subcat id="5030" name="TV/SD" />
              <subcat id="5040" name="TV/HD" />
              <subcat id="5070" name="TV/Anime" />
            </category>
          </categories>
        </caps>
        """;

    private static SeriesSearchCriteria Show(SeriesType type = SeriesType.Standard) =>
        new("Breaking Bad", 81189, 1396, "tt0903747", type, ["Breaking Bad US"]);

    [Fact]
    public void Caps_give_tv_search_parameters_and_tv_and_anime_categories_apart()
    {
        Assert.True(Full.SupportsTvSearch);
        Assert.True(Full.SupportsTvParam("tvdbid"));
        Assert.True(Full.SupportsTvParam("season"));
        Assert.Equal([5000, 5030, 5040], Full.TvCategoryIds);
        Assert.Equal([5070], Full.AnimeCategoryIds);

        var recorded = TorznabParser.ParseCapabilities(TorznabRecorded.Caps);
        Assert.True(recorded.SupportsTvParam("imdbid"));
        Assert.False(recorded.SupportsTvParam("tmdbid"));

        var noTv = TorznabParser.ParseCapabilities(TorznabRecorded.CapsNoMovieSearch);
        Assert.False(noTv.SupportsTvSearch);
    }

    [Fact]
    public void Series_categories_come_from_the_row_then_the_caps_then_sonarr_defaults_and_anime_adds_its_own()
    {
        var pushedByProwlarr = new Indexer { Name = "p", Categories = [2000, 2040] };
        Assert.Equal([5000, 5030, 5040], SeriesQueryBuilder.Categories(pushedByProwlarr, Full, SeriesType.Standard));
        Assert.Equal([5000, 5030, 5040, 5070], SeriesQueryBuilder.Categories(pushedByProwlarr, Full, SeriesType.Anime));

        var edited = new Indexer { Name = "e", TvCategories = [5040], AnimeCategories = [5070, 100070] };
        Assert.Equal([5040], SeriesQueryBuilder.Categories(edited, Full, SeriesType.Daily));
        Assert.Equal([5040, 5070, 100070], SeriesQueryBuilder.Categories(edited, Full, SeriesType.Anime));

        var bare = new IndexerCapabilities(false, false, false, false, [], null);
        Assert.Equal(Indexer.DefaultTvCategories, SeriesQueryBuilder.Categories(new Indexer { Name = "b" }, bare, SeriesType.Standard));
    }

    [Fact]
    public void Standard_episode_asks_by_ids_with_season_and_episode_then_by_titles()
    {
        var tiers = SeriesQueryBuilder.Build(Full, Show() with { SeasonNumber = 1, EpisodeNumber = 5 }, Tv);

        Assert.Equal(2, tiers.Count);
        var ids = Assert.Single(tiers[0]).Parameters;
        Assert.Equal("tvsearch", ids["t"]);
        Assert.Equal("81189", ids["tvdbid"]);
        Assert.Equal("1396", ids["tmdbid"]);
        Assert.Equal("tt0903747", ids["imdbid"]);
        Assert.Equal("1", ids["season"]);
        Assert.Equal("5", ids["ep"]);
        Assert.False(ids.ContainsKey("q"));

        Assert.Equal(["Breaking Bad", "Breaking Bad US"], tiers[1].Select(r => r.Parameters["q"]));
        Assert.All(tiers[1], r => Assert.Equal("tvsearch", r.Parameters["t"]));
        Assert.All(tiers[1], r => Assert.Equal("5", r.Parameters["ep"]));
        Assert.All(tiers.SelectMany(t => t), r => Assert.Equal(Tv, r.Categories));
    }

    [Fact]
    public void Season_search_asks_the_season_only_and_series_search_nothing_but_the_show()
    {
        var season = SeriesQueryBuilder.Build(Full, Show() with { SeasonNumber = 2 }, Tv);
        Assert.Equal("2", season[0][0].Parameters["season"]);
        Assert.False(season[0][0].Parameters.ContainsKey("ep"));

        var series = SeriesQueryBuilder.Build(Full, Show(), Tv);
        Assert.False(series[0][0].Parameters.ContainsKey("season"));
        Assert.Equal("81189", series[0][0].Parameters["tvdbid"]);
    }

    [Fact]
    public void Daily_episode_is_asked_by_air_date()
    {
        var tiers = SeriesQueryBuilder.Build(Full, new SeriesSearchCriteria("The Daily Show", 71256, null, null, SeriesType.Daily, []) { AirDate = new DateOnly(2026, 1, 5) }, Tv);

        var ids = tiers[0][0].Parameters;
        Assert.Equal("2026", ids["season"]);
        Assert.Equal("01/05", ids["ep"]);
        Assert.Equal("The Daily Show", tiers[1][0].Parameters["q"]);
        Assert.Equal("01/05", tiers[1][0].Parameters["ep"]);
    }

    [Fact]
    public void Anime_episode_is_asked_by_ids_and_as_title_plus_absolute_number_in_the_first_tier()
    {
        var criteria = new SeriesSearchCriteria("Frieren: Beyond Journey's End", 424536, null, null, SeriesType.Anime, ["Sousou no Frieren"])
        {
            SeasonNumber = 2,
            EpisodeNumber = 5,
            AbsoluteEpisodeNumber = 33,
        };
        var tiers = SeriesQueryBuilder.Build(Full, criteria, [5000, 5070]);

        var first = tiers[0];
        Assert.Equal("tvsearch", first[0].Parameters["t"]);
        Assert.Equal("424536", first[0].Parameters["tvdbid"]);
        Assert.Equal(["Frieren Beyond Journey s End 33", "Sousou no Frieren 33"], first.Skip(1).Select(r => r.Parameters["q"]));
        Assert.All(first.Skip(1), r => Assert.Equal("search", r.Parameters["t"]));

        // Under an XEM season name the episode counts from 1 and the request carries no ids.
        var sceneName = SeriesQueryBuilder.Build(Full, new SeriesSearchCriteria("Sousou no Frieren S2", null, null, null, SeriesType.Anime, []) { AbsoluteEpisodeNumber = 5 }, [5070]);
        Assert.Equal("Sousou no Frieren S2 05", Assert.Single(Assert.Single(sceneName)).Parameters["q"]);
    }

    [Fact]
    public void An_indexer_without_tv_search_gets_text_queries_with_the_numbers_written_like_release_names()
    {
        var textOnly = TorznabParser.ParseCapabilities(TorznabRecorded.CapsTextOnly);
        var tiers = SeriesQueryBuilder.Build(textOnly, Show() with { SeasonNumber = 1, EpisodeNumber = 5 }, Tv);

        var only = Assert.Single(tiers);
        Assert.Equal(["Breaking Bad S01E05", "Breaking Bad US S01E05"], only.Select(r => r.Parameters["q"]));
        Assert.All(only, r => Assert.Equal("search", r.Parameters["t"]));
    }

    [Fact]
    public void Xem_scene_numbers_are_what_the_indexers_are_asked()
    {
        var series = new Series { Title = "Sousou no Frieren", SortTitle = "x", TvdbId = 424536, SeriesType = SeriesType.Anime, UseSceneNumbering = true };
        var episode = new Episode { Title = "e", SeasonNumber = 1, EpisodeNumber = 33, AbsoluteEpisodeNumber = 33, SceneSeasonNumber = 2, SceneEpisodeNumber = 5, SceneAbsoluteEpisodeNumber = 33 };
        var names = new List<SceneAlias> { new(424536, "Sousou no Frieren", null), new(424536, "Sousou no Frieren S2", 2) };

        var criteria = SeriesSearchCriteriaBuilder.ForEpisode(series, episode, names);
        Assert.Equal(2, criteria[0].SeasonNumber);
        Assert.Equal(5, criteria[0].EpisodeNumber);
        Assert.Equal(33, criteria[0].AbsoluteEpisodeNumber);
        var seasonName = criteria[1];
        Assert.Equal("Sousou no Frieren S2", seasonName.Title);
        Assert.Equal(5, seasonName.AbsoluteEpisodeNumber);

        var standard = new Series { Title = "Show", SortTitle = "show", TvdbId = 1, UseSceneNumbering = true };
        var mapped = new Episode { Title = "e", SeasonNumber = 1, EpisodeNumber = 12, SceneSeasonNumber = 2, SceneEpisodeNumber = 1 };
        var asked = Assert.Single(SeriesSearchCriteriaBuilder.ForEpisode(standard, mapped, []));
        Assert.Equal((2, 1), (asked.SeasonNumber!.Value, asked.EpisodeNumber!.Value));

        // Without an XEM mapping the TVDB numbers go out.
        standard.UseSceneNumbering = false;
        var plain = Assert.Single(SeriesSearchCriteriaBuilder.ForEpisode(standard, mapped, []));
        Assert.Equal((1, 12), (plain.SeasonNumber!.Value, plain.EpisodeNumber!.Value));
    }

    [Fact]
    public async Task Titles_are_only_asked_when_the_ids_find_nothing_and_every_request_is_paced()
    {
        var paced = 0;
        var handler = new StubHttpHandler(request =>
        {
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            if (query["t"] == "caps")
            {
                return StubHttpHandler.Xml(AnimeCaps);
            }

            return StubHttpHandler.Xml(query["tvdbid"] is null ? TorznabRecorded.Feed : TorznabRecorded.EmptyFeed);
        });
        var client = new TorznabClient(new StubHttpClientFactory(handler), new FixedTimeProvider(DateTimeOffset.UtcNow), NullLogger<TorznabClient>.Instance);
        var indexer = new Indexer { Id = 3, Name = "p", BaseUrl = "http://prowlarr:9696/3", ApiKey = "k", Categories = [2000] };

        var releases = await client.SearchSeriesAsync(indexer, Show() with { SeasonNumber = 1, EpisodeNumber = 5 }, _ => { paced++; return Task.CompletedTask; }, CancellationToken.None);

        Assert.Equal(2, releases.Count);
        Assert.Equal(4, handler.Requests.Count); // caps, ids (empty), two titles
        Assert.Equal(4, paced);
        var idsRequest = HttpUtility.ParseQueryString(handler.Requests[1].Query);
        Assert.Equal("5000,5030,5040", idsRequest["cat"]);

        // The ids answer: no title request.
        var answering = new StubHttpHandler(request => StubHttpHandler.Xml(HttpUtility.ParseQueryString(request.RequestUri!.Query)["t"] == "caps" ? AnimeCaps : TorznabRecorded.Feed));
        var second = new TorznabClient(new StubHttpClientFactory(answering), new FixedTimeProvider(DateTimeOffset.UtcNow), NullLogger<TorznabClient>.Instance);
        await second.SearchSeriesAsync(indexer, Show() with { SeasonNumber = 1, EpisodeNumber = 5 }, _ => Task.CompletedTask, CancellationToken.None);
        Assert.Equal(2, answering.Requests.Count);
    }
}
