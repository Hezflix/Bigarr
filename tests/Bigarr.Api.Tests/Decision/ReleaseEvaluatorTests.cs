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

    private static MovieVersion Version(VersionMode mode = VersionMode.Managed, AcquisitionConstraints? constraints = null) => new()
    {
        Id = 7,
        Name = "1080p",
        Mode = mode,
        QualityProfileId = 1,
        Constraints = constraints,
        FolderName = "The Matrix (1999)",
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
    public void Specifications_can_be_composed()
    {
        var evaluator = new ReleaseEvaluator([new QualityAllowedSpecification()]);
        var decision = evaluator.Evaluate(Candidate("The.Matrix.1999.1080p.BluRay.x264-GRP", seeders: 0), Context(activeGrab: true));
        Assert.True(decision.Approved);
    }
}
