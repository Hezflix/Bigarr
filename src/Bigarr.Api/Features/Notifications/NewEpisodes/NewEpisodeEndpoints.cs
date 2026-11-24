using System.Security.Claims;
using Bigarr.Api.Features.Users;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Http;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Common;
using Bigarr.Domain.Notifications;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Notifications.NewEpisodes;

/// <summary>
/// New episodes told in Discord direct messages: the bot's token and the recent announcements for the admin; for every
/// signed-in user, the shows they follow and the switch that stops one of them.
/// </summary>
public static class NewEpisodeEndpoints
{
    public const int TokenMaxLength = 256;

    public static IServiceCollection AddBigarrNewEpisodes(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NewEpisodeOptions>(configuration.GetSection(NewEpisodeOptions.Section));
        services.AddHttpClient(DiscordBotClient.HttpClientName, http =>
            {
                http.Timeout = TimeSpan.FromSeconds(30);

                // Discord wants a bot's user agent to say so: "DiscordBot ($url, $version)".
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"DiscordBot (https://github.com/Hezflix/Bigarr, {Server.SystemEndpoints.Version})");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })

            // The factory's handlers log requests; the bot token travels in a header and must never reach a log.
            .RemoveAllLoggers();
        services.AddSingleton<DiscordBotClient>();
        services.AddSingleton<PlexOwners>();
        services.AddScoped<NewEpisodeFollowers>();
        services.AddScoped<NewEpisodeAnnouncements>();
        services.AddHostedService<NewEpisodeAnnouncer>();
        return services;
    }

    public static IEndpointRouteBuilder MapNewEpisodes(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/notifications").WithTags("Notifications").RequireAuthorization(AuthPolicies.Api);
        admin.MapGet("/discord-bot", BotAsync);
        admin.MapPut("/discord-bot", UpdateBotAsync);
        admin.MapGet("/new-episodes", AnnouncementsAsync);

        // Every signed-in user's own shows: what they watch is theirs to see, and only they turn a show off.
        var profile = app.MapGroup("/api/v1/profile").WithTags("Users").RequireAuthorization(AuthPolicies.SignedIn);
        profile.MapGet("/shows", MyShowsAsync);
        profile.MapPut("/shows/{seriesId:int}", FollowAsync);
        return app;
    }

    private static async Task<Ok<DiscordBotResponse>> BotAsync(BigarrDbContext db, CancellationToken ct)
    {
        var settings = await UserSettings.ReadAsync(db, ct);
        return TypedResults.Ok(ToResponse(settings));
    }

    private static async Task<Results<Ok<DiscordBotResponse>, ProblemHttpResult>> UpdateBotAsync(DiscordBotRequest request, BigarrDbContext db, ILoggerFactory logs, CancellationToken ct)
    {
        var settings = await UserSettings.EditAsync(db, ct);
        if (!SecretMask.Kept(request.Token))
        {
            var token = request.Token?.Trim();
            if (token is { Length: > TokenMaxLength } || (token?.Any(char.IsWhiteSpace) ?? false))
            {
                return TypedResults.Problem(title: "That is not a Discord bot token.", detail: "Copy it from the Discord Developer Portal: your application → Bot → Reset Token.", statusCode: StatusCodes.Status400BadRequest);
            }

            var before = !string.IsNullOrEmpty(settings.DiscordBotToken);
            settings.DiscordBotToken = string.IsNullOrEmpty(token) ? null : token;
            await db.SaveChangesAsync(ct);
            logs.CreateLogger("Bigarr.Notifications").LogInformation(
                "Discord bot token {Change}",
                settings.DiscordBotToken is null ? (before ? "removed" : "left empty") : before ? "replaced" : "set");
        }

        return TypedResults.Ok(ToResponse(settings));
    }

    private static DiscordBotResponse ToResponse(InstanceSettings settings) =>
        new(!string.IsNullOrEmpty(settings.DiscordBotToken), SecretMask.Of(settings.DiscordBotToken));

    private static async Task<Ok<List<NewEpisodeAnnouncementResponse>>> AnnouncementsAsync(int? limit, BigarrDbContext db, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 30, 1, 200);
        var announcements = await db.NewEpisodeAnnouncements.AsNoTracking()
            .Include(a => a.Episodes)
            .Include(a => a.Messages)
            .OrderByDescending(a => a.Id)
            .Take(take)
            .AsSplitQuery()
            .ToListAsync(ct);
        var seriesIds = announcements.Select(a => a.SeriesId).Distinct().ToList();
        var titles = await db.Series.AsNoTracking().Where(s => seriesIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Title, ct);
        var userIds = announcements.SelectMany(a => a.Messages).Select(m => m.UserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        return TypedResults.Ok(announcements.Select(a => new NewEpisodeAnnouncementResponse(
            a.Id,
            a.SeriesId,
            titles.GetValueOrDefault(a.SeriesId),
            [.. a.Episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).Select(e => $"S{e.SeasonNumber:00}E{e.EpisodeNumber:00}")],
            a.CreatedAt,
            a.ReadyAt,
            a.PlexWait,
            [.. a.Messages.OrderBy(m => m.Id).Select(m => new NewEpisodeMessageResponse(m.Id, m.UserId, names.GetValueOrDefault(m.UserId), m.State, m.Attempts, m.DeliveredAt, m.NextAttemptAt, m.LastStatusCode, m.LastError))])).ToList());
    }

    private static async Task<Results<Ok<FollowedShowsResponse>, NotFound>> MyShowsAsync(ClaimsPrincipal principal, BigarrDbContext db, NewEpisodeFollowers followers, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == principal.UserId(), ct);
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        var shows = await followers.ShowsOfAsync(user.Id, ct);
        var settings = await UserSettings.ReadAsync(db, ct);
        return TypedResults.Ok(new FollowedShowsResponse(shows is not null, !string.IsNullOrEmpty(settings.DiscordBotToken), Mentions.WantsNewEpisodes(user), shows ?? []));
    }

    private static async Task<Results<NoContent, NotFound>> FollowAsync(int seriesId, FollowShowRequest request, ClaimsPrincipal principal, BigarrDbContext db, CancellationToken ct)
    {
        var userId = principal.UserId();
        if (!await db.Series.AnyAsync(s => s.Id == seriesId, ct) || !await db.Users.AnyAsync(u => u.Id == userId, ct))
        {
            return TypedResults.NotFound();
        }

        var optOut = await db.SeriesNotificationOptOuts.FirstOrDefaultAsync(o => o.UserId == userId && o.SeriesId == seriesId, ct);
        if (request.Notify && optOut is not null)
        {
            db.SeriesNotificationOptOuts.Remove(optOut);
        }
        else if (!request.Notify && optOut is null)
        {
            db.SeriesNotificationOptOuts.Add(new SeriesNotificationOptOut { UserId = userId, SeriesId = seriesId });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two clicks at once: the other request already wrote the same thing.
        }

        return TypedResults.NoContent();
    }
}
