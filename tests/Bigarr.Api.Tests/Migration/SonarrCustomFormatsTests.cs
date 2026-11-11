using System.Text.Json;
using Bigarr.Api.Features.Migration;
using Bigarr.Decision.CustomFormats;
using Bigarr.Decision.CustomFormats.Specifications;
using Bigarr.Domain.Acquisition;
using Bigarr.Parsing.Languages;
using EpisodeParser = Bigarr.Parsing.Episodes.Parser;

namespace Bigarr.Api.Tests.Migration;

/// <summary>
/// Sonarr's custom formats in Bigarr's terms, on payloads in Sonarr v4's shape: flags, languages and sources translated
/// from Sonarr's numbering to Radarr's (Bigarr's engine), Sonarr's release type evaluated, what Bigarr cannot evaluate kept
/// and marked rather than dropped, definitions compared regardless of order and labels.
/// </summary>
public sealed class SonarrCustomFormatsTests
{
    private static SonarrCustomFormats.Translated Translate(string json) => SonarrCustomFormats.Translate(JsonDocument.Parse(json).RootElement);

    private static int Value(CustomFormatSpecificationDefinition spec) => spec.Fields["value"].GetInt32();

    [Fact]
    public void Indexer_flags_move_to_bigarr_bits_and_flag_sets_translate_both_ways()
    {
        var internalFormat = Translate(SonarrFormatSamples.Internal());
        Assert.Empty(internalFormat.Notes);
        Assert.Equal((int)IndexerFlags.Internal, Value(internalFormat.Definition.Specifications.Single(s => s.Name == "Internal")));
        Assert.Equal((int)IndexerFlags.Scene, Value(internalFormat.Definition.Specifications.Single(s => s.Name == "Scene")));

        Assert.Equal(IndexerFlags.Internal | IndexerFlags.Scene, SonarrCustomFormats.Flags(8 | 16));
        Assert.Equal(IndexerFlags.Freeleech | IndexerFlags.Nuked | IndexerFlags.Freeleech75, SonarrCustomFormats.Flags(1 | 128 | 32 | 256));
        Assert.Equal(IndexerFlags.Freeleech | IndexerFlags.Internal, SonarrCustomFormats.ParseFlags("Freeleech, Internal"));
        Assert.Equal(IndexerFlags.Internal | IndexerFlags.Scene, SonarrCustomFormats.ParseFlags("24"));
        Assert.Equal(IndexerFlags.None, SonarrCustomFormats.ParseFlags(null));

        // The translated format matches a release flagged Internal the way the indexer reports it.
        var calculator = new CustomFormatCalculator([internalFormat.Definition with { Id = 1 }]);
        var parsed = EpisodeParser.ParseTitle("Show.S01E01.1080p.WEB-DL.DDP5.1.H.264-NTb")!;
        Assert.Single(calculator.Match(CustomFormatInput.FromEpisode(parsed, null, 1, IndexerFlags.Internal, 2020, null)));
        Assert.Empty(calculator.Match(CustomFormatInput.FromEpisode(parsed, null, 1, IndexerFlags.Freeleech, 2020, null)));
    }

    [Fact]
    public void Languages_follow_their_name_and_release_types_are_evaluated_on_episodes()
    {
        var pack = Translate(SonarrFormatSamples.HindiPack());
        Assert.Empty(pack.Notes);
        Assert.Equal(Language.Hindi.Id, Value(pack.Definition.Specifications.Single(s => s.Implementation == "LanguageSpecification")));
        Assert.Equal(3, Value(pack.Definition.Specifications.Single(s => s.Implementation == "ReleaseTypeSpecification")));

        var calculator = new CustomFormatCalculator([pack.Definition with { Id = 1 }]);
        var seasonPack = EpisodeParser.ParseTitle("Show.S01.1080p.WEB-DL.HINDI.DDP5.1.H.264-GRP")!;
        var single = EpisodeParser.ParseTitle("Show.S01E01.1080p.WEB-DL.HINDI.DDP5.1.H.264-GRP")!;
        Assert.Equal(ReleaseType.SeasonPack, CustomFormatInput.ReleaseTypeOf(seasonPack));
        Assert.Equal(ReleaseType.SingleEpisode, CustomFormatInput.ReleaseTypeOf(single));
        Assert.Equal(ReleaseType.MultiEpisode, CustomFormatInput.ReleaseTypeOf(EpisodeParser.ParseTitle("Show.S01E01E02.1080p.WEB-DL-GRP")!));
        Assert.Single(calculator.Match(CustomFormatInput.FromEpisode(seasonPack, null, 1, IndexerFlags.None, 2020, null)));
        Assert.Empty(calculator.Match(CustomFormatInput.FromEpisode(single, null, 1, IndexerFlags.None, 2020, null)));
    }

    [Fact]
    public void What_bigarr_cannot_evaluate_is_kept_marked_and_never_matches()
    {
        var odd = Translate(SonarrFormatSamples.Odd());
        Assert.Equal(3, odd.Definition.Specifications.Count);
        var subtitles = odd.Definition.Specifications.Single(s => s.Name == "Subs");
        Assert.Equal(UnsupportedSpecification.ImplementationName, subtitles.Implementation);
        Assert.Equal("IndexerFlagSpecification", subtitles.Fields["implementation"].GetString());
        Assert.Equal(UnsupportedSpecification.ImplementationName, odd.Definition.Specifications.Single(s => s.Name == "Future").Implementation);
        Assert.Equal(9, Value(odd.Definition.Specifications.Single(s => s.Name == "Remux"))); // BlurayRaw → BLURAY, approximated
        Assert.Equal(3, odd.Notes.Count);
        Assert.Contains(odd.Notes, n => n.Contains("Subtitles", StringComparison.Ordinal));
        Assert.Contains(odd.Notes, n => n.Contains("BlurayRaw", StringComparison.Ordinal));

        // The stored form parses back (so the library can show and evaluate it); the unsupported parts never match, negated or not.
        var stored = CustomFormatJson.ParseSpecifications(CustomFormatJson.SerializeSpecifications(odd.Definition.Specifications), "Odd");
        var unsupported = CustomFormatSpecificationFactory.Create(stored.Single(s => s.Name == "Future"), "Odd");
        var input = CustomFormatInput.FromEpisode(EpisodeParser.ParseTitle("Show.S01E01.1080p.BluRay.x264-GRP")!, null, 1, IndexerFlags.None, 2020, null);
        Assert.False(unsupported.IsSatisfiedBy(input));
        Assert.False(CustomFormatSpecificationFactory.Create(stored.Single(s => s.Name == "Future") with { Negate = true }, "Odd").IsSatisfiedBy(input));
    }

    [Fact]
    public void Definitions_compare_regardless_of_order_and_labels()
    {
        var a = Translate(SonarrFormatSamples.Group());
        var b = Translate(SonarrFormatSamples.Format(9, "WEB Tier 01",
            SonarrFormatSamples.Spec("Flux group", "ReleaseGroupSpecification", "\"^(FLUX)$\""),
            SonarrFormatSamples.Spec("NTb group", "ReleaseGroupSpecification", "\"^(NTb)$\"")));
        Assert.Equal(a.Key, b.Key);
        Assert.NotEqual(a.Key, Translate(SonarrFormatSamples.Format(9, "WEB Tier 01", SonarrFormatSamples.Spec("NTb", "ReleaseGroupSpecification", "\"^(NTb)$\""))).Key);
    }
}
