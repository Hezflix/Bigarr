namespace Bigarr.Domain.Movies;

/// <summary>Per version: when a version becomes searchable. Values follow Radarr.</summary>
public enum MinimumAvailability
{
    Announced = 1,
    InCinemas = 2,
    Released = 3,
}
