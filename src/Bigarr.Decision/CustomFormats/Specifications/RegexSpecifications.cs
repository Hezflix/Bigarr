using System.Text.RegularExpressions;

namespace Bigarr.Decision.CustomFormats.Specifications;

/// <summary>Regex on some text of the release. Case-insensitive like Radarr; a pattern that does not compile is refused at import.</summary>
public abstract class RegexSpecificationBase : CustomFormatSpecificationBase
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    protected RegexSpecificationBase(CustomFormatSpecificationDefinition definition, string formatName)
        : base(definition)
    {
        Pattern = SpecificationFields.RequireString(definition, formatName, "value");
        try
        {
            Regex = new Regex(Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new CustomFormatParseException($"Custom format '{formatName}', specification '{definition.Name}': invalid regular expression: {ex.Message}", ex);
        }
    }

    public string Pattern { get; }

    protected Regex Regex { get; }

    protected bool Matches(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}

/// <summary>Radarr's <c>ReleaseTitleSpecification</c>: the release title (film name masked) or the file name.</summary>
public sealed class ReleaseTitleSpecification(CustomFormatSpecificationDefinition definition, string formatName) : RegexSpecificationBase(definition, formatName)
{
    protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input) => Matches(input.ReleaseTitle) || Matches(input.FileName);
}

public sealed class ReleaseGroupSpecification(CustomFormatSpecificationDefinition definition, string formatName) : RegexSpecificationBase(definition, formatName)
{
    protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input) => Matches(input.ReleaseGroup);
}

public sealed class EditionSpecification(CustomFormatSpecificationDefinition definition, string formatName) : RegexSpecificationBase(definition, formatName)
{
    protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input) => Matches(input.Edition);
}
