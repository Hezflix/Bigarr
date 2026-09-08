using Bigarr.Api.Features.Auth;
using Bigarr.Api.Features.System;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BigarrOptions>(builder.Configuration.GetSection(BigarrOptions.Section));
builder.Services.AddBigarrPersistence(builder.Configuration);
builder.Services.AddBigarrAuthentication();
builder.Services.AddScoped<IStartupTask, AdminSeeder>();
builder.Services.AddHealthChecks().AddDbContextCheck<BigarrDbContext>();
builder.Services.AddOpenApi("v1");
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi(); // /openapi/v1.json — feeds the generated TypeScript client
app.MapAuth();
app.MapApiKeys();
app.MapSystem();

// The SPA is built into wwwroot by `bun run build`; anything that is not API falls back to index.html.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("{*path:regex(^(?!api/|health$|openapi/).*$)}", "index.html");

app.Run();

/// <summary>Exposed for <c>WebApplicationFactory</c> in the tests.</summary>
public partial class Program;
