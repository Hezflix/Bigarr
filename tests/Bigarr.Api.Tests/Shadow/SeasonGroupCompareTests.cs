using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Shadow;
using Bigarr.Decision;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Shadow;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>Activity → Compare: a difference the season's release group made on purpose is "intended", with the reason <c>season-group</c>.</summary>
public sealed class SeasonGroupCompareTests
{
    [Fact]
    public void A_pick_the_season_group_decided_is_an_intended_difference_when_sonarr_took_another_group()
    {
        var tie = Pick("TLC", realigned: false);
        var realign = Pick("TLC", realigned: true);

        var different = SeriesCompareService.Intended(SeriesCompareOutcome.Different, [tie], [Grab("Tsundere-raws")]);
        Assert.Equal(("season-group", "Bigarr preferred TLC, the season's release group, over an equal release from Tsundere-raws"), different);

        // Sonarr kept the other group's files: Bigarr's realignment stands alone.
        var alone = SeriesCompareService.Intended(SeriesCompareOutcome.BigarrOnly, [realign], []);
        Assert.Equal(("season-group", "Bigarr replaced files of another group with TLC's release, to keep one release group per season"), alone);
    }

    [Fact]
    public void Anything_else_stays_a_mismatch()
    {
        // No season group behind the pick, Sonarr took the same group, a tie-break alone, same releases or Sonarr only.
        Assert.Null(SeriesCompareService.Intended(SeriesCompareOutcome.Different, [Pick(null, false)], [Grab("Tsundere-raws")]));
        Assert.Null(SeriesCompareService.Intended(SeriesCompareOutcome.Different, [Pick("TLC", false)], [Grab("tlc")]));
        Assert.Null(SeriesCompareService.Intended(SeriesCompareOutcome.BigarrOnly, [Pick("TLC", false)], []));
        Assert.Null(SeriesCompareService.Intended(SeriesCompareOutcome.Same, [Pick("TLC", true)], [Grab("TLC")]));
        Assert.Null(SeriesCompareService.Intended(SeriesCompareOutcome.SonarrOnly, [], [Grab("Tsundere-raws")]));

        // Every pick of the row must be the season group's.
        Assert.Null(SeriesCompareService.Intended(SeriesCompareOutcome.Different, [Pick("TLC", false), Pick(null, false)], [Grab("Other")]));
    }

    [Fact]
    public void The_season_group_decides_a_pick_only_when_it_broke_a_tie_or_realigns()
    {
        var coverage = new EpisodeCoverage([new EpisodeSlot(null!, 401, 4, 1, null, 24, null, false)], true, true, 24, DateTime.UtcNow)
        {
            SeasonGroups = new Dictionary<int, string> { [4] = "TLC" },
        };
        var web = ParserQuality.WEBDL1080p.Id;
        var tlc = (Group: "TLC", Quality: web, Score: 0);
        var other = (Group: "Tsundere-raws", Quality: web, Score: 0);
        var betterOther = (Group: "Tsundere-raws", Quality: web, Score: -10);
        Assert.Equal(new Bigarr.Api.Features.Downloads.SeasonGroupChoice("TLC", false), SeasonGroupChoiceOf(tlc, [tlc, other]));

        // Alone, or against a release it beat on score, the group decided nothing.
        Assert.Null(SeasonGroupChoiceOf(tlc, [tlc]));
        Assert.Null(SeasonGroupChoiceOf(tlc, [tlc, betterOther]));
        Assert.Null(SeasonGroupChoiceOf(other, [other, tlc]));

        Bigarr.Api.Features.Downloads.SeasonGroupChoice? SeasonGroupChoiceOf((string Group, int Quality, int Score) chosen, (string Group, int Quality, int Score)[] all) =>
            ReleaseRanking.SeasonGroupChoice(chosen, all, _ => coverage, r => r.Group, r => r.Quality, r => r.Score, _ => [401]);
    }

    [Fact]
    public void The_season_group_tie_follows_the_profile_not_the_parser_weight()
    {
        var coverage = new EpisodeCoverage([new EpisodeSlot(null!, 401, 4, 1, null, 24, null, false)], true, true, 24, DateTime.UtcNow)
        {
            SeasonGroups = new Dictionary<int, string> { [4] = "TLC" },
        };
        var tlcWeb = (Group: "TLC", Quality: ParserQuality.WEBDL1080p.Id, Score: 0);
        var otherBluray = (Group: "Tsundere-raws", Quality: ParserQuality.Bluray1080p.Id, Score: 0);

        // The profile groups the two qualities: they tie, so the season's group decided although the parser weighs Bluray higher.
        Assert.Equal(new Bigarr.Api.Features.Downloads.SeasonGroupChoice("TLC", false), ChoiceOf(Grouped, tlcWeb, [tlcWeb, otherBluray]));

        // The profile lists them as two items, Bluray the better: the quality decided, not the group.
        Assert.Null(ChoiceOf(BlurayBest, tlcWeb, [tlcWeb, otherBluray]));

        // Two items with the WEB the better: the other group's Bluray is a worse quality, the group decided nothing either.
        Assert.Null(ChoiceOf(WebBest, tlcWeb, [tlcWeb, otherBluray]));

        Bigarr.Api.Features.Downloads.SeasonGroupChoice? ChoiceOf(QualityProfile profile, (string Group, int Quality, int Score) chosen, (string Group, int Quality, int Score)[] all) =>
            ReleaseRanking.SeasonGroupChoice(chosen, all, _ => coverage, r => r.Group, r => ProfileRanking.RankOf(profile, r.Quality), r => r.Score, _ => [401]);
    }

    // Worst to best; WEBDL-1080p and Bluray-1080p share one item.
    private static readonly QualityProfile Grouped = new()
    {
        Id = 1,
        Name = "Grouped",
        PreferredLanguage = "en",
        Items = [new QualityProfileItem { GroupId = 1000, GroupName = "1080p", QualityIds = [ParserQuality.WEBDL1080p.Id, ParserQuality.Bluray1080p.Id], Allowed = true }],
    };

    private static readonly QualityProfile BlurayBest = new()
    {
        Id = 2,
        Name = "Bluray best",
        PreferredLanguage = "en",
        Items =
        [
            new QualityProfileItem { QualityIds = [ParserQuality.WEBDL1080p.Id], Allowed = true },
            new QualityProfileItem { QualityIds = [ParserQuality.Bluray1080p.Id], Allowed = true },
        ],
    };

    private static readonly QualityProfile WebBest = new()
    {
        Id = 3,
        Name = "Web best",
        PreferredLanguage = "en",
        Items =
        [
            new QualityProfileItem { QualityIds = [ParserQuality.Bluray1080p.Id], Allowed = true },
            new QualityProfileItem { QualityIds = [ParserQuality.WEBDL1080p.Id], Allowed = true },
        ],
    };

    private static SeriesShadowPick Pick(string? seasonGroup, bool realigned) => new()
    {
        SeriesTitle = "Show",
        VersionName = "1080p",
        Trigger = "rss",
        ReleaseTitle = "[TLC] Show - 10",
        ReleaseGuid = Guid.NewGuid().ToString(),
        ReleaseGroup = "TLC",
        SeasonGroup = seasonGroup,
        SeasonGroupRealigned = realigned,
    };

    private static SonarrGrab Grab(string group) => new()
    {
        SeriesTitle = "Show",
        VersionName = "1080p",
        ReleaseTitle = $"[{group}] Show - 10",
        ReleaseGroup = group,
    };
}
