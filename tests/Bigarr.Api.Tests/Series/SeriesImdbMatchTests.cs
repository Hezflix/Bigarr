using Bigarr.Api.Features.Search;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.Tv;
using Bigarr.Api.Tests.Search;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;
using EpisodeParser = Bigarr.Parsing.Episodes.Parser;

namespace Bigarr.Api.Tests.Series;

/// <summary>
/// Sonarr's id fallback (<c>ParsingService.FindSeries</c>): a release whose name finds no series of the library is
/// matched by its TVDB id, then, when it carries no TVDB id, by its IMDb id. A name that matched always wins.
/// </summary>
public sealed class SeriesImdbMatchTests
{
    private static readonly IndexedSeries Sisters = new(1, 334231, "The Sisters", 2020, [], "tt12363714");
    private static readonly IndexedSeries Other = new(2, 400001, "Les Sisters", 2019, [], "tt0000002");
    private static readonly IndexedSeries Third = new(3, 400002, "Another Show", 2018, [], "tt0000003");

    private static SeriesMatch Find(SeriesTitleIndex index, string title, int? tvdbId, string? imdbId) =>
        index.Find(EpisodeParser.ParseTitle(title)!, tvdbId, imdbId);

    [Fact]
    public void A_foreign_title_with_an_imdb_id_and_no_tvdb_id_finds_the_show()
    {
        var index = new SeriesTitleIndex([Sisters, Third], []);

        var match = Find(index, "Les.Sisters.S03.VFF.1080p.WEB.EAC3.2.0.H265-GRP", null, "tt12363714");

        Assert.Equal(Sisters.Id, match.Series?.Id);
        Assert.Equal(SeriesMatchKind.ImdbId, match.Kind);
        Assert.True(match.IsById);
    }

    [Theory]
    [InlineData("12363714")]
    [InlineData("tt12363714")]
    [InlineData("TT12363714")]
    public void The_imdb_id_is_compared_normalized(string imdbId)
    {
        var index = new SeriesTitleIndex([Sisters], []);

        Assert.Equal(Sisters.Id, index.ByImdbId(imdbId)?.Id);
    }

    [Fact]
    public void The_tvdb_id_wins_over_the_imdb_id()
    {
        var index = new SeriesTitleIndex([Sisters, Third], []);

        var match = Find(index, "Les.Sisters.S03E01.1080p.WEB.H264-GRP", Third.TvdbId, Sisters.ImdbId);

        Assert.Equal(Third.Id, match.Series?.Id);
        Assert.Equal(SeriesMatchKind.Id, match.Kind);
    }

    [Fact]
    public void A_tvdb_id_outside_the_library_turns_the_imdb_id_off()
    {
        // Sonarr only looks at the IMDb id when the release carries no TVDB id.
        var index = new SeriesTitleIndex([Sisters], []);

        var match = Find(index, "Les.Sisters.S03E01.1080p.WEB.H264-GRP", 999999, Sisters.ImdbId);

        Assert.Null(match.Series);
    }

    [Fact]
    public void Without_ids_a_foreign_title_stays_unmatched()
    {
        var index = new SeriesTitleIndex([Sisters], []);

        Assert.Null(Find(index, "Les.Sisters.S03E01.1080p.WEB.H264-GRP", null, null).Series);
        Assert.Null(Find(index, "Les.Sisters.S03E01.1080p.WEB.H264-GRP", null, "tt9999999").Series);
    }

    [Fact]
    public void A_title_match_is_never_overridden_by_an_id()
    {
        var index = new SeriesTitleIndex([Sisters, Other, Third], []);

        // The name finds "Les Sisters": neither the other show's IMDb id nor its TVDB id moves the release.
        var byImdb = Find(index, "Les.Sisters.S03E01.1080p.WEB.H264-GRP", null, Sisters.ImdbId);
        var byTvdb = Find(index, "Les.Sisters.S03E01.1080p.WEB.H264-GRP", Sisters.TvdbId, null);
        var plain = Find(index, "The.Sisters.S03E01.1080p.WEB.H264-GRP", null, Third.ImdbId);

        Assert.Equal((Other.Id, SeriesMatchKind.Title), (byImdb.Series?.Id, byImdb.Kind));
        Assert.Equal((Other.Id, SeriesMatchKind.Title), (byTvdb.Series?.Id, byTvdb.Kind));
        Assert.Equal((Sisters.Id, SeriesMatchKind.Title), (plain.Series?.Id, plain.Kind));
    }

