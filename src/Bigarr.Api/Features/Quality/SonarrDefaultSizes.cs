namespace Bigarr.Api.Features.Quality;

/// <summary>
/// Sonarr's default quality sizes (MB per minute), <c>Quality.DefaultQualityDefinitions</c> in Sonarr's
/// <c>src/NzbDrone.Core/Qualities/Quality.cs</c>, develop at <c>c018578e4b79ee048575c02d7a8aa2b929e4b837</c>
/// (2026-11-03), keyed by Bigarr's quality ids (Radarr's: Sonarr's remuxes and Bluray-480p/576p carry other ids there).
/// Qualities Sonarr does not have (CAM, telesync, DVD-R, BR-DISK…) take Sonarr's Unknown limits. A null max is no limit.
/// </summary>
public static class SonarrDefaultSizes
{
    public sealed record Sizes(double? Min, double? Preferred, double? Max);

    private static readonly Sizes Unknown = new(1, 95, 199.9);

    public static readonly IReadOnlyDictionary<int, Sizes> ByQualityId = new Dictionary<int, Sizes>
    {
        [0] = Unknown, // Unknown
        [1] = new(2, 95, 100), // SDTV
        [12] = new(2, 95, 100), // WEBRip-480p
        [8] = new(2, 95, 100), // WEBDL-480p
        [2] = new(2, 95, 100), // DVD
        [20] = new(2, 95, 100), // Bluray-480p
        [21] = new(2, 95, 100), // Bluray-576p
        [4] = new(3, 95, 125), // HDTV-720p
        [9] = new(4, 95, 125), // HDTV-1080p
        [10] = new(4, 95, null), // Raw-HD
        [14] = new(3, 95, 130), // WEBRip-720p
        [5] = new(3, 95, 130), // WEBDL-720p
        [6] = new(4, 95, 130), // Bluray-720p
        [15] = new(4, 95, 130), // WEBRip-1080p
        [3] = new(4, 95, 130), // WEBDL-1080p
        [7] = new(4, 95, 155), // Bluray-1080p
        [30] = new(35, 95, null), // Bluray-1080p Remux
        [16] = new(35, 95, 199.9), // HDTV-2160p
        [17] = new(35, 95, null), // WEBRip-2160p
        [18] = new(35, 95, null), // WEBDL-2160p
        [19] = new(35, 95, null), // Bluray-2160p
        [31] = new(35, 95, null), // Bluray-2160p Remux
    };

    public static Sizes For(int qualityId) => ByQualityId.GetValueOrDefault(qualityId, Unknown);
}
