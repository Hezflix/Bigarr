namespace Bigarr.Domain.Tv;

/// <summary>Which order a series uses: the order itself and, for a TMDB episode group, which one.</summary>
public sealed record OrderChoice(EpisodeOrder Ordering, string? EpisodeGroupId = null, string? EpisodeGroupName = null, EpisodeGroupType? EpisodeGroupType = null)
{
    public static readonly OrderChoice Tvdb = new(EpisodeOrder.Tvdb);

    public static OrderChoice Of(Series series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return new OrderChoice(series.Ordering, series.EpisodeGroupId, series.EpisodeGroupName, series.EpisodeGroupType);
    }

    public bool SameAs(OrderChoice other) =>
        other is not null && Ordering == other.Ordering && (Ordering != EpisodeOrder.TmdbEpisodeGroup || EpisodeGroupId == other.EpisodeGroupId);

    public void ApplyTo(Series series)
    {
        ArgumentNullException.ThrowIfNull(series);
        series.Ordering = Ordering;
        series.EpisodeGroupId = Ordering == EpisodeOrder.TmdbEpisodeGroup ? EpisodeGroupId : null;
        series.EpisodeGroupName = Ordering == EpisodeOrder.TmdbEpisodeGroup ? EpisodeGroupName : null;
        series.EpisodeGroupType = Ordering == EpisodeOrder.TmdbEpisodeGroup ? EpisodeGroupType : null;
    }
}

/// <summary>An episode's numbers in the alternative order before a change, to put back on undo.</summary>
public sealed record OrderNumbersSnapshot(int EpisodeId, int? SeasonNumber, int? EpisodeNumber, int? AbsoluteNumber);

/// <summary>One file (or extra) the change moved: undo moves it back.</summary>
public sealed record OrderFileMove(int EpisodeFileId, int VersionId, string From, string To, List<OrderExtraMove> Extras);

public sealed record OrderExtraMove(int ExtraFileId, string From, string To);

/// <summary>
/// Journal of one order switch of a series (blueprint §8): what the order and the episodes' alternative numbers were,
/// and every file moved on disk. Only the latest change of a series can be undone, until the next change.
/// </summary>
public sealed class SeriesOrderChange
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series Series { get; set; } = null!;

    public required OrderChoice From { get; set; }

    public required OrderChoice To { get; set; }

    public DateTime AppliedAt { get; set; }

    public DateTime? UndoneAt { get; set; }

    public List<OrderNumbersSnapshot> PreviousNumbers { get; set; } = [];

    public List<OrderOnlyEpisode> PreviousOrderOnly { get; set; } = [];

    public List<OrderFileMove> Moves { get; set; } = [];

    /// <summary>Files that could not be placed in the new order (or whose move failed): left where they were.</summary>
    public int UnmatchedFiles { get; set; }
}
