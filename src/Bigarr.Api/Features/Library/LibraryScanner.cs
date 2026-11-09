using System.Text.RegularExpressions;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Library;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Movies;
using Bigarr.Parsing;
using Bigarr.Parsing.Model;
using Bigarr.Parsing.Qualities;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Library;

/// <summary>
/// Read-only scan of an existing library (blueprint §5): one first-level folder = one film, one video file = one version
/// on the system profile <c>Any</c>, unmonitored, folder name observed on disk. Never moves or renames anything.
/// </summary>
public sealed partial class LibraryScanner(BigarrDbContext db, IMetadataProvider metadata, TimeProvider time, ILogger<LibraryScanner> logger) : IRootFolderScanner
{
    public MediaKind Kind => MediaKind.Movies;

    public static HashSet<string> VideoExtensions => VideoFiles.Extensions;

    private static readonly HashSet<string> ExtrasFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "extras", "featurettes", "behind the scenes", "deleted scenes", "interviews", "scenes", "shorts", "trailers", "other", "sample", "samples",
    };

    private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "@eadir", "#recycle", "lost+found", ".recycle", "$recycle.bin", "system volume information",
    };

    public async Task ScanAsync(int rootFolderId, ScanProgress progress, CancellationToken ct)
    {
        var root = await db.RootFolders.FindAsync([rootFolderId], ct)
                   ?? throw new InvalidOperationException($"Root folder {rootFolderId} no longer exists.");

        if (root.Kind != MediaKind.Movies)
        {
            throw new InvalidOperationException($"'{root.Path}' is a series root folder; the film scan does not walk it.");
        }

        if (!Directory.Exists(root.Path))
        {
            throw new DirectoryNotFoundException($"Root folder path '{root.Path}' is not accessible.");
        }

        var profiles = await ResolveProfilesAsync(ct);

        var scanStartedAt = time.GetUtcNow().UtcDateTime;
        var folders = Directory.EnumerateDirectories(root.Path)
            .Select(p => new DirectoryInfo(p))
            .Where(d => !d.Name.StartsWith('.') && !IgnoredFolders.Contains(d.Name))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        progress.FoldersTotal = folders.Count;
        logger.LogInformation("Scanning {Root}: {Count} folder(s)", root.Path, folders.Count);

        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();
            progress.CurrentFolder = folder.Name;

            try
            {
                await ScanFolderAsync(root, folder, profiles, progress, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scan of folder {Folder} failed", folder.FullName);
                db.ChangeTracker.Clear();
                await UpsertUnmappedAsync(root, folder.Name, 0, UnmappedReason.MetadataError, ex.Message, ct);
                progress.FolderUnmapped();
            }

            progress.FolderDone();
        }

        // Folders that were unmapped in a previous scan and are gone or mapped now.
        await db.UnmappedFolders
            .Where(u => u.RootFolderId == root.Id && u.LastSeenAt < scanStartedAt)
            .ExecuteDeleteAsync(ct);

        progress.CurrentFolder = null;
    }

    /// <summary>The system profiles a discovered file is filed under: its resolution decides, nothing else is known at scan time.</summary>
    private readonly record struct ScanProfiles(int Hd, int Uhd)
    {
        public int For(int resolution) => resolution >= QualitySeeder.UhdResolution ? Uhd : Hd;
    }

    private async Task<ScanProfiles> ResolveProfilesAsync(CancellationToken ct)
    {
        var profiles = await db.QualityProfiles.OrderBy(p => p.Id).Select(p => new { p.Id, p.Name }).ToListAsync(ct);
        if (profiles.Count == 0)
        {
            throw new InvalidOperationException("No quality profile is seeded; the scan has nothing to file versions under.");
        }

        var hd = profiles.FirstOrDefault(p => p.Name == QualitySeeder.HdProfileName)?.Id ?? profiles[0].Id;
        return new ScanProfiles(hd, profiles.FirstOrDefault(p => p.Name == QualitySeeder.UhdProfileName)?.Id ?? hd);
    }

    private async Task ScanFolderAsync(RootFolder root, DirectoryInfo folder, ScanProfiles profiles, ScanProgress progress, CancellationToken ct)
    {
        var files = EnumerateVideoFiles(folder).ToList();
        if (files.Count == 0)
        {
            await UpsertUnmappedAsync(root, folder.Name, 0, UnmappedReason.NoVideoFiles, null, ct);
            progress.FolderUnmapped();
            return;
        }

        var parsed = TryParse(folder.Name, isDir: true) ?? TryParse(Path.GetFileNameWithoutExtension(files[0].Name), isDir: false);
        if (parsed is null || string.IsNullOrWhiteSpace(parsed.PrimaryMovieTitle))
        {
            await UpsertUnmappedAsync(root, folder.Name, files.Count, UnmappedReason.NotParsable, folder.Name, ct);
            progress.FolderUnmapped();
            return;
        }

        var (found, reason, detail) = await ResolveAsync(parsed, folder.Name, ct);
        if (found is null)
        {
            await UpsertUnmappedAsync(root, folder.Name, files.Count, reason, detail, ct);
            progress.FolderUnmapped();
            return;
        }

        var movie = await db.Movies
            .Include(m => m.Versions).ThenInclude(v => v.File)
            .FirstOrDefaultAsync(m => m.TmdbId == found.TmdbId, ct);

        if (movie is null)
        {
            movie = Movie.FromMetadata(found, root.Id);
            db.Movies.Add(movie);
            progress.MovieAdded();
        }

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(folder.FullName, file.FullName);
            var known = movie.Versions.FirstOrDefault(v =>
                v.RootFolderId == root.Id && v.FolderName == folder.Name && v.File?.RelativePath == relativePath);

            if (known is not null)
            {
                if (known.File!.Size != file.Length)
                {
                    known.File.Size = file.Length;
                }

                continue;
            }

            var parsedFile = TryParsePath(file.FullName);
            var quality = parsedFile?.Quality ?? new QualityModel(Parsing.Qualities.Quality.Unknown);
            var baseName = VersionNaming.FromQuality(quality.Quality.Resolution, quality.Quality.Name);

            // A fileless version created by hand for this very folder adopts the file; otherwise a new version is born.
            var target = movie.Versions.FirstOrDefault(v =>
                v.File is null && v.RootFolderId == root.Id && v.FolderName == folder.Name && v.Name == baseName);

            if (target is null)
            {
                target = new MovieVersion
                {
                    Movie = movie,
                    Name = VersionNaming.Unique(baseName, movie.Versions.Select(v => v.Name)),
                    Mode = VersionMode.Managed,
                    QualityProfileId = profiles.For(quality.Quality.Resolution),
                    RootFolderId = root.Id,
                    FolderName = folder.Name,
                    IsDefault = movie.Versions.All(v => !v.IsDefault),
                    Monitored = false,
                    MinimumAvailability = MinimumAvailability.Released,
                };
                movie.Versions.Add(target);
                progress.VersionAdded();
            }

            target.File = new MediaFile
            {
                RelativePath = relativePath,
                Size = file.Length,
                DateAdded = time.GetUtcNow().UtcDateTime,
                QualityId = quality.Quality.Id,
                Revision = new Bigarr.Domain.Movies.Revision
                {
                    Version = quality.Revision?.Version ?? 1,
                    Real = quality.Revision?.Real ?? 0,
                    IsRepack = quality.Revision?.IsRepack ?? false,
                },
                Edition = string.IsNullOrWhiteSpace(parsedFile?.Edition) ? null : parsedFile!.Edition,
                ReleaseGroup = string.IsNullOrWhiteSpace(parsedFile?.ReleaseGroup) ? null : parsedFile!.ReleaseGroup,
                Languages = parsedFile?.Languages?.Select(l => l.Name).Where(n => n is not null).Cast<string>().ToList() ?? [],
                OriginalReleaseTitle = Path.GetFileNameWithoutExtension(file.Name),
            };
            progress.FileAdded();
        }

        await db.UnmappedFolders
            .Where(u => u.RootFolderId == root.Id && u.RelativePath == folder.Name)
            .ExecuteDeleteAsync(ct);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private async Task<(MovieMetadata? Movie, UnmappedReason Reason, string? Detail)> ResolveAsync(ParsedMovieInfo parsed, string folderName, CancellationToken ct)
    {
        try
        {
            if (parsed.TmdbId > 0)
            {
                var byId = await metadata.GetMovieAsync(parsed.TmdbId, ct);
                return byId is null ? (null, UnmappedReason.NoMatch, $"tmdb:{parsed.TmdbId} unknown") : (byId, default, null);
            }

            if (!string.IsNullOrWhiteSpace(parsed.ImdbId))
            {
                var byImdb = await metadata.GetMovieByImdbIdAsync(parsed.ImdbId, ct);
                return byImdb is null ? (null, UnmappedReason.NoMatch, $"imdb:{parsed.ImdbId} unknown") : (byImdb, default, null);
            }

            var year = parsed.Year > 0 ? parsed.Year : (int?)null;
            var candidates = await metadata.SearchAsync(parsed.PrimaryMovieTitle, year, ct);
            var wanted = parsed.MovieTitles.Select(t => t.CleanMovieTitle()).Where(t => t.Length > 0).ToHashSet();

            // The parser tends to read a number inside the title as a year ("Blade Runner 2049 (2017)" → "Blade Runner").
            // The folder name minus its year and tags is a second opinion on the title.
            var folderTitle = TitleFromFolderName(folderName, year);
            if (folderTitle.Length > 0)
            {
                wanted.Add(folderTitle);
            }

            var matches = candidates.Where(c => TitleMatches(c, wanted) && YearMatches(c, year)).ToList();
            if (matches.Count > 1 && year is not null)
            {
                var exactYear = matches.Where(c => c.Year == year).ToList();
                if (exactYear.Count >= 1)
                {
                    matches = exactYear;
                }
            }

            // A single candidate with the exact year whose title starts with what we parsed is accepted too:
            // "Blade Runner" (2017) → "Blade Runner 2049" (2017).
            if (matches.Count == 0 && year is not null && candidates.Count == 1 && candidates[0].Year == year
                && wanted.Any(w => candidates[0].Title.CleanMovieTitle().StartsWith(w, StringComparison.Ordinal)))
            {
                matches = [candidates[0]];
            }

            return matches.Count switch
            {
                1 => (matches[0], default, null),
                0 => (null, UnmappedReason.NoMatch, $"parsed '{parsed.PrimaryMovieTitle}' ({(year?.ToString() ?? "no year")}), {candidates.Count} candidate(s)"),
                _ => (null, UnmappedReason.AmbiguousMatch, string.Join("; ", matches.Take(5).Select(m => $"{m.Title} ({m.Year}) tmdb:{m.TmdbId}"))),
            };
        }
        catch (HttpRequestException ex)
        {
            return (null, UnmappedReason.MetadataError, ex.Message);
        }
    }

    /// <summary>Folder name without a trailing "(2017)" / "2017", "{tmdb-…}" or "[…]" tags, cleaned like a title.</summary>
    private static string TitleFromFolderName(string folderName, int? year)
    {
        var name = FolderTagsRegex().Replace(folderName, " ").Trim();
        if (year is not null)
        {
            var yearText = year.Value.ToString();
            if (name.EndsWith($"({yearText})", StringComparison.Ordinal))
            {
                name = name[..^(yearText.Length + 2)];
            }
            else if (name.EndsWith(yearText, StringComparison.Ordinal) && name.Length > yearText.Length)
            {
                name = name[..^yearText.Length];
            }
        }

        return name.Trim(' ', '-', '.', '_').CleanMovieTitle();
    }

    [GeneratedRegex(@"\{[^}]*\}|\[[^\]]*\]")]
    private static partial Regex FolderTagsRegex();

    private static bool TitleMatches(MovieMetadata candidate, HashSet<string> wanted)
    {
        IEnumerable<string?> titles = [candidate.Title, candidate.OriginalTitle, .. candidate.AlternativeTitles.Select(a => a.Title)];
        return titles.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.CleanMovieTitle()).Any(wanted.Contains);
    }

    private static bool YearMatches(MovieMetadata candidate, int? year) =>
        year is null || candidate.Year is null || Math.Abs(candidate.Year.Value - year.Value) <= 1;

    private static IEnumerable<FileInfo> EnumerateVideoFiles(DirectoryInfo folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        foreach (var file in folder.EnumerateFiles("*", options))
        {
            if (!VideoExtensions.Contains(file.Extension))
            {
                continue;
            }

            if (file.Name.Contains("sample", StringComparison.OrdinalIgnoreCase) && !folder.Name.Contains("sample", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsUnderExtras(folder, file))
            {
                continue;
            }

            yield return file;
        }
    }

    private static bool IsUnderExtras(DirectoryInfo root, FileInfo file)
    {
        for (var dir = file.Directory; dir is not null && dir.FullName != root.FullName; dir = dir.Parent)
        {
            if (ExtrasFolders.Contains(dir.Name) || dir.Name.StartsWith('.'))
            {
                return true;
            }
        }

        return false;
    }

    private static ParsedMovieInfo? TryParse(string title, bool isDir)
    {
        try
        {
            return Parser.ParseMovieTitle(title, isDir);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ParsedMovieInfo? TryParsePath(string path)
    {
        try
        {
            return Parser.ParseMoviePath(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task UpsertUnmappedAsync(RootFolder root, string relativePath, int fileCount, UnmappedReason reason, string? detail, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var existing = await db.UnmappedFolders.FirstOrDefaultAsync(u => u.RootFolderId == root.Id && u.RelativePath == relativePath, ct);
        if (existing is null)
        {
            db.UnmappedFolders.Add(new UnmappedFolder
            {
                RootFolderId = root.Id,
                RelativePath = relativePath,
                FileCount = fileCount,
                Reason = reason,
                Detail = Truncate(detail),
                DiscoveredAt = now,
                LastSeenAt = now,
            });
        }
        else
        {
            existing.FileCount = fileCount;
            existing.Reason = reason;
            existing.Detail = Truncate(detail);
            existing.LastSeenAt = now;
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private static string? Truncate(string? value) => value is { Length: > 2000 } ? value[..2000] : value;
}
