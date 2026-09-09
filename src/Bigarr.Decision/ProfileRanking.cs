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

    public static bool CutoffMet(QualityProfile profile, int qualityId)
    {
        var rank = RankOf(profile, qualityId);
        var cutoff = CutoffRank(profile);
        return rank >= 0 && cutoff >= 0 && rank >= cutoff;
    }
}
