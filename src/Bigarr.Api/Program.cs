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
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Server;
using Bigarr.Api.Features.Server.Backup;
using Bigarr.Api.Features.Server.Health;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Features.Tags;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Downloads;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Files;
using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Infrastructure.RadarrImport;
using Bigarr.Api.Infrastructure.Media;
using Bigarr.Api.Infrastructure.Metadata;
using Bigarr.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;

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
builder.Services.AddBigarrIndexers(builder.Configuration);
builder.Services.AddBigarrDownloads(builder.Configuration);
builder.Services.AddBigarrEvents(builder.Configuration);
builder.Services.AddScoped<MovieService>();
builder.Services.AddScoped<TagService>();
builder.Services.AddScoped<HistoryService>();
builder.Services.AddScoped<RootFolderService>();
builder.Services.AddScoped<CustomFormatScorer>();
builder.Services.AddScoped<QualityProfileService>();
builder.Services.AddScoped<CustomFormatService>();
builder.Services.AddScoped<LibraryScanner>();
builder.Services.AddSingleton<LibraryScanRunner>();
builder.Services.AddScoped<IndexerService>();
builder.Services.AddScoped<ProwlarrSyncService>();
builder.Services.AddSingleton<ProwlarrSyncState>();
builder.Services.AddHostedService<ProwlarrStartupSync>();
builder.Services.AddScoped<TargetContextFactory>();
builder.Services.AddScoped<InteractiveSearchService>();
builder.Services.Configure<RssSyncOptions>(builder.Configuration.GetSection(RssSyncOptions.Section));
builder.Services.Configure<SearchOptions>(builder.Configuration.GetSection(SearchOptions.Section));
builder.Services.AddSingleton<RssFeedState>();
builder.Services.AddScoped<RssSyncService>();
builder.Services.AddScoped<AutomaticSearchService>();
builder.Services.AddScoped<WantedService>();
builder.Services.AddScoped<MovieQueries>();
builder.Services.AddSingleton<SearchQueue>();
builder.Services.AddHostedService<SearchRunner>();
builder.Services.AddScoped<BlocklistService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LibraryScanRunner>());
builder.Services.AddScoped<DownloadClientService>();
builder.Services.AddScoped<GrabService>();
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
builder.Services.AddScoped<MediaMaintenance>();
builder.Services.AddSingleton<ImportQueue>();
builder.Services.AddHostedService<ImportRunner>();

// The scheduler (blueprint §3.7): jobs are scoped, resolved per run; the table remembers the cadence across restarts.
builder.Services.Configure<SchedulerOptions>(builder.Configuration.GetSection(SchedulerOptions.Section));
builder.Services.AddSingleton<SchedulerState>();
builder.Services.AddSingleton<SchedulerSignal>();
builder.Services.AddSingleton<TaskRunner>();
builder.Services.AddHostedService<SchedulerRunner>();
builder.Services.AddScoped<IScheduledJob, RssSyncJob>();
builder.Services.AddScoped<IScheduledJob, CutoffUnmetSearchJob>();
builder.Services.AddScoped<IScheduledJob, HealthCheckJob>();
builder.Services.AddScoped<IScheduledJob, MediaMaintenanceJob>();
builder.Services.Configure<MetadataRefreshOptions>(builder.Configuration.GetSection(MetadataRefreshOptions.Section));
builder.Services.AddScoped<IScheduledJob, MetadataRefreshJob>();
builder.Services.Configure<BackupOptions>(builder.Configuration.GetSection(BackupOptions.Section));
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<IScheduledJob, BackupJob>();

// The Radarr-compatible surface (blueprint §7): /api/v3 behind API keys, /signalr/messages, /MediaCover.
builder.Services.AddRadarrCompat();

// Moving in from Radarr (M8): one background import at a time, reading Radarr's v3 API.
builder.Services.AddHttpClient(RadarrClient.HttpClientName, http => http.Timeout = TimeSpan.FromSeconds(120));
builder.Services.AddSingleton<RadarrClient>();
builder.Services.AddScoped<RadarrImportService>();
builder.Services.AddSingleton<RadarrImportRunner>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RadarrImportRunner>());

// Media servers (M8): Plex, Jellyfin and Emby hear about changed folders from the outbox.
builder.Services.AddBigarrMediaServers();
builder.Services.AddScoped<MediaServerService>();
builder.Services.AddSingleton<HealthState>();
builder.Services.AddScoped<HealthCheckService>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddHealthChecks().AddDbContextCheck<BigarrDbContext>();
builder.Services.AddOpenApi("v1");
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

// Static files must run before routing: once an endpoint is matched (the SPA fallback matches
// everything), the static file middleware steps aside and /assets/*.js would come back as index.html.
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();

app.UseRadarrCompat();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi(); // /openapi/v1.json — feeds the generated TypeScript client
app.MapAuth();
app.MapApiKeys();
app.MapSystem();
app.MapMovies();
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
app.MapRadarrCompat();

// The SPA is built into wwwroot by `bun run build`; anything that is not API falls back to index.html.
app.MapFallbackToFile("{*path:regex(^(?!api/|health$|openapi/|signalr/|MediaCover/).*$)}", "index.html");

app.Run();

/// <summary>Exposed for <c>WebApplicationFactory</c> in the tests.</summary>
public partial class Program;
