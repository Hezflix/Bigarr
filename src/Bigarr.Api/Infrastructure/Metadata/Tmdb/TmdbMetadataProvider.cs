using System.Globalization;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Movies;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Metadata.Tmdb;

public sealed class TmdbMetadataProvider(TmdbClient client, IOptions<MetadataOptions> options, TimeProvider timeProvider) : IMetadataProvider
{
    /// <summary>TMDB genre ids → names, for search results which only carry ids. Stable since 2016.</summary>
    private static readonly Dictionary<int, string> GenreNames = new()
    {
        [28] = "Action",
        [12] = "Adventure",
        [16] = "Animation",
        [35] = "Comedy",
        [80] = "Crime",
        [99] = "Documentary",
        [18] = "Drama",
        [10751] = "Family",
        [14] = "Fantasy",
        [36] = "History",
        [27] = "Horror",
        [10402] = "Music",
        [9648] = "Mystery",
        [10749] = "Romance",
        [878] = "Science Fiction",
        [10770] = "TV Movie",
        [53] = "Thriller",
        [10752] = "War",
        [37] = "Western",
    };

    public string Name => "tmdb";

    public async Task<MovieMetadata?> GetMovieAsync(int tmdbId, CancellationToken cancellationToken)
    {
        var details = await client.GetMovieAsync(tmdbId, cancellationToken);
        return details is null ? null : Map(details);
    }

