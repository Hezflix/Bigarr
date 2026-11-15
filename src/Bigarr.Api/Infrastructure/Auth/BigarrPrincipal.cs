using System.Security.Claims;
using Bigarr.Domain.Auth;

namespace Bigarr.Api.Infrastructure.Auth;

public static class BigarrPrincipal
{
    public static ClaimsPrincipal ForUser(User user, string scheme, ApiKey? apiKey = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString()),
        };
        claims.AddRange(user.Effective.Granted().Select(p => new Claim(BigarrClaims.Permission, p.ToString())));

        if (scheme == AuthSchemes.Session)
        {
            claims.Add(new Claim(BigarrClaims.SessionStamp, SessionStamps.Of(user)));
        }

        if (apiKey is not null)
        {
            claims.Add(new Claim(BigarrClaims.ApiKeyId, apiKey.Id.ToString()));
            claims.Add(new Claim(BigarrClaims.CompatView, apiKey.CompatView.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    }

    public static int UserId(this ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static bool IsAdmin(this ClaimsPrincipal principal) => principal.IsInRole(nameof(UserRole.Admin));

    /// <summary>Whether the user may do <paramref name="permission"/>: an admin always may.</summary>
    public static bool Has(this ClaimsPrincipal principal, UserPermission permission) =>
        principal.IsAdmin() || principal.HasClaim(BigarrClaims.Permission, permission.ToString());

    public static IEnumerable<string> Permissions(this ClaimsPrincipal principal) =>
        principal.FindAll(BigarrClaims.Permission).Select(c => c.Value);

    public static CompatView CompatView(this ClaimsPrincipal principal) =>
        Domain.Auth.CompatView.TryParse(principal.FindFirstValue(BigarrClaims.CompatView), out var view) ? view : Domain.Auth.CompatView.Default;
}
