using System.Globalization;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Domain.Movies;
using Bigarr.Parsing;
using Bigarr.Parsing.Languages;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary>Translates one compat line (a version and its film, §7.5) to Radarr's movie resource. Pure: everything comes in as arguments.</summary>
public static class RadarrMapper
{
    public static RadarrMovie ToMovie(CompatRow row, DateOnly today, FileAssessment? assessment)
    {
        ArgumentNullException.ThrowIfNull(row);

        var (movie, version) = row;
        var folder = Path.Combine(version.RootFolder.Path, version.FolderName);
        var file = version.File is null ? null : ToMovieFile(version, folder, assessment);
        return new RadarrMovie
        {
            Id = version.Id,
            Title = movie.Title,
            OriginalTitle = movie.OriginalTitle,
            OriginalLanguage = OriginalLanguage(movie.OriginalLanguage),
            AlternateTitles = movie.AlternativeTitles.Select((t, i) => new RadarrAlternateTitle(i + 1, "tmdb", movie.Id, t.Title)).ToList(),
            SortTitle = movie.SortTitle,
            SizeOnDisk = version.File?.Size ?? 0,
            Status = Camel(Availability.EffectiveStatus(movie, today).ToString()),
            Overview = movie.Overview,
            InCinemas = Utc(movie.InCinemasDate),
            PhysicalRelease = Utc(movie.PhysicalReleaseDate),
            DigitalRelease = Utc(movie.DigitalReleaseDate),
            ReleaseDate = Utc(movie.DigitalReleaseDate ?? movie.PhysicalReleaseDate ?? movie.InCinemasDate),
            Images = Images(movie, version.Id),
            RemotePoster = movie.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
            Year = movie.Year ?? 0,
            Path = folder,
            QualityProfileId = version.QualityProfileId,
            ProfileId = version.QualityProfileId,
            HasFile = version.File is not null,
            MovieFileId = version.File?.Id ?? 0,
            Monitored = version.Monitored,
            MinimumAvailability = Camel(version.MinimumAvailability.ToString()),
            IsAvailable = Availability.IsAvailable(movie, version.MinimumAvailability, today),
            FolderName = folder,
            Runtime = movie.Runtime ?? 0,
            CleanTitle = movie.Title.CleanMovieTitle(),
            ImdbId = movie.ImdbId,
            TmdbId = movie.TmdbId,
            TitleSlug = movie.TmdbId.ToString(CultureInfo.InvariantCulture),
            RootFolderPath = version.RootFolder.Path,
            Folder = version.FolderName,
            Certification = movie.Certification,
            Genres = movie.Genres,
            Tags = version.Tags.Select(t => t.Id).OrderBy(id => id).ToList(),
            Added = movie.AddedAt,
            Ratings = movie.Ratings?.Tmdb is { } tmdb ? new RadarrRatings(new RadarrRating(tmdb.Votes, tmdb.Value)) : null,
            MovieFile = file,
            Collection = movie.CollectionTmdbId is { } collection ? new RadarrCollection(string.Empty, collection) : null,
            LastSearchTime = version.LastSearchAt,
            Statistics = new RadarrStatistics(file is null ? 0 : 1, version.File?.Size ?? 0, file?.ReleaseGroup is { } group ? [group] : []),
        };
    }

    /// <summary>A lookup hit that is not in the view: Radarr's shape with <c>id</c> 0, which is what tells Seerr to POST (§7.2).</summary>
    public static RadarrMovie ToLookup(MovieLookupResult found, DateOnly today) => new()
    {
        Id = 0,
        Title = found.Title,
        OriginalTitle = found.OriginalTitle,
        SortTitle = SortTitle.Build(found.Title),
        Status = Camel(found.Status.ToString()),
        Overview = found.Overview,
        Images = found.PosterUrl is null ? [] : [new RadarrImage("poster", found.PosterUrl, found.PosterUrl)],
        RemotePoster = found.PosterUrl,
        Year = found.Year ?? 0,
        QualityProfileId = 0,
        HasFile = false,
        Monitored = false,
        MinimumAvailability = "released",
        IsAvailable = found.Status == MovieStatus.Released,
        CleanTitle = found.Title.CleanMovieTitle(),
        TmdbId = found.TmdbId,
        TitleSlug = found.TmdbId.ToString(CultureInfo.InvariantCulture),
        Added = DateTime.MinValue,
    };

    public static RadarrMovieFile ToMovieFile(MovieVersion version, string folder, FileAssessment? assessment)
    {
        ArgumentNullException.ThrowIfNull(version);
        var file = version.File ?? throw new ArgumentException("The version has no file.", nameof(version));
        return new RadarrMovieFile(
            file.Id,
            version.Id,
            file.RelativePath,
            Path.Combine(folder, file.RelativePath),
            file.Size,
            file.DateAdded,
            file.OriginalReleaseTitle,
            file.ReleaseGroup,
            file.Edition,
            Languages(file.Languages),
            ToQuality(file.Quality, file.Revision),
            assessment?.CustomFormatIds.Zip(assessment.CustomFormats, (id, name) => new RadarrCustomFormat(id, name)).ToList() ?? [],
            assessment?.CustomFormatScore ?? 0,
            (int)file.IndexerFlags,
            file.MediaInfo is null ? null : ToMediaInfo(file.MediaInfo),
            file.OriginalReleaseTitle,
            assessment is not null && !assessment.CutoffMet);
    }