    public async Task<MovieMetadata?> GetMovieByImdbIdAsync(string imdbId, CancellationToken cancellationToken)
    {
        var found = await client.FindByImdbIdAsync(imdbId, cancellationToken);
        var first = found?.MovieResults.FirstOrDefault();
        return first is null ? null : await GetMovieAsync(first.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<MovieMetadata>> SearchAsync(string query, int? year, CancellationToken cancellationToken)
    {
        var response = await client.SearchMoviesAsync(query, year, cancellationToken);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return response?.Results.Select(r => MapSearchResult(r, today)).ToList() ?? [];
    }

    private MovieMetadata Map(TmdbMovieDetails d)
    {
        var releaseDate = ParseDate(d.ReleaseDate);
        var (inCinemas, digital, physical, certification) = ExtractReleaseDates(d.ReleaseDates, options.Value.CertificationCountry);

        var alternativeTitles = new List<AlternativeTitle>();
        if (!string.IsNullOrWhiteSpace(d.OriginalTitle) && !string.Equals(d.OriginalTitle, d.Title, StringComparison.OrdinalIgnoreCase))
        {
            alternativeTitles.Add(new AlternativeTitle(d.OriginalTitle, null));
        }

        foreach (var alt in d.AlternativeTitles?.Titles ?? [])
        {
            if (!string.IsNullOrWhiteSpace(alt.Title)
                && !string.Equals(alt.Title, d.Title, StringComparison.OrdinalIgnoreCase)
                && !alternativeTitles.Any(a => string.Equals(a.Title, alt.Title, StringComparison.OrdinalIgnoreCase)))
            {
                alternativeTitles.Add(new AlternativeTitle(alt.Title, alt.Iso31661));
            }
        }

        return new MovieMetadata
        {
            TmdbId = d.Id,
            ImdbId = NormalizeImdbId(d.ImdbId ?? d.ExternalIds?.ImdbId),
            Title = d.Title ?? d.OriginalTitle ?? $"TMDB {d.Id}",
            OriginalTitle = d.OriginalTitle,
            Year = (inCinemas ?? releaseDate ?? digital ?? physical)?.Year,
            Runtime = d.Runtime is > 0 ? d.Runtime : null,
            Overview = string.IsNullOrWhiteSpace(d.Overview) ? null : d.Overview,
            Certification = certification,
            OriginalLanguage = d.OriginalLanguage,
            Genres = d.Genres.Select(g => g.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Cast<string>().ToList(),
            Ratings = Ratings(d.VoteAverage, d.VoteCount),
            AlternativeTitles = alternativeTitles,
            Status = MapStatus(d.Status),
            InCinemasDate = inCinemas ?? releaseDate,
            DigitalReleaseDate = digital,
            PhysicalReleaseDate = physical,
            CollectionTmdbId = d.BelongsToCollection?.Id,
            Images = Images(d.PosterPath, d.BackdropPath),
        };
    }

    private MovieMetadata MapSearchResult(TmdbSearchResult r, DateOnly today)
    {
        var releaseDate = ParseDate(r.ReleaseDate);
        var alternativeTitles = new List<AlternativeTitle>();
        if (!string.IsNullOrWhiteSpace(r.OriginalTitle) && !string.Equals(r.OriginalTitle, r.Title, StringComparison.OrdinalIgnoreCase))
        {
            alternativeTitles.Add(new AlternativeTitle(r.OriginalTitle, null));
        }

        return new MovieMetadata
        {
            TmdbId = r.Id,
            Title = r.Title ?? r.OriginalTitle ?? $"TMDB {r.Id}",
            OriginalTitle = r.OriginalTitle,
            Year = releaseDate?.Year,
            Overview = string.IsNullOrWhiteSpace(r.Overview) ? null : r.Overview,
            OriginalLanguage = r.OriginalLanguage,
            Genres = r.GenreIds.Select(id => GenreNames.GetValueOrDefault(id)).Where(n => n is not null).Cast<string>().ToList(),
            Ratings = Ratings(r.VoteAverage, r.VoteCount),
            AlternativeTitles = alternativeTitles,
            // Search results carry no status: approximate from the primary release date. Adding a film refetches the details.
            Status = releaseDate is null ? MovieStatus.Tba : releaseDate <= today ? MovieStatus.Released : MovieStatus.Announced,
            InCinemasDate = releaseDate,
            Images = Images(r.PosterPath, r.BackdropPath),
        };
    }

    private List<MediaImage> Images(string? posterPath, string? backdropPath)
    {
        var images = new List<MediaImage>();
        var baseUrl = options.Value.ImageBaseUrl.TrimEnd('/') + "/original";
        if (!string.IsNullOrWhiteSpace(posterPath))
        {
            images.Add(new MediaImage(ImageType.Poster, baseUrl + posterPath));
        }

        if (!string.IsNullOrWhiteSpace(backdropPath))
        {
            images.Add(new MediaImage(ImageType.Fanart, baseUrl + backdropPath));
        }

        return images;
    }

    private static MovieRatings? Ratings(double? average, int? votes) =>
        average is > 0 && votes is > 0 ? new MovieRatings { Tmdb = new RatingValue(average.Value, votes.Value) } : null;

    private static (DateOnly? InCinemas, DateOnly? Digital, DateOnly? Physical, string? Certification) ExtractReleaseDates(
        TmdbReleaseDates? releaseDates, string certificationCountry)
    {
        DateOnly? inCinemas = null, digital = null, physical = null;
        string? certification = null;

        foreach (var country in releaseDates?.Results ?? [])
        {
            foreach (var release in country.ReleaseDates)
            {
                var date = ParseDate(release.ReleaseDate);
                switch (release.Type)
                {
                    case 2 or 3:
                        inCinemas = Min(inCinemas, date);
                        break;
                    case 4:
                        digital = Min(digital, date);
                        break;
                    case 5:
                        physical = Min(physical, date);
                        break;
                }

                if (certification is null
                    && string.Equals(country.Iso31661, certificationCountry, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(release.Certification))
                {
                    certification = release.Certification;
                }
            }
        }

        return (inCinemas, digital, physical, certification);
    }

    private static DateOnly? Min(DateOnly? current, DateOnly? candidate) =>
        candidate is null ? current : current is null || candidate < current ? candidate : current;

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var datePart = value.Length >= 10 ? value[..10] : value;
        return DateOnly.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    private static MovieStatus MapStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "released" => MovieStatus.Released,
        "in production" or "post production" => MovieStatus.Announced,
        "canceled" => MovieStatus.Deleted,
        _ => MovieStatus.Tba,
    };

    private static string? NormalizeImdbId(string? imdbId) =>
        string.IsNullOrWhiteSpace(imdbId) ? null : imdbId.Trim();
}
