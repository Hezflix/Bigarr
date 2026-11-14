using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Bigarr.Api.Tests.Hosting;

namespace Bigarr.Api.Tests.Auth;

/// <summary>
/// plex.tv's sign-in PINs and accounts, with invented people: <c>POST /api/v2/pins</c> creates a PIN,
/// <c>GET /api/v2/pins/{id}</c> carries a token once the test approves it, <c>GET /api/v2/user</c> says whose token it is.
/// </summary>
public sealed class FakePlexTvSignIn : IAsyncDisposable
{
    public const string ClientIdentifier = "bigarr";

    private readonly FakeHttpServer _http;
    private readonly ConcurrentDictionary<long, Pin> _pins = new();
    private readonly ConcurrentDictionary<string, Account> _tokens = new(StringComparer.Ordinal);
    private long _nextPin = 4000;

    private FakePlexTvSignIn(FakeHttpServer http)
    {
        _http = http;
        http.Responder = Respond;
    }

    /// <summary>An invented plex.tv account.</summary>
    public sealed record Account(long Id, string Username, string Email, string Thumb);

    public static readonly Account Owner = new(1001, "demo-owner", "owner@example.invalid", "https://plex.tv/users/demo-owner/avatar");

    public static readonly Account Stranger = new(2002, "demo-stranger", "stranger@example.invalid", "https://plex.tv/users/demo-stranger/avatar");

    private sealed record Pin(long Id, string Code, string? Token);

    public string Url => _http.Url;

    /// <summary>Answer HTTP 503 to everything.</summary>
    public bool Down { get; set; }

    /// <summary>plex.tv refuses the token it gave (<c>/api/v2/user</c> answers 401).</summary>
    public bool RefuseTokens { get; set; }

    public IEnumerable<FakeHttpServer.Received> Requests => _http.Requests;

    public long LastPinId => Interlocked.Read(ref _nextPin);

    public static async Task<FakePlexTvSignIn> StartAsync() => new(await FakeHttpServer.StartAsync());

    /// <summary>The person signs in on plex.tv's auth page as <paramref name="account"/>.</summary>
    public void Approve(long pinId, Account account)
    {
        var token = "fake-token-" + Guid.NewGuid().ToString("N");
        _tokens[token] = account;
        _pins[pinId] = _pins[pinId] with { Token = token };
    }

    /// <summary>plex.tv forgets the PIN, as it does once it expires.</summary>
    public void Expire(long pinId) => _pins.TryRemove(pinId, out _);

    private FakeHttpServer.Answer? Respond(FakeHttpServer.Received request)
    {
        if (Down)
        {
            return new FakeHttpServer.Answer(503);
        }

        if (request.Path.StartsWith("/api/v2/pins", StringComparison.Ordinal)
            && (!request.Headers.TryGetValue("X-Plex-Client-Identifier", out var client) || client != ClientIdentifier))
        {
            return new FakeHttpServer.Answer(400, """{"errors":[{"code":1000,"message":"X-Plex-Client-Identifier is missing"}]}""");
        }

        if (request.Method == "POST" && request.Path == "/api/v2/pins")
        {
            var id = Interlocked.Increment(ref _nextPin);
            var pin = new Pin(id, "code" + id.ToString(CultureInfo.InvariantCulture), null);
            _pins[id] = pin;
            return new FakeHttpServer.Answer(201, PinJson(pin));
        }

        if (request.Method == "GET" && request.Path.StartsWith("/api/v2/pins/", StringComparison.Ordinal))
        {
            return long.TryParse(request.Path["/api/v2/pins/".Length..], CultureInfo.InvariantCulture, out var id) && _pins.TryGetValue(id, out var pin)
                ? new FakeHttpServer.Answer(200, PinJson(pin))
                : new FakeHttpServer.Answer(404, """{"errors":[{"code":1020,"message":"Code not found or expired"}]}""");
        }

        if (request.Method == "GET" && request.Path == "/api/v2/user")
        {
            if (RefuseTokens || !request.Headers.TryGetValue("X-Plex-Token", out var token) || !_tokens.TryGetValue(token, out var account))
            {
                return new FakeHttpServer.Answer(401, """{"errors":[{"code":1001,"message":"User could not be authenticated"}]}""");
            }

            return new FakeHttpServer.Answer(200, JsonSerializer.Serialize(new
            {
                id = account.Id,
                uuid = "uuid-" + account.Id.ToString(CultureInfo.InvariantCulture),
                username = account.Username,
                title = account.Username,
                email = account.Email,
                thumb = account.Thumb,
                authToken = token,
            }));
        }

        return null;
    }

    private static string PinJson(Pin pin) => JsonSerializer.Serialize(new
    {
        id = pin.Id,
        code = pin.Code,
        product = "Bigarr",
        trusted = false,
        clientIdentifier = ClientIdentifier,
        expiresIn = 1800,
        createdAt = "2026-11-14T10:00:00Z",
        expiresAt = "2099-01-01T00:00:00Z",
        authToken = pin.Token,
    });

    public async ValueTask DisposeAsync() => await _http.DisposeAsync();
}
