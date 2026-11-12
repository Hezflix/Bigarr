using Bigarr.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Shadow;

/// <summary>Passes asked by Radarr's webhook (see <see cref="FollowWakeups"/>).</summary>
public sealed class RadarrFollowWakeups(IServiceScopeFactory scopes, ILogger<RadarrFollowWakeups> logger) : FollowWakeups(scopes, logger)
{
    protected override string Source => "Radarr";

    protected override Task<bool> IsEnabledAsync(BigarrDbContext db, int id, CancellationToken ct) =>
        db.FollowedRadarrs.AnyAsync(f => f.Id == id && f.Enabled, ct);

    protected override async Task<(string Name, string Summary)> PassAsync(IServiceProvider services, int id, bool fullList, CancellationToken ct)
    {
        var pass = await services.GetRequiredService<RadarrFollower>().SyncAsync(id, ct, fullList);
        return (pass.Name, pass.Summary);
    }
}
