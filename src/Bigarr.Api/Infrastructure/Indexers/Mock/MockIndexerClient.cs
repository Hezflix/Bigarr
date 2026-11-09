using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Infrastructure.Indexers.Mock;

/// <summary>
/// Fabricates a realistic spread of releases for whatever film is searched: several qualities, a remux, a repack,
/// an edition, a foreign dub, and a few that must be rejected (no seeders, oversized, wrong film, wrong year,
/// no quality). Deterministic, so tests and screenshots are stable. Never touches the network.
/// </summary>
public sealed partial class MockIndexerClient(TimeProvider time) : IIndexerClient
{
    /// <summary>One release blueprint. Sizes are for a two-hour film and scale with nothing: the decision engine does that.</summary>
    private sealed record Template(string Suffix, double SizeGb, int Seeders, int Leechers, int AgeDays, IndexerFlags Flags = IndexerFlags.None, bool CarriesIds = true, int YearShift = 0, string? TitleSuffix = null);

    private static readonly Template[] Templates =
    [
        new("2160p.UHD.BluRay.REMUX.HDR.HEVC.TrueHD.Atmos-FraMeSToR", 58.4, 35, 4, 210, IndexerFlags.Internal),
        new("2160p.UHD.BluRay.x265.HDR.DTS-HD.MA.5.1-TERMiNAL", 22.1, 80, 6, 160),
        new("2160p.WEB-DL.DDP5.1.Atmos.DV.HDR.H.265-FLUX", 17.9, 140, 12, 95, IndexerFlags.Freeleech),
        new("1080p.BluRay.REMUX.AVC.DTS-HD.MA.5.1-EPSiLON", 28.3, 25, 3, 230),
        new("1080p.BluRay.x264-SPARKS", 10.9, 320, 18, 300),
        new("REPACK.1080p.BluRay.x264-SPARKS", 11.2, 150, 9, 290),
        new("1080p.BluRay.x265-RARBG", 2.3, 900, 60, 280),
        new("1080p.WEB-DL.DDP5.1.H.264-NTb", 6.5, 210, 15, 60),
        new("Directors.Cut.1080p.BluRay.x264-AMIABLE", 12.4, 60, 5, 250),
        new("FRENCH.1080p.BluRay.x264-GRP", 12.0, 45, 7, 240),
        new("720p.BluRay.x264-YIFY", 1.1, 1500, 200, 310),
        new("720p.WEB-DL.DD5.1.H.264-BLOAT", 45.0, 30, 2, 20),
        new("1080p.BluRay.x264-NOSEED", 10.1, 0, 0, 400),
        new("HDTS.x264-ETRG", 1.8, 400, 300, 5),
        new("1080p.BluRay.x264-GRP", 11.0, 90, 4, 120, CarriesIds: false, TitleSuffix: "2 Reloaded", YearShift: 4),
        new("1080p.BluRay.x264-GRP", 10.7, 70, 2, 130, CarriesIds: false, YearShift: 7),
        new("-GRP", 9.3, 12, 1, 500, CarriesIds: false),
    ];

    /// <summary>What the RSS loop sees: a handful of well-known films from the fixture catalogue.</summary>
    private static readonly MovieSearchCriteria[] RecentFilms =
    [
        new("The Matrix", 1999, "tt0133093", 603, ["Matrix"]),
        new("Inception", 2010, "tt1375666", 27205, []),
        new("Interstellar", 2014, "tt0816692", 157336, []),
        new("Parasite", 2019, "tt6751668", 496243, ["Gisaengchung"]),
        new("Blade Runner 2049", 2017, "tt1856101", 335984, []),
    ];

    public IndexerImplementation Implementation => IndexerImplementation.Mock;

    public Task<IndexerCapabilities> GetCapabilitiesAsync(Indexer indexer, CancellationToken cancellationToken) =>
        Task.FromResult(new IndexerCapabilities(
            true,
            true,
            true,
            true,
            Indexer.DefaultCategories.Select(c => new IndexerCategory(c, $"Movies/{c}"))
                .Concat(new[] { 5000, 5030, 5040, 5045, Indexer.AnimeCategory }.Select(c => new IndexerCategory(c, $"TV/{c}")))
                .ToList(),
            100)
        {
            SupportsTvSearch = true,
            TvSearchParams = new HashSet<string>(["q", "season", "ep", "tvdbid", "tmdbid", "imdbid"]),
        });

