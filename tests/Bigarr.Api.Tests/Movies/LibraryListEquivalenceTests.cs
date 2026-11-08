using System.Text.Json;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Api.Tests.Search;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Movies;

/// <summary>
/// The library list reads each file's custom formats from <see cref="FileFormatMatches"/>, which the Wanted list fills
/// too; the query it replaced matched every file on every load. Both run on the Wanted equivalence seed and must give
/// the same list through every edit that could leave a remembered match stale, with Wanted passes in between: neither
/// list may evict the other's matches.
/// </summary>
public sealed class LibraryListEquivalenceTests
{
    private const int Bluray1080p = 7;
    private const int Remux1080p = 30;

    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_remembered_matches_give_what_matching_every_file_gave_through_every_edit()
    {
        using var factory = new ConfiguredBigarrApiFactory(null, null, time: new MutableTimeProvider(Now));
        _ = factory.CreateClient(); // boots the app: schema, quality definitions, system profiles

        await WantedEquivalenceTests.SeedAsync(factory);
        var seeded = await AssertSameAsync(factory);
        var verdicts = seeded.SelectMany(m => m.Versions).Select(v => v.CutoffMet).ToList();
        Assert.Contains(true, verdicts);
        Assert.Contains(false, verdicts);
        Assert.Contains(null, verdicts);

        // A format's specifications change what files match: every remembered match is stale.
        await EditBothAsync(factory, async db =>
        {
            var tier = await db.CustomFormats.SingleAsync(f => f.Name == "Tier");
            tier.SpecificationsJson = WantedEquivalenceTests.Specifications("""{ "name": "Tier", "specifications": [ { "name": "Groups", "implementation": "ReleaseGroupSpecification", "fields": { "value": "^(FLUX|NTb|GRP)$" } } ] }""");
        });

        // Scores and cutoffs live on the profile: nothing remembered depends on them.
        await EditBothAsync(factory, async db =>
        {
            var tier = await db.CustomFormats.SingleAsync(f => f.Name == "Tier");
            var hd = await db.QualityProfiles.Include(p => p.FormatScores).SingleAsync(p => p.Name == "HD scored");
            hd.CutoffFormatScore = 60;
            hd.FormatScores.Single(s => s.CustomFormatId == tier.Id).Score = 40;
        });

        // Files whose match is remembered re-tagged in place: same id, other inputs, another verdict.
        await EditBothAsync(factory, async db =>
        {
            foreach (var file in await RememberedAsync(db))
            {
                var group = file.ReleaseGroup == "YIFY" ? "FLUX" : "YIFY";
                file.OriginalReleaseTitle = $"Retagged.2001.1080p.BluRay.x264-{group}";
                file.RelativePath = $"Retagged.2001.1080p.BluRay.x264-{group}.mkv";
                file.ReleaseGroup = group;
            }
        });

        // A refresh corrects the year of those films: the "Recent" format comes or goes.
        await EditBothAsync(factory, async db =>
        {
            var versions = (await RememberedAsync(db)).Select(f => f.VersionId).ToList();
            foreach (var movie in await db.Movies.Where(m => m.Versions.Any(v => versions.Contains(v.Id))).ToListAsync())
            {
                movie.Year = movie.Year >= 2015 ? 2000 : 2020;
            }
        });

        // An upgrade replaces a file, another is deleted.
        await EditBothAsync(factory, async db =>
        {
            var files = await RememberedAsync(db);
            var replaced = files[0];
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
            db.MediaFiles.Remove(files[1]);
        });

        // Versions moved to another profile, a one-shot version made managed.
        await EditBothAsync(factory, async db =>
        {
            var unscored = await db.QualityProfiles.SingleAsync(p => p.Name == "Unscored");
            foreach (var version in await db.MovieVersions.Where(v => v.File != null).OrderBy(v => v.Id).Take(3).ToListAsync())
            {
                version.QualityProfileId = unscored.Id;
            }

            var oneShot = await db.MovieVersions.Where(v => v.Mode == VersionMode.OneShot && v.File != null).OrderBy(v => v.Id).FirstAsync();
            oneShot.Mode = VersionMode.Managed;
        });

        // A deleted format no longer counts anywhere.
        await EditBothAsync(factory, async db => db.CustomFormats.Remove(await db.CustomFormats.SingleAsync(f => f.Name == "Recent")));
    }

