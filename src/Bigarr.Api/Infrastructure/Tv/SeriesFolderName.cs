using System.Globalization;
using System.Text.RegularExpressions;

namespace Bigarr.Api.Infrastructure.Tv;

/// <summary>What a series folder name says: a title, maybe a year, maybe a TVDB/TMDB/IMDb id tag.</summary>
public sealed record SeriesFolderInfo(string Title, int? Year, int? TvdbId, int? TmdbId, string? ImdbId);

/// <summary>
/// Reads Sonarr/Plex/Jellyfin style series folder names: <c>Show (2008)</c>, <c>Show (2008) {tvdb-81189}</c>,
/// <c>Show [tvdbid-81189]</c>, <c>Show {tmdb-1396}</c>, <c>Show {imdb-tt0903747}</c>.
/// </summary>
public static partial class SeriesFolderName
{
    public static SeriesFolderInfo Parse(string folderName)
    {
        ArgumentNullException.ThrowIfNull(folderName);

        int? tvdb = null, tmdb = null;
        string? imdb = null;
        foreach (Match tag in IdTagRegex().Matches(folderName))
        {
            var source = tag.Groups["source"].Value.ToLowerInvariant();
            var value = tag.Groups["id"].Value;
            if (source.StartsWith("tvdb", StringComparison.Ordinal) && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var t))
            {
                tvdb = t;
            }
            else if (source.StartsWith("tmdb", StringComparison.Ordinal) && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var m))
            {
                tmdb = m;
            }
            else if (source.StartsWith("imdb", StringComparison.Ordinal))
            {
                imdb = value;
            }
        }

        var name = TagsRegex().Replace(folderName, " ");
        int? year = null;
        var yearMatch = TrailingYearRegex().Match(name);
        if (yearMatch.Success)
        {
            year = int.Parse(yearMatch.Groups["year"].Value, CultureInfo.InvariantCulture);
            name = name[..yearMatch.Index];
        }

        var title = SpacesRegex().Replace(name.Replace('.', ' ').Replace('_', ' '), " ").Trim(' ', '-');
        return new SeriesFolderInfo(title, year, tvdb, tmdb, imdb);
    }

    [GeneratedRegex(@"[\[{](?<source>tvdbid|tvdb|tmdbid|tmdb|imdbid|imdb)[-=](?<id>tt\d+|\d+)[\]}]", RegexOptions.IgnoreCase)]
    private static partial Regex IdTagRegex();

    [GeneratedRegex(@"\{[^}]*\}|\[[^\]]*\]")]
    private static partial Regex TagsRegex();

    [GeneratedRegex(@"[\s.(_-]*\(?(?<year>(?:19|20)\d{2})\)?\s*$")]
    private static partial Regex TrailingYearRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex SpacesRegex();
}
