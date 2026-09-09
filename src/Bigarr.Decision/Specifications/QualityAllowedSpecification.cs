namespace Bigarr.Decision.Specifications;

public sealed class QualityAllowedSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        var quality = candidate.Parsed.Quality?.Quality;
        if (quality is null || quality.Id == 0)
        {
            return new Rejection("Quality could not be determined from the release title");
        }

        return ProfileRanking.IsAllowed(target.Profile, quality.Id)
            ? null
            : new Rejection($"{quality.Name} is not wanted in profile {target.Profile.Name}");
    }
}
