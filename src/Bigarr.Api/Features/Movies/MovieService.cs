using System.Text.RegularExpressions;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Files;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Common;
using Bigarr.Domain.Events;
using Bigarr.Domain.History;
using Bigarr.Domain.Library;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Naming;
using Bigarr.Domain.Tags;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Movies;

/// <summary>Application rules of the Movies slice: add, version, promote, delete, refresh. Shared with the compat layer later.</summary>
public sealed partial class MovieService(BigarrDbContext db, IMetadataProvider metadata, RecycleBin recycleBin, OutboxWriter outbox, SearchQueue searches, TimeProvider time, ILogger<MovieService> logger)
{
    public IQueryable<Movie> WithGraph(bool tracking = false)
    {
        var query = db.Movies
            .Include(m => m.Versions).ThenInclude(v => v.QualityProfile).ThenInclude(p => p.FormatScores)
            .Include(m => m.Versions).ThenInclude(v => v.RootFolder)
            .Include(m => m.Versions).ThenInclude(v => v.File!).ThenInclude(f => f.Quality)
            .Include(m => m.Versions).ThenInclude(v => v.File!).ThenInclude(f => f.Extras)
            .Include(m => m.Versions).ThenInclude(v => v.Tags)
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

        if (Quality.ProfileKindRules.ForMovieVersion(profile) is { } wrongKind)
        {
            return wrongKind;
        }

        var root = await db.RootFolders.FindAsync([request.RootFolderId], ct);
        if (root is null)
        {
            return Failure.Invalid("Unknown root folder", $"No root folder with id {request.RootFolderId}.");
        }

        if (root.Kind != MediaKind.Movies)
        {
            return Failure.Invalid("Not a film root folder", $"'{root.Path}' holds series. Pick a film root folder.");
        }

        var versionName = string.IsNullOrWhiteSpace(request.VersionName) ? VersionNaming.FromProfile(profile.Name) : request.VersionName.Trim();
        if (!Slug.IsValid(versionName))
        {
            return Failure.Invalid("Invalid version name", "A version name is a lowercase slug: letters, digits and single dashes.");
        }

        var tags = await ResolveTagsAsync(request.TagIds, ct);
        if (!tags.IsSuccess)
        {
            return tags.Failure!;
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
            FolderName = await FolderNameAsync(found.Title, found.OriginalTitle, found.Year, found.TmdbId, found.ImdbId, versionName, ct),
            IsDefault = true,
            Monitored = request.Monitored,
            MinimumAvailability = request.MinimumAvailability,
            Tags = tags.Value!,
        });

        // Adding a film is a deliberate act: it lifts a previous exclusion.
        await db.MovieExclusions.Where(e => e.TmdbId == request.TmdbId).ExecuteDeleteAsync(ct);

        db.Movies.Add(movie);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE constraint failed: Movies.TmdbId", StringComparison.Ordinal) == true)
        {
            await transaction.RollbackAsync(ct);
            return Failure.Conflict("Movie already in the library", $"TMDB {request.TmdbId} was added concurrently.");
        }

        var created = movie.Versions.Single();
        outbox.Publish(new MovieAdded(MovieRef.From(movie), VersionRef.From(created)));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        if (request.SearchOnAdd)
        {
            searches.TryEnqueue(created.Id, SearchTrigger.Added);
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

        if (Quality.ProfileKindRules.ForMovieVersion(profile) is { } wrongKind)
        {
            return wrongKind;
        }

        var rootId = request.RootFolderId ?? movie.DefaultRootFolderId;
        var root = await db.RootFolders.FindAsync([rootId], ct);
        if (root is null)
        {
            return Failure.Invalid("Unknown root folder", $"No root folder with id {rootId}.");
        }

        if (root.Kind != MediaKind.Movies)
        {
            return Failure.Invalid("Not a film root folder", $"'{root.Path}' holds series. Pick a film root folder.");
        }

        var constraints = MovieMapper.FromConstraints(request.Constraints);
        if (await ValidateCustomFormatsAsync(constraints, ct) is { } invalidConstraints)
        {
            return invalidConstraints;
        }

        var tags = await ResolveTagsAsync(request.TagIds, ct);
        if (!tags.IsSuccess)
        {
            return tags.Failure!;
        }

        var defaultVersion = movie.Versions.FirstOrDefault(v => v.IsDefault);
        var folderName = string.IsNullOrWhiteSpace(request.FolderName)
            ? defaultVersion?.FolderName ?? await FolderNameAsync(movie.Title, movie.OriginalTitle, movie.Year, movie.TmdbId, movie.ImdbId, name, ct)
            : FolderNameBuilder.Sanitize(request.FolderName, (await MediaManagementAsync(ct)).FilmNameCleaning);

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
            Constraints = constraints,
            Tags = tags.Value!,
        };

