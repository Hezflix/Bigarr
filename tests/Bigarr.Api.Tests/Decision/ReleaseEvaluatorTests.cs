using Bigarr.Decision;
using Bigarr.Decision.Specifications;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Bigarr.Parsing;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Decision;

/// <summary>The engine sees <see cref="IAcquisitionTarget"/> plus a context; every rule is exercised through the public evaluator.</summary>
public sealed class ReleaseEvaluatorTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static readonly ReleaseEvaluator Evaluator = new();

    // Worst to best; the WEB 1080p pair is a group; cutoff at Bluray-1080p.
    private static QualityProfile Profile(bool upgradeAllowed = true) => new()
    {
        Id = 1,
        Name = "HD",
        PreferredLanguage = "en",
        UpgradeAllowed = upgradeAllowed,
        Cutoff = ParserQuality.Bluray1080p.Id,
        Items =
        [
            new QualityProfileItem { QualityIds = [ParserQuality.HDTV720p.Id], Allowed = false },
            new QualityProfileItem { QualityIds = [ParserQuality.Bluray720p.Id], Allowed = true },
            new QualityProfileItem { GroupId = 1000, GroupName = "WEB 1080p", QualityIds = [ParserQuality.WEBDL1080p.Id, ParserQuality.WEBRip1080p.Id], Allowed = true },
            new QualityProfileItem { QualityIds = [ParserQuality.Bluray1080p.Id], Allowed = true },
            new QualityProfileItem { QualityIds = [ParserQuality.Remux1080p.Id], Allowed = true },
            new QualityProfileItem { QualityIds = [ParserQuality.Bluray2160p.Id], Allowed = true },
        ],
    };

    private static readonly Dictionary<int, QualityDefinition> Definitions = ParserQuality.DefaultQualityDefinitions.ToDictionary(
        d => d.Quality.Id,
        d => new QualityDefinition
        {
            Id = d.Quality.Id,
            Name = d.Quality.Name,
            Source = (QualitySource)(int)d.Quality.Source,
            Resolution = d.Quality.Resolution,
            MinSize = d.Quality.Id == ParserQuality.Bluray1080p.Id ? 20 : d.MinSize,
            MaxSize = d.Quality.Id == ParserQuality.Bluray1080p.Id ? 200 : d.MaxSize,
        });

    private static MovieVersion Version(VersionMode mode = VersionMode.Managed, AcquisitionConstraints? constraints = null, int id = 7, string name = "1080p", bool monitored = true) => new()
    {
        Id = id,
        Name = name,
        Mode = mode,
        QualityProfileId = 1,
        Constraints = constraints,
        FolderName = "The Matrix (1999)",
        Monitored = monitored,
    };

    private static TargetContext Context(
        MovieVersion? target = null,
        QualityProfile? profile = null,
        ExistingFile? file = null,
        int? runtime = 136,
        IReadOnlyCollection<BlocklistEntry>? blocklist = null,
        bool activeGrab = false) =>
        new(target ?? Version(), profile ?? Profile(), Definitions, file, runtime, blocklist ?? [], activeGrab);

    private static ReleaseCandidate Candidate(string title, double sizeGb = 12, int? seeders = 50, int minimumSeeders = 1, string? guid = null, string? infoHash = null) =>
        new(
            new ReleaseInfo
            {
                Guid = guid ?? $"guid:{title}",
                Title = title,
                IndexerName = "Mock",
                Size = (long)(sizeGb * Gb),
                Seeders = seeders,
                InfoHash = infoHash,
            },
            Parser.ParseMovieTitle(title),
            minimumSeeders);

    [Fact]
    public void A_wanted_quality_with_a_sane_size_and_seeders_is_approved()
    {
        var decision = Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-SPARKS"), Context());

        Assert.True(decision.Approved);
        Assert.Empty(decision.Rejections);
    }

    [Fact]
    public void Every_failing_rule_is_reported_at_once()
    {
        var context = Context(activeGrab: true);
        var decision = Evaluator.Evaluate(Candidate("The.Matrix.1999.CAM.x264-GRP", seeders: 0), context);

        Assert.False(decision.Approved);
        Assert.Contains(decision.Rejections, r => r.Reason.Contains("not wanted in profile HD") && r.Type == RejectionType.Permanent);
        Assert.Contains(decision.Rejections, r => r.Reason.StartsWith("Not enough seeders") && r.Type == RejectionType.Temporary);
        Assert.Contains(decision.Rejections, r => r.Reason.Contains("already active") && r.Type == RejectionType.Temporary);
        Assert.False(decision.IsTemporary);
    }

    [Fact]
    public void Unknown_quality_and_disallowed_quality_are_rejected()
    {
        Assert.Contains(Evaluator.Evaluate(Candidate("The.Matrix.1999-GRP"), Context()).Rejections, r => r.Reason.Contains("could not be determined"));
        Assert.Contains(Evaluator.Evaluate(Candidate("The.Matrix.1999.720p.HDTV.x264-GRP"), Context()).Rejections, r => r.Reason == "HDTV-720p is not wanted in profile HD");
    }

    [Fact]
    public void Size_is_checked_against_the_definition_and_the_runtime()
    {
        // Bluray-1080p: 20-200 MB per minute, 136 minutes → 2.66 GB to 26.6 GB.
        var tooSmall = Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP", sizeGb: 1.5), Context());
        Assert.Contains(tooSmall.Rejections, r => r.Reason.Contains("smaller than the minimum"));

        var tooBig = Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP", sizeGb: 40), Context());
        Assert.Contains(tooBig.Rejections, r => r.Reason.Contains("larger than the maximum"));

        // No runtime known: 110 minutes are assumed, 40 GB still too big; 20 GB fine.
        Assert.False(Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP", sizeGb: 40), Context(runtime: null)).Approved);
        Assert.True(Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP", sizeGb: 20), Context(runtime: null)).Approved);

        // Unknown size is not a reason.
        Assert.True(Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP", sizeGb: 0), Context()).Approved);
    }

    [Fact]
    public void Seeders_are_only_checked_when_known_and_a_minimum_is_set()
    {
        Assert.True(Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP", seeders: null), Context()).Approved);
        Assert.True(Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP", seeders: 0, minimumSeeders: 0), Context()).Approved);

        var decision = Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP", seeders: 2, minimumSeeders: 5), Context());
        Assert.Equal("Not enough seeders: 2 (minimum 5)", Assert.Single(decision.Rejections).Reason);
        Assert.True(decision.IsTemporary);
    }

    [Fact]
    public void Version_constraints_route_editions_and_groups()
    {
        var directorsCut = Context(Version(constraints: new AcquisitionConstraints { Edition = "director'?s cut" }));

        Assert.True(Evaluator.Evaluate(Candidate("The.Matrix.1999.Directors.Cut.1080p.BluRay.x264-GRP"), directorsCut).Approved);
        Assert.Contains(
            Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP"), directorsCut).Rejections,
            r => r.Reason.StartsWith("Edition must match"));
        Assert.Contains(
            Evaluator.Evaluate(Candidate("The.Matrix.1999.Extended.Cut.1080p.BluRay.x264-GRP"), directorsCut).Rejections,
            r => r.Reason.Contains("does not match"));

        var framestor = Context(Version(constraints: new AcquisitionConstraints { ReleaseGroup = "FraMeSToR" }));
        Assert.True(Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.REMUX.AVC.DTS-HD.MA.5.1-FRAMESTOR"), framestor).Approved);
        Assert.Contains(
            Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-SPARKS"), framestor).Rejections,
            r => r.Reason == "Release group SPARKS is not FraMeSToR");
    }

    [Fact]
    public void Blocklist_matches_on_guid_hash_or_title()
    {
        var blocklist = new List<BlocklistEntry>
        {
            new() { TargetId = 7, SourceTitle = "old title", ReleaseGuid = "guid-1", Reason = "Download failed" },
            new() { TargetId = 7, SourceTitle = "hash only", InfoHash = "ABCDEF" },
            new() { TargetId = 7, SourceTitle = "The.Matrix.1999.1080p.BluRay.x264-BADGRP" },
        };
        var context = Context(blocklist: blocklist);

        Assert.Contains(
            Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP", guid: "guid-1"), context).Rejections,
            r => r.Reason == "Release is blocklisted: Download failed");
        Assert.Contains(
            Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP", infoHash: "abcdef"), context).Rejections,
            r => r.Reason == "Release is blocklisted");
        Assert.Contains(
            Evaluator.Evaluate(Candidate("the.matrix.1999.1080p.bluray.x264-badgrp"), context).Rejections,
            r => r.Reason == "Release is blocklisted");
        Assert.True(Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP"), context).Approved);
    }

    [Fact]
    public void Upgrades_follow_rank_then_revision_and_stop_at_the_cutoff()
    {
        var bluray1080 = new ExistingFile(ParserQuality.Bluray1080p.Id, 1, 0, false);
        var webdl1080 = new ExistingFile(ParserQuality.WEBDL1080p.Id, 1, 0, false);

        // Lower rank, same rank, same rank but proper.
        Assert.Contains(Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.WEB-DL.H.264-GRP"), Context(file: bluray1080)).Rejections, r => r.Reason == "WEBDL-1080p is lower than the existing Bluray-1080p");
        Assert.Contains(Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP"), Context(file: bluray1080)).Rejections, r => r.Reason == "Not an upgrade over the existing Bluray-1080p");
        Assert.True(Evaluator.Evaluate(Candidate("The.Matrix.1999.PROPER.1080p.BluRay.x264-GRP"), Context(file: bluray1080)).Approved);

        // Cutoff met by the current file: a higher quality is not fetched anymore.
        Assert.Contains(Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.REMUX.AVC-GRP"), Context(file: bluray1080)).Rejections, r => r.Reason.Contains("already meets the profile cutoff"));

        // Below the cutoff: anything higher is welcome, even above the cutoff.
        Assert.True(Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP"), Context(file: webdl1080)).Approved);
        Assert.True(Evaluator.Evaluate(Candidate("The.Matrix.1999.2160p.BluRay.x265-GRP"), Context(file: webdl1080)).Approved);

        // WEBRip and WEB-DL share a group, so they rank equal.
        Assert.Contains(Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.WEBRip.x264-GRP"), Context(file: webdl1080)).Rejections, r => r.Reason == "Not an upgrade over the existing WEBDL-1080p");
    }

    [Fact]
    public void One_shot_versions_and_frozen_profiles_never_upgrade()
    {
        var file = new ExistingFile(ParserQuality.WEBDL1080p.Id, 1, 0, false);

        var oneShot = Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP"), Context(Version(VersionMode.OneShot), file: file));
        Assert.Equal("One-shot version already has its file", Assert.Single(oneShot.Rejections).Reason);

        var frozen = Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP"), Context(profile: Profile(upgradeAllowed: false), file: file));
        Assert.Contains(frozen.Rejections, r => r.Reason.Contains("does not allow upgrades"));

        // Without a file, both are just missing and searchable.
        Assert.True(Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP"), Context(Version(VersionMode.OneShot))).Approved);
    }

    [Fact]
    public void Profile_ranking_resolves_groups_and_cutoffs()
    {
        var profile = Profile();
        Assert.Equal(2, ProfileRanking.RankOf(profile, ParserQuality.WEBRip1080p.Id));
        Assert.Equal(3, ProfileRanking.CutoffRank(profile));
        Assert.Equal(-1, ProfileRanking.RankOf(profile, ParserQuality.CAM.Id));
        Assert.False(ProfileRanking.IsAllowed(profile, ParserQuality.HDTV720p.Id));
        Assert.True(ProfileRanking.CutoffMet(profile, ParserQuality.Remux1080p.Id));
        Assert.False(ProfileRanking.CutoffMet(profile, ParserQuality.WEBDL1080p.Id));

        profile.Cutoff = 1000; // the group id
        Assert.Equal(2, ProfileRanking.CutoffRank(profile));
        Assert.True(ProfileRanking.CutoffMet(profile, ParserQuality.WEBRip1080p.Id));
    }

    [Fact]
    public void Automatic_rules_reject_unmonitored_and_unavailable_versions_but_interactive_search_ignores_them()
    {
        var automatic = ReleaseEvaluator.Automatic();
        var interactive = ReleaseEvaluator.Interactive();
        var release = Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP");

        var unmonitored = Version(monitored: false);
        Assert.Equal("Version is not monitored", Assert.Single(automatic.Evaluate(release, Context(unmonitored)).Rejections).Reason);
        Assert.True(interactive.Evaluate(release, Context(unmonitored)).Approved);

        var pending = Context() with { IsAvailable = false };
        var rejection = Assert.Single(automatic.Evaluate(release, pending).Rejections);
        Assert.Equal("Minimum availability not reached yet", rejection.Reason);
        Assert.Equal(RejectionType.Temporary, rejection.Type);
        Assert.True(interactive.Evaluate(release, pending).Approved);
    }

    [Fact]
    public void Attribution_picks_the_most_specific_accepting_version_then_the_default_then_the_oldest()
    {
        var evaluator = ReleaseEvaluator.Automatic();
        var release = Candidate("The.Matrix.1999.Directors.Cut.1080p.BluRay.x264-GRP");

        var plain = Version(id: 1);
        var directorsCut = Version(constraints: new AcquisitionConstraints { Edition = "director'?s cut" }, id: 2, name: "directors-cut");
        var rejected = Version(constraints: new AcquisitionConstraints { ReleaseGroup = "FraMeSToR" }, id: 3, name: "framestor");
        var decisions = new[]
        {
            evaluator.Evaluate(release, Context(plain) with { IsDefaultTarget = true }),
            evaluator.Evaluate(release, Context(directorsCut)),
            evaluator.Evaluate(release, Context(rejected)),
        };

        Assert.Equal(2, VersionAttribution.Choose(decisions)!.Target.Target.Id);

        // Two unconstrained versions accept: the default wins; without a default, the oldest.
        var older = evaluator.Evaluate(release, Context(Version(id: 5)));
        var newerDefault = evaluator.Evaluate(release, Context(Version(id: 9)) with { IsDefaultTarget = true });
        Assert.Equal(9, VersionAttribution.Choose([older, newerDefault])!.Target.Target.Id);
        Assert.Equal(5, VersionAttribution.Choose([older, evaluator.Evaluate(release, Context(Version(id: 9)))])!.Target.Target.Id);

        Assert.Null(VersionAttribution.Choose([decisions[2]]));
    }

    [Fact]
    public void Custom_format_scores_come_from_the_profile_and_gate_on_the_minimum()
    {
        var profile = Profile();
        profile.FormatScores.Add(new ProfileFormatScore { CustomFormatId = 1, Score = 1950 });
        profile.FormatScores.Add(new ProfileFormatScore { CustomFormatId = 2, Score = -10000 });
        profile.FormatScores.Add(new ProfileFormatScore { CustomFormatId = 3, Score = 5 });
        var context = Context(profile: profile);

        var tier = Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.REMUX.AVC-FraMeSToR") with { CustomFormatIds = [1, 3] }, context);
        Assert.True(tier.Approved);
        Assert.Equal(1955, tier.CustomFormatScore);

        var lq = Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-YIFY") with { CustomFormatIds = [2] }, context);
        Assert.Equal(-10000, lq.CustomFormatScore);
        Assert.Equal("Custom format score -10000 is below the profile minimum 0", Assert.Single(lq.Rejections).Reason);

        // A format the profile does not score counts zero.
        Assert.Equal(0, Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP") with { CustomFormatIds = [99] }, context).CustomFormatScore);

        profile.MinFormatScore = 100;
        Assert.Contains(Evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP") with { CustomFormatIds = [3] }, context).Rejections,
            r => r.Reason == "Custom format score 5 is below the profile minimum 100");
    }

    [Fact]
    public void Upgrades_at_equal_quality_follow_the_score_its_minimum_gain_and_the_format_cutoff()
    {
        var profile = Profile();
        profile.MinUpgradeFormatScore = 100;
        profile.CutoffFormatScore = 2000;
        profile.FormatScores.Add(new ProfileFormatScore { CustomFormatId = 1, Score = 1950 });
        profile.FormatScores.Add(new ProfileFormatScore { CustomFormatId = 3, Score = 5 });
        var plainFile = new ExistingFile(ParserQuality.Bluray1080p.Id, 1, 0, false);
        var tierFile = new ExistingFile(ParserQuality.Bluray1080p.Id, 1, 0, false) { CustomFormatIds = [1] };
        var bluray = Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP");
        var remux = Candidate("The.Matrix.1999.1080p.BluRay.REMUX.AVC.DTS-HD.MA.5.1-GRP", sizeGb: 25);

        // Same quality, file at the quality cutoff but below the format cutoff: a better-scored release is an upgrade.
        Assert.True(Evaluator.Evaluate(bluray with { CustomFormatIds = [1] }, Context(profile: profile, file: plainFile)).Approved);
        Assert.Contains(Evaluator.Evaluate(bluray with { CustomFormatIds = [3] }, Context(profile: profile, file: plainFile)).Rejections,
            r => r.Reason == "Custom format score gain 5 is below the profile minimum 100");
        Assert.Contains(Evaluator.Evaluate(bluray with { CustomFormatIds = [1] }, Context(profile: profile, file: tierFile)).Rejections,
            r => r.Reason == "Not an upgrade over the existing Bluray-1080p: same custom format score 1950");
        Assert.Contains(Evaluator.Evaluate(bluray, Context(profile: profile, file: tierFile)).Rejections,
            r => r.Reason == "Custom format score 0 is lower than the existing 1950");

        // Higher quality is welcome while the file's score stays below the format cutoff...
        Assert.True(Evaluator.Evaluate(remux, Context(profile: profile, file: tierFile)).Approved);

        // ...and refused once the file meets both cutoffs, a proper excepted.
        profile.CutoffFormatScore = 1000;
        Assert.Contains(Evaluator.Evaluate(remux, Context(profile: profile, file: tierFile)).Rejections,
            r => r.Reason == "Existing Bluray-1080p already meets the profile cutoff (custom format score 1950)");
        Assert.True(Evaluator.Evaluate(Candidate("The.Matrix.1999.PROPER.1080p.BluRay.x264-GRP"), Context(profile: profile, file: tierFile)).Approved);
    }

    [Fact]
    public void Version_constraints_can_require_or_forbid_custom_formats()
    {
        var names = new Dictionary<int, string> { [1] = "Remux Tier 01", [2] = "LQ" };
        var release = Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP");

        var required = Context(Version(constraints: new AcquisitionConstraints { RequiredCustomFormatIds = [1] })) with { CustomFormatNames = names };
        Assert.True(Evaluator.Evaluate(release with { CustomFormatIds = [1] }, required).Approved);
        Assert.Contains(Evaluator.Evaluate(release, required).Rejections, r => r.Reason == "Required custom format Remux Tier 01 not matched");

        var forbidden = Context(Version(constraints: new AcquisitionConstraints { ForbiddenCustomFormatIds = [2] })) with { CustomFormatNames = names };
        Assert.True(Evaluator.Evaluate(release, forbidden).Approved);
        Assert.Contains(Evaluator.Evaluate(release with { CustomFormatIds = [2] }, forbidden).Rejections, r => r.Reason == "Forbidden custom format LQ matched");

        var unnamed = Context(Version(constraints: new AcquisitionConstraints { RequiredCustomFormatIds = [7] }));
        Assert.Contains(Evaluator.Evaluate(release, unnamed).Rejections, r => r.Reason == "Required custom format #7 not matched");
    }

    [Fact]
    public void Specifications_can_be_composed()
    {
        var evaluator = new ReleaseEvaluator([new QualityAllowedSpecification()]);
        var decision = evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP", seeders: 0), Context(activeGrab: true));
        Assert.True(decision.Approved);
    }
}
