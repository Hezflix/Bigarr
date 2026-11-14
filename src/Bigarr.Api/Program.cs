using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Features.Auth;
using Bigarr.Api.Features.Blocklist;
using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.History;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Indexers;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.MediaServers;
using Bigarr.Api.Features.Migration;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Notifications;
using Bigarr.Api.Features.Playback;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Features.SeriesFilms;
using Bigarr.Api.Features.Server;
using Bigarr.Api.Features.Server.Backup;
using Bigarr.Api.Features.Server.Health;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Features.Tags;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Downloads;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Files;
using Bigarr.Api.Infrastructure.Http;
using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Media;
using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Infrastructure.Metadata;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.RadarrImport;
using Bigarr.Api.Infrastructure.SonarrImport;
using Bigarr.Api.Infrastructure.Xem;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BigarrOptions>(builder.Configuration.GetSection(BigarrOptions.Section));
builder.Services.AddBigarrPersistence(builder.Configuration);
builder.Services.AddBigarrAuthentication();
// Behind a reverse proxy (Traefik, nginx, oauth2-proxy) the scheme and the client address arrive in X-Forwarded-*.
// In that setup the proxy is the only thing that can reach Bigarr, so its headers are trusted from any address:
// the session cookie is then marked Secure on https and failed logins log the real client.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});
builder.Services.AddScoped<IStartupTask, AdminSeeder>();
builder.Services.AddScoped<IStartupTask, QualitySeeder>();
builder.Services.AddBigarrMetadata(builder.Configuration);
builder.Services.AddBigarrXem(builder.Configuration);
builder.Services.AddBigarrIndexers();
builder.Services.AddBigarrDownloads(builder.Configuration);
builder.Services.AddBigarrEvents(builder.Configuration);
builder.Services.AddScoped<MovieService>();
builder.Services.AddScoped<MovieSummaryQuery>();
builder.Services.AddScoped<BulkEditService>();
builder.Services.AddScoped<SeriesService>();
builder.Services.AddScoped<SeriesOrderService>();
builder.Services.AddScoped<SeriesQueries>();
builder.Services.AddScoped<SeriesBulkEditService>();
builder.Services.AddScoped<SeriesFilmService>();
builder.Services.AddScoped<SeriesFilmLinker>();
builder.Services.AddScoped<SeriesFilmPlanner>();
builder.Services.AddSingleton<SeriesFilmLinkQueue>();
builder.Services.AddHostedService<SeriesFilmLinkRunner>();
builder.Services.AddScoped<TagService>();
builder.Services.AddScoped<HistoryService>();
builder.Services.AddScoped<RootFolderService>();
builder.Services.AddScoped<CustomFormatScorer>();
builder.Services.AddSingleton<FileFormatMatches>();
builder.Services.AddScoped<QualityProfileService>();
builder.Services.AddScoped<CustomFormatService>();
builder.Services.AddScoped<LibraryScanner>();
builder.Services.AddScoped<IRootFolderScanner>(sp => sp.GetRequiredService<LibraryScanner>());
builder.Services.AddScoped<SeriesLibraryScanner>();
builder.Services.AddScoped<IRootFolderScanner>(sp => sp.GetRequiredService<SeriesLibraryScanner>());
builder.Services.AddSingleton<LibraryScanRunner>();
builder.Services.AddScoped<IndexerService>();
builder.Services.AddScoped<TargetContextFactory>();
builder.Services.AddScoped<InteractiveSearchService>();
builder.Services.AddScoped<SeriesTargetsFactory>();
builder.Services.AddScoped<SeriesSearchService>();
builder.Services.AddScoped<SeriesRssPass>();
builder.Services.Configure<RssSyncOptions>(builder.Configuration.GetSection(RssSyncOptions.Section));
builder.Services.Configure<SearchOptions>(builder.Configuration.GetSection(SearchOptions.Section));
builder.Services.AddSingleton<RssFeedState>();
builder.Services.AddScoped<RssSyncService>();
builder.Services.AddScoped<AutomaticSearchService>();
builder.Services.AddScoped<WantedService>();
builder.Services.AddScoped<MovieQueries>();
builder.Services.AddSingleton<SearchQueue>();
builder.Services.AddHostedService<SearchRunner>();
builder.Services.AddSingleton<SeriesSearchQueue>();
builder.Services.AddSingleton<SeriesRefreshAlerts>();
builder.Services.AddHostedService<SeriesSearchRunner>();
builder.Services.AddScoped<BlocklistService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LibraryScanRunner>());
builder.Services.AddScoped<DownloadClientService>();
builder.Services.AddScoped<GrabService>();

