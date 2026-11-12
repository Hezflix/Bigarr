namespace Bigarr.Decision.CustomFormats;

/// <summary>
/// Evaluates every custom format against an input, at read time. Radarr's rule: specifications are
/// grouped by implementation; a group passes when none of its required specifications fails and at least one
/// specification matches; the format matches when every group passes.
/// </summary>
public sealed class CustomFormatCalculator
{
    private readonly List<(CustomFormatDefinition Definition, List<ICustomFormatSpecification> Specifications)> _formats;

    public CustomFormatCalculator(IEnumerable<CustomFormatDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _formats = definitions
            .Select(d => (d, d.Specifications.Select(s => CustomFormatSpecificationFactory.Create(s, d.Name)).ToList()))
            .ToList();
    }

    public static CustomFormatCalculator Empty { get; } = new([]);

    public IReadOnlyList<CustomFormatDefinition> Definitions => _formats.Select(f => f.Definition).ToList();

    public IReadOnlyList<CustomFormatDefinition> Match(CustomFormatInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return _formats.Where(f => Matches(f.Specifications, input)).Select(f => f.Definition).ToList();
    }

    public static bool Matches(IReadOnlyList<ICustomFormatSpecification> specifications, CustomFormatInput input)
    {
        ArgumentNullException.ThrowIfNull(specifications);
        ArgumentNullException.ThrowIfNull(input);

        if (specifications.Count == 0)
        {
            return false;
        }

        foreach (var group in specifications.GroupBy(s => s.Implementation))
        {
            var anyMatched = false;
            foreach (var specification in group)
            {
                var satisfied = specification.IsSatisfiedBy(input);
                if (specification.Required && !satisfied)
                {
                    return false;
                }

                anyMatched |= satisfied;
            }

            if (!anyMatched)
            {
                return false;
            }
        }

        return true;
    }
}
