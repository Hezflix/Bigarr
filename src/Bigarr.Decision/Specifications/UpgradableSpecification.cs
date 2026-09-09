using Bigarr.Parsing.Qualities;

namespace Bigarr.Decision.Specifications;

/// <summary>
/// Quality rank first, then proper/repack revision at equal rank. Custom format scores and
/// <c>MinUpgradeFormatScore</c> join in M3 (blueprint §3.6, §5).
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

        if (!target.Target.UpgradesAllowed)
        {
            return new Rejection("One-shot version already has its file");
        }

        var profile = target.Profile;
        if (!profile.UpgradeAllowed)
        {
            return new Rejection($"Profile {profile.Name} does not allow upgrades and the version already has a file");
        }

        var newQuality = candidate.Parsed.Quality?.Quality;
        if (newQuality is null || newQuality.Id == 0)
        {
            return null; // QualityAllowedSpecification already explains this one.
        }

        var currentRank = ProfileRanking.RankOf(profile, file.QualityId);
        var newRank = ProfileRanking.RankOf(profile, newQuality.Id);
        var currentName = target.Definitions.TryGetValue(file.QualityId, out var def) ? def.Name : $"#{file.QualityId}";

        if (newRank < currentRank)
        {
            return new Rejection($"{newQuality.Name} is lower than the existing {currentName}");
        }

        if (newRank == currentRank)
        {
            var currentRevision = new Revision(file.RevisionVersion, file.RevisionReal, file.IsRepack);
            var newRevision = candidate.Parsed.Quality!.Revision ?? new Revision();
            if (newRevision.CompareTo(currentRevision) <= 0)
            {
                return new Rejection($"Not an upgrade over the existing {currentName}");
            }

            return null; // A proper or repack of the same quality is always welcome.
        }

        if (ProfileRanking.CutoffMet(profile, file.QualityId))
        {
            return new Rejection($"Existing {currentName} already meets the profile cutoff");
        }

        return null;
    }
}
