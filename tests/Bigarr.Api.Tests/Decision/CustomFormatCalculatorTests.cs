using System.Text.Json;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Acquisition;
using Bigarr.Parsing.Languages;
using Bigarr.Parsing.Qualities;

namespace Bigarr.Api.Tests.Decision;

/// <summary>The engine against the TRaSH guide files, imported unchanged: the acceptance test of M3 (blueprint §6).</summary>
public sealed class CustomFormatCalculatorTests
{
    [Fact]
    public void Every_guide_file_imports_unchanged()
    {
        Assert.Equal(20, TrashFixtures.All.Count);
        Assert.All(TrashFixtures.All, f =>
        {
            Assert.NotEmpty(f.Definition.Specifications);
            Assert.False(string.IsNullOrEmpty(f.Definition.TrashId));
            Assert.All(f.Definition.Specifications, s => Assert.Contains(s.Implementation, CustomFormatSpecificationFactory.KnownImplementations));
        });

        var remux = TrashFixtures.Get("Remux Tier 01");
        Assert.Equal("3a3ff47579026e76d6504ebea39390de", remux.TrashId);
        Assert.Equal(1950, remux.TrashDefaultScore);
        Assert.Equal(12, remux.Specifications.Count);
        Assert.Equal(100, TrashFixtures.Get("LQ").Specifications.Count);
        Assert.Null(TrashFixtures.Get("MULTi").TrashDefaultScore);
    }

    [Theory]
    [InlineData("The.Matrix.1999.1080p.BluRay.REMUX.AVC.DTS-HD.MA.5.1-FraMeSToR", "Remux Tier 01")]
    [InlineData("The.Matrix.1999.2160p.UHD.BluRay.REMUX.HDR.HEVC.TrueHD.Atmos-FraMeSToR", "Remux Tier 01", "TrueHD ATMOS")]
    [InlineData("The.Matrix.1999.REPACK.1080p.BluRay.x264-SPARKS", "Repack/Proper")]
    [InlineData("The.Matrix.1999.PROPER.1080p.BluRay.x264-SPARKS", "Repack/Proper")]
    [InlineData("The.Matrix.1999.1080p.BluRay.x265-RARBG", "LQ", "x265 (HD)", "x265 (no HDR/DV)")]
    [InlineData("The.Matrix.1999.1080p.WEB-DL.DDP5.1.H.264-NTb", "WEB Tier 01")]
    [InlineData("The.Matrix.1999.2160p.WEB-DL.DDP5.1.Atmos.DV.HDR.H.265-FLUX", "WEB Tier 01")]
    [InlineData("The.Matrix.1999.3D.1080p.BluRay.x264-GRP", "3D")]
    [InlineData("The.Matrix.1999.IMAX.Enhanced.1080p.DSNP.WEB-DL.DDP5.1.H.264-FLUX", "IMAX Enhanced", "WEB Tier 01")]
    [InlineData("The.Matrix.1999.REMASTERED.1080p.BluRay.x264-GRP", "Remaster")]
    [InlineData("The.Matrix.1999.1080p.BluRay.x264-CtrlHD", "HD Bluray Tier 01")]
    [InlineData("The.Matrix.1999.1080p.BluRay.REMUX.AVC.DTS-HD.MA.5.1-CtrlHD")]
    [InlineData("The.Matrix.1999.2160p.UHD.BluRay.x265.HDR.DTS-HD.MA.5.1-DON", "UHD Bluray Tier 01")]
    [InlineData("The.Matrix.1999.2160p.WEB-DL.DDP5.1.H.265-DON")]
    [InlineData("The.Matrix.1999.TRUEFRENCH.1080p.BluRay.x264-GRP", "Language: Not English", "VFF")]
    [InlineData("The.Matrix.1999.FRENCH.1080p.BluRay.x264-GRP", "Language: Not English")]
    [InlineData("The.Matrix.1999.1080p.BluRay.x264-SPARKS")]
    [InlineData("The.Matrix.1999.1080p.BluRay.REMUX.AVC.DTS-HD.MA.5.1-EPSiLON")]
    public void Releases_match_the_same_guide_formats_as_radarr(string title, params string[] expected) =>
        Assert.Equal(expected.OrderBy(e => e), TrashFixtures.Match(title).OrderBy(e => e));

    [Fact]
    public void Multi_is_a_format_of_its_own()
    {
        Assert.Contains("MULTi", TrashFixtures.Match("The.Matrix.1999.MULTi.1080p.BluRay.x264-GRP"));
        Assert.DoesNotContain("MULTi", TrashFixtures.Match("The.Matrix.1999.MULTi-SUBS.1080p.BluRay.x264-GRP"));
    }

