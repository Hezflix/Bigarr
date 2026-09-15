using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Bigarr.Api.Infrastructure.Downloads.Torrents;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Infrastructure.Indexers.Torznab;

/// <summary>
/// Reads Torznab documents: <c>t=caps</c> and RSS feeds carrying <c>torznab:attr</c> elements (Prowlarr, Jackett).
/// Newznab attributes are read too, since Prowlarr emits both.
/// </summary>
public static class TorznabParser
{
    public static readonly XNamespace TorznabNs = "http://torznab.com/schemas/2015/feed";

    public static readonly XNamespace NewznabNs = "http://www.newznab.com/DTD/2010/feeds/attributes/";

    public static IndexerCapabilities ParseCapabilities(string xml)
    {
        var root = Load(xml);
        if (root.Name.LocalName != "caps")
        {
            throw new IndexerException(IndexerFailureKind.InvalidResponse, $"Expected a <caps> document, got <{root.Name.LocalName}>.");
        }

        var searching = root.Element("searching");
        var movieSearch = searching?.Element("movie-search");
        var supportsMovie = IsAvailable(movieSearch);
        var movieParams = SupportedParams(movieSearch);

        var categories = new List<IndexerCategory>();
        foreach (var category in root.Element("categories")?.Elements("category") ?? [])
        {
            AddCategory(categories, category);
            foreach (var sub in category.Elements("subcat"))
            {
                AddCategory(categories, sub);
            }
        }

        int? max = int.TryParse(root.Element("limits")?.Attribute("max")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) && m > 0 ? m : null;

        return new IndexerCapabilities(
            supportsMovie,
            supportsMovie && movieParams.Contains("imdbid"),
            supportsMovie && movieParams.Contains("tmdbid"),
            supportsMovie && movieParams.Contains("year"),
            categories,
            max);
    }

    public static IReadOnlyList<ReleaseInfo> ParseFeed(string xml, Indexer indexer)
    {
        ArgumentNullException.ThrowIfNull(indexer);

        var root = Load(xml);
        if (root.Name.LocalName != "rss")
        {
            throw new IndexerException(IndexerFailureKind.InvalidResponse, $"Expected an <rss> document, got <{root.Name.LocalName}>.");
        }

        var releases = new List<ReleaseInfo>();
        foreach (var item in root.Element("channel")?.Elements("item") ?? [])
        {
            var release = ParseItem(item, indexer);
            if (release is not null)
            {
                releases.Add(release);
            }
        }

        return releases;
    }

    private static XElement Load(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            throw new IndexerException(IndexerFailureKind.InvalidResponse, "Empty response.");
        }

        XElement root;
        try
        {
            root = XDocument.Parse(xml, LoadOptions.None).Root ?? throw new IndexerException(IndexerFailureKind.InvalidResponse, "Empty XML document.");
        }
        catch (XmlException ex)
        {
            var preview = xml.Length > 120 ? xml[..120] + "…" : xml;
            throw new IndexerException(IndexerFailureKind.InvalidResponse, $"Response is not XML: {preview.Trim()}", ex);
        }

        if (root.Name.LocalName == "error")
        {
            throw ToException(root);
        }

