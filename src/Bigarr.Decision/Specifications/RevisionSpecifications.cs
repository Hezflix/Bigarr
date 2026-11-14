using System.Globalization;
using Bigarr.Parsing.Qualities;

namespace Bigarr.Decision.Specifications;

// Sonarr's and Radarr's limits on propers and repacks (NzbDrone.Core.DecisionEngine.Specifications.RepackSpecification
// and RssSync.ProperSpecification, develop), with their default "prefer and upgrade" propers setting: Bigarr has no
// such setting. UpgradableSpecification lets any newer revision of the quality in place through, cutoff or not; these
// two narrow that down. A film checks its one file (or pick); a series release checks every episode file it would
// replace, Sonarr's per-episode loop, and names the episode in the reason.

/// <summary>
/// A repack only replaces a file of the same release group: it fixes that group's own release. Applies to every search,
/// the interactive one included, like Sonarr's and Radarr's <c>RepackSpecification</c>.
/// </summary>
public sealed class RepackSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(target);

        if (candidate.Quality is not { Revision.IsRepack: true } quality)
        {
            return null;
        }

        return RevisionUpgrade.FirstRefusal(target, quality, (file, noun) =>
        {
            if (string.IsNullOrWhiteSpace(file.ReleaseGroup))
            {
                return $"Repack, but the release group of the {noun} is unknown";
            }

            if (candidate.ReleaseGroup is not { } group)
            {
                return $"Repack with an unknown release group: it cannot be matched to the {noun}";
            }

            return string.Equals(file.ReleaseGroup, group, StringComparison.OrdinalIgnoreCase)
                ? null
                : $"Repack from {group}, but the {noun} is from {file.ReleaseGroup}";
        });
    }
}

/// <summary>
/// RSS only: a proper or repack is fetched for a file added within the last <see cref="MaxFileAgeDays"/> days, not for an
/// old one, like Sonarr's and Radarr's <c>RssSync.ProperSpecification</c>. Searches, automatic or interactive, skip it.
/// </summary>
/// <remarks>
/// "Today" is the UTC date of the injected clock: dates in place are UTC, and Sonarr's local <c>DateTime.Today</c> would
/// only move the boundary by the server's offset. A file whose date is unknown passes.
/// </remarks>
public sealed class ProperSpecification(TimeProvider time) : IReleaseSpecification
{
    public const int MaxFileAgeDays = 7;

    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(target);

        if (candidate.Quality is not { } quality)
        {
            return null;
        }

        var oldest = time.GetUtcNow().UtcDateTime.Date.AddDays(-MaxFileAgeDays);
        var kind = quality.Revision?.IsRepack == true ? "Repack" : "Proper";
        return RevisionUpgrade.FirstRefusal(target, quality, (file, noun) =>
            file.DateAdded is { } added && added < oldest
                ? string.Create(CultureInfo.InvariantCulture,
                    $"{kind} for an old file: the {noun} dates from {added:yyyy-MM-dd}, more than {MaxFileAgeDays} days ago")
                : null);
    }
}

/// <summary>What both rules share: which files a release is a newer revision of, and how a refusal is worded.</summary>
internal static class RevisionUpgrade
{
    /// <summary>
    /// Sonarr's <c>IsRevisionUpgrade</c>: the same quality, not just the same rank, with a higher revision. A proper
    /// WEBRip over a WEB-DL is a plain upgrade question, left to <see cref="UpgradableSpecification"/>.
    /// </summary>
    public static bool Is(ExistingFile file, QualityModel release) =>
        release.Quality is { } quality
        && quality.Id == file.QualityId
        && (release.Revision ?? new Revision()).CompareTo(new Revision(file.RevisionVersion, file.RevisionReal, file.IsRepack)) > 0;

    /// <summary>
    /// Runs <paramref name="refuse"/> on every file (or pick) the release is a newer revision of and returns the first
    /// refusal, episode named, with how many more episodes were refused. <paramref name="refuse"/> gets the file and how
    /// to name it ("existing file" or "pick").
    /// </summary>
    public static Rejection? FirstRefusal(TargetContext target, QualityModel release, Func<ExistingFile, string, string?> refuse)
    {
        if (target.Episodes is { } episodes)
        {
            var refused = episodes.Slots
                .Where(s => s.File is { } file && Is(file, release))
                .OrderBy(s => s.SeasonNumber).ThenBy(s => s.EpisodeNumber)
                .Select(s => (Slot: s, Reason: refuse(s.File!, Noun(s.File!))))
                .Where(r => r.Reason is not null)
                .ToList();
            if (refused.Count == 0)
            {
                return null;
            }

            var first = refused[0];
            return new Rejection(episodes.Slots.Count == 1
                ? first.Reason!
                : $"{first.Slot.Label}: {first.Reason}" + (refused.Count > 1 ? $" (and {refused.Count - 1} more)" : string.Empty));
        }

        return target.File is { } single && Is(single, release) && refuse(single, Noun(single)) is { } reason
            ? new Rejection(reason)
            : null;
    }

    private static string Noun(ExistingFile file) => file.IsPick ? "pick" : "existing file";
}
