using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Tests.Search;

/// <summary>
/// The Wanted list reads light rows and remembers each file's custom formats between requests; the algorithm it replaced
/// loaded every film and matched every file on every call. Both run on the same seeded library and must give the same
/// list: same versions in the same order, same kinds, qualities, scores and totals, through every edit that could leave
/// a remembered match stale.
/// </summary>
public sealed class WantedEquivalenceTests
{
    private const int PageSize = 7;
    private const int Bluray1080p = 7;
    private const int Webdl1080p = 3;
    private const int Remux1080p = 30;
    private const int Bluray720p = 6;
    private const int Hdtv720p = 4;

    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public async Task The_light_list_gives_what_the_full_graph_gave_through_every_edit()
    {
        using var factory = new ConfiguredBigarrApiFactory(null, null, time: new MutableTimeProvider(Now));
        _ = factory.CreateClient(); // boots the app: schema, quality definitions, system profiles

        await SeedAsync(factory);
        var seeded = await AssertSameAsync(factory);
        Assert.Contains(seeded, i => i.Kind == WantedFilter.Missing);
        Assert.Contains(seeded, i => i.Kind == WantedFilter.CutoffUnmet && i.CurrentQuality == "Bluray-1080p"); // at the cutoff quality, below the score
        Assert.Contains(seeded, i => i.Kind == WantedFilter.CutoffUnmet && i.CurrentQuality != "Bluray-1080p" && i.CurrentQuality != "Remux-1080p");

        // A format's specifications change what files match: every remembered match is stale.
        await EditAsync(factory, async db =>
        {
            var tier = await db.CustomFormats.SingleAsync(f => f.Name == "Tier");
            tier.SpecificationsJson = Specifications("""{ "name": "Tier", "specifications": [ { "name": "Groups", "implementation": "ReleaseGroupSpecification", "fields": { "value": "^(FLUX|NTb|GRP)$" } } ] }""");
        });
        await AssertSameAsync(factory);

        // Scores and cutoffs live on the profile: nothing remembered depends on them.
        await EditAsync(factory, async db =>
        {
            var tier = await db.CustomFormats.SingleAsync(f => f.Name == "Tier");
            var hd = await db.QualityProfiles.Include(p => p.FormatScores).SingleAsync(p => p.Name == "HD scored");
            hd.CutoffFormatScore = 60;
            hd.FormatScores.Single(s => s.CustomFormatId == tier.Id).Score = 40;
        });
        var listed = await AssertSameAsync(factory);

        // Files on the list (their match is remembered) re-tagged in place: same id, other inputs, another score.
        var retagged = Scored(listed).Take(6).ToList();
        Assert.NotEmpty(retagged);
        await EditAsync(factory, async db =>
        {
            foreach (var file in await db.MediaFiles.Where(f => retagged.Contains(f.VersionId)).ToListAsync())
            {
                var group = file.ReleaseGroup == "YIFY" ? "FLUX" : "YIFY";
                file.OriginalReleaseTitle = $"Retagged.2001.1080p.BluRay.x264-{group}";
                file.RelativePath = $"Retagged.2001.1080p.BluRay.x264-{group}.mkv";
                file.ReleaseGroup = group;
            }
        });
        listed = await AssertSameAsync(factory);

        // A refresh corrects the year of films on the list: the "Recent" format comes or goes.
        var refreshed = Scored(listed).Take(6).ToList();
        await EditAsync(factory, async db =>
        {
            foreach (var movie in await db.Movies.Where(m => m.Versions.Any(v => refreshed.Contains(v.Id))).ToListAsync())
            {
                movie.Year = movie.Year >= 2015 ? 2000 : 2020;
            }
        });
        await AssertSameAsync(factory);

        // An upgrade replaces the file, a delete leaves the version missing.
        await EditAsync(factory, async db =>
        {
            var files = await db.MediaFiles.OrderBy(f => f.Id).ToListAsync();
            var replaced = files[3];
            db.MediaFiles.Remove(replaced);
            db.MediaFiles.Add(new MediaFile
            {
                VersionId = replaced.VersionId,
                RelativePath = "Upgrade.2001.1080p.BluRay.REMUX.AVC-NTb.mkv",
                OriginalReleaseTitle = "Upgrade.2001.1080p.BluRay.REMUX.AVC-NTb",
                ReleaseGroup = "NTb",
                Size = 30_000_000_000,
                QualityId = Remux1080p,
                Languages = ["French"],
            });
            db.MediaFiles.Remove(files[8]);
        });
        await AssertSameAsync(factory);

        // Versions moved to another profile.
        await EditAsync(factory, async db =>
        {
            var unscored = await db.QualityProfiles.SingleAsync(p => p.Name == "Unscored");
            foreach (var version in await db.MovieVersions.Where(v => v.File != null).OrderBy(v => v.Id).Take(3).ToListAsync())
            {
                version.QualityProfileId = unscored.Id;
            }
        });
        await AssertSameAsync(factory);

        // A deleted format no longer counts anywhere.
        await EditAsync(factory, async db => db.CustomFormats.Remove(await db.CustomFormats.SingleAsync(f => f.Name == "Recent")));
        await AssertSameAsync(factory);
    }

