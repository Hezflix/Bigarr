using System.Text.Json;
using Bigarr.Api.Infrastructure.Auth.External;
using Bigarr.Api.Infrastructure.MediaServers;

namespace Bigarr.Api.Tests.Auth;

/// <summary>plex.tv's PIN and account answers, read the way Bigarr needs them.</summary>
public sealed class PlexIdentityParsingTests
{
    [Fact]
    public void A_pin_without_a_token_yet_and_one_with_it()
    {
        var waiting = PlexTvClient.ParsePin(Json("""{"id":4001,"code":"abcd","expiresAt":"2026-11-14T10:30:00Z","authToken":null}"""));
        Assert.NotNull(waiting);
        Assert.Equal(4001, waiting.Id);
        Assert.Equal("abcd", waiting.Code);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 10, 30, 0, TimeSpan.Zero), waiting.ExpiresAt);
        Assert.Null(waiting.AuthToken);

        Assert.Equal("t0ken", PlexTvClient.ParsePin(Json("""{"id":4001,"code":"abcd","authToken":"t0ken"}"""))!.AuthToken);
        Assert.Null(PlexTvClient.ParsePin(Json("""{"code":"abcd"}""")));
    }

    [Fact]
    public void An_account_reads_its_numeric_id_and_falls_back_to_the_title()
    {
        var account = PlexTvClient.ParseAccount(Json("""{"id":1001,"uuid":"u","username":"demo","email":"demo@example.invalid","thumb":"https://plex.tv/a.png"}"""));
        Assert.Equal(new PlexTvAccount("1001", "demo", "demo@example.invalid", "https://plex.tv/a.png"), account);

        Assert.Equal("Demo Home", PlexTvClient.ParseAccount(Json("""{"id":"77","username":"","title":"Demo Home"}"""))!.Username);
        Assert.Null(PlexTvClient.ParseAccount(Json("""{"username":"demo"}""")));
    }

    [Fact]
    public void The_auth_page_names_bigarr_and_carries_the_code()
    {
        var url = PlexIdentityProvider.AuthUrl("ab cd");

        Assert.StartsWith("https://app.plex.tv/auth/#!?clientID=bigarr&code=ab%20cd&", url, StringComparison.Ordinal);
        Assert.Contains("context%5Bdevice%5D%5Bproduct%5D=Bigarr", url, StringComparison.Ordinal);
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
