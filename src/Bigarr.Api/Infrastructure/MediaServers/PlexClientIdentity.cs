using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Infrastructure.MediaServers;

/// <summary>
/// This install's <c>X-Plex-Client-Identifier</c>, sent to plex.tv and to Plex servers alike: a GUID generated the
/// first time it is needed, kept in <see cref="InstanceSettings"/> (Radarr and Sonarr keep theirs in their config the
/// same way), then held in memory. A sign-in PIN is read back with the identifier that created it, so it must not change.
/// </summary>
public sealed class PlexClientIdentity(IServiceScopeFactory scopes)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _value;

    public async Task<string> GetAsync(CancellationToken ct)
    {
        if (_value is { } known)
        {
            return known;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_value is { } raced)
            {
                return raced;
            }

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var settings = await db.InstanceSettings.FirstOrDefaultAsync(s => s.Id == InstanceSettings.SingletonId, ct);
            if (settings is null)
            {
                settings = new InstanceSettings();
                db.InstanceSettings.Add(settings);
            }

            if (string.IsNullOrWhiteSpace(settings.PlexClientIdentifier))
            {
                settings.PlexClientIdentifier = Guid.NewGuid().ToString();
                await db.SaveChangesAsync(ct);
            }

            return _value = settings.PlexClientIdentifier;
        }
        finally
        {
            _gate.Release();
        }
    }
}
