using Bigarr.Api.Features.Search;
using Bigarr.Decision;
using Bigarr.Domain.Quality;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Search;

/// <summary>The automatic paths rank by the order of the target's profile, as Radarr and Sonarr do, not by the parser's fixed weight.</summary>
public sealed class ReleaseRankingTests
{
    private sealed record Candidate(string Title, int QualityId, int Score);

    // Worst to best; WEBDL-1080p and Bluray-1080p share the last item.
    private static readonly QualityProfile Grouped = new()
    {
        Id = 1,
        Name = "Grouped",
        PreferredLanguage = "en",
        Items =
        [
            new QualityProfileItem { QualityIds = [ParserQuality.HDTV1080p.Id], Allowed = true },
            new QualityProfileItem { GroupId = 1000, GroupName = "1080p", QualityIds = [ParserQuality.WEBDL1080p.Id, ParserQuality.Bluray1080p.Id], Allowed = true },
        ],
    };

    // Worst to best; the WEB comes after the Bluray, so it is the better item although the parser weighs Bluray higher.
    private static readonly QualityProfile WebFirst = new()
    {
        Id = 2,
        Name = "Web best",
        PreferredLanguage = "en",
        Items =
        [
            new QualityProfileItem { QualityIds = [ParserQuality.Bluray1080p.Id], Allowed = true },
            new QualityProfileItem { QualityIds = [ParserQuality.WEBDL1080p.Id], Allowed = true },
        ],
    };

    private static List<string> Rank(QualityProfile profile, params Candidate[] releases) =>
        ReleaseRanking.Order(releases, r => ProfileRanking.RankOf(profile, r.QualityId), r => r.Score, _ => 1, _ => 25, _ => 0)
            .Select(r => r.Title)
            .ToList();

    private static List<string> RankSeries(QualityProfile profile, params Candidate[] releases) =>
        ReleaseRanking.OrderSeries(releases, r => ProfileRanking.RankOf(profile, r.QualityId), r => r.Score, _ => false, _ => 1, _ => false, _ => 1, _ => false, _ => 25, _ => 0)
            .Select(r => r.Title)
            .ToList();

    private static readonly Candidate Bluray0 = new("Bluray score 0", ParserQuality.Bluray1080p.Id, 0);
    private static readonly Candidate Web100 = new("WEBDL score 100", ParserQuality.WEBDL1080p.Id, 100);
    private static readonly Candidate Hdtv500 = new("HDTV score 500", ParserQuality.HDTV1080p.Id, 500);
    private static readonly Candidate Web0 = new("WEBDL score 0", ParserQuality.WEBDL1080p.Id, 0);

    [Fact]
    public void Qualities_grouped_in_the_profile_are_decided_by_custom_format_score() =>
        Assert.Equal(["WEBDL score 100", "Bluray score 0"], Rank(Grouped, Bluray0, Web100));

    [Fact]
    public void A_better_item_of_the_profile_still_wins_over_a_higher_score() =>
        Assert.Equal(["WEBDL score 0", "HDTV score 500"], Rank(Grouped, Hdtv500, Web0));

    [Fact]
    public void The_profile_order_wins_over_the_parser_weight()
    {
        Assert.True(ReleaseRanking.WeightOf(ParserQuality.Bluray1080p.Id) > ReleaseRanking.WeightOf(ParserQuality.WEBDL1080p.Id));
        Assert.Equal(["WEBDL score 0", "Bluray score 0"], Rank(WebFirst, Bluray0, Web0));
    }

    [Fact]
    public void The_series_order_ranks_by_the_profile_the_same_way()
    {
        Assert.Equal(["WEBDL score 100", "Bluray score 0"], RankSeries(Grouped, Bluray0, Web100));
        Assert.Equal(["WEBDL score 0", "HDTV score 500"], RankSeries(Grouped, Hdtv500, Web0));
        Assert.Equal(["WEBDL score 0", "Bluray score 0"], RankSeries(WebFirst, Bluray0, Web0));
    }
}
