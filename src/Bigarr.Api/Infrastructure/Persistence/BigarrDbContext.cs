using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.Events;
using Bigarr.Domain.History;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Library;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Notifications;
using Bigarr.Domain.Playback;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Scheduling;
using Bigarr.Domain.Shadow;
using Bigarr.Domain.Tags;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Infrastructure.Persistence;

/// <summary>
/// Single database. Entity configurations live in the slice that owns the entity
/// (<c>Features/*/Persistence</c>) and are discovered by assembly scan.
/// </summary>
public sealed class BigarrDbContext(DbContextOptions<BigarrDbContext> options) : DbContext(options)
{
    public DbSet<Movie> Movies => Set<Movie>();

    public DbSet<MovieExclusion> MovieExclusions => Set<MovieExclusion>();

    public DbSet<AcquisitionTarget> AcquisitionTargets => Set<AcquisitionTarget>();

    public DbSet<MovieVersion> MovieVersions => Set<MovieVersion>();

    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();

    public DbSet<ExtraFile> ExtraFiles => Set<ExtraFile>();

    public DbSet<Series> Series => Set<Series>();

    public DbSet<Season> Seasons => Set<Season>();

    public DbSet<SeriesVersion> SeriesVersions => Set<SeriesVersion>();

    public DbSet<SeriesVersionSeason> SeriesVersionSeasons => Set<SeriesVersionSeason>();

    public DbSet<Episode> Episodes => Set<Episode>();

    public DbSet<EpisodeTarget> EpisodeTargets => Set<EpisodeTarget>();

    public DbSet<EpisodeFile> EpisodeFiles => Set<EpisodeFile>();

    public DbSet<EpisodeExtraFile> EpisodeExtraFiles => Set<EpisodeExtraFile>();

    public DbSet<UnmatchedEpisodeFile> UnmatchedEpisodeFiles => Set<UnmatchedEpisodeFile>();

    public DbSet<SeriesOrderChange> SeriesOrderChanges => Set<SeriesOrderChange>();

    public DbSet<SceneName> SceneNames => Set<SceneName>();

    public DbSet<Grab> Grabs => Set<Grab>();

    public DbSet<GrabTarget> GrabTargets => Set<GrabTarget>();

    public DbSet<BlocklistEntry> Blocklist => Set<BlocklistEntry>();

    public DbSet<HistoryEvent> History => Set<HistoryEvent>();

    public DbSet<QualityDefinition> QualityDefinitions => Set<QualityDefinition>();

    public DbSet<QualityProfile> QualityProfiles => Set<QualityProfile>();

    public DbSet<CustomFormat> CustomFormats => Set<CustomFormat>();

    public DbSet<Indexer> Indexers => Set<Indexer>();

    public DbSet<DownloadClient> DownloadClients => Set<DownloadClient>();

    public DbSet<RootFolder> RootFolders => Set<RootFolder>();

    public DbSet<UnmappedFolder> UnmappedFolders => Set<UnmappedFolder>();

    public DbSet<MediaManagementConfig> MediaManagement => Set<MediaManagementConfig>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<User> Users => Set<User>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<OutboxEvent> Outbox => Set<OutboxEvent>();

    public DbSet<NotificationEndpoint> NotificationEndpoints => Set<NotificationEndpoint>();

    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    public DbSet<ScheduledJob> ScheduledJobs => Set<ScheduledJob>();

    public DbSet<MediaServer> MediaServers => Set<MediaServer>();

    public DbSet<ShadowPick> ShadowPicks => Set<ShadowPick>();

    public DbSet<SeriesShadowPick> SeriesShadowPicks => Set<SeriesShadowPick>();

    public DbSet<SeriesShadowPickEpisode> SeriesShadowPickEpisodes => Set<SeriesShadowPickEpisode>();

    public DbSet<ReleaseSighting> ReleaseSightings => Set<ReleaseSighting>();

    public DbSet<FollowedRadarr> FollowedRadarrs => Set<FollowedRadarr>();

    public DbSet<RadarrGrab> RadarrGrabs => Set<RadarrGrab>();

    public DbSet<FollowedSonarr> FollowedSonarrs => Set<FollowedSonarr>();

    public DbSet<SonarrGrab> SonarrGrabs => Set<SonarrGrab>();

    public DbSet<SonarrGrabEpisode> SonarrGrabEpisodes => Set<SonarrGrabEpisode>();

    public DbSet<SeriesExclusion> SeriesExclusions => Set<SeriesExclusion>();

    public DbSet<MediaServerUser> MediaServerUsers => Set<MediaServerUser>();

    public DbSet<MediaServerItem> MediaServerItems => Set<MediaServerItem>();

    public DbSet<Play> Plays => Set<Play>();

    public DbSet<PlayStream> PlayStreams => Set<PlayStream>();

    public DbSet<PlaybackSession> PlaybackSessions => Set<PlaybackSession>();

    public DbSet<PlaybackSettings> PlaybackSettings => Set<PlaybackSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BigarrDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }
}
