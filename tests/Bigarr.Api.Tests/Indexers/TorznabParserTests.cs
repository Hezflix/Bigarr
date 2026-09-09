using Bigarr.Api.Infrastructure.Indexers.Torznab;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Tests.Indexers;

public sealed class TorznabParserTests
{
    private static readonly Indexer Indexer = new() { Id = 3, Name = "Some Tracker", Priority = 10 };

    [Fact]
    public void Capabilities_expose_movie_search_parameters_and_categories()
    {
        var caps = TorznabParser.ParseCapabilities(TorznabRecorded.Caps);

        Assert.True(caps.SupportsMovieSearch);
        Assert.True(caps.SupportsImdbSearch);
        Assert.True(caps.SupportsTmdbSearch);
        Assert.True(caps.SupportsYearParameter);
        Assert.Equal(100, caps.MaxPageSize);
        Assert.Equal([2000, 2010, 2040, 2045, 5000], caps.Categories.Select(c => c.Id));
        Assert.Equal("Movies/UHD", caps.Categories.Single(c => c.Id == 2045).Name);

        var textOnly = TorznabParser.ParseCapabilities(TorznabRecorded.CapsTextOnly);
        Assert.True(textOnly.SupportsMovieSearch);
        Assert.False(textOnly.SupportsImdbSearch);
        Assert.False(textOnly.SupportsTmdbSearch);
        Assert.Null(textOnly.MaxPageSize);

        Assert.False(TorznabParser.ParseCapabilities(TorznabRecorded.CapsNoMovieSearch).SupportsMovieSearch);
    }

    [Fact]
    public void Feed_items_become_releases_with_their_attributes()
    {
        var releases = TorznabParser.ParseFeed(TorznabRecorded.Feed, Indexer);

        Assert.Equal(2, releases.Count); // the item without a title is dropped

        var sparks = releases[0];
        Assert.Equal("The.Matrix.1999.1080p.BluRay.x264-SPARKS", sparks.Title);
        Assert.Equal("https://tracker.example/details/123456", sparks.Guid);
        Assert.StartsWith("http://prowlarr:9696/1/download?apikey=secret", sparks.DownloadUrl);
        Assert.Null(sparks.MagnetUrl);
        Assert.Equal("https://tracker.example/details/123456#comments", sparks.InfoUrl);
        Assert.Equal(11_706_000_000, sparks.Size);
        Assert.Equal(320, sparks.Seeders);
        Assert.Equal(18, sparks.Leechers);
        Assert.Equal(new DateTimeOffset(2026, 9, 8, 10, 15, 0, TimeSpan.Zero), sparks.PublishDate);
        Assert.Equal([2000, 2040], sparks.Categories);
        Assert.Equal("tt0133093", sparks.ImdbId);
        Assert.Equal(603, sparks.TmdbId);
        Assert.Equal("a1b2c3d4e5f60718293a4b5c6d7e8f9012345678", sparks.InfoHash);
        Assert.Equal(IndexerFlags.Freeleech | IndexerFlags.Internal, sparks.Flags);
        Assert.Equal(1.0, sparks.MinimumRatio);
        Assert.Equal(TimeSpan.FromDays(2), sparks.MinimumSeedTime);
        Assert.Equal(3, sparks.IndexerId);
        Assert.Equal("Some Tracker", sparks.IndexerName);
        Assert.Equal(10, sparks.IndexerPriority);
        Assert.Equal(DownloadProtocol.Torrent, sparks.Protocol);

        var remux = releases[1];
        Assert.StartsWith("magnet:?xt=urn:btih:ffff", remux.MagnetUrl);
        Assert.Null(remux.DownloadUrl);
        Assert.Equal(62_000_000_000, remux.Size); // from the enclosure
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 8, 0, 0, TimeSpan.Zero), remux.PublishDate);
        Assert.Equal(IndexerFlags.Halfleech | IndexerFlags.DoubleUpload, remux.Flags);
        Assert.Equal("tt0133093", remux.ImdbId);
        Assert.Null(remux.TmdbId);
        Assert.Equal(4, remux.Leechers);

        Assert.Empty(TorznabParser.ParseFeed(TorznabRecorded.EmptyFeed, Indexer));
    }

    [Theory]
    [InlineData(TorznabRecorded.ErrorCredentials, IndexerFailureKind.Authentication, "Incorrect user credentials")]
    [InlineData(TorznabRecorded.ErrorLimit, IndexerFailureKind.RateLimited, "Request limit reached")]
    [InlineData(TorznabRecorded.ErrorParameter, IndexerFailureKind.Request, "Incorrect parameter")]
    public void Torznab_errors_are_typed(string xml, IndexerFailureKind kind, string message)
    {
        var ex = Assert.Throws<IndexerException>(() => TorznabParser.ParseFeed(xml, Indexer));
        Assert.Equal(kind, ex.Kind);
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void Non_xml_and_wrong_documents_are_invalid_responses()
    {
        var html = Assert.Throws<IndexerException>(() => TorznabParser.ParseFeed(TorznabRecorded.HtmlLoginPage, Indexer));
        Assert.Equal(IndexerFailureKind.InvalidResponse, html.Kind);

        var capsAsFeed = Assert.Throws<IndexerException>(() => TorznabParser.ParseFeed(TorznabRecorded.Caps, Indexer));
        Assert.Equal(IndexerFailureKind.InvalidResponse, capsAsFeed.Kind);

        var feedAsCaps = Assert.Throws<IndexerException>(() => TorznabParser.ParseCapabilities(TorznabRecorded.Feed));
        Assert.Equal(IndexerFailureKind.InvalidResponse, feedAsCaps.Kind);

        Assert.Equal(IndexerFailureKind.InvalidResponse, Assert.Throws<IndexerException>(() => TorznabParser.ParseFeed("", Indexer)).Kind);
    }

    [Theory]
    [InlineData("tt0133093", "tt0133093")]
    [InlineData("0133093", "tt0133093")]
    [InlineData("133093", "tt0133093")]
    [InlineData("TT0133093", "tt0133093")]
    [InlineData("0", null)]
    [InlineData("", null)]
    [InlineData("abc", null)]
    public void Imdb_ids_are_normalised(string input, string? expected) =>
        Assert.Equal(expected, TorznabParser.NormalizeImdbId(input));
}
