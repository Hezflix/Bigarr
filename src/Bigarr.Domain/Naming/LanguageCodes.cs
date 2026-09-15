namespace Bigarr.Domain.Naming;

/// <summary>Two-letter codes for the <c>{MediaInfo AudioLanguages}</c> style tokens. Unknown names fall back to their first two letters.</summary>
public static class LanguageCodes
{
    private static readonly Dictionary<string, string> TwoLetter = new(StringComparer.OrdinalIgnoreCase)
    {
        ["English"] = "EN", ["French"] = "FR", ["Spanish"] = "ES", ["German"] = "DE", ["Italian"] = "IT", ["Danish"] = "DA",
        ["Dutch"] = "NL", ["Japanese"] = "JA", ["Icelandic"] = "IS", ["Chinese"] = "ZH", ["Russian"] = "RU", ["Polish"] = "PL",
        ["Vietnamese"] = "VI", ["Swedish"] = "SV", ["Norwegian"] = "NO", ["Finnish"] = "FI", ["Turkish"] = "TR", ["Portuguese"] = "PT",
        ["Flemish"] = "NL", ["Greek"] = "EL", ["Korean"] = "KO", ["Hungarian"] = "HU", ["Hebrew"] = "HE", ["Lithuanian"] = "LT",
        ["Czech"] = "CS", ["Arabic"] = "AR", ["Hindi"] = "HI", ["Bulgarian"] = "BG", ["Malayalam"] = "ML", ["Ukrainian"] = "UK",
        ["Slovak"] = "SK", ["Thai"] = "TH", ["Romanian"] = "RO", ["Latvian"] = "LV", ["Persian"] = "FA", ["Catalan"] = "CA",
        ["Croatian"] = "HR", ["Serbian"] = "SR", ["Bosnian"] = "BS", ["Estonian"] = "ET", ["Tamil"] = "TA", ["Indonesian"] = "ID",
        ["Telugu"] = "TE", ["Macedonian"] = "MK", ["Slovenian"] = "SL", ["Kannada"] = "KN", ["Marathi"] = "MR", ["Urdu"] = "UR",
        ["Bengali"] = "BN", ["Punjabi"] = "PA", ["Tagalog"] = "TL", ["Malay"] = "MS", ["Albanian"] = "SQ", ["Afrikaans"] = "AF",
        ["Georgian"] = "KA", ["Azerbaijani"] = "AZ", ["Uzbek"] = "UZ", ["Kazakh"] = "KK", ["Mongolian"] = "MN", ["Nepali"] = "NE",
        ["Sinhala"] = "SI", ["Khmer"] = "KM", ["Lao"] = "LO", ["Burmese"] = "MY", ["Amharic"] = "AM", ["Swahili"] = "SW",
    };

    public static string Of(string languageName)
    {
        ArgumentNullException.ThrowIfNull(languageName);
        if (TwoLetter.TryGetValue(languageName.Trim(), out var code))
        {
            return code;
        }

        var letters = new string(languageName.Where(char.IsLetter).Take(2).ToArray());
        return letters.Length == 0 ? "UN" : letters.ToUpperInvariant();
    }
}
