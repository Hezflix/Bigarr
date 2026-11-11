using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Migration;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Shadow;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Migration;

/// <summary>
/// Sonarr's custom formats through the import and the follow: reused when Bigarr has the same name and definition,
/// imported as "name (Sonarr)" when the name is taken by another definition, created otherwise; scores and thresholds on
/// the Series profiles only; the follow keeps what it brought in in line (renames included), never rewrites a reused or
/// film-scored format (a "(Sonarr)" copy takes the change) and never touches a Films profile.
/// </summary>
public sealed class SonarrFormatImportTests(SonarrShadowApiFactory factory) : IClassFixture<SonarrShadowApiFactory>
{
    [Fact]
    public async Task Formats_are_reused_renamed_or_created_scored_on_series_profiles_and_kept_in_sync()
    {
        var client = await ClientAsync(factory);
        var tv = await SonarrImportTests.SeriesRootAsync(client, "bigarr-formats-tv-");
        await using var sonarr = await FakeSonarr.StartAsync();
        sonarr.Series.Add(FakeSonarr.BreakingBad());

        // Bigarr already has x265 (HD) exactly as Sonarr has it, and a Repack/Proper of its own that the film profile scores.
        int filmProfile;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var x265 = SonarrCustomFormats.Translate(JsonDocument.Parse(SonarrFormatSamples.X265()).RootElement).Definition;
            db.CustomFormats.Add(new CustomFormat { Name = "x265 (HD)", SpecificationsJson = CustomFormatJson.SerializeSpecifications(x265.Specifications) });
            var repack = new CustomFormat { Name = "Repack/Proper", SpecificationsJson = """[{"name":"Repack","implementation":"ReleaseTitleSpecification","negate":false,"required":false,"fields":{"value":"\\bREPACK\\b"}}]""" };
            db.CustomFormats.Add(repack);
            var film = await db.QualityProfiles.Include(p => p.FormatScores).SingleAsync(p => p.Name == Features.Quality.QualitySeeder.HdProfileName);
            await db.SaveChangesAsync();
            film.FormatScores.Add(new ProfileFormatScore { CustomFormatId = repack.Id, Score = 50 });
            await db.SaveChangesAsync();
            filmProfile = film.Id;
        }

        sonarr.CustomFormatsJson = SonarrFormatSamples.All(SonarrFormatSamples.X265(), SonarrFormatSamples.Repack(), SonarrFormatSamples.Internal(), SonarrFormatSamples.Odd());
        sonarr.Profiles.Clear();
        sonarr.Profiles.Add(new
        {
            id = 1,
            name = "HD-1080p",
            minFormatScore = 0,
            cutoffFormatScore = 500,
            minUpgradeFormatScore = 10,
            formatItems = new object[]
            {
                new { format = 1, name = "x265 (HD)", score = -1000 },
                new { format = 2, name = "Repack/Proper", score = 5 },
                new { format = 3, name = "Internal", score = 100 },
                new { format = 6, name = "Odd", score = 0 },
            },
        });

        // Preview: what would happen to each format.
        var preview = (await (await client.PostAsJsonAsync("/api/v1/migration/sonarr/preview", new SonarrImportPreviewRequest(sonarr.Url, FakeSonarr.Key), Json)).Content.ReadFromJsonAsync<SonarrImportPreviewResponse>(Json))!;
        var outcomes = preview.CustomFormats!.ToDictionary(f => f.SonarrName);
        Assert.Equal("reused", outcomes["x265 (HD)"].Outcome);
        Assert.Equal(("renamed", "Repack/Proper (Sonarr)"), (outcomes["Repack/Proper"].Outcome, outcomes["Repack/Proper"].BigarrName));
        Assert.Equal("new", outcomes["Internal"].Outcome);
        Assert.Equal(3, outcomes["Odd"].Notes.Count);

        var report = await SonarrImportTests.ImportAsync(client, new SonarrImportRequest(
            sonarr.Url, FakeSonarr.Key, "1080p", new() { [1] = SonarrImportService.CreateProfile }, new() { [FakeSonarr.RootPath] = tv.Id }, Follow: true, ImportProfileQualities: false));
        Assert.Equal((2, 1, 1), (report.CustomFormatsAdded, report.CustomFormatsReused, report.CustomFormatsRenamed));
        Assert.Contains(report.Issues, i => i.Title == "Odd" && i.Reason.Contains("Subtitles", StringComparison.Ordinal));

        int seriesProfile;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var formats = await db.CustomFormats.AsNoTracking().ToDictionaryAsync(f => f.Name);
            Assert.Equal(["Internal", "Odd", "Repack/Proper", "Repack/Proper (Sonarr)", "x265 (HD)"], formats.Keys.Order(StringComparer.Ordinal));
            Assert.NotNull(formats["Odd"].Note);
            Assert.Null(formats["Internal"].Note);
            var internalSpecs = CustomFormatJson.ParseSpecifications(formats["Internal"].SpecificationsJson, "Internal");
            Assert.Contains(internalSpecs, s => s.Fields["value"].GetInt32() == (int)IndexerFlags.Internal);

            var created = await db.QualityProfiles.AsNoTracking().Include(p => p.FormatScores).SingleAsync(p => p.Name == "HD-1080p");
            seriesProfile = created.Id;
            Assert.Equal(ProfileKind.Series, created.Kind);
            var scores = created.FormatScores.ToDictionary(s => s.CustomFormatId, s => s.Score);
            Assert.Equal(-1000, scores[formats["x265 (HD)"].Id]);
            Assert.Equal(5, scores[formats["Repack/Proper (Sonarr)"].Id]);
            Assert.Equal(100, scores[formats["Internal"].Id]);
            Assert.False(scores.ContainsKey(formats["Repack/Proper"].Id));
            Assert.Equal((500, 10), (created.CutoffFormatScore, created.MinUpgradeFormatScore));

