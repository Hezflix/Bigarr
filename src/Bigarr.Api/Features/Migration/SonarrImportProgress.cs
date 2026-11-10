namespace Bigarr.Api.Features.Migration;

/// <summary>Mutable, in-memory progress of one Sonarr import, or of one pass of the follow. Read by the API while the import writes it.</summary>
public sealed class SonarrImportProgress(SonarrImportRequest request)
{
    public const int MaxIssues = 500;

    private readonly Lock _gate = new();
    private readonly List<SonarrImportIssue> _issues = [];
    private int _processed;
    private int _seriesAdded;
    private int _versionsAdded;
    private int _versionsUpdated;
    private int _episodesAdded;
    private int _filesAdded;
    private int _filesMissing;
    private int _skipped;
    private int _failed;
    private int _tagsAdded;
    private int _exclusionsAdded;
    private int _historyAdded;
    private int _blocklistAdded;
    private int _profilesScored;
    private int _profileQualitiesCopied;
    private int _filesReplaced;
    private int _profilesCreated;
    private int _qualitySizesUpdated;
    private int _filesRenamed;
    private int _filesRemoved;

    public SonarrImportRequest Request { get; } = request;

    public RadarrImportState State { get; set; } = RadarrImportState.Queued;

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public int Total { get; set; }

    public string? Current { get; set; }

    public string? Error { get; set; }

    public int Processed => _processed;

    public int SeriesAddedCount => _seriesAdded;

    public int VersionsAddedCount => _versionsAdded;

    public int VersionsUpdatedCount => _versionsUpdated;

    public int FilesAddedCount => _filesAdded;

    public int FilesMissingCount => _filesMissing;

    public int HistoryAddedCount => _historyAdded;

    public int SkippedCount => _skipped;

    public int FailedCount => _failed;

    public int FilesReplaced => _filesReplaced;

    public int FilesRenamed => _filesRenamed;

    public int FilesRemoved => _filesRemoved;

    public void SeriesDone() => Interlocked.Increment(ref _processed);

    public void SeriesAdded() => Interlocked.Increment(ref _seriesAdded);

    public void VersionAdded() => Interlocked.Increment(ref _versionsAdded);

    public void VersionUpdated() => Interlocked.Increment(ref _versionsUpdated);

    public void EpisodesAdded(int count) => Interlocked.Add(ref _episodesAdded, count);

    public void FileAdded() => Interlocked.Increment(ref _filesAdded);

    public void FileMissing() => Interlocked.Increment(ref _filesMissing);

    public void Skipped() => Interlocked.Increment(ref _skipped);

    public void Failed() => Interlocked.Increment(ref _failed);

    public void TagAdded() => Interlocked.Increment(ref _tagsAdded);

    public void ExclusionAdded() => Interlocked.Increment(ref _exclusionsAdded);

    public void HistoryAdded(int count) => Interlocked.Add(ref _historyAdded, count);

    public void BlocklistAdded(int count) => Interlocked.Add(ref _blocklistAdded, count);

    public void ProfileScored() => Interlocked.Increment(ref _profilesScored);

    public void ProfileQualitiesCopied() => Interlocked.Increment(ref _profileQualitiesCopied);

    public void ProfileCreated() => Interlocked.Increment(ref _profilesCreated);

    public void QualitySizesUpdated(int count) => Interlocked.Add(ref _qualitySizesUpdated, count);

    /// <summary>Following a Sonarr: its file replaced the episode's (an upgrade, a new download).</summary>
    public void FileReplaced() => Interlocked.Increment(ref _filesReplaced);

    /// <summary>Following a Sonarr: the same file under a new name.</summary>
    public void FileRenamed() => Interlocked.Increment(ref _filesRenamed);

    /// <summary>Following a Sonarr: it has no such file any more, the record went (the disk is not touched).</summary>
    public void FileRemoved() => Interlocked.Increment(ref _filesRemoved);

    /// <summary>One line about one series (or profile), kept for the report; the list is capped so a huge library cannot exhaust memory.</summary>
    public void Issue(string title, int? tvdbId, string reason, int? seriesId = null)
    {
        lock (_gate)
        {
            if (_issues.Count < MaxIssues)
            {
                _issues.Add(new SonarrImportIssue(title, tvdbId, reason, seriesId));
            }
        }
    }

    public SonarrImportStatusResponse ToResponse()
    {
        List<SonarrImportIssue> issues;
        lock (_gate)
        {
            issues = [.. _issues];
        }

        return new SonarrImportStatusResponse(
            State, StartedAt, FinishedAt, Total, _processed, _seriesAdded, _versionsAdded, _versionsUpdated, _episodesAdded, _filesAdded, _filesMissing,
            _skipped, _failed, _tagsAdded, _exclusionsAdded, _historyAdded, _blocklistAdded, _profilesScored, _profileQualitiesCopied, Current, Error, issues, _profilesCreated, _qualitySizesUpdated);
    }

    public static SonarrImportStatusResponse Idle { get; } =
        new(RadarrImportState.Idle, null, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null, []);
}
