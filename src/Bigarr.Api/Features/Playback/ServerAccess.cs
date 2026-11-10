using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.MediaServers;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Playback;

/// <summary>One server's pass: how many users are still on it and how many left, or why plex.tv could not tell.</summary>
public sealed record ServerAccessPass(string Server, int On, int Off, string? Error);

/// <summary>
/// Whether each user still has access to the server, from plex.tv's list of the accounts the owner shares with
/// (<c>/api/users</c>, the list Tautulli marks users deleted by): on when plex.tv lists the server among the account's,
/// or the account is a Plex Home member; the owner always is. Kept in the database, so plex.tv down leaves the last
/// known answer; read once in a while, it changes rarely.
/// </summary>
public sealed class ServerAccess(BigarrDbContext db, PlexClient plex, PlexTvClient plexTv, TimeProvider time, ILogger<ServerAccess> logger)
{
    public async Task<List<ServerAccessPass>> RunAllAsync(CancellationToken ct)
    {
        var servers = await db.MediaServers
            .Where(s => s.Enabled && s.Kind == MediaServerKind.Plex && s.Token != string.Empty)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);
        var passes = new List<ServerAccessPass>(servers.Count);
        foreach (var server in servers)
        {
            passes.Add(await RunAsync(server, ct));
        }

        return passes;
    }

    public async Task<ServerAccessPass> RunAsync(MediaServer server, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);

        List<PlexTvFriend> friends;
        try
        {
            friends = await plexTv.FriendsAsync(server.Token, ct);
        }
        catch (MediaServerException ex)
        {
            logger.LogWarning("Could not ask plex.tv who {Server} is shared with: {Error}", server.Name, ex.Message);
            return new ServerAccessPass(server.Name, 0, 0, ex.Message);
        }

        // Without the server's id (the server is down), any server of the owner's shared with the account counts.
        string? machineId = null;
        try
        {
            machineId = await plex.MachineIdentifierAsync(server, ct);
        }
        catch (MediaServerException ex)
        {
            logger.LogDebug("Plex {Server} did not give its id: {Error}", server.Name, ex.Message);
        }

        var shared = friends
            .Where(f => f.Home || (machineId is null ? f.Servers.Count > 0 : f.Servers.Contains(machineId, StringComparer.Ordinal)))
            .Select(f => f.Id)
            .ToHashSet(StringComparer.Ordinal);

        var now = time.GetUtcNow().UtcDateTime;
        var users = await db.MediaServerUsers.Where(u => u.MediaServerId == server.Id).ToListAsync(ct);
        foreach (var user in users)
        {
            var on = user.IsOwner || shared.Contains(user.ExternalId);
            if (user.OnServer != on && user.OnServerCheckedAt is not null)
            {
                logger.LogInformation("{User} is {State} {Server}", user.ShownName, on ? "back on" : "no longer on", server.Name);
            }

            user.OnServer = on;
            user.OnServerCheckedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return new ServerAccessPass(server.Name, users.Count(u => u.OnServer == true), users.Count(u => u.OnServer == false), null);
    }
}

/// <summary>Who still has access to the servers, from plex.tv, every few hours. Read-only, so it runs in shadow mode too.</summary>
public sealed class ServerAccessJob(ServerAccess access) : IScheduledJob
{
    public const string JobName = "plexServerAccess";

    public string Name => JobName;

    public string DisplayName => "Plex server access";

    public TimeSpan Interval => TimeSpan.FromHours(6);

    public bool Enabled => true;

    public TimeSpan InitialDelay => TimeSpan.FromMinutes(2);

    public bool RunsWhenSchedulerDisabled => true;

    public async Task<string?> RunAsync(CancellationToken ct)
    {
        var passes = await access.RunAllAsync(ct);
        if (passes.Count == 0)
        {
            return "No Plex server";
        }

        if (passes.All(p => p.Error is not null))
        {
            throw new InvalidOperationException(string.Join(" · ", passes.Select(p => $"{p.Server}: {p.Error}")));
        }

        return string.Join(" · ", passes.Select(p => p.Error is null ? $"{p.Server}: {p.On} on, {p.Off} off" : $"{p.Server}: failed, {p.Error}"));
    }
}