    [Fact]
    public void Required_and_negated_specifications_combine_like_radarr()
    {
        // Repack/Proper: "repack" required, "repack2" negated and required → a second repack is not a repack for the guide.
        Assert.Contains("Repack/Proper", TrashFixtures.Match("The.Matrix.1999.REPACK.1080p.BluRay.x264-SPARKS"));
        Assert.DoesNotContain("Repack/Proper", TrashFixtures.Match("The.Matrix.1999.REPACK2.1080p.BluRay.x264-SPARKS"));

        // x265 (HD): x265 required and "not 2160p" required → UHD x265 is fine, HD x265 is not.
        Assert.Contains("x265 (HD)", TrashFixtures.Match("The.Matrix.1999.1080p.BluRay.x265-GRP"));
        Assert.DoesNotContain("x265 (HD)", TrashFixtures.Match("The.Matrix.1999.2160p.UHD.BluRay.x265.HDR.DTS-HD.MA.5.1-TERMiNAL"));
        Assert.DoesNotContain("x265 (no HDR/DV)", TrashFixtures.Match("The.Matrix.1999.1080p.WEB-DL.x265.HDR-GRP"));

        // WEB Tier 01: the group list is "any of", the source list is "any of", both groups must pass.
        Assert.Contains("WEB Tier 01", TrashFixtures.Match("The.Matrix.1999.1080p.WEBRip.x264-NTb"));
        Assert.DoesNotContain("WEB Tier 01", TrashFixtures.Match("The.Matrix.1999.1080p.BluRay.x264-NTb"));

        // TrueHD ATMOS: every negated audio spec must hold.
        Assert.Contains("TrueHD ATMOS", TrashFixtures.Match("The.Matrix.1999.1080p.BluRay.REMUX.AVC.TrueHD.7.1.Atmos-GRP"));
        Assert.DoesNotContain("TrueHD ATMOS", TrashFixtures.Match("The.Matrix.1999.1080p.BluRay.REMUX.AVC.TrueHD.Atmos.DTS-HD.MA.5.1-GRP"));
    }

    [Fact]
    public void A_release_without_language_speaks_the_films_original_language()
    {
        var plain = "The.Matrix.1999.1080p.BluRay.x264-GRP";
        Assert.DoesNotContain("Language: Not English", TrashFixtures.Match(plain, originalLanguage: "en"));
        Assert.Contains("Language: Not English", TrashFixtures.Match(plain, originalLanguage: "fr"));
        Assert.Contains("Language: Not English", TrashFixtures.Match("Le.Fabuleux.Destin.dAmelie.Poulain.2001.FRENCH.1080p.BluRay.x264-GRP", year: 2001, originalLanguage: "fr"));
        Assert.DoesNotContain("Language: Not English", TrashFixtures.Match("Amelie.2001.FRENCH.ENGLISH.1080p.BluRay.x264-GRP", year: 2001, originalLanguage: "fr"));
    }

    [Fact]
    public void Group_regexes_never_match_the_films_title()
    {
        // A film called "Hybrid" is not a hybrid release; the parser masks the title before the formats look at it.
        Assert.DoesNotContain("Hybrid", TrashFixtures.Match("Hybrid.2007.1080p.BluRay.x264-GRP", year: 2007));
        Assert.Contains("Hybrid", TrashFixtures.Match("The.Matrix.1999.Hybrid.1080p.BluRay.x264-GRP"));
    }

