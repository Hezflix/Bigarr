namespace Bigarr.Domain.Quality;

/// <summary>Values follow Radarr's <c>QualitySource</c>.</summary>
public enum QualitySource
{
    Unknown = 0,
    Cam = 1,
    Telesync = 2,
    Telecine = 3,
    Workprint = 4,
    Dvd = 5,
    Tv = 6,
    WebDl = 7,
    WebRip = 8,
    Bluray = 9,
}
