using Bigarr.Decision.Specifications;

namespace Bigarr.Decision;

/// <summary>Runs every specification and keeps every rejection: the interactive search shows them all (blueprint §5).</summary>
public sealed class ReleaseEvaluator
{
    public static readonly IReadOnlyList<IReleaseSpecification> DefaultSpecifications =
    [
        new QualityAllowedSpecification(),
        new SizeSpecification(),
        new MinimumSeedersSpecification(),
        new ConstraintsSpecification(),
        new BlocklistSpecification(),
        new QueueSpecification(),
        new UpgradableSpecification(),
    ];

    private readonly IReadOnlyList<IReleaseSpecification> _specifications;

    public ReleaseEvaluator()
        : this(DefaultSpecifications)
    {
    }

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

        return new ReleaseDecision(target, rejections);
    }
}
