namespace Bigarr.Api.Tests.Migration;

/// <summary>Custom formats in the shape Sonarr v4's <c>GET /api/v3/customformat</c> returns (fields as arrays, labels, select options left out).</summary>
internal static class SonarrFormatSamples
{
    public static string Field(string name, string value) => $$"""{"order":0,"name":"{{name}}","label":"{{name}}","value":{{value}},"type":"textbox","advanced":false,"privacy":"normal","isFloat":false}""";

    public static string Spec(string name, string implementation, string value, bool negate = false, bool required = false, string? extraField = null) =>
        $$"""{"name":"{{name}}","implementation":"{{implementation}}","implementationName":"{{implementation.Replace("Specification", string.Empty, StringComparison.Ordinal)}}","infoLink":"https://wiki.servarr.com/sonarr/settings#custom-formats-2","negate":{{(negate ? "true" : "false")}},"required":{{(required ? "true" : "false")}},"fields":[{{Field("value", value)}}{{(extraField is null ? string.Empty : "," + extraField)}}]}""";

    public static string Format(int id, string name, params string[] specs) =>
        $$"""{"id":{{id}},"name":"{{name}}","includeCustomFormatWhenRenaming":false,"specifications":[{{string.Join(",", specs)}}]}""";

    /// <summary>x265 (HD) as TRaSH publishes it for Sonarr: a title regex and a negated 2160p resolution.</summary>
    public static string X265(int id = 1) => Format(id, "x265 (HD)",
        Spec("x265", "ReleaseTitleSpecification", "\"[xh][ ._-]?265|\\\\bHEVC(\\\\b|\\\\d)\"", required: true),
        Spec("Not 2160p", "ResolutionSpecification", "2160", negate: true, required: true));

    public static string Repack(int id = 2) => Format(id, "Repack/Proper", Spec("Repack", "ReleaseTitleSpecification", "\"\\\\b(repack|proper)\\\\b\""));

    /// <summary>An indexer-flag format: Sonarr's Internal is 8 (Bigarr's 32), Scene 16 (128).</summary>
    public static string Internal(int id = 3) => Format(id, "Internal", Spec("Internal", "IndexerFlagSpecification", "8"), Spec("Scene", "IndexerFlagSpecification", "16"));

    public static string Group(int id = 4) => Format(id, "WEB Tier 01", Spec("NTb", "ReleaseGroupSpecification", "\"^(NTb)$\""), Spec("FLUX", "ReleaseGroupSpecification", "\"^(FLUX)$\""));

    /// <summary>A season pack (release type 3) in Hindi (Sonarr's 27, Bigarr's 26).</summary>
    public static string HindiPack(int id = 5) => Format(id, "Hindi Pack",
        Spec("Season Pack", "ReleaseTypeSpecification", "3", required: true),
        Spec("Hindi", "LanguageSpecification", "27", required: true, extraField: Field("exceptLanguage", "false")));

    /// <summary>What Bigarr cannot evaluate: Sonarr's Subtitles flag, a remux source (approximated) and an unknown implementation.</summary>
    public static string Odd(int id = 6) => Format(id, "Odd",
        Spec("Subs", "IndexerFlagSpecification", "256"),
        Spec("Remux", "SourceSpecification", "7"),
        Spec("Future", "FutureSpecification", "1"));

    public static string All(params string[] formats) => "[" + string.Join(",", formats) + "]";
}
