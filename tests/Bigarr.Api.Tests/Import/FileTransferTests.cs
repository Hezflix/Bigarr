using Bigarr.Api.Infrastructure.Files;
using Bigarr.Domain.Library;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bigarr.Api.Tests.Import;

public sealed class FileTransferTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-transfer-").FullName;
    private readonly FileTransfer _transfer = new(NullLogger<FileTransfer>.Instance);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public async Task Hardlink_or_copy_hardlinks_on_the_same_volume_and_keeps_the_source()
    {
        var source = Write("downloads/movie.mkv", 64 * 1024);
        var destination = Path.Combine(_root, "library", "Movie (2020)", "Movie (2020) [Bluray-1080p].mkv");

        var outcome = await _transfer.TransferAsync(source, destination, TransferMode.HardlinkOrCopy, CancellationToken.None);

        Assert.Equal(TransferOutcome.Hardlinked, outcome);
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(destination));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(destination));

        // Same inode: writing through one name is visible through the other.
        await File.AppendAllTextAsync(source, "x");
        Assert.Equal(new FileInfo(source).Length, new FileInfo(destination).Length);
    }

    [Fact]
    public async Task Copy_duplicates_and_move_relocates()
    {
        var source = Write("a/copy-me.mkv", 4096);
        var copied = Path.Combine(_root, "b", "copied.mkv");
        Assert.Equal(TransferOutcome.Copied, await _transfer.TransferAsync(source, copied, TransferMode.Copy, CancellationToken.None));
        Assert.True(File.Exists(source));
        Assert.Equal(4096, new FileInfo(copied).Length);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "b"), "*.bigarr-partial"));

        var moved = Path.Combine(_root, "c", "moved.mkv");
        Assert.Equal(TransferOutcome.Moved, await _transfer.TransferAsync(source, moved, TransferMode.Move, CancellationToken.None));
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(moved));
    }

    [Fact]
    public async Task An_existing_destination_is_never_overwritten()
    {
        var source = Write("a/src.mkv", 10);
        var destination = Write("b/dst.mkv", 20);

        await Assert.ThrowsAsync<IOException>(() => _transfer.TransferAsync(source, destination, TransferMode.Copy, CancellationToken.None));
        Assert.Equal(20, new FileInfo(destination).Length);
        await Assert.ThrowsAsync<FileNotFoundException>(() => _transfer.TransferAsync(Path.Combine(_root, "nope.mkv"), Path.Combine(_root, "x.mkv"), TransferMode.Copy, CancellationToken.None));
    }

    private string Write(string relative, int bytes)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = new byte[bytes];
        new Random(bytes).NextBytes(data);
        File.WriteAllBytes(path, data);
        return path;
    }
}
