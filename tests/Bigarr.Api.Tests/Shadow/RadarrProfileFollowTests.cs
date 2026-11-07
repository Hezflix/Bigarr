using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Migration;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Shadow;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>
/// A Radarr profile's qualities come with the import (allowed qualities, order, groups, cutoff, upgrades), and in shadow
/// mode every pass of the follow keeps the mapped Bigarr profile in line with it, format scores included.
/// </summary>
public sealed class RadarrProfileFollowTests(ShadowApiFactory factory) : IClassFixture<ShadowApiFactory>
{
    [Fact]
    public async Task The_import_copies_the_qualities_and_cutoff_of_the_radarr_profile()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var library = Directory.CreateTempSubdirectory("bigarr-profile-import-").FullName;
        var root = await AddRootAsync(client, library);
        await using var radarr = await FakeRadarr.StartAsync();
        radarr.Profiles.Add(Films(cutoff: 1002, remuxAllowed: false));
        radarr.Films.Add(new FakeRadarr.Film { Id = 1, Title = "Gattaca", Year = 1997, TmdbId = 782, ProfileId = 6 });

        try
        {
            var started = await client.PostAsJsonAsync("/api/v1/migration/radarr/import", new RadarrImportRequest(
                radarr.Url, FakeRadarr.Key, "1080p", new() { [6] = hd }, new() { [FakeRadarr.RootPath] = root.Id }), Json);
            Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
            var status = await WaitUntilAsync(async () =>
            {
                var s = await client.GetFromJsonAsync<RadarrImportStatusResponse>("/api/v1/migration/radarr/status", Json);
                return s!.State is RadarrImportState.Completed or RadarrImportState.Failed ? s : null;
            }, "the import");
            Assert.Equal(RadarrImportState.Completed, status.State);
            Assert.Equal(1, status.ProfileQualitiesCopied);

            var profile = await ProfileAsync(client, hd);
            Assert.Equal(1002, profile.Cutoff);
            Assert.Equal([4, 1001, 6, 9, 1002, 7], Allowed(profile));
            Assert.Equal([15, 3], profile.Items.Single(i => i.GroupId == 1002).QualityIds);
            Assert.False(profile.Items.Single(i => i.QualityIds.SequenceEqual([30])).Allowed);
        }
        finally
        {
            TryDelete(library);
        }
    }

    [Fact]
    public async Task Each_pass_of_the_follow_keeps_the_bigarr_profile_in_line_with_radarrs()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var library = Directory.CreateTempSubdirectory("bigarr-profile-follow-").FullName;
        var root = await AddRootAsync(client, library);
        var format = await client.PostAsJsonAsync("/api/v1/customformats", new
        {
            name = "Banned group",
            includeWhenRenaming = false,
            specifications = new object[] { new { name = "Banned group", implementation = "ReleaseGroupSpecification", negate = false, required = true, fields = new { value = "^BAD$" } } },
        }, Json);
        Assert.Equal(HttpStatusCode.Created, format.StatusCode);
        var formatId = (await format.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        await using var radarr = await FakeRadarr.StartAsync();
        radarr.Profiles.Add(Films(cutoff: 1002, remuxAllowed: false));
        radarr.Films.Add(new FakeRadarr.Film { Id = 1, Title = "Gattaca", Year = 1997, TmdbId = 782, ProfileId = 6 });

        try
        {
            var created = await client.PostAsJsonAsync("/api/v1/shadow/radarrs", new FollowedRadarrRequest(
                "Radarr profiles", radarr.Url, FakeRadarr.Key, "1080p", new() { [6] = hd }, new() { [FakeRadarr.RootPath] = root.Id }), Json);
            var follow = (await created.Content.ReadFromJsonAsync<FollowedRadarrResponse>(Json))!;
            await SyncAsync(client, follow.Id);
            Assert.Equal(1002, (await ProfileAsync(client, hd)).Cutoff);

            // In Radarr the profile moves on: cutoff on Bluray, remux allowed, a group scored down.
            radarr.Profiles.Clear();
            radarr.Profiles.Add(Films(cutoff: 7, remuxAllowed: true, bannedScore: -10000));
            var pass = await SyncAsync(client, follow.Id);
            Assert.Contains("1 profile(s) aligned with Radarr", pass.Summary, StringComparison.Ordinal);

            var profile = await ProfileAsync(client, hd);
            Assert.Equal(7, profile.Cutoff);
            Assert.True(profile.Items.Single(i => i.QualityIds.SequenceEqual([30])).Allowed);
            Assert.Equal(-10000, profile.FormatScores.Single(s => s.CustomFormatId == formatId).Score);
            Assert.Equal(-500, profile.MinFormatScore);

            // Nothing moved since: nothing to align.
            Assert.DoesNotContain("aligned", (await SyncAsync(client, follow.Id)).Summary, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(library);
        }
    }

    private static int[] Allowed(QualityProfileResponse profile) => [.. profile.Items.Where(i => i.Allowed).Select(i => i.GroupId ?? i.QualityIds[0])];

    private static async Task<QualityProfileResponse> ProfileAsync(HttpClient client, int id) =>
        (await client.GetFromJsonAsync<QualityProfileResponse>($"/api/v1/qualityprofiles/{id}", Json))!;

    private static async Task<RadarrFollowPass> SyncAsync(HttpClient client, int id)
    {
        var response = await client.PostAsync($"/api/v1/shadow/radarrs/{id}/sync", null);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<RadarrFollowPass>(body, Json)!;
    }

    /// <summary>Radarr's usual HD profile, in its API shape: lowest first, WEB-DL and WEBRip grouped.</summary>
    private static object Films(int cutoff, bool remuxAllowed, int bannedScore = 0) => new
    {
        id = 6,
        name = "Films",
        upgradeAllowed = true,
        cutoff,
        items = new object[]
        {
            Q(4, "HDTV-720p", true),
            G(1001, "WEB 720p", true, Q(14, "WEBRip-720p", true), Q(5, "WEBDL-720p", true)),
            Q(6, "Bluray-720p", true),
            Q(9, "HDTV-1080p", true),
            G(1002, "WEB 1080p", true, Q(15, "WEBRip-1080p", true), Q(3, "WEBDL-1080p", true)),
            Q(7, "Bluray-1080p", true),
            Q(30, "Remux-1080p", remuxAllowed),
        },
        formatItems = new object[] { new { format = 1, name = "Banned group", score = bannedScore } },
        minFormatScore = bannedScore == 0 ? 0 : -500,
        cutoffFormatScore = 0,
        minUpgradeFormatScore = 1,
    };

    private static object Q(int id, string name, bool allowed) => new { quality = new { id, name }, items = Array.Empty<object>(), allowed };

    private static object G(int id, string name, bool allowed, params object[] items) => new { id, name, items, allowed };
}
