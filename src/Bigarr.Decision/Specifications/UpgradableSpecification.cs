using Bigarr.Parsing.Qualities;

namespace Bigarr.Decision.Specifications;

/// <summary>
/// Is the release better than the file in place? Quality rank first, then proper/repack revision, then custom format
/// score at equal rank, where the gain must reach <c>MinUpgradeFormatScore</c>. A file at the quality cutoff with a
/// score at <c>CutoffFormatScore</c> wants nothing more, a proper or repack excepted (blueprint §3.6, §5). In shadow
/// mode the "file" may be Bigarr's own pick, and the reasons say so.
/// </summary>
public sealed class UpgradableSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        var file = target.File;
        if (file is null)
        {
            return null;
        }

        var noun = file.IsPick ? "picked" : "existing";
        var nounAtStart = file.IsPick ? "Picked" : "Existing";

        if (!target.Target.UpgradesAllowed)
        {
            return new Rejection(file.IsPick ? "One-shot version already has a pick" : "One-shot version already has its file");
        }

        var newQuality = candidate.Parsed.Quality?.Quality;
        if (newQuality is null || newQuality.Id == 0)
        {
            return null; // QualityAllowedSpecification already explains this one.
        }

        var profile = target.Profile;
        var currentRank = ProfileRanking.RankOf(profile, file.QualityId);
        var newRank = ProfileRanking.RankOf(profile, newQuality.Id);
        var currentName = target.Definitions.TryGetValue(file.QualityId, out var def) ? def.Name : $"#{file.QualityId}";

        if (newRank < currentRank)
        {
            return new Rejection($"{newQuality.Name} is lower than the {noun} {currentName}");
        }

        var currentRevision = new Revision(file.RevisionVersion, file.RevisionReal, file.IsRepack);
        var newRevision = candidate.Parsed.Quality!.Revision ?? new Revision();
        var revisionCompare = newRevision.CompareTo(currentRevision);
        var isRevisionUpgrade = newRank == currentRank && revisionCompare > 0;

        var currentScore = ProfileRanking.FormatScore(profile, file.CustomFormatIds);
        var newScore = ProfileRanking.FormatScore(profile, candidate.CustomFormatIds);

        if (newRank == currentRank && !isRevisionUpgrade)
        {
            if (revisionCompare < 0)
            {
                return new Rejection($"{nounAtStart} {currentName} is a newer revision");
            }

            if (newScore == currentScore)
            {
                return new Rejection(newScore == 0
                    ? $"Not an upgrade over the {noun} {currentName}"
                    : $"Not an upgrade over the {noun} {currentName}: same custom format score {currentScore}");
            }

            if (newScore < currentScore)
            {
                return new Rejection($"Custom format score {newScore} is lower than the {noun} {currentScore}");
            }
        }

        if (!isRevisionUpgrade && ProfileRanking.CutoffMet(profile, file.QualityId) && currentScore >= profile.CutoffFormatScore)
        {
            return new Rejection(profile.CutoffFormatScore > 0 || currentScore != 0
                ? $"{nounAtStart} {currentName} already meets the profile cutoff (custom format score {currentScore})"
                : $"{nounAtStart} {currentName} already meets the profile cutoff");
        }

        if (!profile.UpgradeAllowed && !isRevisionUpgrade)
        {
            return new Rejection($"Profile {profile.Name} does not allow upgrades and the version already has {(file.IsPick ? "a pick" : "a file")}");
        }

        if (newRank > currentRank || isRevisionUpgrade)
        {
            return null;
        }

        // Same rank, same revision, higher score: the gain must be worth a re-download.
        var gain = newScore - currentScore;
        if (gain < profile.MinUpgradeFormatScore)
        {
            return new Rejection($"Custom format score gain {gain} is below the profile minimum {profile.MinUpgradeFormatScore}");
        }

        return null;
    }
}
