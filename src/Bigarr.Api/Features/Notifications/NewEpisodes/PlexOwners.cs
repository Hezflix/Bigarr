using System.Collections.Concurrent;
using Bigarr.Api.Infrastructure.Auth.External;
using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Auth;
using Bigarr.Domain.MediaServers;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Notifications.NewEpisodes;

/// <summary>
/// The Bigarr user who owns a Plex server. Plex numbers its owner 1 in sessions, in its history and in Tautulli's, so
/// the owner's plays cannot be matched to a Plex sign-in by account id as a friend's are. The server's token is the
/// owner's: plex.tv says whose account it is (<c>/api/v2/user</c>), and the user signed in with that account is the
/// owner. When no user is (plex.tv down, the owner never signed in with Plex), the first admin is: whoever set Bigarr
/// up with the owner's token. plex.tv's answer is kept 12 hours, a failure 5 minutes.
/// </summary>
public sealed class PlexOwners(IServiceProvider services, TimeProvider time, ILogger<PlexOwners> logger)
{
    private static readonly TimeSpan Keep = TimeSpan.FromHours(12);
    private static readonly TimeSpan KeepFailure = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<int, (string Token, string? AccountId, DateTimeOffset Until)> _accounts = new();

    /// <summary>The user each Plex server's owner is, by server id; a server whose owner is no one is left out.</summary>
    public async Task<Dictionary<int, int>> UsersAsync(BigarrDbContext db, IEnumerable<int> serverIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(serverIds);

        var ids = serverIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var servers = await db.MediaServers.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        var firstAdmin = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Admin && u.DisabledAt == null)
            .OrderBy(u => u.Id)
            .Select(u => (int?)u.Id)
            .FirstOrDefaultAsync(ct);

        var owners = new Dictionary<int, int>();
        foreach (var server in servers)
        {
            var account = server.Kind == MediaServerKind.Plex ? await AccountAsync(server, ct) : null;
            var user = account is null
                ? null
                : await db.ExternalLogins.AsNoTracking()
                    .Where(l => l.Provider == PlexIdentityProvider.ProviderKey && l.ExternalId == account)
                    .Select(l => (int?)l.UserId)
                    .FirstOrDefaultAsync(ct);
            if ((user ?? firstAdmin) is { } owner)
            {
                owners[server.Id] = owner;
            }
        }

        return owners;
    }

    private async Task<string?> AccountAsync(MediaServer server, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (_accounts.TryGetValue(server.Id, out var known) && known.Until > now && string.Equals(known.Token, server.Token, StringComparison.Ordinal))
        {
            return known.AccountId;
        }

        string? accountId = null;
        if (!string.IsNullOrWhiteSpace(server.Token))
        {
            try
            {
                accountId = (await services.GetRequiredService<PlexTvClient>().AccountAsync(server.Token, ct))?.Id;
            }
            catch (MediaServerException ex)
            {
                logger.LogDebug("plex.tv did not say who owns {Server}: {Error}", server.Name, ex.Message);
            }
        }

        _accounts[server.Id] = (server.Token, accountId, now + (accountId is null ? KeepFailure : Keep));
        return accountId;
    }
}
