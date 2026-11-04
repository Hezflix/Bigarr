namespace Bigarr.Api.Features.Migration;

/// <summary>Mutable, in-memory progress of one Radarr import. Read by the API while the import writes it.</summary>
public sealed class RadarrImportProgress(RadarrImportRequest request)
{
    public const int MaxIssues = 500;

    private readonly Lock _gate = new();
    private readonly List<RadarrImportIssue> _issues = [];
    private int _processed;
    private int _moviesAdded;
    private int _versionsAdded;
    private int _versionsUpdated;
    private int _filesAdded;
    private int _filesMissing;
    private int _skipped;
    private int _failed;
    private int _tagsAdded;
    private int _exclusionsAdded;
    private int _historyAdded;
    private int _blocklistAdded;
    private int _customFormatsAdded;
    private int _customFormatsUpdated;
    private int _profilesScored;

    public RadarrImportRequest Request { get; } = request;

    public RadarrImportState State { get; set; } = RadarrImportState.Queued;

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public int Total { get; set; }

    public string? Current { get; set; }

    public string? Error { get; set; }

    public int Processed => _processed;

    public void MovieDone() => Interlocked.Increment(ref _processed);

    public void MovieAdded() => Interlocked.Increment(ref _moviesAdded);

    public void VersionAdded() => Interlocked.Increment(ref _versionsAdded);

    public void VersionUpdated() => Interlocked.Increment(ref _versionsUpdated);

    public void FileAdded() => Interlocked.Increment(ref _filesAdded);

    public void FileMissing() => Interlocked.Increment(ref _filesMissing);

    public void Skipped() => Interlocked.Increment(ref _skipped);

    public void Failed() => Interlocked.Increment(ref _failed);

    public void TagAdded() => Interlocked.Increment(ref _tagsAdded);

    public void ExclusionAdded() => Interlocked.Increment(ref _exclusionsAdded);

    public void HistoryAdded(int count) => Interlocked.Add(ref _historyAdded, count);

    public void BlocklistAdded(int count) => Interlocked.Add(ref _blocklistAdded, count);

    public void CustomFormatAdded() => Interlocked.Increment(ref _customFormatsAdded);

    public void CustomFormatUpdated() => Interlocked.Increment(ref _customFormatsUpdated);

    public void ProfileScored() => Interlocked.Increment(ref _profilesScored);

    /// <summary>One line about one film (or format, or profile), kept for the report; the list is capped so a huge library cannot exhaust memory.</summary>
    public void Issue(string title, int? tmdbId, string reason, int? movieId = null)
    {
        lock (_gate)
        {
            if (_issues.Count < MaxIssues)
            {
                _issues.Add(new RadarrImportIssue(title, tmdbId, reason, movieId));
            }
        }
    }

    public RadarrImportStatusResponse ToResponse()
    {
        List<RadarrImportIssue> issues;
        lock (_gate)
        {
            issues = [.. _issues];
        }

        return new RadarrImportStatusResponse(
            State, StartedAt, FinishedAt, Total, _processed, _moviesAdded, _versionsAdded, _versionsUpdated, _filesAdded, _filesMissing,
            _skipped, _failed, _tagsAdded, _exclusionsAdded, _historyAdded, _blocklistAdded, _customFormatsAdded, _customFormatsUpdated, _profilesScored, Current, Error, issues);
    }

    public static RadarrImportStatusResponse Idle { get; } =
        new(RadarrImportState.Idle, null, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null, []);
}
