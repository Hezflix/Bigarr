namespace Bigarr.Decision.Specifications;

/// <summary>The profile's floor for custom format scores: a release scoring below it is never wanted.</summary>
public sealed class MinimumFormatScoreSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        var minimum = target.Profile.MinFormatScore;
        var score = ProfileRanking.FormatScore(target.Profile, candidate.CustomFormatIds);
        return score < minimum
            ? new Rejection($"Custom format score {score} is below the profile minimum {minimum}")
            : null;
    }
}
