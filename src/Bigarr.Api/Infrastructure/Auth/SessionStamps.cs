using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Infrastructure.Auth;

/// <summary>
/// What ties a session cookie to the user as they are now. The cookie is stateless (nothing server-side to revoke), so
/// it carries a fingerprint of the user's password hash, checked on every request: once the password changes, every
/// other session's fingerprint is stale and the cookie is refused; so is the cookie of a disabled user. A cookie whose
/// role or permissions are out of date is reissued with the current ones. The users' current state is kept in memory
/// so the check costs no query per request; whatever changes a user calls <see cref="Changed"/>.
/// </summary>
public sealed class SessionStamps
{
    private readonly ConcurrentDictionary<int, UserState> _current = new();

    /// <summary>A user as their sessions must see them.</summary>
    public sealed record UserState(string Stamp, bool Disabled, string Role, string Permissions);

    public static string Of(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Of(user.PasswordHash);
    }

    private static string Of(string passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)), 0, 12);

    /// <summary>The user changed (password, role, permissions, disabled): their state is read again.</summary>
    public void Changed(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        _current[user.Id] = StateOf(user);
    }

    /// <summary>
    /// The user's state when <paramref name="stamp"/> is still the fingerprint of their password and they are not
    /// disabled; null otherwise, and for a user that is gone.
    /// </summary>
    public async Task<UserState?> CurrentAsync(int userId, string? stamp, BigarrDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (stamp is null)
        {
            return null;
        }

        if (!_current.TryGetValue(userId, out var current))
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
            {
                return null;
            }

            current = _current.GetOrAdd(userId, StateOf(user));
        }

        return !current.Disabled && string.Equals(current.Stamp, stamp, StringComparison.Ordinal) ? current : null;
    }

    /// <summary>True when the session's claims still say what <paramref name="state"/> says.</summary>
    public static bool Matches(ClaimsPrincipal principal, UserState state)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(state);
        return principal.FindFirstValue(ClaimTypes.Role) == state.Role
            && string.Join(',', principal.FindAll(BigarrClaims.Permission).Select(c => c.Value)) == state.Permissions;
    }

    private static UserState StateOf(User user) =>
        new(Of(user), user.IsDisabled, user.Role.ToString(), string.Join(',', user.Effective.Granted()));
}
