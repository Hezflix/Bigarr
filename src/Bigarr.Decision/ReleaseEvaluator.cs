using Bigarr.Decision.Specifications;

namespace Bigarr.Decision;

/// <summary>Runs every specification and keeps every rejection: the interactive search shows them all.</summary>
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
        new RepackSpecification(),

        // Episode-only rules: silent for a film release.
        new EpisodeRequestedSpecification(),
        new FullSeasonSpecification(),
        new MultiSeasonSpecification(),
        new SplitEpisodeSpecification(),
        new NotSampleSpecification(),
        new SameEpisodesGrabSpecification(),
        new AnimeVersionUpgradeSpecification(),
    ];

    /// <summary>What automatic search applies (and RSS, see <see cref="Rss"/>): the defaults plus monitoring and availability.</summary>
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

    /// <summary>What the RSS pass applies: the automatic rules plus the RSS-only one (no proper for an old file).</summary>
    public static ReleaseEvaluator Rss(TimeProvider time) => new([.. AutomaticSpecifications, new ProperSpecification(time)]);

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