    /// <summary>
    /// Edits, then checks the library list, a Wanted pass and the library list again. The Wanted pass prunes the shared
    /// matches: it must forget no file that still exists, so the second library load matches nothing again.
    /// </summary>
    private static async Task EditBothAsync(ConfiguredBigarrApiFactory factory, Func<BigarrDbContext, Task> edit)
    {
        await WantedEquivalenceTests.EditAsync(factory, edit);
        await AssertSameAsync(factory);

        var matches = factory.Services.GetRequiredService<FileFormatMatches>();
        var remembered = matches.Count;
        await WantedEquivalenceTests.AssertSameAsync(factory);
        Assert.True(matches.Count >= remembered, $"The Wanted pass forgot {remembered - matches.Count} of the library list's matches.");

        var count = matches.Count;
        await AssertSameAsync(factory);
        Assert.Equal(count, matches.Count);
    }

    private static async Task<List<MovieSummary>> AssertSameAsync(ConfiguredBigarrApiFactory factory)
    {
        // A scope per check: its scorer reads the formats again, as a new request does.
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var reference = new ReferenceQuery(services.GetRequiredService<BigarrDbContext>(), services.GetRequiredService<CustomFormatScorer>(), services.GetRequiredService<TimeProvider>());
        var expected = await reference.ListAsync(CancellationToken.None);
        var actual = await services.GetRequiredService<MovieSummaryQuery>().ListAsync(CancellationToken.None);

        // The records hold lists, compared by reference: compare what the endpoint would send.
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        return actual;
    }

    /// <summary>Files the library list judges by their formats: managed, in the scored profile, at the cutoff quality.</summary>
    private static Task<List<MediaFile>> RememberedAsync(BigarrDbContext db) =>
        db.MediaFiles
            .Where(f => f.Version.Mode == VersionMode.Managed && f.Version.QualityProfile.Name == "HD scored" && (f.QualityId == Bluray1080p || f.QualityId == Remux1080p))
            .OrderBy(f => f.Id)
            .Take(6)
            .ToListAsync();