    public static RadarrQuality ToQuality(Bigarr.Domain.Quality.QualityDefinition? quality, Revision? revision) => new(
        ToQualityDefinition(quality),
        new RadarrRevision(revision?.Version ?? 1, revision?.Real ?? 0, revision?.IsRepack ?? false));

    public static RadarrQualityDefinition ToQualityDefinition(Bigarr.Domain.Quality.QualityDefinition? quality) =>
        quality is null
            ? new RadarrQualityDefinition(0, "Unknown", "unknown", 0, "none")
            : new RadarrQualityDefinition(quality.Id, quality.Name, quality.Source.ToString().ToLowerInvariant(), quality.Resolution, Camel(quality.Modifier.ToString()));

    public static RadarrMediaInfo ToMediaInfo(MediaInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new RadarrMediaInfo(
            info.AudioBitrate,
            info.AudioChannels,
            info.AudioCodec,
            info.AudioLanguages.Count == 0 ? null : string.Join('/', info.AudioLanguages),
            info.AudioLanguages.Count,
            info.VideoBitDepth,
            info.VideoBitrate,
            info.VideoCodec,
            0,
            info.VideoDynamicRange,
            info.VideoDynamicRangeType,
            info.Width is { } w && info.Height is { } h ? $"{w}x{h}" : null,
            info.RunTime is { } runTime ? runTime.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : null,
            info.ScanType,
            info.Subtitles.Count == 0 ? null : string.Join('/', info.Subtitles));
    }

    /// <summary>Radarr's language ids, from the vendored table; unknown names keep the name with id 0 rather than vanish.</summary>
    public static List<RadarrLanguage> Languages(IEnumerable<string> names) =>
        names.Select(Language).ToList();

    public static RadarrLanguage Language(string name)
    {
        var iso = IsoLanguages.FindByName(name);
        return iso is null ? new RadarrLanguage(0, name) : new RadarrLanguage(iso.Language.Id, iso.Language.Name);
    }

    /// <summary>From an ISO 639-1 code as TMDB gives it (<c>en</c>); Bazarr falls back on the profile's language when this is missing.</summary>
    public static RadarrLanguage? OriginalLanguage(string? isoCode)
    {
        if (string.IsNullOrWhiteSpace(isoCode))
        {
            return null;
        }

        var iso = IsoLanguages.Find(isoCode);
        return iso is null ? new RadarrLanguage(0, isoCode) : new RadarrLanguage(iso.Language.Id, iso.Language.Name);
    }

    public static List<RadarrImage> Images(Movie movie, int versionId)
    {
        ArgumentNullException.ThrowIfNull(movie);

        // Poster first, fanart second: Bazarr reads images[0] and images[1] by position.
        var images = new List<RadarrImage>();
        if (movie.Images.FirstOrDefault(i => i.Type == ImageType.Poster) is { } poster)
        {
            images.Add(new RadarrImage("poster", $"/MediaCover/{versionId}/poster.jpg", poster.RemoteUrl));
        }

        if (movie.Images.FirstOrDefault(i => i.Type == ImageType.Fanart) is { } fanart)
        {
            images.Add(new RadarrImage("fanart", $"/MediaCover/{versionId}/fanart.jpg", fanart.RemoteUrl));
        }

        return images;
    }

    /// <summary>The quality the parser reads in a release title, in Radarr's shape; unknown when the title does not parse.</summary>
    public static RadarrQuality QualityFromTitle(string title)
    {
        try
        {
            var parsed = Parser.ParseMovieTitle(title)?.Quality;
            if (parsed?.Quality is null)
            {
                return ToQuality(null, null);
            }

            var q = parsed.Quality;
            return new RadarrQuality(
                new RadarrQualityDefinition(q.Id, q.Name, q.Source.ToString().ToLowerInvariant(), q.Resolution, Camel(q.Modifier.ToString().ToLowerInvariant())),
                new RadarrRevision(parsed.Revision?.Version ?? 1, parsed.Revision?.Real ?? 0, parsed.Revision?.IsRepack ?? false));
        }
        catch (Exception)
        {
            return ToQuality(null, null);
        }
    }

    public static string Camel(string value) => value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value[1..];

    public static DateTime? Utc(DateOnly? date) => date is { } d ? d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : null;

    /// <summary>Custom formats and cutoff of every file of the film, judged now against each version's profile (§3.6).</summary>
    public static async Task<Dictionary<int, FileAssessment>> AssessAsync(IEnumerable<CompatRow> rows, CustomFormatScorer scorer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scorer);

        var result = new Dictionary<int, FileAssessment>();
        var withFiles = rows.Where(r => r.Version.File is not null).ToList();
        if (withFiles.Count == 0)
        {
            return result;
        }

        var calculator = await scorer.CalculatorAsync(ct);
        foreach (var (movie, version) in withFiles)
        {
            var file = version.File!;
            var matched = calculator.Match(CustomFormatScorer.FileInput(
                file.OriginalReleaseTitle, file.RelativePath, file.Size, file.IndexerFlags, file.Quality, file.Languages, file.Edition, file.ReleaseGroup, movie.Year, movie.OriginalLanguage));
            result[version.Id] = CustomFormatScorer.Assess(version.QualityProfile, file.QualityId, matched);
        }

        return result;
    }
}
