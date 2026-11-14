using Bigarr.Domain.Events;

namespace Bigarr.Domain.Notifications;

/// <summary>
/// Which deliveries one event makes for one endpoint: its subscription, its tags, in the Radarr
/// format its view and whether Radarr has a word for the event, in the Discord format whether a message is worth
/// posting. Pure: the dispatcher passes the versions' tags.
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

    /// <summary>Sonarr's <c>WebhookEventType</c> for each Bigarr event it has a counterpart for (series events, health, test).</summary>
    private static readonly Dictionary<string, string> SonarrTypes = new (Type Event, string Sonarr)[]
    {
        (typeof(EpisodeGrabbed), "Grab"),
        (typeof(EpisodeFileImported), "Download"),
        (typeof(EpisodeFileRenamed), "Rename"),
        (typeof(SeriesAdded), "SeriesAdd"),
        (typeof(SeriesDeleted), "SeriesDelete"),
        (typeof(EpisodeFileDeleted), "EpisodeFileDelete"),
        (typeof(HealthIssueRaised), "Health"),
        (typeof(HealthIssueResolved), "HealthRestored"),
        (typeof(EpisodeImportFailed), "ManualInteractionRequired"),
        (typeof(TestEvent), "Test"),
    }.ToDictionary(e => DomainEventCatalog.NameOf(e.Event), e => e.Sonarr, StringComparer.Ordinal);

    /// <summary>Sonarr's event type for a Bigarr one, or null when the Sonarr format does not send it.</summary>
    public static string? SonarrEventType(string eventType) => SonarrTypes.GetValueOrDefault(eventType);

    /// <summary>
    /// What Radarr's Discord connection posts, plus what Bigarr adds and a human wants to read: the failures, and a
    /// version added, deleted or made the default. A version's settings changing stays out of the channel: it fires on
    /// every monitoring, profile or tag edit, once per version in a bulk edit.
    /// </summary>
    private static readonly HashSet<string> DiscordTypes = new[]
    {
        typeof(ReleaseGrabbed), typeof(FileImported), typeof(FileUpgraded), typeof(FileRenamed), typeof(MovieAdded), typeof(MovieDeleted),
        typeof(FileDeleted), typeof(HealthIssueRaised), typeof(HealthIssueResolved), typeof(ImportFailed), typeof(DownloadFailed), typeof(TestEvent),
        typeof(VersionAdded), typeof(VersionDeleted), typeof(DefaultVersionChanged),
        typeof(EpisodeGrabbed), typeof(EpisodeFileImported), typeof(EpisodeFileRenamed), typeof(SeriesAdded), typeof(SeriesDeleted),
        typeof(EpisodeFileDeleted), typeof(EpisodeImportFailed), typeof(EpisodeDownloadFailed), typeof(SeriesVersionDeleted),
    }.Select(DomainEventCatalog.NameOf).ToHashSet(StringComparer.Ordinal);

    /// <summary>True when the Discord format posts a message for the event type.</summary>
    public static bool SentToDiscord(string eventType) => DiscordTypes.Contains(eventType);

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

    /// <summary>The series versions an event is about. Empty for film events, health and test.</summary>
    public static IReadOnlyList<SeriesVersionRef> SeriesSubjects(DomainEvent domainEvent) => domainEvent switch
    {
        SeriesAdded e => [e.Version],
        SeriesDeleted e => e.Versions ?? [],
        SeriesVersionDeleted e => [e.Version],
        SeriesVersionAdded e => [e.Version],
        SeriesVersionUpdated e => [e.Version],
        SeriesDefaultVersionChanged e => e.PreviousDefault is null ? [e.NewDefault] : [e.NewDefault, e.PreviousDefault],
        EpisodeGrabbed e => [e.Version],
        EpisodeFileImported e => [e.Version],
        EpisodeFileRenamed e => [e.Version],
        EpisodeFileDeleted e => [e.Version],
        EpisodeDownloadFailed { Version: { } version } => [version],
        EpisodeImportFailed { Version: { } version } => [version],
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
    /// since each one is a separate Radarr movie. A Discord endpoint is routed like a Bigarr one, one message per event.
    /// </summary>
    /// <param name="domainEvent">The parsed payload; null when its type is no longer in the catalogue.</param>
    /// <param name="tagsOf">The tag ids of a version the event is about.</param>
    /// <param name="seriesTagsOf">The tag ids of a series version the event is about; none when null.</param>
    public static List<int?> Deliveries(NotificationEndpoint endpoint, string eventType, DomainEvent? domainEvent, Func<VersionRef, IEnumerable<int>> tagsOf, Func<SeriesVersionRef, IEnumerable<int>>? seriesTagsOf = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(tagsOf);
        seriesTagsOf ??= v => (v.Tags ?? []).Select(t => t.Id);

        if (!endpoint.Subscribes(eventType))
        {
            return [];
        }

        if (endpoint.Format == WebhookFormat.Sonarr)
        {
            if (domainEvent is null || SonarrEventType(eventType) is null)
            {
                return [];
            }

            if (IgnoresTags(domainEvent))
            {
                return [null];
            }

            // Each series version is a Sonarr series: judged on its own, through the endpoint's view and tags.
            return SeriesSubjects(domainEvent)
                .Where(v => endpoint.View.Sees(v.Name, v.IsDefault) && endpoint.Matches(seriesTagsOf(v)))
                .Select(v => (int?)v.Id)
                .Distinct()
                .ToList();
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

        if (endpoint.Format == WebhookFormat.Discord && (domainEvent is null || !SentToDiscord(eventType)))
        {
            return [];
        }

        if (endpoint.Tags.Count == 0 || (domainEvent is not null && IgnoresTags(domainEvent)))
        {
            return [null];
        }

        return domainEvent is not null && (Subjects(domainEvent).Any(v => endpoint.Matches(tagsOf(v))) || SeriesSubjects(domainEvent).Any(v => endpoint.Matches(seriesTagsOf(v))))
            ? [null]
            : [];
    }
}
