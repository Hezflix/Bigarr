using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Bigarr.Api.Features.Search;

/// <summary>
/// Versions waiting for an automatic search. Adding a film, a failed download and the wanted page enqueue; the runner
/// searches one version at a time so the indexers see a steady trickle, never a burst.
/// </summary>
public sealed class SearchQueue
{
    private readonly Channel<SearchRequest> _channel = Channel.CreateUnbounded<SearchRequest>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<int, byte> _pending = new();

    public ChannelReader<SearchRequest> Reader => _channel.Reader;

    /// <summary>False when the version is already waiting or being searched.</summary>
    public bool TryEnqueue(int versionId, SearchTrigger trigger) => _pending.TryAdd(versionId, 0) && _channel.Writer.TryWrite(new SearchRequest(versionId, trigger));

    public bool IsPending(int versionId) => _pending.ContainsKey(versionId);

    public int Pending => _pending.Count;

    public void Done(int versionId) => _pending.TryRemove(versionId, out _);
}

/// <summary>Drains <see cref="SearchQueue"/>.</summary>
public sealed class SearchRunner(SearchQueue queue, IServiceScopeFactory scopeFactory, ILogger<SearchRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var results = await scope.ServiceProvider.GetRequiredService<AutomaticSearchService>().SearchAsync([request.VersionId], request.Trigger, stoppingToken);
                    foreach (var result in results)
                    {
                        logger.LogInformation("Automatic search ({Trigger}) for {Movie} [{Version}]: {Outcome}{Detail}",
                            AutomaticSearchService.TriggerName(request.Trigger), result.MovieTitle, result.VersionName, result.Outcome,
                            result.Outcome == AutomaticSearchOutcome.Grabbed ? $" → {result.ReleaseTitle}" : result.Detail is null ? string.Empty : $" ({result.Detail})");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Automatic search of version {VersionId} crashed", request.VersionId);
                }
                finally
                {
                    queue.Done(request.VersionId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }
}
