using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Tests.Auth;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Quality;

public sealed class QualityProfilesEndpointsTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = QualityTestClient.Json;

    [Fact]
    public async Task Seeded_profiles_are_complete_and_the_schema_is_a_blank_template()
    {
        var client = await QualityTestClient.LoginAsync(factory);

        var hd = await QualityTestClient.ProfileAsync(client, QualitySeeder.HdProfileName);
        var full = (await client.GetFromJsonAsync<QualityProfileResponse>($"/api/v1/qualityprofiles/{hd.Id}", Json))!;
        Assert.True(full.IsSystem);
        Assert.Equal(ParserQuality.Bluray1080p.Id, full.Cutoff);
        Assert.Contains(full.Items, i => i.Allowed && i.QualityIds.Contains(ParserQuality.Bluray1080p.Id));
        Assert.Contains(full.Items, i => !i.Allowed && i.QualityIds.Contains(ParserQuality.Bluray2160p.Id));
        Assert.Contains(full.Items, i => i.GroupId is not null && i.GroupName == "WEB 1080p" && i.QualityIds.Count == 2);
        Assert.Empty(full.FormatScores);

        var definitions = (await client.GetFromJsonAsync<List<QualityDefinitionResponse>>("/api/v1/qualitydefinitions", Json))!;
        Assert.Equal(definitions.Count, full.Items.Sum(i => i.QualityIds.Count));

        var schema = (await client.GetFromJsonAsync<QualityProfileResponse>("/api/v1/qualityprofiles/schema", Json))!;
        Assert.Equal(0, schema.Id);
        Assert.Equal(string.Empty, schema.Name);
        Assert.Equal(0, schema.Cutoff);
        Assert.All(schema.Items, i => Assert.False(i.Allowed));
        Assert.Equal(full.Items.Select(i => i.QualityIds), schema.Items.Select(i => i.QualityIds));
    }

    [Fact]
    public async Task Profiles_are_created_from_the_schema_validated_updated_and_deleted()
    {
        var client = await QualityTestClient.LoginAsync(factory);
        var schema = (await client.GetFromJsonAsync<QualityProfileResponse>("/api/v1/qualityprofiles/schema", Json))!;

        List<QualityProfileItemDto> Items(params int[] allowedQualityIds) =>
            schema.Items.Select(i => i with { Allowed = i.QualityIds.Any(allowedQualityIds.Contains) }).ToList();

        var webGroup = schema.Items.Single(i => i.GroupName == "WEB 1080p").GroupId!.Value;
        var request = new QualityProfileRequest("Remux only", Items(ParserQuality.Bluray1080p.Id, ParserQuality.Remux1080p.Id, ParserQuality.WEBDL1080p.Id), ParserQuality.Remux1080p.Id,
            UpgradeAllowed: true, MinFormatScore: 0, CutoffFormatScore: 0, MinUpgradeFormatScore: 50, PreferredLanguage: "fr");

        var created = await client.PostAsJsonAsync("/api/v1/qualityprofiles", request, Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var profile = (await created.Content.ReadFromJsonAsync<QualityProfileResponse>(Json))!;
        Assert.Equal("Remux only", profile.Name);
        Assert.Equal(ParserQuality.Remux1080p.Id, profile.Cutoff);
        Assert.Equal("fr", profile.PreferredLanguage);
        Assert.Equal(50, profile.MinUpgradeFormatScore);
        Assert.False(profile.IsSystem);
        Assert.Equal(0, profile.VersionCount);
        Assert.True(profile.Items.Single(i => i.GroupId == webGroup).Allowed); // the group is allowed as a whole
        Assert.Contains(profile.Id, (await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!.Select(p => p.Id));

        async Task<string> Refused(QualityProfileRequest r, HttpStatusCode expected = HttpStatusCode.BadRequest)
        {
            var response = await client.PostAsJsonAsync("/api/v1/qualityprofiles", r, Json);
            Assert.Equal(expected, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }

        Assert.Contains("already used", await Refused(request, HttpStatusCode.Conflict));
        Assert.Contains("Incomplete items", await Refused(request with { Name = "p", Items = request.Items.Skip(1).ToList() }));
        Assert.Contains("Duplicated quality", await Refused(request with { Name = "p", Items = request.Items.Concat([request.Items[0]]).ToList() }));
        Assert.Contains("Invalid cutoff", await Refused(request with { Name = "p", Cutoff = ParserQuality.Bluray2160p.Id }));
        Assert.Contains("Invalid cutoff", await Refused(request with { Name = "p", Cutoff = 424242 }));
        Assert.Contains("Nothing allowed", await Refused(request with { Name = "p", Items = Items() }));
        Assert.Contains("Invalid preferred language", await Refused(request with { Name = "p", PreferredLanguage = "klingon" }));
        Assert.Contains("Name is required", await Refused(request with { Name = " " }));
        Assert.Contains("Unknown custom format", await Refused(request with { Name = "p", FormatScores = [new FormatScoreRequest(424242, 10)] }));

        // Group cutoff and scores.
        var imported = await QualityTestClient.ImportAsync(client, ["repack-proper"]);
        var repack = Assert.Single(imported.Added.Concat(imported.Updated));
        var updated = await client.PutAsJsonAsync($"/api/v1/qualityprofiles/{profile.Id}",
            request with { Name = "Web first", Cutoff = webGroup, MinFormatScore = -5, FormatScores = [new FormatScoreRequest(repack.Id, 25)] }, Json);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var renamed = (await updated.Content.ReadFromJsonAsync<QualityProfileResponse>(Json))!;
        Assert.Equal("Web first", renamed.Name);
        Assert.Equal(webGroup, renamed.Cutoff);
        Assert.Equal(-5, renamed.MinFormatScore);
        var score = Assert.Single(renamed.FormatScores);
        Assert.Equal((repack.Id, "Repack/Proper", 25), (score.CustomFormatId, score.CustomFormat, score.Score));

        var cleared = await client.PutAsJsonAsync($"/api/v1/qualityprofiles/{profile.Id}", request with { Name = "Web first", FormatScores = [new FormatScoreRequest(repack.Id, 0)] }, Json);
        Assert.Empty((await cleared.Content.ReadFromJsonAsync<QualityProfileResponse>(Json))!.FormatScores);

        // A profile in use cannot go; a system profile never can.
        var rootId = await QualityTestClient.RootFolderAsync(client);
        var movie = await QualityTestClient.AddMovieAsync(client, 603, profile.Id, rootId);
        Assert.Equal(1, (await client.GetFromJsonAsync<QualityProfileResponse>($"/api/v1/qualityprofiles/{profile.Id}", Json))!.VersionCount);
        var inUse = await client.DeleteAsync($"/api/v1/qualityprofiles/{profile.Id}");
        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        Assert.Contains("in use", await inUse.Content.ReadAsStringAsync());

        var hd = await QualityTestClient.ProfileAsync(client, QualitySeeder.HdProfileName);
        var system = await client.DeleteAsync($"/api/v1/qualityprofiles/{hd.Id}");
        Assert.Equal(HttpStatusCode.Conflict, system.StatusCode);
        Assert.Contains("System profile", await system.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/movies/{movie.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/qualityprofiles/{profile.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/qualityprofiles/{profile.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/qualityprofiles/{profile.Id}")).StatusCode);
    }

    [Fact]
    public async Task Quality_definition_sizes_are_editable_within_bounds()
    {
        var client = await QualityTestClient.LoginAsync(factory);
        var id = ParserQuality.WEBDL720p.Id;

        var ok = await client.PutAsJsonAsync($"/api/v1/qualitydefinitions/{id}", new QualityDefinitionUpdateRequest("WEB 720p", 5, 40, 80), Json);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var definition = (await ok.Content.ReadFromJsonAsync<QualityDefinitionResponse>(Json))!;
        Assert.Equal(("WEB 720p", 5d, 40d, 80d), (definition.Title, definition.MinSize, definition.PreferredSize, definition.MaxSize));

        var unlimited = await client.PutAsJsonAsync($"/api/v1/qualitydefinitions/{id}", new QualityDefinitionUpdateRequest(null, 0, null, null), Json);
        Assert.Null((await unlimited.Content.ReadFromJsonAsync<QualityDefinitionResponse>(Json))!.MaxSize);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/qualitydefinitions/{id}", new QualityDefinitionUpdateRequest(null, 50, null, 10), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/qualitydefinitions/{id}", new QualityDefinitionUpdateRequest(null, 0, 90, 80), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/qualitydefinitions/{id}", new QualityDefinitionUpdateRequest(null, -1, null, null), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/v1/qualitydefinitions/424242", new QualityDefinitionUpdateRequest(null, 0, null, null), Json)).StatusCode);
    }
}