        return root;
    }

    /// <summary>Newznab error codes: 100-102 credentials, 500-501 limits, everything else a request problem.</summary>
    private static IndexerException ToException(XElement error)
    {
        var code = int.TryParse(error.Attribute("code")?.Value, out var c) ? c : 0;
        var description = error.Attribute("description")?.Value ?? "Unknown error";
        var kind = code switch
        {
            >= 100 and <= 102 => IndexerFailureKind.Authentication,
            500 or 501 => IndexerFailureKind.RateLimited,
            910 => IndexerFailureKind.Authentication,
            _ => IndexerFailureKind.Request,
        };
        return new IndexerException(kind, $"Indexer error {code}: {description}");
    }

    private static ReleaseInfo? ParseItem(XElement item, Indexer indexer)
    {
        var title = item.Element("title")?.Value.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        var attrs = item.Elements(TorznabNs + "attr").Concat(item.Elements(NewznabNs + "attr"))
            .Select(a => (Name: a.Attribute("name")?.Value?.ToLowerInvariant(), Value: a.Attribute("value")?.Value))
            .Where(a => a.Name is not null && a.Value is not null)
            .ToLookup(a => a.Name!, a => a.Value!);

        string? Attr(string name) => attrs[name].FirstOrDefault();

        var link = item.Element("link")?.Value.Trim();
        var enclosure = item.Element("enclosure");
        var enclosureUrl = enclosure?.Attribute("url")?.Value;
        var magnet = Attr("magneturl");
        string? downloadUrl = null;
        foreach (var candidate in new[] { link, enclosureUrl })
        {
            if (string.IsNullOrEmpty(candidate))
            {
                continue;
            }

            if (candidate.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                magnet ??= candidate;
            }
            else
            {
                downloadUrl ??= candidate;
            }
        }

        var guid = item.Element("guid")?.Value.Trim();
        if (string.IsNullOrEmpty(guid))
        {
            guid = downloadUrl ?? magnet ?? title;
        }

        var size = ParseLong(item.Element("size")?.Value) ?? ParseLong(Attr("size")) ?? ParseLong(enclosure?.Attribute("length")?.Value) ?? 0;
        var seeders = ParseInt(Attr("seeders"));
        var peers = ParseInt(Attr("peers"));
        int? leechers = ParseInt(Attr("leechers")) ?? (seeders is not null && peers is not null ? Math.Max(0, peers.Value - seeders.Value) : null);

        var categories = attrs["category"].Select(ParseInt).Where(c => c is not null).Select(c => c!.Value)
            .Concat(item.Elements("category").Select(c => ParseInt(c.Value)).Where(c => c is not null).Select(c => c!.Value))
            .Distinct()
            .ToList();

        return new ReleaseInfo
        {
            Guid = guid,
            Title = title,
            IndexerId = indexer.Id,
            IndexerName = indexer.Name,
            IndexerPriority = indexer.Priority,
            Protocol = indexer.Protocol,
            Size = size,
            DownloadUrl = downloadUrl,
            MagnetUrl = magnet,
            InfoUrl = item.Element("comments")?.Value.Trim() ?? Attr("infourl") ?? Attr("details"),
            InfoHash = NormalizeHash(Attr("infohash")),
            Seeders = seeders,
            Leechers = leechers,
            PublishDate = ParseDate(item.Element("pubDate")?.Value),
            Categories = categories,
            ImdbId = NormalizeImdbId(Attr("imdb") ?? Attr("imdbid")),
            TmdbId = ParseInt(Attr("tmdbid") ?? Attr("tmdb")),
            Flags = ParseFlags(attrs),
            MinimumRatio = ParseDouble(Attr("minimumratio")),
            MinimumSeedTime = ParseLong(Attr("minimumseedtime")) is { } seconds and > 0 ? TimeSpan.FromSeconds(seconds) : null,
        };
    }

    private static IndexerFlags ParseFlags(ILookup<string, string> attrs)
    {
        var flags = IndexerFlags.None;

        var download = ParseDouble(attrs["downloadvolumefactor"].FirstOrDefault());
        if (download is not null)
        {
            flags |= download.Value switch
            {
                0 => IndexerFlags.Freeleech,
                0.25 => IndexerFlags.Freeleech75,
                0.5 => IndexerFlags.Halfleech,
                0.75 => IndexerFlags.Freeleech25,
                _ => IndexerFlags.None,
            };
        }

        if (ParseDouble(attrs["uploadvolumefactor"].FirstOrDefault()) is >= 2)
        {
            flags |= IndexerFlags.DoubleUpload;
        }

        foreach (var tag in attrs["tag"].SelectMany(t => t.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
        {
            flags |= tag.ToLowerInvariant() switch
            {
                "internal" => IndexerFlags.Internal,
                "scene" => IndexerFlags.Scene,
                "freeleech" => IndexerFlags.Freeleech,
                "halfleech" => IndexerFlags.Halfleech,
                "doubleupload" => IndexerFlags.DoubleUpload,
                "nuked" => IndexerFlags.Nuked,
                _ => IndexerFlags.None,
            };
        }

        return flags;
    }

    private static bool IsAvailable(XElement? element) =>
        string.Equals(element?.Attribute("available")?.Value, "yes", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> SupportedParams(XElement? element) =>
        (element?.Attribute("supportedParams")?.Value ?? string.Empty)
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.ToLowerInvariant())
        .ToHashSet();

    private static void AddCategory(List<IndexerCategory> categories, XElement element)
    {
        if (int.TryParse(element.Attribute("id")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            categories.Add(new IndexerCategory(id, element.Attribute("name")?.Value ?? id.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static DateTimeOffset ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTimeOffset.MinValue;
        }

        if (DateTimeOffset.TryParseExact(value.Trim(), "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var rfc))
        {
            return rfc;
        }

        return DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var any) ? any : DateTimeOffset.MinValue;
    }

    private static long? ParseLong(string? value) =>
        long.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static int? ParseInt(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static double? ParseDouble(string? value) =>
        double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

    /// <summary>40 hex characters, or null when the indexer sent something that is not a hash (see <see cref="InfoHashes"/>).</summary>
    private static string? NormalizeHash(string? value) => InfoHashes.Normalize(value);

    /// <summary>Indexers send <c>tt0133093</c>, <c>0133093</c> or <c>133093</c>; the library stores <c>tt0133093</c>.</summary>
    public static string? NormalizeImdbId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = value.Trim().TrimStart('t', 'T');
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit) || digits.All(d => d == '0'))
        {
            return null;
        }

        return "tt" + digits.PadLeft(7, '0');
    }
}
