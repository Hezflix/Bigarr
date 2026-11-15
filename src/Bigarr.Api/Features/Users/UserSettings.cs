using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Users;

/// <summary>What new users start with, kept on <see cref="InstanceSettings"/>; its row may not exist yet.</summary>
public static class UserSettings
{
    /// <summary>The settings, read-only; their defaults when the row does not exist yet.</summary>
    public static async Task<InstanceSettings> ReadAsync(BigarrDbContext db, CancellationToken ct) =>
        await db.InstanceSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == InstanceSettings.SingletonId, ct) ?? new InstanceSettings();

    /// <summary>The settings row, tracked, created when missing.</summary>
    public static async Task<InstanceSettings> EditAsync(BigarrDbContext db, CancellationToken ct)
    {
        var settings = await db.InstanceSettings.FirstOrDefaultAsync(s => s.Id == InstanceSettings.SingletonId, ct);
        if (settings is null)
        {
            settings = new InstanceSettings();
            db.InstanceSettings.Add(settings);
        }

        return settings;
    }
}
