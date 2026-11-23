namespace Bigarr.Api.Tests.Auth;

/// <summary>
/// Keeps the cookies the app sets, by name, and sends them back. Expiry is the app's business, on the app's clock: a
/// cookie leaves the jar only when the app clears it (an empty value or <c>Max-Age=0</c>).
/// </summary>
public sealed class CookieJar : DelegatingHandler
{
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_cookies.Count > 0)
            {
                request.Headers.Remove("Cookie");
                request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
            }
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            lock (_lock)
            {
                foreach (var header in setCookies)
                {
                    var parts = header.Split(';', StringSplitOptions.TrimEntries);
                    var separator = parts[0].IndexOf('=', StringComparison.Ordinal);
                    if (separator <= 0)
                    {
                        continue;
                    }

                    var name = parts[0][..separator];
                    var value = parts[0][(separator + 1)..];
                    var cleared = value.Length == 0 || parts.Any(p => p.Equals("max-age=0", StringComparison.OrdinalIgnoreCase));
                    if (cleared)
                    {
                        _cookies.Remove(name);
                    }
                    else
                    {
                        _cookies[name] = value;
                    }
                }
            }
        }

        return response;
    }
}
