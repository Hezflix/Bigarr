using Bigarr.Parsing.Qualities;

namespace Bigarr.Decision.Specifications;

/// <summary>
/// Is the release better than the file in place? Quality rank first, then proper/repack revision, then custom format
/// score at equal rank, where the gain must reach <c>MinUpgradeFormatScore</c>. A file at the quality cutoff with a
/// score at <c>CutoffFormatScore</c> wants nothing more, a proper or repack excepted. In shadow
/// mode the "file" may be Bigarr's own pick, and the reasons say so.
/// </summary>
/// <remarks>
/// For a series version every episode the release covers is checked against its own file (Sonarr's
/// <c>UpgradeDiskSpecification</c>, season packs in its default "all" mode): the release must be an upgrade over every
/// file it would replace, and an episode without a file counts as upgraded. One episode that would not gain rejects
/// the whole release, with that episode named.
/// </remarks>
public sealed class UpgradableSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        if (target.Episodes is { } episodes)
        {
            return EvaluateEpisodes(candidate, target, episodes);
        }

        return target.File is { } file ? Check(candidate, target, file) : null;
    }

    private static Rejection? EvaluateEpisodes(ReleaseCandidate candidate, TargetContext target, EpisodeCoverage episodes)
    {
        var withFile = episodes.Slots.Where(s => s.File is not null).ToList();
        if (withFile.Count == 0)
        {
            return null;
        }

        var refused = new List<(EpisodeSlot Slot, Rejection Rejection)>();
        foreach (var slot in withFile)
        {
            if (Check(candidate, target, slot.File!) is { } rejection)
            {
                refused.Add((slot, rejection));
            }
        }

        if (refused.Count == 0)
        {
            return null;
        }

        if (episodes.Slots.Count == 1)
        {
            return refused[0].Rejection;
        }

        var upgradedCount = episodes.Slots.Count - refused.Count;
        if (candidate.ParsedEpisode?.FullSeason == true)
        {
            // Sonarr's season-pack modes: all (default), a percentage, or any one episode.
            var percent = 100.0 * upgradedCount / episodes.Slots.Count;
            var accepted = episodes.SeasonPackUpgrade switch
            {
                Bigarr.Domain.Library.SeasonPackUpgrade.Any => upgradedCount > 0,
                Bigarr.Domain.Library.SeasonPackUpgrade.Threshold => percent >= episodes.SeasonPackUpgradeThreshold,
                _ => false,
            };
            if (accepted)
            {
                return null;
            }

            if (episodes.SeasonPackUpgrade != Bigarr.Domain.Library.SeasonPackUpgrade.All)
            {
                var rule = episodes.SeasonPackUpgrade == Bigarr.Domain.Library.SeasonPackUpgrade.Any
                    ? "at least one"
                    : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{episodes.SeasonPackUpgradeThreshold:0.##}%");
                return new Rejection(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"Season pack does not upgrade enough episodes: {upgradedCount}/{episodes.Slots.Count} ({percent:0.##}%), {rule} required"));
            }
        }

        var first = refused.OrderBy(r => r.Slot.SeasonNumber).ThenBy(r => r.Slot.EpisodeNumber).First();
        return new Rejection(
            $"Not an upgrade for every episode it would replace ({upgradedCount}/{episodes.Slots.Count} upgradable): {first.Slot.Label}: {first.Rejection.Reason}"
            + (refused.Count > 1 ? $" (and {refused.Count - 1} more)" : string.Empty));
    }

    /// <summary>The film rule against one file (or pick).</summary>
    public static Rejection? Check(ReleaseCandidate candidate, TargetContext target, ExistingFile file)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(file);

        var noun = file.IsPick ? "picked" : "existing";
        var nounAtStart = file.IsPick ? "Picked" : "Existing";

        if (!target.Target.UpgradesAllowed)
        {
            return new Rejection(file.IsPick ? "One-shot version already has a pick" : "One-shot version already has its file");
        }

        var newQuality = candidate.Quality?.Quality;
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
        var newRevision = candidate.Quality!.Revision ?? new Revision();
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
