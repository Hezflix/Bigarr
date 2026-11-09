namespace Bigarr.Domain.Tv;

/// <summary>How releases and files of the series are numbered, as in Sonarr. Chosen at add, editable.</summary>
public enum SeriesType
{
    /// <summary>S01E01.</summary>
    Standard = 1,

    /// <summary>Absolute numbers first (one piece 1071), season numbering as a fallback.</summary>
    Anime = 2,

    /// <summary>Air date (talk shows, news): 2026.11.09.</summary>
    Daily = 3,
}
