using Bigarr.Api.Features.Playback;
using Bigarr.Domain.Metadata;
using static Bigarr.Api.Features.Playback.LinkSuggestions;

namespace Bigarr.Api.Tests.Playback;

/// <summary>The link picker's suggestions, ranked: ids first, then TMDB and the library's titles, close titles last.</summary>
public sealed class LinkSuggestionsTests
{
    private static readonly Film Signal = new(1, 5001, "tt5000001", "Distant Signal", "Le Signal lointain", 2008, []);
    private static readonly Film SignalRemake = new(2, 5002, null, "Distant Signal", null, 2021, []);
    private static readonly Film Echo = new(3, 5003, null, "Crimson Echo", null, 2012, ["L’Écho pourpre"]);
    private static readonly Film Tide = new(4, 5004, null, "Burning Tide", null, 1997, []);
    private static readonly Film Atlas = new(5, 5005, null, "Distant Atlas", null, 1987, []);
    private static readonly Film[] Library = [Signal, SignalRemake, Echo, Tide, Atlas];

    private static MovieMetadata Hit(int tmdbId, int? year) => new() { TmdbId = tmdbId, Title = "Any", Year = year };

    [Fact]
    public void The_same_tmdb_id_comes_first_even_against_a_title_and_year()
    {
        var item = new ItemFacts("Distant Signal", null, 2021, TmdbId: 5001, ImdbId: null);
        var ranked = Rank(item, Library, []);
        Assert.Equal([1, 2], ranked.Select(r => r.MovieId));
        Assert.Equal(SuggestionReason.SameTmdbId, ranked[0].Reasons[0]);
        Assert.Equal([SuggestionReason.TitleAndYear], ranked[1].Reasons);
    }

    [Fact]
    public void An_imdb_id_counts_when_there_is_no_tmdb_id()
    {
        var ranked = Rank(new ItemFacts("Something else", null, null, null, "TT5000001"), Library, []);
        Assert.Equal(Signal.Id, ranked.Single().MovieId);
        Assert.Equal([SuggestionReason.SameImdbId], ranked[0].Reasons);
    }

    [Fact]
    public void Titles_match_without_articles_accents_or_punctuation_and_the_year_tells_a_remake_apart()
    {
        // The item's title is the film's original title, articles and case aside.
        var ranked = Rank(new ItemFacts("SIGNAL LOINTAIN (le)", null, 2008, null, null), Library, []);
        Assert.Equal(Signal.Id, ranked[0].MovieId);
        Assert.Equal([SuggestionReason.TitleAndYear], ranked[0].Reasons);

        // An alternative title, with an apostrophe and accents of its own.
        var echo = Rank(new ItemFacts("L'Echo Pourpre!", null, 2012, null, null), Library, []);
        Assert.Equal(Echo.Id, echo.Single().MovieId);

        // Same title, two films: the item's year first, the other one still offered, lower.
        var remake = Rank(new ItemFacts("Distant Signal", null, 2021, null, null), Library, []);
        Assert.Equal([2, 1], remake.Select(r => r.MovieId).Take(2));
        Assert.Equal(SuggestionReason.SameTitle, remake[1].Reasons[0]);
        Assert.True(remake[0].Score > remake[1].Score);
    }

    [Fact]
    public void The_original_title_of_the_item_is_used_too()
    {
        var ranked = Rank(new ItemFacts("La Marée brûlante", "Burning Tide", 1997, null, null), Library, []);
        Assert.Equal(Tide.Id, ranked.Single().MovieId);
        Assert.Equal([SuggestionReason.TitleAndYear], ranked[0].Reasons);
    }

    [Fact]
    public void A_tmdb_hit_counts_only_for_a_film_of_the_library_and_the_year_ranks_it()
    {
        var item = new ItemFacts("Le Phare", null, 2004, null, null);
        var ranked = Rank(item, Library, [Hit(9999, 2004), Hit(5005, 1987), Hit(5004, 2004)]);
        Assert.Equal([Tide.Id, Atlas.Id], ranked.Select(r => r.MovieId));
        Assert.All(ranked, r => Assert.Equal([SuggestionReason.TmdbSearch], r.Reasons));
    }

    [Fact]
    public void A_close_title_is_suggested_below_the_sure_ones_and_reasons_add_up()
    {
        var item = new ItemFacts("Distant Signall", null, 2008, null, null);
        var ranked = Rank(item, Library, [Hit(5001, 2008)]);
        Assert.Equal(Signal.Id, ranked[0].MovieId);
        Assert.Equal([SuggestionReason.TmdbSearch, SuggestionReason.CloseTitle], ranked[0].Reasons);
        var close = Rank(item, Library, []);
        Assert.Equal([1, 2], close.Select(r => r.MovieId));
        Assert.All(close, r => Assert.Equal([SuggestionReason.CloseTitle], r.Reasons));
        Assert.True(ranked[0].Score > close[0].Score);
        Assert.DoesNotContain(close, r => r.MovieId == Atlas.Id);
    }

    [Fact]
    public void Nothing_alike_suggests_nothing_and_never_more_than_five()
    {
        Assert.Empty(Rank(new ItemFacts("Completely unrelated", null, 1950, null, null), Library, []));
        var many = Enumerable.Range(100, 9).Select(i => new Film(i, i, null, "Same Title", null, 2000, [])).ToList();
        Assert.Equal(MaxSuggestions, Rank(new ItemFacts("Same Title", null, 2000, null, null), many, []).Count);
    }

    [Theory]
    [InlineData("L’Écho pourpre !", "echo pourpre")]
    [InlineData("The Matrix", "matrix")]
    [InlineData("Les", "les")]
    [InlineData("Amélie: Part 2", "amelie part 2")]
    public void Titles_fold_to_their_words(string title, string folded) => Assert.Equal(folded, Fold(title));
}
