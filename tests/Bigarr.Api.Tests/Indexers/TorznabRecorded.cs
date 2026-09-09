namespace Bigarr.Api.Tests.Indexers;

/// <summary>Hand-captured Torznab documents in the shape Prowlarr serves (September 2026).</summary>
public static class TorznabRecorded
{
    public const string Caps = """
    <?xml version="1.0" encoding="UTF-8"?>
    <caps>
      <server title="Prowlarr" />
      <limits default="100" max="100" />
      <searching>
        <search available="yes" supportedParams="q" />
        <tv-search available="yes" supportedParams="q,season,ep,imdbid,tvdbid" />
        <movie-search available="yes" supportedParams="q,imdbid,tmdbid,year" />
        <music-search available="no" supportedParams="q" />
        <audio-search available="no" supportedParams="q" />
        <book-search available="no" supportedParams="q" />
      </searching>
      <categories>
        <category id="2000" name="Movies">
          <subcat id="2010" name="Movies/Foreign" />
          <subcat id="2040" name="Movies/HD" />
          <subcat id="2045" name="Movies/UHD" />
        </category>
        <category id="5000" name="TV" />
      </categories>
    </caps>
    """;

    public const string CapsTextOnly = """
    <?xml version="1.0" encoding="UTF-8"?>
    <caps>
      <searching>
        <search available="yes" supportedParams="q" />
        <movie-search available="yes" supportedParams="q" />
      </searching>
      <categories>
        <category id="2000" name="Movies" />
      </categories>
    </caps>
    """;

    public const string CapsNoMovieSearch = """
    <caps>
      <searching>
        <search available="yes" supportedParams="q" />
        <movie-search available="no" supportedParams="q" />
      </searching>
    </caps>
    """;

    public const string Feed = """
    <?xml version="1.0" encoding="UTF-8"?>
    <rss version="2.0" xmlns:atom="http://www.w3.org/2005/Atom" xmlns:torznab="http://torznab.com/schemas/2015/feed">
      <channel>
        <atom:link href="http://prowlarr:9696/1/api" rel="self" type="application/rss+xml" />
        <title>Some Tracker (Prowlarr)</title>
        <description>Some Tracker</description>
        <link>http://prowlarr:9696/1/</link>
        <language>en-US</language>
        <item>
          <title>The.Matrix.1999.1080p.BluRay.x264-SPARKS</title>
          <guid isPermaLink="false">https://tracker.example/details/123456</guid>
          <link>http://prowlarr:9696/1/download?apikey=secret&amp;link=abc&amp;file=The.Matrix.1999.1080p.BluRay.x264-SPARKS</link>
          <comments>https://tracker.example/details/123456#comments</comments>
          <pubDate>Tue, 08 Sep 2026 10:15:00 +0000</pubDate>
          <size>11706000000</size>
          <category>2000</category>
          <category>2040</category>
          <enclosure url="http://prowlarr:9696/1/download?apikey=secret&amp;link=abc&amp;file=The.Matrix.1999.1080p.BluRay.x264-SPARKS" length="11706000000" type="application/x-bittorrent" />
          <torznab:attr name="category" value="2000" />
          <torznab:attr name="category" value="2040" />
          <torznab:attr name="genre" value="" />
          <torznab:attr name="imdb" value="0133093" />
          <torznab:attr name="tmdbid" value="603" />
          <torznab:attr name="seeders" value="320" />
          <torznab:attr name="peers" value="338" />
          <torznab:attr name="infohash" value="A1B2C3D4E5F60718293A4B5C6D7E8F9012345678" />
          <torznab:attr name="downloadvolumefactor" value="0" />
          <torznab:attr name="uploadvolumefactor" value="1" />
          <torznab:attr name="minimumratio" value="1.0" />
          <torznab:attr name="minimumseedtime" value="172800" />
          <torznab:attr name="tag" value="internal" />
        </item>
        <item>
          <title>The Matrix 1999 2160p UHD BluRay REMUX HDR HEVC TrueHD Atmos-FraMeSToR</title>
          <guid>magnet:?xt=urn:btih:ffffffffffffffffffffffffffffffffffffffff&amp;dn=Matrix</guid>
          <link>magnet:?xt=urn:btih:ffffffffffffffffffffffffffffffffffffffff&amp;dn=Matrix</link>
          <pubDate>Mon, 07 Sep 2026 08:00:00 GMT</pubDate>
          <enclosure url="magnet:?xt=urn:btih:ffffffffffffffffffffffffffffffffffffffff&amp;dn=Matrix" length="62000000000" type="application/x-bittorrent" />
          <torznab:attr name="category" value="2045" />
          <torznab:attr name="seeders" value="35" />
          <torznab:attr name="peers" value="39" />
          <torznab:attr name="downloadvolumefactor" value="0.5" />
          <torznab:attr name="uploadvolumefactor" value="2" />
          <torznab:attr name="imdb" value="tt0133093" />
        </item>
        <item>
          <title></title>
          <guid>empty-title</guid>
        </item>
      </channel>
    </rss>
    """;

    public const string EmptyFeed = """
    <rss version="2.0" xmlns:torznab="http://torznab.com/schemas/2015/feed"><channel><title>Empty</title></channel></rss>
    """;

    public const string ErrorCredentials = """<error code="100" description="Incorrect user credentials" />""";

    public const string ErrorLimit = """<?xml version="1.0"?><error code="500" description="Request limit reached" />""";

    public const string ErrorParameter = """<error code="201" description="Incorrect parameter" />""";

    public const string HtmlLoginPage = "<!DOCTYPE html><html><body>Please log in</body></html>";
}