    public Task<IReadOnlyList<ReleaseInfo>> SearchSeriesAsync(Indexer indexer, SeriesSearchCriteria criteria, Func<CancellationToken, Task> pace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(criteria);
        return Task.FromResult<IReadOnlyList<ReleaseInfo>>(MockSeriesReleases.ForSearch(indexer, criteria, time.GetUtcNow()));
    }

    public Task<IReadOnlyList<ReleaseInfo>> FetchRecentSeriesAsync(Indexer indexer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        return Task.FromResult<IReadOnlyList<ReleaseInfo>>(MockSeriesReleases.Recent(indexer, time.GetUtcNow()));
    }

    public Task<IReadOnlyList<ReleaseInfo>> SearchAsync(Indexer indexer, MovieSearchCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(criteria);

        var releases = Templates.Select((template, index) => Build(indexer, criteria, template, index)).ToList();
        return Task.FromResult<IReadOnlyList<ReleaseInfo>>(releases);
    }

    public Task<IReadOnlyList<ReleaseInfo>> FetchRecentAsync(Indexer indexer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(indexer);

        var releases = new List<ReleaseInfo>();
        var day = 0;
        foreach (var film in RecentFilms)
        {
            foreach (var index in new[] { 4, 7, 2 })
            {
                var template = Templates[index] with { AgeDays = day++ % 4 };
                releases.Add(Build(indexer, film, template, index));
            }
        }

        return Task.FromResult<IReadOnlyList<ReleaseInfo>>(releases.OrderByDescending(r => r.PublishDate).ToList());
    }

    private ReleaseInfo Build(Indexer indexer, MovieSearchCriteria criteria, Template template, int index)
    {
        var title = Dotted(template.TitleSuffix is null ? criteria.Title : $"{criteria.Title} {template.TitleSuffix}");
        var year = criteria.Year is > 0 ? criteria.Year.Value + template.YearShift : (int?)null;
        var name = year is null ? title : $"{title}.{year.Value.ToString(CultureInfo.InvariantCulture)}";
        name = template.Suffix.StartsWith('-') ? name + template.Suffix : $"{name}.{template.Suffix}";

        var slug = Slugify(criteria.Title);
        var hash = Sha1Hex($"{slug}/{name}");
        var now = time.GetUtcNow();

        return new ReleaseInfo
        {
            Guid = $"mock://{indexer.Id}/{slug}/{index.ToString(CultureInfo.InvariantCulture)}",
            Title = name,
            IndexerId = indexer.Id,
            IndexerName = indexer.Name,
            IndexerPriority = indexer.Priority,
            Protocol = DownloadProtocol.Torrent,
            Size = (long)(template.SizeGb * 1024 * 1024 * 1024),
            DownloadUrl = $"https://mock.invalid/download/{hash}.torrent",
            MagnetUrl = $"magnet:?xt=urn:btih:{hash}&dn={Uri.EscapeDataString(name)}",
            InfoUrl = $"https://mock.invalid/details/{hash}",
            InfoHash = hash,
            Seeders = template.Seeders,
            Leechers = template.Leechers,
            PublishDate = now.AddDays(-template.AgeDays).AddHours(-(index % 7)),
            Categories = [2000, template.Suffix.Contains("2160p", StringComparison.Ordinal) ? 2045 : 2040],
            ImdbId = template.CarriesIds ? criteria.ImdbId : null,
            TmdbId = template.CarriesIds ? criteria.TmdbId : null,
            Flags = template.Flags,
        };
    }

    private static string Dotted(string title) =>
        DotsRegex().Replace(NonWordRegex().Replace(title.Normalize(NormalizationForm.FormD), "."), ".").Trim('.');

    private static string Slugify(string title) => Dotted(title).ToLowerInvariant().Replace('.', '-');

    private static string Sha1Hex(string value) => Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(value)));

    [GeneratedRegex(@"[^A-Za-z0-9]+")]
    private static partial Regex NonWordRegex();

    [GeneratedRegex(@"\.{2,}")]
    private static partial Regex DotsRegex();
}
