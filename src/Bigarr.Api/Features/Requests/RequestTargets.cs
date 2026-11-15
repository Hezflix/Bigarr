using Bigarr.Api.Features.Discover;
using Bigarr.Api.Features.Users;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Requests;

/// <summary>The profile, root folder and version an approved request lands in.</summary>
public sealed record RequestTarget(QualityProfile Profile, RootFolder RootFolder, string VersionName);

/// <summary>
/// Where an approved request goes when the approver leaves the profile and root folder: the defaults of Settings →
/// Requests for its type and slot, else the first profile and root folder of the right kind (for the 4K slot, the first
/// profile whose name says 4K). The version is the title's own for the slot when the library has it; a new one is named
/// after the 4K setting, or, for the standard slot, after the name most of the library's default versions carry (never the 4K name).
/// </summary>
public sealed class RequestTargets(BigarrDbContext db, LibraryAvailability library)
{
    private static readonly string[] FourKWords = ["4k", "2160", "uhd", "ultra"];

    public async Task<(int? ProfileId, int? RootFolderId)> DefaultsAsync(RequestMediaType type, bool fourK, CancellationToken ct)
    {
        var settings = (await UserSettings.ReadAsync(db, ct)).RequestDefaults;
        var (profileId, rootId) = (type, fourK) switch
        {
            (RequestMediaType.Movie, false) => (settings.MovieProfileId, settings.MovieRootFolderId),
            (RequestMediaType.Movie, true) => (settings.MovieFourKProfileId, settings.MovieFourKRootFolderId),
            (_, false) => (settings.SeriesProfileId, settings.SeriesRootFolderId),
            _ => (settings.SeriesFourKProfileId, settings.SeriesFourKRootFolderId),
        };

        var profiles = await ProfilesAsync(type, ct);
        var roots = await RootFoldersAsync(type, ct);
        if (profileId is null || profiles.All(p => p.Id != profileId))
        {
            profileId = (fourK ? profiles.FirstOrDefault(p => FourKWords.Any(w => p.Name.Contains(w, StringComparison.OrdinalIgnoreCase))) : null)?.Id
                ?? (fourK ? null : profiles.FirstOrDefault(p => !FourKWords.Any(w => p.Name.Contains(w, StringComparison.OrdinalIgnoreCase))))?.Id
                ?? profiles.FirstOrDefault()?.Id;
        }

        if (rootId is null || roots.All(r => r.Id != rootId))
        {
            rootId = roots.FirstOrDefault()?.Id;
        }

        return (profileId, rootId);
    }

    public async Task<List<QualityProfile>> ProfilesAsync(RequestMediaType type, CancellationToken ct)
    {
        var all = await db.QualityProfiles.AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct);
        return [.. all.Where(p => type == RequestMediaType.Movie ? p.Kind.ForMovies() : p.Kind.ForSeries())];
    }

    public async Task<List<RootFolder>> RootFoldersAsync(RequestMediaType type, CancellationToken ct)
    {
        var kind = type == RequestMediaType.Movie ? MediaKind.Movies : MediaKind.Series;
        return await db.RootFolders.AsNoTracking().Where(r => r.Kind == kind).OrderBy(r => r.Id).ToListAsync(ct);
    }

    /// <summary>The profile and root folder to use (the approver's, or the defaults), checked against the request's type.</summary>
    public async Task<Result<(QualityProfile Profile, RootFolder Root)>> ResolveAsync(RequestMediaType type, bool fourK, int? profileId, int? rootFolderId, CancellationToken ct)
    {
        var (defaultProfile, defaultRoot) = await DefaultsAsync(type, fourK, ct);
        profileId ??= defaultProfile;
        rootFolderId ??= defaultRoot;
        var what = type == RequestMediaType.Movie ? "film" : "series";
        var profile = profileId is null ? null : (await ProfilesAsync(type, ct)).FirstOrDefault(p => p.Id == profileId);
        if (profile is null)
        {
            return Failure.Invalid("No quality profile", $"No {what} quality profile to add the request with.");
        }

        var root = rootFolderId is null ? null : (await RootFoldersAsync(type, ct)).FirstOrDefault(r => r.Id == rootFolderId);
        if (root is null)
        {
            return Failure.Invalid("No root folder", $"No {what} root folder to add the request in. Add one in Settings → Library.");
        }

        return Result<(QualityProfile, RootFolder)>.Ok((profile, root));
    }

    /// <summary>The name of a new standard version: the one most default versions of this type carry, else the profile's.</summary>
    public async Task<string> StandardVersionNameAsync(RequestMediaType type, QualityProfile profile, CancellationToken ct)
    {
        var fourK = await library.FourKVersionAsync(ct);
        var names = type == RequestMediaType.Movie
            ? await db.MovieVersions.AsNoTracking().Where(v => v.IsDefault && v.Name != fourK).GroupBy(v => v.Name).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct)
            : await db.SeriesVersions.AsNoTracking().Where(v => v.IsDefault && v.Name != fourK).GroupBy(v => v.Name).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var common = names.OrderByDescending(n => n.Count).ThenBy(n => n.Key, StringComparer.Ordinal).Select(n => n.Key).FirstOrDefault();
        var name = common ?? (type == RequestMediaType.Movie ? VersionNaming.FromProfile(profile.Name) : VersionNaming.FromSeriesProfile(profile.Name));

        // A standard version never takes the 4K slot's name (an approver may pick a 4K profile for a standard request).
        return string.Equals(name, fourK, StringComparison.OrdinalIgnoreCase) ? "standard" : name;
    }

    /// <summary>The root folder's own folder name: what a request manager sees instead of the path.</summary>
    public static string FolderName(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}
