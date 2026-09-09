using System.Text.RegularExpressions;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Common;
using Bigarr.Domain.History;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Movies;

/// <summary>Application rules of the Movies slice: add, version, promote, delete, refresh. Shared with the compat layer later.</summary>
public sealed partial class MovieService(BigarrDbContext db, IMetadataProvider metadata, TimeProvider time, ILogger<MovieService> logger)
{
    public IQueryable<Movie> WithGraph(bool tracking = false)
    {
        var query = db.Movies
            .Include(m => m.Versions).ThenInclude(v => v.QualityProfile)
            .Include(m => m.Versions).ThenInclude(v => v.RootFolder)
            .Include(m => m.Versions).ThenInclude(v => v.File!).ThenInclude(f => f.Quality)
            .AsSplitQuery();
        return tracking ? query : query.AsNoTracking();
    }

    public async Task<IReadOnlySet<int>> ActiveTargetsAsync(IEnumerable<int> targetIds, CancellationToken ct)
    {
        var ids = targetIds.ToList();
        if (ids.Count == 0)
        {
            return new HashSet<int>();
        }

        var active = await db.GrabTargets.Where(t => t.Active && ids.Contains(t.TargetId)).Select(t => t.TargetId).ToListAsync(ct);
        return active.ToHashSet();
    }

