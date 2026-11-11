namespace Bigarr.Api.Features.Series;

using Bigarr.Domain.Tv;

/// <summary>The order to switch to: TVDB, TMDB's default order, or one TMDB episode group (by id).</summary>
public sealed record OrderChoiceRequest(EpisodeOrder Ordering, string? EpisodeGroupId = null);

public sealed record OrderChoiceResponse(EpisodeOrder Ordering, string? EpisodeGroupId, string? EpisodeGroupName, EpisodeGroupType? EpisodeGroupType)
{
    public static OrderChoiceResponse From(OrderChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return new OrderChoiceResponse(choice.Ordering, choice.EpisodeGroupId, choice.EpisodeGroupName, choice.EpisodeGroupType);
    }
}

public sealed record EpisodeGroupResponse(string Id, string Name, EpisodeGroupType Type, int GroupCount, int EpisodeCount);

/// <param name="CanUndo">The latest change of the series, not undone yet: undo is offered until the next change.</param>
public sealed record OrderChangeResponse(
    int Id,
    OrderChoiceResponse From,
    OrderChoiceResponse To,
    DateTime AppliedAt,
    DateTime? UndoneAt,
    bool CanUndo,
    int FilesMoved,
    int UnmatchedFiles);

/// <param name="Groups">TMDB episode groups of the show (empty without TMDB).</param>
/// <param name="Unavailable">Why TMDB orders cannot be offered (no TMDB id, provider down).</param>
public sealed record SeriesOrdersResponse(int SeriesId, OrderChoiceResponse Current, List<EpisodeGroupResponse> Groups, OrderChangeResponse? LastChange, string? Unavailable);

public enum OrderFileStatus
{
    /// <summary>Renamed or moved to the new order's path on apply.</summary>
    Move = 1,

    /// <summary>Already at its path in the new order.</summary>
    Unchanged = 2,

    /// <summary>One of its episodes has no counterpart in the new order: left where it is, flagged.</summary>
    Unmatched = 3,

    /// <summary>Another file is (or would be) at the new path: left where it is.</summary>
    Conflict = 4,
}

/// <param name="Tvdb">The episode's TVDB numbers (its stable identity).</param>
/// <param name="From">Numbers shown before the change.</param>
/// <param name="To">Numbers shown after; null when the new order has no counterpart (the TVDB numbers stay shown).</param>
public sealed record OrderEpisodeChange(int EpisodeId, string Title, string Tvdb, string From, string? To);

public sealed record OrderFilePreview(int EpisodeFileId, int VersionId, string VersionName, List<string> Episodes, string From, string To, OrderFileStatus Status, string? Detail);

/// <param name="LargeChange">Many files move: the UI asks for a stronger confirmation.</param>
public sealed record OrderPreviewResponse(
    int SeriesId,
    OrderChoiceResponse From,
    OrderChoiceResponse To,
    int Episodes,
    int EpisodesMoving,
    int EpisodesNotInOrder,
    int Files,
    int FilesMoving,
    int FilesUnchanged,
    int FilesUnmatched,
    int FilesConflicting,
    bool LargeChange,
    List<OrderEpisodeChange> EpisodeChanges,
    List<OrderFilePreview> FileChanges,
    List<OrderOnlyEpisode> OrderOnly);

public sealed record OrderFileResult(int EpisodeFileId, string From, string To, bool Moved, string? Error);

public sealed record OrderApplyResponse(OrderChangeResponse Change, List<OrderFileResult> Files);

public sealed record OrderUndoResponse(OrderChangeResponse Change, int FilesRestored, List<OrderFileResult> Files);

/// <summary>The manual fix of a file the order could not place: the episodes it holds, optionally renamed after them.</summary>
public sealed record ReassignEpisodeFileRequest(List<int>? EpisodeIds, bool Rename = true);
