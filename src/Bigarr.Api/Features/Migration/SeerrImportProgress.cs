namespace Bigarr.Api.Features.Migration;

/// <summary>Mutable, in-memory progress of one Seerr import. Read by the API while the import writes it.</summary>
public sealed class SeerrImportProgress(SeerrImportRequest request) : IImportProgress<SeerrImportStatusResponse>
{
    public const int MaxIssues = 500;

    private readonly Lock _gate = new();
    private readonly List<SeerrImportIssue> _issues = [];

    public SeerrImportRequest Request { get; } = request;

    public RadarrImportState State { get; set; } = RadarrImportState.Queued;

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public string? Current { get; set; }

    public string? Error { get; set; }

    public bool UploadDeleted { get; set; }

    public int Total { get; set; }

    public int Processed { get; set; }

    public int UsersAdded { get; set; }

    public int UsersMatched { get; set; }

    public int UsersUpdated { get; set; }

    public int RequestsAdded { get; set; }

    public int IssuesAdded { get; set; }

    public int CommentsAdded { get; set; }

    public int SlidersAdded { get; set; }

    public int SlidersUpdated { get; set; }

    public int AlreadyImported { get; set; }

    public int Skipped { get; set; }

    public int TitlesFromTmdb { get; set; }

    public void Issue(string kind, int seerrId, string what, string reason)
    {
        lock (_gate)
        {
            if (_issues.Count < MaxIssues)
            {
                _issues.Add(new SeerrImportIssue(kind, seerrId, what, reason));
            }
        }
    }

    public SeerrImportStatusResponse ToResponse()
    {
        List<SeerrImportIssue> issues;
        lock (_gate)
        {
            issues = [.. _issues];
        }

        return new SeerrImportStatusResponse(
            State, StartedAt, FinishedAt, Total, Processed, UsersAdded, UsersMatched, UsersUpdated, RequestsAdded, IssuesAdded, CommentsAdded,
            SlidersAdded, SlidersUpdated, AlreadyImported, Skipped, TitlesFromTmdb, Current, Error, issues, UploadDeleted);
    }

    public static SeerrImportStatusResponse Idle { get; } =
        new(RadarrImportState.Idle, null, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null, [], false);
}
