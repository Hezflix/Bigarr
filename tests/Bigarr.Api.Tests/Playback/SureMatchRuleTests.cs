using Bigarr.Api.Features.Playback;
using Bigarr.Domain.Metadata;
using static Bigarr.Api.Features.Playback.LinkSuggestions;

namespace Bigarr.Api.Tests.Playback;

/// <summary>When the picker's best film is sure enough to be linked from the bulk list, and when it is left for the picker.</summary>
public sealed class SureMatchRuleTests
{
    private static readonly Film Signal = new(1, 5001, "tt5000001", "Distant Signal", null, 2008, []);
    private static readonly Film Harbor = new(2, 5002, null, "Night Harbor", null, 2015, []);
    private static readonly Film HarborDocumentary = new(3, 5003, null, "Night Harbor", null, 2015, []);
    private static readonly Film Tide = new(4, 5004, "tt5000004", "Burning Tide", null, 1997, []);
    private static readonly Film[] Library = [Signal, Harbor, HarborDocumentary, Tide];

    private static MovieMetadata Hit(int tmdbId, int? year) => new() { TmdbId = tmdbId, Title = "Any", Year = year };

    private static LinkSuggestion? SureOf(ItemFacts item, params MovieMetadata[] hits) => Sure(item, Library, Rank(item, Library, hits));

    [Fact]
    public void A_title_in_another_language_found_by_tmdb_with_the_same_year_is_sure()
    {
        var sure = SureOf(new ItemFacts("Le Signal lointain", null, 2008, null, null), Hit(5001, 2008));
        Assert.Equal(Signal.Id, sure?.MovieId);
        Assert.Equal([SuggestionReason.TmdbSearch], sure!.Reasons);
    }

    [Fact]
    public void The_same_title_and_year_is_sure_when_no_other_film_comes_close()
    {
        Assert.Equal(Tide.Id, SureOf(new ItemFacts("The Burning Tide", null, 1997, null, null))?.MovieId);
    }

    [Fact]
    public void A_year_off_by_one_is_not_sure_whatever_found_it()
    {
        Assert.Null(SureOf(new ItemFacts("Le Signal lointain", null, 2009, null, null), Hit(5001, 2008)));
        Assert.Null(SureOf(new ItemFacts("Distant Signal", null, 2009, null, null), Hit(5001, 2008)));

        // Nor is an item without a year.
        Assert.Null(SureOf(new ItemFacts("Distant Signal", null, null, null, null), Hit(5001, 2008)));
    }

    [Fact]
    public void A_runner_up_close_behind_is_not_sure()
    {
        // Two films of that title and year: the owner chooses.
        var item = new ItemFacts("Night Harbor", null, 2015, null, null);
        var ranked = Rank(item, Library, []);
        Assert.Equal(2, ranked.Count);
        Assert.True(ranked[0].Score - ranked[1].Score < SureLead);
        Assert.Null(Sure(item, Library, ranked));
    }

    [Fact]
    public void No_candidate_or_only_a_close_title_is_not_sure()
    {
        Assert.Null(SureOf(new ItemFacts("A Film Bigarr Does Not Have", null, 2005, null, null)));
        Assert.Null(SureOf(new ItemFacts("Burning Tides", null, 1997, null, null)));
    }

    [Fact]
    public void The_same_id_is_sure_unless_another_film_has_the_other_id()
    {
        Assert.Equal(Signal.Id, SureOf(new ItemFacts("Whatever", null, 2011, 5001, null))?.MovieId);

        // TMDB id says one film, IMDb id another: not sure.
        Assert.Null(SureOf(new ItemFacts("Whatever", null, 2011, 5001, "tt5000004")));
    }
}
