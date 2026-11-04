using System.Text.Json.Serialization;
using Bigarr.Domain.Movies;

namespace Bigarr.Domain.Events;

/// <summary>
/// What happened, with enough of a snapshot that a consumer never has to query Bigarr back (blueprint §3.9). Each
/// record is stored as JSON in the outbox and sent as the <c>data</c> of a webhook or SSE event, so the shapes here
/// are a public contract: add fields, never rename them.
/// </summary>
public abstract record DomainEvent
{
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <summary>camelCase name used by the outbox, the SSE stream and webhook subscriptions.</summary>
    [JsonIgnore]
    public string Type => DomainEventCatalog.NameOf(GetType());
}

public sealed record MovieRef(int Id, int TmdbId, string? ImdbId, string Title, int? Year)
{
    public static MovieRef From(Movie movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        return new MovieRef(movie.Id, movie.TmdbId, movie.ImdbId, movie.Title, movie.Year);
    }
}

/// <param name="Folder">The version's folder on disk, when the root folder was loaded with it.</param>
public sealed record VersionRef(int Id, string Name, VersionMode Mode, bool IsDefault, bool Monitored, string? QualityProfile, string? Folder = null)
{
    public static VersionRef From(MovieVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var folder = version.RootFolder is null ? null : Path.Combine(version.RootFolder.Path, version.FolderName);
        return new VersionRef(version.Id, version.Name, version.Mode, version.IsDefault, version.Monitored, version.QualityProfile?.Name, folder);
    }
}

public sealed record ReleaseRef(string Title, string? Quality, string? Indexer, long Size, string? ReleaseGroup = null, int? Seeders = null, string? InfoHash = null);

public sealed record FileRef(int Id, string Path, string? Quality, long Size, List<string>? CustomFormats = null, int? CustomFormatScore = null);

public sealed record MovieAdded(MovieRef Movie, VersionRef Version) : DomainEvent;

public sealed record VersionAdded(MovieRef Movie, VersionRef Version) : DomainEvent;

/// <summary>Monitoring, profile, availability, mode, name, constraints or tags of a version changed; its file is untouched.</summary>
public sealed record VersionUpdated(MovieRef Movie, VersionRef Version) : DomainEvent;

/// <param name="Versions">Every version the film had, so a consumer that tracked them (the compat SignalR hub) can drop each one.</param>
public sealed record MovieDeleted(MovieRef Movie, bool DeletedFiles, List<VersionRef>? Versions = null) : DomainEvent;

public sealed record VersionDeleted(MovieRef Movie, VersionRef Version, bool DeletedFiles) : DomainEvent;

public sealed record ReleaseGrabbed(MovieRef Movie, VersionRef Version, ReleaseRef Release, int GrabId, string DownloadClient, string DownloadId, string? Trigger) : DomainEvent;

public sealed record FileImported(MovieRef Movie, VersionRef Version, FileRef File, ReleaseRef? Release, int? GrabId, bool Manual, string Transfer) : DomainEvent;

public sealed record FileUpgraded(MovieRef Movie, VersionRef Version, FileRef File, FileRef PreviousFile, ReleaseRef? Release, int? GrabId, bool Manual, string Transfer) : DomainEvent;

public sealed record DownloadFailed(MovieRef? Movie, VersionRef? Version, ReleaseRef Release, int GrabId, string Reason, bool Blocklisted, bool SearchRequested) : DomainEvent;

public sealed record ImportFailed(MovieRef? Movie, VersionRef? Version, ReleaseRef Release, int GrabId, string Reason) : DomainEvent;

public sealed record FileDeleted(MovieRef Movie, VersionRef Version, FileRef File, string Reason) : DomainEvent;

/// <summary>A file was moved to the current naming template; <paramref name="File"/> carries the new path.</summary>
public sealed record FileRenamed(MovieRef Movie, VersionRef Version, FileRef File, string PreviousPath) : DomainEvent;

public sealed record DefaultVersionChanged(MovieRef Movie, VersionRef NewDefault, VersionRef? PreviousDefault) : DomainEvent;

public enum HealthIssueType
{
    Notice = 1,
    Warning = 2,
    Error = 3,
}

public sealed record HealthIssueRaised(string Source, HealthIssueType IssueType, string Message) : DomainEvent;

public sealed record HealthIssueResolved(string Source, string Message) : DomainEvent;

/// <summary>Sent by the "test" button of a notification endpoint.</summary>
public sealed record TestEvent(string Message) : DomainEvent;

/// <summary>Every event a consumer can subscribe to, with the wording the settings page shows.</summary>
public static class DomainEventCatalog
{
    public static readonly IReadOnlyList<(Type Clr, string Type, string Description)> Entries =
    [
        (typeof(MovieAdded), "movieAdded", "A film was added to the library"),
        (typeof(VersionAdded), "versionAdded", "A version was added to a film"),
        (typeof(VersionUpdated), "versionUpdated", "A version's settings changed"),
        (typeof(MovieDeleted), "movieDeleted", "A film was removed from the library"),
        (typeof(VersionDeleted), "versionDeleted", "A version was removed from a film"),
        (typeof(ReleaseGrabbed), "grabbed", "A release was sent to a download client"),
        (typeof(FileImported), "imported", "A file landed in the library"),
        (typeof(FileUpgraded), "upgraded", "A file replaced a lower one"),
        (typeof(DownloadFailed), "downloadFailed", "A download failed and was blocklisted"),
        (typeof(ImportFailed), "importFailed", "A completed download could not be imported"),
        (typeof(FileDeleted), "fileDeleted", "A file was deleted"),
        (typeof(FileRenamed), "fileRenamed", "A file was renamed"),
        (typeof(DefaultVersionChanged), "defaultVersionChanged", "The default version of a film changed"),
        (typeof(HealthIssueRaised), "healthIssue", "A health check raised an issue"),
        (typeof(HealthIssueResolved), "healthRestored", "A health issue went away"),
        (typeof(TestEvent), "test", "Test notification"),
    ];

    private static readonly Dictionary<Type, string> Names = Entries.ToDictionary(e => e.Clr, e => e.Type);

    public static IReadOnlyList<(string Type, string Description)> All { get; } = Entries.Select(e => (e.Type, e.Description)).ToList();

    public static bool IsKnown(string type) => Names.Values.Contains(type, StringComparer.Ordinal);

    public static string NameOf(Type clr) =>
        Names.TryGetValue(clr, out var name) ? name : throw new InvalidOperationException($"{clr.Name} is not listed in the event catalogue.");
}
