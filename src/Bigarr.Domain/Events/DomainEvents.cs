namespace Bigarr.Domain.Events;

public interface IDomainEvent
{
    DateTime OccurredAt { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

public sealed record MovieAdded(int MovieId, int TmdbId) : DomainEvent;

public sealed record ReleaseGrabbed(int GrabId, IReadOnlyList<int> TargetIds) : DomainEvent;

public sealed record FileImported(int MediaFileId, int TargetId) : DomainEvent;

public sealed record FileUpgraded(int MediaFileId, int TargetId, int ReplacedMediaFileId) : DomainEvent;

public sealed record DownloadFailed(int GrabId, string Reason) : DomainEvent;

public sealed record ImportFailed(int GrabId, string Reason) : DomainEvent;

public sealed record DefaultVersionChanged(int MovieId, int NewDefaultVersionId, int? PreviousDefaultVersionId) : DomainEvent;

public sealed record HealthIssueRaised(string Source, string Message) : DomainEvent;
