namespace Bigarr.Api.Infrastructure.Metadata;

/// <summary>TVDB speaks ISO 639-2 and ISO 3166-1 alpha-3 (<c>jpn</c>, <c>usa</c>); Bigarr stores TMDB's two-letter codes.</summary>
public static class IsoCodes
{
    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eng"] = "en", ["jpn"] = "ja", ["kor"] = "ko", ["zho"] = "zh", ["chi"] = "zh", ["fra"] = "fr", ["fre"] = "fr",
        ["deu"] = "de", ["ger"] = "de", ["spa"] = "es", ["ita"] = "it", ["por"] = "pt", ["pt"] = "pt", ["rus"] = "ru",
        ["swe"] = "sv", ["dan"] = "da", ["nor"] = "no", ["nld"] = "nl", ["dut"] = "nl", ["pol"] = "pl", ["tur"] = "tr",
        ["hin"] = "hi", ["tha"] = "th", ["ara"] = "ar", ["heb"] = "he", ["fin"] = "fi", ["ces"] = "cs", ["hun"] = "hu",
        ["ell"] = "el", ["ukr"] = "uk", ["ind"] = "id", ["vie"] = "vi", ["tgl"] = "tl", ["isl"] = "is",
    };

    private static readonly Dictionary<string, string> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["usa"] = "us", ["gbr"] = "gb", ["jpn"] = "jp", ["kor"] = "kr", ["can"] = "ca", ["aus"] = "au", ["fra"] = "fr",
        ["deu"] = "de", ["esp"] = "es", ["ita"] = "it", ["chn"] = "cn", ["twn"] = "tw", ["ind"] = "in", ["bra"] = "br",
        ["mex"] = "mx", ["swe"] = "se", ["dnk"] = "dk", ["nor"] = "no", ["nld"] = "nl", ["irl"] = "ie", ["nzl"] = "nz",
        ["rus"] = "ru", ["bel"] = "be", ["fin"] = "fi", ["pol"] = "pl", ["tur"] = "tr", ["tha"] = "th", ["arg"] = "ar",
        ["isr"] = "il", ["zaf"] = "za", ["aut"] = "at", ["che"] = "ch", ["prt"] = "pt",
    };

    /// <summary>Two-letter language code, or null when unknown.</summary>
    public static string? Language(string? code) => Normalize(code, Languages);

    /// <summary>Two-letter country code (lower case), or null when unknown.</summary>
    public static string? Country(string? code) => Normalize(code, Countries);

    private static string? Normalize(string? code, Dictionary<string, string> map)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var trimmed = code.Trim();
        if (trimmed.Length == 2)
        {
            return trimmed.ToLowerInvariant();
        }

        return map.GetValueOrDefault(trimmed);
    }
}
