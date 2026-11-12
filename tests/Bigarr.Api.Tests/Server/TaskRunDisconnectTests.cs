using System.Net.Http.Json;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Tests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Server;

/// <summary>A task run by hand belongs to the app, not to the request that asked for it.</summary>
public sealed class TaskRunDisconnectTests
{
    [Fact]
    public async Task A_task_run_by_hand_finishes_and_is_recorded_when_its_caller_goes_away()
    {
        var job = new GatedJob();
        using var factory = new GatedJobFactory(job);
        var client = await ClientAsync(factory);

        using var caller = new CancellationTokenSource();
        var request = client.PostAsync($"/api/v1/system/tasks/{GatedJob.JobName}/run", null, caller.Token);
        await job.Started.Task.WaitAsync(TimeSpan.FromSeconds(20));

        // The tab is closed while the job works.
        await caller.CancelAsync();
        await Task.Delay(200);
        job.Gate.TrySetResult();
        try
        {
            await request.WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (OperationCanceledException)
        {
            // the caller is gone: whatever the request became is nobody's to read
        }

        var task = await WaitUntilAsync(async () =>
        {
            var tasks = (await client.GetFromJsonAsync<List<ScheduledTaskResponse>>("/api/v1/system/tasks", Json))!;
            return tasks.SingleOrDefault(t => t.Name == GatedJob.JobName && t.LastRunAt is not null);
        }, "the run to be recorded");
        Assert.True(task.LastSucceeded);
        Assert.Equal("finished", task.LastSummary);
        Assert.False(job.Cancelled, "the job was cut by its caller going away");
    }

    private sealed class GatedJobFactory(GatedJob job) : BigarrApiFactory(null, null)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddScoped<IScheduledJob>(_ => job));
        }
    }

    private sealed class GatedJob : IScheduledJob
    {
        public const string JobName = "gatedTestJob";

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Cancelled { get; private set; }

        public string Name => JobName;

        public string DisplayName => "Gated test job";

        public TimeSpan Interval => TimeSpan.FromHours(1);

        public bool Enabled => false;

        public TimeSpan InitialDelay => TimeSpan.Zero;

        public async Task<string?> RunAsync(CancellationToken ct)
        {
            Started.TrySetResult();
            try
            {
                await Gate.Task.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }

            return "finished";
        }
    }
}
