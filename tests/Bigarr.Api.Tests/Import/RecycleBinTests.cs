using Bigarr.Api.Infrastructure.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bigarr.Api.Tests.Import;

public sealed class RecycleBinTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-bin-").FullName;
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));

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
    public void Without_a_bin_the_file_is_deleted_for_good()
    {
        var bin = new RecycleBin(_time, NullLogger<RecycleBin>.Instance);
        var file = Write("movies/The Matrix (1999)/old.mkv");

        Assert.Null(bin.RemoveFile(file, null, Path.Combine(_root, "movies")));
        Assert.False(File.Exists(file));
        Assert.Null(bin.RemoveFile(file, null, null));
    }

    [Fact]
    public void With_a_bin_the_file_keeps_its_path_relative_to_the_root_and_collisions_get_a_stamp()
    {
        var bin = new RecycleBin(_time, NullLogger<RecycleBin>.Instance);
        var binPath = Path.Combine(_root, "bin");
        var library = Path.Combine(_root, "movies");

        var first = bin.RemoveFile(Write("movies/The Matrix (1999)/old.mkv"), binPath, library);
        Assert.Equal(Path.Combine(binPath, "The Matrix (1999)", "old.mkv"), first);
        Assert.True(File.Exists(first));
        Assert.Equal(_time.GetUtcNow().UtcDateTime, File.GetLastWriteTimeUtc(first!), TimeSpan.FromSeconds(2));

        var second = bin.RemoveFile(Write("movies/The Matrix (1999)/old.mkv"), binPath, library);
        Assert.Equal(Path.Combine(binPath, "The Matrix (1999)", "old_20260915120000.mkv"), second);
    }

    [Fact]
    public void Cleanup_deletes_what_is_older_than_the_retention_and_prunes_empty_folders()
    {
        var bin = new RecycleBin(_time, NullLogger<RecycleBin>.Instance);
        var binPath = Path.Combine(_root, "bin");
        var old = Write("bin/A (2000)/old.mkv");
        var fresh = Write("bin/B (2001)/fresh.mkv");
        File.SetLastWriteTimeUtc(old, _time.GetUtcNow().UtcDateTime.AddDays(-10));
        File.SetLastWriteTimeUtc(fresh, _time.GetUtcNow().UtcDateTime.AddDays(-2));

        Assert.Equal(1, bin.Cleanup(binPath, 7));
        Assert.False(File.Exists(old));
        Assert.False(Directory.Exists(Path.GetDirectoryName(old)));
        Assert.True(File.Exists(fresh));
        Assert.Equal(0, bin.Cleanup(binPath, 0));
    }

    private string Write(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "data");
        return path;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
