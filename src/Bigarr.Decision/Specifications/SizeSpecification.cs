namespace Bigarr.Decision.Specifications;

/// <summary>Quality definitions give MB per minute of runtime; unknown runtime falls back to 110 minutes.</summary>
public sealed class SizeSpecification : IReleaseSpecification
{
    private const long Megabyte = 1024L * 1024L;

    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        var size = candidate.Release.Size;
        if (size <= 0)
        {
            return null; // Some indexers do not report sizes; nothing to check.
        }

        var qualityId = candidate.Parsed.Quality?.Quality?.Id ?? 0;
        if (!target.Definitions.TryGetValue(qualityId, out var definition))
        {
            return null;
        }

        var runtime = target.EffectiveRuntimeMinutes;
        if (definition.MinSize is > 0)
        {
            var min = (long)(definition.MinSize.Value * runtime * Megabyte);
            if (size < min)
            {
                return new Rejection($"{Format(size)} is smaller than the minimum {Format(min)} for {definition.Name} ({runtime} min)");
            }
        }

        if (definition.MaxSize is > 0)
        {
            var max = (long)(definition.MaxSize.Value * runtime * Megabyte);
            if (size > max)
            {
                return new Rejection($"{Format(size)} is larger than the maximum {Format(max)} for {definition.Name} ({runtime} min)");
            }
        }

        return null;
    }

    private static string Format(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.#} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0} MB",
        _ => $"{bytes} B",
    };
}
