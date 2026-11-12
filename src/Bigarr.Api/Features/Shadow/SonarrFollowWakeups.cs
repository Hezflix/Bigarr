using Bigarr.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Shadow;

/// <summary>Passes asked by Sonarr's webhook (see <see cref="FollowWakeups"/>).</summary>
public sealed class SonarrFollowWakeups(IServiceScopeFactory scopes, ILogger<SonarrFollowWakeups> logger) : FollowWakeups(scopes, logger)
{
    protected override string Source => "Sonarr";

    protected override Task<bool> IsEnabledAsync(BigarrDbContext db, int id, CancellationToken ct) =>
        db.FollowedSonarrs.AnyAsync(f => f.Id == id && f.Enabled, ct);

    protected override async Task<(string Name, string Summary)> PassAsync(IServiceProvider services, int id, bool fullList, CancellationToken ct)
    {
        var pass = await services.GetRequiredService<SonarrFollower>().SyncAsync(id, ct, fullList);
        return (pass.Name, pass.Summary);
    }
}
