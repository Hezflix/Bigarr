using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Bigarr.Api.Tests.Compat.CompatTestHelpers;

namespace Bigarr.Api.Tests.Compat;

/// <summary>
/// Radarr's <c>MissingMoviesSearch</c> and <c>CutoffUnmetMoviesSearch</c> search every film the Wanted list holds, the
/// least recently searched first (MoviesSearchService, develop). The compat commands queue every version the key sees,
/// in that order, however long the list: they used to read one page of the Wanted list, 200 versions at most.
/// </summary>
public sealed class CompatWantedSearchTests
{
    private const int Films = 300;
    private const int Hdtv720p = 4;

    [Fact]
    public async Task The_wanted_search_commands_queue_every_visible_version_least_recently_searched_first()
    {
        using var factory = new NoSearchRunnerFactory();
        var session = await SessionAsync(factory);
        var (client, _) = await KeyClientAsync(factory, session, "dashboard", "default");
        var (missing, cutoffUnmet) = await SeedAsync(factory);
        Assert.True(missing.Count > 200, $"the test needs more missing versions than a Wanted page holds, got {missing.Count}");

        var queue = factory.Services.GetRequiredService<SearchQueue>();
        var response = await client.PostAsJsonAsync("/api/v3/command", new { name = "MissingMoviesSearch" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var command = await response.ReadJsonAsync();
        Assert.Equal("MissingMoviesSearch", command.GetProperty("name").GetString());
        Assert.Equal($"{missing.Count} version(s) queued for search", command.GetProperty("message").GetString());
        Assert.Equal(missing, Drain(queue));

        var cutoff = await (await client.PostAsJsonAsync("/api/v3/command", new { name = "CutoffUnmetMoviesSearch" })).ReadJsonAsync();
        Assert.Equal("CutoffUnmetMoviesSearch", cutoff.GetProperty("name").GetString());
        Assert.Equal($"{cutoffUnmet.Count} version(s) queued for search", cutoff.GetProperty("message").GetString());
        Assert.Equal(cutoffUnmet, Drain(queue));
    }

    /// <summary>What the runner would search next, in order; the versions stay pending, as they would until searched.</summary>
    private static List<int> Drain(SearchQueue queue)
    {
        var ids = new List<int>();
        while (queue.Reader.TryRead(out var request))
        {
            Assert.Equal(SearchTrigger.User, request.Trigger);
            ids.Add(request.VersionId);
        }

        return ids;
    }

    /// <summary>
    /// Films with a default version, missing, cutoff unmet or off the list (unmonitored, not available yet), some with a
    /// second missing version the default key does not see. Returns what each command should queue, in order.
    /// </summary>
    private static async Task<(List<int> Missing, List<int> CutoffUnmet)> SeedAsync(BigarrApiFactory factory)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTime.UtcNow;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var hd = await db.QualityProfiles.SingleAsync(p => p.Name == QualitySeeder.HdProfileName);
        var root = new RootFolder { Path = "/data/movies" };
        db.RootFolders.Add(root);

        var defaults = new List<MovieVersion>();
        for (var n = 0; n < Films; n++)
        {
            var title = $"Film {n:000}";
            var movie = new Movie
            {
                TmdbId = 900_000 + n,
                Title = title,
                SortTitle = SortTitle.Build(title),
                Year = 2020,
                TmdbStatus = MovieStatus.Released,
                InCinemasDate = today.AddYears(-3),
                DigitalReleaseDate = today.AddYears(-3).AddDays(90),
                DefaultRootFolder = root,
            };
            if (n % 23 == 0)
            {
                movie.TmdbStatus = MovieStatus.Announced;
                movie.InCinemasDate = today.AddDays(100);
                movie.DigitalReleaseDate = null;
            }

            MovieVersion Version(string name, bool isDefault) => new()
            {
                Movie = movie,
                Name = name,
                IsDefault = isDefault,
                Mode = VersionMode.Managed,
                Monitored = n % 17 != 0,
                QualityProfile = hd,
                RootFolder = root,
                FolderName = $"{title} (2020)",
                MinimumAvailability = MinimumAvailability.Released,
                // Never searched, or searched hours ago with ties: the film id breaks them.
                LastSearchAt = n % 5 == 0 ? null : now.AddHours(-(n * 7 % 40)),
            };

            var main = Version("1080p", isDefault: true);
            if (n % 11 == 0)
            {
                main.File = new MediaFile { RelativePath = $"{title} (2020).mkv", Size = 2_000_000_000, QualityId = Hdtv720p, Languages = ["English"] };
            }

            movie.Versions.Add(main);
            if (n % 4 == 0)
            {
                movie.Versions.Add(Version("4k", isDefault: false));
            }

            db.Movies.Add(movie);
            defaults.Add(main);
        }

        await db.SaveChangesAsync();

        var wanted = defaults
            .Where(v => v.Monitored && v.Movie.TmdbStatus == MovieStatus.Released)
            .OrderBy(v => v.LastSearchAt ?? DateTime.MinValue)
            .ThenBy(v => v.MovieId)
            .ToList();
        return (wanted.Where(v => v.File is null).Select(v => v.Id).ToList(), wanted.Where(v => v.File is not null).Select(v => v.Id).ToList());
    }

    /// <summary>The real app without the search runner, so the queue keeps what the commands put in it.</summary>
    private sealed class NoSearchRunnerFactory() : BigarrApiFactory(null, null)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                foreach (var runner in services.Where(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(SearchRunner)).ToList())
                {
                    services.Remove(runner);
                }
            });
        }
    }
}
