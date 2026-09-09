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

        if (apiKey is not null)
        {
            claims.Add(new Claim(BigarrClaims.ApiKeyId, apiKey.Id.ToString()));
            claims.Add(new Claim(BigarrClaims.CompatView, apiKey.CompatView.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    }

    public static int UserId(this ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static CompatView CompatView(this ClaimsPrincipal principal) =>
        Domain.Auth.CompatView.TryParse(principal.FindFirstValue(BigarrClaims.CompatView), out var view) ? view : Domain.Auth.CompatView.Default;
}
