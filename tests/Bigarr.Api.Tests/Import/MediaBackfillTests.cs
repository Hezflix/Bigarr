using Bigarr.Api.Features.Import;
using Bigarr.Api.Infrastructure.Media;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Indexers;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Import;

/// <summary>
/// The hourly backfill takes the first files without media info. A file ffprobe cannot read used to stay among the
/// first ones for ever, so once a batch of them sat at the lowest ids no other file was ever probed.
/// </summary>
public sealed class MediaBackfillTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-backfill-").FullName;

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
    public async Task Unreadable_files_do_not_hold_the_backfill_back()
    {
        var clock = new MutableTimeProvider(Start);
        var reader = new FakeMediaInfoReader();
        using var factory = new BackfillApiFactory(clock, reader);
        var files = await SeedAsync(factory, 3);
        reader.Broken.Add(files[0].Path);
        reader.Broken.Add(files[1].Path);

        var first = await BackfillAsync(factory, batchSize: 2);
        Assert.Equal(0, first.Probed);
        Assert.Equal(3, first.Remaining);
        Assert.Equal([files[0].Path, files[1].Path], reader.Calls);

        reader.Calls.Clear();
        var second = await BackfillAsync(factory, batchSize: 2);
        Assert.Equal(1, second.Probed);
        Assert.Equal(2, second.Remaining);
        Assert.Equal([files[2].Path], reader.Calls);

        reader.Calls.Clear();
        clock.Advance(TimeSpan.FromHours(23));
        await BackfillAsync(factory, batchSize: 2);
        Assert.Empty(reader.Calls);

        clock.Advance(TimeSpan.FromHours(2));
        await BackfillAsync(factory, batchSize: 2);
        Assert.Equal([files[0].Path, files[1].Path], reader.Calls);
    }

    [Fact]
    public async Task A_file_missing_from_disk_is_recorded_as_tried_too()
    {
        var clock = new MutableTimeProvider(Start);
        var reader = new FakeMediaInfoReader();
        using var factory = new BackfillApiFactory(clock, reader);
        var files = await SeedAsync(factory, 3);
        File.Delete(files[0].Path);
        File.Delete(files[1].Path);

        var first = await BackfillAsync(factory, batchSize: 2);
        Assert.Equal(0, first.Probed);
        Assert.Empty(reader.Calls);

        var second = await BackfillAsync(factory, batchSize: 2);
        Assert.Equal(1, second.Probed);
        Assert.Equal([files[2].Path], reader.Calls);

        File.WriteAllBytes(files[0].Path, new byte[16]);
        reader.Calls.Clear();
        clock.Advance(TimeSpan.FromHours(25));
        var third = await BackfillAsync(factory, batchSize: 2);
        Assert.Equal(1, third.Probed);
        Assert.Equal(1, third.Remaining);
        Assert.Equal([files[0].Path], reader.Calls);
    }

    private static async Task<BackfillResponse> BackfillAsync(BigarrApiFactory factory, int batchSize)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MediaMaintenance>().BackfillAsync(batchSize, CancellationToken.None);
    }

    /// <summary>Film files without media info, in id order, each one an empty file on disk.</summary>
    private async Task<List<(int Id, string Path)>> SeedAsync(BigarrApiFactory factory, int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var profile = await db.QualityProfiles.FirstAsync();
        var root = new RootFolder { Path = _root };
        db.RootFolders.Add(root);

        var seeded = new List<(MediaFile File, string Path)>();
        for (var i = 0; i < count; i++)
        {
            var title = $"Backfill Film {i}";
            var movie = new Movie
            {
                TmdbId = 960_000 + i,
                Title = title,
                SortTitle = SortTitle.Build(title),
                Year = 2026,
                TmdbStatus = MovieStatus.Released,
                DefaultRootFolder = root,
            };
            var version = new MovieVersion
            {
                Movie = movie,
                Name = "1080p",
                IsDefault = true,
                Monitored = true,
                QualityProfile = profile,
                RootFolder = root,
                FolderName = $"{title} (2026)",
                MinimumAvailability = MinimumAvailability.Released,
            };
            var file = new MediaFile { RelativePath = $"{title}.mkv", Size = 16, QualityId = ParserQuality.Bluray1080p.Id };
            version.File = file;
            movie.Versions.Add(version);
            db.Movies.Add(movie);
            var path = Path.Combine(_root, version.FolderName, file.RelativePath);
            seeded.Add((file, path));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, new byte[16]);
        }

        await db.SaveChangesAsync();
        return seeded.OrderBy(s => s.File.Id).Select(s => (s.File.Id, s.Path)).ToList();
    }

    private sealed class BackfillApiFactory(TimeProvider time, FakeMediaInfoReader reader) : BigarrApiFactory(null, null, deleteOnDispose: true, time)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMediaInfoReader>();
                services.AddSingleton<IMediaInfoReader>(reader);
            });
        }
    }

    /// <summary>Reads nothing from the paths in <see cref="Broken"/>, a small answer from any other, and remembers what it was asked.</summary>
    private sealed class FakeMediaInfoReader : IMediaInfoReader
    {
        public HashSet<string> Broken { get; } = [];

        public List<string> Calls { get; } = [];

        public bool IsAvailable => true;

        public Task<MediaInfo?> ReadAsync(string path, string? releaseTitle, CancellationToken ct)
        {
            Calls.Add(path);
            return Task.FromResult<MediaInfo?>(Broken.Contains(path) ? null : new MediaInfo { VideoCodec = "h264", Width = 1920, Height = 1080 });
        }
    }
}
