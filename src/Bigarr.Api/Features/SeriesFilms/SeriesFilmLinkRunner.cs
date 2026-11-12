using System.Collections.Concurrent;
using System.Threading.Channels;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Domain.Events;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.SeriesFilms;

/// <summary>What the link keeper should look at: one film, one series, or everything.</summary>
public sealed record LinkWork(int? MovieId, int? SeriesId)
{
    public static readonly LinkWork All = new(null, null);

    public string Key => MovieId is { } m ? $"m{m}" : SeriesId is { } s ? $"s{s}" : "*";
}

/// <summary>Pending link work, deduplicated; drained by <see cref="SeriesFilmLinkRunner"/>.</summary>
public sealed class SeriesFilmLinkQueue
{
    private readonly Channel<LinkWork> _channel = Channel.CreateUnbounded<LinkWork>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);

    public ChannelReader<LinkWork> Reader => _channel.Reader;

    public int Pending => _pending.Count;

    /// <summary>Held by every link keeper pass (<see cref="SeriesFilmLinker"/>): passes never overlap.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public void Enqueue(LinkWork work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (_pending.TryAdd(work.Key, 0))
        {
            _channel.Writer.TryWrite(work);
        }
    }

    public void Done(LinkWork work) => _pending.TryRemove(work.Key, out _);
}

/// <summary>
/// The link keeper's seam with the film engine (blueprint §3.9: modules react to dispatched events, never call each
/// other): a film file imported, upgraded, renamed or deleted, a version or film deleted, a default changed, each
/// queues its film. Shadow mode touches no file, so nothing is linked then.
/// </summary>
public sealed class SeriesFilmLinkRunner(SeriesFilmLinkQueue queue, EventStream events, IServiceScopeFactory scopeFactory, IOptions<ShadowOptions> shadow, ILogger<SeriesFilmLinkRunner> logger) : BackgroundService
{
    private static readonly HashSet<Type> FilmFileEventTypes =
    [
        typeof(FileImported), typeof(FileUpgraded), typeof(FileDeleted), typeof(FileRenamed), typeof(MovieDeleted), typeof(VersionDeleted), typeof(DefaultVersionChanged),
    ];

    /// <summary>The outbox names of those events (<c>imported</c>, <c>upgraded</c>…): only they are deserialized.</summary>
    private static readonly HashSet<string> FilmFileEvents = DomainEventCatalog.Entries.Where(e => FilmFileEventTypes.Contains(e.Clr)).Select(e => e.Type).ToHashSet(StringComparer.Ordinal);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(ListenAsync(stoppingToken), DrainAsync(stoppingToken));

    /// <summary>Maps a dispatched event to the film it concerns; null for any other event.</summary>
    public static LinkWork? WorkFor(string type, string payload)
    {
        if (!FilmFileEvents.Contains(type))
        {
            return null;
        }

        // A deleted film has no link left: its orphaned hardlinks are settled by any pass.
        return EventJson.Deserialize(type, payload) switch
        {
            MovieDeleted => LinkWork.All,
            FileImported e => new LinkWork(e.Movie.Id, null),
            FileUpgraded e => new LinkWork(e.Movie.Id, null),
            FileDeleted e => new LinkWork(e.Movie.Id, null),
            FileRenamed e => new LinkWork(e.Movie.Id, null),
            VersionDeleted e => new LinkWork(e.Movie.Id, null),
            DefaultVersionChanged e => new LinkWork(e.Movie.Id, null),
            _ => null,
        };
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var serverEvent in events.SubscribeAsync(ct))
            {
                if (WorkFor(serverEvent.Type, serverEvent.Payload) is { } work)
                {
                    queue.Enqueue(work);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var work in queue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    if (shadow.Value.Enabled)
                    {
                        continue;
                    }

                    await using var scope = scopeFactory.CreateAsyncScope();
                    var linker = scope.ServiceProvider.GetRequiredService<SeriesFilmLinker>();
                    _ = work switch
                    {
                        { MovieId: { } movieId } => await linker.ReconcileMovieAsync(movieId, ct),
                        { SeriesId: { } seriesId } => await linker.ReconcileSeriesAsync(seriesId, ct),
                        _ => await linker.ReconcileAllAsync(ct),
                    };
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Series film links ({Work}) crashed", work.Key);
                }
                finally
                {
                    queue.Done(work);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
    }
}

/// <summary>The safety net under the event seam: every link checked against the disk, a few times a day.</summary>
public sealed class SeriesFilmLinkJob(SeriesFilmLinker linker, IOptions<ShadowOptions> shadow) : IScheduledJob
{
    public const string JobName = "seriesFilmLinks";

    public string Name => JobName;

    public string DisplayName => "Series film links";

    public TimeSpan Interval => TimeSpan.FromHours(6);

    public bool Enabled => true;

    public TimeSpan InitialDelay => TimeSpan.FromMinutes(10);

    public async Task<string?> RunAsync(CancellationToken ct)
    {
        if (shadow.Value.Enabled)
        {
            return "Shadow mode: no file touched";
        }

        var pass = await linker.ReconcileAllAsync(ct);
        return $"{pass.Created} linked, {pass.Removed} removed, {pass.KeptAsFiles} kept as files, {pass.Failed} failed";
    }
}
