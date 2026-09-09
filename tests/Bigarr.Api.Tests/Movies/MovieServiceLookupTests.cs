using Bigarr.Api.Features.Movies;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bigarr.Api.Tests.Movies;

public sealed class MovieServiceLookupTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task A_failing_provider_is_a_502_not_a_crash()
    {
        await using var db = _database.CreateContext();
        var service = new MovieService(db, new ThrowingProvider(new HttpRequestException("connection reset")), TimeProvider.System, NullLogger<MovieService>.Instance);

        var result = await service.LookupAsync("tmdb:603", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status502BadGateway, result.Failure!.StatusCode);
    }

    [Fact]
    public async Task A_provider_timeout_is_a_502_too()
    {
        await using var db = _database.CreateContext();
        var service = new MovieService(db, new ThrowingProvider(new TaskCanceledException("timeout")), TimeProvider.System, NullLogger<MovieService>.Instance);

        var result = await service.LookupAsync("matrix", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status502BadGateway, result.Failure!.StatusCode);
    }

    [Fact]
    public async Task A_bare_number_falls_back_to_a_title_search_when_the_year_search_is_empty()
    {
        await using var db = _database.CreateContext();
        var provider = new RecordingProvider();
        var service = new MovieService(db, provider, new FixedTimeProvider(new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero)), NullLogger<MovieService>.Instance);

        await service.LookupAsync("blade runner 2049", CancellationToken.None);
        await service.LookupAsync("blade runner 2012", CancellationToken.None);
        await service.LookupAsync("some film (2012)", CancellationToken.None);

        Assert.Equal(
            [
                ("blade runner 2049", (int?)null),      // 2049 is not a plausible year: title search only
                ("blade runner", 2012), ("blade runner 2012", null), // plausible year, empty → retry with the full term
                ("some film", 2012),                    // parentheses are unambiguous: no retry
            ],
            provider.Searches);
    }

    private sealed class ThrowingProvider(Exception exception) : IMetadataProvider
    {
        public string Name => "throwing";

        public Task<MovieMetadata?> GetMovieAsync(int tmdbId, CancellationToken cancellationToken) => throw exception;

        public Task<MovieMetadata?> GetMovieByImdbIdAsync(string imdbId, CancellationToken cancellationToken) => throw exception;

        public Task<IReadOnlyList<MovieMetadata>> SearchAsync(string query, int? year, CancellationToken cancellationToken) => throw exception;
    }

    private sealed class RecordingProvider : IMetadataProvider
    {
        public List<(string Query, int? Year)> Searches { get; } = [];

        public string Name => "recording";

        public Task<MovieMetadata?> GetMovieAsync(int tmdbId, CancellationToken cancellationToken) => Task.FromResult<MovieMetadata?>(null);

        public Task<MovieMetadata?> GetMovieByImdbIdAsync(string imdbId, CancellationToken cancellationToken) => Task.FromResult<MovieMetadata?>(null);

        public Task<IReadOnlyList<MovieMetadata>> SearchAsync(string query, int? year, CancellationToken cancellationToken)
        {
            Searches.Add((query, year));
            return Task.FromResult<IReadOnlyList<MovieMetadata>>([]);
        }
    }
}
