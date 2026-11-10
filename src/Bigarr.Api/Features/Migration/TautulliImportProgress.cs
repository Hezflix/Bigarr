namespace Bigarr.Api.Features.Migration;

/// <summary>Mutable, in-memory progress of one Tautulli import. Read by the API while the import writes it.</summary>
public sealed class TautulliImportProgress(TautulliImportRequest request)
{
    public const int MaxIssues = 500;

    private readonly Lock _gate = new();
    private readonly List<TautulliImportIssue> _issues = [];
    private int _processed;
    private int _usersAdded;
    private int _itemsAdded;
    private int _playsAdded;
    private int _streamsAdded;
    private int _playsExtended;
    private int _alreadyImported;
    private int _overlappingLive;
    private int _otherSkipped;
    private int _moviePlays;
    private int _moviePlaysWithFilm;
    private int _moviePlaysWithVersion;
    private int _plexAsked;
    private int _plexResolved;

    public TautulliImportRequest Request { get; } = request;

    public RadarrImportState State { get; set; } = RadarrImportState.Queued;

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public int Total { get; set; }

    public string? Current { get; set; }

    public string? Error { get; set; }

    public bool UploadDeleted { get; set; }

    public int Processed => _processed;

    public int PlaysAdded => _playsAdded;

    public int StreamsAdded => _streamsAdded;

    public void GroupsDone(int count) => Interlocked.Add(ref _processed, count);

    public void UserAdded() => Interlocked.Increment(ref _usersAdded);

    public void ItemAdded() => Interlocked.Increment(ref _itemsAdded);

    public void PlayAdded() => Interlocked.Increment(ref _playsAdded);

    public void StreamsAddedBy(int count) => Interlocked.Add(ref _streamsAdded, count);

    public void PlayExtended() => Interlocked.Increment(ref _playsExtended);

    public void AlreadyImported() => Interlocked.Increment(ref _alreadyImported);

    public void OverlappingLive() => Interlocked.Increment(ref _overlappingLive);

    public void OtherSkipped() => Interlocked.Increment(ref _otherSkipped);

    public void MoviePlay(bool withFilm, bool withVersion)
    {
        Interlocked.Increment(ref _moviePlays);
        if (withFilm)
        {
            Interlocked.Increment(ref _moviePlaysWithFilm);
        }

        if (withVersion)
        {
            Interlocked.Increment(ref _moviePlaysWithVersion);
        }
    }

    public void PlexAsked(bool resolved)
    {
        Interlocked.Increment(ref _plexAsked);
        if (resolved)
        {
            Interlocked.Increment(ref _plexResolved);
        }
    }

    public void Issue(string title, int? year, string reason, int plays)
    {
        lock (_gate)
        {
            if (_issues.Count < MaxIssues)
            {
                _issues.Add(new TautulliImportIssue(title, year, reason, plays));
            }
        }
    }

    public TautulliImportStatusResponse ToResponse()
    {
        List<TautulliImportIssue> issues;
        lock (_gate)
        {
            issues = [.. _issues];
        }

        return new TautulliImportStatusResponse(
            State, StartedAt, FinishedAt, Total, _processed, _usersAdded, _itemsAdded, _playsAdded, _streamsAdded, _playsExtended,
            _alreadyImported, _overlappingLive, _otherSkipped, _moviePlays, _moviePlaysWithFilm, _moviePlaysWithVersion, _plexAsked, _plexResolved,
            Current, Error, issues, UploadDeleted);
    }

    public static TautulliImportStatusResponse Idle { get; } =
        new(RadarrImportState.Idle, null, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null, [], false);
}
