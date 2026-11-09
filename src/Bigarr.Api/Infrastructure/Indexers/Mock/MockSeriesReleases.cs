using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Tv;

namespace Bigarr.Api.Infrastructure.Indexers.Mock;

/// <summary>
/// The mock indexer's series side: for whatever is searched, a spread of single episodes, multi-episode files, season
/// packs, a multi-season pack, a sample, a wrong episode and other qualities, named the way scene and anime groups
/// name them. Deterministic, never touches the network.
/// </summary>
public static partial class MockSeriesReleases
{
    private const double Gb = 1024d * 1024 * 1024;

    public static List<ReleaseInfo> ForSearch(Indexer indexer, SeriesSearchCriteria criteria, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var dotted = Dotted(criteria.Title);
        var names = new List<(string Name, double SizeGb, int Seeders, int AgeDays)>();
        if (criteria.SpecialQueries.Count > 0)
        {
            // Specials named by their title, as the scene does.
            return criteria.SpecialQueries
                .Select((q, i) => Build(indexer, $"{Dotted(q)}.1080p.WEB-DL.DD5.1.H.264-NTb", 0.9, 40 - i, 60, i, criteria.TvdbId, now))
                .ToList();
        }

        if (criteria.AirDate is { } date)
        {
            var day = date.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
            names.Add(($"{dotted}.{day}.1080p.WEB.h264-EDITH", 1.6, 120, 1));
            names.Add(($"{dotted}.{day}.720p.WEB.h264-EDITH", 0.7, 60, 1));
            names.Add(($"{dotted}.{date.AddDays(-1).ToString("yyyy.MM.dd", CultureInfo.InvariantCulture)}.1080p.WEB.h264-EDITH", 1.6, 80, 2));
        }
        else if (criteria.SeriesType == SeriesType.Anime && criteria.AbsoluteEpisodeNumber is { } absolute)
        {
            var number = absolute.ToString("00", CultureInfo.InvariantCulture);
            names.Add(($"[SubsPlease] {criteria.Title} - {number} (1080p) [{Hash8(criteria.Title + number)}].mkv", 1.4, 900, 3));
            names.Add(($"[Erai-raws] {criteria.Title} - {number}v2 [1080p][Multiple Subtitle]", 1.4, 300, 2));
            names.Add(($"[SubsPlease] {criteria.Title} - {number} (720p) [{Hash8(criteria.Title + number + "720")}].mkv", 0.7, 400, 3));
            names.Add(($"[SubsPlease] {criteria.Title} - {(absolute + 1).ToString("00", CultureInfo.InvariantCulture)} (1080p) [{Hash8(criteria.Title + (absolute + 1))}].mkv", 1.4, 800, 1));
        }

        var season = criteria.SeasonNumber ?? 1;
        var s = $"S{season.ToString("00", CultureInfo.InvariantCulture)}";
        if (criteria.EpisodeNumber is { } episode)
        {
            var e = $"E{episode.ToString("00", CultureInfo.InvariantCulture)}";
            names.Add(($"{dotted}.{s}{e}.1080p.WEB-DL.DDP5.1.H.264-NTb", 1.9, 210, 30));
            names.Add(($"{dotted}.{s}{e}.2160p.WEB-DL.DDP5.1.HDR.H.265-FLUX", 6.1, 95, 30));
            names.Add(($"{dotted}.{s}{e}.720p.HDTV.x264-KILLERS", 0.9, 40, 400));
            names.Add(($"{dotted}.{s}{e}.REPACK.1080p.WEB-DL.DDP5.1.H.264-NTb", 1.9, 120, 29));
            names.Add(($"{dotted}.{s}{e}.1080p.WEB-DL.sample-NTb", 0.04, 10, 30));
            names.Add(($"{dotted}.{s}E{(episode + 1).ToString("00", CultureInfo.InvariantCulture)}.1080p.WEB-DL.DDP5.1.H.264-NTb", 1.9, 200, 23));
        }

        if (criteria.AirDate is null && criteria.AbsoluteEpisodeNumber is null || criteria.IsSeasonSearch)
        {
            names.Add(($"{dotted}.{s}.1080p.BluRay.x264-DEMAND", 19.5, 150, 200));
            names.Add(($"{dotted}.{s}.2160p.WEB-DL.DDP5.1.HDR.H.265-FLUX", 52.0, 60, 120));
            names.Add(($"{dotted}.{s}.720p.WEB-DL.DD5.1.H.264-NTb", 3.1, 20, 210));
            names.Add(($"{dotted}.S01-S03.1080p.BluRay.x264-GRP", 61.0, 35, 300));
        }

        if (criteria.IsSeasonSearch || criteria.IsSeriesSearch)
        {
            names.Add(($"{dotted}.{s}E01.1080p.WEB-DL.DDP5.1.H.264-NTb", 1.9, 180, 220));
            names.Add(($"{dotted}.{s}E01E02.1080p.WEB-DL.DDP5.1.H.264-NTb", 3.8, 70, 220));
            names.Add(($"{dotted}.{s}E03.1080p.WEB-DL.DDP5.1.H.264-NTb", 1.9, 150, 215));
        }

        return names.Select((n, i) => Build(indexer, n.Name, n.SizeGb, n.Seeders, n.AgeDays, i, criteria.TvdbId, now)).ToList();
    }