        movie.Versions.Add(version);
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.SaveChangesAsync(ct);
            outbox.Publish(new VersionAdded(MovieRef.From(movie), VersionRef.From(version)));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        if (request.SearchOnAdd)
        {
            searches.TryEnqueue(version.Id, SearchTrigger.Added);
        }

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

            if (Quality.ProfileKindRules.ForMovieVersion(profile) is { } wrongKind)
            {
                return wrongKind;
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
            var constraints = MovieMapper.FromConstraints(request.Constraints);
            if (await ValidateCustomFormatsAsync(constraints, ct) is { } invalidConstraints)
            {
                return invalidConstraints;
            }

            version.Constraints = constraints;
        }

        if (request.TagIds is not null)
        {
            var tags = await ResolveTagsAsync(request.TagIds, ct);
            if (!tags.IsSuccess)
            {
                return tags.Failure!;
            }

            version.Tags.Clear();
            foreach (var tag in tags.Value!)
            {
                version.Tags.Add(tag);
            }
        }

        // A no-op PUT (Seerr re-sends the whole resource) publishes nothing: consumers only hear about actual changes.
        if (db.ChangeTracker.HasChanges())
        {
            outbox.Publish(new VersionUpdated(MovieRef.From(movie), VersionRef.From(version)));
            await db.SaveChangesAsync(ct);
        }