            // The film profile keeps its own scores: Bigarr's Repack/Proper only.
            var film = await db.QualityProfiles.AsNoTracking().Include(p => p.FormatScores).SingleAsync(p => p.Id == filmProfile);
            Assert.Equal([(formats["Repack/Proper"].Id, 50)], film.FormatScores.Select(s => (s.CustomFormatId, s.Score)));

            var follow = await db.FollowedSonarrs.AsNoTracking().SingleAsync();
            Assert.Equal(4, follow.FormatMap.Count);
            Assert.DoesNotContain(formats["x265 (HD)"].Id, follow.OwnedFormats);
            Assert.Contains(formats["Internal"].Id, follow.OwnedFormats);
            Assert.Contains(formats["Repack/Proper (Sonarr)"].Id, follow.OwnedFormats);

            // A film profile now scores Odd, a format this Sonarr owns.
            var filmTracked = await db.QualityProfiles.Include(p => p.FormatScores).SingleAsync(p => p.Id == filmProfile);
            filmTracked.FormatScores.Add(new ProfileFormatScore { CustomFormatId = formats["Odd"].Id, Score = 7 });
            await db.SaveChangesAsync();
        }

        // In Sonarr: Internal renamed and redefined; x265 (HD) redefined; Odd redefined; the Repack score raised.
        sonarr.CustomFormatsJson = SonarrFormatSamples.All(
            SonarrFormatSamples.Format(1, "x265 (HD)", SonarrFormatSamples.Spec("x265", "ReleaseTitleSpecification", "\"x265\"", required: true)),
            SonarrFormatSamples.Repack(),
            SonarrFormatSamples.Format(3, "Internal Groups", SonarrFormatSamples.Spec("Scene", "IndexerFlagSpecification", "16")),
            SonarrFormatSamples.Format(6, "Odd", SonarrFormatSamples.Spec("Remux", "SourceSpecification", "6")));
        sonarr.Profiles.Clear();
        sonarr.Profiles.Add(new
        {
            id = 1,
            name = "HD-1080p",
            minFormatScore = 0,
            cutoffFormatScore = 500,
            minUpgradeFormatScore = 10,
            formatItems = new object[]
            {
                new { format = 1, name = "x265 (HD)", score = -1000 },
                new { format = 2, name = "Repack/Proper", score = 15 },
                new { format = 3, name = "Internal Groups", score = 100 },
                new { format = 6, name = "Odd", score = 3 },
            },
        });

        var followId = (await client.GetFromJsonAsync<List<FollowedSonarrResponse>>("/api/v1/shadow/sonarrs", Json))!.Single().Id;
        var pass = await client.PostAsync($"/api/v1/shadow/sonarrs/{followId}/sync", null);
        Assert.True(pass.StatusCode == HttpStatusCode.OK, await pass.Content.ReadAsStringAsync());
        Assert.Contains("custom format(s) taken from Sonarr", (await pass.Content.ReadFromJsonAsync<SonarrFollowPass>(Json))!.Summary, StringComparison.Ordinal);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var formats = await db.CustomFormats.AsNoTracking().ToDictionaryAsync(f => f.Name);

            // Owned and only used by series: rewritten in place, renamed with Sonarr.
            Assert.False(formats.ContainsKey("Internal"));
            var renamed = formats["Internal Groups"];
            Assert.Equal([(int)IndexerFlags.Scene], CustomFormatJson.ParseSpecifications(renamed.SpecificationsJson, renamed.Name).Select(s => s.Fields["value"].GetInt32()));

            // Reused (shared): Bigarr's stays, Sonarr's change is a copy. Owned but scored by a film profile: a copy too.
            Assert.Equal(2, CustomFormatJson.ParseSpecifications(formats["x265 (HD)"].SpecificationsJson, "x265").Count);
            Assert.True(formats.ContainsKey("x265 (HD) (Sonarr)"));
            Assert.Equal(3, CustomFormatJson.ParseSpecifications(formats["Odd"].SpecificationsJson, "Odd").Count);
            Assert.True(formats.ContainsKey("Odd (Sonarr)"));

            var series = (await db.QualityProfiles.AsNoTracking().Include(p => p.FormatScores).SingleAsync(p => p.Id == seriesProfile)).FormatScores.ToDictionary(s => s.CustomFormatId, s => s.Score);
            Assert.Equal(-1000, series[formats["x265 (HD) (Sonarr)"].Id]);
            Assert.Equal(15, series[formats["Repack/Proper (Sonarr)"].Id]);
            Assert.Equal(100, series[renamed.Id]);
            Assert.Equal(3, series[formats["Odd (Sonarr)"].Id]);
            Assert.False(series.ContainsKey(formats["x265 (HD)"].Id));

            // The film profile is untouched.
            var film = (await db.QualityProfiles.AsNoTracking().Include(p => p.FormatScores).SingleAsync(p => p.Id == filmProfile)).FormatScores.ToDictionary(s => s.CustomFormatId, s => s.Score);
            Assert.Equal(new Dictionary<int, int> { [formats["Repack/Proper"].Id] = 50, [formats["Odd"].Id] = 7 }, film);
        }

        Assert.All(sonarr.Requests, r => Assert.Equal("GET", r.Method));
    }
}
