using Bigarr.Domain.Events;

namespace Bigarr.Domain.Notifications;

/// <summary>
/// Which deliveries one event makes for one endpoint (blueprint §3.9): its subscription, its tags and, in the Radarr
/// format, its view and whether Radarr has a word for the event. Pure: the dispatcher passes the versions' tags.
/// </summary>
public static class WebhookRouting
{
    /// <summary>Radarr's <c>WebhookEventType</c> for each Bigarr event it has a counterpart for.</summary>
    private static readonly Dictionary<string, string> RadarrTypes = new (Type Event, string Radarr)[]
    {
        (typeof(ReleaseGrabbed), "Grab"),
        (typeof(FileImported), "Download"),
        (typeof(FileUpgraded), "Download"),
        (typeof(FileRenamed), "Rename"),
        (typeof(MovieAdded), "MovieAdded"),
        (typeof(MovieDeleted), "MovieDelete"),
        (typeof(FileDeleted), "MovieFileDelete"),
        (typeof(HealthIssueRaised), "Health"),
        (typeof(HealthIssueResolved), "HealthRestored"),
        (typeof(ImportFailed), "ManualInteractionRequired"),
        (typeof(TestEvent), "Test"),
    }.ToDictionary(e => DomainEventCatalog.NameOf(e.Event), e => e.Radarr, StringComparer.Ordinal);

    /// <summary>Radarr's event type for a Bigarr one, or null when the Radarr format does not send it.</summary>
    public static string? RadarrEventType(string eventType) => RadarrTypes.GetValueOrDefault(eventType);

    /// <summary>The versions an event is about. Empty when it is about no film: health, test, a download nothing matched.</summary>
    public static IReadOnlyList<VersionRef> Subjects(DomainEvent domainEvent) => domainEvent switch
    {
        MovieAdded e => [e.Version],
        VersionAdded e => [e.Version],
        VersionUpdated e => [e.Version],
        MovieDeleted e => e.Versions ?? [],
        VersionDeleted e => [e.Version],
        ReleaseGrabbed e => [e.Version],
        FileImported e => [e.Version],
        FileUpgraded e => [e.Version],
        DownloadFailed { Version: { } version } => [version],
        ImportFailed { Version: { } version } => [version],
        FileDeleted e => [e.Version],
        FileRenamed e => [e.Version],
        DefaultVersionChanged e => e.PreviousDefault is null ? [e.NewDefault] : [e.NewDefault, e.PreviousDefault],
        _ => [],
    };

    private static readonly HashSet<string> FilmlessTypes = new[] { typeof(HealthIssueRaised), typeof(HealthIssueResolved), typeof(TestEvent) }
        .Select(DomainEventCatalog.NameOf)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>Health, health restored and test concern no film: an endpoint's tags do not filter them, like in Radarr.</summary>
    public static bool IgnoresTags(string eventType) => FilmlessTypes.Contains(eventType);

    public static bool IgnoresTags(DomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return IgnoresTags(domainEvent.Type);
    }

    /// <summary>
    /// One entry per delivery to create, empty when the endpoint gets nothing. <c>null</c> is a delivery about the
    /// event as a whole; a version id is a Radarr-format delivery whose Radarr movie is that version. A film-level
    /// event matches the tags when any of its versions does; in the Radarr format each version is judged on its own,
    /// since each one is a separate Radarr movie.
    /// </summary>
    /// <param name="domainEvent">The parsed payload; null when its type is no longer in the catalogue.</param>
    /// <param name="tagsOf">The tag ids of a version the event is about.</param>
    public static List<int?> Deliveries(NotificationEndpoint endpoint, string eventType, DomainEvent? domainEvent, Func<VersionRef, IEnumerable<int>> tagsOf)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(tagsOf);

        if (!endpoint.Subscribes(eventType))
        {
            return [];
        }

        if (endpoint.Format == WebhookFormat.Radarr)
        {
            if (domainEvent is null || RadarrEventType(eventType) is null)
            {
                return [];
            }

            if (IgnoresTags(domainEvent))
            {
                return [null];
            }

            // An event without a film (an import that matched nothing) has no Radarr movie to describe: not sent.
            return Subjects(domainEvent)
                .Where(v => endpoint.View.Sees(v.Name, v.IsDefault) && endpoint.Matches(tagsOf(v)))
                .Select(v => (int?)v.Id)
                .Distinct()
                .ToList();
        }

        if (endpoint.Tags.Count == 0 || (domainEvent is not null && IgnoresTags(domainEvent)))
        {
            return [null];
        }

        return domainEvent is not null && Subjects(domainEvent).Any(v => endpoint.Matches(tagsOf(v))) ? [null] : [];
    }
}