// Shadow mode (next to Radarr): picks instead of grabs, sightings of what the RSS pass saw.
builder.Services.Configure<ShadowOptions>(builder.Configuration.GetSection(ShadowOptions.Section));
builder.Services.AddScoped<ShadowPickRecorder>();
builder.Services.AddScoped<SeriesShadowPickRecorder>();
builder.Services.AddScoped<ReleaseSightings>();
builder.Services.AddScoped<ShadowJudge>();
builder.Services.AddScoped<QueueService>();
builder.Services.AddScoped<QueuePoller>();
builder.Services.AddSingleton<QueueState>();
builder.Services.AddHostedService<QueueTrackerRunner>();
builder.Services.Configure<MediaOptions>(builder.Configuration.GetSection(MediaOptions.Section));
builder.Services.AddSingleton<IMediaInfoReader, FfprobeMediaInfoReader>();
builder.Services.AddSingleton<FileTransfer>();
builder.Services.AddSingleton<RecycleBin>();
builder.Services.AddScoped<MediaManagementService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<ManualImportService>();
builder.Services.AddScoped<RenameService>();
builder.Services.AddScoped<SeriesImportService>();
builder.Services.AddScoped<SeriesManualImportService>();
builder.Services.AddScoped<SeriesRenameService>();
builder.Services.AddScoped<MediaMaintenance>();
builder.Services.AddSingleton<ImportQueue>();
builder.Services.AddHostedService<ImportRunner>();
builder.Services.AddScoped<InterruptedImports>();
builder.Services.AddScoped<IStartupTask>(sp => sp.GetRequiredService<InterruptedImports>());

// The scheduler: jobs are scoped, resolved per run; the table remembers the cadence across restarts.
builder.Services.Configure<SchedulerOptions>(builder.Configuration.GetSection(SchedulerOptions.Section));
builder.Services.AddSingleton<SchedulerState>();
builder.Services.AddSingleton<SchedulerSignal>();
builder.Services.AddSingleton<TaskRunner>();
builder.Services.AddHostedService<SchedulerRunner>();
builder.Services.AddScoped<IScheduledJob, RssSyncJob>();
builder.Services.AddScoped<IScheduledJob, RssRecheckJob>();
builder.Services.AddScoped<IScheduledJob, CutoffUnmetSearchJob>();
builder.Services.AddScoped<IScheduledJob, HealthCheckJob>();
builder.Services.AddScoped<IScheduledJob, MediaMaintenanceJob>();
builder.Services.Configure<MetadataRefreshOptions>(builder.Configuration.GetSection(MetadataRefreshOptions.Section));
builder.Services.AddScoped<IScheduledJob, MetadataRefreshJob>();
builder.Services.AddScoped<IScheduledJob, SceneMappingRefreshJob>();
builder.Services.AddScoped<IScheduledJob, SeriesRefreshJob>();
builder.Services.AddScoped<IScheduledJob, SeriesFilmLinkJob>();
builder.Services.Configure<BackupOptions>(builder.Configuration.GetSection(BackupOptions.Section));
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<IScheduledJob, BackupJob>();

// The Radarr-compatible surface: /api/v3 behind API keys, /signalr/messages, /MediaCover.
builder.Services.AddRadarrCompat();

// Every outgoing HttpClient accepts gzip/deflate/brotli answers.
builder.Services.AddOutgoingDecompression();

// Moving in from Radarr: one background import at a time, reading Radarr's v3 API.
builder.Services.AddHttpClient(RadarrClient.HttpClientName, http => http.Timeout = TimeSpan.FromSeconds(120));
builder.Services.AddSingleton<RadarrClient>();
builder.Services.AddScoped<RadarrImportService>();
builder.Services.AddSingleton<RadarrImportRunner>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RadarrImportRunner>());

// Moving in from Tautulli: its play history, read from a copy of its database.
builder.Services.AddScoped<TautulliImportService>();
builder.Services.AddSingleton<TautulliImportRunner>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TautulliImportRunner>());

// Shadow mode: the Radarrs Bigarr follows (history, grabs, library) to compare its picks with what they grab.
builder.Services.AddSingleton<RadarrFollowMemory>();
builder.Services.AddScoped<RadarrFollower>();
builder.Services.AddScoped<FollowedRadarrService>();
builder.Services.AddSingleton<RadarrFollowWakeups>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RadarrFollowWakeups>());
builder.Services.AddScoped<CompareService>();
builder.Services.AddScoped<IScheduledJob, RadarrFollowJob>();