    [Fact]
    public void Radarr_export_shape_parses_like_the_guide_shape()
    {
        const string export = """
        [{
          "name": "Exported",
          "includeCustomFormatWhenRenaming": true,
          "specifications": [
            { "name": "Group", "implementation": "ReleaseGroupSpecification", "negate": false, "required": true,
              "fields": [ { "order": 0, "name": "value", "label": "Regular Expression", "value": "^(SPARKS)$", "type": "textbox" } ] },
            { "name": "Not 4K", "implementation": "ResolutionSpecification", "negate": true, "required": true,
              "fields": [ { "name": "value", "value": 2160 } ] }
          ]
        }]
        """;

        var definition = Assert.Single(CustomFormatJson.ParseMany(export));
        Assert.Equal("Exported", definition.Name);
        Assert.True(definition.IncludeWhenRenaming);
        Assert.Null(definition.TrashId);
        Assert.Equal(2, definition.Specifications.Count);
        Assert.Equal("^(SPARKS)$", definition.Specifications[0].Fields["value"].GetString());
        Assert.Equal(2160, definition.Specifications[1].Fields["value"].GetInt32());

        var roundTrip = CustomFormatJson.ParseSpecifications(CustomFormatJson.SerializeSpecifications(definition.Specifications), "Exported");
        Assert.Equal(definition.Specifications.Select(s => (s.Name, s.Implementation, s.Negate, s.Required)), roundTrip.Select(s => (s.Name, s.Implementation, s.Negate, s.Required)));

        var calculator = new CustomFormatCalculator([definition with { Id = 1 }]);
        Assert.Single(calculator.Match(Input("The.Matrix.1999.1080p.BluRay.x264-SPARKS", QualitySource.BLURAY, 1080, group: "SPARKS")));
        Assert.Empty(calculator.Match(Input("The.Matrix.1999.2160p.BluRay.x265-SPARKS", QualitySource.BLURAY, 2160, group: "SPARKS")));
    }

