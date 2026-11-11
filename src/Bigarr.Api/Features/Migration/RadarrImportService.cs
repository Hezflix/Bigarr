using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Tags;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.RadarrImport;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Common;
using Bigarr.Domain.Events;
using Bigarr.Domain.History;
using Bigarr.Domain.Library;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Shadow;
using Bigarr.Domain.Tags;
using Bigarr.Parsing;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Migration;

/// <summary>
/// Moves a Radarr library into Bigarr (M8). Every Radarr movie becomes one version of the film, named after the Radarr
/// it comes from (<c>1080p</c>, <c>4k</c>), with its monitored flag, profile, tags, availability and file; a second
/// Radarr imported with another version name lands as a second version of the same films. Metadata comes from
/// Radarr's own payload, so the import needs no TMDB round trip; the film is flagged for a refresh. Radarr's history of
/// each imported film is copied into the journal, so the film page still says where its file came from, and its blocklist
/// onto the version, so a release Radarr rejected is not grabbed again. Custom formats come along, and each mapped
/// profile takes the format scores of the Radarr profile mapped to it. In shadow mode the same per-film rules follow a
/// Radarr afterwards (<see cref="FollowAsync"/>), files included.
/// </summary>
public sealed class RadarrImportService(BigarrDbContext db, RadarrClient radarr, IMetadataProvider metadata, OutboxWriter outbox, TimeProvider time, ILogger<RadarrImportService> logger)
{
    private static readonly char[] Separators = ['/', '\\'];

    public async Task<Result<RadarrImportPreviewResponse>> PreviewAsync(RadarrImportPreviewRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Connection(request.Url, request.ApiKey) is not { } connection)
        {
            return Failure.Invalid("Radarr URL and API key are required");
        }

        Snapshot snapshot;
        try
        {
            snapshot = await LoadAsync(connection, withHistory: false, withBlocklist: false, ct);
            snapshot = snapshot with { HistoryCount = await radarr.HistoryCountAsync(connection, ct), BlocklistCount = await radarr.BlocklistCountAsync(connection, ct) };
        }
        catch (RadarrUnavailableException ex)
        {
            return new Failure(StatusCodes.Status502BadGateway, "Radarr did not answer", ex.Message);
        }

        var profiles = await db.QualityProfiles.AsNoTracking().Where(p => p.Kind == ProfileKind.Movies || p.Kind == ProfileKind.Both).Select(p => new { p.Id, p.Name }).ToListAsync(ct);
        var roots = await db.RootFolders.AsNoTracking().ToListAsync(ct);
        var known = (await db.Movies.AsNoTracking().Select(m => m.TmdbId).ToListAsync(ct)).ToHashSet();

        var byProfile = snapshot.Movies.GroupBy(m => m.QualityProfileId).ToDictionary(g => g.Key, g => g.Count());
        var profilePreviews = snapshot.Profiles
            .Select(p => new RadarrProfilePreview(p.Id, p.Name, byProfile.GetValueOrDefault(p.Id), SuggestProfile(p.Name, profiles.Select(b => (b.Id, b.Name)))))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rootPaths = snapshot.RootFolders.Select(r => NormalizePath(r.Path)).Concat(snapshot.Movies.Select(RootPathOf).OfType<string>()).Distinct(StringComparer.Ordinal).ToList();
        var byRoot = snapshot.Movies.GroupBy(m => RootPathOf(m) ?? string.Empty).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var rootPreviews = rootPaths
            .Select(path => new RadarrRootFolderPreview(
                path,
                byRoot.GetValueOrDefault(path),
                roots.FirstOrDefault(r => string.Equals(NormalizePath(r.Path), path, StringComparison.Ordinal))?.Id,
                Directory.Exists(path)))
            .OrderBy(r => r.Path, StringComparer.Ordinal)
            .ToList();

