namespace Bigarr.Api.Infrastructure.Persistence;

/// <summary>Runs once at startup, after migrations, inside a DI scope (seeding, backfills…).</summary>
public interface IStartupTask
{
    Task RunAsync(CancellationToken cancellationToken);
}
