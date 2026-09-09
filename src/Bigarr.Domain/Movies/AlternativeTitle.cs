namespace Bigarr.Domain.Movies;

/// <param name="Language">Provider hint: an ISO 3166-1 country code for TMDB alternative titles, or null.</param>
public sealed record AlternativeTitle(string Title, string? Language);
