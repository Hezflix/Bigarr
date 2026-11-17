using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Tv;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.Movies;
using EpisodeParser = Bigarr.Parsing.Episodes.Parser;

namespace Bigarr.Api.Tests.Series;

/// <summary>
/// Year twins: "Scrubs" (2001, TVDB 76156) and its revival, which TVDB names "Scrubs (2026)" (465690) and Sonarr knows
/// by that clean title. TVDB gives each the other's name as an alias, so both answer "Scrubs" in Bigarr. A release that
/// names the year is the revival, whatever ids the feed put on it; one that does not stays ambiguous unless an id
/// picks a twin, or a search for the show whose own title it is.
/// </summary>
public sealed class SeriesYearTwinMatchTests
{
    private static readonly IndexedSeries Scrubs = new(1, 76156, "Scrubs", 2001, ["Scrubs (2001)"], "tt0285403");
    private static readonly IndexedSeries Revival = new(2, 465690, "Scrubs", 2026, ["Scrubs"], "tt40197357");
    private static readonly IndexedSeries RevivalByTvdbName = Revival with { Title = "Scrubs (2026)" };

    private static SeriesMatch Find(SeriesTitleIndex index, string title, int? tvdbId = null, string? imdbId = null) =>
        index.Find(EpisodeParser.ParseTitle(title)!, tvdbId, imdbId);

    public static TheoryData<string> RevivalNames => new()
    {
        "Scrubs.2026.S01E05.1080p.WEB.h264-GRP",
        "Scrubs (2026) S01E05 1080p WEB h264-GRP",
        "Scrubs.(2026).S01E05.1080p.WEB.h264-GRP",
        "Scrubs 2026 S01E05 1080p",
        "Scrubs.2026.S01.1080p.WEB.h264-GRP",
        "Scrubs.2026.S01E05.MULTi.1080p.WEB.h264-GRP",
    };

    [Theory]
    [MemberData(nameof(RevivalNames))]
    public void A_name_with_the_year_is_the_revival_by_title(string release)
    {
        foreach (var revival in new[] { Revival, RevivalByTvdbName })
        {
            var match = Find(new SeriesTitleIndex([Scrubs, revival], []), release);

            Assert.Equal(Revival.Id, match.Series?.Id);
            Assert.Equal(SeriesMatchKind.Title, match.Kind);
        }
    }

    [Theory]
    [MemberData(nameof(RevivalNames))]
    public void A_name_with_the_year_is_the_revival_even_when_the_feed_tags_it_with_the_old_shows_ids(string release)
    {
        var index = new SeriesTitleIndex([Scrubs, Revival], []);

        Assert.Equal(Revival.Id, Find(index, release, Scrubs.TvdbId).Series?.Id);
        Assert.Equal(Revival.Id, Find(index, release, imdbId: Scrubs.ImdbId).Series?.Id);
    }

    [Fact]
    public void A_name_with_another_year_never_falls_back_to_the_old_show_by_id()
    {
        // The revival is not in the library, or its year is unknown: the old show's ids do not make it the old show.
        foreach (var index in new[] { new SeriesTitleIndex([Scrubs], []), new SeriesTitleIndex([Scrubs, Revival with { Year = null }], []) })
        {
            var byTvdb = Find(index, "Scrubs.2026.S01E05.1080p.WEB.h264-GRP", Scrubs.TvdbId);
            var byImdb = Find(index, "Scrubs.2026.S01E05.1080p.WEB.h264-GRP", imdbId: Scrubs.ImdbId);

            Assert.NotEqual(Scrubs.Id, byTvdb.Series?.Id);
            Assert.NotEqual(Scrubs.Id, byImdb.Series?.Id);
        }

        // The revival's own id still finds it when its year is unknown.
        Assert.Equal(Revival.Id, Find(new SeriesTitleIndex([Scrubs, Revival with { Year = null }], []), "Scrubs.2026.S01E05.1080p.WEB.h264-GRP", Revival.TvdbId).Series?.Id);
    }

    [Fact]
    public void A_year_off_by_one_is_metadata_drift_and_the_id_still_stands()
    {
        var show = new IndexedSeries(7, 500001, "Some Show", 2019, []);

        var match = Find(new SeriesTitleIndex([show], []), "Some.Show.2020.S01E01.1080p.WEB.h264-GRP", show.TvdbId);

        Assert.Equal(show.Id, match.Series?.Id);
        Assert.Equal(SeriesMatchKind.Id, match.Kind);
    }

    [Fact]
    public void A_name_without_the_year_is_ambiguous_unless_an_id_picks_a_twin()
    {
        var index = new SeriesTitleIndex([Scrubs, Revival], []);
        const string release = "Scrubs.S01E05.1080p.WEB.h264-GRP";

        var bare = Find(index, release);
        Assert.Null(bare.Series);
        Assert.True(bare.IsAmbiguous);
        Assert.Equal(["Scrubs (2001)", "Scrubs (2026)"], bare.Ambiguous.Select(SeriesTitleIndex.Label).Order());

        Assert.Equal(Revival.Id, Find(index, release, Revival.TvdbId).Series?.Id);
        Assert.Equal(Scrubs.Id, Find(index, release, Scrubs.TvdbId).Series?.Id);
        Assert.Equal(Scrubs.Id, Find(index, "Scrubs.2001.S01E05.DVDRip.x264-GRP").Series?.Id);
    }

