using Bigarr.Domain.Library;

namespace Bigarr.Api.Features.Library;

/// <summary>Scans one root folder of its kind. The runner picks the scanner from the root's <see cref="MediaKind"/>.</summary>
public interface IRootFolderScanner
{
    MediaKind Kind { get; }

    Task ScanAsync(int rootFolderId, ScanProgress progress, CancellationToken ct);
}
