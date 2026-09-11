using Bigarr.Parsing.Languages;

namespace Bigarr.Decision.CustomFormats.Specifications;

/// <summary>
/// Radarr's <c>LanguageSpecification</c>: <c>value</c> is a language id (English = 1, French = 2… Original = -2, resolved
/// against the film's original language). <c>exceptLanguage</c> flips to "any language other than this one".
/// </summary>
public sealed class LanguageSpecification(CustomFormatSpecificationDefinition definition, string formatName) : CustomFormatSpecificationBase(definition)
{
    public int Value { get; } = SpecificationFields.RequireInt(definition, formatName, "value");

    public bool ExceptLanguage { get; } = SpecificationFields.GetBool(definition, "exceptLanguage");

    protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input)
    {
        var compared = Value == Language.Original.Id && input.MovieOriginalLanguage is not null
            ? input.MovieOriginalLanguage
            : Language.All.FirstOrDefault(l => l.Id == Value) ?? (Value == Language.Original.Id ? Language.Original : Language.Any);

        if (ExceptLanguage)
        {
            return input.Languages.Any(l => l.Id != compared.Id);
        }

        return input.Languages.Any(l => l.Id == compared.Id);
    }
}
