namespace Bigarr.Api.Features.Series;

using System.Globalization;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.Tv;
using Bigarr.Domain.History;
using Bigarr.Domain.Library;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// The per-show order (blueprint §8, S6): which numbering names and shows a series' episodes. Switching is a preview
/// (every file's current → new path, episodes that move, what cannot be placed), then an apply that renames the files
/// on disk through the S5 rename (moves within the version folder, never a delete, hardlinks kept), journaled so the
/// latest change can be undone until the next one. An episode's identity and its TVDB numbers never change: files,
/// history, monitoring, search and the Sonarr follow keep working on TVDB numbers.
/// </summary>
public sealed class SeriesOrderService(
    BigarrDbContext db,
    ISeriesMetadataProvider metadata,
    SeriesRenameService renamer,
    MediaManagementService settings,
    TimeProvider time,
    ILogger<SeriesOrderService> logger)
{
    /// <summary>From this many moving files (or half the files) the UI asks for a stronger confirmation.</summary>
    public const int LargeChangeFiles = 25;

    public async Task<Result<SeriesOrdersResponse>> OrdersAsync(int seriesId, CancellationToken ct)
    {
        var series = await db.Series.AsNoTracking().FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series is null)
        {
            return Failure.NotFound("Series");
        }

        var groups = new List<EpisodeGroupResponse>();
        string? unavailable = null;
        if (series.TmdbId is not { } tmdbId)
        {
            unavailable = "This series has no TMDB id: only the TVDB order is available.";
        }
        else
        {
            try
            {
                groups = (await metadata.GetEpisodeGroupsAsync(tmdbId, ct))
                    .Select(g => new EpisodeGroupResponse(g.Id, g.Name, g.Type, g.GroupCount, g.EpisodeCount))
                    .OrderBy(g => g.Type).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                logger.LogWarning(ex, "TMDB episode groups of {Series} unavailable", series.Title);
                unavailable = "TMDB is unreachable: its episode groups cannot be listed right now.";
            }
        }

        return Result<SeriesOrdersResponse>.Ok(new SeriesOrdersResponse(series.Id, OrderChoiceResponse.From(OrderChoice.Of(series)), groups, await LastChangeAsync(seriesId, ct), unavailable));
    }

    public async Task<OrderChangeResponse?> LastChangeAsync(int seriesId, CancellationToken ct)
    {
        var last = await db.SeriesOrderChanges.AsNoTracking().Where(c => c.SeriesId == seriesId).OrderByDescending(c => c.Id).FirstOrDefaultAsync(ct);
        return last is null ? null : Describe(last, latest: true);
    }

    public async Task<Result<OrderPreviewResponse>> PreviewAsync(int seriesId, OrderChoiceRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var series = await LoadAsync(seriesId, tracking: false, ct);
        if (series is null)
        {
            return Failure.NotFound("Series");
        }

        var plan = await PlanAsync(series, request, ct);
        return plan.IsSuccess ? Result<OrderPreviewResponse>.Ok(plan.Value!.Preview) : plan.Failure!;
    }

    /// <summary>Switches the order: numbers, then the moves, journaled. Files the new order cannot place stay where they are.</summary>
    public async Task<Result<OrderApplyResponse>> ApplyAsync(int seriesId, OrderChoiceRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var turn = await SeriesFileGate.EnterAsync(seriesId, ct);
        var series = await LoadAsync(seriesId, tracking: true, ct);
        if (series is null)
        {
            return Failure.NotFound("Series");
        }

        var planned = await PlanAsync(series, request, ct);
        if (!planned.IsSuccess)
        {
            return planned.Failure!;
        }

        var plan = planned.Value!;
        if (plan.From.SameAs(plan.To))
        {
            return Failure.Invalid("Already in this order", "Pick another order.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var change = new SeriesOrderChange
        {
            SeriesId = series.Id,
            From = plan.From,
            To = plan.To,
            AppliedAt = now,
            PreviousNumbers = plan.PreviousNumbers,
            PreviousOrderOnly = plan.PreviousOrderOnly,
        };

        // The plan already set the new numbers and order on the tracked graph; the moves follow it.
        var results = new List<OrderFileResult>();
        foreach (var move in plan.Moves)
        {
            var extrasBefore = move.File.Extras.ToDictionary(e => e.Id, e => e.RelativePath);
            var from = move.File.RelativePath;
            var outcome = renamer.Move(series, move.Version, move.File, move.To, now);
            results.Add(new OrderFileResult(move.File.Id, from, move.To, outcome.Renamed, outcome.Error));
            if (!outcome.Renamed)
            {
                change.UnmatchedFiles++;
                continue;
            }

            change.Moves.Add(new OrderFileMove(
                move.File.Id,
                move.Version.Id,
                from,
                move.File.RelativePath,
                move.File.Extras.Where(e => extrasBefore.TryGetValue(e.Id, out var before) && before != e.RelativePath)
                    .Select(e => new OrderExtraMove(e.Id, extrasBefore[e.Id], e.RelativePath))
                    .ToList()));
        }

        change.UnmatchedFiles += plan.Preview.FilesUnmatched + plan.Preview.FilesConflicting;
        db.SeriesOrderChanges.Add(change);
        renamer.PublishRenamed();

        // The files have moved: the new paths and the journal that can undo them are written whatever the request says
        // by now. A caller that went away must not leave the disk renamed and the database on the old names.
        await db.SaveChangesAsync(CancellationToken.None);
        foreach (var version in series.Versions)
        {
            SeriesRenameService.RemoveEmptySeasonFolders(SeriesRenameService.Folder(version));
        }

        logger.LogInformation(
            "Order of {Series}: {From} → {To}; {Moved} file(s) moved, {Unmatched} left in place, {Moving} episode(s) renumbered",
            series.Title, Label(plan.From), Label(plan.To), change.Moves.Count, change.UnmatchedFiles, plan.Preview.EpisodesMoving);
        return Result<OrderApplyResponse>.Ok(new OrderApplyResponse(Describe(change, latest: true), results));
    }

    /// <summary>Puts back the order, the numbers and every file the latest change moved (when it is still where the change put it).</summary>
    public async Task<Result<OrderUndoResponse>> UndoAsync(int seriesId, CancellationToken ct)
    {
        using var turn = await SeriesFileGate.EnterAsync(seriesId, ct);
        var change = await db.SeriesOrderChanges.Where(c => c.SeriesId == seriesId).OrderByDescending(c => c.Id).FirstOrDefaultAsync(ct);
        if (change is null || change.UndoneAt is not null)
        {
            return Failure.Conflict("Nothing to undo", "The latest order change of this series is already undone, or there is none.");
        }

        var series = await LoadAsync(seriesId, tracking: true, ct);
        if (series is null)
        {
            return Failure.NotFound("Series");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var files = series.Versions.SelectMany(v => v.Files.Select(f => (Version: v, File: f))).ToDictionary(x => x.File.Id);
        var results = new List<OrderFileResult>();
        var restored = 0;
        foreach (var move in Enumerable.Reverse(change.Moves))
        {
            if (!files.TryGetValue(move.EpisodeFileId, out var entry))
            {
                results.Add(new OrderFileResult(move.EpisodeFileId, move.To, move.From, false, "The file is no longer in the library."));
                continue;
            }

            var (version, file) = entry;
            if (!string.Equals(file.RelativePath, move.To, StringComparison.Ordinal))
            {
                results.Add(new OrderFileResult(file.Id, file.RelativePath, move.From, false, "The file was moved since the change: left where it is."));
                continue;
            }

            var folder = SeriesRenameService.Folder(version);
            var error = MoveOnDisk(Path.Combine(folder, move.To), Path.Combine(folder, move.From));
            if (error is not null)
            {
                results.Add(new OrderFileResult(file.Id, move.To, move.From, false, error));
                continue;
            }

            file.RelativePath = move.From;
            renamer.Record(series, version, file, move.To);
            foreach (var extraMove in move.Extras)
            {
                var extra = file.Extras.FirstOrDefault(e => e.Id == extraMove.ExtraFileId);
                if (extra is null || extra.RelativePath != extraMove.To)
                {
                    continue;
                }

                if (MoveOnDisk(Path.Combine(folder, extraMove.To), Path.Combine(folder, extraMove.From)) is { } extraError)
                {
                    logger.LogWarning("Undo of the order of {Series}: extra {Path} left in place: {Error}", series.Title, extraMove.To, extraError);
                    continue;
                }

                extra.RelativePath = extraMove.From;
            }

            foreach (var target in file.Episodes)
            {
                var line = GrabService.SeriesHistory(version, target, HistoryEventType.Renamed, file.OriginalReleaseTitle, null, null, now);
                line.Data["from"] = Path.Combine(folder, move.To);
                line.Data["to"] = Path.Combine(folder, move.From);
                line.Data["episodeFileId"] = file.Id.ToString(CultureInfo.InvariantCulture);
                line.Data["reason"] = "orderUndo";
                db.History.Add(line);
            }

            restored++;
            results.Add(new OrderFileResult(file.Id, move.To, move.From, true, null));
        }

        var previous = change.PreviousNumbers.ToDictionary(n => n.EpisodeId);
        foreach (var episode in series.Episodes)
        {
            var numbers = previous.GetValueOrDefault(episode.Id);
            episode.OrderSeasonNumber = numbers?.SeasonNumber;
            episode.OrderEpisodeNumber = numbers?.EpisodeNumber;
            episode.OrderAbsoluteNumber = numbers?.AbsoluteNumber;
        }

        change.From.ApplyTo(series);
        series.OrderOnlyEpisodes = [.. change.PreviousOrderOnly];
        change.UndoneAt = now;
        renamer.PublishRenamed();

        // As for the apply: the files are back, the rows follow whatever the request says by now.
        await db.SaveChangesAsync(CancellationToken.None);
        foreach (var version in series.Versions)
        {
            SeriesRenameService.RemoveEmptySeasonFolders(SeriesRenameService.Folder(version));
        }

        logger.LogInformation("Order of {Series} back to {From}: {Restored}/{Moves} file(s) restored", series.Title, Label(change.From), restored, change.Moves.Count);
        return Result<OrderUndoResponse>.Ok(new OrderUndoResponse(Describe(change, latest: true), restored, results));
    }

    /// <summary>Re-reads the series' alternative order from the provider (refresh): numbers kept current, files not touched.</summary>
    public static async Task RelinkAsync(ISeriesMetadataProvider metadata, ILogger logger, Series series, IReadOnlyCollection<Episode> episodes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(episodes);
        if (series.Ordering == EpisodeOrder.Tvdb)
        {
            EpisodeOrderLinker.Apply(series, episodes, null);
            return;
        }

        if (series.TmdbId is not { } tmdbId)
        {
            return;
        }

        try
        {
            var order = await metadata.GetOrderEpisodesAsync(tmdbId, OrderChoice.Of(series), ct);
            if (order is null)
            {
                logger.LogWarning("{Series}: its order ({Order}) is gone from TMDB; the numbers stay as they were", series.Title, Label(OrderChoice.Of(series)));
                return;
            }

            EpisodeOrderLinker.Apply(series, episodes, EpisodeOrderLinker.Link(episodes, order));
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "{Series}: TMDB unreachable, its order numbers stay as they were", series.Title);
        }
    }

    /// <summary>
    /// The manual fix of a file the order could not place (or any misattached file): it now holds
    /// <paramref name="request"/>'s episodes, which must have no other file in that version; optionally renamed after them.
    /// </summary>
    public async Task<Result<OrderFileResult>> ReassignFileAsync(int seriesId, int fileId, ReassignEpisodeFileRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EpisodeIds is not { Count: > 0 })
        {
            return Failure.Invalid("No episode", "Choose the episodes this file holds.");
        }

        var series = await LoadAsync(seriesId, tracking: true, ct);
        var entry = series?.Versions.SelectMany(v => v.Files.Select(f => (Version: v, File: f))).FirstOrDefault(x => x.File.Id == fileId);
        if (series is null || entry is null || entry.Value.File is null)
        {
            return Failure.NotFound("Episode file");
        }

        var (version, file) = entry.Value;
        var ids = request.EpisodeIds.Distinct().ToList();
        var targets = await db.EpisodeTargets.Where(t => t.VersionId == version.Id && ids.Contains(t.EpisodeId)).ToListAsync(ct);
        if (targets.Count != ids.Count)
        {
            return Failure.Invalid("Unknown episodes", "Some episodes are not episodes of this series version.");
        }

        var taken = targets.Where(t => t.EpisodeFileId is not null && t.EpisodeFileId != file.Id).ToList();
        if (taken.Count > 0)
        {
            return Failure.Conflict("Episode already has a file", string.Join(", ", taken.Select(t => GrabService.EpisodeLabel(series.Episodes.First(e => e.Id == t.EpisodeId)))) + " already has a file. Delete it first.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        foreach (var old in file.Episodes.Where(t => !ids.Contains(t.EpisodeId)).ToList())
        {
            old.EpisodeFileId = null;
            old.EpisodeFile = null;
        }

        foreach (var target in targets)
        {
            target.EpisodeFile = file;
            target.Episode = series.Episodes.First(e => e.Id == target.EpisodeId);
            target.Version = version;
        }

        file.SeasonNumber = targets.Min(t => t.Episode.SeasonNumber);
        await db.SaveChangesAsync(ct);

        var from = file.RelativePath;
        var result = new OrderFileResult(file.Id, from, from, false, null);
        if (request.Rename)
        {
            var config = await settings.GetAsync(ct);
            var target = await renamer.TargetPathAsync(config, series, version, file, ct);
            var moved = renamer.Move(series, version, file, target, now);
            renamer.PublishRenamed();
            await db.SaveChangesAsync(ct);
            SeriesRenameService.RemoveEmptySeasonFolders(SeriesRenameService.Folder(version));
            result = new OrderFileResult(file.Id, from, file.RelativePath, moved.Renamed, moved.Error);
        }

        logger.LogInformation("{Series} [{Version}]: {Path} now holds {Episodes}", series.Title, version.Name, file.RelativePath, string.Join(", ", targets.Select(t => GrabService.EpisodeLabel(t.Episode))));
        return Result<OrderFileResult>.Ok(result);
    }

    // ---- planning ---------------------------------------------------------------------------------------------------

    private sealed record PlannedMove(SeriesVersion Version, EpisodeFile File, string To);

    private sealed record OrderPlan(
        OrderChoice From,
        OrderChoice To,
        OrderPreviewResponse Preview,
        List<PlannedMove> Moves,
        List<OrderNumbersSnapshot> PreviousNumbers,
        List<OrderOnlyEpisode> PreviousOrderOnly);

    /// <summary>
    /// Links the episodes to <paramref name="request"/>'s order and works out every file's path in it. Sets the new
    /// numbers and order on <paramref name="series"/> (persisted only when the caller tracks and saves it).
    /// </summary>
    private async Task<Result<OrderPlan>> PlanAsync(Series series, OrderChoiceRequest request, CancellationToken ct)
    {
        var to = await ResolveAsync(series, request, ct);
        if (!to.IsSuccess)
        {
            return to.Failure!;
        }

        var choice = to.Value!;
        var episodes = series.Episodes.ToList();
        OrderLink? link = null;
        if (choice.Ordering != EpisodeOrder.Tvdb)
        {
            IReadOnlyList<OrderEpisode>? order;
            try
            {
                order = await metadata.GetOrderEpisodesAsync(series.TmdbId!.Value, choice, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                logger.LogWarning(ex, "TMDB order {Order} of {Series} unavailable", Label(choice), series.Title);
                return new Failure(StatusCodes.Status502BadGateway, "Metadata provider unavailable", ex.Message);
            }

            if (order is null)
            {
                return Failure.NotFound($"{Label(choice)} on TMDB");
            }

            link = EpisodeOrderLinker.Link(episodes, order);
        }

        var from = OrderChoice.Of(series);
        var previousNumbers = episodes.Select(e => new OrderNumbersSnapshot(e.Id, e.OrderSeasonNumber, e.OrderEpisodeNumber, e.OrderAbsoluteNumber)).ToList();
        var previousOrderOnly = series.OrderOnlyEpisodes.ToList();
        var before = episodes.ToDictionary(e => e.Id, e => Numbers(e, from.Ordering));

        EpisodeOrderLinker.Apply(series, episodes, link);
        choice.ApplyTo(series);

        var episodeChanges = new List<OrderEpisodeChange>();
        foreach (var episode in episodes.OrderBy(e => e.SeasonNumber == 0 ? int.MaxValue : e.SeasonNumber).ThenBy(e => e.EpisodeNumber))
        {
            var after = choice.Ordering == EpisodeOrder.Tvdb || episode.InOrder ? Numbers(episode, choice.Ordering) : null;
            if (after != before[episode.Id])
            {
                episodeChanges.Add(new OrderEpisodeChange(episode.Id, episode.Title, Label(episode.SeasonNumber, episode.EpisodeNumber), before[episode.Id], after));
            }
        }

        var config = await settings.GetAsync(ct);
        var previews = new List<OrderFilePreview>();
        var moves = new List<PlannedMove>();
        foreach (var version in series.Versions.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id))
        {
            var folder = SeriesRenameService.Folder(version);
            var candidates = new List<(EpisodeFile File, string To, List<string> Labels)>();
            foreach (var file in version.Files.Where(f => f.Episodes.Count > 0).OrderBy(f => f.SeasonNumber).ThenBy(f => f.Episodes.Min(t => t.Episode.EpisodeNumber)))
            {
                var held = file.Episodes.Select(t => t.Episode).OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList();
                var labels = held.Select(e => Numbers(e, choice.Ordering)).ToList();
                if (choice.Ordering != EpisodeOrder.Tvdb && held.Any(e => !e.InOrder))
                {
                    var missing = held.Where(e => !e.InOrder).Select(e => Label(e.SeasonNumber, e.EpisodeNumber));
                    previews.Add(new OrderFilePreview(file.Id, version.Id, version.Name, labels, file.RelativePath, file.RelativePath, OrderFileStatus.Unmatched,
                        $"Not in {Label(choice)}: {string.Join(", ", missing)} (TVDB). The file stays where it is."));
                    continue;
                }

                var target = await renamer.TargetPathAsync(config, series, version, file, ct, rename: config.RenameEpisodes);
                if (string.Equals(target, file.RelativePath, StringComparison.Ordinal))
                {
                    previews.Add(new OrderFilePreview(file.Id, version.Id, version.Name, labels, file.RelativePath, target, OrderFileStatus.Unchanged, null));
                    continue;
                }

                candidates.Add((file, target, labels));
            }

            // Moves run one after the other: a file whose new path is another moving file's current one waits for it;
            // two files wanting one path, a path held on disk by something else, or a cycle are conflicts.
            var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
            var duplicates = candidates.GroupBy(c => c.To, comparer).Where(g => g.Count() > 1).SelectMany(g => g).Select(c => c.File.Id).ToHashSet();
            var pending = candidates.Where(c => !duplicates.Contains(c.File.Id)).ToList();
            foreach (var conflict in candidates.Where(c => duplicates.Contains(c.File.Id)))
            {
                previews.Add(new OrderFilePreview(conflict.File.Id, version.Id, version.Name, conflict.Labels, conflict.File.RelativePath, conflict.To, OrderFileStatus.Conflict, "Another file of this version would get the same name."));
            }

            bool progress;
            do
            {
                progress = false;
                foreach (var candidate in pending.ToList())
                {
                    if (pending.Any(o => o.File.Id != candidate.File.Id && comparer.Equals(o.File.RelativePath, candidate.To)))
                    {
                        continue; // its new path is still another moving file's: after that one
                    }

                    // Paths are still the current ones while planning: a planned move frees its file's path.
                    var full = Path.Combine(folder, candidate.To);
                    var vacated = moves.Any(m => m.Version.Id == version.Id && comparer.Equals(m.File.RelativePath, candidate.To));
                    if (File.Exists(full) && !vacated && !SeriesImportService.PathsEqual(full, Path.Combine(folder, candidate.File.RelativePath)))
                    {
                        previews.Add(new OrderFilePreview(candidate.File.Id, version.Id, version.Name, candidate.Labels, candidate.File.RelativePath, candidate.To, OrderFileStatus.Conflict, $"{candidate.To} already exists."));
                    }
                    else
                    {
                        moves.Add(new PlannedMove(version, candidate.File, candidate.To));
                        previews.Add(new OrderFilePreview(candidate.File.Id, version.Id, version.Name, candidate.Labels, candidate.File.RelativePath, candidate.To, OrderFileStatus.Move, null));
                    }

                    pending.Remove(candidate);
                    progress = true;
                }
            }
            while (progress && pending.Count > 0);

            foreach (var cycle in pending)
            {
                previews.Add(new OrderFilePreview(cycle.File.Id, version.Id, version.Name, cycle.Labels, cycle.File.RelativePath, cycle.To, OrderFileStatus.Conflict, "Its new name is held by another file that waits for this one's."));
            }
        }

        var count = previews.Count;
        var moving = previews.Count(p => p.Status == OrderFileStatus.Move);
        var preview = new OrderPreviewResponse(
            series.Id,
            OrderChoiceResponse.From(from),
            OrderChoiceResponse.From(choice),
            episodes.Count,
            episodeChanges.Count(c => c.To is not null),
            choice.Ordering == EpisodeOrder.Tvdb ? 0 : episodes.Count(e => !e.InOrder),
            count,
            moving,
            previews.Count(p => p.Status == OrderFileStatus.Unchanged),
            previews.Count(p => p.Status == OrderFileStatus.Unmatched),
            previews.Count(p => p.Status == OrderFileStatus.Conflict),
            moving >= LargeChangeFiles || (count > 0 && moving * 2 >= count && moving >= 5),
            episodeChanges,
            previews.OrderBy(p => p.Status == OrderFileStatus.Unchanged ? 1 : 0).ThenBy(p => p.VersionId).ThenBy(p => p.From, StringComparer.Ordinal).ToList(),
            [.. series.OrderOnlyEpisodes]);
        return Result<OrderPlan>.Ok(new OrderPlan(from, choice, preview, moves, previousNumbers, previousOrderOnly));
    }

    /// <summary>The asked order, checked, with the group's name and type from TMDB's list.</summary>
    private async Task<Result<OrderChoice>> ResolveAsync(Series series, OrderChoiceRequest request, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.Ordering))
        {
            return Failure.Invalid("Unknown order");
        }

        if (request.Ordering == EpisodeOrder.Tvdb)
        {
            return Result<OrderChoice>.Ok(OrderChoice.Tvdb);
        }

        if (series.TmdbId is not { } tmdbId)
        {
            return Failure.Invalid("No TMDB id", "TMDB orders need the series' TMDB id; refresh the series first.");
        }

        if (request.Ordering == EpisodeOrder.TmdbDefault)
        {
            return Result<OrderChoice>.Ok(new OrderChoice(EpisodeOrder.TmdbDefault));
        }

        if (string.IsNullOrWhiteSpace(request.EpisodeGroupId))
        {
            return Failure.Invalid("No episode group", "Pick one of the show's TMDB episode groups.");
        }

        EpisodeGroupSummary? group;
        try
        {
            group = (await metadata.GetEpisodeGroupsAsync(tmdbId, ct)).FirstOrDefault(g => g.Id == request.EpisodeGroupId);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return new Failure(StatusCodes.Status502BadGateway, "Metadata provider unavailable", ex.Message);
        }

        if (group is null)
        {
            return Failure.NotFound($"Episode group {request.EpisodeGroupId} of this show");
        }

        return Result<OrderChoice>.Ok(new OrderChoice(EpisodeOrder.TmdbEpisodeGroup, group.Id, group.Name, group.Type));
    }

    private Task<Series?> LoadAsync(int seriesId, bool tracking, CancellationToken ct)
    {
        var query = db.Series
            .Include(s => s.Episodes)
            .Include(s => s.Versions).ThenInclude(v => v.RootFolder)
            .Include(s => s.Versions).ThenInclude(v => v.Files).ThenInclude(f => f.Quality)
            .Include(s => s.Versions).ThenInclude(v => v.Files).ThenInclude(f => f.Extras)
            .Include(s => s.Versions).ThenInclude(v => v.Files).ThenInclude(f => f.Episodes).ThenInclude(t => t.Episode)
            .AsSplitQuery();
        return (tracking ? query : query.AsNoTrackingWithIdentityResolution()).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
    }

    /// <summary>A plain move inside the version folder; never overwrites.</summary>
    private string? MoveOnDisk(string from, string to)
    {
        try
        {
            if (!File.Exists(from))
            {
                return "The file is missing on disk.";
            }

            if (File.Exists(to) && !SeriesImportService.PathsEqual(from, to))
            {
                return $"A file already exists at {to}.";
            }

            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Move(from, to);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Move of {From} failed", from);
            return ex.Message;
        }
    }

    private OrderChangeResponse Describe(SeriesOrderChange change, bool latest) =>
        new(change.Id, OrderChoiceResponse.From(change.From), OrderChoiceResponse.From(change.To), change.AppliedAt, change.UndoneAt, latest && change.UndoneAt is null, change.Moves.Count, change.UnmatchedFiles);

    private static string Numbers(Episode episode, EpisodeOrder ordering) => Label(episode.DisplaySeasonNumber(ordering), episode.DisplayEpisodeNumber(ordering));

    private static string Label(int season, int episode) => $"S{season:00}E{episode:00}";

    public static string Label(OrderChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return choice.Ordering switch
        {
            EpisodeOrder.Tvdb => "the TVDB order",
            EpisodeOrder.TmdbDefault => "TMDB's default order",
            _ => $"the TMDB episode group \"{choice.EpisodeGroupName ?? choice.EpisodeGroupId}\"",
        };
    }
}
