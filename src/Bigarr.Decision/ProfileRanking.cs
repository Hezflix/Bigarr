using Bigarr.Domain.Quality;

namespace Bigarr.Decision;

/// <summary>Ranks exist only inside a profile: the position of the item holding a quality (blueprint §3.6).</summary>
public static class ProfileRanking
{
    /// <summary>Zero-based rank of the item containing <paramref name="qualityId"/>, or -1 when the profile does not list it.</summary>
    public static int RankOf(QualityProfile profile, int qualityId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Items.FindIndex(i => i.QualityIds.Contains(qualityId));
    }

    public static bool IsAllowed(QualityProfile profile, int qualityId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Items.Any(i => i.Allowed && i.QualityIds.Contains(qualityId));
    }

    /// <summary>Rank of the cutoff item; the cutoff is a quality id or a group id.</summary>
    public static int CutoffRank(QualityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var byGroup = profile.Items.FindIndex(i => i.GroupId == profile.Cutoff);
        return byGroup >= 0 ? byGroup : RankOf(profile, profile.Cutoff);
    }

    /// <summary>Sum of the profile's scores for the custom formats matched. Formats the profile does not score count zero (§3.6).</summary>
    public static int FormatScore(QualityProfile profile, IEnumerable<int> customFormatIds)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(customFormatIds);

        var ids = customFormatIds as ISet<int> ?? customFormatIds.ToHashSet();
        return ids.Count == 0 ? 0 : profile.FormatScores.Where(s => ids.Contains(s.CustomFormatId)).Sum(s => s.Score);
    }

    public static bool CutoffMet(QualityProfile profile, int qualityId)
    {
        var rank = RankOf(profile, qualityId);
        var cutoff = CutoffRank(profile);
        return rank >= 0 && cutoff >= 0 && rank >= cutoff;
    }
}