    /// <summary>The library list as it was computed before: every managed file parsed and matched on every load.</summary>
    private sealed class ReferenceQuery(BigarrDbContext db, CustomFormatScorer scorer, TimeProvider time)
    {
        public async Task<List<MovieSummary>> ListAsync(CancellationToken ct)
        {
            var rows = await db.Movies.AsNoTracking()
                .OrderBy(m => m.SortTitle).ThenBy(m => m.Year)
                .Select(m => new MovieRow(
                    m.Id,
                    m.TmdbId,
                    m.Title,
                    m.SortTitle,
                    m.Year,
                    m.Images,
                    m.Ratings,
                    m.AddedAt,
                    m.TmdbStatus,
                    m.InCinemasDate,
                    m.DigitalReleaseDate,
                    m.PhysicalReleaseDate,
                    m.OriginalLanguage,
                    m.Versions.Select(v => new VersionRow(
                        v.Id,
                        v.Name,
                        v.Mode,
                        v.IsDefault,
                        v.Monitored,
                        v.QualityProfileId,
                        v.MinimumAvailability,
                        v.RootFolderId,
                        v.Tags.Select(t => t.Id).ToList(),
                        v.File == null
                            ? null
                            : new FileRow(v.File.QualityId, v.File.Size, v.File.RelativePath, v.File.OriginalReleaseTitle, v.File.Languages, v.File.IndexerFlags, v.File.Edition, v.File.ReleaseGroup)))
                        .ToList()))
                .AsSplitQuery()
                .ToListAsync(ct);

            var active = (await db.GrabTargets.AsNoTracking().Where(t => t.Active).Select(t => t.TargetId).ToListAsync(ct)).ToHashSet();
            var profiles = await db.QualityProfiles.AsNoTracking().Include(p => p.FormatScores).ToDictionaryAsync(p => p.Id, ct);
            var qualities = await db.QualityDefinitions.AsNoTracking().ToDictionaryAsync(q => q.Id, ct);
            var calculator = rows.Any(r => r.Versions.Any(v => v.File is not null)) ? await scorer.CalculatorAsync(ct) : null;
            var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

            return rows.Select(row =>
            {
                // The availability rules only read the status and the release dates.
                var movie = new Movie
                {
                    Title = row.Title,
                    SortTitle = row.SortTitle,
                    TmdbStatus = row.TmdbStatus,
                    InCinemasDate = row.InCinemasDate,
                    DigitalReleaseDate = row.DigitalReleaseDate,
                    PhysicalReleaseDate = row.PhysicalReleaseDate,
                };

                return new MovieSummary(
                    row.Id,
                    row.TmdbId,
                    row.Title,
                    row.SortTitle,
                    row.Year,
                    row.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
                    Availability.EffectiveStatus(movie, today),
                    row.Versions.Any(v => v.Monitored),
                    row.Versions
                        .OrderByDescending(v => v.IsDefault)
                        .ThenBy(v => v.Id)
                        .Select(v => new VersionSummary(
                            v.Id,
                            v.Name,
                            v.Mode,
                            v.IsDefault,
                            v.Monitored,
                            v.QualityProfileId,
                            Availability.StatusOf(movie, v.MinimumAvailability, v.File is not null, active.Contains(v.Id), today),
                            v.File is null ? null : qualities.GetValueOrDefault(v.File.QualityId)?.Name,
                            v.MinimumAvailability,
                            v.RootFolderId,
                            v.TagIds.Order().ToList(),
                            v.File?.Size,
                            v.File is null || v.Mode != VersionMode.Managed ? null : CutoffMet(row, v.File, profiles[v.QualityProfileId], qualities.GetValueOrDefault(v.File.QualityId), calculator!)))
                        .ToList(),
                    row.AddedAt,
                    row.Ratings?.Tmdb?.Value,
                    row.Ratings?.Imdb?.Value);
            }).ToList();
        }

        /// <summary>
        /// The movie page's verdict (<see cref="CustomFormatScorer.Assess"/>). A profile that scores no custom format
        /// gives every file 0, so the file name is only parsed when a score can change the answer.
        /// Only managed versions get a verdict: a one-shot version is never upgraded .
        /// </summary>
        private static bool CutoffMet(MovieRow movie, FileRow file, QualityProfile profile, QualityDefinition? quality, CustomFormatCalculator calculator)
        {
            if (profile.FormatScores.All(s => s.Score == 0))
            {
                return ProfileRanking.CutoffMet(profile, file.QualityId) && profile.CutoffFormatScore <= 0;
            }

            var matched = calculator.Match(CustomFormatScorer.FileInput(
                file.OriginalReleaseTitle, file.RelativePath, file.Size, file.IndexerFlags, quality, file.Languages, file.Edition, file.ReleaseGroup, movie.Year, movie.OriginalLanguage));
            return CustomFormatScorer.Assess(profile, file.QualityId, matched).CutoffMet;
        }

        private sealed record MovieRow(
            int Id,
            int TmdbId,
            string Title,
            string SortTitle,
            int? Year,
            List<MediaImage> Images,
            MovieRatings? Ratings,
            DateTime AddedAt,
            MovieStatus TmdbStatus,
            DateOnly? InCinemasDate,
            DateOnly? DigitalReleaseDate,
            DateOnly? PhysicalReleaseDate,
            string? OriginalLanguage,
            List<VersionRow> Versions);

        private sealed record VersionRow(
            int Id,
            string Name,
            VersionMode Mode,
            bool IsDefault,
            bool Monitored,
            int QualityProfileId,
            MinimumAvailability MinimumAvailability,
            int RootFolderId,
            List<int> TagIds,
            FileRow? File);

        private sealed record FileRow(
            int QualityId,
            long Size,
            string RelativePath,
            string? OriginalReleaseTitle,
            List<string> Languages,
            IndexerFlags IndexerFlags,
            string? Edition,
            string? ReleaseGroup);
    }
}
