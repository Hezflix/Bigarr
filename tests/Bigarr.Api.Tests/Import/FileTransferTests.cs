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
    public async Task A_move_goes_through_the_partial_name_and_replaces_what_a_killed_transfer_left_there()
    {
        var source = Write("a/move-me.mkv", 4096);
        var moved = Path.Combine(_root, "d", "moved.mkv");

        // What a transfer killed half-way leaves behind: never read by anything, overwritten by the retry.
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        await File.WriteAllBytesAsync(moved + ".bigarr-partial", new byte[100]);

        Assert.Equal(TransferOutcome.Moved, await _transfer.TransferAsync(source, moved, TransferMode.Move, CancellationToken.None));
        Assert.False(File.Exists(source));
        Assert.Equal(4096, new FileInfo(moved).Length);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "d"), "*.bigarr-partial"));
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

    [Fact]
    public async Task Replacing_brings_the_new_file_in_before_the_old_one_leaves()
    {
        var source = Write("downloads/new.mkv", 2048);
        var destination = Write("library/Movie/Movie.mkv", 512);
        var oldBytes = File.ReadAllBytes(destination);
        var newBytes = File.ReadAllBytes(source);
        var bin = Path.Combine(_root, "bin");
        var sawStagedBesideOld = false;

        var outcome = await _transfer.ReplaceAsync(source, destination, TransferMode.Copy, () =>
        {
            // The new file is complete next to the old one when the old one is asked to leave.
            sawStagedBesideOld = File.Exists(destination + ".bigarr-partial") && File.ReadAllBytes(destination).SequenceEqual(oldBytes);
            Directory.CreateDirectory(bin);
            File.Move(destination, Path.Combine(bin, "Movie.mkv"));
        }, CancellationToken.None);

        Assert.Equal(TransferOutcome.Copied, outcome);
        Assert.True(sawStagedBesideOld);
        Assert.Equal(newBytes, File.ReadAllBytes(destination));
        Assert.Equal(oldBytes, File.ReadAllBytes(Path.Combine(bin, "Movie.mkv")));
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(destination)!, "*.bigarr-partial"));
    }

    [Fact]
    public async Task A_replacement_whose_transfer_fails_leaves_the_old_file_alone()
    {
        var source = Write("downloads/new.mkv", 2048);
        var destination = Write("library/Movie/Movie.mkv", 512);
        var oldBytes = File.ReadAllBytes(destination);
        var retired = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _transfer.ReplaceAsync(source, destination, TransferMode.Copy, () => retired = true, new CancellationToken(canceled: true)));

        Assert.False(retired);
        Assert.Equal(oldBytes, File.ReadAllBytes(destination));
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(destination)!, "*.bigarr-partial"));
    }

    [Fact]
    public async Task A_replacement_whose_old_file_cannot_be_retired_puts_everything_back()
    {
        foreach (var mode in new[] { TransferMode.Copy, TransferMode.Move })
        {
            var source = Write($"downloads/{mode}/new.mkv", 2048);
            var destination = Write($"library/{mode}/Movie.mkv", 512);
            var oldBytes = File.ReadAllBytes(destination);
            var sourceBytes = File.ReadAllBytes(source);

            await Assert.ThrowsAsync<IOException>(() =>
                _transfer.ReplaceAsync(source, destination, mode, () => throw new IOException("locked"), CancellationToken.None));

            Assert.Equal(oldBytes, File.ReadAllBytes(destination));
            Assert.Equal(sourceBytes, File.ReadAllBytes(source));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(destination)!, "*.bigarr-partial"));
        }
    }

    [Fact]
    public async Task A_partial_file_left_by_a_killed_replacement_is_overwritten()
    {
        var source = Write("downloads/new.mkv", 2048);
        var destination = Write("library/Movie/Movie.mkv", 512);
        var newBytes = File.ReadAllBytes(source);
        await File.WriteAllBytesAsync(destination + ".bigarr-partial", new byte[100]);

        await _transfer.ReplaceAsync(source, destination, TransferMode.Copy, () => File.Delete(destination), CancellationToken.None);

        Assert.Equal(newBytes, File.ReadAllBytes(destination));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(destination)!, "*.bigarr-partial"));
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
