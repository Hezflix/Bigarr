namespace Bigarr.Api.Infrastructure.Tv;

/// <summary>Sonarr's "an alias may be needed" note, written when a release found its series by an id rather than its name.</summary>
public static class SeriesMatchLog
{
    public static void ById(ILogger logger, SeriesMatch match, string? releaseSeriesTitle, int? tvdbId, string? imdbId)
    {
        switch (match.Kind)
        {
            case SeriesMatchKind.Id:
                logger.LogInformation("Found matching series {Series} by TVDB id {TvdbId}, an alias may be needed for '{Title}'", match.Series?.Title, tvdbId, releaseSeriesTitle);
                break;
            case SeriesMatchKind.ImdbId:
                logger.LogInformation("Found matching series {Series} by IMDb id {ImdbId}, an alias may be needed for '{Title}'", match.Series?.Title, imdbId, releaseSeriesTitle);
                break;
        }
    }
}
