using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Features.Auth;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.History;
using Bigarr.Api.Features.Indexers;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Server;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Downloads;
using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Api.Infrastructure.Metadata;
using Bigarr.Api.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BigarrOptions>(builder.Configuration.GetSection(BigarrOptions.Section));
builder.Services.AddBigarrPersistence(builder.Configuration);
builder.Services.AddBigarrAuthentication();
builder.Services.AddScoped<IStartupTask, AdminSeeder>();
builder.Services.AddScoped<IStartupTask, QualitySeeder>();
builder.Services.AddBigarrMetadata(builder.Configuration);
builder.Services.AddBigarrIndexers(builder.Configuration);
builder.Services.AddBigarrDownloads(builder.Configuration);
builder.Services.AddScoped<MovieService>();
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
builder.Services.AddSingleton<RssFeedState>();
builder.Services.AddScoped<RssSyncService>();
builder.Services.AddHostedService<RssSyncRunner>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LibraryScanRunner>());
builder.Services.AddScoped<DownloadClientService>();
builder.Services.AddScoped<GrabService>();
builder.Services.AddScoped<QueueService>();
builder.Services.AddScoped<QueuePoller>();
builder.Services.AddSingleton<QueueState>();
builder.Services.AddHostedService<QueueTrackerRunner>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddHealthChecks().AddDbContextCheck<BigarrDbContext>();
builder.Services.AddOpenApi("v1");
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Static files must run before routing: once an endpoint is matched (the SPA fallback matches
// everything), the static file middleware steps aside and /assets/*.js would come back as index.html.
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi(); // /openapi/v1.json — feeds the generated TypeScript client
app.MapAuth();
app.MapApiKeys();
app.MapSystem();
app.MapMovies();
app.MapQuality();
app.MapRootFolders();
app.MapIndexers();
app.MapSearch();
app.MapRss();
app.MapDownloads();
app.MapHistory();

// The SPA is built into wwwroot by `bun run build`; anything that is not API falls back to index.html.
app.MapFallbackToFile("{*path:regex(^(?!api/|health$|openapi/).*$)}", "index.html");

app.Run();

/// <summary>Exposed for <c>WebApplicationFactory</c> in the tests.</summary>
public partial class Program;
