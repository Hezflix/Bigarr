using System.Globalization;
using System.Text;
using Bigarr.Api.Features.Playback;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.TautulliImport;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Migration;

/// <summary>
/// Brings a Tautulli history into Bigarr's plays, for one Plex server:
/// <list type="bullet">
/// <item>Tautulli's accounts become media server users under the same Plex account id the live sessions carry (the
/// server owner, whom Plex numbers 1 in its sessions, included); deleted ones come along as they are;</item>
/// <item>each rating key becomes an item, with the ids its guids, Tautulli's lookups or Plex give, and its film by
/// TMDB or IMDb id, else by title and year;</item>
/// <item>each grouped play (rows sharing a <c>reference_id</c>) becomes one play of one stream per row, watched by the
/// saved play rules, a film's version told by the library's folders or the played resolution.</item>
/// </list>
/// Each stream remembers its row (<c>tautulli:&lt;id&gt;</c>), so a second run adds only rows Tautulli recorded since.
/// A play Bigarr followed live wins over a row of the same user and item at the same time.
/// </summary>
public sealed class TautulliImportService(
    BigarrDbContext db,
    PlexClient plex,
    PlexLibraries libraries,
    VersionResolver resolver,
    IOptions<BigarrOptions> options,
    TimeProvider time,
    ILogger<TautulliImportService> logger)
{
    public const string KeyPrefix = "tautulli:";

    /// <summary>Plex numbers the server owner's account 1 in its sessions; Tautulli stores the owner's plex.tv id instead.</summary>
    public const string OwnerExternalId = "1";

    /// <summary>Grouped plays written per transaction.</summary>
    public const int ChunkSize = 1000;

    /// <summary>Where an uploaded copy of <c>tautulli.db</c> goes, in the config directory.</summary>
    public string UploadPath => Path.GetFullPath(Path.Combine(options.Value.ConfigDirectory, "imports", "tautulli.db"));

    public async Task<Result<TautulliImportPreviewResponse>> PreviewAsync(TautulliImportPreviewRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var server = await ServerAsync(request.MediaServerId, ct);
        if (server.Failure is { } failure)
        {
            return failure;
        }

        TautulliDatabase source;
        try
        {
            source = TautulliDatabase.Open(request.Path);
        }
        catch (TautulliDatabaseException ex)
        {
            return Failure.Invalid("Cannot read the Tautulli database", ex.Message);
        }

        using (source)
        {
            var snapshot = Snapshot.Read(source);
            var context = await ContextAsync(server.Value!, ct);
            var plans = Plan(snapshot, context.Films);
            var users = snapshot.UserIds();
            var known = await db.MediaServerUsers.AsNoTracking().Where(u => u.MediaServerId == server.Value!.Id).Select(u => u.ExternalId).ToListAsync(ct);
            var folders = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

            int rows = 0, plays = 0, moviePlays = 0, episodePlays = 0, other = 0, withFilm = 0, withVersion = 0, askPlex = 0, unmatched = 0, already = 0, overlapping = 0;
            DateTime? first = null, last = null;
            var movies = new HashSet<string>(StringComparer.Ordinal);
            var episodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in Groups(source.Rows(ct)))
            {
                rows += group.Count;
                plays++;
                var head = group[0];
                first = first is null || head.StartedAt < first ? head.StartedAt : first;
                last = last is null || group[^1].StoppedAt > last ? group[^1].StoppedAt : last;
                var plan = plans[head.RatingKey];
                if (plan.Kind is null)
                {
                    other++;
                    continue;
                }

                if (group.All(r => context.Imported.ContainsKey(r.Id)))
                {
                    already++;
                    continue;
                }

                if (!group.Any(r => context.Imported.ContainsKey(r.Id)) && context.OverlapsLive(users[head.UserId], plan.RatingKey, head.StartedAt, group.Max(r => r.StoppedAt)))
                {
                    overlapping++;
                    continue;
                }

                if (plan.Kind == MediaItemKind.Episode)
                {
                    episodePlays++;
                    episodes.Add(plan.RatingKey);
                    continue;
                }

                moviePlays++;
                movies.Add(plan.RatingKey);
                if (plan.MovieId is null)
                {
                    if (plan.NeedsPlex)
                    {
                        askPlex++;
                    }
                    else
                    {
                        unmatched++;
                    }

                    continue;
                }

                withFilm++;
                if ((await VersionAsync(server.Value!, plan, group[^1].Details.SourceVideoResolution, folders, askPlex: true, ct))?.VersionId is not null)
                {
                    withVersion++;
                }
            }

            return Result<TautulliImportPreviewResponse>.Ok(new TautulliImportPreviewResponse(
                source.Version(),
                snapshot.Users.Count,
                snapshot.Users.Count(u => u.Deleted),
                users.Values.Distinct(StringComparer.Ordinal).Count(known.Contains),
                first,
                last,
                rows,
                plays,
                moviePlays,
                episodePlays,
                other,
                movies.Count,
                episodes.Count,
                withFilm,
                withVersion,
                askPlex,
                unmatched,
                already,
                overlapping));
        }
    }

    public async Task<Failure?> ValidateAsync(TautulliImportRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var server = await ServerAsync(request.MediaServerId, ct);
        if (server.Failure is { } failure)
        {
            return failure;
        }

        try
        {
            using var source = TautulliDatabase.Open(request.Path);
        }
        catch (TautulliDatabaseException ex)
        {
            return Failure.Invalid("Cannot read the Tautulli database", ex.Message);
        }

        return null;
    }

    public async Task ImportAsync(TautulliImportProgress progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var request = progress.Request;
        var server = (await ServerAsync(request.MediaServerId, ct)).Value ?? throw new InvalidOperationException("The media server is gone.");
        var stamp = time.GetUtcNow().UtcDateTime;

        using (var source = TautulliDatabase.Open(request.Path))
        {
            progress.Current = "Reading the Tautulli database";
            var snapshot = Snapshot.Read(source);
            progress.Total = source.GroupCount();
            var context = await ContextAsync(server, ct);
            var plans = Plan(snapshot, context.Films);

            if (request.AskPlex)
            {
                await AskPlexAsync(server, plans, context.Films, progress, ct);
            }

            progress.Current = "Adding users and items";
            var users = await UsersAsync(server, snapshot, stamp, progress, ct);
            await ItemsAsync(server, plans, context, stamp, progress, ct);

            var rules = new PlayRules(await db.PlaybackSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == PlaybackSettings.SingletonId, ct) ?? PlaybackSettings.Default());
            var folders = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            var unmatched = new Dictionary<string, int>(StringComparer.Ordinal);
            var externalIds = snapshot.UserIds();
            var chunk = 0;
            progress.Current = "Adding plays";
            foreach (var group in Groups(source.Rows(ct)))
            {
                await AddAsync(server, group, plans, users, externalIds, context, rules, folders, unmatched, request.AskPlex, progress, ct);
                if (++chunk >= ChunkSize)
                {
                    await FlushAsync(chunk, progress, ct);
                    chunk = 0;
                }
            }

            await FlushAsync(chunk, progress, ct);

            progress.Current = "Finishing";
            await SeenDatesAsync(server, stamp, ct);
            foreach (var (ratingKey, count) in unmatched.OrderByDescending(u => u.Value))
            {
                var plan = plans[ratingKey];
                progress.Issue(plan.Title, plan.Year, plan.NeedsPlex ? "No id from Plex and no film of this title and year in Bigarr" : "No film in Bigarr with these ids or this title and year", count);
            }
        }

        if (string.Equals(Path.GetFullPath(request.Path), UploadPath, StringComparison.Ordinal) && File.Exists(UploadPath))
        {
            File.Delete(UploadPath);
            progress.UploadDeleted = true;
        }
    }

    /// <summary>Copies an uploaded <c>tautulli.db</c> into the config directory, replacing the previous upload.</summary>
    public async Task<TautulliUploadResponse> SaveUploadAsync(Stream body, CancellationToken ct)
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

        return new TautulliUploadResponse(UploadPath, new FileInfo(UploadPath).Length);
    }

    private async Task AddAsync(
        MediaServer server,
        List<TautulliRow> group,
        Dictionary<string, ItemPlan> plans,
        Dictionary<long, int> users,
        Dictionary<long, string> externalIds,
        Context context,
        PlayRules rules,
        Dictionary<string, IReadOnlyList<string>> folders,
        Dictionary<string, int> unmatched,
        bool askPlex,
        TautulliImportProgress progress,
        CancellationToken ct)
    {
        var head = group[0];
        var plan = plans[head.RatingKey];
        if (plan.Kind is null || plan.EntityId is not { } itemId)
        {
            progress.OtherSkipped();
            return;
        }

        var fresh = group.Where(r => !context.Imported.ContainsKey(r.Id)).ToList();
        if (fresh.Count == 0)
        {
            progress.AlreadyImported();
            return;
        }

        var userId = users[head.UserId];
        var existingPlayId = group.Select(r => context.Imported.TryGetValue(r.Id, out var playId) ? playId : (int?)null).FirstOrDefault(id => id is not null);
        if (existingPlayId is null && context.OverlapsLive(externalIds[head.UserId], plan.RatingKey, head.StartedAt, group.Max(r => r.StoppedAt)))
        {
            progress.OverlappingLive();
            return;
        }

        var duration = plan.DurationMs ?? group.Max(r => r.MediaDurationMs);
        ResolvedVersion? version = null;
        if (plan.Kind == MediaItemKind.Movie && plan.MovieId is not null)
        {
            version = await VersionAsync(server, plan, group[^1].Details.SourceVideoResolution, folders, askPlex, ct);
        }

        Play play;
        if (existingPlayId is { } playId)
        {
            play = await db.Plays.FirstAsync(p => p.Id == playId, ct);
            progress.PlayExtended();
        }
        else
        {
            play = db.Plays.Add(new Play
            {
                UserId = userId,
                ItemId = itemId,
                StartedAt = head.StartedAt,
                StoppedAt = head.StoppedAt,
                Source = PlaySource.Tautulli,
                MovieId = plan.MovieId,
                VersionId = version?.VersionId,
                VersionName = version?.VersionName,
            }).Entity;
            progress.PlayAdded();
            if (plan.Kind == MediaItemKind.Movie)
            {
                progress.MoviePlay(plan.MovieId is not null, version?.VersionId is not null);
                if (plan.MovieId is null)
                {
                    unmatched[plan.RatingKey] = unmatched.GetValueOrDefault(plan.RatingKey) + 1;
                }
            }
        }

        long? previousEnd = null;
        foreach (var row in group)
        {
            var played = Math.Max(0, (int)(row.StoppedAt - row.StartedAt).TotalSeconds - row.PausedSeconds);
            var end = PlayRules.SnapEnd(row.ViewOffsetMs, duration);

            // Tautulli keeps where a stream stopped, not where it started: a resumed stream starts where the one
            // before stopped, the first one the time played before its end.
            var start = previousEnd is { } resumedAt && resumedAt <= end ? resumedAt : Math.Max(0, end - played * 1000L);
            previousEnd = end;
            if (context.Imported.ContainsKey(row.Id))
            {
                continue;
            }

            play.Streams.Add(new PlayStream
            {
                StartedAt = row.StartedAt,
                StoppedAt = row.StoppedAt,
                StartOffsetMs = start,
                EndOffsetMs = end,
                PausedSeconds = row.PausedSeconds,
                Details = row.Details,
                VersionId = version?.VersionId,
                ExternalKey = KeyPrefix + row.Id.ToString(CultureInfo.InvariantCulture),
            });

            play.StartedAt = row.StartedAt < play.StartedAt ? row.StartedAt : play.StartedAt;
            play.StoppedAt = row.StoppedAt > play.StoppedAt ? row.StoppedAt : play.StoppedAt;
            play.PlayedSeconds += played;
            play.PausedSeconds += row.PausedSeconds;
            play.MaxOffsetMs = Math.Max(play.MaxOffsetMs, end);
        }

        play.DurationMs = duration ?? play.DurationMs;
        play.Watched = rules.IsWatched(play.MaxOffsetMs, play.DurationMs, plan.CreditsStartMs);
        progress.StreamsAddedBy(fresh.Count);
    }

    private async Task FlushAsync(int groups, TautulliImportProgress progress, CancellationToken ct)
    {
        if (groups == 0)
        {
            return;
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        db.ChangeTracker.Clear();
        progress.GroupsDone(groups);
    }

    private async Task<Dictionary<long, int>> UsersAsync(MediaServer server, Snapshot snapshot, DateTime stamp, TautulliImportProgress progress, CancellationToken ct)
    {
        var existing = await db.MediaServerUsers.Where(u => u.MediaServerId == server.Id).ToDictionaryAsync(u => u.ExternalId, StringComparer.Ordinal, ct);
        var byTautulliId = snapshot.Users.ToDictionary(u => u.UserId);
        var entities = new Dictionary<long, MediaServerUser>();
        foreach (var (tautulliId, externalId) in snapshot.UserIds())
        {
            byTautulliId.TryGetValue(tautulliId, out var account);
            if (!existing.TryGetValue(externalId, out var user))
            {
                user = new MediaServerUser
                {
                    MediaServerId = server.Id,
                    ExternalId = externalId,
                    Name = account?.Username ?? snapshot.HistoryUsers.GetValueOrDefault(tautulliId) ?? externalId,
                    IsOwner = externalId == OwnerExternalId,
                    KeepHistory = account?.KeepHistory ?? true,
                    FirstSeenAt = stamp,

                    // Seen when they played: SeenDatesAsync dates the ones with a history, the others were never seen.
                    LastSeenAt = null,
                };
                db.MediaServerUsers.Add(user);
                existing[externalId] = user;
                progress.UserAdded();
            }

            if (account is not null)
            {
                if (user.DisplayName is null && account.FriendlyName is { } friendly && !string.Equals(friendly, user.Name, StringComparison.Ordinal))
                {
                    user.DisplayName = friendly;
                }

                user.ThumbUrl ??= account.Thumb;

                // Tautulli's word on who left the server, until plex.tv gives its own.
                if (user.OnServerCheckedAt is null)
                {
                    user.OnServer = user.IsOwner || !account.Deleted;
                }
            }

            entities[tautulliId] = user;
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return entities.ToDictionary(e => e.Key, e => e.Value.Id);
    }

    private async Task ItemsAsync(MediaServer server, Dictionary<string, ItemPlan> plans, Context context, DateTime stamp, TautulliImportProgress progress, CancellationToken ct)
    {
        var existing = await db.MediaServerItems.Where(i => i.MediaServerId == server.Id).ToDictionaryAsync(i => i.RatingKey, StringComparer.Ordinal, ct);
        var added = new List<(ItemPlan Plan, MediaServerItem Item)>();
        foreach (var plan in plans.Values.Where(p => p.Kind is not null))
        {
            if (existing.TryGetValue(plan.RatingKey, out var item))
            {
                // Bigarr's own view of the item (from the live sessions) wins; the import only fills what it lacks.
                item.TmdbId ??= plan.TmdbId;
                item.ImdbId ??= plan.ImdbId;
                item.TvdbId ??= plan.TvdbId;
                item.ShowTmdbId ??= plan.ShowTmdbId;
                item.ShowTvdbId ??= plan.ShowTvdbId;
                item.ShowImdbId ??= plan.ShowImdbId;
                item.OriginalTitle ??= plan.Source.OriginalTitle;
                item.MovieId ??= plan.MovieId;
                plan.MovieId = item.MovieId;
                plan.DurationMs ??= item.DurationMs;
                plan.CreditsStartMs ??= item.CreditsStartMs;
                plan.SectionId ??= item.LibrarySectionId;
                plan.EntityId = item.Id;
                continue;
            }

            var source = plan.Source;
            item = new MediaServerItem
            {
                MediaServerId = server.Id,
                RatingKey = plan.RatingKey,
                Kind = plan.Kind!.Value,
                Guid = source.Guid,
                TmdbId = plan.TmdbId,
                ImdbId = plan.ImdbId,
                TvdbId = plan.TvdbId,
                Title = context.Films.Title(plan.MovieId) ?? plan.Title,
                OriginalTitle = source.OriginalTitle,
                Year = plan.MovieId is not null ? context.Films.Year(plan.MovieId) ?? plan.Year : plan.Year,
                DurationMs = plan.DurationMs,
                CreditsStartMs = plan.CreditsStartMs,
                LibrarySectionId = plan.SectionId,
                LibrarySectionTitle = plan.SectionTitle,
                ShowRatingKey = source.GrandparentRatingKey,
                ShowTitle = plan.ShowTitle,
                ShowTmdbId = plan.ShowTmdbId,
                ShowTvdbId = plan.ShowTvdbId,
                ShowImdbId = plan.ShowImdbId,
                ShowGuid = plan.ShowGuid,
                SeasonRatingKey = source.ParentRatingKey,
                SeasonNumber = source.SeasonNumber,
                EpisodeNumber = source.EpisodeNumber,
                MovieId = plan.MovieId,
                FirstSeenAt = stamp,

                // Never refreshed: the live tracker reads the item from Plex the first time it plays again.
                UpdatedAt = DateTime.MinValue,
            };
            db.MediaServerItems.Add(item);
            added.Add((plan, item));
            progress.ItemAdded();
            if (added.Count % 5000 == 0)
            {
                await db.SaveChangesAsync(ct);
            }
        }

        await db.SaveChangesAsync(ct);
        foreach (var (plan, item) in added)
        {
            plan.EntityId = item.Id;
        }

        db.ChangeTracker.Clear();
    }

    /// <summary>Users and items the import created were first seen at their first play, last seen at their last.</summary>
    private async Task SeenDatesAsync(MediaServer server, DateTime stamp, CancellationToken ct)
    {
        await db.MediaServerUsers.Where(u => u.MediaServerId == server.Id && u.FirstSeenAt == stamp && db.Plays.Any(p => p.UserId == u.Id))
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(u => u.FirstSeenAt, u => db.Plays.Where(p => p.UserId == u.Id).Min(p => p.StartedAt))
                    .SetProperty(u => u.LastSeenAt, u => db.Plays.Where(p => p.UserId == u.Id).Max(p => p.StoppedAt)),
                ct);
        await db.MediaServerItems.Where(i => i.MediaServerId == server.Id && i.FirstSeenAt == stamp && db.Plays.Any(p => p.ItemId == i.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.FirstSeenAt, i => db.Plays.Where(p => p.ItemId == i.Id).Min(p => p.StartedAt)), ct);
    }

    /// <summary>
    /// Asks Plex for the ids of films known only by a Plex guid, and for the ids of shows Tautulli has none for. Stops
    /// asking at the first failure that is not "no such item": the rest stay unresolved or fall back to the title.
    /// </summary>
    private async Task AskPlexAsync(MediaServer server, Dictionary<string, ItemPlan> plans, FilmIndex films, TautulliImportProgress progress, CancellationToken ct)
    {
        var movies = plans.Values.Where(p => p.Kind == MediaItemKind.Movie && p.NeedsPlex).ToList();
        // Every show is asked, for the title Plex gives it now; its ids too when Tautulli has none.
        var shows = plans.Values.Where(p => p.Kind == MediaItemKind.Episode && p.Source.GrandparentRatingKey is not null)
            .GroupBy(p => p.Source.GrandparentRatingKey!, StringComparer.Ordinal)
            .ToList();
        var asked = 0;
        var total = movies.Count + shows.Count;
        try
        {
            foreach (var plan in movies)
            {
                progress.Current = $"Asking Plex about items ({++asked}/{total})";
                var item = await plex.ItemAsync(server, plan.RatingKey, ct);
                var ids = item?.Ids;
                plan.TmdbId ??= ids?.TmdbId;
                plan.ImdbId ??= ids?.ImdbId;
                plan.CreditsStartMs ??= item?.CreditsStartMs;
                plan.DurationMs ??= item?.DurationMs;
                plan.SectionTitle ??= item?.LibrarySectionTitle;
                progress.PlexAsked(ids is { TmdbId: not null } or { ImdbId: not null });
                plan.MovieId = films.Find(plan);
            }

            foreach (var show in shows)
            {
                progress.Current = $"Asking Plex about items ({++asked}/{total})";
                var found = await plex.IdsAsync(server, show.Key, ct);
                progress.PlexAsked(found is not null);
                foreach (var plan in show)
                {
                    plan.ShowTitle = found?.Title ?? plan.ShowTitle;
                    plan.ShowGuid ??= found?.Guid;
                    plan.ShowTvdbId ??= found?.Ids.TvdbId;
                    plan.ShowTmdbId ??= found?.Ids.TmdbId;
                    plan.ShowImdbId ??= found?.Ids.ImdbId;
                }
            }
        }
        catch (MediaServerException ex)
        {
            logger.LogWarning("Plex stopped answering during the Tautulli import, {Left} item(s) left without its ids: {Error}", total - asked + 1, ex.Message);
            progress.Issue(server.Name, null, $"Plex did not answer ({ex.Message}): {total - asked + 1} item(s) were not asked for their ids.", 0);
        }
    }

    private async Task<ResolvedVersion?> VersionAsync(MediaServer server, ItemPlan plan, string? resolution, Dictionary<string, IReadOnlyList<string>> folders, bool askPlex, CancellationToken ct)
    {
        var key = VersionResolver.ResolutionOf(resolution)?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        if (plan.Versions.TryGetValue(key, out var known))
        {
            return known;
        }

        IReadOnlyList<string> libraryFolders = [];
        if (askPlex && plan.SectionId is { } sectionId && !folders.TryGetValue(sectionId, out libraryFolders!))
        {
            libraryFolders = await libraries.FoldersAsync(server, sectionId, ct);
            folders[sectionId] = libraryFolders;
        }

        var item = new MediaServerItem { MediaServerId = server.Id, RatingKey = plan.RatingKey, Kind = MediaItemKind.Movie, Title = plan.Title, MovieId = plan.MovieId };
        var resolved = await resolver.ResolveAsync(server, item, null, resolution, libraryFolders ?? [], ct);
        plan.Versions[key] = resolved;
        return resolved;
    }

    private async Task<Result<MediaServer>> ServerAsync(int id, CancellationToken ct)
    {
        var server = await db.MediaServers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (server is null)
        {
            return Failure.NotFound("Media server");
        }

        return server.Kind == MediaServerKind.Plex
            ? Result<MediaServer>.Ok(server)
            : Failure.Invalid("Not a Plex server", "Tautulli only follows Plex: pick the Plex server its history comes from.");
    }

    private async Task<Context> ContextAsync(MediaServer server, CancellationToken ct)
    {
        var films = new FilmIndex(await db.Movies.AsNoTracking()
            .Select(m => new FilmIndex.Film(m.Id, m.TmdbId, m.ImdbId, m.Title, m.OriginalTitle, m.Year, m.AlternativeTitles))
            .ToListAsync(ct));

        var imported = new Dictionary<long, int>();
        var keys = await db.PlayStreams.AsNoTracking()
            .Where(s => s.ExternalKey != null && s.ExternalKey.StartsWith(KeyPrefix) && s.Play.User.MediaServerId == server.Id)
            .Select(s => new { s.ExternalKey, s.PlayId })
            .ToListAsync(ct);
        foreach (var key in keys)
        {
            if (long.TryParse(key.ExternalKey!.AsSpan(KeyPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                imported[id] = key.PlayId;
            }
        }

        var live = await db.Plays.AsNoTracking()
            .Where(p => p.Source == PlaySource.Live && p.User.MediaServerId == server.Id)
            .Select(p => new { p.User.ExternalId, p.Item.RatingKey, p.StartedAt, p.StoppedAt })
            .ToListAsync(ct);
        return new Context(
            films,
            imported,
            live.GroupBy(p => (p.ExternalId, p.RatingKey)).ToDictionary(g => g.Key, g => g.Select(p => (p.StartedAt, p.StoppedAt)).ToList()));
    }

    /// <summary>Each rating key's kind, ids and film, from what Tautulli alone knows.</summary>
    private static Dictionary<string, ItemPlan> Plan(Snapshot snapshot, FilmIndex films)
    {
        var plans = new Dictionary<string, ItemPlan>(StringComparer.Ordinal);
        foreach (var item in snapshot.Items.Values)
        {
            var plan = new ItemPlan(item)
            {
                Kind = item.Live ? null : KindOf(item.MediaType),
                DurationMs = item.DurationMs,
                CreditsStartMs = item.CreditsStartMs,
                SectionId = item.SectionId,
            };
            plans[item.RatingKey] = plan;
            if (plan.Kind == MediaItemKind.Movie)
            {
                // The newest guid names the item as Plex has it now; Tautulli's lookup comes next.
                var ids = PlexPlayback.IdsOf([item.Guid]);
                var lookup = snapshot.Lookups.GetValueOrDefault(item.RatingKey);
                plan.TmdbId = ids.TmdbId ?? lookup?.TmdbId;
                plan.ImdbId = ids.ImdbId ?? lookup?.ImdbId;
                plan.NeedsPlex = plan.TmdbId is null && plan.ImdbId is null && IsPlexGuid(item.Guid);
                plan.MovieId = films.Find(plan);
            }
            else if (plan.Kind == MediaItemKind.Episode)
            {
                // Legacy episode guids (com.plexapp.agents.thetvdb://<show>/<season>/<episode>) name the show.
                var ids = PlexPlayback.IdsOf(item.Guids.Where(g => !IsPlexGuid(g)));
                var lookup = item.GrandparentRatingKey is { } show ? snapshot.Lookups.GetValueOrDefault(show) : null;
                plan.ShowTvdbId = lookup?.TvdbId ?? ids.TvdbId;
                plan.ShowTmdbId = lookup?.TmdbId ?? ids.TmdbId;
                plan.ShowImdbId = lookup?.ImdbId ?? ids.ImdbId;
                plan.NeedsPlex = plan.ShowTvdbId is null && plan.ShowTmdbId is null;
            }
        }

        return plans;
    }

    private static MediaItemKind? KindOf(string? mediaType) => mediaType switch
    {
        "movie" => MediaItemKind.Movie,
        "episode" => MediaItemKind.Episode,
        _ => null,
    };

    private static bool IsPlexGuid(string? guid) => guid?.StartsWith("plex://", StringComparison.Ordinal) == true;

    /// <summary>The rows of one play together: same <c>reference_id</c>, same user, same item.</summary>
    private static IEnumerable<List<TautulliRow>> Groups(IEnumerable<TautulliRow> rows)
    {
        List<TautulliRow>? group = null;
        foreach (var row in rows)
        {
            if (group is not null && (group[0].GroupId != row.GroupId || group[0].UserId != row.UserId || group[0].RatingKey != row.RatingKey))
            {
                yield return group;
                group = null;
            }

            (group ??= []).Add(row);
        }

        if (group is not null)
        {
            yield return group;
        }
    }

    /// <summary>What the Tautulli database holds that the import needs before walking its rows.</summary>
    private sealed record Snapshot(List<TautulliUser> Users, Dictionary<long, string?> HistoryUsers, Dictionary<string, TautulliItem> Items, Dictionary<string, TautulliLookup> Lookups)
    {
        public static Snapshot Read(TautulliDatabase source)
        {
            var items = source.Items();
            foreach (var bare in source.ItemsWithoutMetadata())
            {
                items.TryAdd(bare.RatingKey, bare);
            }

            return new Snapshot(source.Users(), source.HistoryUsers(), items, source.Lookups());
        }

        /// <summary>Each Tautulli account id → the Plex account id the live sessions use. The owner is 1 there.</summary>
        public Dictionary<long, string> UserIds()
        {
            var ids = new Dictionary<long, string>();
            foreach (var user in Users)
            {
                ids[user.UserId] = user.IsAdmin ? OwnerExternalId : user.UserId.ToString(CultureInfo.InvariantCulture);
            }

            foreach (var id in HistoryUsers.Keys)
            {
                ids.TryAdd(id, id.ToString(CultureInfo.InvariantCulture));
            }

            return ids;
        }
    }

    private sealed record Context(
        FilmIndex Films,
        Dictionary<long, int> Imported,
        Dictionary<(string ExternalId, string RatingKey), List<(DateTime StartedAt, DateTime StoppedAt)>> Live)
    {
        public bool OverlapsLive(string externalId, string ratingKey, DateTime startedAt, DateTime stoppedAt) =>
            Live.TryGetValue((externalId, ratingKey), out var plays) && plays.Any(p => p.StartedAt < stoppedAt && p.StoppedAt > startedAt);
    }

    private sealed class ItemPlan(TautulliItem source)
    {
        public TautulliItem Source { get; } = source;

        public string RatingKey => Source.RatingKey;

        public string Title => Source.Title;

        public int? Year => Source.Year;

        /// <summary>Null for what the import leaves out: music, photos, Live TV.</summary>
        public MediaItemKind? Kind { get; init; }

        public int? TmdbId { get; set; }

        public string? ImdbId { get; set; }

        public int? TvdbId { get; set; }

        public string? ShowGuid { get; set; }

        /// <summary>Tautulli's, or Plex's when it answers.</summary>
        public string? ShowTitle { get; set; } = source.GrandparentTitle;

        public int? ShowTmdbId { get; set; }

        public string? ShowImdbId { get; set; }

        public int? ShowTvdbId { get; set; }

        /// <summary>Only Plex can tell the ids: a Plex guid, no lookup.</summary>
        public bool NeedsPlex { get; set; }

        public int? MovieId { get; set; }

        public long? DurationMs { get; set; }

        public long? CreditsStartMs { get; set; }

        public string? SectionId { get; set; }

        public string? SectionTitle { get; set; }

        /// <summary>The Bigarr item, once written.</summary>
        public int? EntityId { get; set; }

        /// <summary>The version per played resolution, worked out once.</summary>
        public Dictionary<string, ResolvedVersion> Versions { get; } = new(StringComparer.Ordinal);

        /// <summary>Ids of the item's older guids (an agent switch leaves the old ones in older rows).</summary>
        public PlexIds OlderIds => PlexPlayback.IdsOf(Source.Guids.Skip(1));
    }

    /// <summary>Bigarr's films by TMDB id, IMDb id, and by title and year (any of its titles, when it names one film only).</summary>
    private sealed class FilmIndex
    {
        private readonly Dictionary<int, int> _byTmdb = [];
        private readonly Dictionary<string, int> _byImdb = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Title, int Year), int?> _byTitle = [];
        private readonly Dictionary<int, Film> _byId = [];

        public FilmIndex(List<Film> films)
        {
            foreach (var film in films)
            {
                _byId[film.Id] = film;
                _byTmdb.TryAdd(film.TmdbId, film.Id);
                if (film.ImdbId is { Length: > 0 } imdb)
                {
                    _byImdb.TryAdd(imdb, film.Id);
                }

                if (film.Year is not { } year)
                {
                    continue;
                }

                foreach (var title in film.AlternativeTitles.Select(a => a.Title).Append(film.Title).Append(film.OriginalTitle).Select(Normalize).OfType<string>().Distinct(StringComparer.Ordinal))
                {
                    _byTitle[(title, year)] = _byTitle.TryGetValue((title, year), out var other) && other != film.Id ? null : film.Id;
                }
            }
        }

        /// <summary>Bigarr's title of the film, shown instead of the title Tautulli saw.</summary>
        public string? Title(int? movieId) => movieId is { } id && _byId.TryGetValue(id, out var film) ? film.Title : null;

        public int? Year(int? movieId) => movieId is { } id && _byId.TryGetValue(id, out var film) ? film.Year : null;

        public int? Find(ItemPlan plan)
        {
            if (plan.TmdbId is { } tmdb && _byTmdb.TryGetValue(tmdb, out var byTmdb))
            {
                return byTmdb;
            }

            if (plan.ImdbId is { } imdb && _byImdb.TryGetValue(imdb, out var byImdb))
            {
                return byImdb;
            }

            var older = plan.OlderIds;
            if (older.TmdbId is { } oldTmdb && _byTmdb.TryGetValue(oldTmdb, out var byOldTmdb))
            {
                return byOldTmdb;
            }

            if (older.ImdbId is { } oldImdb && _byImdb.TryGetValue(oldImdb, out var byOldImdb))
            {
                return byOldImdb;
            }

            return plan.Year is { } year && Normalize(plan.Title) is { } title && _byTitle.TryGetValue((title, year), out var byTitle) ? byTitle : null;
        }

        /// <summary>Lowercase letters and digits only, accents dropped: "Guy Roux: Une histoire" and "Guy Roux, une histoire" agree.</summary>
        public static string? Normalize(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var builder = new StringBuilder(title.Length);
            foreach (var c in title.Normalize(NormalizationForm.FormD))
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.Length > 0 ? builder.ToString() : null;
        }

        public sealed record Film(int Id, int TmdbId, string? ImdbId, string Title, string? OriginalTitle, int? Year, List<Domain.Movies.AlternativeTitle> AlternativeTitles);
    }
}
