using System.Globalization;
using Bigarr.Api.Features.Issues;
using Bigarr.Api.Features.Requests;
using Bigarr.Api.Features.Users;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Auth.External;
using Bigarr.Api.Infrastructure.Metadata;
using Bigarr.Api.Infrastructure.Metadata.Tmdb;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.SeerrImport;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Discover;
using Bigarr.Domain.Issues;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Notifications;
using Bigarr.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Migration;

/// <summary>
/// Brings a Seerr (Overseerr, Jellyseerr) history into Bigarr, once, as history only:
/// <list type="bullet">
/// <item>Seerr's accounts find their Bigarr user by Plex account, else by name, Seerr's owner being Bigarr's admin; the
/// others are created with the role and switches their permission bits map to, their quota and Discord id, and sign in
/// with Plex later. Passwords and tokens never come along.</item>
/// <item>Requests keep their status, requester, approver and dates, the 4K slot and the seasons, and link to the
/// library's title by TMDB id (a series by TVDB id first); a title the library lacks keeps the request unlinked.</item>
/// <item>Issues keep their type, scope, status, reporter and thread; Seerr keeps no file for them.</item>
/// <item>Discover rows Bigarr can show take Seerr's order and on/off state.</item>
/// </list>
/// Nothing is forwarded, searched or notified: rows are written directly, never through the request or issue services,
/// and a request already available is marked so the "available" message does not go out for it. Each row remembers its
/// Seerr id (<see cref="User.SourceRef"/>, …), so a second run adds only what is new.
/// </summary>
public sealed class SeerrImportService(
    BigarrDbContext db,
    TmdbDiscoverClient tmdb,
    RequestStates states,
    IOptions<BigarrOptions> options,
    IOptions<MetadataOptions> metadata,
    TimeProvider time,
    ILogger<SeerrImportService> logger)
{
    public const string RefPrefix = "seerr:";

    /// <summary>Seerr numbers its owner, the account that set it up, 1.</summary>
    public const int OwnerId = 1;

    private const string Placeholder = "TMDB {0}";

    /// <summary>Where an uploaded copy of <c>db.sqlite3</c> goes, in the config directory.</summary>
    public string UploadPath => Path.GetFullPath(Path.Combine(options.Value.ConfigDirectory, "imports", "seerr.sqlite3"));

    public static string UserRef(int id) => $"{RefPrefix}user:{id.ToString(CultureInfo.InvariantCulture)}";

    public static string RequestRef(int id) => $"{RefPrefix}request:{id.ToString(CultureInfo.InvariantCulture)}";

    public static string IssueRef(int id) => $"{RefPrefix}issue:{id.ToString(CultureInfo.InvariantCulture)}";

    public static string CommentRef(int id) => $"{RefPrefix}comment:{id.ToString(CultureInfo.InvariantCulture)}";

    public async Task<Result<SeerrImportPreviewResponse>> PreviewAsync(SeerrImportPreviewRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        SeerrSnapshot snapshot;
        try
        {
            using var source = SeerrDatabase.Open(request.Path);
            snapshot = source.Read();
        }
        catch (SeerrDatabaseException ex)
        {
            return Failure.Invalid("Cannot read the Seerr database", ex.Message);
        }

        var plan = await PlanAsync(snapshot, ct);
        var requests = plan.Snapshot.Requests;
        var issues = plan.Snapshot.Issues;
        var comments = plan.Snapshot.Comments;

        int inLibrary = 0, notInLibrary = 0, already = 0, skipped = 0, overlapping = 0;
        var titles = new Dictionary<(RequestMediaType, int), TitleCount>();
        foreach (var seerr in requests)
        {
            var decision = plan.Request(seerr);
            if (plan.ImportedRequests.Contains(RequestRef(seerr.Id)))
            {
                already++;
                continue;
            }

            if (decision.Skip is not null)
            {
                skipped++;
                continue;
            }

            if (decision.InLibrary)
            {
                inLibrary++;
            }
            else
            {
                notInLibrary++;
                Count(titles, decision.Media!, isRequest: true);
            }

            if (decision.Status is RequestStatus.Pending or RequestStatus.Failed || (decision.Status == RequestStatus.Approved && seerr.Status != 5))
            {
                if (plan.ActiveBigarr.Contains((decision.Type, decision.Media!.TmdbId, seerr.Is4K)))
                {
                    overlapping++;
                }
            }
        }

        int issuesInLibrary = 0, issuesNotInLibrary = 0, issuesAlready = 0, issuesSkipped = 0;
        foreach (var seerr in issues)
        {
            var decision = plan.Issue(seerr);
            if (plan.ImportedIssues.ContainsKey(IssueRef(seerr.Id)))
            {
                issuesAlready++;
                continue;
            }

            if (decision.Skip is not null)
            {
                issuesSkipped++;
                continue;
            }

            if (decision.InLibrary)
            {
                issuesInLibrary++;
            }
            else
            {
                issuesNotInLibrary++;
                Count(titles, decision.Media!, isRequest: false);
            }
        }

        var commentsAlready = comments.Count(c => plan.ImportedComments.Contains(CommentRef(c.Id)));
        var counts = new SeerrRequestCounts(
            requests.Count,
            requests.Count(r => r.Type == "movie"),
            requests.Count(r => r.Type == "tv"),
            requests.Count(r => r.Is4K),
            requests.Count(r => r.Status == 1),
            requests.Count(r => r.Status == 2),
            requests.Count(r => r.Status == 3),
            requests.Count(r => r.Status == 4),
            requests.Count(r => r.Status == 5),
            inLibrary,
            notInLibrary,
            already,
            skipped,
            overlapping);
        var issueCounts = new SeerrIssueCounts(
            issues.Count,
            issues.Count(i => i.Status != 2),
            issues.Count(i => i.Status == 2),
            comments.Count,
            issuesInLibrary,
            issuesNotInLibrary,
            issuesAlready,
            commentsAlready,
            issuesSkipped);

        var users = plan.Users.Values.OrderBy(u => u.Seerr.Id).Select(u => new SeerrUserLine(
            u.Seerr.Id,
            u.Seerr.DisplayName,
            u.Match,
            u.Username,
            u.Role,
            u.Access.Permissions,
            u.DiscordId is not null,
            requests.Count(r => r.RequestedById == u.Seerr.Id),
            issues.Count(i => i.CreatedById == u.Seerr.Id),
            [.. u.Access.Skipped])).ToList();

        var titleLines = titles.Select(t => new SeerrTitleLine(t.Key.Item1, t.Key.Item2, t.Value.TvdbId, t.Value.Requests, t.Value.Issues))
            .OrderByDescending(t => t.Requests + t.Issues).ThenBy(t => t.TmdbId).ToList();

        return Result<SeerrImportPreviewResponse>.Ok(new SeerrImportPreviewResponse(
            plan.Snapshot.Flavor,
            users,
            counts,
            issueCounts,
            titleLines,
            tmdb.Configured ? titleLines.Count : 0,
            [.. plan.Snapshot.Sliders.Select(SliderLine)],
            plan.SkippedLines(),
            NotImported(plan)));
    }

    public Failure? Validate(SeerrImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            using var source = SeerrDatabase.Open(request.Path);
        }
        catch (SeerrDatabaseException ex)
        {
            return Failure.Invalid("Cannot read the Seerr database", ex.Message);
        }

        return null;
    }

    public async Task ImportAsync(SeerrImportProgress progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var request = progress.Request;

        progress.Current = "Reading the Seerr database";
        SeerrSnapshot snapshot;
        using (var source = SeerrDatabase.Open(request.Path))
        {
            snapshot = source.Read();
        }

        var plan = await PlanAsync(snapshot, ct);
        progress.Total = snapshot.Users.Count + snapshot.Requests.Count + snapshot.Issues.Count;
        var now = time.GetUtcNow().UtcDateTime;

        progress.Current = "Users";
        var userIds = await UsersAsync(plan, progress, now, ct);

        progress.Current = "Asking TMDB for titles the library lacks";
        var titles = request.AskTmdb && tmdb.Configured ? await TitlesAsync(plan, progress, ct) : [];

        progress.Current = "Requests";
        await RequestsAsync(plan, userIds, titles, progress, ct);

        progress.Current = "Issues";
        await IssuesAsync(plan, userIds, titles, progress, ct);

        if (request.ImportSliders)
        {
            progress.Current = "Discover rows";
            await SlidersAsync(plan, progress, ct);
        }

        if (string.Equals(Path.GetFullPath(request.Path), UploadPath, StringComparison.Ordinal) && File.Exists(UploadPath))
        {
            File.Delete(UploadPath);
            progress.UploadDeleted = true;
        }
    }

    /// <summary>Copies an uploaded <c>db.sqlite3</c> into the config directory, replacing the previous upload.</summary>
    public async Task<SeerrUploadResponse> SaveUploadAsync(Stream body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Directory.CreateDirectory(Path.GetDirectoryName(UploadPath)!);
        var partial = UploadPath + ".part";
        try
        {
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                await body.CopyToAsync(file, 1 << 20, ct);
            }

            File.Move(partial, UploadPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }

        return new SeerrUploadResponse(UploadPath, new FileInfo(UploadPath).Length);
    }

    // ---- reading and planning ----

    private async Task<Plan> PlanAsync(SeerrSnapshot snapshot, CancellationToken ct)
    {
        var settings = await UserSettings.ReadAsync(db, ct);
        var fourK = string.IsNullOrWhiteSpace(settings.DiscoverFourKVersion) ? null : settings.DiscoverFourKVersion.Trim().ToLowerInvariant();

        var bigarrUsers = await db.Users.AsNoTracking().OrderBy(u => u.Id)
            .Select(u => new BigarrUser(u.Id, u.Username, u.Role, u.SourceRef))
            .ToListAsync(ct);
        var plexLinks = await db.ExternalLogins.AsNoTracking().Where(l => l.Provider == PlexIdentityProvider.ProviderKey)
            .Select(l => new { l.ExternalId, l.ExternalUsername, l.UserId })
            .ToListAsync(ct);

        var movieIds = snapshot.Media.Values.Where(m => m.MediaType == "movie").Select(m => m.TmdbId).Distinct().ToList();
        var films = (await db.Movies.AsNoTracking().Where(m => movieIds.Contains(m.TmdbId))
                .Select(m => new { m.Id, m.TmdbId, m.Title, m.Year, m.Images, Versions = m.Versions.Select(v => new LibraryVersion(v.Id, v.Name, v.IsDefault)).ToList() })
                .ToListAsync(ct))
            .GroupBy(m => m.TmdbId)
            .ToDictionary(g => g.Key, g => g.Select(m => new LibraryTitle(m.Id, m.Title, m.Year, Poster(m.Images), m.Versions, null)).First());

        var tvMedia = snapshot.Media.Values.Where(m => m.MediaType == "tv").ToList();
        var tvdbIds = tvMedia.Where(m => m.TvdbId is not null).Select(m => m.TvdbId).Distinct().ToList();
        var tvTmdbIds = tvMedia.Select(m => (int?)m.TmdbId).Distinct().ToList();
        var series = await db.Series.AsNoTracking().Where(s => tvdbIds.Contains(s.TvdbId) || tvTmdbIds.Contains(s.TmdbId))
            .Select(s => new { s.Id, s.TvdbId, s.TmdbId, s.Title, s.Year, s.Images, Versions = s.Versions.Select(v => new LibraryVersion(v.Id, v.Name, v.IsDefault)).ToList() })
            .ToListAsync(ct);
        var seriesByTvdb = series.Where(s => s.TvdbId is not null).GroupBy(s => s.TvdbId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(s => new LibraryTitle(s.Id, s.Title, s.Year, Poster(s.Images), s.Versions, s.TvdbId)).First());
        var seriesByTmdb = series.Where(s => s.TmdbId is not null).GroupBy(s => s.TmdbId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(s => new LibraryTitle(s.Id, s.Title, s.Year, Poster(s.Images), s.Versions, s.TvdbId)).First());

        var requestRefs = await db.MediaRequests.AsNoTracking().Where(r => r.SourceRef != null).Select(r => r.SourceRef!).ToListAsync(ct);
        var issueRefs = await db.Issues.AsNoTracking().Where(i => i.SourceRef != null).Select(i => new { i.SourceRef, i.Id }).ToListAsync(ct);
        var commentRefs = await db.IssueComments.AsNoTracking().Where(c => c.SourceRef != null).Select(c => c.SourceRef!).ToListAsync(ct);
        var active = await db.MediaRequests.AsNoTracking()
            .Where(r => r.SourceRef == null && r.TmdbId != null && (r.Status == RequestStatus.Pending || r.Status == RequestStatus.Approved || r.Status == RequestStatus.Failed))
            .Select(r => new { r.MediaType, r.TmdbId, r.FourK })
            .ToListAsync(ct);

        var plan = new Plan(snapshot, fourK)
        {
            Films = films,
            SeriesByTvdb = seriesByTvdb,
            SeriesByTmdb = seriesByTmdb,
            ImportedRequests = [.. requestRefs],
            ImportedIssues = issueRefs.ToDictionary(i => i.SourceRef!, i => i.Id, StringComparer.Ordinal),
            ImportedComments = [.. commentRefs],
            ActiveBigarr = [.. active.Select(a => (a.MediaType, a.TmdbId!.Value, a.FourK))],
            DefaultQuota = settings.DefaultQuota,
        };

        MatchUsers(plan, bigarrUsers, plexLinks.Select(l => (l.ExternalId, l.ExternalUsername, l.UserId)).ToList());
        return plan;
    }

    /// <summary>
    /// Brought before; else the same Plex account; else the same name (a Bigarr username or the name of a linked Plex
    /// account, case aside); else Seerr's owner is Bigarr's first admin; else a new user. A Bigarr user takes one Seerr
    /// account at most.
    /// </summary>
    private static void MatchUsers(Plan plan, List<BigarrUser> bigarr, List<(string ExternalId, string? ExternalUsername, int UserId)> plexLinks)
    {
        var claimed = new HashSet<int>();
        var byRef = bigarr.Where(u => u.SourceRef is not null).ToDictionary(u => u.SourceRef!, StringComparer.Ordinal);
        var byId = bigarr.ToDictionary(u => u.Id);
        var byPlex = plexLinks.GroupBy(l => l.ExternalId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().UserId, StringComparer.Ordinal);
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in bigarr)
        {
            byName.TryAdd(user.Username, user.Id);
        }

        foreach (var link in plexLinks.Where(l => !string.IsNullOrWhiteSpace(l.ExternalUsername)))
        {
            byName.TryAdd(link.ExternalUsername!, link.UserId);
        }

        var taken = new HashSet<string>(bigarr.Select(u => u.Username), StringComparer.OrdinalIgnoreCase);

        // Earlier imports and Plex accounts first, so a name match never takes a user an exact match needs.
        var pending = new List<SeerrUser>();
        foreach (var seerr in plan.Snapshot.Users)
        {
            var access = SeerrMapping.Access(seerr.Permissions);
            if (byRef.TryGetValue(UserRef(seerr.Id), out var imported) && claimed.Add(imported.Id))
            {
                plan.Add(new UserPlan(seerr, access, SeerrUserMatch.AlreadyImported, imported.Id, imported.Username, imported.Role));
            }
            else if (seerr.PlexId is { } plexId && byPlex.TryGetValue(plexId.ToString(CultureInfo.InvariantCulture), out var linked) && claimed.Add(linked))
            {
                plan.Add(new UserPlan(seerr, access, SeerrUserMatch.PlexId, linked, byId[linked].Username, byId[linked].Role));
            }
            else
            {
                pending.Add(seerr);
            }
        }

        foreach (var seerr in pending)
        {
            var access = SeerrMapping.Access(seerr.Permissions);
            var named = new[] { seerr.Username, seerr.PlexUsername, seerr.JellyfinUsername }
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => byName.TryGetValue(n!.Trim(), out var id) ? id : (int?)null)
                .FirstOrDefault(id => id is not null && !claimed.Contains(id.Value));
            if (named is { } nameId && claimed.Add(nameId))
            {
                plan.Add(new UserPlan(seerr, access, SeerrUserMatch.Username, nameId, byId[nameId].Username, byId[nameId].Role));
                continue;
            }

            if (seerr.Id == OwnerId && access.Role == UserRole.Admin
                && bigarr.FirstOrDefault(u => u.Role == UserRole.Admin && !claimed.Contains(u.Id)) is { } admin && claimed.Add(admin.Id))
            {
                plan.Add(new UserPlan(seerr, access, SeerrUserMatch.Owner, admin.Id, admin.Username, admin.Role));
                continue;
            }

            var username = Unique(seerr.DisplayName, taken);
            plan.Add(new UserPlan(seerr, access, seerr.PlexId is null ? SeerrUserMatch.NewWithoutSignIn : SeerrUserMatch.NewPlex, null, username, access.Role));
        }
    }

    /// <summary>The name made unique among the users (<c>name</c>, <c>name-2</c>…), as a first Plex sign-in does.</summary>
    private static string Unique(string wanted, HashSet<string> taken)
    {
        var name = wanted.Trim();
        name = name.Length == 0 ? "user" : name.Length > 60 ? name[..60] : name;
        var candidate = name;
        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{name}-{n.ToString(CultureInfo.InvariantCulture)}";
        }

        taken.Add(candidate);
        return candidate;
    }

    // ---- writing ----

    private async Task<Dictionary<int, int>> UsersAsync(Plan plan, SeerrImportProgress progress, DateTime now, CancellationToken ct)
    {
        var ids = new Dictionary<int, int>();
        var created = new List<(UserPlan Plan, User User)>();
        foreach (var user in plan.Users.Values.OrderBy(u => u.Seerr.Id))
        {
            var seerr = user.Seerr;
            if (user.BigarrId is { } existingId)
            {
                ids[seerr.Id] = existingId;
                if (user.Match == SeerrUserMatch.AlreadyImported)
                {
                    progress.AlreadyImported++;
                }
                else
                {
                    progress.UsersMatched++;
                }

                // A matched user keeps everything of theirs; only a Discord id they never gave comes from Seerr.
                if (user.DiscordId is { } discordId)
                {
                    var bigarr = await db.Users.FirstAsync(u => u.Id == existingId, ct);
                    if (bigarr.DiscordUserId is null)
                    {
                        bigarr.DiscordUserId = discordId;
                        bigarr.DiscordMentions ??= SeerrMapping.Mentions(seerr.DiscordTypes ?? 0);
                        progress.UsersUpdated++;
                    }
                }

                progress.Processed++;
                continue;
            }

            var entity = new User
            {
                Username = user.Username,
                PasswordHash = string.Empty,
                Role = user.Role,
                Permissions = user.Access.Permissions.Copy(),
                Quota = Quota(seerr, plan.DefaultQuota),
                CreatedAt = seerr.CreatedAt ?? now,
                DiscordUserId = user.DiscordId,
                DiscordMentions = user.DiscordId is null ? null : SeerrMapping.Mentions(seerr.DiscordTypes ?? 0),
                SourceRef = UserRef(seerr.Id),
            };
            db.Users.Add(entity);
            if (seerr.PlexId is { } plexId)
            {
                db.ExternalLogins.Add(new ExternalLogin
                {
                    Provider = PlexIdentityProvider.ProviderKey,
                    ExternalId = plexId.ToString(CultureInfo.InvariantCulture),
                    User = entity,
                    ExternalUsername = seerr.PlexUsername ?? seerr.Username,
                    Email = seerr.Email,
                    LinkedAt = now,
                });
            }
            else
            {
                progress.Issue("user", seerr.Id, seerr.DisplayName, "Not a Plex account in Seerr: created without a way to sign in until the admin links one.");
            }

            created.Add((user, entity));
            progress.UsersAdded++;
            progress.Processed++;
        }

        await db.SaveChangesAsync(ct);
        foreach (var (user, entity) in created)
        {
            ids[user.Seerr.Id] = entity.Id;
        }

        db.ChangeTracker.Clear();
        return ids;
    }

    /// <summary>
    /// Seerr's own limit when the user has one (0 is unlimited there too), else Bigarr's default for new users, as Seerr
    /// falls back to its global quota.
    /// </summary>
    private static RequestQuota Quota(SeerrUser seerr, RequestQuota defaults)
    {
        var quota = defaults.Copy();
        if (seerr.MovieQuotaLimit is { } movies)
        {
            quota.FilmLimit = movies > 0 ? movies : null;
            quota.FilmDays = seerr.MovieQuotaDays is > 0 and var days ? days : quota.FilmDays;
        }

        if (seerr.TvQuotaLimit is { } seasons)
        {
            quota.SeasonLimit = seasons > 0 ? seasons : null;
            quota.SeasonDays = seerr.TvQuotaDays is > 0 and var days ? days : quota.SeasonDays;
        }

        return quota;
    }

    /// <summary>Asks TMDB once per title the library lacks; a title TMDB does not answer for keeps its id as a name.</summary>
    private async Task<Dictionary<(RequestMediaType, int), TmdbTitle>> TitlesAsync(Plan plan, SeerrImportProgress progress, CancellationToken ct)
    {
        var wanted = plan.Snapshot.Requests.Select(plan.Request).Where(d => d.Skip is null && !d.InLibrary && !plan.ImportedRequests.Contains(RequestRef(d.SeerrId))).Select(d => (d.Type, d.Media!.TmdbId))
            .Concat(plan.Snapshot.Issues.Select(plan.Issue).Where(d => d.Skip is null && !d.InLibrary && !plan.ImportedIssues.ContainsKey(IssueRef(d.SeerrId))).Select(d => (d.Type, d.Media!.TmdbId)))
            .Distinct()
            .ToList();
        var titles = new Dictionary<(RequestMediaType, int), TmdbTitle>();
        foreach (var (type, tmdbId) in wanted)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                TmdbTitle? title = type == RequestMediaType.Movie
                    ? await tmdb.MovieAsync(tmdbId, ct) is { } movie ? new TmdbTitle(movie.Title ?? movie.OriginalTitle, YearOf(movie.ReleaseDate), Image(movie.PosterPath)) : null
                    : await tmdb.SeriesAsync(tmdbId, ct) is { } tv ? new TmdbTitle(tv.Name ?? tv.OriginalName, YearOf(tv.FirstAirDate), Image(tv.PosterPath)) : null;
                if (title?.Title is { Length: > 0 })
                {
                    titles[(type, tmdbId)] = title;
                    progress.TitlesFromTmdb++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "TMDB did not answer for {Type} {TmdbId}; the Seerr import names it by its id", type, tmdbId);
            }
        }

        return titles;
    }

    private async Task RequestsAsync(Plan plan, Dictionary<int, int> users, Dictionary<(RequestMediaType, int), TmdbTitle> titles, SeerrImportProgress progress, CancellationToken ct)
    {
        var added = new List<MediaRequest>();
        foreach (var seerr in plan.Snapshot.Requests)
        {
            progress.Processed++;
            if (plan.ImportedRequests.Contains(RequestRef(seerr.Id)))
            {
                progress.AlreadyImported++;
                continue;
            }

            var decision = plan.Request(seerr);
            if (decision.Skip is not null)
            {
                progress.Skipped++;
                progress.Issue("request", seerr.Id, decision.What, decision.Skip);
                continue;
            }

            var media = decision.Media!;
            var (title, year, poster) = Title(decision.Type, media.TmdbId, decision.Library, titles);
            var status = decision.Status!.Value;
            var requester = users[seerr.RequestedById!.Value];
            int? decider = seerr.ModifiedById is { } modifiedBy && users.TryGetValue(modifiedBy, out var mapped) ? mapped : null;

            // Seerr marks a request its requester's own flags approved with the requester as the one who modified it.
            var auto = status == RequestStatus.Approved && (seerr.ModifiedById is null || seerr.ModifiedById == seerr.RequestedById);
            var request = new MediaRequest
            {
                MediaType = decision.Type,
                TmdbId = media.TmdbId,
                TvdbId = decision.Type == RequestMediaType.Series ? decision.Library?.TvdbId ?? media.TvdbId : null,
                Title = title,
                Year = year,
                PosterUrl = poster,
                FourK = seerr.Is4K,
                Seasons = decision.Type == RequestMediaType.Series ? [.. seerr.Seasons.Order()] : [],
                Status = status,
                RequestedById = requester,
                CreatedAt = seerr.CreatedAt,
                UpdatedAt = seerr.UpdatedAt,
                AutoApproved = auto,
                DecidedById = status == RequestStatus.Pending || auto ? null : decider,
                DecidedAt = status == RequestStatus.Pending ? null : auto ? seerr.CreatedAt : seerr.UpdatedAt,
                Error = status == RequestStatus.Failed ? "Failed in Seerr before the move to Bigarr." : null,
                SourceRef = RequestRef(seerr.Id),
            };

            if (status == RequestStatus.Approved && decision.Library is { } library)
            {
                if (decision.Type == RequestMediaType.Movie)
                {
                    request.MovieId = library.Id;
                }
                else
                {
                    request.SeriesId = library.Id;
                }

                request.VersionName = seerr.Is4K ? library.Versions.FirstOrDefault(v => v.Name == plan.FourK)?.Name : library.Versions.FirstOrDefault(v => v.IsDefault)?.Name;
            }

            // Seerr told its users this one arrived: Bigarr's "available" message must not go out for it again.
            if (status == RequestStatus.Approved && (seerr.Status == 5 || (seerr.Is4K ? media.Status4K : media.Status) == SeerrMapping.MediaAvailable))
            {
                request.AvailableAt = seerr.UpdatedAt;
            }

            if (decision.Library is null)
            {
                progress.Issue("request", seerr.Id, title, "Not in the library: imported without a link to a title.");
            }

            db.MediaRequests.Add(request);
            added.Add(request);
            progress.RequestsAdded++;
        }

        await db.SaveChangesAsync(ct);

        // An approved request the library already holds in full is available now: mark it, as the job would without the message.
        var waiting = added.Where(r => r.Status == RequestStatus.Approved && r.AvailableAt is null).ToList();
        if (waiting.Count > 0)
        {
            var shown = await states.ResolveAsync(waiting, ct);
            var now = time.GetUtcNow().UtcDateTime;
            foreach (var request in waiting.Where(r => shown[r.Id] == RequestState.Available))
            {
                request.AvailableAt = now;
            }

            await db.SaveChangesAsync(ct);
        }

        db.ChangeTracker.Clear();
    }

    private async Task IssuesAsync(Plan plan, Dictionary<int, int> users, Dictionary<(RequestMediaType, int), TmdbTitle> titles, SeerrImportProgress progress, CancellationToken ct)
    {
        var threads = plan.Snapshot.Comments.GroupBy(c => c.IssueId).ToDictionary(g => g.Key, g => g.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToList());
        var seriesIds = plan.Snapshot.Issues.Select(plan.Issue).Where(d => d.Scope == IssueScope.Episode && d.Library is not null).Select(d => d.Library!.Id).Distinct().ToList();
        var episodes = (await db.Episodes.AsNoTracking().Where(e => seriesIds.Contains(e.SeriesId))
                .Select(e => new { e.Id, e.SeriesId, e.SeasonNumber, e.EpisodeNumber, e.Title }).ToListAsync(ct))
            .GroupBy(e => (e.SeriesId, e.SeasonNumber, e.EpisodeNumber))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var seerr in plan.Snapshot.Issues)
        {
            progress.Processed++;
            var thread = threads.GetValueOrDefault(seerr.Id) ?? [];
            if (plan.ImportedIssues.TryGetValue(IssueRef(seerr.Id), out var existingId))
            {
                progress.AlreadyImported++;

                // Comments written in Seerr since the last run join the thread; the first one is the issue's own text.
                foreach (var comment in thread.Skip(1).Where(c => !plan.ImportedComments.Contains(CommentRef(c.Id))))
                {
                    db.IssueComments.Add(Comment(existingId, comment, users));
                    progress.CommentsAdded++;
                }

                continue;
            }

            var decision = plan.Issue(seerr);
            if (decision.Skip is not null)
            {
                progress.Skipped++;
                progress.Issue("issue", seerr.Id, decision.What, decision.Skip);
                continue;
            }

            var media = decision.Media!;
            var library = decision.Library;
            var (title, year, poster) = Title(decision.Type, media.TmdbId, library, titles);
            var version = library?.Versions.FirstOrDefault(v => v.IsDefault) ?? library?.Versions.FirstOrDefault();
            var first = thread.FirstOrDefault();
            var issue = new Issue
            {
                Type = SeerrMapping.IssueTypeOf(seerr.IssueType) ?? IssueType.Other,
                Scope = decision.Scope,
                Status = SeerrMapping.IssueStatusOf(seerr.Status),
                MediaType = decision.Type,
                TmdbId = media.TmdbId,
                TvdbId = decision.Type == RequestMediaType.Series ? library?.TvdbId ?? media.TvdbId : null,
                Title = title,
                Year = year,
                PosterUrl = poster,
                VersionName = version?.Name ?? VersionNaming.Fallback,
                Message = Clip(first?.Message is { Length: > 0 } text ? text : "(No description in Seerr.)"),
                ReportedById = users[seerr.CreatedById!.Value],
                CreatedAt = seerr.CreatedAt,
                UpdatedAt = seerr.UpdatedAt,
                SourceRef = IssueRef(seerr.Id),
            };

            if (decision.Type == RequestMediaType.Movie)
            {
                (issue.MovieId, issue.MovieVersionId) = (library?.Id, version?.Id);
            }
            else
            {
                (issue.SeriesId, issue.SeriesVersionId) = (library?.Id, version?.Id);
                if (decision.Scope is IssueScope.Season or IssueScope.Episode)
                {
                    issue.SeasonNumber = seerr.ProblemSeason;
                }

                if (decision.Scope == IssueScope.Episode)
                {
                    var episode = library is null ? null : episodes.GetValueOrDefault((library.Id, seerr.ProblemSeason, seerr.ProblemEpisode));
                    issue.EpisodeId = episode?.Id;
                    issue.EpisodeLabel = IssueService.EpisodeLabel(seerr.ProblemSeason, seerr.ProblemEpisode, episode?.Title);
                }
            }

            int? resolver = seerr.ModifiedById is { } modifiedBy && users.TryGetValue(modifiedBy, out var mapped) ? mapped : null;
            if (issue.Status == IssueStatus.Resolved)
            {
                (issue.ResolvedAt, issue.ResolvedById) = (seerr.UpdatedAt, resolver);
            }

            foreach (var comment in thread.Skip(1))
            {
                issue.Comments.Add(Comment(null, comment, users));
                progress.CommentsAdded++;
            }

            if (issue.Status == IssueStatus.Resolved)
            {
                issue.Comments.Add(new IssueComment { Kind = IssueCommentKind.Resolved, AuthorId = resolver, CreatedAt = seerr.UpdatedAt });
            }

            if (library is null)
            {
                progress.Issue("issue", seerr.Id, title, "Not in the library: imported without a link to a title.");
            }

            db.Issues.Add(issue);
            progress.IssuesAdded++;
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private static IssueComment Comment(int? issueId, SeerrComment comment, Dictionary<int, int> users) => new()
    {
        IssueId = issueId ?? 0,
        Kind = IssueCommentKind.Comment,
        AuthorId = comment.UserId is { } userId && users.TryGetValue(userId, out var mapped) ? mapped : null,
        Message = Clip(comment.Message),
        CreatedAt = comment.CreatedAt,
        SourceRef = CommentRef(comment.Id),
    };

    private static string Clip(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length > IssueService.MaxMessageLength ? trimmed[..IssueService.MaxMessageLength] : trimmed;
    }

    /// <summary>
    /// Seerr's rows in Seerr's order, each switched on or off as there; a custom row Bigarr has already (same kind and
    /// value) is updated, else added. Bigarr's rows Seerr does not have keep their state and follow.
    /// </summary>
    private async Task SlidersAsync(Plan plan, SeerrImportProgress progress, CancellationToken ct)
    {
        var existing = await db.DiscoverSliders.OrderBy(s => s.Position).ThenBy(s => s.Id).ToListAsync(ct);
        var ordered = new List<DiscoverSlider>();
        foreach (var seerr in plan.Snapshot.Sliders.OrderBy(s => s.Order).ThenBy(s => s.Id))
        {
            var match = SeerrMapping.Slider(seerr.Type, seerr.Data);
            if (match.Kind is not { } kind)
            {
                continue;
            }

            var slider = existing.FirstOrDefault(s => s.Kind == kind && (DiscoverSlider.IsBuiltInKind(kind) || s.Value == match.Value) && !ordered.Contains(s));
            if (slider is null)
            {
                slider = new DiscoverSlider
                {
                    Kind = kind,
                    Value = match.Value,
                    Title = DiscoverSlider.IsBuiltInKind(kind) ? null : seerr.Title is { Length: > 0 } t ? t : SeerrMapping.SliderName(seerr.Type),
                };
                db.DiscoverSliders.Add(slider);
                progress.SlidersAdded++;
            }
            else
            {
                if (!DiscoverSlider.IsBuiltInKind(kind) && seerr.Title is { Length: > 0 } title)
                {
                    slider.Title = title;
                }

                progress.SlidersUpdated++;
            }

            slider.Enabled = seerr.Enabled;
            ordered.Add(slider);
        }

        if (ordered.Count == 0)
        {
            return;
        }

        var position = 0;
        foreach (var slider in ordered.Concat(existing.Where(s => !ordered.Contains(s))))
        {
            slider.Position = position++;
        }

        await db.SaveChangesAsync(ct);
    }

    // ---- helpers ----

    private static (string Title, int? Year, string? Poster) Title(RequestMediaType type, int tmdbId, LibraryTitle? library, Dictionary<(RequestMediaType, int), TmdbTitle> titles) =>
        library is not null ? (library.Title, library.Year, library.Poster)
        : titles.TryGetValue((type, tmdbId), out var found) ? (found.Title!, found.Year, found.Poster)
        : (string.Format(CultureInfo.InvariantCulture, Placeholder, tmdbId), null, null);

    private static string? Poster(IEnumerable<MediaImage> images) => images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl;

    private string? Image(string? path) => string.IsNullOrWhiteSpace(path) ? null : metadata.Value.ImageBaseUrl.TrimEnd('/') + "/original" + path;

    private static int? YearOf(string? date) =>
        date is { Length: >= 4 } && int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) ? year : null;

    private static void Count(Dictionary<(RequestMediaType, int), TitleCount> titles, SeerrMedia media, bool isRequest)
    {
        var key = (media.MediaType == "movie" ? RequestMediaType.Movie : RequestMediaType.Series, media.TmdbId);
        var count = titles.GetValueOrDefault(key) ?? new TitleCount(media.TvdbId, 0, 0);
        titles[key] = isRequest ? count with { Requests = count.Requests + 1 } : count with { Issues = count.Issues + 1 };
    }

    private static SeerrSliderLine SliderLine(SeerrSlider slider)
    {
        var match = SeerrMapping.Slider(slider.Type, slider.Data);
        return new SeerrSliderLine(
            SeerrMapping.SliderName(slider.Type),
            slider.Title,
            slider.Enabled,
            match.Kind is { } kind ? System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()) : null,
            match.Value,
            match.Skipped);
    }

    /// <summary>What Seerr has that never comes along, with how many rows carry it.</summary>
    private static List<SeerrNotImported> NotImported(Plan plan)
    {
        var users = plan.Snapshot.Users;
        var lines = new List<SeerrNotImported>
        {
            new("Passwords, Plex and Jellyfin tokens", users.Count, "Never imported: users sign in to Bigarr with Plex."),
            new("Language", users.Count(u => !string.IsNullOrWhiteSpace(u.Locale)), "Bigarr has no per-user language."),
            new(
                "Settings of matched users",
                plan.Users.Values.Count(u => u.BigarrId is not null && u.Match != SeerrUserMatch.AlreadyImported),
                "A user Bigarr already has keeps their role, permissions and quota; only a Discord id they never set comes from Seerr."),
            new("Notification agents other than Discord", users.Count, "Bigarr mentions users on Discord only."),
            new("Server, profile, root folder and tags of each request", plan.Snapshot.Requests.Count, "Bigarr's approval picks its own; imported requests are history and are not sent anywhere."),
            new("File at report time", plan.Snapshot.Issues.Count, "Seerr keeps no file for an issue: imported issues show none, and their actions read the file as it is now."),
            new("Votes, watchlists and the blocklist", 0, "Bigarr has no such thing yet."),
        };
        return [.. lines.Where(l => l.Count > 0)];
    }

    // ---- the plan ----

    private sealed record BigarrUser(int Id, string Username, UserRole Role, string? SourceRef);

    private sealed record LibraryVersion(int Id, string Name, bool IsDefault);

    private sealed record LibraryTitle(int Id, string Title, int? Year, string? Poster, List<LibraryVersion> Versions, int? TvdbId);

    private sealed record TmdbTitle(string? Title, int? Year, string? Poster);

    private sealed record TitleCount(int? TvdbId, int Requests, int Issues);

    private sealed record UserPlan(SeerrUser Seerr, SeerrAccess Access, SeerrUserMatch Match, int? BigarrId, string Username, UserRole Role)
    {
        /// <summary>The first of the user's Discord ids that is a Discord user id.</summary>
        public string? DiscordId { get; } = Seerr.DiscordIds.Select(id => Mentions.NormalizeDiscordUserId(id, out _)).FirstOrDefault(id => id is not null);
    }

    private sealed record RequestDecision(int SeerrId, RequestMediaType Type, SeerrMedia? Media, RequestStatus? Status, LibraryTitle? Library, string What, string? Skip)
    {
        public bool InLibrary => Library is not null;
    }

    private sealed record IssueDecision(int SeerrId, RequestMediaType Type, SeerrMedia? Media, IssueScope Scope, LibraryTitle? Library, string What, string? Skip)
    {
        public bool InLibrary => Library is not null;
    }

    private sealed class Plan(SeerrSnapshot snapshot, string? fourK)
    {
        public SeerrSnapshot Snapshot { get; } = snapshot;

        public string? FourK { get; } = fourK;

        public Dictionary<int, UserPlan> Users { get; } = [];

        public required Dictionary<int, LibraryTitle> Films { get; init; }

        public required Dictionary<int, LibraryTitle> SeriesByTvdb { get; init; }

        public required Dictionary<int, LibraryTitle> SeriesByTmdb { get; init; }

        public required HashSet<string> ImportedRequests { get; init; }

        public required Dictionary<string, int> ImportedIssues { get; init; }

        public required HashSet<string> ImportedComments { get; init; }

        public required HashSet<(RequestMediaType, int, bool)> ActiveBigarr { get; init; }

        public required RequestQuota DefaultQuota { get; init; }

        public void Add(UserPlan user) => Users[user.Seerr.Id] = user;

        public RequestDecision Request(SeerrRequest seerr)
        {
            var media = seerr.MediaId is { } id ? Snapshot.Media.GetValueOrDefault(id) : null;
            var type = (seerr.Type is { Length: > 0 } t ? t : media?.MediaType) == "tv" ? RequestMediaType.Series : RequestMediaType.Movie;
            var what = media is null ? $"Request {seerr.Id}" : Describe(type, media.TmdbId);
            var status = SeerrMapping.Status(seerr.Status);
            var skip =
                media is null ? "Its title is gone from Seerr."
                : seerr.RequestedById is not { } requester || !Users.ContainsKey(requester) ? "Its requester is gone from Seerr."
                : status is null ? $"Unknown status {seerr.Status}."
                : null;
            return new RequestDecision(seerr.Id, type, media, status, media is null ? null : Find(type, media), what, skip);
        }

        public IssueDecision Issue(SeerrIssue seerr)
        {
            var media = seerr.MediaId is { } id ? Snapshot.Media.GetValueOrDefault(id) : null;
            var type = media?.MediaType == "tv" ? RequestMediaType.Series : RequestMediaType.Movie;
            var what = media is null ? $"Issue {seerr.Id}" : Describe(type, media.TmdbId);
            var skip =
                media is null ? "Its title is gone from Seerr."
                : seerr.CreatedById is not { } reporter || !Users.ContainsKey(reporter) ? "Its reporter is gone from Seerr."
                : null;
            var scope = SeerrMapping.ScopeOf(type == RequestMediaType.Movie, seerr.ProblemSeason, seerr.ProblemEpisode);
            return new IssueDecision(seerr.Id, type, media, scope, media is null ? null : Find(type, media), what, skip);
        }

        public List<SeerrSkippedLine> SkippedLines() =>
        [
            .. Snapshot.Requests.Select(Request).Where(d => d.Skip is not null && !ImportedRequests.Contains(RequestRef(d.SeerrId)))
                .Select(d => new SeerrSkippedLine("request", d.SeerrId, d.What, d.Skip!)),
            .. Snapshot.Issues.Select(Issue).Where(d => d.Skip is not null && !ImportedIssues.ContainsKey(IssueRef(d.SeerrId)))
                .Select(d => new SeerrSkippedLine("issue", d.SeerrId, d.What, d.Skip!)),
        ];

        /// <summary>A film by TMDB id; a series by TVDB id first (the library's key), else by TMDB id.</summary>
        private LibraryTitle? Find(RequestMediaType type, SeerrMedia media) =>
            type == RequestMediaType.Movie
                ? Films.GetValueOrDefault(media.TmdbId)
                : (media.TvdbId is { } tvdb ? SeriesByTvdb.GetValueOrDefault(tvdb) : null) ?? SeriesByTmdb.GetValueOrDefault(media.TmdbId);

        private static string Describe(RequestMediaType type, int tmdbId) =>
            $"{(type == RequestMediaType.Movie ? "Film" : "Series")} TMDB {tmdbId.ToString(CultureInfo.InvariantCulture)}";
    }
}