    /// <summary>Both algorithms on every filter: pages walked to the end, the ids the search-all queues, the job's batch.</summary>
    private static async Task<List<WantedItemResponse>> AssertSameAsync(ConfiguredBigarrApiFactory factory)
    {
        // A scope per check: its scorer reads the formats again, as a new request does.
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var wanted = services.GetRequiredService<WantedService>();
        var ct = CancellationToken.None;

        List<WantedItemResponse> all = [];
        foreach (var filter in new WantedFilter?[] { null, WantedFilter.Missing, WantedFilter.CutoffUnmet })
        {
            var expected = await ReferenceAsync(services, filter);
            var actual = new List<WantedItemResponse>();
            for (var page = 1; ; page++)
            {
                var result = await wanted.PageAsync(filter, page, PageSize, ct);
                Assert.Equal(expected.Count, result.Total);
                actual.AddRange(result.Items);
                if (result.Items.Count < PageSize)
                {
                    break;
                }
            }

            Assert.Equal(expected, actual);
            Assert.Equal(expected.Select(i => i.VersionId), await wanted.VersionIdsAsync(filter, ct));
            if (filter is null)
            {
                all = expected;
            }
        }

        var now = Now.UtcDateTime;
        var due = all
            .Where(w => w.NextSearchAt is null || w.NextSearchAt <= now)
            .OrderBy(w => w.LastSearchAt ?? DateTime.MinValue)
            .ThenBy(w => w.MovieId)
            .Take(10)
            .Select(w => w.VersionId);
        Assert.Equal(due, await wanted.DueAsync(10, ct));
        return all;
    }

    /// <summary>The list as it was computed before: the whole graph loaded, every file matched against every format.</summary>
    private static async Task<List<WantedItemResponse>> ReferenceAsync(IServiceProvider services, WantedFilter? filter)
    {
        var db = services.GetRequiredService<BigarrDbContext>();
        var contexts = services.GetRequiredService<TargetContextFactory>();
        var scorer = services.GetRequiredService<CustomFormatScorer>();
        var settings = services.GetRequiredService<IOptions<SearchOptions>>().Value;
        var ct = CancellationToken.None;

        var movies = await contexts.MoviesWithVersions().ToListAsync(ct);
        var active = (await db.GrabTargets.AsNoTracking().Where(t => t.Active).Select(t => t.TargetId).ToListAsync(ct)).ToHashSet();
        var definitions = await db.QualityDefinitions.AsNoTracking().ToDictionaryAsync(q => q.Id, ct);
        var calculator = await scorer.CalculatorAsync(ct);

        var items = new List<WantedItemResponse>();
        foreach (var movie in movies.OrderBy(m => m.SortTitle).ThenBy(m => m.Year))
        {
            foreach (var version in movie.Versions.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id))
            {
                if (version.Mode != VersionMode.Managed || !version.Monitored || active.Contains(version.Id) || !Availability.IsAvailable(movie, version.MinimumAvailability, Today))
                {
                    continue;
                }

                var profile = version.QualityProfile;
                WantedFilter kind;
                string? currentQuality = null;
                int? currentScore = null;
                if (version.File is null)
                {
                    kind = WantedFilter.Missing;
                }
                else
                {
                    if (!profile.UpgradeAllowed)
                    {
                        continue;
                    }

                    var file = version.File;
                    var matched = calculator.Match(CustomFormatScorer.FileInput(file.OriginalReleaseTitle, file.RelativePath, file.Size, file.IndexerFlags, file.Quality, file.Languages, file.Edition, file.ReleaseGroup, movie.Year, movie.OriginalLanguage));
                    currentScore = ProfileRanking.FormatScore(profile, matched.Select(m => m.Id));
                    currentQuality = file.Quality?.Name ?? definitions.GetValueOrDefault(file.QualityId)?.Name;
                    if (ProfileRanking.CutoffMet(profile, file.QualityId) && currentScore >= profile.CutoffFormatScore)
                    {
                        continue;
                    }

                    kind = WantedFilter.CutoffUnmet;
                }

                if (filter is not null && filter != kind)
                {
                    continue;
                }

                items.Add(new WantedItemResponse(
                    movie.Id,
                    movie.Title,
                    movie.Year,
                    movie.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
                    version.Id,
                    version.Name,
                    profile.Name,
                    kind,
                    currentQuality,
                    currentScore,
                    WantedService.CutoffName(profile, definitions),
                    version.LastSearchAt,
                    version.SearchAttempts,
                    SearchBackoff.NextAllowed(version.LastSearchAt, version.SearchAttempts, settings)));
            }
        }

