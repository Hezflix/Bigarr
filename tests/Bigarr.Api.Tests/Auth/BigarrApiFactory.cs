using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Bigarr.Api.Tests.Auth;

/// <summary>Boots the real application on an isolated config directory (fresh SQLite database, seeded admin).</summary>
public sealed class BigarrApiFactory : WebApplicationFactory<Program>
{
    public const string AdminUsername = "admin";

    public const string AdminPassword = "test-password-123";

    private readonly string _configDirectory = Path.Combine(Path.GetTempPath(), $"bigarr-api-test-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Bigarr:ConfigDirectory", _configDirectory);
        builder.UseSetting("Bigarr:InitialAdminPassword", AdminPassword);
        // The queue is polled by hand in the tests (POST /api/v1/queue/refresh) so every step is deterministic.
        builder.UseSetting("Bigarr:Downloads:Enabled", "false");
    }

    public HttpClient CreateClientWithCookies() =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });

    public HttpClient CreateAnonymousClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            SqliteConnection.ClearAllPools();
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
