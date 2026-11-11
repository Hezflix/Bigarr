using Bigarr.Domain.Acquisition;

namespace Bigarr.Decision.CustomFormats.Specifications;

/// <summary>Radarr's <c>SourceSpecification</c>: <c>value</c> is the numeric <c>QualitySource</c> (BLURAY = 9, WEBDL = 7…).</summary>
public sealed class SourceSpecification(CustomFormatSpecificationDefinition definition, string formatName) : CustomFormatSpecificationBase(definition)
{
    public int Value { get; } = SpecificationFields.RequireInt(definition, formatName, "value");

    protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input) => (int)input.Source == Value;
}

/// <summary>Radarr's <c>ResolutionSpecification</c>: <c>value</c> is the vertical resolution (2160, 1080, 720, 576, 480…).</summary>
public sealed class ResolutionSpecification(CustomFormatSpecificationDefinition definition, string formatName) : CustomFormatSpecificationBase(definition)
{
    public int Value { get; } = SpecificationFields.RequireInt(definition, formatName, "value");

    protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input) => input.Resolution == Value;
}

/// <summary>Radarr's <c>QualityModifierSpecification</c>: <c>value</c> is the numeric <c>Modifier</c> (REMUX = 5, BRDISK = 4, RAWHD = 3…).</summary>
public sealed class QualityModifierSpecification(CustomFormatSpecificationDefinition definition, string formatName) : CustomFormatSpecificationBase(definition)
{
    public int Value { get; } = SpecificationFields.RequireInt(definition, formatName, "value");

    protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input) => (int)input.Modifier == Value;
}

/// <summary>Radarr's <c>IndexerFlagSpecification</c>: <c>value</c> is the flag bit (Freeleech = 1, Internal = 32…).</summary>
public sealed class IndexerFlagSpecification(CustomFormatSpecificationDefinition definition, string formatName) : CustomFormatSpecificationBase(definition)
{
    public int Value { get; } = SpecificationFields.RequireInt(definition, formatName, "value");

    protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input) => Value != 0 && input.IndexerFlags.HasFlag((IndexerFlags)Value);
}

/// <summary>Radarr's <c>SizeSpecification</c>: <c>min</c> and <c>max</c> in GB, min exclusive, max inclusive. An unknown size never matches.</summary>
public sealed class SizeSpecification(CustomFormatSpecificationDefinition definition, string formatName) : CustomFormatSpecificationBase(definition)
{
    private const double Gigabyte = 1024d * 1024 * 1024;

    public double Min { get; } = SpecificationFields.GetDouble(definition, "min") ?? 0;

    public double Max { get; } = SpecificationFields.GetDouble(definition, "max") ?? throw new CustomFormatParseException($"Custom format '{formatName}', specification '{definition.Name}': field 'max' is required.");

    protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input) =>
        input.Size > 0 && input.Size > Min * Gigabyte && input.Size <= Max * Gigabyte;
}

/// <summary>Radarr's <c>YearSpecification</c>: the film's year between <c>min</c> and <c>max</c> inclusive.</summary>
public sealed class YearSpecification(CustomFormatSpecificationDefinition definition, string formatName) : CustomFormatSpecificationBase(definition)
{
    public int Min { get; } = SpecificationFields.GetInt(definition, "min") ?? 0;

    public int Max { get; } = SpecificationFields.GetInt(definition, "max") ?? throw new CustomFormatParseException($"Custom format '{formatName}', specification '{definition.Name}': field 'max' is required.");

    protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input) => input.Year is { } year && year >= Min && year <= Max;
}

/// <summary>Sonarr's <c>ReleaseTypeSpecification</c>: <c>value</c> is the release type (single episode = 1, multi-episode = 2, season pack = 3, multi-season pack = 4). A film never matches.</summary>
public sealed class ReleaseTypeSpecification(CustomFormatSpecificationDefinition definition, string formatName) : CustomFormatSpecificationBase(definition)
{
    public int Value { get; } = SpecificationFields.RequireInt(definition, formatName, "value");

    protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input) => (int)input.ReleaseType == Value;
}

/// <summary>
/// A specification Bigarr cannot evaluate, kept so the format is imported whole rather than silently changed: its
/// <c>implementation</c> field names the original, <c>reason</c> says why. It never matches, negated or not, so a required
/// one keeps the format from matching and an optional one changes nothing.
/// </summary>
public sealed class UnsupportedSpecification(CustomFormatSpecificationDefinition definition) : ICustomFormatSpecification
{
    public const string ImplementationName = "UnsupportedSpecification";

    public string Name => definition.Name;

    public string Implementation => ImplementationName;

    public bool Negate => definition.Negate;

    public bool Required => definition.Required;

    public string? Original => SpecificationFields.GetString(definition, "implementation");

    public string? Reason => SpecificationFields.GetString(definition, "reason");

    public bool IsSatisfiedBy(CustomFormatInput input) => false;
}