// Moving in from Sonarr and following it in shadow mode: the series twin of the two blocks above.
builder.Services.AddHttpClient(SonarrClient.HttpClientName, http => http.Timeout = TimeSpan.FromSeconds(120));
builder.Services.AddSingleton<SonarrClient>();
builder.Services.AddScoped<SonarrImportService>();
builder.Services.AddSingleton<SonarrImportRunner>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SonarrImportRunner>());
builder.Services.AddSingleton<SonarrFollowMemory>();
builder.Services.AddScoped<SeriesShadowJudge>();
builder.Services.AddScoped<SonarrFollower>();
builder.Services.AddScoped<FollowedSonarrService>();
builder.Services.AddScoped<SeriesCompareService>();
builder.Services.AddScoped<SeriesAutomaticSearchService>();
builder.Services.AddScoped<HeldReleaseService>();
builder.Services.AddScoped<SeriesWantedService>();
builder.Services.AddScoped<CalendarService>();
builder.Services.AddSingleton<EpisodeFileFormatMatches>();
builder.Services.AddSingleton<SonarrFollowWakeups>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SonarrFollowWakeups>());
builder.Services.AddScoped<IScheduledJob, SonarrFollowJob>();

// Media servers: Plex, Jellyfin and Emby hear about changed folders from the outbox.
builder.Services.AddBigarrMediaServers(builder.Configuration);
builder.Services.AddScoped<MediaServerService>();

// Playback (Tautulli replacement): Plex streams become plays, linked to the version that was played.
builder.Services.AddBigarrPlayback();
builder.Services.AddSingleton<HealthState>();
builder.Services.AddScoped<HealthCheckService>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddHealthChecks().AddDbContextCheck<BigarrDbContext>();
// System.Text.Json reads a number from a string too, so the document says "integer or string": the client only ever
// receives numbers, and that is what its types should say.
//
// A response always carries every property, default value or not, so everything but a request body lists them all as
// required: the client never has to guard against a field the server always sends.
builder.Services.AddOpenApi("v1", o => o.AddSchemaTransformer((schema, context, _) =>
{
    if (schema.Properties is { Count: > 0 } properties && !context.JsonTypeInfo.Type.Name.EndsWith("Request", StringComparison.Ordinal))
    {
        schema.Required = new HashSet<string>(properties.Keys);
    }

    if (schema.Type is { } type && type.HasFlag(JsonSchemaType.String) && (type.HasFlag(JsonSchemaType.Integer) || type.HasFlag(JsonSchemaType.Number)))
    {
        schema.Type = type & ~JsonSchemaType.String;
        schema.Pattern = null;
    }

    return Task.CompletedTask;
}));
builder.Services.AddSingleton<ProcessStart>();
builder.Services.AddProblemDetails();
builder.Services.AddBigarrResponseCompression();

// The build writes the OpenAPI document by running this entry point: it needs the endpoints, not the database or the
// background loops.
if (Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider")
{
    builder.Services.RemoveAll<IHostedService>();
}

var app = builder.Build();
app.Services.GetRequiredService<ProcessStart>();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseBigarrResponseCompression();

// Static files must run before routing: once an endpoint is matched (the SPA fallback matches
// everything), the static file middleware steps aside and /assets/*.js would come back as index.html.
app.UseDefaultFiles();
app.UsePrecompressedStaticFiles();
app.UseStaticFiles(PrecompressedStaticFiles.Options);
app.UseRouting();

app.UseRadarrCompat();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi(); // /openapi/v1.json, the same document the build writes for the TypeScript types
app.MapAuth();
app.MapApiKeys();
app.MapExternalLogin();
app.MapSystem();
app.MapMovies();
app.MapSeries();
app.MapSeriesFilms();
app.MapSeriesScan();
app.MapSeriesOrders();
app.MapHeldReleases();
app.MapSeriesWanted();
app.MapCalendar();
app.MapQuality();
app.MapRootFolders();
app.MapFileSystem();
app.MapIndexers();
app.MapSearch();
app.MapRss();
app.MapDownloads();
app.MapHistory();
app.MapImport();
app.MapBlocklist();
app.MapNotifications();
app.MapTasks();
app.MapHealth();
app.MapBackups();
app.MapTags();
app.MapMigration();
app.MapMediaServers();
app.MapPlayback();
app.MapShadow();
app.MapRadarrCompat();

// The SPA is built into wwwroot by `bun run build`; anything that is not API falls back to index.html.
app.MapFallbackToFile("{*path:regex(^(?!api/|health$|openapi/|signalr/|MediaCover/).*$)}", "index.html", PrecompressedStaticFiles.Options);

app.Run();

/// <summary>Exposed for <c>WebApplicationFactory</c> in the tests.</summary>
public partial class Program;
