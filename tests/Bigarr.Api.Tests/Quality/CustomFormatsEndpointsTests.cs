using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bigarr.Api.Features.Indexers;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Tests.Quality;

/// <summary>
/// Guide files imported unchanged, scored on a profile, and the scores visible where releases are judged: the M3
/// acceptance path. Each test boots its own application: imports and renames must not leak between them.
/// </summary>
public sealed class CustomFormatsEndpointsTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = QualityTestClient.Json;

    private readonly BigarrApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Guide_files_import_unchanged_re_import_updates_and_scores_land_on_the_profile()
    {
        var client = await QualityTestClient.LoginAsync(factory);
        var hd = await QualityTestClient.ProfileAsync(client, QualitySeeder.HdProfileName);
        var uhd = await QualityTestClient.ProfileAsync(client, QualitySeeder.UhdProfileName);

        var imported = await QualityTestClient.ImportAsync(client, ["remux-tier-01", "repack-proper", "lq"], hd.Id);
        Assert.Equal(3, imported.Added.Count);
        Assert.Empty(imported.Updated);
        Assert.Equal(3, imported.ScoresApplied.Count);
        Assert.Contains("Remux Tier 01 → 1080p: 1950", imported.ScoresApplied);

        var remux = imported.Added.Single(f => f.Name == "Remux Tier 01");
        Assert.Equal("3a3ff47579026e76d6504ebea39390de", remux.TrashId);
        Assert.Equal(1950, remux.TrashScores!["default"]);
        Assert.Equal(12, remux.Specifications.Count);
        Assert.Equal("QualityModifierSpecification", remux.Specifications[0].Implementation);
        Assert.Equal(5, remux.Specifications[0].Fields["value"].GetInt32());
        Assert.Equal((hd.Id, 1950), (Assert.Single(remux.Scores).QualityProfileId, remux.Scores[0].Score));

        var profile = (await client.GetFromJsonAsync<QualityProfileResponse>($"/api/v1/qualityprofiles/{hd.Id}", Json))!;
        Assert.Equal(new Dictionary<string, int> { ["LQ"] = -10000, ["Remux Tier 01"] = 1950, ["Repack/Proper"] = 5 }, profile.FormatScores.ToDictionary(s => s.CustomFormat, s => s.Score));

        // Same guide id: an update, not a duplicate, even when renamed; the second profile gets the score this time.
        var renamedJson = TrashFixtures_Json("remux-tier-01").Replace("\"name\": \"Remux Tier 01\"", "\"name\": \"Remux Tier 01 (2026)\"");
        var again = await client.PostAsync($"/api/v1/customformats/import?profileIds={uhd.Id}", new StringContent(renamedJson, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var second = (await again.Content.ReadFromJsonAsync<CustomFormatImportResponse>(Json))!;
        Assert.Empty(second.Added);
        var updated = Assert.Single(second.Updated);
        Assert.Equal(remux.Id, updated.Id);
        Assert.Equal("Remux Tier 01 (2026)", updated.Name);
        Assert.Equal(2, updated.Scores.Count);
        Assert.Equal(3, (await client.GetFromJsonAsync<List<CustomFormatResponse>>("/api/v1/customformats", Json))!.Count);

        // The guide has no default score for MULTi: imported, but profiles untouched and the response says so.
        var multi = await QualityTestClient.ImportAsync(client, ["multi"], hd.Id);
        Assert.Single(multi.Added);
        Assert.Contains(multi.ScoresApplied, s => s.StartsWith("MULTi: no 'default' score"));

        var badJson = await client.PostAsync("/api/v1/customformats/import", new StringContent("{ \"name\": \"x\", \"specifications\": [] }", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, badJson.StatusCode);
        Assert.Contains("would match everything", await badJson.Content.ReadAsStringAsync());

        var badProfile = await client.PostAsync("/api/v1/customformats/import?profileIds=424242", new StringContent(TrashFixtures_Json("3d"), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, badProfile.StatusCode);
    }

    [Fact]
    public async Task Evaluate_shows_the_formats_a_title_matches_and_the_score_per_profile()
    {
        var client = await QualityTestClient.LoginAsync(factory);
        var hd = await QualityTestClient.ProfileAsync(client, QualitySeeder.HdProfileName);
        var uhd = await QualityTestClient.ProfileAsync(client, QualitySeeder.UhdProfileName);
        await QualityTestClient.ImportAsync(client, ["remux-tier-01", "lq", "language-not-english"], hd.Id);

        // No language in the title and no film: the language is unknown, so the guide's "Not English" applies, as in Radarr.
        var unknownLanguage = (await client.GetFromJsonAsync<CustomFormatEvaluationResponse>("/api/v1/customformats/evaluate?title=The.Matrix.1999.1080p.BluRay.REMUX.AVC.DTS-HD.MA.5.1-FraMeSToR", Json))!;
        Assert.Equal(["Language: Not English", "Remux Tier 01"], unknownLanguage.Formats.Select(f => f.Name).OrderBy(n => n));
        Assert.Empty(unknownLanguage.Languages);

        var remux = (await client.GetFromJsonAsync<CustomFormatEvaluationResponse>("/api/v1/customformats/evaluate?title=The.Matrix.1999.1080p.BluRay.REMUX.AVC.DTS-HD.MA.5.1-FraMeSToR&originalLanguage=en", Json))!;
        Assert.Equal(["English"], remux.Languages);
        Assert.Equal("The Matrix", remux.ParsedTitle);
        Assert.Equal(1999, remux.Year);
        Assert.Equal("Remux-1080p", remux.Quality);
        Assert.Equal("FraMeSToR", remux.ReleaseGroup);
        Assert.Equal(["Remux Tier 01"], remux.Formats.Select(f => f.Name));
        Assert.Equal(1950, remux.Scores.Single(s => s.QualityProfileId == hd.Id).Score);
        Assert.Equal(0, remux.Scores.Single(s => s.QualityProfileId == uhd.Id).Score);

        var lq = (await client.GetFromJsonAsync<CustomFormatEvaluationResponse>("/api/v1/customformats/evaluate?title=The.Matrix.1999.1080p.BluRay.x264-YIFY&originalLanguage=en", Json))!;
        Assert.Equal(["LQ"], lq.Formats.Select(f => f.Name));
        Assert.Equal(-10000, lq.Scores.Single(s => s.QualityProfileId == hd.Id).Score);

        // No language in the title: the film's original language decides, taken from the library when a movie is given.
        var rootId = await QualityTestClient.RootFolderAsync(client);
        var amelie = await QualityTestClient.AddMovieAsync(client, 194, hd.Id, rootId);
        try
        {
            var french = (await client.GetFromJsonAsync<CustomFormatEvaluationResponse>($"/api/v1/customformats/evaluate?title=Amelie.2001.1080p.BluRay.x264-GRP&movieId={amelie.Id}", Json))!;
            Assert.Equal(["French"], french.Languages);
            Assert.Equal(["Language: Not English"], french.Formats.Select(f => f.Name));
            var asEnglish = (await client.GetFromJsonAsync<CustomFormatEvaluationResponse>("/api/v1/customformats/evaluate?title=Amelie.2001.1080p.BluRay.x264-GRP&originalLanguage=en", Json))!;
            Assert.Equal(["English"], asEnglish.Languages);
            Assert.Empty(asEnglish.Formats);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/movies/{amelie.Id}");
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/customformats/evaluate?title=")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/customformats/evaluate?title=x.2000.1080p&movieId=424242")).StatusCode);
    }

    [Fact]
    public async Task Hand_made_formats_are_validated_updated_and_deleted_with_their_scores_and_constraints()
    {
        var client = await QualityTestClient.LoginAsync(factory);
        var hd = await QualityTestClient.ProfileAsync(client, QualitySeeder.HdProfileName);

        // The create endpoint takes fields as an object; Radarr's array shape goes through the import endpoint.
        var body = """
        { "name": "SPARKS releases", "includeWhenRenaming": false, "specifications": [
            { "name": "SPARKS", "implementation": "ReleaseGroupSpecification", "negate": false, "required": true, "fields": { "value": "^(SPARKS)$" } }
        ] }
        """;
        var created = await client.PostAsync("/api/v1/customformats", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var sparks = (await created.Content.ReadFromJsonAsync<CustomFormatResponse>(Json))!;
        Assert.Equal("^(SPARKS)$", Assert.Single(sparks.Specifications).Fields["value"].GetString());
        Assert.Null(sparks.TrashId);

        var duplicate = await client.PostAsJsonAsync("/api/v1/customformats", new CustomFormatRequest("sparks releases", false, sparks.Specifications), Json);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var badRegex = await client.PostAsJsonAsync("/api/v1/customformats", new CustomFormatRequest("bad", false,
            [new CustomFormatSpecificationDto("x", "ReleaseTitleSpecification", false, false, new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("(") })]), Json);
        Assert.Equal(HttpStatusCode.BadRequest, badRegex.StatusCode);
        Assert.Contains("invalid regular expression", await badRegex.Content.ReadAsStringAsync());
        var unknown = await client.PostAsJsonAsync("/api/v1/customformats", new CustomFormatRequest("bad", false,
            [new CustomFormatSpecificationDto("x", "NopeSpecification", false, false, [])]), Json);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        var empty = await client.PostAsJsonAsync("/api/v1/customformats", new CustomFormatRequest("bad", false, []), Json);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        // Scores travel with the format: the editor saves both in one call, and the profile shows the result.
        var renamed = await client.PutAsJsonAsync($"/api/v1/customformats/{sparks.Id}", new CustomFormatRequest("SPARKS", true, sparks.Specifications, [new CustomFormatScoreRequest(hd.Id, 100)]), Json);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var scoredFormat = (await renamed.Content.ReadFromJsonAsync<CustomFormatResponse>(Json))!;
        Assert.True(scoredFormat.IncludeWhenRenaming);
        Assert.Equal((hd.Id, 100), (Assert.Single(scoredFormat.Scores).QualityProfileId, scoredFormat.Scores[0].Score));
        Assert.Equal(100, (await client.GetFromJsonAsync<QualityProfileResponse>($"/api/v1/qualityprofiles/{hd.Id}", Json))!.FormatScores.Single(s => s.CustomFormatId == sparks.Id).Score);

        var unknownProfile = await client.PutAsJsonAsync($"/api/v1/customformats/{sparks.Id}", new CustomFormatRequest("SPARKS", true, sparks.Specifications, [new CustomFormatScoreRequest(424242, 1)]), Json);
        Assert.Equal(HttpStatusCode.BadRequest, unknownProfile.StatusCode);

        // No scores in the request: the definition changes, the scores stay.
        var untouched = await client.PutAsJsonAsync($"/api/v1/customformats/{sparks.Id}", new CustomFormatRequest("SPARKS", false, sparks.Specifications), Json);
        Assert.Equal(HttpStatusCode.OK, untouched.StatusCode);
        Assert.Single((await untouched.Content.ReadFromJsonAsync<CustomFormatResponse>(Json))!.Scores);

        // Require it on a version, then delete it: the score and the constraint go away.
        var rootId = await QualityTestClient.RootFolderAsync(client);
        var movie = await QualityTestClient.AddMovieAsync(client, 603, hd.Id, rootId);
        try
        {
            var constrained = await client.PostAsJsonAsync($"/api/v1/movies/{movie.Id}/versions",
                new AddVersionRequest("sparks-only", VersionMode.Managed, hd.Id, Constraints: new ConstraintsDto(null, [sparks.Id], null, null)), Json);
            Assert.Equal(HttpStatusCode.Created, constrained.StatusCode);
            var version = (await constrained.Content.ReadFromJsonAsync<VersionResponse>(Json))!;
            Assert.Equal([sparks.Id], version.Constraints!.RequiredCustomFormatIds!);

            var unknownFormat = await client.PostAsJsonAsync($"/api/v1/movies/{movie.Id}/versions",
                new AddVersionRequest("nope", VersionMode.Managed, hd.Id, Constraints: new ConstraintsDto(null, [424242], null, null)), Json);
            Assert.Equal(HttpStatusCode.BadRequest, unknownFormat.StatusCode);
            Assert.Contains("Unknown custom format", await unknownFormat.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/customformats/{sparks.Id}")).StatusCode);
            Assert.Empty((await client.GetFromJsonAsync<QualityProfileResponse>($"/api/v1/qualityprofiles/{hd.Id}", Json))!.FormatScores);
            var after = (await client.GetFromJsonAsync<MovieResponse>($"/api/v1/movies/{movie.Id}", Json))!;
            Assert.Null(after.Versions.Single(v => v.Name == "sparks-only").Constraints);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/customformats/{sparks.Id}")).StatusCode);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
    }

    [Fact]
    public async Task Scores_drive_the_verdicts_of_the_interactive_search()
    {
        var client = await QualityTestClient.LoginAsync(factory);
        var hd = await QualityTestClient.ProfileAsync(client, QualitySeeder.HdProfileName);
        var imported = await QualityTestClient.ImportAsync(client, ["remux-tier-01", "repack-proper", "lq", "web-tier-01"], hd.Id);
        var remuxTier = imported.Added.Concat(imported.Updated).Single(f => f.Name == "Remux Tier 01");

        var rootId = await QualityTestClient.RootFolderAsync(client);
        var movie = await QualityTestClient.AddMovieAsync(client, 603, hd.Id, rootId);
        var indexer = await client.PostAsJsonAsync("/api/v1/indexers", new IndexerRequest("Mock scores", IndexerImplementation.Mock, MinimumSeeders: 0, RateLimitSeconds: 0), Json);
        var mock = (await indexer.Content.ReadFromJsonAsync<IndexerResponse>(Json))!;
        try
        {
            var result = (await client.GetFromJsonAsync<SearchResponse>($"/api/v1/search?movieId={movie.Id}", Json))!;
            var anyVersion = movie.Versions.Single().Id;
            VersionDecisionResponse Verdict(ReleaseResponse r) => r.Versions.Single(v => v.VersionId == anyVersion);

            var framestor = result.Releases.Single(r => r.Title.Contains("-FraMeSToR"));
            Assert.Equal(["Remux Tier 01"], framestor.CustomFormats);
            Assert.Equal(1950, Verdict(framestor).CustomFormatScore);
            Assert.False(Verdict(framestor).Approved); // 2160p is not in the 1080p profile, the score does not change that

            var rarbg = result.Releases.Single(r => r.Title.Contains("-RARBG"));
            Assert.Contains("LQ", rarbg.CustomFormats);
            Assert.Equal(-10000, Verdict(rarbg).CustomFormatScore);
            Assert.Contains(Verdict(rarbg).Rejections, x => x.Reason == "Custom format score -10000 is below the profile minimum 0");

            var repack = result.Releases.Single(r => r.Title.Contains("REPACK"));
            Assert.Equal(["Repack/Proper"], repack.CustomFormats);
            Assert.Equal(5, Verdict(repack).CustomFormatScore);
            Assert.True(Verdict(repack).Approved);

            var ntb = result.Releases.Single(r => r.Title.EndsWith("-NTb"));
            Assert.Equal(["WEB Tier 01"], ntb.CustomFormats);
            Assert.Equal(1700, Verdict(ntb).CustomFormatScore);
            Assert.True(Verdict(ntb).Approved);

            // Ranking: quality first, then score. Among the approved Bluray-1080p releases the repack (5) precedes the plain ones (0).
            var approvedBluray = result.Releases.Where(r => r.MatchesMovie && r.Quality == "Bluray-1080p" && Verdict(r).Approved).ToList();
            Assert.Contains("REPACK", approvedBluray[0].Title);
            Assert.True(approvedBluray.Count > 1);

            // A required format on a version: the remux without it is refused for that version only.
            var constrained = await client.PostAsJsonAsync($"/api/v1/movies/{movie.Id}/versions",
                new AddVersionRequest("tier-remux", VersionMode.Managed, hd.Id, Constraints: new ConstraintsDto(null, [remuxTier.Id], null, null)), Json);
            Assert.Equal(HttpStatusCode.Created, constrained.StatusCode);
            var tierVersion = (await constrained.Content.ReadFromJsonAsync<VersionResponse>(Json))!;
            var again = (await client.GetFromJsonAsync<SearchResponse>($"/api/v1/search?movieId={movie.Id}&versionId={tierVersion.Id}", Json))!;
            var epsilon = again.Releases.Single(r => r.Title.Contains("-EPSiLON"));
            Assert.Contains(Assert.Single(epsilon.Versions).Rejections, x => x.Reason == "Required custom format Remux Tier 01 not matched");
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/indexers/{mock.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
    }

    [Fact]
    public async Task Schema_lists_the_condition_types_and_a_draft_is_tested_before_it_is_saved()
    {
        var client = await QualityTestClient.LoginAsync(factory);

        var schema = (await client.GetFromJsonAsync<CustomFormatSchemaResponse>("/api/v1/customformats/schema", Json))!;
        Assert.Equal(11, schema.Specifications.Count);
        Assert.Equal([1, 2, 3, 4], schema.Specifications.Single(s => s.Implementation == "ReleaseTypeSpecification").Fields[0].Options!.Select(o => o.Value));
        var source = schema.Specifications.Single(s => s.Implementation == "SourceSpecification");
        Assert.Equal("select", Assert.Single(source.Fields).Type);
        Assert.Contains(source.Fields[0].Options!, o => o is { Value: 9, Label: "Blu-ray" });
        var language = schema.Specifications.Single(s => s.Implementation == "LanguageSpecification");
        Assert.Equal(["value", "exceptLanguage"], language.Fields.Select(f => f.Name));
        Assert.Equal(-2, language.Fields[0].Options![0].Value);
        Assert.Equal("regex", Assert.Single(schema.Specifications.Single(s => s.Implementation == "ReleaseGroupSpecification").Fields).Type);
        Assert.Equal(["min", "max"], schema.Specifications.Single(s => s.Implementation == "SizeSpecification").Fields.Select(f => f.Name));

        List<CustomFormatSpecificationDto> draft =
        [
            new("SPARKS", "ReleaseGroupSpecification", false, true, new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("^SPARKS$") }),
            new("1080p", "ResolutionSpecification", false, false, new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(1080) }),
        ];
        var hit = await client.PostAsJsonAsync("/api/v1/customformats/test", new CustomFormatTestRequest("The.Matrix.1999.1080p.BluRay.x264-SPARKS", draft), Json);
        Assert.Equal(HttpStatusCode.OK, hit.StatusCode);
        var matched = (await hit.Content.ReadFromJsonAsync<CustomFormatTestResponse>(Json))!;
        Assert.True(matched.Matches);
        Assert.Equal("SPARKS", matched.ReleaseGroup);
        Assert.Equal("Bluray-1080p", matched.Quality);
        Assert.All(matched.Specifications, s => Assert.True(s.Satisfied));

        var missResponse = await client.PostAsJsonAsync("/api/v1/customformats/test", new CustomFormatTestRequest("The.Matrix.1999.1080p.BluRay.x264-GRP", draft), Json);
        var miss = (await missResponse.Content.ReadFromJsonAsync<CustomFormatTestResponse>(Json))!;
        Assert.False(miss.Matches);
        Assert.False(miss.Specifications.Single(s => s.Name == "SPARKS").Satisfied);
        Assert.True(miss.Specifications.Single(s => s.Name == "1080p").Satisfied);

        // Testing saves nothing.
        Assert.Empty((await client.GetFromJsonAsync<List<CustomFormatResponse>>("/api/v1/customformats", Json))!);

        var badRegex = await client.PostAsJsonAsync("/api/v1/customformats/test", new CustomFormatTestRequest("x.2000.1080p",
            [new("x", "ReleaseTitleSpecification", false, false, new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("(") })]), Json);
        Assert.Equal(HttpStatusCode.BadRequest, badRegex.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/customformats/test", new CustomFormatTestRequest("", draft), Json)).StatusCode);
    }

    private static string TrashFixtures_Json(string file) => Bigarr.Api.Tests.Decision.TrashFixtures.Json(file);
}
