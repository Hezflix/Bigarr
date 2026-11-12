using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Infrastructure.Auth;

/// <summary>
/// What ties a session cookie to the password it was opened with. The cookie is stateless (nothing server-side to
/// revoke), so it carries a fingerprint of the user's password hash, checked on every request: once the password
/// changes, every other session's fingerprint is stale and the cookie is refused. The current fingerprints are kept
/// in memory so the check costs no query per request.
/// </summary>
public sealed class SessionStamps
{
    private readonly ConcurrentDictionary<int, string> _current = new();

    public static string Of(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Of(user.PasswordHash);
    }

    private static string Of(string passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)), 0, 12);

    /// <summary>The password hash of <paramref name="user"/> changed: its fingerprint is read again.</summary>
    public void Changed(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        _current[user.Id] = Of(user);
    }

    /// <summary>True when <paramref name="stamp"/> is the fingerprint of the user's password today. False for a user that is gone.</summary>
    public async Task<bool> IsCurrentAsync(int userId, string? stamp, BigarrDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (stamp is null)
        {
            return false;
        }

        if (!_current.TryGetValue(userId, out var current))
        {
            var hash = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.PasswordHash).FirstOrDefaultAsync(ct);
            if (hash is null)
            {
                return false;
            }

            current = _current.GetOrAdd(userId, Of(hash));
        }

        return string.Equals(current, stamp, StringComparison.Ordinal);
    }
}