    [Fact]
    public void An_imdb_id_two_series_share_points_to_neither()
    {
        var twin = Third with { ImdbId = Sisters.ImdbId };
        var index = new SeriesTitleIndex([Sisters, twin], []);

        Assert.Null(index.ByImdbId(Sisters.ImdbId));
        Assert.Null(Find(index, "Les.Sisters.S03E01.1080p.WEB.H264-GRP", null, Sisters.ImdbId).Series);
    }

    [Fact]
    public void An_id_only_picks_among_the_series_an_ambiguous_title_names()
    {
        var first = new IndexedSeries(10, 500010, "Doctor Who", 1963, [], "tt0056751");
        var second = new IndexedSeries(11, 500011, "Doctor Who", 2005, [], "tt0436992");
        var elsewhere = new IndexedSeries(12, 500012, "Torchwood", 2006, [], "tt0485301");
        var index = new SeriesTitleIndex([first, second, elsewhere], []);

        Assert.Equal(second.Id, Find(index, "Doctor.Who.S01E01.720p.HDTV-GRP", null, second.ImdbId).Series?.Id);
        Assert.Equal(first.Id, Find(index, "Doctor.Who.S01E01.720p.HDTV-GRP", first.TvdbId, null).Series?.Id);
        Assert.True(Find(index, "Doctor.Who.S01E01.720p.HDTV-GRP", null, elsewhere.ImdbId).IsAmbiguous);
    }
}

/// <summary>The RSS pass on a hand-made feed: a release named in another language finds its show by its IMDb id.</summary>
public sealed class SeriesRssImdbMatchTests(SeriesApiFactory factory) : IClassFixture<SeriesApiFactory>
{
    [Fact]
    public async Task An_rss_release_with_a_foreign_title_matches_by_imdb_id_and_one_without_ids_does_not()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        Assert.Equal("tt0903747", series.ImdbId);
        await SeriesSetup.MockIndexerAsync(client);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var indexer = await db.Indexers.AsNoTracking().FirstAsync();
        ReleaseInfo Release(string title, string? imdbId) => new()
        {
            Guid = $"imdb://{title}",
            Title = title,
            IndexerId = indexer.Id,
            IndexerName = indexer.Name,
            Protocol = DownloadProtocol.Torrent,
            Size = 1_500_000_000,
            InfoHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(title))),
            Seeders = 40,
            PublishDate = SeriesEndpointsTests.Now,
            Categories = [5040],
            ImdbId = imdbId,
        };

        const string byImdb = "Le.Chimiste.S05E14.FRENCH.1080p.WEB.H264-GRP";
        const string noIds = "Le.Chimiste.S05E15.FRENCH.1080p.WEB.H264-GRP";
        await scope.ServiceProvider.GetRequiredService<SeriesRssPass>().RunAsync(
            [(indexer, Release(byImdb, "tt0903747")), (indexer, Release(noIds, null))],
            SeriesEndpointsTests.Now.UtcDateTime,
            new HashSet<string>(),
            CancellationToken.None);

        var entries = scope.ServiceProvider.GetRequiredService<RssFeedState>().Releases(null, RssFeedState.MaxReleases);
        var matched = entries.Single(e => e.Title == byImdb);
        Assert.Equal(series.Id, matched.Series?.Id);
        Assert.Contains("S05E14", matched.Episodes, StringComparison.Ordinal);

        var unmatched = entries.Single(e => e.Title == noIds);
        Assert.Null(unmatched.Series);
        Assert.Equal("No series in the library matches 'Le Chimiste'", unmatched.UnmatchedReason);
    }
}
