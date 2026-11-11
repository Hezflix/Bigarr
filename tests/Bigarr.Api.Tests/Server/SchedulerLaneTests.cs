using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Tests.Server;

/// <summary>A long job (a series refresh over a whole library) runs in its own lane: the short ones keep their cadence.</summary>
public sealed class SchedulerLaneTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task A_short_job_runs_while_a_long_one_is_still_running()
    {
        var gate = new TaskCompletionSource();
        var longStarted = new TaskCompletionSource();
        var shortRan = new TaskCompletionSource();
        var services = new ServiceCollection();
        services.AddScoped<BigarrDbContext>(_ => _database.CreateContext());
        services.AddScoped<IScheduledJob>(_ => new Job("aLong", lane: true, () =>
        {
            longStarted.TrySetResult();
            return gate.Task;
        }, TimeSpan.Zero));
        services.AddScoped<IScheduledJob>(_ => new Job("bShort", lane: false, () =>
        {
            shortRan.TrySetResult();
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(1)));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        await using var provider = services.BuildServiceProvider();

        var options = Options.Create(new SchedulerOptions { Enabled = true });
        var state = new SchedulerState();
        var signal = new SchedulerSignal();
        var runner = new TaskRunner(provider.GetRequiredService<IServiceScopeFactory>(), state, signal, options, TimeProvider.System, NullLogger<TaskRunner>.Instance);
        var scheduler = new SchedulerRunner(runner, signal, options, TimeProvider.System, NullLogger<SchedulerRunner>.Instance);

        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            // The long job is due first and blocks on its gate; the short one, due a second later, still runs.
            Assert.Same(longStarted.Task, await Task.WhenAny(longStarted.Task, Task.Delay(TimeSpan.FromSeconds(20))));
            Assert.Same(shortRan.Task, await Task.WhenAny(shortRan.Task, Task.Delay(TimeSpan.FromSeconds(20))));
            Assert.True(state.IsRunning("aLong"), "the long job is still in its lane");
        }
        finally
        {
            gate.TrySetResult();
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    private sealed class Job(string name, bool lane, Func<Task> work, TimeSpan initialDelay) : IScheduledJob
    {
        public string Name => name;

        public string DisplayName => name;

        public TimeSpan Interval => TimeSpan.FromHours(1);

        public bool Enabled => true;

        public TimeSpan InitialDelay => initialDelay;

        public bool LongRunning => lane;

        public async Task<string?> RunAsync(CancellationToken ct)
        {
            await work();
            return null;
        }
    }
}
