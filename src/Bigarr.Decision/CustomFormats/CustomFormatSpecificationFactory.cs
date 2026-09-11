using Bigarr.Decision.CustomFormats.Specifications;

namespace Bigarr.Decision.CustomFormats;

/// <summary>Radarr's implementation names, so guide files import unchanged.</summary>
public static class CustomFormatSpecificationFactory
{
    public static readonly IReadOnlyList<string> KnownImplementations =
    [
        "ReleaseTitleSpecification",
        "ReleaseGroupSpecification",
        "EditionSpecification",
        "SourceSpecification",
        "ResolutionSpecification",
        "QualityModifierSpecification",
        "LanguageSpecification",
        "IndexerFlagSpecification",
        "SizeSpecification",
        "YearSpecification",
    ];

    public static ICustomFormatSpecification Create(CustomFormatSpecificationDefinition definition, string formatName)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition.Implementation switch
        {
            "ReleaseTitleSpecification" => new ReleaseTitleSpecification(definition, formatName),
            "ReleaseGroupSpecification" => new ReleaseGroupSpecification(definition, formatName),
            "EditionSpecification" => new EditionSpecification(definition, formatName),
            "SourceSpecification" => new SourceSpecification(definition, formatName),
            "ResolutionSpecification" => new ResolutionSpecification(definition, formatName),
            "QualityModifierSpecification" => new QualityModifierSpecification(definition, formatName),
            "LanguageSpecification" => new LanguageSpecification(definition, formatName),
            "IndexerFlagSpecification" => new IndexerFlagSpecification(definition, formatName),
            "SizeSpecification" => new SizeSpecification(definition, formatName),
            "YearSpecification" => new YearSpecification(definition, formatName),
            _ => throw new CustomFormatParseException($"Custom format '{formatName}': unknown specification implementation '{definition.Implementation}'. Known: {string.Join(", ", KnownImplementations)}."),
        };
    }
}
