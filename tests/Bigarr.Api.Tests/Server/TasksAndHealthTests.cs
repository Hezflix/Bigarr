using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Server.Health;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Server;

/// <summary>The scheduler is a table plus an API (blueprint §3.7); the health checks say what stops the loop.</summary>
public sealed class TasksAndHealthTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task Tasks_are_listed_and_run_on_demand()
    {
        var client = await ClientAsync(factory);

        var tasks = (await client.GetFromJsonAsync<List<ScheduledTaskResponse>>("/api/v1/system/tasks", Json))!;
        Assert.Equal([Bigarr.Api.Features.Server.Backup.BackupJob.JobName, CutoffUnmetSearchJob.JobName, HealthCheckJob.JobName, MediaMaintenanceJob.JobName, Bigarr.Api.Features.Movies.MetadataRefreshJob.JobName, Bigarr.Api.Features.Playback.PlexHistoryJob.JobName, Bigarr.Api.Features.Playback.ServerAccessJob.JobName, Bigarr.Api.Features.Shadow.RadarrFollowJob.JobName, RssSyncJob.JobName, Bigarr.Api.Features.Series.SceneMappingRefreshJob.JobName, Bigarr.Api.Features.Series.SeriesRefreshJob.JobName, Bigarr.Api.Features.Shadow.SonarrFollowJob.JobName], tasks.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));

        // The scheduler is off in the tests, like in shadow mode: only the read-only Plex jobs keep their schedule.
        var scheduled = tasks.Where(t => t.Enabled).ToList();
        Assert.Equal([Bigarr.Api.Features.Playback.PlexHistoryJob.JobName, Bigarr.Api.Features.Playback.ServerAccessJob.JobName], scheduled.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(scheduled, t => Assert.NotNull(t.NextRunAt));
        Assert.All(tasks.Except(scheduled), t => Assert.Null(t.NextRunAt));
        Assert.Equal(60, tasks.Single(t => t.Name == CutoffUnmetSearchJob.JobName).IntervalMinutes);

        var run = await client.PostAsync($"/api/v1/system/tasks/{RssSyncJob.JobName}/run", null);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        var outcome = (await run.Content.ReadFromJsonAsync<TaskRunResponse>(Json))!;
        Assert.True(outcome.Success);
        Assert.Equal("No indexer has RSS enabled", outcome.Summary);

        var maintenance = (await (await client.PostAsync($"/api/v1/system/tasks/{MediaMaintenanceJob.JobName}/run", null)).Content.ReadFromJsonAsync<TaskRunResponse>(Json))!;
        Assert.True(maintenance.Success);
        Assert.Contains("purged", maintenance.Summary, StringComparison.Ordinal);

        tasks = (await client.GetFromJsonAsync<List<ScheduledTaskResponse>>("/api/v1/system/tasks", Json))!;
        var rss = tasks.Single(t => t.Name == RssSyncJob.JobName);
        Assert.Equal(outcome.RanAt, rss.LastRunAt);
        Assert.Equal(outcome.Summary, rss.LastSummary);
        Assert.True(rss.LastSucceeded);
        Assert.NotNull(rss.LastDurationMs);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/v1/system/tasks/nope/run", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateAnonymousClient().GetAsync("/api/v1/system/tasks")).StatusCode);
    }

    [Fact]
    public async Task A_restart_neither_raises_standing_issues_again_nor_forgets_to_resolve_one_fixed_meanwhile()
    {
        var configDirectory = Path.Combine(Path.GetTempPath(), $"bigarr-health-restart-{Guid.NewGuid():N}");
        int raisedBefore;

        // First life: nothing is configured, the check raises its warnings once.
        using (var first = new ConfiguredBigarrApiFactory(configDirectory, null, deleteOnDispose: false))
        {
            var client = await ClientAsync(first);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/health/check", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/health/check", null)).StatusCode);
            raisedBefore = await EventsAsync(first, "healthIssue");
            Assert.True(raisedBefore >= 2, "the indexer and download client warnings were expected");
            Assert.Equal(0, await EventsAsync(first, "healthRestored"));
        }

        // Second life on the same database: one cause is fixed before the first check of this life.
        using (var second = new ConfiguredBigarrApiFactory(configDirectory, null, deleteOnDispose: true))
        {
            var client = await ClientAsync(second);
            var indexer = await AddMockIndexerAsync(client, "Mock health restart");
            var health = (await (await client.PostAsync("/api/v1/health/check", null)).Content.ReadFromJsonAsync<HealthResponse>(Json))!;
            Assert.DoesNotContain(health.Issues, i => i.Source == "indexers");
            Assert.Contains(health.Issues, i => i.Source == "downloadClients");

            // The download client warning was already standing: not raised again. The indexer one went away: resolved.
            Assert.Equal(raisedBefore, await EventsAsync(second, "healthIssue"));
            Assert.Equal(1, await EventsAsync(second, "healthRestored"));
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
        }
    }

    private static async Task<int> EventsAsync(BigarrApiFactory app, string type)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Bigarr.Api.Infrastructure.Persistence.BigarrDbContext>();
        return await db.Outbox.AsNoTracking().CountAsync(e => e.Type == type);
    }

    [Fact]
    public async Task Health_reports_what_is_missing_and_settles_once_configured()
    {
        var client = await ClientAsync(factory);

        var health = (await client.GetFromJsonAsync<HealthResponse>("/api/v1/health", Json))!;
        Assert.NotNull(health.CheckedAt);
        Assert.Contains(health.Issues, i => i.Source == "indexers" && i.Type == HealthIssueType.Warning && i.Message.StartsWith("No indexer is configured", StringComparison.Ordinal));
        Assert.Contains(health.Issues, i => i.Source == "downloadClients" && i.Type == HealthIssueType.Warning);
        Assert.Contains(health.Issues, i => i.Source == "metadata" && i.Type == HealthIssueType.Notice);

        var indexer = await AddMockIndexerAsync(client, "Mock health");
        var downloadClient = await AddMockClientAsync(client, "Mock health qbt");
        var missingRoot = await client.PostAsJsonAsync("/api/v1/rootfolders", new Bigarr.Api.Features.Library.AddRootFolderRequest(Path.Combine(Path.GetTempPath(), $"bigarr-gone-{Guid.NewGuid():N}")));
        try
        {
            var recheck = await client.PostAsync("/api/v1/health/check", null);
            Assert.Equal(HttpStatusCode.OK, recheck.StatusCode);
            health = (await recheck.Content.ReadFromJsonAsync<HealthResponse>(Json))!;
            Assert.DoesNotContain(health.Issues, i => i.Source == "indexers");
            Assert.DoesNotContain(health.Issues, i => i.Source == "downloadClients");
            Assert.Equal(2, health.Issues.Count(i => i.Source == "mock" && i.Type == HealthIssueType.Notice));
            if (missingRoot.StatusCode == HttpStatusCode.Created)
            {
                // The root folder was created on disk by the API; delete it to simulate an unmounted drive.
                var root = (await missingRoot.Content.ReadFromJsonAsync<Bigarr.Api.Features.Library.RootFolderResponse>(Json))!;
                TryDelete(root.Path);
                health = (await (await client.PostAsync("/api/v1/health/check", null)).Content.ReadFromJsonAsync<HealthResponse>(Json))!;
                Assert.Contains(health.Issues, i => i.Source == "rootFolders" && i.Type == HealthIssueType.Error && i.Message.Contains(root.Path, StringComparison.Ordinal));
                await client.DeleteAsync($"/api/v1/rootfolders/{root.Id}");
            }

            // Errors first, then warnings, then notices.
            var order = health.Issues.Select(i => i.Type).ToList();
            Assert.Equal(order.OrderByDescending(t => t).ToList(), order);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
        }
    }
}
