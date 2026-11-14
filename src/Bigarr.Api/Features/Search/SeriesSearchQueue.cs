using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Bigarr.Api.Features.Search;

/// <param name="EpisodeIds">The episodes to search for the version; null searches every missing one (the whole-series rule).</param>
public sealed record SeriesSearchRequest(int VersionId, IReadOnlyList<int>? EpisodeIds, SearchTrigger Trigger)
{
    /// <summary>One request per version and episode set waits at a time.</summary>
    public string Key => EpisodeIds is null ? $"{VersionId}:*" : $"{VersionId}:{string.Join(',', EpisodeIds.Order())}";
}

/// <summary>
/// Series searches waiting to run, the twin of <see cref="SearchQueue"/>: a failed download, a refresh that added recent
/// episodes and the Wanted page enqueue; the runner searches one request at a time so the indexers see a trickle.
/// </summary>
public sealed class SeriesSearchQueue
{
    private readonly Channel<SeriesSearchRequest> _channel = Channel.CreateUnbounded<SeriesSearchRequest>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);

    public ChannelReader<SeriesSearchRequest> Reader => _channel.Reader;

    /// <summary>False when the same request is already waiting or running.</summary>
    public bool TryEnqueue(SeriesSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _pending.TryAdd(request.Key, 0) && _channel.Writer.TryWrite(request);
    }

    public int Pending => _pending.Count;

    /// <summary>True while the request is waiting or being searched.</summary>
    public bool IsPending(SeriesSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _pending.ContainsKey(request.Key);
    }

    public void Done(SeriesSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _pending.TryRemove(request.Key, out _);
    }
}

/// <summary>Drains <see cref="SeriesSearchQueue"/>.</summary>
public sealed class SeriesSearchRunner(SeriesSearchQueue queue, IServiceScopeFactory scopeFactory, ILogger<SeriesSearchRunner> logger) : BackgroundService
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
                    var search = scope.ServiceProvider.GetRequiredService<SeriesAutomaticSearchService>();
                    var results = request.EpisodeIds is null
                        ? await search.SearchAsync([request.VersionId], request.Trigger, stoppingToken)
                        : [await search.SearchEpisodesAsync(request.VersionId, request.EpisodeIds, request.Trigger, stoppingToken)];
                    foreach (var result in results)
                    {
                        logger.LogInformation("Series search ({Trigger}) for {Series} [{Version}]: {Covered}/{Wanted} covered, {Grabbed} grabbed, {Picked} picked{Detail}",
                            AutomaticSearchService.TriggerName(request.Trigger), result.SeriesTitle, result.VersionName, result.Covered, result.Wanted, result.Grabbed, result.Picked,
                            result.Detail is null ? string.Empty : $" ({result.Detail})");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Series search of version {VersionId} crashed", request.VersionId);
                }
                finally
                {
                    queue.Done(request);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }
}