        var byTag = snapshot.Movies.SelectMany(m => m.Tags ?? []).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        var tagPreviews = snapshot.Tags.Select(t => new RadarrTagPreview(t.Id, TagService.NormalizeLabel(t.Label), byTag.GetValueOrDefault(t.Id))).OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase).ToList();

        var resolutions = snapshot.Movies.Select(m => m.MovieFile?.Quality?.Quality?.Resolution ?? 0).Where(r => r > 0).ToList();
        var mostlyUhd = resolutions.Count > 0 && resolutions.Count(r => r >= QualitySeeder.UhdResolution) * 2 > resolutions.Count;
        var suggestedName = mostlyUhd || snapshot.Profiles.All(p => LooksUhd(p.Name)) ? "4k" : "1080p";

        return Result<RadarrImportPreviewResponse>.Ok(new RadarrImportPreviewResponse(
            snapshot.Status.Version ?? "?",
            snapshot.Movies.Count,
            snapshot.Movies.Count(m => m.HasFile && m.MovieFile is not null),
            snapshot.Movies.Count(m => m.Monitored),
            snapshot.Movies.Count(m => known.Contains(m.TmdbId)),
            snapshot.Exclusions.Count,
            snapshot.HistoryCount,
            snapshot.BlocklistCount,
            snapshot.CustomFormats.GetArrayLength(),
            suggestedName,
            profilePreviews,
            rootPreviews,
            tagPreviews));
    }

    /// <summary>What must hold before an import is queued: a valid version name and mappings that point at things Bigarr has.</summary>
    public async Task<Failure?> ValidateAsync(RadarrImportRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Connection(request.Url, request.ApiKey) is null)
        {
            return Failure.Invalid("Radarr URL and API key are required");
        }

        if (!Slug.IsValid(request.VersionName?.Trim()))
        {
            return Failure.Invalid("Invalid version name", "A version name is a lowercase slug: letters, digits and single dashes.");
        }

        var profileIds = (request.ProfileMap ?? []).Values.Distinct().ToList();
        var knownProfiles = await db.QualityProfiles.Where(p => profileIds.Contains(p.Id)).Select(p => new { p.Id, p.Name, p.Kind }).ToListAsync(ct);
        if (profileIds.Except(knownProfiles.Select(p => p.Id)).Any())
        {
            return Failure.Invalid("Unknown quality profile", $"No quality profile with id {string.Join(", ", profileIds.Except(knownProfiles.Select(p => p.Id)))}.");
        }

        if (knownProfiles.FirstOrDefault(p => !p.Kind.ForMovies()) is { } seriesProfile)
        {
            return Failure.Invalid("Not a film quality profile", $"'{seriesProfile.Name}' is a series profile: map Radarr's profiles onto Films or Both profiles.");
        }

        var rootIds = (request.RootFolderMap ?? []).Values.Distinct().ToList();
        var knownRoots = await db.RootFolders.Where(r => rootIds.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct);
        if (rootIds.Except(knownRoots).Any())
        {
            return Failure.Invalid("Unknown root folder", $"No root folder with id {string.Join(", ", rootIds.Except(knownRoots))}.");
        }

        return null;
    }

    public async Task ImportAsync(RadarrImportProgress progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var request = progress.Request;
        var connection = Connection(request.Url, request.ApiKey) ?? throw new InvalidOperationException("Radarr URL and API key are required.");
        var startedAt = time.GetUtcNow().UtcDateTime;
        var snapshot = await LoadAsync(connection, request.ImportHistory, request.ImportBlocklist, ct);
        var context = await ContextAsync(request, snapshot, progress, ct);

        if (request.ImportCustomFormats || request.ImportProfileQualities)
        {
            progress.Current = "Custom formats and profiles";
            await ImportProfilesAsync(snapshot, request, progress, ct);
        }

        if (request.ImportNaming)
        {
            progress.Current = "Naming";
            await ImportNamingAsync(connection, progress, ct);
        }

        progress.Total = snapshot.Movies.Count;
        foreach (var item in snapshot.Movies.OrderBy(m => m.SortTitle ?? m.Title, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            progress.Current = item.Title;
            try
            {
                await ImportOneAsync(item, context, progress, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Import of {Title} (TMDB {TmdbId}) from Radarr failed", item.Title, item.TmdbId);
                db.ChangeTracker.Clear();
                progress.Failed();
                progress.Issue(item.Title, item.TmdbId, ex.Message, await MovieIdAsync(item.TmdbId, ct));
            }
            finally
            {
                db.ChangeTracker.Clear();
                progress.MovieDone();
            }
        }

        if (request.ImportExclusions)
        {
            await ImportExclusionsAsync(snapshot, progress, ct);
        }

        if (request.Follow)
        {
            await KeepFollowingAsync(request, startedAt, ct);
        }

        progress.Current = null;
    }

    /// <summary>
    /// Radarr's "Replace Illegal Characters" and "Colon Replacement" into the film naming settings (its templates stay out of
    /// it: Radarr's are not Bigarr's, which also name versions). A Radarr older than the settings, or a value Bigarr does not
    /// know, leaves Bigarr's as they are. Import only: the follow never rewrites them.
    /// </summary>
    public async Task<bool> ImportNamingAsync(RadarrConnection connection, RadarrImportProgress? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var naming = await radarr.NamingAsync(connection, ct);
        if (naming is null)
        {
            return false;
        }

        var colon = NamingMapping.Colon(naming.ColonReplacementFormat, allowCustom: false);
        if (naming.ReplaceIllegalCharacters is null && colon is null)
        {
            return false;
        }

        var config = await db.MediaManagement.FirstOrDefaultAsync(c => c.Id == MediaManagementConfig.SingletonId, ct);
        if (config is null)
        {
            config = MediaManagementConfig.Default();
            db.MediaManagement.Add(config);
        }

        config.ReplaceIllegalCharacters = naming.ReplaceIllegalCharacters ?? config.ReplaceIllegalCharacters;
        config.ColonReplacement = colon ?? config.ColonReplacement;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        if (progress is not null)
        {
            progress.NamingImported = true;
        }

        logger.LogInformation("Radarr naming imported: replace illegal characters {Replace}, colon {Colon}", config.ReplaceIllegalCharacters, config.ColonReplacement);
        return true;
    }

    private async Task<MediaManagementConfig> ConfigAsync(CancellationToken ct) =>
        await db.MediaManagement.AsNoTracking().FirstOrDefaultAsync(c => c.Id == MediaManagementConfig.SingletonId, ct) ?? MediaManagementConfig.Default();

    /// <summary>
    /// "Keep following this Radarr": the import's mapping becomes a followed Radarr whose history is read from the moment
    /// the import loaded it, so nothing between the import and the first pass is lost. A Radarr already followed under the
    /// same URL and version keeps its checkpoint and takes the new mapping.
    /// </summary>
    private async Task KeepFollowingAsync(RadarrImportRequest request, DateTime startedAt, CancellationToken ct)
    {
        var url = request.Url.Trim();
        var versionName = request.VersionName.Trim();
        var follow = await db.FollowedRadarrs.FirstOrDefaultAsync(f => f.Url == url && f.VersionName == versionName, ct);
        if (follow is null)
        {
            var name = $"Radarr {versionName}";
            for (var n = 2; await db.FollowedRadarrs.AnyAsync(f => f.Name == name, ct); n++)
            {
                name = $"Radarr {versionName} {n}";
            }

            follow = new FollowedRadarr { Name = name, Url = url, ApiKey = request.ApiKey.Trim(), VersionName = versionName, HistoryCheckpoint = startedAt, CreatedAt = time.GetUtcNow().UtcDateTime };
            db.FollowedRadarrs.Add(follow);
        }

        follow.ApiKey = request.ApiKey.Trim();
        follow.ProfileMap = new Dictionary<int, int>(request.ProfileMap ?? []);
        follow.RootFolderMap = new Dictionary<string, int>(request.RootFolderMap ?? [], StringComparer.Ordinal);
        follow.ImportTags = request.ImportTags;
        follow.KeepMonitored = request.KeepMonitored;
        follow.IncludeMoviesWithoutFile = request.IncludeMoviesWithoutFile;
        follow.Enabled = true;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        logger.LogInformation("Radarr at {Url} is now followed as {Name} ({Version})", url, follow.Name, versionName);
    }

    /// <summary>
    /// One pass of the follow (shadow mode): the films of a followed Radarr that differ from their Bigarr version, or that
    /// have history since the last pass, go through the import's per-film rules again (Radarr's settings win, new films
    /// and versions come in, history and blocklist are copied without duplicates), plus what a plain import leaves alone:
    /// the version's file follows Radarr's (imported, upgraded, renamed, deleted), in the database only. Custom formats,
    /// profiles and exclusions are not touched.
    /// </summary>
    public Task<RadarrImportProgress> FollowAsync(RadarrImportRequest request, RadarrFollowData data, CancellationToken ct) =>
        FollowCoreAsync(request, data, newFilmsOnly: false, ct);

    /// <summary>
    /// The first step of a pass: the films (or versions) Radarr has and Bigarr does not, brought in without their file nor
    /// their history. Radarr's grabs of them are then judged, and searched for, against the film as it was before Radarr's
    /// download; <see cref="FollowAsync"/> brings the files and the history right after.
    /// </summary>
    public Task<RadarrImportProgress> AddNewFilmsAsync(RadarrImportRequest request, RadarrFollowData data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return FollowCoreAsync(request with { ImportHistory = false, ImportBlocklist = false }, data, newFilmsOnly: true, ct);
    }

    private async Task<RadarrImportProgress> FollowCoreAsync(RadarrImportRequest request, RadarrFollowData data, bool newFilmsOnly, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(data);

        var progress = new RadarrImportProgress(request) { State = RadarrImportState.Running, StartedAt = time.GetUtcNow().UtcDateTime };
        var movies = data.Movies.Where(m => m.TmdbId > 0 && !string.IsNullOrWhiteSpace(m.Title)).ToList();
        var snapshot = new Snapshot(
            new RadarrSystemStatus(null, null, null), data.Profiles, [], data.Tags, movies, [], EmptyFormats,
            data.History.ToLookup(h => h.MovieId), data.History.Count, data.Blocklist.ToLookup(b => b.MovieId), data.Blocklist.Count);

        var changed = await ChangedAsync(snapshot, request, newFilmsOnly, ct);
        progress.Total = changed.Count;
        if (changed.Count == 0)
        {
            return progress;
        }

        var context = await ContextAsync(request, snapshot with { Movies = changed }, progress, ct) with { Follow = true, SkipFiles = newFilmsOnly };
        foreach (var item in changed)
        {
            ct.ThrowIfCancellationRequested();
            progress.Current = item.Title;
            try
            {
                await ImportOneAsync(item, context, progress, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Following {Title} (TMDB {TmdbId}) from Radarr failed", item.Title, item.TmdbId);
                db.ChangeTracker.Clear();
                progress.Failed();
                progress.Issue(item.Title, item.TmdbId, ex.Message, await MovieIdAsync(item.TmdbId, ct));
            }
            finally
            {
                db.ChangeTracker.Clear();
                progress.MovieDone();
            }
        }

        progress.Current = null;
        return progress;
    }

    /// <summary>
    /// The Radarr films the follow has to look at: not in Bigarr yet as this version, or with a monitored flag, profile,
    /// availability, tags, folder or file that differs, or with history lines in this pass. Only the first kind when
    /// <paramref name="newFilmsOnly"/>.
    /// </summary>
    private async Task<List<RadarrMovie>> ChangedAsync(Snapshot snapshot, RadarrImportRequest request, bool newFilmsOnly, CancellationToken ct)
    {
        var versionName = request.VersionName.Trim();
        var bigarr = (await db.MovieVersions.AsNoTracking()
                .Where(v => v.Name == versionName)
                .Select(v => new
                {
                    v.Movie.TmdbId,
                    v.Monitored,
                    v.QualityProfileId,
                    v.MinimumAvailability,
                    v.FolderName,
                    Tags = v.Tags.Select(t => t.Label).ToList(),
                    File = v.File == null ? null : new { v.File.RelativePath, v.File.Size, v.File.QualityId },
                })
                .ToListAsync(ct))
            .GroupBy(v => v.TmdbId)
            .ToDictionary(g => g.Key, g => g.First());
        var qualityIds = (await db.QualityDefinitions.AsNoTracking().Select(q => q.Id).ToListAsync(ct)).ToHashSet();
        var tagLabels = snapshot.Tags.ToDictionary(t => t.Id, t => TagService.NormalizeLabel(t.Label));
        var profileMap = request.ProfileMap ?? [];

        var changed = new List<RadarrMovie>();
        foreach (var item in snapshot.Movies)
        {
            var file = item.HasFile ? item.MovieFile : null;
            if (!bigarr.TryGetValue(item.TmdbId, out var version))
            {
                if (file is not null || request.IncludeMoviesWithoutFile)
                {
                    changed.Add(item);
                }

                continue;
            }

            if (newFilmsOnly)
            {
                continue;
            }

            if (snapshot.History[item.Id].Any())
            {
                changed.Add(item);
                continue;
            }

            var tags = request.ImportTags
                ? (item.Tags ?? []).Select(id => tagLabels.GetValueOrDefault(id) ?? string.Empty).Where(l => l.Length is > 0 and <= TagService.MaxLabelLength).ToHashSet(StringComparer.Ordinal)
                : null;
            var relative = file is null ? null : RelativePathOf(file, item);
            var sameFile = version.File is null
                ? file is null
                : file is not null && relative is not null && SameFile(version.File.RelativePath, version.File.Size, version.File.QualityId, relative, file, qualityIds);
            var settingsDiffer = version.Monitored != (request.KeepMonitored && item.Monitored)
                || (profileMap.TryGetValue(item.QualityProfileId, out var profileId) && profileId != version.QualityProfileId)
                || version.MinimumAvailability != Availability(item.MinimumAvailability)
                || (LastSegment(item.Path) is { } folder && folder != version.FolderName)
                || (tags is not null && !tags.SetEquals(version.Tags));
            if (settingsDiffer || !sameFile)
            {
                changed.Add(item);
            }
        }

        return changed;
    }

    /// <summary>Radarr's file path below the film's folder, with this host's separators; null when Radarr gives none.</summary>
    private static string? RelativePathOf(RadarrMovieFile file, RadarrMovie item) =>
        (file.RelativePath ?? RelativeTo(file.Path, item.Path))?.Replace('\\', '/').TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

    /// <summary>Same path, same size, same quality (when Radarr's quality is one Bigarr knows): the file Bigarr already has.</summary>
    private static bool SameFile(string relativePath, long size, int qualityId, string relative, RadarrMovieFile file, HashSet<int> qualityIds) =>
        string.Equals(relativePath, relative, StringComparison.Ordinal)
        && (file.Size <= 0 || size == file.Size)
        && (file.Quality?.Quality is not { } quality || !qualityIds.Contains(quality.Id) || quality.Id == qualityId);

    private async Task ImportOneAsync(RadarrMovie item, ImportContext context, RadarrImportProgress progress, CancellationToken ct)
    {
        if (!context.Profiles.TryGetValue(item.QualityProfileId, out var profileId))
        {
            var name = context.Snapshot.Profiles.FirstOrDefault(p => p.Id == item.QualityProfileId)?.Name ?? item.QualityProfileId.ToString();
            await SkipAsync(progress, item, $"Quality profile '{name}' is not mapped.", ct);
            return;
        }

        var radarrRoot = RootPathOf(item);
        if (radarrRoot is null || !context.Roots.TryGetValue(radarrRoot, out var root))
        {
            await SkipAsync(progress, item, $"Root folder '{radarrRoot ?? "?"}' is not mapped.", ct);
            return;
        }

        var file = item.HasFile ? item.MovieFile : null;
        if (file is null && !context.Request.IncludeMoviesWithoutFile)
        {
            await SkipAsync(progress, item, "No file in Radarr.", ct);
            return;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var monitored = context.Request.KeepMonitored && item.Monitored;
        var availability = Availability(item.MinimumAvailability);
        var tags = context.Request.ImportTags ? await TagsForAsync(item.Tags, context, ct) : [];
        var folderName = LastSegment(item.Path) ?? FolderNameBuilder.Build(item.Title, item.Year > 0 ? item.Year : null, (await ConfigAsync(ct)).FilmNameCleaning);

        var movie = await db.Movies
            .Include(m => m.Versions).ThenInclude(v => v.Tags)
            .Include(m => m.Versions).ThenInclude(v => v.File)
            .FirstOrDefaultAsync(m => m.TmdbId == item.TmdbId, ct);

        MovieVersion version;
        var newMovie = false;
        var newVersion = false;
        var settingsChanged = false;
        if (movie is null)
        {
            var found = Metadata(item) ?? await FetchAsync(item.TmdbId, ct);
            if (found is null)
            {
                progress.Failed();
                progress.Issue(item.Title, item.TmdbId, "No metadata: Radarr's payload has no title and the metadata provider knows no such film.");
                return;
            }

            movie = Movie.FromMetadata(found, root.Id);
            movie.AddedAt = item.Added ?? now;
            // Radarr's snapshot is good enough to start; a later refresh normalizes it against the provider.
            movie.LastMetadataRefreshAt = null;
            version = NewVersion(context.VersionName, profileId, root.Id, folderName, monitored, availability, tags, isDefault: true);
            movie.Versions.Add(version);
            db.Movies.Add(movie);
            newMovie = true;
        }
        else if (movie.Versions.FirstOrDefault(v => v.Name == context.VersionName) is { } existing)
        {
            // The film already has this version (a previous import, or the library scan): Radarr's settings win, the folder stays.
            version = existing;
            settingsChanged = version.Monitored != monitored
                || version.QualityProfileId != profileId
                || version.MinimumAvailability != availability
                || (context.Request.ImportTags && !version.Tags.Select(t => t.Id).ToHashSet().SetEquals(tags.Select(t => t.Id)));
            version.Monitored = monitored;
            version.QualityProfileId = profileId;
            version.MinimumAvailability = availability;
            if (context.Request.ImportTags)
            {
                version.Tags.Clear();
                foreach (var tag in tags)
                {
                    version.Tags.Add(tag);
                }
            }

            if (context.Follow && version.FolderName != folderName)
            {
                // Radarr moved the film to another folder: the version follows, in the database only.
                version.FolderName = folderName;
                settingsChanged = true;
            }
        }
        else
        {
            version = NewVersion(context.VersionName, profileId, root.Id, folderName, monitored, availability, tags, isDefault: movie.Versions.All(v => !v.IsDefault));
            movie.Versions.Add(version);
            newVersion = true;
        }

        // Radarr's history of this pass starts from the version's file as it was.
        var hadFile = version.File is not null;
        var previousQuality = version.File is { } before ? QualityName(before.QualityId) : null;

        // Reported once the film is saved, so the report can link to it.
        var notes = new List<string>();
        if (file is not null && !context.SkipFiles && (version.File is null || context.Follow))
        {
            var relative = RelativePathOf(file, item);
            if (relative is null)
            {
                if (version.File is null)
                {
                    progress.FileMissing();
                    notes.Add("Radarr lists a file without a path.");
                }
            }
            else if (version.File is not { } current || !SameFile(current.RelativePath, current.Size, current.QualityId, relative, file, context.QualityIds))
            {
                var full = Path.Combine(context.RootPaths.GetValueOrDefault(version.RootFolderId) ?? root.Path, version.FolderName, relative);
                if (!File.Exists(full))
                {
                    progress.FileMissing();
                    notes.Add($"File not found: {full}");
                }
                else if (version.File is { } renamed && renamed.Size == file.Size && renamed.QualityId == QualityIdOf(file, full, context.QualityIds))
                {
                    renamed.RelativePath = relative;
                    progress.FileRenamed();
                }
                else
                {
                    if (version.File is { } replaced)
                    {
                        // Radarr upgraded or replaced it: a new record, the journal keeps pointing at the old one's lines.
                        db.MediaFiles.Remove(replaced);
                        progress.FileReplaced();
                    }
                    else
                    {
                        progress.FileAdded();
                    }

                    version.File = BuildFile(file, relative, full, context, now);
                }
            }
        }
        else if (context.Follow && file is null && version.File is { } gone)
        {
            // Radarr has no file any more: the record goes. The disk is Radarr's, never touched from here.
            db.MediaFiles.Remove(gone);
            version.File = null;
            progress.FileRemoved();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Importing a film is a deliberate act: it lifts a previous exclusion, like adding it by hand.
        await db.MovieExclusions.Where(e => e.TmdbId == item.TmdbId).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);

        // Ids exist now: the history lines can point at the film, the version and the file.
        var historyAdded = context.Request.ImportHistory ? await AddHistoryAsync(item, movie, version, context, context.Follow && hadFile, context.Follow ? previousQuality : null, ct) : 0;
        var blocklistAdded = context.Request.ImportBlocklist ? await AddBlocklistAsync(item, movie, version, context, ct) : 0;

        var movieRef = MovieRef.From(movie);
        var versionRef = VersionRef.From(version);
        if (newMovie)
        {
            outbox.Publish(new MovieAdded(movieRef, versionRef));
            progress.MovieAdded();
            progress.VersionAdded();
        }
        else if (newVersion)
        {
            outbox.Publish(new VersionAdded(movieRef, versionRef));
            progress.VersionAdded();
        }
        else if (!context.Follow || settingsChanged)
        {
            // Following a Radarr, a new file or a journal line is no settings change: Radarr already told whoever listens.
            outbox.Publish(new VersionUpdated(movieRef, versionRef));
            progress.VersionUpdated();
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        progress.HistoryAdded(historyAdded);
        progress.BlocklistAdded(blocklistAdded);
        foreach (var note in notes)
        {
            progress.Issue(item.Title, item.TmdbId, note, movie.Id);
        }
    }

    /// <summary>
    /// Radarr's custom formats, then every mapped profile: the scores of the formats and their thresholds, and the qualities
    /// (which are allowed, their order and groups, the cutoff, upgrades). A format is matched on its name and takes Radarr's
    /// definition. When several Radarr profiles map to one Bigarr profile, the one holding the most films gives its settings.
    /// </summary>
    private async Task ImportProfilesAsync(Snapshot snapshot, RadarrImportRequest request, RadarrImportProgress progress, CancellationToken ct)
    {
        var byName = request.ImportCustomFormats
            ? await ImportCustomFormatsAsync(snapshot, progress, ct)
            : new Dictionary<string, CustomFormat>(StringComparer.OrdinalIgnoreCase);
        var qualityIds = await db.QualityDefinitions.AsNoTracking().Select(q => q.Id).ToListAsync(ct);
        var films = snapshot.Movies.GroupBy(m => m.QualityProfileId).ToDictionary(g => g.Key, g => g.Count());
        foreach (var group in (request.ProfileMap ?? []).GroupBy(kv => kv.Value))
        {
            var sources = Sources(group.Select(kv => kv.Key), snapshot.Profiles, films);
            if (sources.Count == 0 || await db.QualityProfiles.Include(p => p.FormatScores).FirstOrDefaultAsync(p => p.Id == group.Key && p.Kind == ProfileKind.Movies, ct) is not { } profile)
            {
                continue;
            }

            if (request.ImportCustomFormats && sources.FirstOrDefault(p => p.FormatItems is not null) is { } scored)
            {
                ApplyScores(profile, scored, byName);
                progress.ProfileScored();
                var differing = sources.Where(other => other != scored && other.FormatItems is not null && ScoreKey(other) != ScoreKey(scored)).Select(other => $"'{other.Name}'").ToList();
                if (differing.Count > 0)
                {
                    progress.Issue(profile.Name, null, $"Radarr profiles '{scored.Name}' and {string.Join(", ", differing)} all map to '{profile.Name}' with different scores: '{scored.Name}' holds the most films, its scores were kept.");
                }
            }

            if (request.ImportProfileQualities && sources.FirstOrDefault(p => p.Items is not null) is { } shaped)
            {
                if (RadarrProfileQualities.Translate(shaped, qualityIds) is { } qualities)
                {
                    RadarrProfileQualities.Apply(profile, qualities);
                    progress.ProfileQualitiesCopied();
                }
                else
                {
                    progress.Issue(profile.Name, null, $"The qualities of Radarr profile '{shaped.Name}' could not be copied (nothing allowed, or a cutoff that is not allowed): '{profile.Name}' keeps its own.");
                }

                var differing = sources.Where(other => other != shaped && other.Items is not null && QualityKey(other) != QualityKey(shaped)).Select(other => $"'{other.Name}'").ToList();
                if (differing.Count > 0)
                {
                    progress.Issue(profile.Name, null, $"Radarr profiles '{shaped.Name}' and {string.Join(", ", differing)} all map to '{profile.Name}' with different qualities: '{shaped.Name}' holds the most films, its qualities were kept.");
                }
            }
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Shadow mode, on every pass of the follow: each Films profile a followed Radarr's films map to (a Both profile keeps its own) takes that Radarr
    /// profile's qualities, cutoff, score thresholds and format scores (formats matched by name; none is created here), so
    /// Bigarr judges with Radarr's settings and the comparison stays fair. Returns the names of the profiles that changed.
    /// </summary>
    public async Task<List<string>> AlignProfilesAsync(RadarrImportRequest request, RadarrFollowData data, Dictionary<int, int>? filmsPerProfile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(data);

        var formats = (await db.CustomFormats.ToListAsync(ct))
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var qualityIds = await db.QualityDefinitions.AsNoTracking().Select(q => q.Id).ToListAsync(ct);
        var films = filmsPerProfile ?? FilmsPerProfile(data.Movies);
        var changed = new List<string>();
        foreach (var group in (request.ProfileMap ?? []).GroupBy(kv => kv.Value))
        {
            var sources = Sources(group.Select(kv => kv.Key), data.Profiles, films);
            if (sources.Count == 0 || await db.QualityProfiles.Include(p => p.FormatScores).FirstOrDefaultAsync(p => p.Id == group.Key && p.Kind == ProfileKind.Movies, ct) is not { } profile)
            {
                continue;
            }

            var before = ProfileKey(profile);
            if (sources.FirstOrDefault(p => p.Items is not null) is { } shaped && RadarrProfileQualities.Translate(shaped, qualityIds) is { } qualities)
            {
                RadarrProfileQualities.Apply(profile, qualities);
            }

            if (sources.FirstOrDefault(p => p.FormatItems is not null) is { } scored)
            {
                ApplyScores(profile, scored, formats);
            }

            if (ProfileKey(profile) != before)
            {
                changed.Add(profile.Name);
            }
        }

        if (changed.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Profiles aligned with Radarr: {Profiles}", string.Join(", ", changed));
        }

        db.ChangeTracker.Clear();
        return changed;
    }

    /// <summary>How many films each Radarr profile holds, which tells the profile of most films apart when several map to one.</summary>
    public static Dictionary<int, int> FilmsPerProfile(IEnumerable<RadarrMovie> movies) =>
        movies.GroupBy(m => m.QualityProfileId).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>The Radarr profiles mapped to one Bigarr profile, the one holding the most films first.</summary>
    internal static List<RadarrProfile> Sources(IEnumerable<int> radarrProfileIds, List<RadarrProfile> profiles, Dictionary<int, int> films) =>
        radarrProfileIds
            .Select(id => profiles.FirstOrDefault(p => p.Id == id))
            .OfType<RadarrProfile>()
            .OrderByDescending(p => films.GetValueOrDefault(p.Id))
            .ThenBy(p => p.Id)
            .ToList();

    /// <summary>The format scores of a Radarr profile (formats matched by name) and its score thresholds.</summary>
    internal static void ApplyScores(QualityProfile profile, RadarrProfile source, IReadOnlyDictionary<string, CustomFormat> formatsByName)
    {
        foreach (var item in source.FormatItems ?? [])
        {
            if (item.Name is null || !formatsByName.TryGetValue(item.Name, out var format))
            {
                continue;
            }

            var entry = profile.FormatScores.FirstOrDefault(s => s.CustomFormatId == format.Id);
            if (entry is not null)
            {
                entry.Score = item.Score;
            }
            else if (item.Score != 0)
            {
                profile.FormatScores.Add(new ProfileFormatScore { CustomFormatId = format.Id, Score = item.Score });
            }
        }

        profile.MinFormatScore = source.MinFormatScore ?? profile.MinFormatScore;
        profile.CutoffFormatScore = source.CutoffFormatScore ?? profile.CutoffFormatScore;
        profile.MinUpgradeFormatScore = source.MinUpgradeFormatScore ?? profile.MinUpgradeFormatScore;
    }

    /// <summary>Everything the follow may change on a profile, as one comparable string.</summary>
    internal static string ProfileKey(QualityProfile profile) =>
        JsonSerializer.Serialize(new
        {
            profile.Items,
            profile.Cutoff,
            profile.UpgradeAllowed,
            profile.MinFormatScore,
            profile.CutoffFormatScore,
            profile.MinUpgradeFormatScore,
            Scores = profile.FormatScores.Where(s => s.Score != 0).OrderBy(s => s.CustomFormatId).Select(s => $"{s.CustomFormatId}={s.Score}"),
        });

    /// <summary>A Radarr profile's qualities as one comparable string.</summary>
    internal static string QualityKey(RadarrProfile profile) =>
        JsonSerializer.Serialize(new { profile.Items, profile.Cutoff, profile.UpgradeAllowed });

    /// <summary>Radarr's custom formats, matched on their name: Radarr's definition wins. Returns them by name.</summary>
    private async Task<Dictionary<string, CustomFormat>> ImportCustomFormatsAsync(Snapshot snapshot, RadarrImportProgress progress, CancellationToken ct)
    {
        var existing = await db.CustomFormats.ToListAsync(ct);
        var byName = new Dictionary<string, CustomFormat>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in snapshot.CustomFormats.EnumerateArray())
        {
            CustomFormatDefinition definition;
            try
            {
                definition = CustomFormatJson.Parse(element, 0);
            }
            catch (CustomFormatParseException ex)
            {
                progress.Issue(FormatName(element), null, $"Custom format not imported: {ex.Message}");
                continue;
            }

            if (definition.Name.Length > CustomFormatService.MaxNameLength)
            {
                progress.Issue(definition.Name, null, $"Custom format not imported: the name is longer than {CustomFormatService.MaxNameLength} characters.");
                continue;
            }

            var format = existing.FirstOrDefault(f => string.Equals(f.Name, definition.Name, StringComparison.OrdinalIgnoreCase));
            if (format is null)
            {
                format = new CustomFormat { Name = definition.Name, SpecificationsJson = string.Empty };
                db.CustomFormats.Add(format);
                existing.Add(format);
                progress.CustomFormatAdded();
            }
            else
            {
                progress.CustomFormatUpdated();
            }

            format.Name = definition.Name;
            format.IncludeWhenRenaming = definition.IncludeWhenRenaming;
            format.SpecificationsJson = CustomFormatJson.SerializeSpecifications(definition.Specifications);
            byName[definition.Name] = format;
        }

        await db.SaveChangesAsync(ct);
        return byName;
    }

    /// <summary>A profile's non-zero scores and thresholds as one comparable string.</summary>
    internal static string ScoreKey(RadarrProfile profile) =>
        string.Join(";", (profile.FormatItems ?? []).Where(i => i.Score != 0).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Select(i => $"{i.Name}={i.Score}"))
        + $"|{profile.MinFormatScore}|{profile.CutoffFormatScore}|{profile.MinUpgradeFormatScore}";

    private static string FormatName(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? "?" : "?";

    private async Task<int?> MovieIdAsync(int tmdbId, CancellationToken ct) =>
        await db.Movies.AsNoTracking().Where(m => m.TmdbId == tmdbId).Select(m => (int?)m.Id).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Radarr's lines about one film, as journal lines of the version it became. A line already copied by an earlier run
    /// (same type, date and release) is not copied twice, so the import can be re-run.
    /// </summary>
    private async Task<int> AddHistoryAsync(RadarrMovie item, Movie movie, MovieVersion version, ImportContext context, bool hadFile, string? previousQuality, CancellationToken ct)
    {
        var records = context.Snapshot.History[item.Id].OrderBy(r => r.Date).ThenBy(r => r.Id).ToList();
        if (records.Count == 0)
        {
            return 0;
        }

        var lines = await db.History.AsNoTracking()
            .Where(h => h.TargetId == version.Id)
            .Select(h => new { h.EventType, h.CreatedAt, h.SourceTitle, h.Data })
            .ToListAsync(ct);
        var known = lines.Select(h => (h.EventType, h.CreatedAt.Ticks, h.SourceTitle ?? string.Empty)).ToHashSet();
        // The follow reads a little before its checkpoint: a line copied by the previous pass is known by its Radarr id,
        // even when the upgrade rule now types it differently.
        var knownIds = lines.Select(h => h.Data.GetValueOrDefault(RadarrHistoryIdKey)).OfType<string>().ToHashSet(StringComparer.Ordinal);

        var fileId = item.HasFile && item.MovieFile is { } radarrFile ? radarrFile.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        var added = 0;
        foreach (var line in HistoryLines(records, hadFile, previousQuality))
        {
            var sourceTitle = Truncate(line.Record.SourceTitle, 1024);
            if (knownIds.Contains(line.Data[RadarrHistoryIdKey]) || !known.Add((line.Type, line.Date.Ticks, sourceTitle ?? string.Empty)))
            {
                continue;
            }

            var indexerName = Truncate(line.Data.GetValueOrDefault("indexer"), 128);
            var linksFile = line.Type is HistoryEventType.Imported or HistoryEventType.Upgraded && fileId is not null && version.File is not null && line.Record.Data?.Any(kv => string.Equals(kv.Key, "fileId", StringComparison.OrdinalIgnoreCase) && kv.Value == fileId) == true;
            db.History.Add(new HistoryEvent
            {
                TargetId = version.Id,
                MovieId = movie.Id,
                TmdbId = movie.TmdbId,
                MovieTitle = movie.Title,
                VersionName = version.Name,
                EventType = line.Type,
                SourceTitle = sourceTitle,
                IndexerId = indexerName is not null && context.Indexers.TryGetValue(indexerName, out var indexerId) ? indexerId : null,
                IndexerName = indexerName,
                MediaFileId = linksFile ? version.File!.Id : null,
                Data = line.Data,
                CreatedAt = line.Date,
            });
            added++;
        }

        return added;
    }

    /// <summary>
    /// Radarr's blocklist of one film, as entries of the version it became (its film's default version for a one-shot,
    /// like any blocklisting). Radarr gives no guid or info hash, so the entries match on the release title. A title the
    /// version already blocklists is not added twice, so the import can be re-run. No journal line: the copied Radarr
    /// history already says what happened.
    /// </summary>
    private async Task<int> AddBlocklistAsync(RadarrMovie item, Movie movie, MovieVersion version, ImportContext context, CancellationToken ct)
    {
        var records = context.Snapshot.Blocklist[item.Id].OrderBy(r => r.Date).ThenBy(r => r.Id).ToList();
        if (records.Count == 0)
        {
            return 0;
        }

        var owner = version.Mode == VersionMode.OneShot ? movie.Versions.FirstOrDefault(v => v.IsDefault) ?? version : version;
        var known = (await db.Blocklist.AsNoTracking().Where(b => b.TargetId == owner.Id).Select(b => b.SourceTitle).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var record in records)
        {
            var sourceTitle = Truncate(record.SourceTitle, 1024);
            if (sourceTitle is null || !known.Add(sourceTitle))
            {
                continue;
            }

            var indexerName = Truncate(record.Indexer, 128);
            db.Blocklist.Add(new BlocklistEntry
            {
                TargetId = owner.Id,
                SourceTitle = sourceTitle,
                IndexerId = indexerName is not null && context.Indexers.TryGetValue(indexerName, out var indexerId) ? indexerId : null,
                IndexerName = indexerName,
                Reason = Truncate(record.Message, 1024),
                CreatedAt = Utc(record.Date),
            });
            added++;
        }

        return added;
    }

    /// <summary>Radarr's id of a copied history line, so a line is never copied twice.</summary>
    public const string RadarrHistoryIdKey = "radarrHistoryId";

    /// <summary>A Radarr history line translated: its Bigarr type, UTC date and the data keys the history page reads.</summary>
    public sealed record HistoryLine(RadarrHistoryItem Record, HistoryEventType Type, DateTime Date, Dictionary<string, string> Data);

    /// <summary>
    /// Radarr's lines of one film, oldest first, in Bigarr's terms. Radarr has no upgrade event: an import that follows an
    /// earlier import, with no deletion in between other than the upgrade's own, becomes <see cref="HistoryEventType.Upgraded"/>.
    /// Events Bigarr has no counterpart for (<c>downloadIgnored</c>, <c>unknown</c>) are left out. A partial history (the
    /// follow's lines since its checkpoint) starts from the file the version had: <paramref name="hadFile"/>.
    /// </summary>
    public static IEnumerable<HistoryLine> HistoryLines(IEnumerable<RadarrHistoryItem> records, bool hadFile = false, string? fileQuality = null)
    {
        ArgumentNullException.ThrowIfNull(records);

        string? currentQuality = fileQuality;
        var hasFile = hadFile;
        foreach (var record in records)
        {
            if (HistoryType(record.EventType) is not { } type)
            {
                continue;
            }

            var raw = new Dictionary<string, string?>(record.Data ?? [], StringComparer.OrdinalIgnoreCase);
            string? Raw(string key) => raw.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

            var data = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["importedFrom"] = "radarr",
                [RadarrHistoryIdKey] = record.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            void Put(string key, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    data[key] = value.Trim();
                }
            }

            var quality = record.Quality?.Quality?.Name;
            Put("quality", quality is "Unknown" ? null : quality);
            Put("indexer", Raw("indexer"));
            switch (type)
            {
                case HistoryEventType.Grabbed:
                    Put("downloadClient", Raw("downloadClientName") ?? Raw("downloadClient"));
                    Put("downloadId", record.DownloadId);
                    Put("size", Raw("size"));
                    Put("releaseGuid", Raw("guid"));
                    Put("releaseGroup", Raw("releaseGroup"));
                    Put("protocol", Raw("protocol"));
                    Put("indexerFlags", Raw("indexerFlags") is { } flags && flags != "0" ? flags : null);
                    break;
                case HistoryEventType.Imported:
                    Put("path", Raw("importedPath"));
                    Put("source", Raw("droppedPath"));
                    Put("size", Raw("size"));
                    Put("downloadClient", Raw("downloadClientName") ?? Raw("downloadClient"));
                    Put("downloadId", record.DownloadId);
                    if (hasFile)
                    {
                        type = HistoryEventType.Upgraded;
                        Put("previousQuality", currentQuality);
                    }

                    hasFile = true;
                    currentQuality = data.GetValueOrDefault("quality");
                    break;
                case HistoryEventType.DownloadFailed:
                    Put("reason", Raw("message"));
                    Put("downloadClient", Raw("downloadClientName") ?? Raw("downloadClient"));
                    Put("downloadId", record.DownloadId);
                    break;
                case HistoryEventType.FileDeleted:
                    Put("reason", Raw("reason"));
                    Put("path", record.SourceTitle);
                    Put("size", Raw("size"));
                    if (!string.Equals(Raw("reason"), "upgrade", StringComparison.OrdinalIgnoreCase))
                    {
                        hasFile = false;
                        currentQuality = null;
                    }

                    break;
                case HistoryEventType.Renamed:
                    Put("from", Raw("sourcePath"));
                    Put("to", Raw("path"));
                    break;
            }

            if (record.CustomFormats is { Count: > 0 } formats)
            {
                Put("customFormats", string.Join(", ", formats.Select(f => f.Name).Where(n => !string.IsNullOrWhiteSpace(n))));
            }

            yield return new HistoryLine(record, type, Utc(record.Date), data);
        }
    }

    public static HistoryEventType? HistoryType(string? radarrEventType) => radarrEventType?.ToLowerInvariant() switch
    {
        "grabbed" => HistoryEventType.Grabbed,
        "downloadfolderimported" or "moviefolderimported" => HistoryEventType.Imported,
        "downloadfailed" => HistoryEventType.DownloadFailed,
        "moviefiledeleted" => HistoryEventType.FileDeleted,
        "moviefilerenamed" => HistoryEventType.Renamed,
        _ => null,
    };

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string? Truncate(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];

    private async Task ImportExclusionsAsync(Snapshot snapshot, RadarrImportProgress progress, CancellationToken ct)
    {
        if (snapshot.Exclusions.Count == 0)
        {
            return;
        }

        var imported = snapshot.Movies.Select(m => m.TmdbId).ToHashSet();
        var candidates = snapshot.Exclusions.Where(e => e.TmdbId > 0 && !imported.Contains(e.TmdbId)).DistinctBy(e => e.TmdbId).ToList();
        var ids = candidates.Select(e => e.TmdbId).ToList();
        var inLibrary = (await db.Movies.Where(m => ids.Contains(m.TmdbId)).Select(m => m.TmdbId).ToListAsync(ct)).ToHashSet();
        var excluded = (await db.MovieExclusions.Where(e => ids.Contains(e.TmdbId)).Select(e => e.TmdbId).ToListAsync(ct)).ToHashSet();
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var exclusion in candidates.Where(e => !inLibrary.Contains(e.TmdbId) && !excluded.Contains(e.TmdbId)))
        {
            db.MovieExclusions.Add(new MovieExclusion { TmdbId = exclusion.TmdbId, Title = exclusion.MovieTitle ?? $"TMDB {exclusion.TmdbId}", Year = exclusion.MovieYear, CreatedAt = now });
            progress.ExclusionAdded();
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private async Task<ImportContext> ContextAsync(RadarrImportRequest request, Snapshot snapshot, RadarrImportProgress progress, CancellationToken ct)
    {
        var versionName = request.VersionName.Trim();
        var profileMap = request.ProfileMap ?? [];
        var knownProfiles = (await db.QualityProfiles.AsNoTracking().Select(p => p.Id).ToListAsync(ct)).ToHashSet();
        var profiles = profileMap.Where(kv => knownProfiles.Contains(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value);

        var rootRows = await db.RootFolders.AsNoTracking().ToListAsync(ct);
        var roots = new Dictionary<string, RootFolder>(StringComparer.Ordinal);
        foreach (var (radarrPath, rootId) in request.RootFolderMap ?? [])
        {
            if (rootRows.FirstOrDefault(r => r.Id == rootId) is { } root)
            {
                roots[NormalizePath(radarrPath)] = root;
            }
        }

        var qualityIds = (await db.QualityDefinitions.AsNoTracking().Select(q => q.Id).ToListAsync(ct)).ToHashSet();

        // Radarr names the indexer of a grab; the same indexer synced from Prowlarr carries the same name here.
        var indexers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var indexer in await db.Indexers.AsNoTracking().Select(i => new { i.Id, i.Name }).ToListAsync(ct))
        {
            indexers.TryAdd(indexer.Name, indexer.Id);
        }

        var tagMap = new Dictionary<int, int>();
        if (request.ImportTags)
        {
            var used = snapshot.Movies.SelectMany(m => m.Tags ?? []).ToHashSet();
            foreach (var radarrTag in snapshot.Tags.Where(t => used.Contains(t.Id)))
            {
                var label = TagService.NormalizeLabel(radarrTag.Label);
                if (label.Length == 0 || label.Length > TagService.MaxLabelLength)
                {
                    continue;
                }

                var tag = await db.Tags.FirstOrDefaultAsync(t => t.Label == label, ct);
                if (tag is null)
                {
                    tag = new Tag { Label = label };
                    db.Tags.Add(tag);
                    await db.SaveChangesAsync(ct);
                    progress.TagAdded();
                }

                tagMap[radarrTag.Id] = tag.Id;
            }

            db.ChangeTracker.Clear();
        }

        return new ImportContext(request, snapshot, versionName, profiles, roots, tagMap, qualityIds, indexers, rootRows.ToDictionary(r => r.Id, r => r.Path));
    }

    private async Task<List<Tag>> TagsForAsync(List<int>? radarrTagIds, ImportContext context, CancellationToken ct)
    {
        var ids = (radarrTagIds ?? []).Select(id => context.Tags.TryGetValue(id, out var tagId) ? tagId : 0).Where(id => id > 0).Distinct().ToList();
        return ids.Count == 0 ? [] : await db.Tags.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
    }

    private async Task<Snapshot> LoadAsync(RadarrConnection connection, bool withHistory, bool withBlocklist, CancellationToken ct)
    {
        var status = await radarr.StatusAsync(connection, ct);
        var profiles = await radarr.ProfilesAsync(connection, ct);
        var roots = await radarr.RootFoldersAsync(connection, ct);
        var tags = await radarr.TagsAsync(connection, ct);
        var movies = await radarr.MoviesAsync(connection, ct);
        var exclusions = await radarr.ExclusionsAsync(connection, ct);
        var customFormats = await radarr.CustomFormatsAsync(connection, ct);
        var history = withHistory ? await radarr.HistoryAsync(connection, ct) : [];
        var blocklist = withBlocklist ? await radarr.BlocklistAsync(connection, ct) : [];
        return new Snapshot(status, profiles, roots, tags, movies.Where(m => m.TmdbId > 0 && !string.IsNullOrWhiteSpace(m.Title)).ToList(), exclusions, customFormats, history.ToLookup(h => h.MovieId), history.Count, blocklist.ToLookup(b => b.MovieId), blocklist.Count);
    }

    private async Task<MovieMetadata?> FetchAsync(int tmdbId, CancellationToken ct)
    {
        try
        {
            return await metadata.GetMovieAsync(tmdbId, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Metadata provider failed for TMDB {TmdbId} during the Radarr import", tmdbId);
            return null;
        }
    }

    private static MovieVersion NewVersion(string name, int profileId, int rootFolderId, string folderName, bool monitored, MinimumAvailability availability, List<Tag> tags, bool isDefault) => new()
    {
        Name = name,
        Mode = VersionMode.Managed,
        QualityProfileId = profileId,
        RootFolderId = rootFolderId,
        FolderName = folderName,
        IsDefault = isDefault,
        Monitored = monitored,
        MinimumAvailability = availability,
        Tags = tags,
    };

    private static MediaFile BuildFile(RadarrMovieFile file, string relativePath, string fullPath, ImportContext context, DateTime now)
    {
        long size;
        try
        {
            size = new FileInfo(fullPath).Length;
        }
        catch (IOException)
        {
            size = file.Size;
        }

        return new MediaFile
        {
            RelativePath = relativePath,
            Size = size > 0 ? size : file.Size,
            DateAdded = file.DateAdded ?? now,
            QualityId = QualityIdOf(file, fullPath, context.QualityIds),
            Revision = new Revision
            {
                Version = file.Quality?.Revision?.Version ?? 1,
                Real = file.Quality?.Revision?.Real ?? 0,
                IsRepack = file.Quality?.Revision?.IsRepack ?? false,
            },
            Edition = NullIfBlank(file.Edition),
            ReleaseGroup = NullIfBlank(file.ReleaseGroup),
            Languages = (file.Languages ?? []).Select(l => l.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Cast<string>().Distinct().ToList(),
            IndexerFlags = (IndexerFlags)file.IndexerFlags,
            OriginalReleaseTitle = NullIfBlank(file.SceneName) ?? Path.GetFileNameWithoutExtension(file.OriginalFilePath ?? relativePath),
        };
    }

    /// <summary>Radarr's quality ids are the vendored parser's; an id Bigarr does not know falls back to what the file name says.</summary>
    private static int QualityIdOf(RadarrMovieFile file, string fullPath, HashSet<int> knownIds)
    {
        if (file.Quality?.Quality is { } quality && knownIds.Contains(quality.Id))
        {
            return quality.Id;
        }

        try
        {
            var parsed = Parser.ParseMoviePath(fullPath)?.Quality?.Quality;
            if (parsed is not null && knownIds.Contains(parsed.Id))
            {
                return parsed.Id;
            }
        }
        catch (Exception)
        {
            // an unparsable name is not an error
        }

        return knownIds.Contains(0) ? 0 : knownIds.Min();
    }

    /// <summary>Radarr's movie resource carries what Bigarr caches about a film; images point at TMDB like ours do.</summary>
    public static MovieMetadata? Metadata(RadarrMovie item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (string.IsNullOrWhiteSpace(item.Title))
        {
            return null;
        }

        MovieRatings? ratings = null;
        if (item.Ratings?.Tmdb is { Votes: > 0 } tmdb || item.Ratings?.Imdb is { Votes: > 0 })
        {
            ratings = new MovieRatings
            {
                Tmdb = item.Ratings.Tmdb is { Votes: > 0 } t ? new RatingValue(t.Value, t.Votes) : null,
                Imdb = item.Ratings.Imdb is { Votes: > 0 } i ? new RatingValue(i.Value, i.Votes) : null,
            };
        }

        var images = new List<MediaImage>();
        foreach (var image in item.Images ?? [])
        {
            var url = image.RemoteUrl ?? (image.Url is { } u && u.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? u : null);
            if (url is null)
            {
                continue;
            }

            if (string.Equals(image.CoverType, "poster", StringComparison.OrdinalIgnoreCase))
            {
                images.Add(new MediaImage(ImageType.Poster, url));
            }
            else if (string.Equals(image.CoverType, "fanart", StringComparison.OrdinalIgnoreCase))
            {
                images.Add(new MediaImage(ImageType.Fanart, url));
            }
        }

        return new MovieMetadata
        {
            TmdbId = item.TmdbId,
            ImdbId = NullIfBlank(item.ImdbId),
            Title = item.Title.Trim(),
            OriginalTitle = NullIfBlank(item.OriginalTitle),
            Year = item.Year > 0 ? item.Year : null,
            Runtime = item.Runtime > 0 ? item.Runtime : null,
            Overview = NullIfBlank(item.Overview),
            Certification = NullIfBlank(item.Certification),
            OriginalLanguage = IsoCode(item.OriginalLanguage?.Name),
            Genres = (item.Genres ?? []).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().ToList(),
            Ratings = ratings,
            AlternativeTitles = (item.AlternateTitles ?? [])
                .Select(t => t.Title?.Trim())
                .Where(t => !string.IsNullOrEmpty(t) && !string.Equals(t, item.Title.Trim(), StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Select(t => new AlternativeTitle(t!, null))
                .ToList(),
            Status = Status(item.Status),
            InCinemasDate = DateOf(item.InCinemas),
            DigitalReleaseDate = DateOf(item.DigitalRelease),
            PhysicalReleaseDate = DateOf(item.PhysicalRelease),
            CollectionTmdbId = item.Collection is { TmdbId: > 0 } collection ? collection.TmdbId : null,
            Images = images,
        };
    }

    public static MovieStatus Status(string? value) => value?.ToLowerInvariant() switch
    {
        "tba" => MovieStatus.Tba,
        "announced" => MovieStatus.Announced,
        "incinemas" => MovieStatus.InCinemas,
        "deleted" => MovieStatus.Deleted,
        _ => MovieStatus.Released,
    };

    public static MinimumAvailability Availability(string? value) => value?.ToLowerInvariant() switch
    {
        "announced" or "tba" or "predb" => MinimumAvailability.Announced,
        "incinemas" => MinimumAvailability.InCinemas,
        _ => MinimumAvailability.Released,
    };

    /// <summary>Radarr names the language ("English"); Bigarr keeps TMDB's ISO 639-1 code.</summary>
    public static string? IsoCode(string? languageName)
    {
        if (string.IsNullOrWhiteSpace(languageName))
        {
            return null;
        }

        try
        {
            return IsoLanguages.FindByName(languageName)?.TwoLetterCode;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The Bigarr profile a Radarr profile most likely maps to: same name, else the system profile its name hints at.</summary>
    public static int? SuggestProfile(string radarrName, IEnumerable<(int Id, string Name)> profiles)
    {
        var list = profiles.ToList();
        if (list.FirstOrDefault(p => string.Equals(p.Name, radarrName, StringComparison.OrdinalIgnoreCase)) is { Id: > 0 } same)
        {
            return same.Id;
        }

        if (LooksUhd(radarrName) && list.FirstOrDefault(p => p.Name == QualitySeeder.UhdProfileName) is { Id: > 0 } uhd)
        {
            return uhd.Id;
        }

        if (LooksHd(radarrName) && list.FirstOrDefault(p => p.Name == QualitySeeder.HdProfileName) is { Id: > 0 } hd)
        {
            return hd.Id;
        }

        return null;
    }

    private static bool LooksUhd(string name) =>
        name.Contains("2160", StringComparison.OrdinalIgnoreCase) || name.Contains("4k", StringComparison.OrdinalIgnoreCase) || name.Contains("uhd", StringComparison.OrdinalIgnoreCase) || name.Contains("ultra", StringComparison.OrdinalIgnoreCase);

    private static bool LooksHd(string name) =>
        name.Contains("1080", StringComparison.OrdinalIgnoreCase) || name.Contains("720", StringComparison.OrdinalIgnoreCase) || name.Contains("hd", StringComparison.OrdinalIgnoreCase) || name.Contains("any", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Radarr root a movie sits in, as Radarr reports it or as its path's parent; normalized for the mapping.</summary>
    public static string? RootPathOf(RadarrMovie item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!string.IsNullOrWhiteSpace(item.RootFolderPath))
        {
            return NormalizePath(item.RootFolderPath);
        }

        if (string.IsNullOrWhiteSpace(item.Path))
        {
            return null;
        }

        var path = item.Path.TrimEnd(Separators);
        var cut = path.LastIndexOfAny(Separators);
        return cut <= 0 ? null : NormalizePath(path[..cut]);
    }

    /// <summary>Trailing separators dropped; a Windows drive root keeps its backslash.</summary>
    public static string NormalizePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var trimmed = path.Trim();
        var stripped = trimmed.TrimEnd(Separators);
        return stripped.Length == 0 ? "/" : stripped.Length == 2 && stripped[1] == ':' ? stripped + '\\' : stripped;
    }

    public static string? LastSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.TrimEnd(Separators);
        var cut = trimmed.LastIndexOfAny(Separators);
        var segment = cut < 0 ? trimmed : trimmed[(cut + 1)..];
        return segment.Length == 0 ? null : segment;
    }

    /// <summary>The part of an absolute file path below the movie folder, when Radarr gave no relative path.</summary>
    public static string? RelativeTo(string? filePath, string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            var folder = folderPath.TrimEnd(Separators);
            if (filePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && filePath.Length > folder.Length && Separators.Contains(filePath[folder.Length]))
            {
                return filePath[(folder.Length + 1)..];
            }
        }

        return LastSegment(filePath);
    }

    /// <summary>A skipped film may still be in Bigarr (a previous import, the scan): the report then links to it.</summary>
    private async Task SkipAsync(RadarrImportProgress progress, RadarrMovie item, string reason, CancellationToken ct)
    {
        progress.Skipped();
        progress.Issue(item.Title, item.TmdbId, reason, await MovieIdAsync(item.TmdbId, ct));
    }

    private static DateOnly? DateOf(DateTime? value) => value is { } d ? DateOnly.FromDateTime(d) : null;

    private static readonly JsonElement EmptyFormats = JsonDocument.Parse("[]").RootElement.Clone();

    /// <summary>The parser's name of a quality id, as Radarr's history names it.</summary>
    internal static string? QualityName(int qualityId)
    {
        try
        {
            var name = Bigarr.Parsing.Qualities.Quality.FindById(qualityId)?.Name;
            return string.IsNullOrEmpty(name) || name == "Unknown" ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static RadarrConnection? Connection(string? url, string? apiKey) =>
        string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey) ? null : new RadarrConnection(url.Trim(), apiKey.Trim());

    /// <param name="CustomFormats">Radarr's custom formats, a JSON array in Radarr's API shape.</param>
    /// <param name="History">Radarr's history by Radarr movie id; empty when the import does not copy it.</param>
    /// <param name="Blocklist">Radarr's blocklist by Radarr movie id; empty when the import does not copy it.</param>
    private sealed record Snapshot(RadarrSystemStatus Status, List<RadarrProfile> Profiles, List<RadarrRootFolder> RootFolders, List<RadarrTag> Tags, List<RadarrMovie> Movies, List<RadarrExclusion> Exclusions, JsonElement CustomFormats, ILookup<int, RadarrHistoryItem> History, int HistoryCount, ILookup<int, RadarrBlocklistItem> Blocklist, int BlocklistCount);

    /// <param name="Profiles">Radarr profile id → Bigarr profile id.</param>
    /// <param name="Roots">Normalized Radarr root path → Bigarr root folder.</param>
    /// <param name="Tags">Radarr tag id → Bigarr tag id.</param>
    /// <param name="Indexers">Bigarr indexer name → id, to link a grab or a blocklisted release to the indexer that still exists.</param>
    /// <param name="RootPaths">Bigarr root folder id → path: an existing version's file lives under its own root.</param>
    /// <param name="Follow">A pass of the follow rather than the import: files and folders follow Radarr's.</param>
    /// <param name="SkipFiles">The follow's first step: films come in without their file, which the next step brings.</param>
    private sealed record ImportContext(RadarrImportRequest Request, Snapshot Snapshot, string VersionName, Dictionary<int, int> Profiles, Dictionary<string, RootFolder> Roots, Dictionary<int, int> Tags, HashSet<int> QualityIds, Dictionary<string, int> Indexers, Dictionary<int, string> RootPaths, bool Follow = false, bool SkipFiles = false);
}