    [Fact]
    public void The_longest_start_of_an_unreadable_name_picks_the_twin_by_its_year()
    {
        var index = new SeriesTitleIndex([Scrubs, Revival], []);

        Assert.Equal(Revival.Id, index.FindInexact("Scrubs.2026.Special.1080p.WEB.h264-GRP")?.Id);
        Assert.Equal(Scrubs.Id, index.FindInexact("Scrubs.2001.Behind.The.Scenes.DVDRip.x264-GRP")?.Id);
        Assert.Null(index.FindInexact("Scrubs.Behind.The.Scenes.1080p.WEB.h264-GRP"));
    }

    [Fact]
    public void A_special_is_found_by_its_name_before_the_feeds_ids()
    {
        var index = new SeriesTitleIndex([Scrubs, Revival], []);

        // Sonarr's ParseSpecialEpisodeTitle: the name, the start of the title, then the ids.
        Assert.Equal(Revival.Id, index.FindShowOfSpecial(EpisodeParser.ParseTitle("Scrubs.2026.Special.1080p.WEB.h264-GRP"), "Scrubs.2026.Special.1080p.WEB.h264-GRP", Scrubs.TvdbId, null)?.Id);
        Assert.Equal(Revival.Id, index.FindShowOfSpecial(null, "Scrubs.2026.Special.1080p.WEB.h264-GRP", Scrubs.TvdbId, null)?.Id);
        Assert.Equal(Scrubs.Id, index.FindShowOfSpecial(null, "Scrubs.Special.1080p.WEB.h264-GRP", Scrubs.TvdbId, null)?.Id);
    }

    [Fact]
    public void An_xem_name_of_the_old_show_does_not_take_the_revivals_year_name()
    {
        var index = new SeriesTitleIndex([Scrubs, Revival], [new SceneAlias(76156, "Scrubs", null)]);

        Assert.Equal(Revival.Id, Find(index, "Scrubs.2026.S01E05.1080p.WEB.h264-GRP").Series?.Id);
        // The scene's own spelling wins, as Sonarr's scene mappings do.
        Assert.Equal(Scrubs.Id, Find(index, "Scrubs.S01E05.1080p.WEB.h264-GRP").Series?.Id);
    }

    [Fact]
    public void A_search_for_one_twin_takes_a_bare_name_only_when_it_is_its_own_title()
    {
        // Sonarr's search rule: the searched show's clean title, or its id; "Scrubs" is the 2001 show's title, an alias
        // of "Scrubs (2026)".
        var index = new SeriesTitleIndex([Scrubs, RevivalByTvdbName], []);
        var bare = EpisodeParser.ParseTitle("Scrubs.S01E05.1080p.WEB.h264-GRP")!;
        var withYear = EpisodeParser.ParseTitle("Scrubs.2026.S01E05.1080p.WEB.h264-GRP")!;

        Assert.Equal(Scrubs.Id, index.FindFor(Scrubs, bare).Series?.Id);
        Assert.Null(index.FindFor(RevivalByTvdbName, bare).Series);
        Assert.Equal(Revival.Id, index.FindFor(RevivalByTvdbName, bare, RevivalByTvdbName.TvdbId).Series?.Id);
        Assert.Equal(Revival.Id, index.FindFor(RevivalByTvdbName, withYear).Series?.Id);

        var other = index.FindFor(Scrubs, withYear);
        Assert.Null(other.Series);
        Assert.Equal([RevivalByTvdbName.Id], other.Ambiguous.Select(s => s.Id));

        // Both twins titled "Scrubs": a bare name cannot tell them apart without an id.
        var same = new SeriesTitleIndex([Scrubs, Revival], []);
        Assert.Null(same.FindFor(Scrubs, bare).Series);
        Assert.Equal(Scrubs.Id, same.FindFor(Scrubs, bare, Scrubs.TvdbId).Series?.Id);
    }

    [Fact]
    public async Task A_search_index_holds_the_show_and_its_twins_only()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        var root = new Bigarr.Domain.Library.RootFolder { Path = "/tv", Kind = Bigarr.Domain.Library.MediaKind.Series };
        var scrubs = new Bigarr.Domain.Tv.Series { Title = "Scrubs", SortTitle = "scrubs", Year = 2001, TvdbId = 76156, DefaultRootFolder = root, AlternativeTitles = [new AlternativeTitle("Scrubs (2001)", null)] };
        var revival = new Bigarr.Domain.Tv.Series { Title = "Scrubs (2026)", SortTitle = "scrubs 2026", Year = 2026, TvdbId = 465690, OriginalTitle = "Scrubs", DefaultRootFolder = root };
        var other = new Bigarr.Domain.Tv.Series { Title = "Crumbs", SortTitle = "crumbs", Year = 2006, TvdbId = 75743, DefaultRootFolder = root };
        db.Series.AddRange(scrubs, revival, other);
        await db.SaveChangesAsync();

        var self = new IndexedSeries(revival.Id, revival.TvdbId, revival.Title, revival.Year, ["Scrubs"]);
        var index = await SeriesTargetsFactory.IndexForAsync(db, self, [], CancellationToken.None);

        Assert.Equal([scrubs.Id, revival.Id], index.All.Select(s => s.Id).Order());
        Assert.Null(index.FindFor(self, EpisodeParser.ParseTitle("Scrubs.S01E05.1080p.WEB.h264-GRP")!).Series);
        Assert.Equal(revival.Id, index.FindFor(self, EpisodeParser.ParseTitle("Scrubs.2026.S01E05.1080p.WEB.h264-GRP")!).Series?.Id);
    }
}