    public DateOnly Today => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    public async Task<Result<IReadOnlyList<MovieLookupResult>>> LookupAsync(string term, CancellationToken ct)
    {
        IReadOnlyList<MovieMetadata> found;
        try
        {
            found = await FindAsync(term.Trim(), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Metadata provider failed for lookup '{Term}'", term);
            return new Failure(StatusCodes.Status502BadGateway, "Metadata provider unavailable", ex.Message);
        }

        if (found.Count == 0)
        {
            return Result<IReadOnlyList<MovieLookupResult>>.Ok([]);
        }

        var tmdbIds = found.Select(f => f.TmdbId).ToList();
        var existing = await db.Movies.Where(m => tmdbIds.Contains(m.TmdbId)).Select(m => new { m.TmdbId, m.Id }).ToDictionaryAsync(x => x.TmdbId, x => x.Id, ct);
        var excluded = await db.MovieExclusions.Where(e => tmdbIds.Contains(e.TmdbId)).Select(e => e.TmdbId).ToListAsync(ct);

        return Result<IReadOnlyList<MovieLookupResult>>.Ok(found.Select(f => new MovieLookupResult(
            f.TmdbId,
            f.Title,
            f.OriginalTitle,
            f.Year,
            f.Overview,
            f.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
            f.Status,
            existing.TryGetValue(f.TmdbId, out var id) ? id : null,
            excluded.Contains(f.TmdbId))).ToList());
    }

    private async Task<IReadOnlyList<MovieMetadata>> FindAsync(string term, CancellationToken ct)
    {
        if (term.StartsWith("tmdb:", StringComparison.OrdinalIgnoreCase) && int.TryParse(term[5..], out var tmdbId))
        {
            var one = await metadata.GetMovieAsync(tmdbId, ct);
            return one is null ? [] : [one];
        }

        if (term.StartsWith("imdb:", StringComparison.OrdinalIgnoreCase) || ImdbIdRegex().IsMatch(term))
        {
            var imdbId = term.StartsWith("imdb:", StringComparison.OrdinalIgnoreCase) ? term[5..] : term;
            var one = await metadata.GetMovieByImdbIdAsync(imdbId.Trim(), ct);
            return one is null ? [] : [one];
        }

        // "Title (2017)" or "Title 2017". A trailing number is only a year when it is plausible:
        // "Blade Runner 2049" is a title, not a film from 2049.
        var match = TitleYearRegex().Match(term);
        if (match.Success && int.TryParse(match.Groups["year"].Value, out var year) && year is >= FirstFilmYear && year <= Today.Year + 5)
        {
            var title = match.Groups["title"].Value.Trim();
            var withYear = await metadata.SearchAsync(title, year, ct);
            if (withYear.Count > 0 || match.Groups["paren"].Success)
            {
                return withYear;
            }

            // "Movie 2012" found nothing: the number may belong to the title after all.
        }

        return await metadata.SearchAsync(term, null, ct);
    }

    private const int FirstFilmYear = 1888;

    public async Task<Result<Movie>> AddMovieAsync(AddMovieRequest request, CancellationToken ct)
    {
        var existingId = await db.Movies.Where(m => m.TmdbId == request.TmdbId).Select(m => (int?)m.Id).FirstOrDefaultAsync(ct);
        if (existingId is not null)
        {
            return Failure.Conflict("Movie already in the library", $"TMDB {request.TmdbId} is movie {existingId}. Add a version instead.");
        }

        var profile = await db.QualityProfiles.FindAsync([request.QualityProfileId], ct);
        if (profile is null)
        {
            return Failure.Invalid("Unknown quality profile", $"No quality profile with id {request.QualityProfileId}.");
        }

        var root = await db.RootFolders.FindAsync([request.RootFolderId], ct);
        if (root is null)
        {
            return Failure.Invalid("Unknown root folder", $"No root folder with id {request.RootFolderId}.");
        }

        var versionName = string.IsNullOrWhiteSpace(request.VersionName) ? VersionNaming.FromProfile(profile.Name) : request.VersionName.Trim();
        if (!Slug.IsValid(versionName))
        {
            return Failure.Invalid("Invalid version name", "A version name is a lowercase slug: letters, digits and single dashes.");
        }

        MovieMetadata? found;
        try
        {
            found = await metadata.GetMovieAsync(request.TmdbId, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Metadata provider failed for TMDB {TmdbId}", request.TmdbId);
            return new Failure(StatusCodes.Status502BadGateway, "Metadata provider unavailable", ex.Message);
        }

        if (found is null)
        {
            return Failure.NotFound($"TMDB movie {request.TmdbId}");
        }

        var movie = Movie.FromMetadata(found, root.Id);
        movie.Versions.Add(new MovieVersion
        {
            Movie = movie,
            Name = versionName,
            Mode = VersionMode.Managed,
            QualityProfile = profile,
            RootFolder = root,
            FolderName = FolderNameBuilder.Build(found.Title, found.Year),
            IsDefault = true,
            Monitored = request.Monitored,
            MinimumAvailability = request.MinimumAvailability,
        });

        // Adding a film is a deliberate act: it lifts a previous exclusion.
        await db.MovieExclusions.Where(e => e.TmdbId == request.TmdbId).ExecuteDeleteAsync(ct);

        db.Movies.Add(movie);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE constraint failed: Movies.TmdbId", StringComparison.Ordinal) == true)
        {
            return Failure.Conflict("Movie already in the library", $"TMDB {request.TmdbId} was added concurrently.");
        }

        if (request.SearchOnAdd)
        {
            logger.LogDebug("searchOnAdd requested for movie {MovieId}: searching arrives with M2", movie.Id);
        }

        return Result<Movie>.Ok(movie);
    }

    public async Task<Result<MovieVersion>> AddVersionAsync(int movieId, AddVersionRequest request, CancellationToken ct)
    {
        var movie = await WithGraph(tracking: true).FirstOrDefaultAsync(m => m.Id == movieId, ct);
        if (movie is null)
        {
            return Failure.NotFound("Movie");
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (!Slug.IsValid(name))
        {
            return Failure.Invalid("Invalid version name", "A version name is a lowercase slug: letters, digits and single dashes.");
        }

        if (movie.Versions.Any(v => v.Name == name))
        {
            return Failure.Conflict("Version name already used", $"Movie {movieId} already has a version named '{name}'.");
        }

        var profile = await db.QualityProfiles.FindAsync([request.QualityProfileId], ct);
        if (profile is null)
        {
            return Failure.Invalid("Unknown quality profile", $"No quality profile with id {request.QualityProfileId}.");
        }

        var rootId = request.RootFolderId ?? movie.DefaultRootFolderId;
        var root = await db.RootFolders.FindAsync([rootId], ct);
        if (root is null)
        {
            return Failure.Invalid("Unknown root folder", $"No root folder with id {rootId}.");
        }

        var defaultVersion = movie.Versions.FirstOrDefault(v => v.IsDefault);
        var folderName = string.IsNullOrWhiteSpace(request.FolderName)
            ? defaultVersion?.FolderName ?? FolderNameBuilder.Build(movie.Title, movie.Year)
            : FolderNameBuilder.Sanitize(request.FolderName);

        var version = new MovieVersion
        {
            Movie = movie,
            Name = name,
            Mode = request.Mode,
            QualityProfile = profile,
            RootFolder = root,
            FolderName = folderName,
            IsDefault = defaultVersion is null && request.Mode == VersionMode.Managed,
            Monitored = request.Monitored,
            MinimumAvailability = request.MinimumAvailability,
            Constraints = MovieMapper.FromConstraints(request.Constraints),
        };

        movie.Versions.Add(version);
        await db.SaveChangesAsync(ct);
        return Result<MovieVersion>.Ok(version);
    }

    public async Task<Result<MovieVersion>> UpdateVersionAsync(int movieId, int versionId, UpdateVersionRequest request, CancellationToken ct)
    {
        var movie = await WithGraph(tracking: true).FirstOrDefaultAsync(m => m.Id == movieId, ct);
        var version = movie?.Versions.FirstOrDefault(v => v.Id == versionId);
        if (movie is null || version is null)
        {
            return Failure.NotFound("Version");
        }

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (!Slug.IsValid(name))
            {
                return Failure.Invalid("Invalid version name", "A version name is a lowercase slug: letters, digits and single dashes.");
            }

            if (movie.Versions.Any(v => v.Id != versionId && v.Name == name))
            {
                return Failure.Conflict("Version name already used", $"Movie {movieId} already has a version named '{name}'.");
            }

            version.Name = name;
        }

        if (request.Mode is { } mode && mode != version.Mode)
        {
            if (version.IsDefault && mode == VersionMode.OneShot)
            {
                return Failure.Conflict("The default version must stay managed", "Promote another version first.");
            }

            version.Mode = mode;
        }

        if (request.QualityProfileId is { } profileId && profileId != version.QualityProfileId)
        {
            var profile = await db.QualityProfiles.FindAsync([profileId], ct);
            if (profile is null)
            {
                return Failure.Invalid("Unknown quality profile", $"No quality profile with id {profileId}.");
            }

            version.QualityProfile = profile;
        }

        if (request.Monitored is { } monitored)
        {
            version.Monitored = monitored;
        }

        if (request.MinimumAvailability is { } availability)
        {
            version.MinimumAvailability = availability;
        }

        if (request.ClearConstraints)
        {
            version.Constraints = null;
        }
        else if (request.Constraints is not null)
        {
            version.Constraints = MovieMapper.FromConstraints(request.Constraints);
        }

        await db.SaveChangesAsync(ct);
        return Result<MovieVersion>.Ok(version);
    }

    public async Task<Result<MovieVersion>> PromoteAsync(int movieId, int versionId, CancellationToken ct)
    {
        var movie = await WithGraph(tracking: true).FirstOrDefaultAsync(m => m.Id == movieId, ct);
        var version = movie?.Versions.FirstOrDefault(v => v.Id == versionId);
        if (movie is null || version is null)
        {
            return Failure.NotFound("Version");
        }

        if (version.Mode != VersionMode.Managed)
        {
            return Failure.Conflict("Only a managed version can be the default", "One-shot versions are grabbed once and never upgraded.");
        }

        if (!version.IsDefault)
        {
            await SwitchDefaultAsync(movie, movie.Versions.FirstOrDefault(v => v.IsDefault), version, ct);
        }

        return Result<MovieVersion>.Ok(version);
    }

    /// <summary>Returns <c>true</c> when the movie itself was deleted (its last version went away).</summary>
    public async Task<Result<bool>> DeleteVersionAsync(int movieId, int versionId, bool deleteFiles, int? promote, bool addImportExclusion, CancellationToken ct)
    {
        var movie = await WithGraph(tracking: true).FirstOrDefaultAsync(m => m.Id == movieId, ct);
        var version = movie?.Versions.FirstOrDefault(v => v.Id == versionId);
        if (movie is null || version is null)
        {
            return Failure.NotFound("Version");
        }

        if (deleteFiles && version.File is not null)
        {
            return FilesDeletionNotAvailable();
        }

        var siblings = movie.Versions.Where(v => v.Id != versionId).ToList();
        if (siblings.Count == 0)
        {
            await RemoveMovieAsync(movie, addImportExclusion, ct);
            return Result<bool>.Ok(true);
        }

        if (version.IsDefault)
        {
            var successor = promote is null ? null : siblings.FirstOrDefault(v => v.Id == promote);
            if (successor is null)
            {
                return Failure.Conflict(
                    "This is the default version",
                    "Choose the new default among the other versions with ?promote=<versionId>.");
            }

            if (successor.Mode != VersionMode.Managed)
            {
                return Failure.Invalid("Only a managed version can become the default");
            }

            await SwitchDefaultAsync(movie, version, successor, ct);
        }

        db.MovieVersions.Remove(version);
        await db.SaveChangesAsync(ct);
        return Result<bool>.Ok(false);
    }

    public async Task<Result<bool>> DeleteMovieAsync(int movieId, bool deleteFiles, bool addImportExclusion, CancellationToken ct)
    {
        var movie = await WithGraph(tracking: true).FirstOrDefaultAsync(m => m.Id == movieId, ct);
        if (movie is null)
        {
            return Failure.NotFound("Movie");
        }

        if (deleteFiles && movie.Versions.Any(v => v.File is not null))
        {
            return FilesDeletionNotAvailable();
        }

        await RemoveMovieAsync(movie, addImportExclusion, ct);
        return Result<bool>.Ok(true);
    }

    public async Task<Result<Movie>> RefreshAsync(int movieId, CancellationToken ct)
    {
        var movie = await WithGraph(tracking: true).FirstOrDefaultAsync(m => m.Id == movieId, ct);
        if (movie is null)
        {
            return Failure.NotFound("Movie");
        }

        MovieMetadata? found;
        try
        {
            found = await metadata.GetMovieAsync(movie.TmdbId, ct);
        }
        catch (HttpRequestException ex)
        {
            return new Failure(StatusCodes.Status502BadGateway, "Metadata provider unavailable", ex.Message);
        }

        if (found is null)
        {
            movie.TmdbStatus = MovieStatus.Deleted;
            movie.LastMetadataRefreshAt = time.GetUtcNow().UtcDateTime;
        }
        else
        {
            movie.ApplyMetadata(found);
        }

        await db.SaveChangesAsync(ct);
        return Result<Movie>.Ok(movie);
    }

    private static Failure FilesDeletionNotAvailable() => Failure.Invalid(
        "Deleting files is not available yet",
        "Physical deletion goes through the recycle bin, which arrives with the import slice (M5). Delete without deleteFiles to keep the files on disk.");

    private async Task RemoveMovieAsync(Movie movie, bool addImportExclusion, CancellationToken ct)
    {
        if (addImportExclusion && !await db.MovieExclusions.AnyAsync(e => e.TmdbId == movie.TmdbId, ct))
        {
            db.MovieExclusions.Add(new MovieExclusion { TmdbId = movie.TmdbId, Title = movie.Title, Year = movie.Year });
        }

        db.Movies.Remove(movie);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Two statements in one transaction: the partial unique index forbids two defaults, even transiently.</summary>
    private async Task SwitchDefaultAsync(Movie movie, MovieVersion? previous, MovieVersion next, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (previous is not null)
        {
            previous.IsDefault = false;
            await db.SaveChangesAsync(ct);
        }

        next.IsDefault = true;
        db.History.Add(new HistoryEvent
        {
            TargetId = next.Id,
            MovieId = movie.Id,
            TmdbId = movie.TmdbId,
            MovieTitle = movie.Title,
            VersionName = next.Name,
            EventType = HistoryEventType.DefaultChanged,
            Data = new Dictionary<string, string>
            {
                ["previousVersionId"] = previous?.Id.ToString() ?? string.Empty,
                ["previousVersionName"] = previous?.Name ?? string.Empty,
                ["newVersionId"] = next.Id.ToString(),
            },
        });
        await db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);
    }

    [GeneratedRegex(@"^tt\d{5,10}$", RegexOptions.IgnoreCase)]
    private static partial Regex ImdbIdRegex();

    [GeneratedRegex(@"^(?<title>.+?)\s*(?:(?<paren>\((?<year>\d{4})\))|\s(?<year>\d{4}))\s*$")]
    private static partial Regex TitleYearRegex();
}
