using Bigarr.Decision.Specifications;

namespace Bigarr.Decision;

/// <summary>Runs every specification and keeps every rejection: the interactive search shows them all (blueprint §5).</summary>
public sealed class ReleaseEvaluator
{
    /// <summary>What the interactive search applies: the user decides about monitoring and availability.</summary>
    public static readonly IReadOnlyList<IReleaseSpecification> DefaultSpecifications =
    [
        new QualityAllowedSpecification(),
        new MinimumFormatScoreSpecification(),
        new SizeSpecification(),
        new MinimumSeedersSpecification(),
        new ConstraintsSpecification(),
        new BlocklistSpecification(),
        new QueueSpecification(),
        new UpgradableSpecification(),

        // Episode-only rules: silent for a film release.
        new EpisodeRequestedSpecification(),
        new FullSeasonSpecification(),
        new MultiSeasonSpecification(),
        new SplitEpisodeSpecification(),
        new NotSampleSpecification(),
        new SameEpisodesGrabSpecification(),
        new AnimeVersionUpgradeSpecification(),
    ];

    /// <summary>What RSS and automatic search apply: the defaults plus monitoring and availability.</summary>
    public static readonly IReadOnlyList<IReleaseSpecification> AutomaticSpecifications =
    [
        new MonitoredSpecification(),
        new AvailabilitySpecification(),
        .. DefaultSpecifications,
    ];

    private readonly IReadOnlyList<IReleaseSpecification> _specifications;

    public ReleaseEvaluator()
        : this(DefaultSpecifications)
    {
    }

    public static ReleaseEvaluator Interactive() => new(DefaultSpecifications);

    public static ReleaseEvaluator Automatic() => new(AutomaticSpecifications);

    public ReleaseEvaluator(IReadOnlyList<IReleaseSpecification> specifications)
    {
        _specifications = specifications;
    }

    public ReleaseDecision Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(target);

        var rejections = new List<Rejection>();
        foreach (var specification in _specifications)
        {
            var rejection = specification.Evaluate(candidate, target);
            if (rejection is not null)
            {
                rejections.Add(rejection);
            }
        }

        return new ReleaseDecision(target, rejections, ProfileRanking.FormatScore(target.Profile, candidate.CustomFormatIds));
    }
}
