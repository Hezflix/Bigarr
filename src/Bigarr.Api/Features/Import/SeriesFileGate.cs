using System.Collections.Concurrent;

namespace Bigarr.Api.Features.Import;

/// <summary>
/// One writer at a time on a show's files: an order switch, its undo and a rename each read every path, move files and
/// save once at the end. Two of them overlapping on the same show would each plan from paths the other is moving: the
/// second finds its targets taken, journals no move, and becomes the "latest change" whose undo restores nothing.
/// </summary>
public static class SeriesFileGate
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Gates = new();

    /// <summary>Waits for the show's turn; dispose the result to give it up.</summary>
    public static async Task<IDisposable> EnterAsync(int seriesId, CancellationToken ct)
    {
        var gate = Gates.GetOrAdd(seriesId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Turn(gate);
    }

    private sealed class Turn(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
