namespace Bigarr.Domain.Discover;

/// <summary>What a row of the Discover page shows. Stored as text.</summary>
public enum DiscoverSliderKind
{
    // Built in: each at most once, never deleted, only switched off.
    Trending = 1,
    PopularMovies = 2,
    UpcomingMovies = 3,
    MovieGenres = 4,
    Studios = 5,
    PopularSeries = 6,
    UpcomingSeries = 7,
    SeriesGenres = 8,
    Networks = 9,

    // Added by the admin: each carries a value (a TMDB id, or the words searched for) and a title.
    MovieGenre = 20,
    SeriesGenre = 21,
    MovieKeyword = 22,
    SeriesKeyword = 23,
    Studio = 24,
    Network = 25,
    MovieSearch = 26,
    SeriesSearch = 27,
}

/// <summary>A row of the Discover page: a TMDB list of films or series, or tiles leading to one.</summary>
public sealed class DiscoverSlider
{
    public int Id { get; set; }

    public DiscoverSliderKind Kind { get; set; }

    /// <summary>The admin's title. Null for a built-in row, which the page names in the reader's language.</summary>
    public string? Title { get; set; }

    /// <summary>The TMDB id (genre, keyword, studio, network) or the search words of a custom row; null for a built-in one.</summary>
    public string? Value { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Rows are shown in ascending order.</summary>
    public int Position { get; set; }

    public bool IsBuiltIn => IsBuiltInKind(Kind);

    public static bool IsBuiltInKind(DiscoverSliderKind kind) => (int)kind < (int)DiscoverSliderKind.MovieGenre;

    /// <summary>Rows whose value is a TMDB id rather than search words.</summary>
    public static bool TakesId(DiscoverSliderKind kind) =>
        kind is DiscoverSliderKind.MovieGenre or DiscoverSliderKind.SeriesGenre or DiscoverSliderKind.MovieKeyword
            or DiscoverSliderKind.SeriesKeyword or DiscoverSliderKind.Studio or DiscoverSliderKind.Network;

    /// <summary>Rows of tiles (genres, studios, networks), each leading to its own list, instead of titles.</summary>
    public static bool ShowsTiles(DiscoverSliderKind kind) =>
        kind is DiscoverSliderKind.MovieGenres or DiscoverSliderKind.SeriesGenres or DiscoverSliderKind.Studios or DiscoverSliderKind.Networks;

    /// <summary>The rows a new install starts with, in this order: Seerr's defaults minus what reads a media server.</summary>
    public static readonly DiscoverSliderKind[] Defaults =
    [
        DiscoverSliderKind.Trending,
        DiscoverSliderKind.PopularMovies,
        DiscoverSliderKind.MovieGenres,
        DiscoverSliderKind.UpcomingMovies,
        DiscoverSliderKind.Studios,
        DiscoverSliderKind.PopularSeries,
        DiscoverSliderKind.SeriesGenres,
        DiscoverSliderKind.UpcomingSeries,
        DiscoverSliderKind.Networks,
    ];
}
