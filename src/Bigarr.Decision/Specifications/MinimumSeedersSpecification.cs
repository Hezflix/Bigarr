using Bigarr.Domain.Acquisition;

namespace Bigarr.Decision.Specifications;

public sealed class MinimumSeedersSpecification : IReleaseSpecification
{
    /// <summary>The start of this rejection's reason, which is all a stored verdict keeps of it.</summary>
    public const string ReasonPrefix = "Not enough seeders";

    /// <summary>A version turned the release down for its seeders and for nothing else: it would take it once seeded.</summary>
    public static bool WaitsOnlyForSeeders(ReleaseDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return decision.Rejections.Count > 0 && decision.Rejections.All(IsNotEnoughSeeders);
    }

    public static bool IsNotEnoughSeeders(Rejection rejection)
    {
        ArgumentNullException.ThrowIfNull(rejection);
        return rejection.Type == RejectionType.Temporary && rejection.Reason.StartsWith(ReasonPrefix, StringComparison.Ordinal);
    }

    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        if (candidate.Release.Protocol != DownloadProtocol.Torrent || candidate.MinimumSeeders <= 0 || candidate.Release.Seeders is null)
        {
            return null;
        }

        return candidate.Release.Seeders.Value >= candidate.MinimumSeeders
            ? null
            : new Rejection($"{ReasonPrefix}: {candidate.Release.Seeders} (minimum {candidate.MinimumSeeders})", RejectionType.Temporary);
    }
}
