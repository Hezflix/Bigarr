namespace Bigarr.Decision.Specifications;

/// <summary>Guid or info hash when there is one, title otherwise (blueprint §3.5). The slice supplies the right list.</summary>
public sealed class BlocklistSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        if (target.Blocklist.Count == 0)
        {
            return null;
        }

        var release = candidate.Release;
        foreach (var entry in target.Blocklist)
        {
            var byGuid = entry.ReleaseGuid is not null && string.Equals(entry.ReleaseGuid, release.Guid, StringComparison.Ordinal);
            var byHash = entry.InfoHash is not null && release.InfoHash is not null && string.Equals(entry.InfoHash, release.InfoHash, StringComparison.OrdinalIgnoreCase);
            var byTitle = entry.ReleaseGuid is null && entry.InfoHash is null && string.Equals(entry.SourceTitle, release.Title, StringComparison.OrdinalIgnoreCase);
            if (byGuid || byHash || byTitle)
            {
                return new Rejection(entry.Reason is null ? "Release is blocklisted" : $"Release is blocklisted: {entry.Reason}");
            }
        }

        return null;
    }
}