    /// <summary>The series feed: a few new episodes of the fixture catalogue's shows (standard, anime, daily).</summary>
    public static List<ReleaseInfo> Recent(Indexer indexer, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var names = new List<(string Name, double SizeGb, int? TvdbId)>
        {
            ("Breaking.Bad.S05E16.1080p.WEB-DL.DD5.1.H.264-BS", 1.8, 81189),
            ("Breaking.Bad.S05.1080p.BluRay.x264-ROVERS", 35.0, 81189),
            ("[SubsPlease] Sousou no Frieren S2 - 05 (1080p) [A1B2C3D4].mkv", 1.4, null),
            ($"The.Daily.Show.{today.AddDays(-1).ToString("yyyy.MM.dd", CultureInfo.InvariantCulture)}.1080p.WEB.h264-EDITH", 1.5, null),
            ("Some.Unknown.Show.S01E01.1080p.WEB.h264-GRP", 1.2, null),
        };

        return names.Select((n, i) => Build(indexer, n.Name, n.SizeGb, 100 - i, i % 3, 100 + i, n.TvdbId, now)).OrderByDescending(r => r.PublishDate).ToList();
    }

    private static ReleaseInfo Build(Indexer indexer, string name, double sizeGb, int seeders, int ageDays, int index, int? tvdbId, DateTimeOffset now)
    {
        var hash = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes($"series/{name}")));
        return new ReleaseInfo
        {
            Guid = $"mock://{indexer.Id}/series/{hash[..12]}",
            Title = name,
            IndexerId = indexer.Id,
            IndexerName = indexer.Name,
            IndexerPriority = indexer.Priority,
            Protocol = DownloadProtocol.Torrent,
            Size = (long)(sizeGb * Gb),
            DownloadUrl = $"https://mock.invalid/download/{hash}.torrent",
            MagnetUrl = $"magnet:?xt=urn:btih:{hash}&dn={Uri.EscapeDataString(name)}",
            InfoUrl = $"https://mock.invalid/details/{hash}",
            InfoHash = hash,
            Seeders = seeders,
            Leechers = seeders / 10,
            PublishDate = now.AddDays(-ageDays).AddHours(-(index % 7)),
            Categories = [5000, name.StartsWith('[') ? Indexer.AnimeCategory : 5040],
            TvdbId = tvdbId,
        };
    }

    private static string Dotted(string title) =>
        DotsRegex().Replace(NonWordRegex().Replace(title.Normalize(NormalizationForm.FormD), "."), ".").Trim('.');

    private static string Hash8(string value) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(value)))[..8];

    [GeneratedRegex(@"[^A-Za-z0-9]+")]
    private static partial Regex NonWordRegex();

    [GeneratedRegex(@"\.{2,}")]
    private static partial Regex DotsRegex();
}
