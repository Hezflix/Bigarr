using Bigarr.Api.Features.Search;
using Bigarr.Decision;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Tv;
using EpisodeParser = Bigarr.Parsing.Episodes.Parser;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Decision;

/// <summary>
/// One release group per season: the season's group (most files, at least 3, specials never), the tie-break it gives
/// the automatic ranking, and the "Align season" rules that let the group's release replace another group's file.
/// </summary>
public sealed class SeasonGroupTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static readonly DateTime Now = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ReleaseEvaluator Interactive = ReleaseEvaluator.Interactive();

    [Fact]
    public void The_season_group_is_the_group_with_the_most_files_and_at_least_three()
    {
        Assert.Equal("TLC", SeasonGroups.Of(4, ["TLC", "TLC", "TLC", "TLC", "TLC", "Tsundere-raws", "Tsundere-raws", "Tsundere-raws"]));

        // Case does not split a group; unknown groups count for none.
        Assert.Equal("TLC", SeasonGroups.Of(4, ["TLC", "tlc", "Tlc", null, " ", "Other"]));

        // Fewer than 3 files: no group yet.
        Assert.Null(SeasonGroups.Of(4, ["TLC", "TLC", "Other"]));
        Assert.Equal("No release group has 3 files in the season yet (most: TLC, 2)", SeasonGroups.WhyNone(4, SeasonGroups.Count(["TLC", "TLC", "Other"])));

        // Two groups tie for the most: none is picked.
        Assert.Null(SeasonGroups.Of(4, ["TLC", "TLC", "TLC", "Other", "Other", "Other"]));
        Assert.Equal("Other and TLC tie with 3 files each", SeasonGroups.WhyNone(4, SeasonGroups.Count(["TLC", "TLC", "TLC", "Other", "Other", "Other"])));

        // Specials never have one, however many files.
        Assert.Null(SeasonGroups.Of(0, ["TLC", "TLC", "TLC", "TLC"]));
        Assert.Null(SeasonGroups.Of(-1, ["TLC", "TLC", "TLC"])); // outside the order
    }

    [Fact]
    public void The_season_group_only_breaks_a_tie_between_equal_quality_and_score()
    {
        var releases = new[]
        {
            (Title: "Other 1080p score 0", Quality: ParserQuality.WEBDL1080p.Id, Score: 0, Group: false, Seeders: 900),
            (Title: "TLC 1080p score 0", Quality: ParserQuality.WEBDL1080p.Id, Score: 0, Group: true, Seeders: 5),
            (Title: "Other 1080p score 10", Quality: ParserQuality.WEBDL1080p.Id, Score: 10, Group: false, Seeders: 1),
            (Title: "Other Bluray-1080p score 0", Quality: ParserQuality.Bluray1080p.Id, Score: 0, Group: false, Seeders: 1),
        };

        var order = ReleaseRanking.OrderSeries(releases, r => ReleaseRanking.WeightOf(r.Quality), r => r.Score, r => r.Group, _ => 1, _ => false, _ => 1, _ => false, _ => 25, r => r.Seeders)
            .Select(r => r.Title)
            .ToList();

        // Quality first, then the score; only then the season's group, before seeders.
        Assert.Equal(["Other Bluray-1080p score 0", "Other 1080p score 10", "TLC 1080p score 0", "Other 1080p score 0"], order);
    }

    [Fact]
    public void A_release_matches_the_season_group_only_where_every_season_it_covers_has_that_group()
    {
        var context = Context(SeasonFour(), groups: new() { [4] = "TLC" });
        Assert.True(SeasonGroups.Matches("tlc", context.Episodes!));
        Assert.False(SeasonGroups.Matches("Tsundere-raws", context.Episodes!));
        Assert.False(SeasonGroups.Matches(null, context.Episodes!));

        // A season without a group (too few files, specials) matches nothing.
        var specials = Context([new Ep(0, 1, File("TLC"))], groups: new() { [4] = "TLC" });
        Assert.False(SeasonGroups.Matches("TLC", specials.Episodes!));
    }

    [Fact]
    public void Aligning_lets_the_season_groups_release_replace_another_groups_file_at_equal_quality_and_score()
    {
        var release = Candidate("Show.S04E10.1080p.WEB-DL.DDP5.1.H.264-TLC");
        var offGroup = new[] { new Ep(4, 10, File("Tsundere-raws")) };

        // Without alignment it is no upgrade.
        var plain = Interactive.Evaluate(release, Context(offGroup, groups: new() { [4] = "TLC" }));
        Assert.Contains(plain.Rejections, r => r.Reason == "Not an upgrade over the existing WEBDL-1080p");

        // Aligning season 4: the same quality and score from the season's group replaces it.
        var aligned = Interactive.Evaluate(release, Context(offGroup, groups: new() { [4] = "TLC" }, align: [4]));
        Assert.True(aligned.Approved, Reasons(aligned));

        // Another group's release gets nothing from the alignment.
        var other = Interactive.Evaluate(Candidate("Show.S04E10.1080p.WEB-DL.DDP5.1.H.264-NTb"), Context(offGroup, groups: new() { [4] = "TLC" }, align: [4]));
        Assert.Contains(other.Rejections, r => r.Reason == "Not an upgrade over the existing WEBDL-1080p");

        // An episode already of the group is not re-downloaded on its own.
        var inGroup = Interactive.Evaluate(release, Context([new Ep(4, 10, File("TLC"))], groups: new() { [4] = "TLC" }, align: [4]));
        Assert.False(inGroup.Approved);
    }

    [Fact]
    public void Aligning_never_lowers_the_quality_or_the_custom_format_score()
    {
        var bluray = new[] { new Ep(4, 10, File("Tsundere-raws", ParserQuality.Bluray1080p.Id)) };
        var lower = Interactive.Evaluate(Candidate("Show.S04E10.1080p.WEB-DL.DDP5.1.H.264-TLC"), Context(bluray, groups: new() { [4] = "TLC" }, align: [4]));
        Assert.Contains(lower.Rejections, r => r.Reason == "WEBDL-1080p is lower than the existing Bluray-1080p: aligning on the season's group never lowers the quality");

        var scored = new[] { new Ep(4, 10, File("Tsundere-raws") with { CustomFormatIds = [7] }) };
        var lowerScore = Interactive.Evaluate(Candidate("Show.S04E10.1080p.WEB-DL.DDP5.1.H.264-TLC"), Context(scored, groups: new() { [4] = "TLC" }, align: [4]));
        Assert.Contains(lowerScore.Rejections, r => r.Reason == "Custom format score 0 is lower than the existing 50: aligning on the season's group never lowers the score");
    }

    [Fact]
    public void An_alignment_is_a_group_change_not_a_v2_so_the_same_group_rules_step_aside()
    {
        var v1 = new[] { new Ep(4, 10, File("Tsundere-raws")) };
        var v2 = Candidate("Show.S04E10.1080p.WEB-DL.DDP5.1.H.264.v2-TLC");
        Assert.Equal(2, v2.Quality!.Revision!.Version);

        // An anime v2 from another group stays refused for an ordinary upgrade...
        var plain = Interactive.Evaluate(v2, Context(v1, anime: true, groups: new() { [4] = "TLC" }));
        Assert.Contains(plain.Rejections, r => r.Reason == "TLC does not match existing release group Tsundere-raws (S04E10)");

        // ...and goes through when it realigns the season.
        var aligned = Interactive.Evaluate(v2, Context(v1, anime: true, groups: new() { [4] = "TLC" }, align: [4]));
        Assert.True(aligned.Approved, Reasons(aligned));

        // Same for a repack, which otherwise only fixes its own group's release.
        var repack = Candidate("Show.S04E10.REPACK.1080p.WEB-DL.DDP5.1.H.264-TLC");
        Assert.Contains(Interactive.Evaluate(repack, Context(v1, groups: new() { [4] = "TLC" })).Rejections, r => r.Reason == "Repack from TLC, but the existing file is from Tsundere-raws");
        var alignedRepack = Interactive.Evaluate(repack, Context(v1, groups: new() { [4] = "TLC" }, align: [4]));
        Assert.True(alignedRepack.Approved, Reasons(alignedRepack));
    }

    [Fact]
    public void A_season_pack_of_the_group_realigns_the_season_and_carries_the_episodes_already_of_the_group()
    {
        var pack = Candidate("Show.S04.1080p.WEB-DL.DDP5.1.H.264-TLC", sizeGb: 12);
        var refused = Interactive.Evaluate(pack, Context(SeasonFour(), groups: new() { [4] = "TLC" }));
        Assert.Contains(refused.Rejections, r => r.Reason.StartsWith("Not an upgrade for every episode it would replace (0/10 upgradable)", StringComparison.Ordinal));

        var aligned = Interactive.Evaluate(pack, Context(SeasonFour(), groups: new() { [4] = "TLC" }, align: [4]));
        Assert.True(aligned.Approved, Reasons(aligned));

        // The group's pack never brings an older revision of its own release back.
        var newer = SeasonFour().Select(e => e.Number == 1 ? e with { File = File("TLC") with { RevisionVersion = 2 } } : e).ToList();
        var older = Interactive.Evaluate(pack, Context(newer, groups: new() { [4] = "TLC" }, align: [4]));
        Assert.Contains(older.Rejections, r => r.Reason.Contains("S04E01: Existing WEBDL-1080p is a newer revision", StringComparison.Ordinal));
    }

    [Fact]
    public void The_automatic_alignment_gives_up_two_days_after_the_file_or_the_switch()
    {
        var twoDays = TimeSpan.FromDays(2);
        var added = Now.AddDays(-1);
        Assert.False(SeasonGroups.GaveUp(added, null, twoDays, Now));
        Assert.True(SeasonGroups.GaveUp(added, null, twoDays, Now.AddDays(1).AddMinutes(1)));

        // An old file gets its two days from the switch, not from its own arrival.
        Assert.False(SeasonGroups.GaveUp(Now.AddDays(-30), Now.AddHours(-1), twoDays, Now));
        Assert.True(SeasonGroups.GaveUp(Now.AddDays(-30), Now.AddDays(-3), twoDays, Now));
        Assert.False(SeasonGroups.GaveUp(null, Now.AddDays(-1), twoDays, Now));
    }

    [Fact]
    public void A_given_up_episode_is_judged_as_an_ordinary_file_again()
    {
        var release = Candidate("Show.S04E10.1080p.WEB-DL.DDP5.1.H.264-TLC");
        var context = Context([new Ep(4, 10, File("Tsundere-raws"))], groups: new() { [4] = "TLC" }, align: [4]);
        var givenUp = context with { Episodes = context.Episodes! with { AlignGivenUp = new HashSet<int> { 410 } } };

        Assert.True(Interactive.Evaluate(release, context).Approved);
        Assert.Contains(Interactive.Evaluate(release, givenUp).Rejections, r => r.Reason == "Not an upgrade over the existing WEBDL-1080p");
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------

    private static QualityProfile Profile() => new()
    {
        Id = 1,
        Name = "HD",
        PreferredLanguage = "en",
        UpgradeAllowed = true,
        Cutoff = ParserQuality.WEBDL1080p.Id,
        Items =
        [
            new QualityProfileItem { QualityIds = [ParserQuality.HDTV720p.Id], Allowed = true },
            new QualityProfileItem { GroupId = 1000, GroupName = "WEB 1080p", QualityIds = [ParserQuality.WEBDL1080p.Id, ParserQuality.WEBRip1080p.Id], Allowed = true },
            new QualityProfileItem { QualityIds = [ParserQuality.Bluray1080p.Id], Allowed = true },
        ],
        FormatScores = [new ProfileFormatScore { CustomFormatId = 7, Score = 50 }],
    };

    private static readonly Dictionary<int, QualityDefinition> Definitions = ParserQuality.DefaultQualityDefinitions.ToDictionary(
        d => d.Quality.Id,
        d => new QualityDefinition { Id = d.Quality.Id, Name = d.Quality.Name, Resolution = d.Quality.Resolution, MinSize = 1, MaxSize = 400, SeriesMinSize = 1, SeriesMaxSize = 400 });

    private sealed record Ep(int Season, int Number, ExistingFile? File = null)
    {
        public int Id => Season * 100 + Number;
    }

    private static ExistingFile File(string group, int? qualityId = null) =>
        new(qualityId ?? ParserQuality.WEBDL1080p.Id, 1, 0, false) { ReleaseGroup = group, DateAdded = Now.AddDays(-1) };

    /// <summary>Season 4: seven TLC episodes, the last three from Tsundere-raws.</summary>
    private static List<Ep> SeasonFour() =>
        Enumerable.Range(1, 10).Select(n => new Ep(4, n, File(n <= 7 ? "TLC" : "Tsundere-raws"))).ToList();

    private static TargetContext Context(IReadOnlyList<Ep> episodes, bool anime = false, Dictionary<int, string>? groups = null, HashSet<int>? align = null)
    {
        var version = new SeriesVersion { Id = 1, Name = "v1", QualityProfileId = 1, FolderName = "Show (2020)" };
        var slots = episodes.Select(e => new EpisodeSlot(
                new EpisodeTarget { Id = 10_000 + e.Id, Version = version, VersionId = 1, EpisodeId = e.Id, Monitored = true },
                e.Id,
                e.Season,
                e.Number,
                Now.AddDays(-30),
                24,
                e.File,
                false)
        {
            FileEpisodeIds = e.File is null ? [] : [e.Id],
        })
            .ToList();
        return new TargetContext(slots[0].Target, Profile(), Definitions, null, 24, [], false)
        {
            Episodes = new EpisodeCoverage(slots, anime, true, 24, Now)
            {
                SeasonGroups = groups ?? [],
                AlignSeasons = align ?? [],
            },
        };
    }

    private static ReleaseCandidate Candidate(string title, double sizeGb = 1.2) =>
        new(new ReleaseInfo { Guid = title, Title = title, IndexerName = "Mock", Size = (long)(sizeGb * Gb), Seeders = 50 }, EpisodeParser.ParseTitle(title), 1);

    private static string Reasons(ReleaseDecision decision) => string.Join(" | ", decision.Rejections.Select(r => r.Reason));
}
