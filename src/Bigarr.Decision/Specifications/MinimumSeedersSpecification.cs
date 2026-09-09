using Bigarr.Domain.Acquisition;

namespace Bigarr.Decision.Specifications;

public sealed class MinimumSeedersSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        if (candidate.Release.Protocol != DownloadProtocol.Torrent || candidate.MinimumSeeders <= 0 || candidate.Release.Seeders is null)
        {
            return null;
        }

        return candidate.Release.Seeders.Value >= candidate.MinimumSeeders
            ? null
            : new Rejection($"Not enough seeders: {candidate.Release.Seeders} (minimum {candidate.MinimumSeeders})", RejectionType.Temporary);
    }
}
