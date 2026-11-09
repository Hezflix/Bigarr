using Bigarr.Decision;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Tv;
using EpisodeParser = Bigarr.Parsing.Episodes.Parser;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Decision;

/// <summary>
/// Sonarr's episode rules in the shared engine: a release judged against every episode it covers for one series
/// version, still only through <see cref="IAcquisitionTarget"/> and an <see cref="EpisodeCoverage"/>.
/// </summary>
public sealed class EpisodeDecisionTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ReleaseEvaluator Interactive = ReleaseEvaluator.Interactive();

    private static readonly ReleaseEvaluator Automatic = ReleaseEvaluator.Automatic();

    private static QualityProfile Profile() => new()
    {
        Id = 1,
        Name = "HD",
        PreferredLanguage = "en",
        UpgradeAllowed = true,
        Cutoff = ParserQuality.Bluray1080p.Id,
        Items =
        [
            new QualityProfileItem { QualityIds = [ParserQuality.Bluray720p.Id], Allowed = true },
            new QualityProfileItem { GroupId = 1000, GroupName = "WEB 1080p", QualityIds = [ParserQuality.WEBDL1080p.Id, ParserQuality.WEBRip1080p.Id], Allowed = true },
            new QualityProfileItem { QualityIds = [ParserQuality.Bluray1080p.Id], Allowed = true },
        ],
    };

    /// <summary>20 to 200 MB per minute for every quality: easy sums.</summary>
    private static readonly Dictionary<int, QualityDefinition> Definitions = ParserQuality.DefaultQualityDefinitions.ToDictionary(
        d => d.Quality.Id,
        d => new QualityDefinition { Id = d.Quality.Id, Name = d.Quality.Name, Resolution = d.Quality.Resolution, MinSize = 20, MaxSize = 200 });

    private static SeriesVersion Version(int id = 1, AcquisitionConstraints? constraints = null) =>
        new() { Id = id, Name = $"v{id}", QualityProfileId = 1, Constraints = constraints, FolderName = "Show (2020)" };

    private sealed record Ep(int Season, int Number, DateTime? AirDate = null, ExistingFile? File = null, bool Monitored = true, bool Active = false, int? Runtime = 45)
    {
        public int Id => Season * 100 + Number;
    }

    private static TargetContext Context(SeriesVersion version, IReadOnlyList<Ep> episodes, bool anime = false, bool isDefault = true, Dictionary<int, int[]>? fileEpisodes = null)
    {
        var slots = episodes.Select(e => new EpisodeSlot(
                new EpisodeTarget { Id = version.Id * 10_000 + e.Id, Version = version, VersionId = version.Id, EpisodeId = e.Id, Monitored = e.Monitored },
                e.Id,
                e.Season,
                e.Number,
                e.AirDate ?? Now.AddDays(-30),
                e.Runtime,
                e.File,
                e.Active)
            {
                FileEpisodeIds = fileEpisodes?.GetValueOrDefault(e.Id) ?? (e.File is null ? [] : [e.Id]),
            })
            .ToList();
        return new TargetContext(slots[0].Target, Profile(), Definitions, null, 45, [], slots.Any(s => s.HasActiveGrab), true, isDefault)
        {
            Episodes = new EpisodeCoverage(slots, anime, true, 45, Now),
        };
    }

    private static ReleaseCandidate Candidate(string title, double sizeGb, EpisodeSearchScope? scope = null) =>
        new(new ReleaseInfo { Guid = title, Title = title, IndexerName = "Mock", Size = (long)(sizeGb * Gb), Seeders = 50 }, EpisodeParser.ParseTitle(title), 1) { Search = scope };

    private static List<Ep> Season(int count, Func<int, Ep>? each = null) =>
        Enumerable.Range(1, count).Select(n => each?.Invoke(n) ?? new Ep(1, n)).ToList();

    private static string Reasons(ReleaseDecision decision) => string.Join(" | ", decision.Rejections.Select(r => r.Reason));

    [Fact]
    public void A_season_pack_waits_until_every_episode_of_it_has_aired()
    {
        var pack = Candidate("Show.S01.1080p.BluRay.x264-GRP", 40);

        var airing = Context(Version(), Season(10, n => new Ep(1, n, n == 10 ? Now.AddDays(5) : Now.AddDays(-n))));
        var refused = Interactive.Evaluate(pack, airing);
        Assert.False(refused.Approved);
        Assert.Contains(refused.Rejections, r => r.Reason == $"Full season release rejected: not every episode has aired yet (S01E10 airs {Now.AddDays(5):yyyy-MM-dd})");

        // Within 24 hours of the last air date counts as aired, like Sonarr.
        var aired = Context(Version(), Season(10, n => new Ep(1, n, n == 10 ? Now.AddHours(12) : Now.AddDays(-n))));
        Assert.True(Interactive.Evaluate(pack, aired).Approved, Reasons(Interactive.Evaluate(pack, aired)));
    }

    [Fact]
    public void A_multi_episode_release_is_an_upgrade_only_when_it_improves_every_episode_it_replaces()
    {
        var release = Candidate("Show.S01E01E02.1080p.WEB-DL.DDP5.1.H.264-NTb", 3);
        var lower = new ExistingFile(ParserQuality.Bluray720p.Id, 1, 0, false);
        var atCutoff = new ExistingFile(ParserQuality.Bluray1080p.Id, 1, 0, false);

        var both = Interactive.Evaluate(release, Context(Version(), [new Ep(1, 1, File: lower), new Ep(1, 2, File: lower)]));
        Assert.True(both.Approved, Reasons(both));

        var missingAndLower = Interactive.Evaluate(release, Context(Version(), [new Ep(1, 1), new Ep(1, 2, File: lower)]));
        Assert.True(missingAndLower.Approved, Reasons(missingAndLower));

        var mixed = Interactive.Evaluate(release, Context(Version(), [new Ep(1, 1, File: lower), new Ep(1, 2, File: atCutoff)]));
        Assert.False(mixed.Approved);
        var reason = Assert.Single(mixed.Rejections).Reason;
        Assert.StartsWith("Not an upgrade for every episode it would replace (1/2 upgradable): S01E02:", reason);
        Assert.Contains("is lower than the existing Bluray-1080p", reason);

        // A season pack follows the same rule (Sonarr's default "all" mode).
        var pack = Candidate("Show.S01.1080p.BluRay.x264-GRP", 30);
        var seasonFiles = Season(10, n => new Ep(1, n, File: n == 3 ? atCutoff : lower));
        var refusedPack = Interactive.Evaluate(pack, Context(Version(), seasonFiles));
        Assert.True(refusedPack.Rejections.Any(r => r.Reason.StartsWith("Not an upgrade for every episode it would replace (9/10 upgradable): S01E03: Not an upgrade over the existing Bluray-1080p", StringComparison.Ordinal)), Reasons(refusedPack));
    }

    [Fact]
    public void Sizes_are_checked_per_minute_of_every_episode_covered()
    {
        // 10 × 45 min at 20–200 MB/min: 8.8 GB to 87.9 GB for the pack, 0.9 GB to 8.8 GB for one episode.
        var season = Context(Version(), Season(10));
        Assert.True(Interactive.Evaluate(Candidate("Show.S01.1080p.BluRay.x264-GRP", 30), season).Approved);
        var small = Interactive.Evaluate(Candidate("Show.S01.1080p.BluRay.x264-GRP", 5), season);
        Assert.True(small.Rejections.Any(r => r.Reason == "5 GB is smaller than the minimum 8.8 GB for Bluray-1080p (10 episodes totalling 450 min)"), Reasons(small));

        var single = Context(Version(), [new Ep(1, 1)]);
        Assert.Contains(Interactive.Evaluate(Candidate("Show.S01E01.1080p.BluRay.x264-GRP", 30), single).Rejections,
            r => r.Reason == "30 GB is larger than the maximum 8.8 GB for Bluray-1080p (45 min)");

        // An episode's own runtime wins over the series'.
        var longEpisode = Context(Version(), [new Ep(1, 1, Runtime: 180)]);
        Assert.True(Interactive.Evaluate(Candidate("Show.S01E01.1080p.BluRay.x264-GRP", 30), longEpisode).Approved);
    }

    [Fact]
    public void Unknown_runtime_is_spelled_out_instead_of_guessed()
    {
        var slot = new Ep(1, 1, Runtime: null);
        var context = Context(Version(), [slot]) is var c ? c with { Episodes = c.Episodes! with { SeriesRuntimeMinutes = null } } : null!;
        Assert.Contains(Interactive.Evaluate(Candidate("Show.S01E01.1080p.BluRay.x264-GRP", 3), context).Rejections,
            r => r.Reason.StartsWith("Runtime of the episodes is unknown", StringComparison.Ordinal));
    }

    [Fact]
    public void A_release_goes_to_the_version_with_the_most_specific_satisfied_constraints_then_the_default()
    {
        var hd = Version(1);
        var ntb = Version(2, new AcquisitionConstraints { ReleaseGroup = "NTb" });
        var episodes = new List<Ep> { new(1, 5) };

        var fromNtb = Candidate("Show.S01E05.1080p.WEB-DL.DDP5.1.H.264-NTb", 2);
        var decisions = new[] { Interactive.Evaluate(fromNtb, Context(hd, episodes)), Interactive.Evaluate(fromNtb, Context(ntb, episodes, isDefault: false)) };
        Assert.All(decisions, d => Assert.True(d.Approved, Reasons(d)));
        Assert.Equal(2 * 10_000 + 105, VersionAttribution.Choose(decisions)!.Target.Target.Id);

        var fromOther = Candidate("Show.S01E05.1080p.WEB-DL.DDP5.1.H.264-FLUX", 2);
        var other = new[] { Interactive.Evaluate(fromOther, Context(hd, episodes)), Interactive.Evaluate(fromOther, Context(ntb, episodes, isDefault: false)) };
        Assert.Contains(other[1].Rejections, r => r.Reason == "Release group FLUX is not NTb");
        Assert.Equal(1 * 10_000 + 105, VersionAttribution.Choose(other)!.Target.Target.Id);
    }

    [Fact]
    public void A_file_holding_more_episodes_than_the_release_is_not_broken_up()
    {
        var lower = new ExistingFile(ParserQuality.Bluray720p.Id, 1, 0, false);
        var context = Context(Version(), [new Ep(1, 1, File: lower)], fileEpisodes: new() { [101] = [101, 102] });

        var decision = Interactive.Evaluate(Candidate("Show.S01E01.1080p.BluRay.x264-GRP", 3), context);
        Assert.Contains(decision.Rejections, r => r.Reason == "Episode file on disk contains more episodes than this release contains");
    }

    [Fact]
    public void An_anime_v2_is_an_upgrade_only_from_the_group_that_made_the_file()
    {
        var file = new ExistingFile(ParserQuality.WEBDL1080p.Id, 1, 0, false) { ReleaseGroup = "SubsPlease" };
        var context = Context(Version(), [new Ep(1, 5, File: file)], anime: true);

        var otherGroup = Interactive.Evaluate(Candidate("[Erai-raws] Show - 05v2 [1080p WEB-DL]", 1.2), context);
        Assert.Contains(otherGroup.Rejections, r => r.Reason == "Erai-raws does not match existing release group SubsPlease (S01E05)");

        var sameGroup = Interactive.Evaluate(Candidate("[SubsPlease] Show - 05v2 (1080p WEB-DL) [ABCDEF12].mkv", 1.2), context);
        Assert.DoesNotContain(sameGroup.Rejections, r => r.Reason.Contains("release group", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Automatic_paths_want_every_covered_episode_monitored_and_none_downloading()
    {
        var pack = Candidate("Show.S01.1080p.BluRay.x264-GRP", 30);
        var oneUnmonitored = Automatic.Evaluate(pack, Context(Version(), Season(10, n => new Ep(1, n, Monitored: n != 2))));
        Assert.Contains(oneUnmonitored.Rejections, r => r.Reason == "One or more episodes is not monitored: S01E02");

        var single = Automatic.Evaluate(Candidate("Show.S01E02.1080p.BluRay.x264-GRP", 3), Context(Version(), [new Ep(1, 2, Monitored: false)]));
        Assert.Contains(single.Rejections, r => r.Reason == "Episode is not monitored");

        // The interactive search lets the user decide.
        Assert.DoesNotContain(Interactive.Evaluate(pack, Context(Version(), Season(10, n => new Ep(1, n, Monitored: n != 2)))).Rejections, r => r.Reason.Contains("monitored", StringComparison.Ordinal));

        var busy = Interactive.Evaluate(pack, Context(Version(), Season(10, n => new Ep(1, n, Active: n == 4))));
        var queued = Assert.Single(busy.Rejections);
        Assert.Equal("A download is already active for S01E04", queued.Reason);
        Assert.Equal(RejectionType.Temporary, queued.Type);
    }

    [Fact]
    public void A_search_wants_what_it_asked_for()
    {
        var episodes = Season(10);
        var context = Context(Version(), episodes);
        var episodeSearch = new EpisodeSearchScope(EpisodeSearchKind.Episode, new HashSet<int> { 105 }, 1, "S01E05");

        Assert.Contains(Interactive.Evaluate(Candidate("Show.S01.1080p.BluRay.x264-GRP", 30, episodeSearch), context).Rejections,
            r => r.Reason == "Full season pack: an episode search wants the episode alone");
        Assert.Contains(Interactive.Evaluate(Candidate("Show.S01E06.1080p.BluRay.x264-GRP", 3, episodeSearch), Context(Version(), [new Ep(1, 6)])).Rejections,
            r => r.Reason == "Episode wasn't requested: the release is S01E06, not S01E05");
        Assert.True(Interactive.Evaluate(Candidate("Show.S01E05.1080p.BluRay.x264-GRP", 3, episodeSearch), Context(Version(), [new Ep(1, 5)])).Approved);

        var seasonTwo = new EpisodeSearchScope(EpisodeSearchKind.Season, new HashSet<int> { 201, 202 }, 2, "Season 2");
        Assert.Contains(Interactive.Evaluate(Candidate("Show.S01.1080p.BluRay.x264-GRP", 30, seasonTwo), context).Rejections,
            r => r.Reason == "Wrong season: the release is S01E01–S01E10 (10 episodes)");
    }

    [Fact]
    public void Multi_season_split_and_sample_releases_are_refused_with_sonarrs_reasons()
    {
        var twoSeasons = Context(Version(), Season(10).Concat(Enumerable.Range(1, 10).Select(n => new Ep(2, n))).ToList());
        Assert.Contains(Interactive.Evaluate(Candidate("Show.S01-S02.1080p.BluRay.x264-GRP", 60), twoSeasons).Rejections,
            r => r.Reason.StartsWith("Multi-season release, not grabbed automatically", StringComparison.Ordinal));

        Assert.Contains(Interactive.Evaluate(Candidate("Show.S01E05.1080p.WEB-DL.sample-NTb", 0.03), Context(Version(), [new Ep(1, 5)])).Rejections,
            r => r.Reason == "Sample");
    }
}
