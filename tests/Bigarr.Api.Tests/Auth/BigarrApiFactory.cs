using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Auth;

/// <summary>Boots the real application on an isolated config directory (fresh SQLite database, seeded admin).</summary>
public class BigarrApiFactory : WebApplicationFactory<Program>
{
    public const string AdminUsername = "admin";

    public const string AdminPassword = "test-password-123";

    private readonly string _configDirectory;
    private readonly bool _deleteOnDispose;
    private readonly IReadOnlyDictionary<string, string> _settings;
    private readonly TimeProvider? _time;

    public BigarrApiFactory()
        : this(null, null, deleteOnDispose: true)
    {
    }

    /// <param name="configDirectory">Reuse a directory to simulate a restart on the same database.</param>
    /// <param name="settings">Extra configuration keys, e.g. <c>Bigarr:Downloads:StalledTimeoutMinutes</c>.</param>
    /// <param name="time">A clock the test controls; the app's <see cref="TimeProvider"/> registration is replaced.</param>
    protected BigarrApiFactory(string? configDirectory, IReadOnlyDictionary<string, string>? settings, bool deleteOnDispose = true, TimeProvider? time = null)
    {
        _configDirectory = configDirectory ?? Path.Combine(Path.GetTempPath(), $"bigarr-api-test-{Guid.NewGuid():N}");
        _settings = settings ?? new Dictionary<string, string>();
        _deleteOnDispose = deleteOnDispose;
        _time = time;
    }

    public string ConfigDirectory => _configDirectory;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Bigarr:ConfigDirectory", _configDirectory);
        builder.UseSetting("Bigarr:InitialAdminPassword", AdminPassword);
        // The queue is polled by hand in the tests (POST /api/v1/queue/refresh) so every step is deterministic.
        builder.UseSetting("Bigarr:Downloads:Enabled", "false");
        // Scheduled tasks run through POST /api/v1/system/tasks/{name}/run, never on their own.
        builder.UseSetting("Bigarr:Scheduler:Enabled", "false");
        // TheXEM is never called from the tests; the XEM tests swap in recorded answers.
        builder.UseSetting("Bigarr:Xem:Enabled", "false");
        // Nor plex.tv: the geolocation goes to a closed port unless a test sets a fake one.
        builder.UseSetting("Bigarr:PlexTv:BaseUrl", "http://127.0.0.1:9");
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        if (_time is not null)
        {
            builder.ConfigureServices(services => services.AddSingleton(_time));
        }
    }

    /// <summary>
    /// A client that keeps its session. Not the built-in cookie handling: a test that freezes the clock in the past gets
    /// a cookie that expires 14 days after that date, which the real clock then drops (see <see cref="SessionCookies"/>).
    /// </summary>
    public HttpClient CreateClientWithCookies() => CreateDefaultClient(new SessionCookies());

    public HttpClient CreateAnonymousClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            SqliteConnection.ClearAllPools();
            if (!_deleteOnDispose)
            {
                return;
            }

            try
            {
                Directory.Delete(_configDirectory, recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }
}

/// <summary>A factory with its own settings or directory, for tests that build it themselves (xUnit fixtures need a single public constructor).</summary>
public sealed class ConfiguredBigarrApiFactory(string? configDirectory, IReadOnlyDictionary<string, string>? settings, bool deleteOnDispose = true, TimeProvider? time = null)
    : BigarrApiFactory(configDirectory, settings, deleteOnDispose, time);

/// <summary>
/// Keeps the cookies the server sets whatever their expiry date, since the server signs them with the test's clock and
/// the cookie jar checks them against the real one. A cookie set to an empty value (sign-out) is still removed.
/// </summary>
internal sealed class SessionCookies : DelegatingHandler
{
    private readonly CookieContainer _jar = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var header = _jar.GetCookieHeader(uri);
        if (header.Length > 0)
        {
            request.Headers.Remove("Cookie");
            request.Headers.Add("Cookie", header);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            foreach (var value in values)
            {
                _jar.SetCookies(uri, WithoutExpiry(value));
            }
        }

        return response;
    }

    private static string WithoutExpiry(string setCookie)
    {
        var parts = setCookie.Split(';');
        var nameValue = parts[0];
        if (nameValue.EndsWith('='))
        {
            return setCookie; // a removal: its date in the past is what removes it
        }

        return string.Join(';', parts.Where(p =>
            !p.TrimStart().StartsWith("expires=", StringComparison.OrdinalIgnoreCase)
            && !p.TrimStart().StartsWith("max-age=", StringComparison.OrdinalIgnoreCase)));
    }
}