    [Theory]
    [InlineData("""{ "name": "x", "specifications": [ { "implementation": "NopeSpecification", "fields": { "value": 1 } } ] }""", "unknown specification implementation")]
    [InlineData("""{ "name": "x", "specifications": [ { "implementation": "ReleaseTitleSpecification", "fields": { "value": "(" } } ] }""", "invalid regular expression")]
    [InlineData("""{ "name": "x", "specifications": [] }""", "would match everything")]
    [InlineData("""{ "specifications": [ { "implementation": "ReleaseTitleSpecification", "fields": { "value": "a" } } ] }""", "needs a name")]
    [InlineData("""{ "name": "x", "specifications": [ { "implementation": "SizeSpecification", "fields": { "min": 1 } } ] }""", "'max' is required")]
    [InlineData("""not json""", "Not valid JSON")]
    [InlineData("""42""", "Expected a custom format object")]
    public void Bad_definitions_are_refused_with_a_reason(string json, string reason)
    {
        var ex = Assert.Throws<CustomFormatParseException>(() => CustomFormatJson.ParseMany(json));
        Assert.Contains(reason, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_specification_type_reads_its_fields()
    {
        var input = Input("The.Matrix.1999.1080p.BluRay.REMUX.AVC-GRP", QualitySource.BLURAY, 1080, Modifier.REMUX, group: "GRP", edition: "Directors Cut",
            languages: [Language.French], size: 20L * 1024 * 1024 * 1024, flags: IndexerFlags.Freeleech | IndexerFlags.Internal, year: 1999, original: Language.English);

        Assert.True(Spec("SourceSpecification", ("value", 9)).IsSatisfiedBy(input));
        Assert.False(Spec("SourceSpecification", ("value", 7)).IsSatisfiedBy(input));
        Assert.True(Spec("ResolutionSpecification", ("value", 1080)).IsSatisfiedBy(input));
        Assert.True(Spec("ResolutionSpecification", ("value", "1080")).IsSatisfiedBy(input)); // TRaSH sometimes quotes numbers
        Assert.True(Spec("QualityModifierSpecification", ("value", 5)).IsSatisfiedBy(input));
        Assert.False(Spec("QualityModifierSpecification", ("value", 4)).IsSatisfiedBy(input));
        Assert.True(Spec("EditionSpecification", ("value", "director'?s cut")).IsSatisfiedBy(input));
        Assert.True(Spec("ReleaseGroupSpecification", ("value", "^(GRP)$")).IsSatisfiedBy(input));
        Assert.False(Spec("ReleaseGroupSpecification", ("value", "^(RP)$")).IsSatisfiedBy(input));
        Assert.True(Spec("ReleaseTitleSpecification", ("value", "\\bREMUX\\b")).IsSatisfiedBy(input));

        Assert.True(Spec("IndexerFlagSpecification", ("value", 1)).IsSatisfiedBy(input));
        Assert.True(Spec("IndexerFlagSpecification", ("value", 32)).IsSatisfiedBy(input));
        Assert.False(Spec("IndexerFlagSpecification", ("value", 2)).IsSatisfiedBy(input));

        Assert.True(Spec("SizeSpecification", ("min", 10), ("max", 20)).IsSatisfiedBy(input)); // max inclusive
        Assert.False(Spec("SizeSpecification", ("min", 20), ("max", 30)).IsSatisfiedBy(input)); // min exclusive
        Assert.False(Spec("SizeSpecification", ("min", 0), ("max", 100)).IsSatisfiedBy(input with { Size = 0 })); // unknown size never matches

        Assert.True(Spec("YearSpecification", ("min", 1990), ("max", 1999)).IsSatisfiedBy(input));
        Assert.False(Spec("YearSpecification", ("min", 2000), ("max", 2010)).IsSatisfiedBy(input));
        Assert.False(Spec("YearSpecification", ("min", 1990), ("max", 1999)).IsSatisfiedBy(input with { Year = null }));

        Assert.True(Spec("LanguageSpecification", ("value", 2)).IsSatisfiedBy(input)); // French
        Assert.False(Spec("LanguageSpecification", ("value", 1)).IsSatisfiedBy(input)); // English
        Assert.True(Spec("LanguageSpecification", ("value", 1), ("exceptLanguage", true)).IsSatisfiedBy(input)); // something other than English
        Assert.False(Spec("LanguageSpecification", ("value", 2), ("exceptLanguage", true)).IsSatisfiedBy(input));
        Assert.False(Spec("LanguageSpecification", ("value", -2)).IsSatisfiedBy(input)); // Original = English here, release is French
        Assert.True(Spec("LanguageSpecification", ("value", -2)).IsSatisfiedBy(input with { Languages = [Language.English] }));
        Assert.True(Spec("LanguageSpecification", ("value", 2), ("negate", true)).Negate);
    }

    [Fact]
    public void Matching_groups_specifications_by_type()
    {
        var specs = new List<ICustomFormatSpecification>
        {
            Spec("ReleaseGroupSpecification", ("value", "^(A)$")),
            Spec("ReleaseGroupSpecification", ("value", "^(B)$")),
            Spec("SourceSpecification", ("value", 9), ("required", true)),
        };

        Assert.True(CustomFormatCalculator.Matches(specs, Input("x", QualitySource.BLURAY, 1080, group: "A")));
        Assert.True(CustomFormatCalculator.Matches(specs, Input("x", QualitySource.BLURAY, 1080, group: "B")));
        Assert.False(CustomFormatCalculator.Matches(specs, Input("x", QualitySource.BLURAY, 1080, group: "C"))); // no group matched
        Assert.False(CustomFormatCalculator.Matches(specs, Input("x", QualitySource.WEBDL, 1080, group: "A"))); // required source failed
        Assert.False(CustomFormatCalculator.Matches([], Input("x", QualitySource.BLURAY, 1080)));
    }

    [Fact]
    public void Languages_map_between_iso_codes_names_and_the_parser()
    {
        Assert.Equal(Language.English, CustomFormatInput.LanguageFromIsoCode("en"));
        Assert.Equal(Language.French, CustomFormatInput.LanguageFromIsoCode("fr"));
        Assert.Equal(Language.PortugueseBR, CustomFormatInput.LanguageFromIsoCode("pt-BR"));
        Assert.Null(CustomFormatInput.LanguageFromIsoCode(null));
        Assert.Equal(Language.French, CustomFormatInput.LanguageFromName("french"));
        Assert.Equal(Language.German, CustomFormatInput.LanguageFromName("German"));
        Assert.Equal([Language.English], CustomFormatInput.EffectiveLanguages([Language.Unknown], Language.English));
        Assert.Equal([Language.French], CustomFormatInput.EffectiveLanguages([Language.French, Language.Unknown], Language.English));
        Assert.Empty(CustomFormatInput.EffectiveLanguages([], null));
    }

    private static CustomFormatInput Input(
        string title,
        QualitySource source,
        int resolution,
        Modifier modifier = Modifier.NONE,
        string? group = null,
        string? edition = null,
        IReadOnlyList<Language>? languages = null,
        long size = 0,
        IndexerFlags flags = IndexerFlags.None,
        int? year = 1999,
        Language? original = null) =>
        new(title, null, source, resolution, modifier, languages ?? [Language.English], original ?? Language.English, edition, group, size, flags, year);

    private static ICustomFormatSpecification Spec(string implementation, params (string Key, object Value)[] fields)
    {
        var negate = fields.Any(f => f.Key == "negate" && f.Value is true);
        var required = fields.Any(f => f.Key == "required" && f.Value is true);
        var json = JsonSerializer.SerializeToElement(fields.Where(f => f.Key is not ("negate" or "required")).ToDictionary(f => f.Key, f => f.Value));
        var definition = new CustomFormatSpecificationDefinition(implementation, implementation, negate, required, json.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase));
        return CustomFormatSpecificationFactory.Create(definition, "test");
    }
}
