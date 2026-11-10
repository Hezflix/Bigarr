using Bigarr.Api.Infrastructure;
using Bigarr.Domain.Quality;

namespace Bigarr.Api.Features.Quality;

/// <summary>A film version takes a Films or Both profile, a series version a Series or Both one.</summary>
public static class ProfileKindRules
{
    public static Failure? ForMovieVersion(QualityProfile? profile) =>
        profile is null || profile.Kind.ForMovies()
            ? null
            : Failure.Invalid("Not a film quality profile", $"'{profile.Name}' is a series profile: a film version takes a Films or Both profile.");

    public static Failure? ForSeriesVersion(QualityProfile? profile) =>
        profile is null || profile.Kind.ForSeries()
            ? null
            : Failure.Invalid("Not a series quality profile", $"'{profile.Name}' is a film profile: a series version takes a Series or Both profile.");
}