        return items;
    }

    private static async Task EditAsync(ConfiguredBigarrApiFactory factory, Func<BigarrDbContext, Task> edit)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        await edit(db);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// About eighty films covering every branch: missing, unavailable, cutoff by quality, cutoff by score, scores that cannot
    /// reach or cannot miss the cutoff, upgrades not allowed, unmonitored and one-shot versions, active and finished grabs,
    /// several versions per film, accented and tied sort titles, a film without a year.
    /// </summary>
    private static async Task SeedAsync(ConfiguredBigarrApiFactory factory)
    {
        var random = new Random(20261108);
        await EditAsync(factory, async db =>
        {
            var formats = new Dictionary<string, CustomFormat>();
            foreach (var json in new[]
            {
                """{ "name": "Tier", "specifications": [ { "name": "Groups", "implementation": "ReleaseGroupSpecification", "fields": { "value": "^(FLUX|NTb)$" } } ] }""",
                """{ "name": "LQ", "specifications": [ { "name": "Groups", "implementation": "ReleaseGroupSpecification", "fields": { "value": "^(YIFY)$" } } ] }""",
                """{ "name": "x265", "specifications": [ { "name": "x265", "implementation": "ReleaseTitleSpecification", "fields": { "value": "[xh][ ._-]?265|\\bHEVC\\b" } } ] }""",
                """{ "name": "French", "specifications": [ { "name": "French", "implementation": "LanguageSpecification", "fields": { "value": 2 } } ] }""",
                """{ "name": "Recent", "specifications": [ { "name": "Recent", "implementation": "YearSpecification", "fields": { "min": 2015, "max": 2030 } } ] }""",
            })
            {
                var definition = CustomFormatJson.ParseMany(json)[0];
                var format = new CustomFormat { Name = definition.Name, SpecificationsJson = CustomFormatJson.SerializeSpecifications(definition.Specifications) };
                db.CustomFormats.Add(format);
                formats[format.Name] = format;
            }

            await db.SaveChangesAsync();

            var template = await db.QualityProfiles.AsNoTracking().SingleAsync(p => p.Name == QualitySeeder.HdProfileName);
            QualityProfile Profile(string name, bool upgrades, int cutoffScore, params (string Format, int Score)[] scores)
            {
                var profile = new QualityProfile
                {
                    Name = name,
                    PreferredLanguage = "en",
                    Items = template.Items.Select(i => new QualityProfileItem { GroupId = i.GroupId, GroupName = i.GroupName, QualityIds = [.. i.QualityIds], Allowed = i.Allowed }).ToList(),
                    Cutoff = template.Cutoff,
                    UpgradeAllowed = upgrades,
                    CutoffFormatScore = cutoffScore,
                };
                foreach (var (format, score) in scores)
                {
                    profile.FormatScores.Add(new ProfileFormatScore { CustomFormat = formats[format], Score = score });
                }

                db.QualityProfiles.Add(profile);
                return profile;
            }

            QualityProfile[] profiles =
            [
                Profile("HD scored", true, 100, ("Tier", 150), ("LQ", -500), ("x265", -50), ("French", 20), ("Recent", 30)),
                Profile("No upgrades", false, 100, ("Tier", 150), ("LQ", -500)),
                Profile("Unscored", true, 0),
                Profile("Unreachable", true, 1000, ("Tier", 150), ("French", 20)),
                Profile("Cannot miss", true, -600, ("LQ", -500), ("x265", -50)),
            ];
            var root = new RootFolder { Path = "/data/movies" };
            db.RootFolders.Add(root);

            string[] titles = ["Été", "Eagle", "École du ciel", "Zebra", "Ångström", "The Abyss", "Abyss", "Øresund", "Zoë", "Le Mépris", "Mépris", "A-Team", "Ateam", "Ça", "Casa"];
            int[] qualities = [Bluray1080p, Bluray1080p, Webdl1080p, Remux1080p, Bluray720p, Hdtv720p];
            string?[] groups = ["FLUX", "NTb", "YIFY", "GRP", null];
            for (var n = 0; n < 80; n++)
            {
                // The first titles come twice with the same year: ties on sort title and year, which the film id breaks.
                var title = titles[n % titles.Length] + (n < titles.Length * 2 ? string.Empty : $" {n / titles.Length}");
                int? year = n == 5 ? null : n < titles.Length * 2 ? 1990 + (n % titles.Length) : 1990 + random.Next(0, 36);
                var movie = new Movie
                {
                    TmdbId = 800_000 + n,
                    Title = title,
                    SortTitle = SortTitle.Build(title),
                    Year = year,
                    OriginalLanguage = random.Next(4) == 0 ? "fr" : "en",
                    TmdbStatus = MovieStatus.Released,
                    DefaultRootFolder = root,
                    Images = n % 3 == 0 ? [new MediaImage(ImageType.Poster, $"https://example.org/poster-{n}.jpg")] : [],
                };
                switch (random.Next(6))
                {
                    case 0: // in cinemas, no home release yet
                        movie.InCinemasDate = Today.AddDays(-20);
                        movie.TmdbStatus = MovieStatus.InCinemas;
                        break;
                    case 1: // announced
                        movie.InCinemasDate = Today.AddDays(100);
                        movie.TmdbStatus = MovieStatus.Announced;
                        break;
                    case 2: // cinema run over, no home date
                        movie.InCinemasDate = Today.AddDays(-120);
                        break;
                    case 3: // no dates at all
                        break;
                    default:
                        movie.InCinemasDate = Today.AddYears(-3);
                        movie.DigitalReleaseDate = Today.AddYears(-3).AddDays(90);
                        break;
                }

                var versionCount = 1 + random.Next(3);
                for (var v = 0; v < versionCount; v++)
                {
                    var version = new MovieVersion
                    {
                        Movie = movie,
                        Name = v == 0 ? "1080p" : $"v{v}",
                        IsDefault = v == 0,
                        Mode = v > 0 && random.Next(4) == 0 ? VersionMode.OneShot : VersionMode.Managed,
                        Monitored = random.Next(10) > 0,
                        QualityProfile = profiles[random.Next(profiles.Length)],
                        RootFolder = root,
                        FolderName = $"{title} ({year})",
                        MinimumAvailability = (MinimumAvailability)(1 + random.Next(3)),
                        LastSearchAt = random.Next(3) == 0 ? null : Now.UtcDateTime.AddHours(-random.Next(1, 400)),
                        SearchAttempts = random.Next(5),
                    };
                    movie.Versions.Add(version);
                    if (random.Next(3) > 0)
                    {
                        var group = groups[random.Next(groups.Length)];
                        var codec = random.Next(4) == 0 ? "x265" : "x264";
                        var release = $"{title.Replace(' ', '.')}.{year}.1080p.BluRay.{codec}" + (group is null ? string.Empty : $"-{group}");
                        version.File = new MediaFile
                        {
                            RelativePath = random.Next(2) == 0 ? $"{title} ({year}).mkv" : release + ".mkv",
                            OriginalReleaseTitle = random.Next(5) == 0 ? null : release,
                            ReleaseGroup = random.Next(4) == 0 ? null : group,
                            Size = 8_000_000_000 + random.Next(1_000_000),
                            QualityId = qualities[random.Next(qualities.Length)],
                            Languages = random.Next(3) == 0 ? ["French"] : ["English"],
                        };
                    }

                    // A download in progress takes a version off the list; a finished one does not.
                    if (random.Next(8) == 0)
                    {
                        var grab = new Grab { ReleaseTitle = $"{title}.{year}.1080p.WEB-DL-FLUX", Size = 1 };
                        grab.AddTarget(version);
                        if (random.Next(2) == 0)
                        {
                            grab.TransitionTo(GrabState.Imported);
                        }

                        db.Grabs.Add(grab);
                    }
                }

                db.Movies.Add(movie);
            }
        });
    }

    /// <summary>Versions on the list whose score the "HD scored" profile computes from several formats.</summary>
    private static IEnumerable<int> Scored(List<WantedItemResponse> listed) =>
        listed.Where(i => i.Kind == WantedFilter.CutoffUnmet && i.QualityProfile == "HD scored").Select(i => i.VersionId);

    private static string Specifications(string json) => CustomFormatJson.SerializeSpecifications(CustomFormatJson.ParseMany(json)[0].Specifications);
}