        return Result<MovieVersion>.Ok(version);
    }

    private async Task<Result<List<Tag>>> ResolveTagsAsync(List<int>? ids, CancellationToken ct)
    {
        var wanted = (ids ?? []).Distinct().ToList();
        if (wanted.Count == 0)
        {
            return Result<List<Tag>>.Ok([]);
        }

        var tags = await db.Tags.Where(t => wanted.Contains(t.Id)).ToListAsync(ct);
        var unknown = wanted.Except(tags.Select(t => t.Id)).ToList();
        return unknown.Count == 0 ? Result<List<Tag>>.Ok(tags) : Failure.Invalid("Unknown tag", $"No tag with id {string.Join(", ", unknown)}.");
    }

    /// <summary>Constraints may name custom formats (§3.3); they must exist and not contradict each other.</summary>
    private async Task<Failure?> ValidateCustomFormatsAsync(AcquisitionConstraints? constraints, CancellationToken ct)
    {
        if (constraints is null)
        {
            return null;
        }

        if (constraints.RequiredCustomFormatIds.Intersect(constraints.ForbiddenCustomFormatIds).Any())
        {
            return Failure.Invalid("Contradictory constraints", "A custom format cannot be both required and forbidden.");
        }

        var ids = constraints.RequiredCustomFormatIds.Concat(constraints.ForbiddenCustomFormatIds).Distinct().ToList();
        if (ids.Count == 0)
        {
            return null;
        }

        var known = await db.CustomFormats.Where(f => ids.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct);
        var unknown = ids.Except(known).ToList();
        return unknown.Count == 0 ? null : Failure.Invalid("Unknown custom format", $"No custom format with id {string.Join(", ", unknown)}.");
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

        // Everything that can refuse the delete is checked before the disk is touched: a refusal must leave the file alone.
        var siblings = movie.Versions.Where(v => v.Id != versionId).ToList();
        MovieVersion? successor = null;
        if (siblings.Count > 0 && version.IsDefault)
        {
            successor = promote is null ? null : siblings.FirstOrDefault(v => v.Id == promote);
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
        }

        if (deleteFiles && version.File is not null)
        {
            var removal = await RemoveFileFromDiskAsync(movie, version, ct);
            if (removal is not null)
            {
                return removal;
            }
        }

        var folder = Path.Combine(version.RootFolder.Path, version.FolderName);
        var folderShared = siblings.Any(v => v.RootFolderId == version.RootFolderId && v.FolderName == version.FolderName);
        if (siblings.Count == 0)
        {
            outbox.Publish(new MovieDeleted(MovieRef.From(movie), deleteFiles, [VersionRef.From(version)]));
            await RemoveMovieAsync(movie, addImportExclusion, ct);
            if (deleteFiles)
            {
                RecycleBin.DeleteIfEmpty(folder);
            }

            return Result<bool>.Ok(true);
        }

        if (successor is not null)
        {
            await SwitchDefaultAsync(movie, version, successor, ct);
        }

        outbox.Publish(new VersionDeleted(MovieRef.From(movie), VersionRef.From(version), deleteFiles));
        db.MovieVersions.Remove(version);
        await db.SaveChangesAsync(ct);
        if (deleteFiles && !folderShared)
        {
            RecycleBin.DeleteIfEmpty(folder);
        }

        return Result<bool>.Ok(false);
    }

    /// <summary>With <paramref name="deleteFiles"/>, every folder of the film goes to the recycle bin (extras included), like Radarr's "delete movie folder".</summary>
    public async Task<Result<bool>> DeleteMovieAsync(int movieId, bool deleteFiles, bool addImportExclusion, CancellationToken ct)
    {
        var movie = await WithGraph(tracking: true).FirstOrDefaultAsync(m => m.Id == movieId, ct);
        if (movie is null)
        {
            return Failure.NotFound("Movie");
        }

        if (deleteFiles)
        {
            var config = await MediaManagementAsync(ct);
            var now = time.GetUtcNow().UtcDateTime;
            foreach (var version in movie.Versions.Where(v => v.File is not null))
            {
                db.History.Add(FileDeletedEvent(movie, version, Path.Combine(version.RootFolder.Path, version.FolderName, version.File!.RelativePath), null, now));
            }

            foreach (var group in movie.Versions.GroupBy(v => (v.RootFolderId, v.FolderName)))
            {
                var root = group.First().RootFolder;
                var folder = Path.Combine(root.Path, group.Key.FolderName);
                try
                {
                    recycleBin.RemoveDirectory(folder, config.RecycleBinPath, root.Path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "Could not delete folder {Folder}", folder);
                    return new Failure(StatusCodes.Status500InternalServerError, "Could not delete the movie folder", $"{folder}: {ex.Message}");
                }
            }
        }

        outbox.Publish(new MovieDeleted(MovieRef.From(movie), deleteFiles, movie.Versions.Select(VersionRef.From).ToList()));
        await RemoveMovieAsync(movie, addImportExclusion, ct);
        return Result<bool>.Ok(true);
    }

    /// <summary>Drops the version's file (to the recycle bin when configured) and keeps the version: it is missing again.</summary>
    public async Task<Result<MovieVersion>> DeleteFileAsync(int movieId, int versionId, CancellationToken ct)
    {
        var movie = await WithGraph(tracking: true).FirstOrDefaultAsync(m => m.Id == movieId, ct);
        var version = movie?.Versions.FirstOrDefault(v => v.Id == versionId);
        if (movie is null || version is null)
        {
            return Failure.NotFound("Version");
        }

        if (version.File is null)
        {
            return Failure.Conflict("No file", $"Version {version.Name} has no file.");
        }

        var removal = await RemoveFileFromDiskAsync(movie, version, ct);
        if (removal is not null)
        {
            return removal;
        }

        db.MediaFiles.Remove(version.File);
        version.File = null;
        await db.SaveChangesAsync(ct);
        return Result<MovieVersion>.Ok(version);
    }

    /// <summary>Moves the file to the recycle bin (or deletes it) and journals it. Null when it went fine.</summary>
    private async Task<Failure?> RemoveFileFromDiskAsync(Movie movie, MovieVersion version, CancellationToken ct)
    {
        var config = await MediaManagementAsync(ct);
        var path = Path.Combine(version.RootFolder.Path, version.FolderName, version.File!.RelativePath);
        string? recycled;
        try
        {
            recycled = recycleBin.RemoveFile(path, config.RecycleBinPath, version.RootFolder.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete file {Path}", path);
            return new Failure(StatusCodes.Status500InternalServerError, "Could not delete the file", $"{path}: {ex.Message}");
        }

        // The extras imported with it leave with it. Anything else in the folder (Bazarr's subtitles) is not Bigarr's to delete.
        foreach (var extra in version.File!.Extras)
        {
            var extraPath = Path.Combine(version.RootFolder.Path, version.FolderName, extra.RelativePath);
            try
            {
                recycleBin.RemoveFile(extraPath, config.RecycleBinPath, version.RootFolder.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Could not delete extra file {Path}: {Message}", extraPath, ex.Message);
            }
        }

        db.History.Add(FileDeletedEvent(movie, version, path, recycled, time.GetUtcNow().UtcDateTime));
        outbox.Publish(new FileDeleted(MovieRef.From(movie), VersionRef.From(version), FileRef.From(version.File!, path) with { RecycleBinPath = recycled }, recycled is null ? "deleted" : "recycled"));
        return null;
    }

    private static HistoryEvent FileDeletedEvent(Movie movie, MovieVersion version, string path, string? recycledTo, DateTime now)
    {
        var data = new Dictionary<string, string>
        {
            ["path"] = path,
            ["quality"] = version.File?.Quality?.Name ?? string.Empty,
            ["size"] = (version.File?.Size ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (recycledTo is not null)
        {
            data["recycledTo"] = recycledTo;
        }

        return new HistoryEvent
        {
            TargetId = version.Id,
            MovieId = movie.Id,
            TmdbId = movie.TmdbId,
            MovieTitle = movie.Title,
            VersionName = version.Name,
            EventType = HistoryEventType.FileDeleted,
            SourceTitle = version.File?.OriginalReleaseTitle,
            Data = data,
            CreatedAt = now,
        };
    }

    private async Task<MediaManagementConfig> MediaManagementAsync(CancellationToken ct) =>
        await db.MediaManagement.AsNoTracking().FirstOrDefaultAsync(c => c.Id == MediaManagementConfig.SingletonId, ct) ?? MediaManagementConfig.Default();

    /// <summary>The folder template of the media management settings, on the film; the default builder when it yields nothing.</summary>
    private async Task<string> FolderNameAsync(string title, string? originalTitle, int? year, int tmdbId, string? imdbId, string versionName, CancellationToken ct)
    {
        var config = await MediaManagementAsync(ct);
        var name = NamingTemplate.RenderFolder(config.FolderNameTemplate, new NamingContext(title, originalTitle, year, tmdbId, imdbId, versionName), config.FilmNameCleaning);
        return name.Length == 0 || name == "_" ? FolderNameBuilder.Build(title, year, config.FilmNameCleaning) : name;
    }

    public async Task<Result<Movie>> RefreshAsync(int movieId, CancellationToken ct)
    {
        var movie = await WithGraph(tracking: true).FirstOrDefaultAsync(m => m.Id == movieId, ct);
        if (movie is null)
        {
            return Failure.NotFound("Movie");
        }

        try
        {
            await ApplyLatestMetadataAsync(movie, ct);
        }
        catch (HttpRequestException ex)
        {
            return new Failure(StatusCodes.Status502BadGateway, "Metadata provider unavailable", ex.Message);
        }

        await db.SaveChangesAsync(ct);
        return Result<Movie>.Ok(movie);
    }

    /// <summary>
    /// Asks the provider again and applies the answer, shared by the refresh endpoint and the scheduled refresh. Metadata
    /// only: versions, monitoring, files and folders stay as they are, and a new title or year renames nothing (Radarr
    /// does not either). A film the provider no longer knows is flagged deleted. Provider failures propagate; the caller saves.
    /// </summary>
    public async Task ApplyLatestMetadataAsync(Movie movie, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(movie);

        var found = await metadata.GetMovieAsync(movie.TmdbId, ct);
        if (found is null)
        {
            movie.TmdbStatus = MovieStatus.Deleted;
        }
        else
        {
            movie.ApplyMetadata(found);
        }

        movie.LastMetadataRefreshAt = time.GetUtcNow().UtcDateTime;
    }

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
        outbox.Publish(new DefaultVersionChanged(MovieRef.From(movie), VersionRef.From(next), previous is null ? null : VersionRef.From(previous)));
        await db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);
    }

    [GeneratedRegex(@"^tt\d{5,10}$", RegexOptions.IgnoreCase)]
    private static partial Regex ImdbIdRegex();

    [GeneratedRegex(@"^(?<title>.+?)\s*(?:(?<paren>\((?<year>\d{4})\))|\s(?<year>\d{4}))\s*$")]
    private static partial Regex TitleYearRegex();
}
