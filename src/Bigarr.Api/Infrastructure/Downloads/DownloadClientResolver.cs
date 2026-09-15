using Bigarr.Domain.Downloads;

namespace Bigarr.Api.Infrastructure.Downloads;

/// <summary>Finds the protocol implementation behind a configured client.</summary>
public sealed class DownloadClientResolver(IEnumerable<IDownloadClient> implementations)
{
    private readonly Dictionary<DownloadClientImplementation, IDownloadClient> _byImplementation = implementations.ToDictionary(i => i.Implementation);

    public IDownloadClient For(DownloadClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return _byImplementation.TryGetValue(client.Implementation, out var implementation)
            ? implementation
            : throw new DownloadClientException(DownloadClientFailureKind.Request, $"No implementation for download client type {client.Implementation}.");
    }
}
