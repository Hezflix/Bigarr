using System.Text.RegularExpressions;

namespace Bigarr.Decision.Specifications;

/// <summary>Per-version constraints (blueprint §3.3). Custom format constraints join in M3 with the scoring.</summary>
public sealed class ConstraintsSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        var constraints = target.Target.Constraints;
        if (constraints is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(constraints.Edition))
        {
            var edition = candidate.Parsed.Edition ?? string.Empty;
            if (!SafeIsMatch(constraints.Edition, edition))
            {
                return new Rejection(edition.Length == 0
                    ? $"Edition must match '{constraints.Edition}' and the release has none"
                    : $"Edition '{edition}' does not match '{constraints.Edition}'");
            }
        }

        if (!string.IsNullOrWhiteSpace(constraints.ReleaseGroup))
        {
            var group = candidate.Parsed.ReleaseGroup ?? string.Empty;
            if (!string.Equals(group, constraints.ReleaseGroup, StringComparison.OrdinalIgnoreCase))
            {
                return new Rejection(group.Length == 0
                    ? $"Release group must be {constraints.ReleaseGroup} and none was found"
                    : $"Release group {group} is not {constraints.ReleaseGroup}");
            }
        }

        foreach (var id in constraints.RequiredCustomFormatIds)
        {
            if (!candidate.CustomFormatIds.Contains(id))
            {
                return new Rejection($"Required custom format {FormatName(target, id)} not matched");
            }
        }

        foreach (var id in constraints.ForbiddenCustomFormatIds)
        {
            if (candidate.CustomFormatIds.Contains(id))
            {
                return new Rejection($"Forbidden custom format {FormatName(target, id)} matched");
            }
        }

        return null;
    }

    private static string FormatName(TargetContext target, int id) =>
        target.CustomFormatNames.TryGetValue(id, out var name) ? name : $"#{id}";

    private static bool SafeIsMatch(string pattern, string input)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException)
        {
            // An invalid pattern is treated as a literal.
            return input.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
