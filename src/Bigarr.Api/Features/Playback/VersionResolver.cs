using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Playback;

/// <summary>The film and version a stream played, and its file as Bigarr sees it. Any part can be unknown.</summary>
public sealed record ResolvedVersion(int? MovieId, int? VersionId, string? VersionName, string? FilePath);

/// <summary>
/// Which version a played file belongs to, from the surest clue to the weakest:
/// <list type="number">
/// <item>the file itself: the server's path through the path mappings is a version's file;</item>
/// <item>the version chosen when the item was linked to its film by hand;</item>
/// <item>the server's library: its folders hold exactly one of the film's versions (Films → 1080p, Films 4K → 4k);</item>
/// <item>the film's versions: only one has a file, or only one has the played resolution.</item>
/// </list>
/// Otherwise the play keeps the film and no version.
/// </summary>
public sealed class VersionResolver(BigarrDbContext db)
{
    private static readonly char[] Separators = ['/', '\\'];

    public async Task<ResolvedVersion> ResolveAsync(MediaServer server, MediaServerItem item, string? serverFilePath, string? sourceResolution, IReadOnlyList<string> libraryFolders, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(libraryFolders);

        var filePath = serverFilePath is null ? null : server.TranslateFromServer(serverFilePath);
        // A film linked by hand is not overruled by a file of another film: the link is the owner's word.
        if (filePath is not null && await ByPathAsync(filePath, ct) is { } byPath && (!item.LinkedByHand || byPath.MovieId == item.MovieId))
        {
            return byPath;
        }

        if (item.Kind != MediaItemKind.Movie || item.MovieId is not { } movieId)
        {
            return new ResolvedVersion(item.MovieId, null, null, filePath);
        }

        var versions = await db.MovieVersions.AsNoTracking()
            .Where(v => v.MovieId == movieId)
            .Select(v => new Candidate(v.Id, v.Name, v.RootFolder.Path, v.File != null, v.File != null ? v.File.Quality.Resolution : null))
            .ToListAsync(ct);

        if (item.LinkedVersionId is { } linked && versions.FirstOrDefault(v => v.Id == linked) is { } chosenByHand)
        {
            return new ResolvedVersion(movieId, chosenByHand.Id, chosenByHand.Name, filePath);
        }

        var folders = libraryFolders.Select(server.TranslateFromServer).ToList();
        if (folders.Count > 0 && Single(versions.Where(v => folders.Any(f => PlexClient.IsUnder(v.RootPath, f) || PlexClient.IsUnder(f, v.RootPath)))) is { } inLibrary)
        {
            return new ResolvedVersion(movieId, inLibrary.Id, inLibrary.Name, filePath);
        }

        var withFile = versions.Where(v => v.HasFile).ToList();
        var chosen = Single(withFile)
            ?? (ResolutionOf(sourceResolution) is { } resolution ? Single(withFile.Where(v => v.Resolution == resolution)) : null);
        return new ResolvedVersion(movieId, chosen?.Id, chosen?.Name, filePath);
    }

    /// <summary>Plex's <c>videoResolution</c> as the vertical resolution Bigarr's qualities use.</summary>
    public static int? ResolutionOf(string? plexResolution) => plexResolution?.Trim().ToLowerInvariant() switch
    {
        "4k" or "2160" or "2160p" => 2160,
        "1080" or "1080p" => 1080,
        "720" or "720p" => 720,
        "576" or "576p" => 576,
        "480" or "480p" or "sd" => 480,
        _ => null,
    };

    private async Task<ResolvedVersion?> ByPathAsync(string filePath, CancellationToken ct)
    {
        var roots = await db.RootFolders.AsNoTracking().ToListAsync(ct);
        foreach (var root in roots.Where(r => PlexClient.IsUnder(filePath, r.Path)).OrderByDescending(r => r.Path.Length))
        {
            var rest = filePath[root.Path.TrimEnd(Separators).Length..].TrimStart(Separators);
            var cut = rest.IndexOfAny(Separators);
            if (cut <= 0)
            {
                continue;
            }

            var folder = rest[..cut];
            var relative = rest[(cut + 1)..];
            var alternate = relative.Contains('/', StringComparison.Ordinal) ? relative.Replace('/', '\\') : relative.Replace('\\', '/');
            var hit = await db.MovieVersions.AsNoTracking()
                .Where(v => v.RootFolderId == root.Id && v.FolderName == folder && v.File != null && (v.File.RelativePath == relative || v.File.RelativePath == alternate))
                .Select(v => new { v.Id, v.Name, v.MovieId })
                .FirstOrDefaultAsync(ct);
            if (hit is not null)
            {
                return new ResolvedVersion(hit.MovieId, hit.Id, hit.Name, filePath);
            }
        }

        return null;
    }

    private static Candidate? Single(IEnumerable<Candidate> candidates)
    {
        Candidate? only = null;
        foreach (var candidate in candidates)
        {
            if (only is not null)
            {
                return null;
            }

            only = candidate;
        }

        return only;
    }

    private sealed record Candidate(int Id, string Name, string RootPath, bool HasFile, int? Resolution);
}
